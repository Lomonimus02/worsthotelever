using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WorstHotel
{
    // Opt-in standalone test driver; no debug shortcuts are exposed to normal players.
    public sealed class HotelSmokeTest : MonoBehaviour
    {
        readonly List<string> checks=new List<string>();
        readonly List<string> errors=new List<string>();
        string dir, role;
        HotelGame game;
        bool walkOk, mvp;
        void Awake(){Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        IEnumerator Start()
        {
            game=HotelGame.Instance;dir=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","..","TestResults"));Directory.CreateDirectory(dir);
            float deadline=Time.realtimeSinceStartup+20;
            while(!game.Session.Connected && Time.realtimeSinceStartup<deadline)yield return null;
            if(!game.Session.Connected){Finish(false,"No network session after 20 seconds");yield break;}
            role=game.Session.IsHost?"host":"client";
            checks.Add("CONNECTED "+role+" id="+game.Session.LocalId);
            yield return new WaitForSecondsRealtime(3);
            mvp=game.Session.State.mvp!=null;
            if(game.Session.State.rooms.Count!=(mvp?6:4)){Finish(false,"Unexpected room count");yield break;}
            checks.Add(mvp?"SIX_ROOMS_FOUR_OWNED":"FOUR_ROOMS");
            if(game.Controller==null||game.View==null){Finish(false,"First person controller or camera missing");yield break;}
            checks.Add("FIRST_PERSON_CREATED");
            int renderers=FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;
            if(renderers<50){Finish(false,"Hotel geometry absent");yield break;}
            checks.Add("WORLD_RENDERERS "+renderers);
            foreach(var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))foreach(var material in renderer.sharedMaterials)
                if(material!=null&&(material.shader==null||material.shader.name=="Hidden/InternalErrorShader"))errors.Add("Missing shader: "+renderer.name);
            checks.Add("SHADER_INSPECTION");
            // The planned static navigation axes must also be open in the actual built geometry.
            foreach(int room in new[]{101,102,103,104}) {
                Vector3 a=new Vector3(0,1,HotelLayout.RoomCenter(room).z);
                Vector3 b=HotelLayout.RoomCenter(room)+Vector3.up;
                if(Physics.Linecast(a,b,out var blocked) && blocked.collider.GetComponentInParent<HotelTarget>()==null)
                    errors.Add("Room route blocked "+room+": "+blocked.collider.name);
            }
            checks.Add("ROOM_DOOR_ROUTES_INSPECTED");
            game.OpenPanel("");
            yield return new WaitForEndOfFrame();CaptureWorld(role+"-hotel");
            deadline=Time.realtimeSinceStartup+25;
            while(game.Session.State.players.Count<2&&Time.realtimeSinceStartup<deadline)yield return null;
            if(game.Session.State.players.Count<2){Finish(false,"Second player did not join");yield break;}
            checks.Add("TWO_PLAYERS_REPLICATED");
            if(game.Session.State.contentVersion!=(mvp?2:1)||!game.Session.State.guidedOpening){Finish(false,"New hotel guided rules not replicated");yield break;}
            checks.Add("GUIDED_RULES_REPLICATED");
            if(game.Session.IsHost) {
                game.Session.Save();
                var loaded=HotelSaveStore.Load(game.Session.SavePath);
                if(loaded.cash!=game.Session.State.cash||loaded.day!=game.Session.State.day){Finish(false,"Save roundtrip mismatch");yield break;}
                checks.Add("SAVE_LOAD_ROUNDTRIP");
                // Change a real replicated field and verify it on the other process.
                game.Session.State.notice="WHE_SMOKE_SNAPSHOT";
                deadline=Time.realtimeSinceStartup+58;
                bool opened=false;
                while(game.Session.State.players.Count>1&&Time.realtimeSinceStartup<deadline) {
                    if(!opened&&game.Session.State.rooms.Find(r=>r.number==102).bed==0) {
                        // The walking/input test above stays untouched. This host fixture starts
                        // a real shift afterward to verify the new guest DTO across processes.
                        var position=new Vector3(0,.1f,-2);game.Teleport(position);
                        game.Session.Simulation.Execute(0,new HotelCommand("pose"){position=position});
                        game.Session.Send(new HotelCommand("open"));opened=true;
                    }
                    yield return null;
                }
                if(game.Session.State.players.Count>1){Finish(false,"Client did not complete/disconnect");yield break;}
                if(game.Session.State.items.Exists(i=>i.holder>0)){Finish(false,"Disconnected client retained an item");yield break;}
                checks.Add("DISCONNECT_RELEASES_OWNERSHIP");
                if(game.Session.State.rooms.Find(r=>r.number==102).bed!=0){Finish(false,"Host did not observe completed bed work");yield break;}
                checks.Add("HOST_SEES_CLIENT_ROOM_WORK");
                if(game.Audio.CompletionCount!=0){Finish(false,"Host claimed remote work as its own completion");yield break;}
                checks.Add("REMOTE_WORK_DOES_NOT_EMIT_LOCAL_SUCCESS");
                if(!game.Session.State.guidedOpening){Finish(false,"Client changed host-only guided pace");yield break;}
                if(!game.Session.Save()||HotelSaveStore.Load(game.Session.SavePath).rooms.Find(r=>r.number==102).bed!=0||!HotelSaveStore.Load(game.Session.SavePath).guidedOpening){Finish(false,"Final cooperative change did not persist");yield break;}
                checks.Add("COOPERATIVE_CHANGE_PERSISTED");
                var savedGuest=HotelSaveStore.Load(game.Session.SavePath).guests.Find(g=>mvp?g.mvp!=null&&g.mvp.archetype=="tourist"&&g.mvp.trait=="patient":g.profileVersion==1&&g.profileId=="patient");
                if(!opened||savedGuest==null){Finish(false,"Guest profile not authored and saved after cooperative work");yield break;}
                checks.Add("HOST_GUEST_CATALOGUE_SNAPSHOT_PERSISTED");
                checks.Add("HOST_WORLD_STABLE");
            } else {
                float t=Time.realtimeSinceStartup;Vector3 start=game.Controller.transform.position;
                var serverStart=game.Session.State.players.Find(p=>p.id==game.Session.LocalId).position;
                if(Keyboard.current==null){Finish(false,"No keyboard device");yield break;}
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.EnableDevice(Keyboard.current);
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.D));
                yield return new WaitForSecondsRealtime(.34f);
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
                yield return new WaitForSecondsRealtime(.8f);
                var authoritative=game.Session.State.players.Find(p=>p.id==game.Session.LocalId);
                if(authoritative==null||Vector3.Distance(authoritative.position,serverStart)<.6f||Vector3.Distance(authoritative.position,game.Controller.transform.position)>.35f){Finish(false,"Movement replication mismatch or no authoritative movement");yield break;}
                checks.Add("CLIENT_MOVEMENT_REPLICATED");
                checks.Add("INPUT_SYSTEM_WASD_TO_CONTROLLER_VERIFIED");
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.Tab));yield return new WaitForSecondsRealtime(.12f);
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return new WaitForSecondsRealtime(.12f);
                if(game.Panel!="tasks"){Finish(false,"TAB did not open tasks");yield break;}
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.Tab));yield return new WaitForSecondsRealtime(.12f);
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return new WaitForSecondsRealtime(.12f);
                if(game.Panel!=""){Finish(false,"Second TAB did not close tasks");yield break;}
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.Tab));yield return new WaitForSecondsRealtime(.12f);
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return new WaitForSecondsRealtime(.12f);
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.Escape));yield return new WaitForSecondsRealtime(.12f);
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return new WaitForSecondsRealtime(.12f);
                if(game.Panel!=""){Finish(false,"ESC did not close tasks");yield break;}
                checks.Add("TAB_AND_ESCAPE_UI_INPUT_VERIFIED");
                yield return Walk(new[]{new Vector3(-1.5f,0,-3.3f),new Vector3(-2.6f,0,-3.3f),new Vector3(-2.6f,0,-2.85f)});
                if(!walkOk)yield break;
                game.LookAtForTest(HotelLayout.Target("towels"));yield return new WaitForSecondsRealtime(.15f);
                if(game.FocusId!="towels"){Finish(false,"Cannot focus towel stock: "+game.FocusId);yield break;}
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.E));yield return new WaitForSecondsRealtime(.15f);
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
                yield return new WaitForSecondsRealtime(.7f);
                if(game.Held?.kind!="towel"){Finish(false,"Client regular command failed to acquire towel");yield break;}
                checks.Add("CLIENT_ITEM_COMMAND_REPLICATED");
                var hint=HotelOnboarding.GetHint(game.Session.State,game.Session.LocalId);
                if(hint==null||hint.targetId!="towel_103"){Finish(false,"Client onboarding did not follow held towel to missing stock");yield break;}
                checks.Add("CLIENT_CONTEXTUAL_HINT_FROM_REPLICATED_WORLD");
                yield return Walk(new[]{new Vector3(-2.6f,0,-3.3f),new Vector3(-1.5f,0,-3.3f),new Vector3(3.1f,0,-2.9f)});
                if(!walkOk)yield break;
                string response=null;game.Session.Feedback+=message=>response=message;
                int original=game.Session.State.cash;
                game.Session.Send(new HotelCommand("upgrade",mvp?"bed":"beds",101));
                yield return new WaitForSecondsRealtime(.8f);
                if(response!="Это действие подтверждает хозяин отеля."||game.Session.State.cash!=original){Finish(false,"Expected explicit host-only rejection near board, got: "+response);yield break;}
                checks.Add("CLIENT_ADMIN_REJECTED_AT_VALID_DISTANCE");
                response=null;
                game.Session.Send(new HotelCommand("endGuidedOpening"));
                yield return new WaitForSecondsRealtime(.6f);
                if(response!="Это действие подтверждает хозяин отеля."||!game.Session.State.guidedOpening){Finish(false,"Client could disable guided opening or did not get explicit refusal");yield break;}
                checks.Add("CLIENT_GUIDED_PACE_CHANGE_REJECTED");
                if(mvp)
                {
                    game.LookAtForTest(HotelLayout.Target("board"));yield return new WaitForSecondsRealtime(.3f);
                    if(game.FocusId!="board"){Finish(false,"Cannot focus MVP ping board: "+game.FocusId);yield break;}
                    InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.F));yield return new WaitForSecondsRealtime(.15f);
                    InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return new WaitForSecondsRealtime(.7f);
                    if(!game.Session.State.mvp.pings.Exists(p=>p.playerId==game.Session.LocalId&&p.target=="board")){Finish(false,"F ping did not replicate");yield break;}
                    checks.Add("MVP_F_PING_INPUT_REPLICATED");
                }
                deadline=Time.realtimeSinceStartup+12;
                while(game.Session.State.notice!="WHE_SMOKE_SNAPSHOT"&&Time.realtimeSinceStartup<deadline)yield return null;
                if(game.Session.State.notice!="WHE_SMOKE_SNAPSHOT"){Finish(false,"Host snapshot was not received");yield break;}
                checks.Add("HOST_SNAPSHOT_RECEIVED");
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.Q));yield return new WaitForSecondsRealtime(.15f);
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return new WaitForSecondsRealtime(.5f);
                if(game.Held!=null){Finish(false,"Q did not release towel");yield break;}
                checks.Add("Q_DROP_REPLICATED");
                yield return Walk(new[]{new Vector3(1.85f,0,.8f),new Vector3(0,0,.8f),new Vector3(0,0,5.5f),new Vector3(4.4f,0,5.5f)});
                if(!walkOk)yield break;
                game.LookAtForTest(HotelLayout.RoomTarget("bed",102));yield return new WaitForSecondsRealtime(.2f);
                if(game.FocusId!="bed_102"){Finish(false,"Cannot focus bed through normal interaction ray: "+game.FocusId);yield break;}
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.E));yield return new WaitForSecondsRealtime(2.8f);
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return new WaitForSecondsRealtime(.5f);
                if(game.Held?.kind!="dirtylinen"||game.Session.State.rooms.Find(r=>r.number==102).bed!=0){Finish(false,"Held E did not complete authoritative bed work");yield break;}
                checks.Add("HELD_E_BED_WORK_REPLICATED");CaptureWorld("client-room-102");
                if(game.Audio.CompletionCount!=1||game.Audio.ActiveWork!=""){Finish(false,"Client snapshot completion cue missing/duplicated or loop stuck");yield break;}
                checks.Add("CLIENT_CONFIRMED_WORK_AUDIO_COMPLETES_ONCE_AND_STOPS");
                deadline=Time.realtimeSinceStartup+8;
                while(game.Session.State.guests.Count==0&&Time.realtimeSinceStartup<deadline)yield return null;
                var guest=game.Session.State.guests.Find(g=>mvp?g.mvp!=null&&g.mvp.trait=="patient":g.profileId=="patient");
                if(guest==null||(!mvp&&(guest.profileVersion!=1||guest.requestDelay<=0||guest.stayDuration<=0))||(mvp&&(guest.mvp.agreedTariff<=0||guest.mvp.requestDelay<=0))||!HotelDirector.ClockHeld(game.Session.State)) {Finish(false,"Active guest catalogue fields/guided clock not replicated");yield break;}
                checks.Add("ACTIVE_GUEST_PROFILE_AND_GUIDED_CLOCK_REPLICATED");
            }
            Finish(errors.Count==0,errors.Count==0?"All smoke checks passed":"Runtime errors");
        }
        IEnumerator Walk(Vector3[] points)
        {
            walkOk=false;
            foreach(Vector3 point in points){
                float until=Time.realtimeSinceStartup+10;
                while(Time.realtimeSinceStartup<until){
                    Vector3 delta=point-game.Controller.transform.position;delta.y=0;
                    if(delta.magnitude<.16f)break;
                    game.Controller.Move(delta.normalized*2.2f*Time.unscaledDeltaTime);
                    yield return null;
                }
                Vector3 remaining=point-game.Controller.transform.position;remaining.y=0;
                if(remaining.magnitude>.35f){Finish(false,"Walk blocked en route to "+point+" at "+game.Controller.transform.position);yield break;}
            }
            yield return new WaitForSecondsRealtime(.4f);walkOk=true;
        }
        void CaptureWorld(string name)
        {
            // Hidden windows have a black backbuffer; explicitly render the real URP camera.
            var target=new RenderTexture(1280,800,24,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;Texture2D texture=null;
            try {
                target.Create();RenderPipeline.SubmitRenderRequest(game.View,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;texture=new Texture2D(1280,800,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,1280,800),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(dir,name+".png"),texture.EncodeToPNG());
                var pixels=texture.GetPixels32();int lit=0;
                for(int i=0;i<pixels.Length;i+=64)if(pixels[i].r+pixels[i].g+pixels[i].b>45)lit++;
                if(lit<100)errors.Add("Rendered camera image is blank");else checks.Add("URP_CAMERA_NONBLANK "+lit);
            }finally{RenderTexture.active=previous;target.Release();Destroy(target);if(texture!=null)Destroy(texture);}
        }
        void Finish(bool success,string reason)
        {
            checks.Insert(0,success?"PASS":"FAIL");checks.Add(reason);checks.AddRange(errors);
            if(dir==null)dir=Application.persistentDataPath;
            File.WriteAllLines(Path.Combine(dir,(role??"unknown")+"-smoke.txt"),checks);
            Debug.Log("WHE_SMOKE "+(success?"PASS":"FAIL")+" "+reason);
            game?.Session.Disconnect();Application.Quit(success?0:1);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
