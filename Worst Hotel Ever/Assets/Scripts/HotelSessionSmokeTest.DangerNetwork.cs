using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WorstHotel
{
    public sealed partial class HotelSessionSmokeTest
    {
        IEnumerator ReviewDangerCoop()
        {
            if(!Check(game.Session.State.version==3&&HotelDangerRules.Enabled(game.Session.State),"Network test is not production v3"))yield break;
            var background=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.EnableDevice(Keyboard.current);
            bool reconnect=Array.IndexOf(Environment.GetCommandLineArgs(),"-whe-no-reconnect")<0;
            try
            {
                if(game.Session.IsHost)
                {
                    var s=game.Session.State;
                    float until=Time.realtimeSinceStartup+25;
                    while(s.players.Count<2&&Time.realtimeSinceStartup<until)yield return null;
                    if(!Check(s.players.Count==2,"Danger partner never connected"))yield break;
                    ulong firstId=s.players.Find(p=>p.id!=0).id;
                    if(!MvpInteractionFixture("REMOTE_PREPARATION_BOARD_POSITION",()=>
                    {
                        var point=HotelLayout.Target("board");point.y=.1f;
                        var error=game.Session.Simulation.Execute(firstId,new HotelCommand("pose"){position=point});
                        if(error!="")throw new Exception(error);
                    }))yield break;
                    until=Time.realtimeSinceStartup+12;
                    while(!s.mvp.pings.Exists(p=>p.playerId==firstId&&p.target=="board")&&Time.realtimeSinceStartup<until)
                    {s.notice="DANGER_AUTH_READY";yield return null;}
                    if(!Check(s.mvp.pings.Exists(p=>p.playerId==firstId&&p.target=="board")&&s.danger.mode=="standard",
                        "Client authority handshake failed or preparation mode changed"))yield break;
                    checks.Add("PREPARATION_HOST_AUTHORITY_GUARD_CHECKED_AT_BOARD");
                    HotelIncidentState incident=null;
                    if(!MvpInteractionFixture("NETWORK_EXPOSURE_SETUP",()=>
                    {
                        FixtureAt("board");FixtureCommand("dangerMode","bold");
                        foreach(var h in s.danger.incidents)h.triggerAt=1000;
                        FixtureAt("desk");FixtureCommand("open");FixtureCommand("endGuidedOpening");
                        incident=s.danger.incidents.Find(h=>h.kind=="steam")??s.danger.incidents[0];
                        incident.triggerAt=s.danger.elapsed;s.danger.graceUntil=s.danger.elapsed;
                        var crew=HotelDangerRules.Crew(s,firstId);crew.health=5;crew.shield=0;
                        Vector3 source=DangerExposurePosition(incident);
                        var error=game.Session.Simulation.Execute(firstId,new HotelCommand("pose"){position=source});
                        if(error!="")throw new Exception(error);
                    }))yield break;
                    until=Time.realtimeSinceStartup+4;
                    while(incident.status=="planned"&&Time.realtimeSinceStartup<until)yield return null;
                    if(!Check(incident.status=="warning","Network incident warning missing"))yield break;
                    incident.warningRemaining=.2f;
                    var partner=HotelDangerRules.Crew(s,firstId);
                    until=Time.realtimeSinceStartup+12;
                    while(partner.life=="healthy"&&Time.realtimeSinceStartup<until)yield return null;
                    if(!Check(partner.life=="downed"&&partner.downs==1,"Actual hazard did not down connected partner"))yield break;
                    checks.Add("AUTHORITATIVE_REAL_DAMAGE_DOWNED_REMOTE_CREW");
                    Vector3 approach=new Vector3(incident.room%2==0?.65f:-.65f,.1f,HotelLayout.RoomCenter(incident.room).z-.1f);
                    yield return MvpInteractionE("isolate_"+incident.room,approach,DangerColliderAim("isolate_"+incident.room),
                        HotelDangerRules.WorkSeconds(s,"isolate_"+incident.room),()=>incident.isolated,"NETWORK_HOST_REAL_E_ISOLATES");
                    if(finished)yield break;
                    game.LookAtForTest(partner.position+Vector3.up*.35f);yield return null;CaptureFeedback("danger-remote-downed");
                    Vector3 near=partner.position+new Vector3(incident.room%2==0?-.5f:.5f,0,1.25f);near.y=.1f;
                    int kits=s.danger.medkits;
                    yield return MvpInteractionE("rescue_1",near,DangerColliderAim("rescue_1"),
                        HotelDangerRules.WorkSeconds(s,"rescue_1"),()=>partner.life=="healthy","NETWORK_HOST_REAL_E_RESCUES_REMOTE","danger-rescue-near");
                    if(finished)yield break;
                    if(!Check(s.danger.medkits==kits-1&&partner.health>0&&partner.health<100,"Rescue resource/HP incorrect"))yield break;
                    float savedHealth=partner.health;
                    if(reconnect)
                    {
                        until=Time.realtimeSinceStartup+25;
                        while(!s.players.Exists(p=>p.id!=0&&p.id!=firstId)&&Time.realtimeSinceStartup<until)yield return null;
                        if(!Check(s.players.Exists(p=>p.id!=0&&p.id!=firstId)&&partner.downs==1&&Math.Abs(partner.health-savedHealth)<.01f,
                            "Reconnect produced new healthy crew instead of restoring the same slot"))yield break;
                        checks.Add("NEW_CONNECTION_ID_REBINDS_DAMAGED_CREW_WITHOUT_HEALING");
                    }
                    else {yield return new WaitForSecondsRealtime(2);checks.Add("IMPAIRED_RESCUE_SNAPSHOT_SETTLED_RECONNECT_TESTED_SEPARATELY");}
                    if(!MvpInteractionFixture("NETWORK_VALIDATE_AND_SAVE",()=>
                    {
                        HotelSaveStore.Validate(s);
                        if(!game.Session.Save())throw new Exception("Network injury save failed");
                        var loaded=HotelSaveStore.Load(game.Session.SavePath);
                        if(loaded.players.Count!=0||HotelDangerRules.Crew(loaded,1).downs!=1||Math.Abs(HotelDangerRules.Crew(loaded,1).health-savedHealth)>.01f)
                            throw new Exception("Save/load lost durable injury when releasing connection objects");
                    }))yield break;
                    until=Time.realtimeSinceStartup+3;
                    while(s.players.Count>1&&Time.realtimeSinceStartup<until){s.notice="DANGER_NETWORK_DONE";yield return null;}
                    checks.Add("NETWORK_INJURY_RESCUE_AND_DURABLE_SAVE_PASSED");
                }
                else
                {
                    float until=Time.realtimeSinceStartup+12;
                    while(game.Session.State.notice!="DANGER_AUTH_READY"&&Time.realtimeSinceStartup<until)yield return null;
                    if(!Check(game.Session.State.phase=="preparation"&&game.Session.State.notice=="DANGER_AUTH_READY","Missing authority setup"))yield break;
                    var board=HotelLayout.Target("board");board.y=.1f;game.Teleport(board);
                    yield return new WaitForSecondsRealtime(.4f);
                    game.Session.Send(new HotelCommand("dangerMode","relief"));
                    yield return new WaitForSecondsRealtime(.7f);
                    if(!Check(game.Session.State.phase=="preparation"&&game.Session.State.danger.mode=="standard"&&game.Toast.Contains("хозяин"),
                        "Client preparation mode must be rejected specifically by host authority"))yield break;
                    game.Session.Send(new HotelCommand("ping","board"));
                    checks.Add("CLIENT_MODE_REJECTED_SPECIFICALLY_BY_HOST_AUTHORITY_IN_PREPARATION");
                    until=Time.realtimeSinceStartup+35;
                    while(HotelDangerRules.Crew(game.Session.State,game.Session.LocalId)?.life!="downed"&&Time.realtimeSinceStartup<until)yield return null;
                    if(!Check(HotelDangerRules.Crew(game.Session.State,game.Session.LocalId)?.life=="downed","Client did not receive authoritative downed state"))yield break;
                    checks.Add("REMOTE_DAMAGE_AND_DOWNED_STATE_REPLICATED");
                    yield return CapturePanels(1280,"danger-client-downed-",new[]{""});
                    until=Time.realtimeSinceStartup+25;
                    while(HotelDangerRules.Crew(game.Session.State,game.Session.LocalId)?.life!="healthy"&&Time.realtimeSinceStartup<until)yield return null;
                    var crew=HotelDangerRules.Crew(game.Session.State,game.Session.LocalId);
                    if(!Check(crew!=null&&crew.life=="healthy"&&crew.downs==1&&crew.health<100,"Client rescue snapshot missing"))yield break;
                    float health=crew.health;
                    int kits=game.Session.State.danger.medkits;
                    string mode=game.Session.State.danger.mode;
                    game.Session.Send(new HotelCommand("dangerMode","relief"));
                    yield return new WaitForSecondsRealtime(.7f);
                    if(!Check(game.Session.State.danger.mode==mode,"Client changed locked host contract"))yield break;
                    checks.Add("RESCUE_REPLICATED_CLIENT_CONTRACT_CHANGE_REJECTED");
                    if(reconnect)
                    {
                        ulong oldId=game.Session.LocalId;
                        string[] args=Environment.GetCommandLineArgs();int portIndex=Array.IndexOf(args,"-whe-port");ushort port=17794;
                        if(portIndex>=0&&portIndex+1<args.Length)ushort.TryParse(args[portIndex+1],out port);
                        if(!Check(game.Session.Disconnect(),"Client cannot disconnect"))yield break;
                        yield return new WaitForSecondsRealtime(.7f);
                        game.Session.Join("127.0.0.1",port,"");
                        until=Time.realtimeSinceStartup+20;
                        while(!game.Playing&&Time.realtimeSinceStartup<until)yield return null;
                        crew=game.Playing?HotelDangerRules.Crew(game.Session.State,game.Session.LocalId):null;
                        if(!Check(game.Playing&&game.Session.LocalId!=oldId&&crew!=null&&crew.life=="healthy"&&crew.downs==1&&
                            Math.Abs(crew.health-health)<.01f&&game.Session.State.danger.medkits==kits,"Rejoining changed injury/resources or did not create a new connection"))yield break;
                        checks.Add("ACTUAL_DISCONNECT_REJOIN_NEW_ID_PRESERVES_INJURY_AND_MEDKITS");
                    }
                    until=Time.realtimeSinceStartup+15;
                    while(game.Playing&&game.Session.State.notice!="DANGER_NETWORK_DONE"&&Time.realtimeSinceStartup<until)yield return null;
                    if(!Check(game.Playing&&game.Session.State.notice=="DANGER_NETWORK_DONE","Host did not confirm durable save"))yield break;
                    checks.Add("HOST_CONFIRMED_COOPERATIVE_SAVE");
                }
            }
            finally {MvpInteractionRelease();InputSystem.settings.backgroundBehavior=background;}
        }
    }
}
