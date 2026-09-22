using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace WorstHotel
{
    // One authority and one simulation. Clients send intentions, never modified hotel data.
    public sealed class HotelSession : MonoBehaviour
    {
        public const string Protocol = "WHE-premvp-3";
        public HotelSimulation Simulation { get; private set; }
        public HotelState State { get; private set; }
        public NetworkManager Manager { get; private set; }
        public bool IsHost => Manager != null && Manager.IsHost;
        public bool Connected => !NeedsRecovery && Manager != null && Manager.IsConnectedClient && State != null;
        public bool OwnsHotel => ownsHotel && State != null;
        public bool NeedsRecovery { get; private set; }
        public string SaveError { get; private set; } = "";
        public bool Connecting { get; private set; }
        public ulong LocalId => Manager != null ? Manager.LocalClientId : 0;
        public string Status = "";
        public string LastSaved = "";
        public bool SteamMode { get; private set; }
        int operation;
        public Action<string> Feedback;
        float broadcastAt, startedAt, saveAt;
        string password = "";
        readonly Dictionary<ulong, float> poseTimes = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, Queue<float>> rates = new Dictionary<ulong, Queue<float>>();
        readonly Dictionary<ulong, long> sequences = new Dictionary<ulong, long>();
        long sequence;
        bool ownsHotel;
        readonly HashSet<ulong> approvedClients = new HashSet<ulong>();
        public string SavePath => Array.IndexOf(Environment.GetCommandLineArgs(),"-whe-smoke")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-whe-session-tests")>=0
            ? System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"..","..","..","TestResults","smoke-save-"+System.Diagnostics.Process.GetCurrentProcess().Id+".json"))
            : System.IO.Path.Combine(Application.persistentDataPath, "hotel-slot-1.json");

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
                // NGO applies approvals in a later update. Reserve the slot in this callback,
                // otherwise two requests in one incoming batch both see the same free place.
                var occupied = new HashSet<ulong>(Manager.ConnectedClientsIds);
                occupied.UnionWith(approvedClients);
                response.Approved = version && (occupied.Contains(request.ClientNetworkId) || occupied.Count < 2);
                if(response.Approved)approvedClients.Add(request.ClientNetworkId);
                response.CreatePlayerObject = false;
                response.Pending = false;
                response.Reason = version ? "В отеле уже два сотрудника." : "Другая версия игры или неверный пароль комнаты.";
            };
            Manager.OnClientConnectedCallback += OnConnected;
            Manager.OnClientDisconnectCallback += OnDisconnected;
            Manager.OnTransportFailure += OnTransportFailure;
            Manager.OnPreShutdown += OnPreShutdown;
            startedAt = Time.unscaledTime;
            Connecting = true;
        }

        public async void Host(bool load, ushort port, string pass)
        {
            if(Manager != null || Connecting || OwnsHotel) return;
            int current=++operation; Connecting=true; startedAt=Time.unscaledTime;
            try {
                if(NetworkManager.Singleton!=null)await Task.Yield();
                if(current!=operation)return;
                HotelState loaded = load ? HotelSaveStore.Load(SavePath) : null;
                if(load && loaded==null) throw new InvalidOperationException("Сохранение не найдено.");
                Simulation = new HotelSimulation(loaded);
                State = Simulation.State;
                Setup("127.0.0.1", port, pass);
                if(!Manager.StartHost()) throw new InvalidOperationException("Не удалось открыть UDP-порт " + port);
                RegisterMessages();
                if(State.players.Find(p=>p.id==0)==null) Simulation.Join(0);
                Status = "Хост · порт " + port;
                if(!load) HotelSaveStore.StartNew(State,SavePath);
                ownsHotel=true;
                Save();
            } catch(Exception e) { Fail("Не удалось создать отель: " + e.Message); }
        }

        public async void Join(string address, ushort port, string pass)
        {
            if(Manager != null || Connecting || OwnsHotel) return;
            int current=++operation; Connecting=true; startedAt=Time.unscaledTime;
            try {
                if(NetworkManager.Singleton!=null)await Task.Yield();
                if(current!=operation)return;
                if(string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Укажите IP-адрес хоста.");
                Setup(address.Trim(),port,pass);
                if(!Manager.StartClient()) throw new InvalidOperationException("Не удалось начать подключение.");
                RegisterMessages();
                Status = "Подключение к " + address + "…";
            } catch(Exception e) { Fail(e.Message); }
        }

        public async void HostSteam(bool load)
        {
            if(Manager!=null||Connecting||OwnsHotel)return;
            int current=++operation;Connecting=true;SteamMode=true;startedAt=Time.unscaledTime;Status="Создаём тестовое Steam-лобби (AppID 480)…";
            try {
                if(!HotelSteam.InitializeForTest(out string error))throw new Exception(error);
                var lobby=await HotelSteam.CreateLobby();
                if(current!=operation)return;
                if(!lobby.Success)throw new Exception(lobby.Error);
                var loaded=load?HotelSaveStore.Load(SavePath):null;
                if(load&&loaded==null)throw new Exception("Сохранение не найдено.");
                Simulation=new HotelSimulation(loaded);State=Simulation.State;
                if(NetworkManager.Singleton!=null)await Task.Yield();
                if(current!=operation)return;
                Setup("127.0.0.1",7777,"");
                if(!HotelSteam.ConfigureTransport(Manager,true,lobby.HostSteamId,out error))throw new Exception(error);
                if(!Manager.StartHost())throw new Exception("Не удалось запустить Steam-хост.");
                RegisterMessages();if(State.players.Find(p=>p.id==0)==null)Simulation.Join(0);
                if(!HotelSteam.SetLobbyReady(true,out error))throw new Exception(error);
                if(!load)HotelSaveStore.StartNew(State,SavePath);ownsHotel=true;Save();
                Status="STEAM TEST 480 · Лобби "+lobby.LobbyId;
            } catch(Exception e){if(current==operation)Fail(e.Message);}
        }
        public async void JoinSteam(ulong lobbyId)
        {
            if(Manager!=null||Connecting||OwnsHotel)return;
            int current=++operation;Connecting=true;SteamMode=true;startedAt=Time.unscaledTime;Status="Входим в тестовое Steam-лобби…";
            try {
                if(!HotelSteam.InitializeForTest(out string error))throw new Exception(error);
                var lobby=await HotelSteam.JoinLobby(lobbyId);
                if(current!=operation)return;
                if(!lobby.Success)throw new Exception(lobby.Error);
                if(NetworkManager.Singleton!=null)await Task.Yield();
                if(current!=operation)return;
                Setup("127.0.0.1",7777,"");
                if(!HotelSteam.ConfigureTransport(Manager,false,lobby.HostSteamId,out error))throw new Exception(error);
                if(!Manager.StartClient())throw new Exception("Не удалось подключить Steam transport.");
                RegisterMessages();Status="STEAM TEST 480 · Лобби "+lobbyId;
            }catch(Exception e){if(current==operation)Fail(e.Message);}
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
            approvedClients.Remove(id);
            if(IsHost) {
                Simulation.Join(id);
                if(State.players.Find(p=>p.id==id)==null) { if(id!=0)Manager.DisconnectClient(id,"В отеле уже два сотрудника."); return; }
                poseTimes[id]=Time.unscaledTime; broadcastAt=0;
            }
            if(id==LocalId && IsHost) Connecting=false;
            Debug.Log("WHE_CONNECTED id=" + id + " host=" + IsHost);
        }
        void OnDisconnected(ulong id)
        {
            approvedClients.Remove(id);sequences.Remove(id);
            if(IsHost && id!=0) { Simulation.Leave(id); poseTimes.Remove(id); rates.Remove(id); return; }
            if(OwnsHotel)return; // NGO can reset IsHost before callbacks; retain our save obligation.
            if(!IsHost) {
                Status = string.IsNullOrEmpty(Manager?.DisconnectReason) ? "Соединение закрыто. Хост сохраняет отель." : Manager.DisconnectReason;
                Connecting=false; State=null;
            }
        }
        void OnTransportFailure()
        {
            Status="Ошибка сети. Сессия остановлена; прогресс отеля будет сохранён.";
            Connecting=false;
            if(OwnsHotel)NeedsRecovery=true;
        }
        void OnPreShutdown()
        {
            if(!OwnsHotel)return;
            // This hook runs before NGO discards host roles. Ownership stays with the session
            // even if disk I/O fails, so the recovery screen can retry after network shutdown.
            NeedsRecovery=true;Connecting=false;
            bool saved=Save();
            Status=saved?"Сеть отключена. Последний прогресс сохранён. Можно открыть отель снова.":"Сеть отключена. Не удалось сохранить отель — повторите запись перед выходом.";
        }
        void Fail(string reason) { Disconnect(false); Status=reason; Feedback?.Invoke(reason); }

        public bool ResetSteam()
        {
            if(Manager!=null||Connecting||OwnsHotel){Feedback?.Invoke("Сначала завершите текущую сессию.");return false;}
            HotelSteam.Shutdown();Status="Steam-подключение сброшено. Можно повторить вход в лобби.";return true;
        }

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
            if(Connecting && Time.unscaledTime-startedAt>(SteamMode?25:12)) { Fail(SteamMode?"Steam-хост не отвечает. Проверьте Steam у обоих игроков.":"Хост не отвечает. Проверьте IP, пароль и UDP-порт. Для интернета нужен доступный порт или VPN-сеть."); return; }
            if(!IsHost || Simulation==null || NeedsRecovery || Manager.ShutdownInProgress || !Manager.IsListening) return;
            Simulation.Tick(Mathf.Min(Time.unscaledDeltaTime,.1f));
            if(Time.unscaledTime>=broadcastAt) {
                broadcastAt=Time.unscaledTime+.1f;
                string json=JsonUtility.ToJson(State);
                foreach(ulong id in Manager.ConnectedClientsIds) if(id!=0) SendText("world",id,json);
            }
            if(Time.unscaledTime>=saveAt) { saveAt=Time.unscaledTime+30; Save(); }
        }
        public bool Save()
        {
            if(!OwnsHotel) return false;
            try { HotelSaveStore.Save(State,SavePath); LastSaved=DateTime.Now.ToString("HH:mm:ss"); SaveError=""; return true; }
            catch(Exception e) { SaveError=e.Message; Feedback?.Invoke("Ошибка сохранения: "+e.Message); Debug.LogError(e); return false; }
        }
        public bool Disconnect(bool save=true)
        {
            // Public cancel/new-session calls cannot discard an owned hotel, even with save=false.
            if(OwnsHotel && !Save()) return false;
            operation++;
            if(Manager!=null) {
                Manager.OnPreShutdown-=OnPreShutdown;Manager.OnTransportFailure-=OnTransportFailure;
                Manager.OnClientConnectedCallback-=OnConnected;Manager.OnClientDisconnectCallback-=OnDisconnected;
                Manager.Shutdown(); Destroy(Manager.gameObject); Manager=null;
            }
            if(SteamMode)HotelSteam.LeaveLobby();SteamMode=false;
            State=null; Simulation=null; Connecting=false; ownsHotel=false;NeedsRecovery=false;SaveError="";
            poseTimes.Clear(); rates.Clear(); sequences.Clear(); approvedClients.Clear(); sequence=0;
            return true;
        }
        void OnApplicationQuit() { Save(); if(Manager!=null)Manager.Shutdown(); HotelSteam.Shutdown(); }
        void OnDestroy() { if(Manager!=null) Manager.Shutdown(); }
    }
}
