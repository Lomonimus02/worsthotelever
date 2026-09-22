using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEngine;

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
        IEnumerator ReviewUI()
        {
            // Visual regression fixtures use real simulation commands but are not an input test.
            // The isolated -whe-session-tests slot protects the user's existing hotel.
            yield return new WaitForSecondsRealtime(5);
            game.Session.Simulation.Execute(0,new HotelCommand("pose"){position=new Vector3(0,.1f,-2)});
            game.Teleport(new Vector3(0,.1f,-2));
            game.Session.Simulation.Execute(0,new HotelCommand("open"));
            foreach(int width in new[]{1280,960})
            {
                Screen.SetResolution(width,width==1280?800:600,FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(.7f);
                foreach(string panel in new[]{"","reception","guest","tasks","management","finish-confirm","pause"})
                {
                    if(panel=="guest")typeof(HotelUI).GetField("selectedGuest",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(game.UI,game.Session.State.guests[0].id);
                    game.OpenPanel(panel);
                    yield return new WaitForSecondsRealtime(.4f);
                    yield return new WaitForEndOfFrame();
                    var texture=ScreenCapture.CaptureScreenshotAsTexture();
                    try {
                        int lit=0;var pixels=texture.GetPixels32();
                        for(int i=0;i<pixels.Length;i+=64)if(pixels[i].r+pixels[i].g+pixels[i].b>70)lit++;
                        if(lit<100)errors.Add("Blank UI capture "+panel+" "+width);
                        File.WriteAllBytes(Path.Combine(directory,"ui-"+width+"-"+(panel==""?"hud":panel)+".png"),texture.EncodeToPNG());
                    }finally{Destroy(texture);}
                }
            }
            checks.Add("UI_RENDER_FIXTURES_1280x800_AND_960x600");
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
