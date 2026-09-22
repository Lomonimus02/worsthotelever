using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class HotelSessionSmokeTest
    {
        // Opt-in test only. Arrival/position/incident setup and fast-forward are labelled fixtures;
        // damage, downing, E work, isolation, self-help and medical use run in real player time.
        IEnumerator ReviewDangerInteraction()
        {
            if (!Check(game.Session.IsHost && game.Session.State.version == 3 && HotelDangerRules.Enabled(game.Session.State), "Production did not create danger v3")) yield break;
            var background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.EnableDevice(Keyboard.current);
            try
            {
                var s = game.Session.State;
                checks.Add("PRODUCTION_V3_DEFAULT_FACTORY");
                foreach (int width in new[] {1280,960}) yield return CapturePanels(width,"danger-prep-",new[]{"management","briefing",""});
                if (!MvpInteractionFixture("DANGER_BOLD_CONTRACT_AND_TWO_SERVICE_GUESTS", () =>
                {
                    FixtureAt("board"); FixtureCommand("dangerMode","bold");
                    foreach (var room in s.rooms) if(room.mvp.owned) room.bed=2; // Room readiness is fixture setup.
                    foreach (var incident in s.danger.incidents) incident.triggerAt=1000;
                    FixtureAt("desk"); FixtureCommand("open"); FixtureCommand("endGuidedOpening");
                    for(int index=0;index<2;index++)
                    {
                        var guest=s.guests.Find(g=>g.stage=="queue");
                        if(guest==null)
                        {
                            var entry=s.mvp.schedule.Find(e=>e.day==s.day&&e.status=="planned");
                            if(entry==null)throw new Exception("Fixture has no second scheduled guest");
                            entry.time=s.time; game.Session.Simulation.Tick(.1f);
                            guest=s.guests.Find(g=>g.stage=="queue");
                        }
                        if(guest==null)throw new Exception("Fixture guest did not arrive");
                        var room=s.rooms.Find(r=>HotelHospitalityRules.AssignmentBlockReason(s,guest.id,r.number)=="");
                        if(room==null)throw new Exception("Fixture has no compatible prepared room");
                        FixtureAt("desk");
                        var error=game.Session.Simulation.Execute(0,new HotelCommand("checkin","",room.number){guestId=guest.id});
                        if(error!="")throw new Exception(error);
                        var bag=s.items.Find(i=>i.kind=="bag"&&i.ownerGuest==guest.id&&!i.consumed);
                        MvpInteractionPose(new Vector3(bag.position.x,.1f,bag.position.z));
                        FixtureCommand("pickup",bag.id);FixtureAt("bag_"+room.number);FixtureCommand("interact","bag_"+room.number);
                    }
                    if(s.danger.servicePoints<4)throw new Exception("Genuine checkin/luggage commands did not earn service points");
                    HotelSaveStore.Validate(s);
                })) yield break;

                for(int i=0;i<s.danger.requiredIncidents;i++)
                {
                    var h=s.danger.incidents[i];
                    if(!MvpInteractionFixture("SHORTEN_WARNING_AND_SCHEDULE_"+h.kind,()=>
                    {
                        MvpInteractionPose(HotelLayout.Spawn);
                        h.triggerAt=s.danger.elapsed;s.danger.graceUntil=s.danger.elapsed;
                    }))yield break;
                    float until=Time.realtimeSinceStartup+3;
                    while(h.status=="planned"&&Time.realtimeSinceStartup<until)yield return null;
                    if(!Check(h.status=="warning","Scheduled incident never entered warning"))yield break;
                    yield return CapturePanels(1280,"danger-warning-"+h.kind+"-",new[]{"","tasks"});
                    h.warningRemaining=.1f; // No work or damage is accelerated by this warning fixture.
                    until=Time.realtimeSinceStartup+2;
                    while(h.status!="active"&&Time.realtimeSinceStartup<until)yield return null;
                    if(!Check(h.status=="active","Warning did not become a real active hazard"))yield break;
                    checks.Add("WARNING_TO_ACTIVE_"+h.kind);
                    if(!MvpInteractionFixture("ACTIVE_ZONE_CAMERA_"+h.kind,()=>
                    {
                        Vector3 at=HotelDangerRules.Source(h)+(h.kind=="electric"?Vector3.back*1.4f:
                            h.kind=="steam"?new Vector3(h.room%2==0?-1.05f:1.05f,0,1.45f):Vector3.forward*1.45f);
                        at.y=.1f;MvpInteractionPose(at);game.LookAtForTest(DangerColliderAim("hazard_"+h.room));
                    }))yield break;
                    yield return null;CaptureFeedback("danger-active-"+h.kind);

                    if(i==0)
                    {
                        var crew=HotelDangerRules.Crew(s,0);
                        if(!MvpInteractionFixture("LOW_HEALTH_EXPOSURE_POSITION",()=>
                        {
                            crew.health=5;crew.shield=0;
                            Vector3 source=DangerExposurePosition(h);
                            MvpInteractionPose(source);
                            game.LookAtForTest(HotelLayout.RoomCenter(h.room)+Vector3.up);
                        }))yield break;
                        game.OpenPanel("tasks");
                        until=Time.realtimeSinceStartup+10;
                        while(crew.life=="healthy"&&Time.realtimeSinceStartup<until)yield return null;
                        if(!Check(crew.life=="downed"&&crew.health==0&&game.Held==null,"Live danger did not knock down player with a panel open"))yield break;
                        checks.Add("REAL_DAMAGE_WITH_OPEN_PANEL_DOWNS_AND_RELEASES_PLAYER");
                        foreach(int width in new[]{1280,960})yield return CapturePanels(width,"danger-downed-",new[]{"","pause"});
                        game.OpenPanel("");MvpInteractionRelease();yield return null;
                        Vector3 at=game.LocalPlayer.position;
                        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.W));
                        yield return new WaitForSecondsRealtime(.3f);MvpInteractionRelease();yield return null;
                        if(!Check(Vector3.Distance(at,game.LocalPlayer.position)<.05f,"Downed movement was accepted"))yield break;
                        int kits=s.danger.medkits, self=s.danger.selfRescues;
                        yield return DangerHoldVirtualRecovery(crew);
                        if(finished)yield break;
                        if(!Check(s.danger.medkits==kits-1&&s.danger.selfRescues==self-1&&crew.health>0,"Self recovery did not consume exactly one of both resources"))yield break;
                    }

                    if(!MvpInteractionTool(""))yield break;
                    Vector3 control=HotelDangerRules.Control(h.room);
                    Vector3 approach=new Vector3(h.room%2==0?.65f:-.65f,.1f,HotelLayout.RoomCenter(h.room).z-.1f);
                    yield return MvpInteractionE("isolate_"+h.room,approach,DangerColliderAim("isolate_"+h.room),
                        HotelDangerRules.WorkSeconds(s,"isolate_"+h.room),()=>h.isolated,"REAL_E_ISOLATES_"+h.kind);
                    if(finished)yield break;
                    var hostCrew=HotelDangerRules.Crew(s,0);
                    float health=hostCrew.health;
                    if(!MvpInteractionFixture("ISOLATED_SOURCE_EXPOSURE",()=>
                    {
                        Vector3 at=HotelDangerRules.Source(h);at.y=.1f;MvpInteractionPose(at);
                    }))yield break;
                    yield return new WaitForSecondsRealtime(.6f);
                    if(!Check(Math.Abs(hostCrew.health-health)<.01f,"Isolated source still hurt employee"))yield break;
                    if(!MvpInteractionTool(h.kind=="fumes"?"mop":"toolbox"))yield break;
                    Vector3 sourcePosition=HotelDangerRules.Source(h);
                    Vector3 repairFloor=sourcePosition + (h.kind=="electric" ? new Vector3(0,0,-1.4f) :
                        h.kind=="steam" ? new Vector3(h.room%2==0?-1.05f:1.05f,0,1.45f) : new Vector3(0,0,1.45f));repairFloor.y=.1f;
                    yield return MvpInteractionE("hazard_"+h.room,repairFloor,DangerColliderAim("hazard_"+h.room),
                        HotelDangerRules.WorkSeconds(s,"hazard_"+h.room),()=>h.status=="resolved","REAL_E_REPAIRS_"+h.kind,"danger-repair-"+h.kind);
                    if(finished)yield break;
                    if(!MvpInteractionTool(""))yield break;
                    if(i==0)
                    {
                        int kits=s.danger.medkits;
                        yield return MvpInteractionE("firstaid",new Vector3(-5.6f,.1f,-3.3f),DangerColliderAim("firstaid"),
                            HotelDangerRules.WorkSeconds(s,"firstaid"),()=>hostCrew.health>=99.9f,"REAL_E_FIRSTAID_HEALS");
                        if(finished)yield break;
                        if(!Check(s.danger.medkits==kits-1,"Firstaid did not consume exactly one medkit"))yield break;
                    }
                }

                if(!MvpInteractionFixture("ACCELERATE_ONLY_REMAINING_CONTRACT_MINIMUM_THEN_FINISH",()=>
                {
                    FixtureAdvance(Mathf.Max(0,s.danger.minimumSeconds-s.danger.elapsed)+.3f);
                    FixtureAt("desk");FixtureCommand("finish");HotelSaveStore.Validate(s);
                }))yield break;
                if(!Check(s.phase=="summary"&&s.danger.status=="won"&&s.danger.settled&&s.danger.paidReward>0,"Completed contract did not win/pay"))yield break;
                int cash=s.cash, wins=s.danger.wins;
                string duplicate=game.Session.Simulation.Execute(0,new HotelCommand("finish"));
                if(!Check(duplicate!=""&&s.cash==cash&&s.danger.wins==wins,"Terminal finish paid twice"))yield break;
                foreach(int width in new[]{1280,960})yield return CapturePanels(width,"danger-won-",new[]{"summary"});
                if(!Check(game.Session.Save(),"Won contract could not save"))yield break;
                if(!MvpInteractionFixture("NEXT_DAY_AFTER_WON",()=>
                {
                    FixtureCommand("nextday");FixtureAt("desk");FixtureCommand("open");HotelSaveStore.Validate(s);
                }))yield break;
                yield return MvpInteractionE("alarm",new Vector3(.4f,.1f,-5.4f),DangerColliderAim("alarm"),
                    HotelDangerRules.WorkSeconds(s,"alarm"),()=>s.phase=="summary","REAL_E_EMERGENCY_EVACUATION");
                if(finished)yield break;
                if(!Check(s.danger.status=="failed"&&s.danger.paidReward==0&&s.danger.chargedPenalty>0,"Evacuation awarded a win or forgot its recovery cost"))yield break;
                foreach(int width in new[]{1280,960})yield return CapturePanels(width,"danger-failed-",new[]{"summary"});
                if(!MvpInteractionFixture("CONTINUE_RECOVERABLE_FAILURE",()=>{FixtureCommand("nextday");HotelSaveStore.Validate(s);}))yield break;
                if(!Check(s.phase=="preparation"&&HotelDangerRules.Crew(s,0).life=="healthy","Cannot recover after failed shift"))yield break;
                checks.Add("SUCCESS_FAILURE_AND_RECOVERY_ONE_PERSISTENT_HOTEL");
            }
            finally {MvpInteractionRelease();InputSystem.settings.backgroundBehavior=background;}
        }

        Vector3 DangerColliderAim(string target)
        {
            foreach(var collider in game.World.GetComponentsInChildren<Collider>())
            {
                var tag=collider.GetComponentInParent<HotelTarget>();
                if(collider.enabled&&!collider.isTrigger&&tag!=null&&tag.id==target)return collider.bounds.center;
            }
            throw new Exception("Missing physical danger target "+target);
        }

        Vector3 DangerExposurePosition(HotelIncidentState incident)
        {
            // Stand beside the source, never inside the sink/bin's solid geometry.
            Vector3 point=HotelDangerRules.Source(incident)+(incident.kind=="steam"?Vector3.forward*.85f:
                incident.kind=="electric"?Vector3.back*.7f:Vector3.forward*(HotelDangerRules.Radius(incident)-.05f));
            point.y=.1f;return point;
        }

        IEnumerator DangerHoldVirtualRecovery(HotelCrewState crew)
        {
            game.OpenPanel("");MvpInteractionRelease();yield return null;yield return null;
            if(!Check(game.FocusId=="recover_"+crew.slot,"Downed employee has no self-help focus"))yield break;
            float started=Time.realtimeSinceStartup;
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.E));
            try {while(crew.life=="downed"&&Time.realtimeSinceStartup-started<8)yield return null;}
            finally {MvpInteractionRelease();}
            yield return null;
            if(!Check(crew.life=="healthy"&&Time.realtimeSinceStartup-started>=5.7f,"Self-help did not require actual held E"))yield break;
            checks.Add("REAL_E_SELF_RECOVERY_6_SECONDS");
        }

        IEnumerator ReviewDangerForfeit()
        {
            var s=game.Session.State;
            if(!Check(game.Session.IsHost&&HotelDangerRules.Enabled(s),"Forfeit fixture requires production host"))yield break;
            if(!MvpInteractionFixture("CRITICAL_WINDOW_UI_SETUP",()=>
            {
                FixtureAt("desk");FixtureCommand("open");FixtureCommand("endGuidedOpening");
                foreach(var h in s.danger.incidents)h.triggerAt=10000;
                s.danger.safety=0;s.danger.criticalRemaining=30;
            }))yield break;
            foreach(int width in new[]{1280,960})yield return CapturePanels(width,"danger-critical-",new[]{"","tasks"});
            if(!MvpInteractionFixture("DOWNED_HOST_OFFLINE_HEALTHY_PARTNER_SETUP",()=>
            {
                game.Session.Simulation.Join(99);game.Session.Simulation.Leave(99);
                s.danger.safety=100;s.danger.criticalRemaining=30;
                var c=HotelDangerRules.Crew(s,0);c.health=0;c.life="downed";c.bleedout=.5f;c.downs=1;
                HotelSaveStore.Validate(s);
            }))yield break;
            float until=Time.realtimeSinceStartup+3;
            while(HotelDangerRules.Crew(s,0).life!="dead"&&Time.realtimeSinceStartup<until)yield return null;
            if(!Check(HotelDangerRules.Crew(s,0).life=="dead"&&HotelDangerRules.HostCanForfeit(s)&&s.danger.status=="active",
                "Offline partner fixture did not reproduce dead-host recovery choice"))yield break;
            checks.Add("REAL_BLEEDOUT_WITH_OFFLINE_PARTNER_EXPOSES_EXPLICIT_FORFEIT");
            foreach(int width in new[]{1280,960})yield return CapturePanels(width,"danger-dead-",new[]{"","pause","abandon-confirm"});
            int before=s.cash;
            if(!MvpInteractionFixture("EXPLICIT_HOST_FORFEIT",()=>FixtureCommand("abandonDanger")))yield break;
            if(!Check(s.phase=="summary"&&s.danger.status=="failed"&&s.cash==before-s.danger.penalty&&HotelDangerRules.Crew(s,1).health==100,
                "Forfeit did not settle exactly one loss or injured offline partner"))yield break;
            foreach(int width in new[]{1280,960})yield return CapturePanels(width,"danger-dead-failed-",new[]{"summary"});
            if(!MvpInteractionFixture("DEAD_HOST_NEXT_DAY",()=>{FixtureCommand("nextday");HotelSaveStore.Validate(s);}))yield break;
            if(!Check(HotelDangerRules.Crew(s,0).health==100&&s.phase=="preparation","Dead host could not recover hotel"))yield break;
            checks.Add("DEAD_HOST_FORFEIT_AND_NEXT_DAY_PRESERVE_HOTEL");
        }

        IEnumerator ReviewDangerCheckpoint()
        {
            var s=game.Session.State;
            if(!Check(game.Session.IsHost&&s.version==3&&HotelDangerRules.Enabled(s),"Checkpoint did not load production v3"))yield break;
            if(scenario=="danger-checkpoint-write")
            {
                if(!MvpInteractionFixture("DOWNED_CHECKPOINT_SETUP",()=>
                {
                    FixtureAt("desk");FixtureCommand("open");FixtureCommand("endGuidedOpening");
                    var crew=HotelDangerRules.Crew(s,0);crew.health=0;crew.life="downed";crew.bleedout=40;crew.shield=0;crew.downs=1;
                    foreach(var h in s.danger.incidents)h.triggerAt=1000;
                    HotelSaveStore.Validate(s);
                }))yield break;
                if(!Check(game.Session.Save(),"Downed checkpoint save failed"))yield break;
                checks.Add("DOWNED_CHECKPOINT_WRITTEN world="+s.worldId);
            }
            else
            {
                var crew=HotelDangerRules.Crew(s,0);
                if(!Check(crew.life=="downed"&&crew.health==0&&crew.downs==1&&crew.bleedout>30&&crew.bleedout<=40&&s.phase=="open", "Restart healed/reset injury or lost active shift"))yield break;
                var before=crew.position;
                string error=game.Session.Simulation.Execute(0,new HotelCommand("pose"){position=HotelLayout.Spawn+Vector3.right});
                if(!Check(error!=""&&crew.position==before,"Restart bypassed incapacitated authority"))yield break;
                checks.Add("FRESH_PROCESS_RESTORES_DOWNED_HOST_AND_REJECTS_MOVEMENT world="+s.worldId);
                foreach(int width in new[]{1280,960})yield return CapturePanels(width,"danger-restart-",new[]{""});
            }
        }
    }
}
