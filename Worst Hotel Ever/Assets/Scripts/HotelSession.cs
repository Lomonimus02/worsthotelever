using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace WorstHotel
{
    // One authority and one simulation. Clients send intentions, never modified hotel data.
    public sealed class HotelSession : MonoBehaviour
    {
        public const string Protocol = "WHE-premvp-1";
        public HotelSimulation Simulation { get; private set; }
        public HotelState State { get; private set; }
        public NetworkManager Manager { get; private set; }
        public bool IsHost => Manager != null && Manager.IsHost;
        public bool Connected => Manager != null && Manager.IsConnectedClient && State != null;
        public bool Connecting { get; private set; }
        public ulong LocalId => Manager != null ? Manager.LocalClientId : 0;
        public string Status = "";
        public string LastSaved = "";
        public Action<string> Feedback;
        float broadcastAt, startedAt, saveAt;
        string password = "";
        readonly Dictionary<ulong, float> poseTimes = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, Queue<float>> rates = new Dictionary<ulong, Queue<float>>();
        readonly Dictionary<ulong, long> sequences = new Dictionary<ulong, long>();
        long sequence;
        public string SavePath => System.IO.Path.Combine(Application.persistentDataPath, "hotel-slot-1.json");

        void Setup(string address, ushort port, string pass)
        {
            password = pass ?? "";
            var go = new GameObject("Network session");
            Manager = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();
            transport.SetConnectionData(address, port, "0.0.0.0");
            transport.ConnectTimeoutMS = 1200;
            transport.MaxConnectAttempts = 5;
            transport.DisconnectTimeoutMS = 6000;
            Manager.NetworkConfig = new NetworkConfig {
                NetworkTransport = transport, EnableSceneManagement = false,
                ConnectionApproval = true, TickRate = 30,
                ConnectionData = Encoding.UTF8.GetBytes(Protocol + "|" + password)
            };
            Manager.ConnectionApprovalCallback = (request, response) => {
                bool version = Encoding.UTF8.GetString(request.Payload) == Protocol + "|" + password;
                response.Approved = version && Manager.ConnectedClientsIds.Count < 2;
                response.CreatePlayerObject = false;
                response.Pending = false;
                response.Reason = version ? "В отеле уже два сотрудника." : "Другая версия игры или неверный пароль комнаты.";
            };
            Manager.OnClientConnectedCallback += OnConnected;
            Manager.OnClientDisconnectCallback += OnDisconnected;
            Manager.OnTransportFailure += () => { Status = "Ошибка сети. Проверьте адрес и доступность порта."; Connecting = false; };
            startedAt = Time.unscaledTime;
            Connecting = true;
        }

        public void Host(bool load, ushort port, string pass)
        {
            if(Manager != null) return;
            try {
                HotelState loaded = load ? HotelSaveStore.Load(SavePath) : null;
                Simulation = new HotelSimulation(loaded);
                State = Simulation.State;
                Setup("127.0.0.1", port, pass);
                if(!Manager.StartHost()) throw new InvalidOperationException("Не удалось открыть UDP-порт " + port);
                RegisterMessages();
                if(State.players.Find(p=>p.id==0)==null) Simulation.Join(0);
                Status = "Хост · порт " + port;
                Save();
            } catch(Exception e) { Fail("Не удалось создать отель: " + e.Message); }
        }

        public void Join(string address, ushort port, string pass)
        {
            if(Manager != null) return;
            try {
                if(string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Укажите IP-адрес хоста.");
                Setup(address.Trim(),port,pass);
                if(!Manager.StartClient()) throw new InvalidOperationException("Не удалось начать подключение.");
                RegisterMessages();
                Status = "Подключение к " + address + "…";
            } catch(Exception e) { Fail(e.Message); }
        }

        void RegisterMessages()
        {
            Manager.CustomMessagingManager.RegisterNamedMessageHandler("world", (sender,reader)=> {
                if(IsHost || sender != NetworkManager.ServerClientId) return;
                try {
                    if(reader.Length > 131072) return;
                    reader.ReadValueSafe(out string json);
                    var value = JsonUtility.FromJson<HotelState>(json);
                    if(value != null && value.version == 1 && value.rooms?.Count == 4) {
                        State = value; Connecting = false; Status = "В сети · сотрудник " + (LocalId+1);
                    }
                } catch(Exception e) { Debug.LogWarning("Rejected state: " + e.Message); }
            });
            Manager.CustomMessagingManager.RegisterNamedMessageHandler("command", (sender,reader)=> {
                if(!IsHost || !Manager.ConnectedClients.ContainsKey(sender) || reader.Length > 2048) return;
                try {
                    reader.ReadValueSafe(out string json);
                    if(!rates.TryGetValue(sender,out var q)) rates[sender] = q = new Queue<float>();
                    while(q.Count>0 && Time.unscaledTime-q.Peek()>1) q.Dequeue();
                    if(q.Count>=90) return;
                    q.Enqueue(Time.unscaledTime);
                    var cmd = JsonUtility.FromJson<HotelCommand>(json);
                    string error = Apply(sender,cmd);
                    if(!string.IsNullOrEmpty(error)) SendText("feedback",sender,error);
                } catch(Exception e) { Debug.LogWarning("Rejected command: " + e.Message); }
            });
            Manager.CustomMessagingManager.RegisterNamedMessageHandler("feedback",(sender,reader)=> {
                if(IsHost || sender!=NetworkManager.ServerClientId || reader.Length>4096) return;
                reader.ReadValueSafe(out string message); Feedback?.Invoke(message);
            });
        }

        void OnConnected(ulong id)
        {
            if(IsHost) { Simulation.Join(id); poseTimes[id]=Time.unscaledTime; broadcastAt=0; }
            if(id==LocalId) Connecting=false;
            Debug.Log("WHE_CONNECTED id=" + id + " host=" + IsHost);
        }
        void OnDisconnected(ulong id)
        {
            if(IsHost && id!=0) { Simulation.Leave(id); poseTimes.Remove(id); rates.Remove(id); return; }
            if(!IsHost) {
                Status = string.IsNullOrEmpty(Manager?.DisconnectReason) ? "Соединение закрыто. Хост сохраняет отель." : Manager.DisconnectReason;
                Connecting=false; State=null;
            }
        }
        void Fail(string reason) { Disconnect(false); Status=reason; Feedback?.Invoke(reason); }

        string Apply(ulong id,HotelCommand cmd)
        {
            if(cmd==null || cmd.action==null || (cmd.target?.Length??0)>128) return "Некорректная команда.";
            if(sequences.TryGetValue(id,out long last) && cmd.sequence<=last) return "";
            sequences[id]=cmd.sequence;
            if(cmd.action=="pose") {
                var p=State.players.Find(x=>x.id==id);
                if(p==null || !Finite(cmd.position.x) || !Finite(cmd.position.y) || !Finite(cmd.position.z) || !Finite(cmd.yaw) || !Finite(cmd.pitch)) return "";
                if(Mathf.Abs(cmd.position.x)>8 || cmd.position.z < -7.5f || cmd.position.z>17 || cmd.position.y < -1 || cmd.position.y>3) return "";
                float elapsed=poseTimes.TryGetValue(id,out float t)?Mathf.Min(Time.unscaledTime-t,.3f):.1f;
                if(Vector3.Distance(p.position,cmd.position)>8*elapsed+.65f) return "";
                // Capsule movement supplies collision. Also reject client poses that cross static hotel walls.
                Vector3 origin=p.position+Vector3.up*.9f, end=cmd.position+Vector3.up*.9f;
                if(Vector3.Distance(origin,end)>.02f && Physics.Linecast(origin,end,out var hit) && hit.collider.GetComponentInParent<HotelTarget>()==null) return "";
                poseTimes[id]=Time.unscaledTime;
            }
            string error=Simulation.Execute(id,cmd);
            if(error=="" && cmd.action!="pose" && cmd.action!="heartbeat" && cmd.action!="cancelwork") broadcastAt=0;
            return error;
        }
        static bool Finite(float f) => !float.IsNaN(f)&&!float.IsInfinity(f);
        public void Send(HotelCommand cmd)
        {
            if(!Connected) return;
            cmd.sequence=++sequence;
            if(IsHost) { var error=Apply(0,cmd); if(!string.IsNullOrEmpty(error)) Feedback?.Invoke(error); }
            else SendText("command",NetworkManager.ServerClientId,JsonUtility.ToJson(cmd));
        }
        void SendText(string name,ulong target,string json)
        {
            using(var writer=new FastBufferWriter(Encoding.Unicode.GetByteCount(json)+16,Allocator.Temp)) {
                writer.WriteValueSafe(json);
                Manager.CustomMessagingManager.SendNamedMessage(name,target,writer,NetworkDelivery.ReliableFragmentedSequenced);
            }
        }
        void Update()
        {
            if(Connecting && Time.unscaledTime-startedAt>12) { Fail("Хост не отвечает. Проверьте IP, пароль и UDP-порт. Для интернета нужен доступный порт или VPN-сеть."); return; }
            if(!IsHost || Simulation==null) return;
            Simulation.Tick(Mathf.Min(Time.unscaledDeltaTime,.1f));
            if(Time.unscaledTime>=broadcastAt) {
                broadcastAt=Time.unscaledTime+.1f;
                string json=JsonUtility.ToJson(State);
                foreach(ulong id in Manager.ConnectedClientsIds) if(id!=0) SendText("world",id,json);
            }
            if(Time.unscaledTime>=saveAt) { saveAt=Time.unscaledTime+30; Save(); }
        }
        public void Save()
        {
            if(!IsHost || State==null) return;
            try { HotelSaveStore.Save(State,SavePath); LastSaved=DateTime.Now.ToString("HH:mm:ss"); }
            catch(Exception e) { Feedback?.Invoke("Ошибка сохранения: "+e.Message); Debug.LogError(e); }
        }
        public void Disconnect(bool save=true)
        {
            if(save) Save();
            if(Manager!=null) { Manager.Shutdown(); Destroy(Manager.gameObject); Manager=null; }
            State=null; Simulation=null; Connecting=false; poseTimes.Clear(); rates.Clear(); sequences.Clear(); sequence=0;
        }
        void OnApplicationQuit() { Save(); }
        void OnDestroy() { if(Manager!=null) Manager.Shutdown(); }
    }
}
