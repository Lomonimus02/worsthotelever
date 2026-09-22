using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public static class HotelMvpLifecycleTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            var legacy = new HotelSimulation();
            HotelState root = legacy.State;
            string world = root.worldId;
            root.cash = 321; root.linenStock = 7; root.towelStock = 8;
            root.rooms[0].leak = true; root.rooms[0].water = .35f;
            root.ledger.Add("Preserved journal entry");
            var promoted = HotelSimulation.ResumeForPlay(root);
            Require(ReferenceEquals(root, promoted.State), "Promotion replaced the host state root");
            Require(root.version == 2 && root.contentVersion == 2 && root.rooms.Count == 6, "Promotion schema/count");
            Require(root.worldId == world && root.cash == 321 && root.linenStock == 7 && root.towelStock == 8, "Promotion changed account or inventories");
            Require(root.rooms[0].leak && root.rooms[0].water == .35f && root.rooms[0].mvp.equipment.Find(e => e.kind == "sink").localFault, "Promotion erased existing damage");
            Require(root.ledger.Contains("Preserved journal entry") && root.items.Exists(i => i.kind == "plunger"), "Promotion lost history or essential tool");
            Require(root.rooms.Find(r => r.number == 105).mvp.owned == false && root.rooms.Find(r => r.number == 106).mvp.owned == false, "Expansion granted for free");
            HotelSaveStore.Validate(root);
            passed.Add("MVP preparation promotion preserves root identity, cash, supplies, damage and history; adds locked expansion");

            legacy = new HotelSimulation(); legacy.Join(0); At(legacy, "desk");
            Ok(legacy, new HotelCommand("endGuidedOpening")); Ok(legacy, new HotelCommand("open"));
            legacy.Tick(20);
            root = legacy.State; world = root.worldId;
            int arrivals = root.arrivals; float time = root.time;
            var resumed = HotelSimulation.ResumeForPlay(root);
            Require(root.mvp == null && root.time == time && root.arrivals == arrivals && root.guests.Count == arrivals, "Resume rewrote an active legacy shift");
            At(resumed, "desk"); Ok(resumed, new HotelCommand("finish"));
            Require(root.mvp == null && root.phase == "summary", "Summary promoted before transition");
            Ok(resumed, new HotelCommand("nextday"));
            Require(root.mvp != null && root.day == 2 && root.worldId == world && ReferenceEquals(root, resumed.State), "Legacy next-day did not promote in place");
            HotelSaveStore.Validate(root);
            passed.Add("Active legacy shift keeps its contracts and promotes only after explicit next-day transition");

            var sim = HotelSimulation.CreateNewMvp(237); sim.Join(0); root = sim.State;
            int random = root.mvp.rngState, draws = root.mvp.rngDraws;
            string schedule = JsonUtility.ToJson(root.mvp);
            var restored = HotelSimulation.ResumeForPlay(JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(root)));
            Require(restored.State.mvp.rngState == random && restored.State.mvp.rngDraws == draws && JsonUtility.ToJson(restored.State.mvp) == schedule, "Loading rerolled demand or changed saved director");
            int cash = root.cash, day = root.day; sim.Tick(30);
            Require(root.time == 0 && root.mvp.elapsed == 0 && root.cash == cash && root.day == day, "Preparation advances clock/economy");
            Require(sim.Execute(0, new HotelCommand("nextday")) != "" && root.cash == cash, "Repeated preparation charged money");
            passed.Add("MVP resume is deterministic and preparation freezes guest clocks and one-time economy");

            Require(sim.Execute(0, new HotelCommand("pose") { position = HotelLayout.RoomCenter(105) }) != "", "Unowned room accepts remote pose");
            Require(sim.Execute(0, new HotelCommand("ping", "invented_target")) != "", "Unknown ping accepted");
            Ok(sim, new HotelCommand("ping", "desk"));
            Ok(sim, new HotelCommand("ping", "board"));
            Require(root.mvp.pings.Count == 1 && root.mvp.pings[0].target == "board", "One employee floods ping list");
            sim.Tick(7); Require(root.mvp.pings.Count == 0 && root.mvp.elapsed == 0, "Preparation marker never expires");
            passed.Add("Unowned geometry is authority-guarded; validated per-player pings expire even during preparation");

            legacy = new HotelSimulation(); legacy.State.day = 2; legacy.State.cash = 500;
            foreach(var room in legacy.State.rooms)room.outOfService = true;
            restored = HotelSimulation.ResumeForPlay(legacy.State); restored.Join(0);
            Require(restored.State.mvp.nextId == 1, "Closed-hotel fixture unexpectedly allocated demand IDs");
            At(restored, "board"); Ok(restored, new HotelCommand("upgrade", "toolbox"));
            Require(restored.State.items.FindAll(i => i.kind == "toolbox").Count == 2, "Second toolbox missing");
            Require(new HashSet<string>(restored.State.items.ConvertAll(i => i.id)).Count == restored.State.items.Count, "Migrated item ID collided");
            HotelSaveStore.Validate(restored.State);
            string slot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WorstHotel-MigrationId-" + Guid.NewGuid().ToString("N") + ".json");
            try { HotelSaveStore.Save(restored.State, slot); HotelSaveStore.Validate(HotelSaveStore.Load(slot)); }
            finally { if(System.IO.File.Exists(slot))System.IO.File.Delete(slot); if(System.IO.File.Exists(slot+".bak"))System.IO.File.Delete(slot+".bak"); }
            passed.Add("Legacy closed hotel with untouched ID counter buys a second unique toolbox and saves/loads");

            legacy = new HotelSimulation(); legacy.State.day = 2;
            for(int i=0;i<1000;i++)legacy.State.items.Add(new ItemState {id="migration_surplus_"+i,kind="trashbag",position=new Vector3(-5,.2f,-3)});
            HotelSaveStore.Validate(legacy.State); world = legacy.State.worldId;
            restored = HotelSimulation.ResumeForPlay(legacy.State);
            Require(restored.State.mvp == null && restored.State.version == 1 && restored.State.items.Count == 1002 && restored.State.worldId == world,
                "Oversized valid legacy preparation was lost or partially promoted");
            HotelSaveStore.Validate(restored.State);
            restored.State.items.RemoveAll(i => i.id.StartsWith("migration_surplus_", StringComparison.Ordinal));
            restored = HotelSimulation.ResumeForPlay(restored.State);
            Require(restored.State.mvp != null && restored.State.worldId == world, "Cleaned legacy hotel cannot retry migration");
            HotelSaveStore.Validate(restored.State);
            passed.Add("Oversized valid legacy preparation remains playable without deleting inventory; cleaned snapshot promotes on retry");
            return passed;
        }
        static void At(HotelSimulation sim, string target)
        {
            Vector3 point = HotelLayout.Target(target);
            point.y = .1f;
            Ok(sim, new HotelCommand("pose") { position = point });
        }
        static void Ok(HotelSimulation sim, HotelCommand command)
        {
            string error = sim.Execute(0, command);
            if (error != "") throw new Exception(command.action + "/" + command.target + ": " + error);
        }
        static void Require(bool value, string text) { if (!value) throw new Exception(text); }
    }
}
