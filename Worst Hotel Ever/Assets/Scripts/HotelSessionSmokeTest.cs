using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WorstHotel
{
    // Opt-in development test only. Injects a transport event into the actual NGO lifecycle,
    // never substitutes a fake save store or writes to the user's normal slot.
    public sealed class HotelSessionSmokeTest : MonoBehaviour
    {
        readonly List<string> checks=new List<string>(), errors=new List<string>();
        HotelGame game;
        string scenario, directory;
        bool expectedFailure, expectedIo, finished;
        int transportFailures, ioFailures;
        FileStream lockedSave;
        void Awake(){Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type)
        {
            if(type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert)return;
            if(type==LogType.Error&&expectedFailure&&message.Contains("Host is shutting down due to network transport failure of UnityTransport!"))
            {transportFailures++;checks.Add("EXPECTED_NGO_TRANSPORT_FAILURE");return;}
            string detail=message+"\n"+stack;
            if(type==LogType.Error&&expectedIo&&detail.Contains("IOException")&&detail.Contains("WorstHotel.HotelSession.Save")&&detail.Contains("WorstHotel.HotelSaveStore.Save"))
            {ioFailures++;checks.Add("EXPECTED_SAVE_IO_FAILURE");return;}
            errors.Add(message+"\n"+stack);
        }
        IEnumerator Start()
        {
            game=HotelGame.Instance;
            directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","..","TestResults"));
            Directory.CreateDirectory(directory);
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-whe-case");
            scenario=index>=0&&index+1<args.Length?args[index+1]:"lifecycle";
            float until=Time.realtimeSinceStartup+14;
            while(!game.Playing&&Time.realtimeSinceStartup<until)
            {
                if(scenario.StartsWith("capacity-client")&&game.Session.Status.Contains("уже два"))
                {checks.Add("CAPACITY_REJECTED_WITH_REASON");Finish(true);yield break;}
                yield return null;
            }
            if(!Check(game.Playing,"Connection did not complete: "+game.Session.Status))yield break;
            yield return null;
            if(!Check(game.LocalPlayer!=null,"Connected client has no simulation player"))yield break;
            if(scenario=="ui") {yield return ReviewUI();Finish(errors.Count==0);yield break;}
            if(scenario=="opening") {yield return ReviewOpening();if(!finished)Finish(errors.Count==0);yield break;}
            if(scenario=="feedback") {yield return ReviewFeedback();if(!finished)Finish(errors.Count==0);yield break;}
            if(scenario.StartsWith("capacity-client"))
            {
                checks.Add("CAPACITY_ACCEPTED_WITH_PLAYER");
                yield return new WaitForSecondsRealtime(5);
                Finish(true);yield break;
            }
            if(scenario=="capacity-host")
            {
                int peak=0;until=Time.realtimeSinceStartup+12;
                while(Time.realtimeSinceStartup<until)
                {
                    peak=Math.Max(peak,game.Session.Manager.ConnectedClientsIds.Count);
                    if(!Check(peak<=2&&game.Session.State.players.Count<=2,"Capacity exceeded two players"))yield break;
                    yield return null;
                }
                if(!Check(peak==2,"Capacity test never admitted a remote player"))yield break;
                checks.Add("CAPACITY_HOST_PEAK_TWO");Finish(true);yield break;
            }

            // Deterministic same-update approval race: neither response has been applied yet.
            var approval=game.Session.Manager.ConnectionApprovalCallback;
            var request=new NetworkManager.ConnectionApprovalRequest{ClientNetworkId=9001,Payload=Encoding.UTF8.GetBytes(HotelSession.Protocol+"|")};
            var first=new NetworkManager.ConnectionApprovalResponse();approval(request,first);
            request.ClientNetworkId=9002;var second=new NetworkManager.ConnectionApprovalResponse();approval(request,second);
            if(!Check(first.Approved&&!second.Approved,"Simultaneous pending approvals did not reserve capacity"))yield break;
            checks.Add("PENDING_APPROVAL_RACE_RESERVED_ONE_SLOT");
            if(!Check(!game.Session.ResetSteam(),"Steam reset allowed during active session"))yield break;

            game.Session.State.cash=350;
            if(!Check(game.Session.Save(),"Initial save failed"))yield break;
            game.Session.State.cash=333;
            InjectTransportFailure();
            yield return new WaitForSecondsRealtime(1);
            if(!Check(transportFailures==1,"Expected exactly one NGO transport failure"))yield break;
            if(!Check(!game.Session.IsHost&&game.Session.OwnsHotel&&game.Session.NeedsRecovery&&!game.Playing,"Host state obligation lost after NGO shutdown"))yield break;
            if(!Check(game.Panel=="recovery"&&HotelSaveStore.Load(game.Session.SavePath).cash==333,"Network failure did not persist latest progress or show recovery"))yield break;
            checks.Add("TRANSPORT_FAILURE_PERSISTS_UNSAVED_PROGRESS");
            if(!Check(game.Session.Disconnect(),"Could not exit recovered session"))yield break;
            yield return null;yield return null;
            game.Session.Host(true,17779,"");
            until=Time.realtimeSinceStartup+6;while(!game.Playing&&Time.realtimeSinceStartup<until)yield return null;
            if(!Check(game.Playing&&game.Session.State.cash==333,"Same-process restart did not restore checkpoint"))yield break;
            game.OpenPanel("");
            // Allow reads but prohibit replacing the primary. The actual OS must reject the write.
            lockedSave=new FileStream(game.Session.SavePath,FileMode.Open,FileAccess.Read,FileShare.Read);
            game.Session.State.cash=322;
            expectedIo=true;InjectTransportFailure();
            yield return new WaitForSecondsRealtime(1);
            if(!Check(transportFailures==2&&ioFailures==1,"Expected one transport failure and one failed emergency write"))yield break;
            if(!Check(game.Session.OwnsHotel&&game.Session.NeedsRecovery&&game.Session.State.cash==322&&!string.IsNullOrEmpty(game.Session.SaveError),"Failed emergency save discarded live state"))yield break;
            if(!Check(!game.Session.Disconnect(false)&&game.Session.State.cash==322,"Cancel/new-session path bypassed failed save"))yield break;
            bool canQuit=(bool)typeof(HotelGame).GetMethod("CanQuit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(game,null);
            if(!Check(!canQuit,"Quit bypassed unsaved recovered state"))yield break;
            if(!Check(ioFailures==3,"Expected exactly three scoped save failures (shutdown/disconnect/quit)"))yield break;
            checks.Add("DISK_LOCK_RETAINS_WORLD_AND_BLOCKS_QUIT_AND_DISCARD");
            lockedSave.Dispose();lockedSave=null;expectedIo=false;
            if(!Check(game.Session.Save()&&HotelSaveStore.Load(game.Session.SavePath).cash==322,"Save retry after unlock failed"))yield break;
            if(!Check(game.Session.Disconnect(false),"Saved recovery could not close"))yield break;
            yield return null;yield return null;
            if(!Check(game.Session.ResetSteam(),"Stopped session cannot reset Steam pending requests"))yield break;
            checks.Add("RECOVERY_RETRY_SUCCEEDS_AND_STEAM_RESET_GUARDED");
            Finish(true);
        }
        void InjectTransportFailure()
        {
            // Reflection exists only in this opt-in test driver, to invoke the protected event
            // on the real configured UnityTransport and exercise NGO's own shutdown path.
            expectedFailure=true;
            try {
                typeof(NetworkTransport).GetMethod("InvokeOnTransportEvent",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(game.Session.Manager.NetworkConfig.NetworkTransport,new object[]{NetworkEvent.TransportFailure,0UL,default(ArraySegment<byte>),Time.realtimeSinceStartup});
            } finally { expectedFailure=false; }
        }
        // This runs the shipped Resources + host rules in the standalone player. It is a
        // deterministic fixture with accelerated ticks/teleports, NOT a human-input test.
        void FixtureAt(string target)
        {
            Vector3 position=HotelLayout.Target(target);position.y=.1f;
            game.Teleport(position);
            string error=game.Session.Simulation.Execute(0,new HotelCommand("pose"){position=position});
            if(error!="")throw new Exception("Fixture pose: "+error);
        }
        void FixtureCommand(string action,string target="",int number=0)
        {
            string failure=null;Action<string> feedback=message=>failure=message;
            game.Session.Feedback+=feedback;
            try{game.Session.Send(new HotelCommand(action,target,number));}
            finally{game.Session.Feedback-=feedback;}
            if(!string.IsNullOrEmpty(failure))throw new Exception(action+" "+target+": "+failure);
        }
        void FixtureAdvance(float seconds)
        {
            for(int i=0;i<(int)Math.Ceiling(seconds*10);i++)game.Session.Simulation.Tick(.1f);
        }
        void FixtureWork(string target)
        {
            FixtureAt(target);FixtureCommand("beginwork",target);
            for(int i=0;i<80&&game.Session.State.players[0].workTarget!="";i++)
            {FixtureCommand("heartbeat",target);game.Session.Simulation.Tick(.1f);}
            if(game.Session.State.players[0].workTarget!="")throw new Exception("Work did not complete: "+target);
        }
        void FixturePick(string kind)
        {
            var item=game.Session.State.items.Find(i=>i.kind==kind&&!i.consumed&&i.holder==-1);
            if(item==null)throw new Exception("Fixture has no free "+kind);
            Vector3 position=item.position;position.y=.1f;game.Teleport(position);
            string error=game.Session.Simulation.Execute(0,new HotelCommand("pose"){position=position});
            if(error!="")throw new Exception("Pickup pose: "+error);
            FixtureCommand("pickup",item.id);
        }
        void FixtureDrop()
        {
            game.Session.Send(new HotelCommand("drop"){position=game.Session.State.players[0].position});
            if(game.Session.State.players[0].held!="")throw new Exception("Fixture drop failed");
        }
        IEnumerator ReviewOpening()
        {
            int firstGuest=0,leakRoom=0;string worldId="";
            float requestDelay=0,stayDuration=0,patienceLimit=0;
            try {
                var state=game.Session.State;worldId=state.worldId;
                if(state.contentVersion!=1||!state.guidedOpening)throw new Exception("New guided state absent in player");
                FixtureAt("desk");FixtureCommand("open");FixtureAdvance(600);
                if(state.arrivals!=1||state.guests[0].stage!="queue"||!HotelDirector.ClockHeld(state))throw new Exception("Slow learner lost first guest or shift");
                var profile=state.guests[0];
                if(profile.profileVersion!=1||profile.profileId!="patient"||profile.requestDelay<=0)throw new Exception("Packaged catalogue not used; fallback guest is insufficient");
                requestDelay=profile.requestDelay;stayDuration=profile.stayDuration;patienceLimit=profile.patienceLimit;
                checks.Add("PACKAGED_GUIDED_OPENING_PROTECTS_SLOW_TEAM");
                FixtureCommand("checkin",number:103);firstGuest=state.guests[0].id;
                FixturePick("bag");FixtureAt("bag_103");FixtureCommand("interact","bag_103");
                FixtureAt("linen");FixtureCommand("interact","linen");FixtureWork("bed_104");
                FixtureAdvance(90);
                if(!state.guests[0].towelRequested)throw new Exception("Guided towel request absent");
                FixtureAt("towels");FixtureCommand("interact","towels");FixtureAt("towel_103");FixtureCommand("interact","towel_103");
                FixtureAdvance(1);
                var leak=state.rooms.Find(r=>r.leak);
                if(leak==null)throw new Exception("Conditional leak absent after service and preparation");
                leakRoom=leak.number;
                if(!game.Session.Save())throw new Exception("Mid-lesson save failed");
                checks.Add("ALTERNATE_ROOM_SERVICE_PREPARATION_AND_REAL_LEAK");
            } catch(Exception e){Check(false,e.ToString());yield break;}
            if(!Check(game.Session.Disconnect(),"Opening fixture disconnect failed"))yield break;
            yield return null;yield return null;
            game.Session.Host(true,17782,"");
            float until=Time.realtimeSinceStartup+6;
            while(!game.Playing&&Time.realtimeSinceStartup<until)yield return null;
            if(!Check(game.Playing,"Mid-lesson reload did not connect"))yield break;
            yield return null;
            try {
                var state=game.Session.State;
                if(state.worldId!=worldId||state.guests[0].id!=firstGuest||!state.rooms.Find(r=>r.number==leakRoom).leak)throw new Exception("Mid-lesson state not restored");
                var profile=state.guests[0];
                if(profile.profileVersion!=1||profile.requestDelay!=requestDelay||profile.stayDuration!=stayDuration||profile.patienceLimit!=patienceLimit)throw new Exception("Active guest catalogue snapshot changed after reload");
                checks.Add("PACKAGED_GUEST_CATALOGUE_AND_ACTIVE_SNAPSHOT_PERSIST");
                FixturePick("toolbox");FixtureWork("sink_"+leakRoom);FixtureDrop();
                FixturePick("mop");FixtureWork("water_"+leakRoom);FixtureDrop();FixtureAdvance(1);
                if(HotelDirector.ClockHeld(state))throw new Exception("Foundation lessons did not release clock");
                if(state.rooms.Exists(r=>r.leak))throw new Exception("Reload duplicated guided leak");
                checks.Add("MID_LESSON_RELOAD_REPAIR_WATER_AND_DIRECTOR_HANDOFF");
                var guest=state.guests.Find(g=>g.id==firstGuest);
                for(int i=0;i<4000&&(guest.stage!="checkout"||Vector3.Distance(guest.position,new Vector3(.7f,0,-2.3f))>.15f);i++)game.Session.Simulation.Tick(.1f);
                FixtureAt("desk");FixtureCommand("checkout",number:firstGuest);
                if(!guest.paid||state.earned<=0)throw new Exception("Actual checkout did not pay");
                FixtureCommand("finish");FixtureCommand("nextday");
                if(state.day!=2||state.phase!="preparation"||state.rooms.Find(r=>r.number==103).bed!=1)throw new Exception("Day2 lost turnover state");
                FixtureAt("board");FixtureCommand("upgrade","toolbox");
                if(!state.secondToolbox||state.items.FindAll(i=>i.kind=="toolbox"&&!i.consumed).Count!=2)throw new Exception("Upgrade not materialized");
                FixtureAt("desk");FixtureCommand("open");FixtureAdvance(190);
                if(state.arrivals<2||state.guests.Find(g=>g.trait=="Спешит")==null||state.time<180||HotelDirector.ClockHeld(state))throw new Exception("Day2 did not use normal population/clock");
                if(!game.Session.Save())throw new Exception("Day2 save failed");
                var loaded=HotelSaveStore.Load(game.Session.SavePath);
                if(loaded.day!=2||!loaded.secondToolbox||loaded.worldId!=worldId||loaded.cash!=state.cash)throw new Exception("Day2 save lost progress");
                checks.Add("CHECKOUT_TURNOVER_PURCHASE_AND_NORMAL_DAY_TWO_PERSIST");
            } catch(Exception e){Check(false,e.ToString());yield break;}
        }
        IEnumerator ReviewUI()
        {
            // Visual regression fixtures use real simulation commands but are not an input test.
            // The isolated -whe-session-tests slot protects the user's existing hotel.
            yield return new WaitForSecondsRealtime(5);
            game.Session.Simulation.Execute(0,new HotelCommand("pose"){position=new Vector3(0,.1f,-2)});
            game.Teleport(new Vector3(0,.1f,-2));
            game.Session.Simulation.Execute(0,new HotelCommand("open"));
            foreach(int width in new[]{1280,960})
                yield return CapturePanels(width,"",new[]{"","reception","guest","tasks","management","briefing","pace-confirm","finish-confirm","pause","settings"});
            FixtureAt("desk");FixtureCommand("finish");FixtureCommand("nextday");
            foreach(int width in new[]{1280,960})
                yield return CapturePanels(width,"day2-",new[]{"briefing",""});
            checks.Add("UI_RENDER_FIXTURES_1280x800_AND_960x600_24_PANELS");
        }
        // Real player and InputSystem path; fixture positioning is deliberately not a human playtest.
        IEnumerator ReviewFeedback()
        {
            var audio=game.Audio;var state=game.Session.State;
            if(!Check(audio.ClipCount==9&&audio.GetComponentsInChildren<AudioSource>().Length==6,"Audio allocation budget"))yield break;
            game.AmbientSound=true;state.rooms[0].leak=true;state.rooms[2].leak=true;
            yield return new WaitForSecondsRealtime(.2f);
            if(!Check(audio.ActiveLeaks==2,"Spatial leak loops missing"))yield break;
            foreach(var source in audio.GetComponentsInChildren<AudioSource>())
                if(source.name.StartsWith("Leak ")&&!Check(source.spatialBlend==1&&source.dopplerLevel==0&&source.maxDistance==9&&source.clip.channels==1,"Invalid spatial source"))yield break;
            int voices=audio.GetComponentsInChildren<AudioSource>().Length;
            for(int i=0;i<100;i++)audio.Apply(state,HotelCue.None,"",true);
            if(!Check(voices==audio.GetComponentsInChildren<AudioSource>().Length&&audio.ClipCount==9,"Repeated snapshots allocated audio"))yield break;
            game.AmbientSound=false;yield return null;yield return null;
            if(!Check(audio.ActiveLeaks==0,"Ambient-off retained leak"))yield break;
            state.rooms[0].leak=false;state.rooms[0].water=.4f;game.AmbientSound=true;
            game.Volume=0;yield return null;yield return null;
            if(!Check(audio.ActiveLeaks==1&&AudioListener.volume==0,"Repaired sink, residual water or master mute wrong"))yield break;
            game.Volume=.55f;state.rooms[2].leak=false;
            checks.Add("SIX_REUSED_SOURCES_NINE_CLIPS_SPATIAL_LEAKS_MUTE_AND_REPAIR_VERIFIED");

            game.OpenPanel("");game.Teleport(new Vector3(4.4f,.1f,5.5f));
            game.Session.Simulation.Execute(0,new HotelCommand("pose"){position=new Vector3(4.4f,.1f,5.5f)});
            game.LookAtForTest(HotelLayout.RoomTarget("bed",102));
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.EnableDevice(Keyboard.current);
            yield return new WaitForSecondsRealtime(.25f);
            if(!Check(game.FocusId=="bed_102","Bed ray not focused"))yield break;
            int successes=audio.CompletionCount;
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.E));
            yield return new WaitForSecondsRealtime(.35f);
            Transform anchor=game.View.transform.Find("Carry anchor");
            if(!Check(game.ConfirmedWork=="bed_102"&&audio.ActiveWork=="cloth"&&Quaternion.Angle(anchor.localRotation,Quaternion.identity)>1,"Confirmed work did not animate/play"))yield break;
            CaptureFeedback("work-bed");
            game.HandMotion=false;yield return new WaitForSecondsRealtime(.35f);
            if(!Check(Quaternion.Angle(anchor.localRotation,Quaternion.identity)<.3f,"Hand motion toggle not neutral"))yield break;
            game.HandMotion=true;game.OpenPanel("pause");
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
            yield return new WaitForSecondsRealtime(.2f);
            if(!Check(audio.ActiveWork==""&&audio.CompletionCount==successes&&state.rooms[1].bed==1,"Panel cancel reported success or continued work"))yield break;
            checks.Add("REAL_E_CONFIRMED_HANDS_AND_WORK_AUDIO_CANCEL_ON_PANEL_MOTION_OFF_NEUTRAL");
            game.OpenPanel("");yield return new WaitForSecondsRealtime(.1f);
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.E));yield return new WaitForSecondsRealtime(.35f);
            // Emulate the client ordering: idle feedback arrives before its completed world snapshot.
            // The host fixture still holds the old world for one frame, then publishes the result.
            game.Notify("Сначала начните работу.");yield return null;yield return null;
            // Finish between frames, then release E on the very next Update: the confirmed result
            // must be observed before local cancellation. Co-op smoke also covers real-time work.
            for(int i=0;i<30&&game.LocalPlayer.workTarget!="";i++)
            {FixtureCommand("heartbeat","bed_102");game.Session.Simulation.Tick(.1f);}
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());yield return new WaitForSecondsRealtime(.2f);
            if(!Check(state.rooms[1].bed==0&&audio.CompletionCount==successes+1&&audio.ActiveWork=="","Completed bed feedback missing/duplicated"))yield break;
            checks.Add("REAL_E_WORK_RESULT_SURVIVES_IDLE_CALLBACK_STALE_WORLD_AND_RELEASE_EXACTLY_ONCE");

            FixtureAt("desk");FixtureCommand("open");yield return new WaitForSecondsRealtime(.2f);
            if(!Check(audio.ArrivalCount==1,"Live first arrival absent or duplicated"))yield break;
            int arrivals=audio.ArrivalCount;
            if(!Check(game.Session.Save()&&game.Session.Disconnect(),"Feedback fixture save/disconnect failed"))yield break;
            yield return new WaitForSecondsRealtime(.2f);
            if(!Check(audio.ActiveLeaks==0&&audio.ActiveWork=="","Disconnect retained audio"))yield break;
            game.Session.Host(true,17783,"");float until=Time.realtimeSinceStartup+6;
            while(!game.Playing&&Time.realtimeSinceStartup<until)yield return null;
            yield return new WaitForSecondsRealtime(.3f);
            if(!Check(game.Playing&&audio.ArrivalCount==arrivals&&audio.ClipCount==9&&audio.GetComponentsInChildren<AudioSource>().Length==6,"Load replayed arrival or leaked sources"))yield break;
            int anchors=0,carriedVisuals=0;
            foreach(var node in game.View.GetComponentsInChildren<Transform>())
            {if(node.name=="Carry anchor")anchors++;if(node.name.StartsWith("Held "))carriedVisuals++;}
            if(!Check(anchors==1&&carriedVisuals==0&&game.Held==null,"Reconnect retained ghost hands or held item"))yield break;
            checks.Add("SAVE_RECONNECT_IS_SILENT_BASELINE_AND_REUSES_AUDIO");
            checks.Add("RECONNECT_HAS_ONE_HAND_RIG_AND_NO_GHOST_CARRIED_ITEM");
            // Render the shipped figure rig in an isolated visual fixture; model/world integration
            // is covered separately by HotelFigureTests, not by fabricated guest simulation states.
            game.OpenPanel("");var observerPosition=new Vector3(2.9f,.1f,3.5f);
            game.Session.Simulation.Execute(0,new HotelCommand("pose"){position=observerPosition});game.Teleport(observerPosition);
            yield return new WaitForSecondsRealtime(.2f);
            game.LookAtForTest(new Vector3(4.2f,1,5.6f));
            var figure=HotelWorld.MakePerson(false,2);var rig=figure.GetComponent<HotelFigure>();
            rig.Pose(new Vector3(4.2f,0,5.6f),-148,0,false,false);
            try {
                foreach(string reaction in new[]{"queue","request","angry","angry-request","recovered"})
                {
                    rig.React(reaction=="queue"?"queue":"staying",reaction.StartsWith("angry")?10:100,reaction.Contains("request"));
                    yield return new WaitForSecondsRealtime(.8f);
                    var point=game.View.WorldToViewportPoint(figure.transform.position+Vector3.up);
                    if(!Check(point.z>0&&point.x>.1f&&point.x<.9f&&point.y>.1f&&point.y<.9f,"Reaction fixture outside camera"))yield break;
                    CaptureFeedback("guest-"+reaction);
                }
            }finally{Destroy(figure);}
            checks.Add("FIVE_GUEST_REACTION_RENDER_FIXTURES_CAPTURED_FOR_VISUAL_REVIEW");
        }
        void CaptureFeedback(string label)
        {
            var target=new RenderTexture(1280,800,24,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;Texture2D texture=null;
            try {
                target.Create();RenderPipeline.SubmitRenderRequest(game.View,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;texture=new Texture2D(1280,800,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,1280,800),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(directory,"feedback-"+label+".png"),texture.EncodeToPNG());
            }finally{RenderTexture.active=previous;target.Release();Destroy(target);if(texture!=null)Destroy(texture);}
        }
        IEnumerator CapturePanels(int width,string prefix,string[] panels)
        {
            Screen.SetResolution(width,width==1280?800:600,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.7f);
            foreach(string panel in panels)
            {
                if(panel=="guest")typeof(HotelUI).GetField("selectedGuest",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(game.UI,game.Session.State.guests[0].id);
                game.OpenPanel(panel);
                yield return new WaitForSecondsRealtime(.4f);
                yield return new WaitForEndOfFrame();
                var texture=ScreenCapture.CaptureScreenshotAsTexture();
                try {
                    int lit=0;var pixels=texture.GetPixels32();
                    for(int i=0;i<pixels.Length;i+=64)if(pixels[i].r+pixels[i].g+pixels[i].b>70)lit++;
                    if(lit<100)errors.Add("Blank UI capture "+prefix+panel+" "+width);
                    File.WriteAllBytes(Path.Combine(directory,"ui-"+width+"-"+prefix+(panel==""?"hud":panel)+".png"),texture.EncodeToPNG());
                }finally{Destroy(texture);}
            }
        }
        bool Check(bool condition,string error)
        {
            if(condition)return true;
            errors.Add(error);Finish(false);return false;
        }
        void Finish(bool passed)
        {
            if(finished)return;finished=true;
            lockedSave?.Dispose();lockedSave=null;expectedIo=false;
            game.Session.Disconnect();
            passed&=errors.Count==0;
            checks.Insert(0,passed?"PASS":"FAIL");checks.AddRange(errors);
            File.WriteAllLines(Path.Combine(directory,scenario+"-session.txt"),checks);
            Application.Quit(passed?0:1);
        }
        void OnDestroy(){lockedSave?.Dispose();Application.logMessageReceived-=Log;}
    }
}
