using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorstHotel
{
    public sealed class HotelGame : MonoBehaviour
    {
        public static HotelGame Instance;
        public HotelSession Session; public HotelWorld World; public Camera View;
        public CharacterController Controller; public HotelUI UI;
        public string Panel = "menu", Toast = "", FocusLabel = "", FocusId = "";
        public float Sensitivity = .105f, Fov = 82, Volume = .55f;
        public bool InvertY, Bob, HandMotion=true, AmbientSound=true;
        public HotelAudio Audio { get; private set; }
        public HotelStamina Stamina { get; } = new HotelStamina();
        public string ConfirmedWork => InputActive && workTarget!="" && LocalPlayer?.workTarget==workTarget &&
            HotelPresentation.DangerWorkIntentAllowed(Session.State,Session.LocalId,workTarget) ? workTarget : "";
        public bool Automated { get; private set; }
        public float FPS { get; private set; }
        public bool Playing => Session.Connected;
        public bool InputActive => Playing && Panel=="" && InputFocused;
        bool InputFocused => !applicationPaused && (testInputFocus ?? (Automated || applicationFocused));
        public PlayerState LocalPlayer => Session.State?.players.Find(p=>p.id==Session.LocalId);
        public ItemState Held => Session.State?.items.Find(i=>i.id==LocalPlayer?.held && !i.consumed);
        GameObject player, carry; Transform handAnchor;
        Material sleeveMaterial, gloveMaterial;
        string carryKind="", oldNotice="", workTarget="";
        string inputDangerWorld="";
        int inputDangerSerial=-1;
        bool wasIncapacitated;
        bool applicationFocused = true, applicationPaused;
        bool? testInputFocus;
        float yaw, pitch, vertical, nextPose, nextHeartbeat, toastUntil, stepAt, cameraBob, fpsSmooth=60;
        readonly HotelFeedback feedback = new HotelFeedback();
        HotelState preview;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if(FindFirstObjectByType<HotelGame>()!=null) return;
            foreach(var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) Destroy(c.gameObject);
            foreach(var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) Destroy(l.gameObject);
            new GameObject("Worst Hotel Ever").AddComponent<HotelGame>();
        }
        void Awake()
        {
            applicationFocused=Application.isFocused;
            Instance=this; Application.runInBackground=true; Application.targetFrameRate=90; QualitySettings.vSyncCount=0;
            Application.wantsToQuit+=CanQuit;
            Sensitivity=PlayerPrefs.GetFloat("sensitivity",.105f); Fov=PlayerPrefs.GetFloat("fov",82); Volume=PlayerPrefs.GetFloat("volume",.55f);
            InvertY=PlayerPrefs.GetInt("invert",0)==1; Bob=PlayerPrefs.GetInt("bob",0)==1;
            HandMotion=PlayerPrefs.GetInt("handMotion",1)==1; AmbientSound=PlayerPrefs.GetInt("ambientSound",1)==1;
            Session=gameObject.AddComponent<HotelSession>(); Session.Feedback=Notify;
            World=new GameObject("Hotel world").AddComponent<HotelWorld>(); World.Build();
            preview=new HotelSimulation().State; World.Apply(preview,ulong.MaxValue);
            View=new GameObject("First person camera").AddComponent<Camera>(); View.tag="MainCamera";
            View.nearClipPlane=.06f; View.farClipPlane=85; View.fieldOfView=Fov;
            View.backgroundColor=new Color(.32f,.47f,.53f); View.clearFlags=CameraClearFlags.SolidColor;
            View.gameObject.AddComponent<AudioListener>();
            Audio=gameObject.AddComponent<HotelAudio>();
            UI=gameObject.AddComponent<HotelUI>(); UI.Game=this;
            SetCursor();
            string[] args=Environment.GetCommandLineArgs();
            Automated=args.Contains("-whe-smoke")||args.Contains("-whe-session-tests")||args.Contains("-whe-path-check");
            Session.UseLegacyFixture = Automated && args.Contains("-whe-legacy-fixture");
            Debug.Log("WHE_INPUT keyboard="+(Keyboard.current!=null)+" mouse="+(Mouse.current!=null));
            int portIndex=Array.IndexOf(args,"-whe-port"); ushort port=7777;
            if(portIndex>=0 && portIndex+1<args.Length) ushort.TryParse(args[portIndex+1],out port);
            if(args.Contains("-whe-host")) { Session.Host(args.Contains("-whe-load"),port,""); Panel=""; }
            int clientIndex=Array.IndexOf(args,"-whe-client");
            if(clientIndex>=0 && clientIndex+1<args.Length) { Session.Join(args[clientIndex+1],port,""); Panel=""; }
            if(args.Contains("-whe-smoke")) gameObject.AddComponent<HotelSmokeTest>();
            if(args.Contains("-whe-session-tests")||args.Contains("-whe-path-check")) gameObject.AddComponent<HotelSessionSmokeTest>();
        }
        void CreatePlayer()
        {
            player=new GameObject("Local employee"); player.layer=2;
            player.transform.position=LocalPlayer?.position??HotelLayout.Spawn;
            Controller=player.AddComponent<CharacterController>(); Controller.height=1.75f; Controller.radius=.28f;
            Controller.center=new Vector3(0,.9f,0); Controller.stepOffset=.25f; Controller.skinWidth=.035f;
            View.transform.SetParent(player.transform,false); View.transform.localPosition=Vector3.up*1.65f;
            handAnchor=new GameObject("Carry anchor").transform; handAnchor.SetParent(View.transform,false);
            handAnchor.localPosition=new Vector3(.3f,-.43f,.66f);
            var shader=Resources.Load<Shader>("Hotel/HotelSurface");
            if(shader!=null&&sleeveMaterial==null){sleeveMaterial=new Material(shader);sleeveMaterial.SetColor("_BaseColor",new Color(.42f,.17f,.14f));}
            if(shader!=null&&gloveMaterial==null){gloveMaterial=new Material(shader);gloveMaterial.SetColor("_BaseColor",new Color(.86f,.70f,.45f));}
            // Small stylized sleeves stay below the reticle; no full-body camera coupling.
            foreach(float side in new[]{-1f,1f}) {
                var sleeve=GameObject.CreatePrimitive(PrimitiveType.Capsule);sleeve.name="Employee sleeve";
                sleeve.transform.SetParent(handAnchor,false);sleeve.transform.localPosition=new Vector3(side*.27f,-.14f,-.2f);
                sleeve.transform.localRotation=Quaternion.Euler(66,side*12,0);sleeve.transform.localScale=new Vector3(.13f,.22f,.13f);
                sleeve.GetComponent<Collider>().enabled=false;Destroy(sleeve.GetComponent<Collider>());sleeve.layer=2;
                if(sleeveMaterial!=null)sleeve.GetComponent<Renderer>().sharedMaterial=sleeveMaterial;
                sleeve.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                var glove=GameObject.CreatePrimitive(PrimitiveType.Sphere);glove.name="Work glove";glove.layer=2;
                glove.transform.SetParent(sleeve.transform,false);glove.transform.localPosition=new Vector3(0,.92f,0);
                glove.transform.localScale=new Vector3(1.08f,.5f,1.05f);
                glove.GetComponent<Collider>().enabled=false;Destroy(glove.GetComponent<Collider>());
                if(gloveMaterial!=null)glove.GetComponent<Renderer>().sharedMaterial=gloveMaterial;
                glove.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            yaw=LocalPlayer?.yaw??0; pitch=0; nextPose=0;
            SetCursor();
        }
        void Update()
        {
            fpsSmooth=Mathf.Lerp(fpsSmooth,1f/Mathf.Max(Time.unscaledDeltaTime,.001f),.04f); FPS=fpsSmooth;
            AudioListener.volume=Volume;
            View.fieldOfView=Mathf.Lerp(View.fieldOfView,Fov,Time.unscaledDeltaTime*8);
            // A down/terminal snapshot cancels local intent before feedback can mistake it for success.
            if(Playing && workTarget!="" && !HotelPresentation.DangerWorkIntentAllowed(Session.State,Session.LocalId,workTarget)) StopWork();
            // Observe the received result before release/ESC/heartbeat cleanup can cancel local intent.
            HotelCue cues=Playing?feedback.Observe(Session.State,Session.LocalId):HotelCue.None;
            var keyboard=Keyboard.current;
            bool keyboardAvailable=keyboard!=null && keyboard.enabled;
            if(InputFocused && keyboardAvailable && keyboard.escapeKey.wasPressedThisFrame) {
                OpenPanel(Playing?(Panel==""?"pause":""):"menu");
            }
            if(Playing && InputFocused && keyboardAvailable && keyboard.tabKey.wasPressedThisFrame) {
                string overview=HotelPresentation.DangerTerminal(Session.State)?"summary":"tasks";
                OpenPanel(HotelHudModel.IsJournalPanel(Panel)?"":overview);
            }
            if(!Playing) {
                if(player!=null) {
                    View.transform.SetParent(null);
                    // The camera survives a session, so its old hands/carry must not survive with it.
                    if(handAnchor!=null)Destroy(handAnchor.gameObject);handAnchor=null;
                    Destroy(player);player=null;Controller=null;carry=null;carryKind="";
                }
                if(Session.NeedsRecovery)Panel="recovery";
                else if(!Session.Connecting && Panel!="menu" && Panel!="connect" && Panel!="new" && Panel!="settings" && Panel!="steam") Panel="menu";
                feedback.Reset();Audio.Silence();workTarget="";oldNotice="";
                inputDangerWorld="";inputDangerSerial=-1;wasIncapacitated=false;
                Stamina.Reset();
                View.transform.position=new Vector3(3.7f+Mathf.Sin(Time.unscaledTime*.08f)*.35f,2.25f,-5.8f);
                View.transform.LookAt(new Vector3(-.4f,1.2f,1.7f)); SetCursor(); return;
            }
            if(player==null) CreatePlayer();
            Stamina.ObserveSession(Session.State.worldId,Session.State.day);
            bool canAct=HotelDangerRules.CanAct(Session.State,Session.LocalId);
            if(HotelDangerRules.Enabled(Session.State)) {
                bool newPreparation=inputDangerWorld!=Session.State.worldId||inputDangerSerial!=Session.State.danger.serial;
                if(newPreparation||(wasIncapacitated&&canAct)) {
                    // Apply the host's recovery/new-day pose before the first local pose can overwrite it.
                    if(LocalPlayer!=null)Teleport(LocalPlayer.position);
                    StopWork();cameraBob=0;View.transform.localPosition=Vector3.up*1.65f;
                }
                inputDangerWorld=Session.State.worldId;inputDangerSerial=Session.State.danger.serial;
                wasIncapacitated=!canAct;
            }
            if(!canAct) {
                var crew=HotelDangerRules.Crew(Session.State,Session.LocalId);
                if(crew!=null)Teleport(crew.position);
                cameraBob=0;vertical=0;
                View.transform.localPosition=new Vector3(0,1.05f,0);
            }
            if(handAnchor!=null)handAnchor.gameObject.SetActive(canAct);
            World.Apply(Session.State,Session.LocalId);
            if(oldNotice!=Session.State.notice) { oldNotice=Session.State.notice; Notify(oldNotice); }
            float horizontalDistance=0;
            bool shiftHeld=keyboardAvailable && keyboard.leftShiftKey.isPressed;
            bool carryingCart=Held?.kind=="cart";
            if(InputActive && keyboardAvailable) {
                if(!Automated)Look(); if(canAct)horizontalDistance=Move(shiftHeld,carryingCart); Focus();
                if(keyboard.eKey.wasPressedThisFrame) Interact();
                if(canAct && Session.State.mvp != null && (keyboard.fKey.wasPressedThisFrame || (Mouse.current?.middleButton.wasPressedThisFrame ?? false)) && FocusId != "")
                    Session.Send(new HotelCommand("ping", FocusId));
                if(keyboard.eKey.wasReleasedThisFrame) StopWork();
                if(workTarget!="" && (FocusId!=workTarget || !keyboard.eKey.isPressed)) StopWork();
                if(workTarget!="" && Time.unscaledTime>nextHeartbeat) {
                    nextHeartbeat=Time.unscaledTime+.15f; Session.Send(new HotelCommand("heartbeat",workTarget));
                }
                if(canAct && keyboard.qKey.wasPressedThisFrame && Held!=null) {
                    StopWork(); var drop=View.transform.position+View.transform.forward*.85f; drop.y=.18f;
                    if(Physics.Raycast(View.transform.position,View.transform.forward,out var obstruction,1.2f)) drop=obstruction.point-View.transform.forward*.35f;
                    Session.Send(new HotelCommand("drop"){position=drop});
                }
                if(keyboard.f5Key.wasPressedThisFrame) { if(Session.Save()) Notify("Отель сохранён."); else if(!Session.IsHost) Notify("Сохранением управляет хост."); }
            } else {
                StopWork();
                // Self-help is a durable virtual focus, including while a panel owns keyboard input.
                if(HotelDangerRules.Crew(Session.State,Session.LocalId)?.life=="downed")Focus();
                else { FocusLabel=""; FocusId=""; }
            }
            Stamina.Tick(Time.unscaledDeltaTime,InputActive && keyboardAvailable,canAct,shiftHeld,carryingCart,horizontalDistance);
            if(canAct && Time.unscaledTime>nextPose) {
                nextPose=Time.unscaledTime+.05f;
                Session.Send(new HotelCommand("pose"){position=player.transform.position,yaw=yaw,pitch=pitch});
            }
            UpdateCarry();
            Audio.Apply(Session.State,cues,ConfirmedWork,AmbientSound);
            if(Toast!="" && Time.unscaledTime>toastUntil) Toast="";
            if(canAct && LocalPlayer!=null && Vector3.Distance(LocalPlayer.position,player.transform.position)>2.5f) Teleport(LocalPlayer.position);
        }
        void Look()
        {
            Vector2 delta=Mouse.current?.delta.ReadValue()??Vector2.zero;
            yaw+=delta.x*Sensitivity; pitch=Mathf.Clamp(pitch+delta.y*Sensitivity*(InvertY?1:-1),-78,78);
            player.transform.rotation=Quaternion.Euler(0,yaw,0);
            View.transform.localRotation=Quaternion.Euler(pitch,0,0);
        }
        float Move(bool shiftHeld, bool carryingCart)
        {
            var k=Keyboard.current;
            Vector2 input=new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
            input=Vector2.ClampMagnitude(input,1);
            float speed=Stamina.Speed(shiftHeld,carryingCart);
            float dt=HotelStamina.ClampDelta(Time.unscaledDeltaTime);
            if(Controller.isGrounded) { vertical=-1.5f; if(k.spaceKey.wasPressedThisFrame) vertical=3.4f; }
            else vertical-=14*dt;
            Vector3 move=player.transform.TransformDirection(new Vector3(input.x,0,input.y))*speed;
            Vector3 before=player.transform.position;
            Controller.Move((move+Vector3.up*vertical)*dt);
            Vector3 displacement=player.transform.position-before;displacement.y=0;
            float horizontalDistance=input.sqrMagnitude>.01f?displacement.magnitude:0;
            cameraBob=Mathf.Lerp(cameraBob,Bob&&input.sqrMagnitude>.01f?Mathf.Sin(Time.unscaledTime*10)*.018f:0,Time.unscaledDeltaTime*10);
            View.transform.localPosition=new Vector3(0,1.65f+cameraBob,0);
            if(input.sqrMagnitude>.1f && Controller.velocity.sqrMagnitude>.1f && Controller.isGrounded && Time.unscaledTime>stepAt) { stepAt=Time.unscaledTime+.46f; Audio.Play("step"); }
            if(player.transform.position.y < -3 || Mathf.Abs(player.transform.position.x)>8.2f || player.transform.position.z>HotelLayout.NorthBoundary || player.transform.position.z < -7.7f) Teleport(HotelLayout.Spawn);
            return horizontalDistance;
        }
        public void Teleport(Vector3 position)
        {
            if(Controller==null)return;
            Controller.enabled=false; player.transform.position=position; Controller.enabled=true; vertical=0;
        }
        public void LookAtForTest(Vector3 point)
        {
            if(!Automated||player==null)return;
            Vector3 direction=(point-View.transform.position).normalized;
            yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;pitch=-Mathf.Asin(direction.y)*Mathf.Rad2Deg;
            player.transform.rotation=Quaternion.Euler(0,yaw,0);View.transform.localRotation=Quaternion.Euler(pitch,0,0);
        }
        void Focus()
        {
            FocusId=""; FocusLabel="";
            if(HotelDangerRules.Enabled(Session.State) && !HotelDangerRules.CanAct(Session.State,Session.LocalId)) {
                var crew=HotelDangerRules.Crew(Session.State,Session.LocalId);
                if(crew?.life=="downed") {
                    FocusId="recover_"+crew.slot;
                    FocusLabel=HotelPresentation.DangerTerminal(Session.State)?"Смена завершена. TAB → итоги; новую подготовку начинает хост.":
                        HotelPresentation.FocusText(Session.State,Session.LocalId,FocusId,"");
                } else FocusLabel=HotelPresentation.DangerTerminal(Session.State)?"Смена завершена. TAB → итоги; новую подготовку начинает хост.":
                    "Вы погибли до конца смены. Можно смотреть по сторонам и открыть TAB / ESC. Коллега ещё может завершить смену.";
                return;
            }
            if(!Physics.Raycast(View.transform.position,View.transform.forward,out var hit,3.15f,~(1<<2),QueryTriggerInteraction.Ignore)) return;
            var target=hit.collider.GetComponentInParent<HotelTarget>(); if(target==null) return;
            FocusId=target.id; FocusLabel=target.label;
            FocusLabel=HotelPresentation.FocusText(Session.State,Session.LocalId,FocusId,FocusLabel);
        }
        void Interact()
        {
            if(FocusId=="")return;
            if(HotelDangerRules.Enabled(Session.State) && HotelDangerRules.IsWorkTarget(FocusId)) {
                string reason=HotelDangerRules.WorkError(Session.State,Session.LocalId,FocusId);
                if(reason!=""){Notify(reason);return;}
                workTarget=FocusId;feedback.BeginWork();Session.Send(new HotelCommand("beginwork",workTarget));nextHeartbeat=0;
                return;
            }
            if(!HotelDangerRules.CanAct(Session.State,Session.LocalId))return;
            if(FocusId=="desk") { OpenPanel("reception"); return; }
            if(FocusId=="board") { OpenPanel("management"); return; }
            if(FocusId.StartsWith("door_")) { OpenPanel("rooms"); return; }
            if(FocusId.StartsWith("guest_")) { OpenPanel("reception"); return; }
            if(Session.State.items.Any(i=>i.id==FocusId&&!i.consumed)) Session.Send(new HotelCommand("pickup",FocusId));
            else if(FocusId.StartsWith("sink_")||FocusId.StartsWith("water_")||FocusId.StartsWith("bed_")||FocusId.StartsWith("trash_")||
                (Session.State.mvp != null && (FocusId=="coffee"||FocusId.StartsWith("utility_")||FocusId.StartsWith("toilet_")||
                FocusId.StartsWith("tv_")||FocusId.StartsWith("lamp_")||FocusId.StartsWith("clean_")||FocusId.StartsWith("dirtytowel_")))) {
                workTarget=FocusId;feedback.BeginWork();Session.Send(new HotelCommand("beginwork",workTarget));nextHeartbeat=0;
            } else Session.Send(new HotelCommand("interact",FocusId));
        }
        void StopWork() { if(workTarget!="") { feedback.CancelWork();Session.Send(new HotelCommand("cancelwork")); } workTarget=""; }
        void UpdateCarry()
        {
            ItemState carried = Held;
            string kind=carried?.kind??"";
            string visualKey=kind+"|"+(carried?.size??"")+"|"+(carried?.condition??"");
            if(visualKey!=carryKind) {
                if(carry!=null)Destroy(carry);
                carryKind=visualKey;
                if(kind!="") {
                    carry=HotelWorld.MakeItem(kind,carried.size,carried.condition); carry.name="Held "+kind; carry.transform.SetParent(handAnchor,false);
                    carry.transform.localPosition=Vector3.zero; carry.transform.localRotation=Quaternion.Euler(0,15,0);
                    if(kind=="mop")carry.transform.localRotation=Quaternion.Euler(-25,0,18);
                    if(kind=="cart")carry.transform.localScale*=.55f;
                    if(kind=="toolbox")carry.transform.localScale*=.72f;
                    foreach(var c in carry.GetComponentsInChildren<Collider>()) c.enabled=false;
                    foreach(var r in carry.GetComponentsInChildren<Renderer>())r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            if(handAnchor==null) return;
            float reach=.73f;
            if(Physics.SphereCast(View.transform.position,.2f,View.transform.forward,out var hit,reach,~(1<<2),QueryTriggerInteraction.Ignore)) reach=Mathf.Max(.3f,hit.distance-.12f);
            HotelFeedback.HandPose(ConfirmedWork,Time.unscaledTime,HandMotion,out var offset,out var rotation);
            float blend=1-Mathf.Exp(-15*Time.unscaledDeltaTime);
            handAnchor.localPosition=Vector3.Lerp(handAnchor.localPosition,new Vector3(.29f,-.43f,reach)+offset,blend);
            handAnchor.localRotation=Quaternion.Slerp(handAnchor.localRotation,rotation,blend);
        }
        public void OpenPanel(string panel) { StopWork(); Stamina.StopSprint(); Panel=panel; SetCursor(); }
        public void SetCursor() { Cursor.lockState=InputActive&&!Automated?CursorLockMode.Locked:CursorLockMode.None; Cursor.visible=!InputActive||Automated; }
        void OnApplicationFocus(bool focused) { applicationFocused=focused; RefreshInputFocus(); }
        void OnApplicationPause(bool paused) { applicationPaused=paused; RefreshInputFocus(); }
        void RefreshInputFocus()
        {
            if(!InputFocused) { StopWork(); Stamina.StopSprint(); vertical=0; }
            if(Session!=null)SetCursor();
        }
        // Only explicit automated players may inject logical focus. No OS focus or preferences changed.
        internal void SetInputFocusForTest(bool? focused)
        {
            if(!Automated)return;
            testInputFocus=focused;RefreshInputFocus();
        }
        public void Notify(string text) {
            if(string.IsNullOrEmpty(text))return;
            // Server may finish before its next world broadcast. Stop the local heartbeat/loop,
            // but keep the observed attempt until that snapshot proves completion or cancellation.
            if(text=="Сначала начните работу."){workTarget="";return;}
            Toast=text; toastUntil=Time.unscaledTime+6;
        }
        public void Leave() { StopWork(); if(Session.Disconnect())OpenPanel("menu"); }
        bool CanQuit(){if(Session!=null&&Session.OwnsHotel&&!Session.Save()){OpenPanel(Session.NeedsRecovery?"recovery":"pause");return false;}return true;}
        void OnDestroy(){Application.wantsToQuit-=CanQuit;if(sleeveMaterial!=null)Destroy(sleeveMaterial);if(gloveMaterial!=null)Destroy(gloveMaterial);}
        public void StoreSettings() {
            PlayerPrefs.SetFloat("sensitivity",Sensitivity);PlayerPrefs.SetFloat("fov",Fov);PlayerPrefs.SetFloat("volume",Volume);
            PlayerPrefs.SetInt("invert",InvertY?1:0);PlayerPrefs.SetInt("bob",Bob?1:0);
            PlayerPrefs.SetInt("handMotion",HandMotion?1:0);PlayerPrefs.SetInt("ambientSound",AmbientSound?1:0);PlayerPrefs.Save();
        }
        public static string ItemName(string kind) {
            switch(kind){case "bag":return "Чемодан";case "toolbox":return "Инструменты";case "mop":return "Швабра";case "plunger":return "Вантуз";case "coffee":return "Кофе";case "linen":return "Чистое бельё";case "dirtylinen":return "Грязное бельё";case "towel":return "Полотенце";case "trashbag":return "Мешок мусора";case "cart":return "Тележка";default:return kind;}
        }
    }
}
