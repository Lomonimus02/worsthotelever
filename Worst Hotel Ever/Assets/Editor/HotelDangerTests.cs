using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace WorstHotel
{
    // Native Unity runner only. Actor positions are intentionally teleported by real pose
    // commands; elapsed time is advanced through Tick, never by writing outcomes/rewards.
    public static class HotelDangerTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "Danger: valid forecast, pure rules, canonical targets and immutable open mode", Forecast);
            Run(passed, "Danger: preparation and guided lesson freeze threat clocks", Guided);
            Run(passed, "Danger: warnings precede damage; doorway, wall and ordinary water safety", WarningAndGeometry);
            Run(passed, "Danger: fumes are slower than steam and grow within the room shell", Fumes);
            Run(passed, "Danger: all three sources require isolation and the correct tool", Counterplay);
            Run(passed, "Danger: standard/bold concurrency, relief and post-repair grace", PressureBudget);
            Run(passed, "Danger: repair can recover the final safety window", CriticalRecovery);
            Run(passed, "Danger: collapse is terminal once and investments survive debt recovery", Collapse);
            Run(passed, "Danger: down drops the item/cancels work; self-help is finite; wipe is irreversible", DownSelfAndWipe);
            Run(passed, "Danger: rescue/self-help are mutually exclusive; death cannot be revived", Rescue);
            Run(passed, "Danger: first aid is finite and cannot waste kits at full health", MedicalSupplies);
            Run(passed, "Danger: disconnected and reloaded partner keeps injury, body and bleedout", Reconnect);
            Run(passed, "Danger: host forfeiture is a pure life/connection/phase gate", ForfeitGate);
            Run(passed, "Danger: dead host can forfeit once after partner repair, disconnect and disk reload", ForfeitDisconnected);
            Run(passed, "Danger: next preparation clears danger pings without clearing same-day or ordinary pings", PreparationPings);
            Run(passed, "Danger: factual service and minimum time gate a single contract reward", Victory);
            Run(passed, "Danger: evacuation cancels unpaid stays, preserves future bookings and group accounting", Evacuation);
            Run(passed, "Danger: incomplete closing is a failure, relief is not a contract victory", IncompleteAndRelief);
            return passed;
        }
        private static void Run(List<string> passed, string name, Action test)
        {
            try { test(); passed.Add(name); }
            catch (Exception ex) { throw new Exception("HotelDangerTests FAILED: " + name, ex.GetBaseException()); }
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Same(float a, float b, string message) { Check(Math.Abs(a - b) < .002f, message + ": " + a + " != " + b); }
        private static PlayerState Player(HotelSimulation sim, ulong id = 0) { return sim.State.players.Find(p => p.id == id); }
        private static HotelCrewState Crew(HotelSimulation sim, ulong id = 0) { return HotelDangerRules.Crew(sim.State, id); }
        private static RoomState Room(HotelSimulation sim, int number) { return sim.State.rooms.Find(r => r.number == number); }
        private static HotelSimulation New(string mode = "standard", bool pair = false, int seed = 8819, bool guide = false)
        {
            HotelSimulation sim = HotelSimulation.CreateNewDanger(seed);
            sim.Join(0); if (pair) sim.Join(27);
            if (!guide) Ok(sim, "endGuidedOpening");
            At(sim, "board"); Ok(sim, "dangerMode", mode);
            HotelSaveStore.Validate(sim.State);
            return sim;
        }
        private static HotelSimulation FirstKind(string kind, bool pair = false)
        {
            for (int seed = 1; seed <= 64; seed++)
            {
                HotelSimulation sim = New(pair: pair, seed: seed);
                if (sim.State.danger.incidents[0].kind == kind) return sim;
            }
            throw new Exception("Could not find deterministic " + kind + " forecast fixture");
        }
        private static void Ok(HotelSimulation sim, string action, string target = "", int number = 0, ulong id = 0, int guest = 0)
        {
            string error = sim.Execute(id, new HotelCommand(action, target, number) { guestId = guest });
            Check(error == "", action + " " + target + ": " + error);
            HotelSaveStore.Validate(sim.State);
        }
        private static void No(HotelSimulation sim, string action, string target = "", int number = 0, ulong id = 0)
        {
            string before = JsonUtility.ToJson(sim.State);
            Check(sim.Execute(id, new HotelCommand(action, target, number)) != "", "Expected rejection: " + action + " " + target);
            Check(before == JsonUtility.ToJson(sim.State), "Rejected command changed the snapshot: " + action + " " + target);
        }
        private static void At(HotelSimulation sim, Vector3 position, ulong id = 0)
        {
            position.y = .1f;
            Check(sim.Execute(id, new HotelCommand("pose") { position = position }) == "", "Pose rejected for " + id + ": " + position);
        }
        private static void At(HotelSimulation sim, string target, ulong id = 0)
        {
            Vector3 position;
            if (!HotelDangerRules.TryTarget(sim.State, target, out position)) position = HotelLayout.Target(target);
            At(sim, position, id);
        }
        private static void Open(HotelSimulation sim) { At(sim, "desk"); Ok(sim, "open"); }
        private static void Advance(HotelSimulation sim, float seconds)
        {
            for (float t = 0; t < seconds - .0001f; t += .1f) sim.Tick(Mathf.Min(.1f, seconds - t));
        }
        private static void Until(HotelSimulation sim, Func<bool> condition, float limit, string reason)
        {
            for (float t = 0; !condition() && t < limit; t += .1f) sim.Tick(.1f);
            Check(condition(), reason);
        }
        private static void Continue(HotelSimulation sim, float limit = 9, ulong id = 0)
        {
            for (float t = 0; t < limit && Player(sim, id).workTarget != ""; t += .1f)
            {
                Ok(sim, "heartbeat", Player(sim, id).workTarget, id: id);
                sim.Tick(.1f);
            }
            HotelSaveStore.Validate(sim.State);
        }
        private static void Work(HotelSimulation sim, string target, ulong id = 0)
        {
            if (!target.StartsWith("recover_", StringComparison.Ordinal)) At(sim, target, id);
            Ok(sim, "beginwork", target, id: id); Continue(sim, id: id);
            Check(Player(sim, id).workTarget == "", "Unfinished work: " + target);
        }
        private static void Pick(HotelSimulation sim, string kind, ulong id = 0)
        {
            ItemState item = sim.State.items.Find(i => !i.consumed && i.kind == kind && i.holder == -1);
            Check(item != null, "Missing free " + kind); At(sim, item.position, id); Ok(sim, "pickup", item.id, id: id);
        }
        private static void Drop(HotelSimulation sim, ulong id = 0)
        {
            if (Player(sim, id).held == "") return;
            Check(sim.Execute(id, new HotelCommand("drop") { position = Player(sim, id).position }) == "", "Drop rejected");
            HotelSaveStore.Validate(sim.State);
        }
        private static void Warn(HotelSimulation sim, HotelIncidentState incident)
        { Until(sim, () => incident.status == "warning" || incident.status == "active", 280, "Incident did not arrive"); }
        private static void Repair(HotelSimulation sim, HotelIncidentState incident, ulong id = 0)
        {
            Drop(sim, id); Warn(sim, incident);
            Work(sim, "isolate_" + incident.room, id);
            Pick(sim, incident.kind == "fumes" ? "mop" : "toolbox", id);
            Work(sim, "hazard_" + incident.room, id); Drop(sim, id);
        }
        private static object Hook(HotelSimulation sim, string name, params object[] args)
        {
            MethodInfo method = typeof(HotelSimulation).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Check(method != null, "Missing integration hook " + name);
            return method.Invoke(sim, args);
        }
        private static GuestState Serve(HotelSimulation sim, GuestState guest, int room)
        {
            At(sim, "desk"); Ok(sim, "checkin", number: room, guest: guest.id);
            ItemState bag = sim.State.items.Find(i => !i.consumed && i.kind == "bag" && i.ownerGuest == guest.id);
            Check(bag != null, "Arrived guest has no physical luggage");
            At(sim, bag.position); Ok(sim, "pickup", bag.id);
            At(sim, "bag_" + room); Ok(sim, "interact", "bag_" + room);
            return guest;
        }
        private static void Prepare102(HotelSimulation sim)
        {
            WorkOrdinary(sim, "bed_102"); At(sim, "hamper"); Ok(sim, "interact", "hamper");
            At(sim, "linen"); Ok(sim, "interact", "linen"); WorkOrdinary(sim, "bed_102");
        }
        private static void WorkOrdinary(HotelSimulation sim, string target) { Work(sim, target); }

        private static void Forecast()
        {
            HotelSimulation sim = New(pair: true); HotelDangerState d = sim.State.danger;
            Check(d.crew.Count == 2 && d.crew.Exists(c => c.slot == 0 && c.joined) && d.crew.Exists(c => c.slot == 1 && c.joined), "Factory/roster contract");
            Check(d.requiredIncidents == 2 && d.requiredService == 2 && d.minimumSeconds == 180 && d.reward == 180 && d.penalty == 80, "Baseline tuning changed");
            Check(d.incidents.Count == 3 && new HashSet<int>(d.incidents.ConvertAll(i => i.room)).Count == 3 &&
                new HashSet<string>(d.incidents.ConvertAll(i => i.kind)).Count == 3, "Forecast is not three distinct owned rooms/kinds");
            string plan = JsonUtility.ToJson(d); int draws = sim.State.mvp.rngDraws, serial = d.serial;
            Hook(sim, "DangerPrepare"); Check(plan == JsonUtility.ToJson(d), "Repeated preparation rerolled/reset the shift");
            foreach (string mode in new[] { "bold", "relief", "standard" }) Ok(sim, "dangerMode", mode);
            Check(plan == JsonUtility.ToJson(d) && draws == sim.State.mvp.rngDraws && serial == d.serial, "Mode switching consumed RNG or changed forecast");
            At(sim, "board", 27); No(sim, "dangerMode", "bold", id: 27);
            foreach (string bad in new[] { "hazard_0101", "isolate_+101", "isolate_101x", "rescue_2", "recover_01", "rescue_-1", "hazard_105", "hazard_999" })
                Check(!HotelDangerRules.TryTarget(sim.State, bad, out _), "Noncanonical/unowned target accepted: " + bad);
            string snapshot = JsonUtility.ToJson(sim.State);
            foreach (HotelIncidentState i in d.incidents)
            { HotelDangerRules.Source(i); HotelDangerRules.Control(i.room); HotelDangerRules.Radius(i); HotelDangerRules.WorkError(sim.State, 0, "hazard_" + i.room); }
            HotelDangerRules.Objective(sim.State); HotelDangerRules.PowerAvailable(sim.State, 101); HotelDangerRules.WaterAvailable(sim.State, 101);
            Check(snapshot == JsonUtility.ToJson(sim.State), "Rules projection mutated state");
            sim.Leave(0); sim.Join(88);
            Check(sim.State.players.Count == 1 && Player(sim, 27) != null && Player(sim, 88) == null,
                "A second partner connection claimed the same durable crew slot while host was absent");
            sim.Join(0); HotelSaveStore.Validate(sim.State);
            Open(sim); At(sim, "board"); No(sim, "dangerMode", "relief");
        }
        private static void Guided()
        {
            HotelSimulation sim = New(guide: true);
            Advance(sim, 75); Same(0, sim.State.danger.elapsed, "Preparation consumed danger time");
            Open(sim); Advance(sim, 75);
            Same(0, sim.State.danger.elapsed, "Guided introduction consumed danger time");
            Check(sim.State.danger.incidents.TrueForAll(i => i.status == "planned"), "Guide issued an incident");
            Ok(sim, "endGuidedOpening"); Warn(sim, sim.State.danger.incidents[0]);
            Check(sim.State.danger.elapsed > 0, "Skipping lesson did not release danger clocks");
        }
        private static void WarningAndGeometry()
        {
            HotelSimulation sim = FirstKind("electric"); Open(sim);
            HotelIncidentState incident = sim.State.danger.incidents[0]; At(sim, HotelDangerRules.Source(incident));
            Warn(sim, incident); Advance(sim, HotelDangerRules.WarningSeconds - .5f);
            Same(100, Crew(sim).health, "Warning caused damage");
            Advance(sim, HotelDangerRules.ElectricPeriod + 1);
            Check(Crew(sim).health < 100 && Crew(sim).life == "healthy", "Electric pulse missing or instantly lethal");
            float health = Crew(sim).health;
            At(sim, HotelDangerRules.Control(incident.room)); Advance(sim, 8);
            Same(health, Crew(sim).health, "Safe control is inside the damage zone");
            Check(HotelDangerRules.Radius(incident) <= 1, "Electric capsule margin lost");
            At(sim, new Vector3(0, .1f, HotelLayout.RoomCenter(incident.room).z - 1.8f));
            No(sim, "beginwork", "isolate_" + incident.room); // Close enough, but segment crosses a wall.
            At(sim, new Vector3(0, .1f, HotelLayout.RoomCenter(incident.room).z)); Advance(sim, 4);
            Same(health, Crew(sim).health, "Room hazard hit the corridor");
            int safeRoom = incident.room == 101 ? 102 : 101;
            Room(sim, safeRoom).water = 1; // Ordinary physical puddle fixture, not a danger result.
            At(sim, HotelLayout.RoomTarget("water", safeRoom)); Advance(sim, 4);
            Same(health, Crew(sim).health, "Ordinary puddle became lethal");
        }
        private static void Counterplay()
        {
            HotelSimulation sim = New("bold"); Open(sim);
            foreach (HotelIncidentState incident in sim.State.danger.incidents)
            {
                Warn(sim, incident); At(sim, HotelDangerRules.Source(incident));
                No(sim, "beginwork", "hazard_" + incident.room);
                Work(sim, "isolate_" + incident.room);
                float active = incident.activeSeconds, safety = sim.State.danger.safety, health = Crew(sim).health;
                At(sim, HotelDangerRules.Source(incident)); Advance(sim, 3);
                Same(active, incident.activeSeconds, "Isolation did not latch"); Same(health, Crew(sim).health, "Isolated source damages crew");
                Same(safety, sim.State.danger.safety, "Isolated source erodes safety");
                Check(HotelDangerRules.PowerAvailable(sim.State, incident.room) == (incident.kind != "electric"), "Wrong local power isolation");
                Check(HotelDangerRules.WaterAvailable(sim.State, incident.room) == (incident.kind != "steam"), "Wrong local water isolation");
                int other = incident.room == 101 ? 102 : 101;
                Check(HotelDangerRules.PowerAvailable(sim.State, other) && HotelDangerRules.WaterAvailable(sim.State, other), "Local isolation disabled the hotel");
                Pick(sim, "plunger"); At(sim, HotelDangerRules.Source(incident)); No(sim, "beginwork", "hazard_" + incident.room); Drop(sim);
                Check((bool)Hook(sim, "TryRaiseFault", incident.room, "sink"), "Physical sink fixture failed");
                Room(sim, incident.room).water = .35f; Room(sim, incident.room).mvp.dirt = .4f;
                Pick(sim, incident.kind == "fumes" ? "mop" : "toolbox"); Work(sim, "hazard_" + incident.room); Drop(sim);
                Check(incident.status == "resolved" && !incident.isolated && Room(sim, incident.room).leak &&
                    Room(sim, incident.room).water > 0 && Room(sim, incident.room).mvp.dirt > 0, "Danger repair erased ordinary physical consequences");
                Check(HotelDangerRules.PowerAvailable(sim.State, incident.room) && HotelDangerRules.WaterAvailable(sim.State, incident.room), "Repair did not restore isolated service");
                At(sim, HotelDangerRules.Source(incident)); No(sim, "beginwork", "hazard_" + incident.room);
            }
            Check((bool)Hook(sim, "TryRaiseFault", 0, "power") && !HotelDangerRules.PowerAvailable(sim.State, 101), "Base utility fault lost to danger projection");
        }
        private static void Fumes()
        {
            HotelSimulation sim = FirstKind("fumes"); Open(sim);
            HotelIncidentState incident = sim.State.danger.incidents[0]; Warn(sim, incident);
            Until(sim, () => incident.status == "active", 12, "Fumes did not activate");
            float radius = HotelDangerRules.Radius(incident);
            At(sim, HotelDangerRules.Source(incident)); Advance(sim, 2);
            Same(100 - 2 * HotelDangerRules.FumesDamage, Crew(sim).health, "Fume exposure is not gradual");
            Check(HotelDangerRules.FumesDamage < HotelDangerRules.SteamDamage && HotelDangerRules.Radius(incident) > radius,
                "Fumes lost their distinct slower/growing profile");
            Vector3 source = HotelDangerRules.Source(incident), center = HotelLayout.RoomCenter(incident.room);
            Check(HotelDangerRules.Radius(incident) <= 3.25f - Mathf.Abs(source.z - center.z), "Fume zone crosses its room's wall");
        }
        private static void PressureBudget()
        {
            foreach (string mode in new[] { "standard", "bold", "relief" })
            {
                HotelSimulation sim = New(mode); Open(sim); Advance(sim, 125);
                int active = sim.State.danger.incidents.FindAll(i => i.status == "warning" || i.status == "active").Count;
                Check(active == (mode == "standard" ? 1 : mode == "bold" ? 2 : 0), "Wrong active incident cap: " + mode);
                if (mode != "standard") continue;
                Repair(sim, sim.State.danger.incidents[0]); Advance(sim, HotelDangerRules.IncidentGrace - 1);
                Check(sim.State.danger.incidents[1].status == "planned", "Immediate replacement pressure after repair");
                Advance(sim, 2); Check(sim.State.danger.incidents[1].status == "warning", "Saved overdue incident never resumed after grace");
            }
        }
        private static void CriticalRecovery()
        {
            HotelSimulation sim = New(); Open(sim);
            Until(sim, () => sim.State.danger.safety <= 0, 360, "Safety did not reach critical");
            Check(sim.State.danger.status == "active" && sim.State.danger.criticalRemaining >= 29.8f, "Critical warning window missing");
            Repair(sim, sim.State.danger.incidents[0]);
            Check(sim.State.danger.status == "active" && sim.State.danger.safety > 0, "Repair did not rescue the critical hotel");
            Same(HotelDangerRules.CriticalSeconds, sim.State.danger.criticalRemaining, "Critical countdown did not reset on real repair");
        }
        private static void Collapse()
        {
            HotelSimulation sim = New("bold"); At(sim, "board"); Ok(sim, "upgrade", "cart"); Open(sim);
            int cash = sim.State.cash, expenses = sim.State.expenses;
            Until(sim, () => sim.State.danger.settled, 400, "Ignored danger never failed the shift");
            HotelDangerState d = sim.State.danger;
            Check(d.outcome == "collapse" && d.status == "failed" && sim.State.phase == "summary" && d.losses == 1, "Wrong collapse outcome");
            Check(sim.State.cash == cash - 120 && sim.State.expenses == expenses + 120 && sim.State.cash < 0 && d.paidReward == 0, "Penalty/debt not exact");
            Check(Crew(sim).life == "healthy", "Collapse inflicted mapwide player death");
            int terminalCash = sim.State.cash; Advance(sim, 40); At(sim, "desk"); No(sim, "finish");
            Check(sim.State.cash == terminalCash && d.losses == 1, "Repeated terminal penalty");
            int serial = d.serial; At(sim, HotelLayout.Spawn); Ok(sim, "nextday");
            Check(sim.State.cartUpgrade && d.serial == serial + 1 && d.losses == 1 && d.status == "briefing" && d.medkits == 3 && sim.State.cash < 0,
                "Failure erased investment or blocked preparation in debt");
        }
        private static void DownSelfAndWipe()
        {
            HotelSimulation sim = FirstKind("steam"); Open(sim);
            HotelIncidentState incident = sim.State.danger.incidents[0]; Warn(sim, incident);
            Pick(sim, "mop"); string itemId = Player(sim).held;
            Room(sim, incident.room).water = .4f;
            At(sim, HotelDangerRules.Source(incident));
            Until(sim, () => Crew(sim).health < 4, 40, "Steam exposure did not injure crew");
            Ok(sim, "beginwork", "water_" + incident.room);
            Until(sim, () => Crew(sim).life == "downed", 1, "Crew did not go down during unfinished physical work");
            Check(Player(sim).held == "" && Player(sim).workTarget == "" && sim.State.items.Find(i => i.id == itemId).holder == -1, "Down retained hand/lease");
            Check(HotelDangerRules.TryTarget(sim.State, "recover_0", out Vector3 body) && body == Crew(sim).position, "Virtual self-help focus missing");
            No(sim, "pickup", itemId); No(sim, "pose"); No(sim, "beginwork", "rescue_0");
            Work(sim, "recover_0");
            Check(Crew(sim).life == "healthy" && Crew(sim).health > 0 && Crew(sim).shield > 6 && sim.State.danger.medkits == 2 && sim.State.danger.selfRescues == 0,
                "Self rescue did not spend exact resources or grant shield");
            float health = Crew(sim).health; Advance(sim, 5); Same(health, Crew(sim).health, "Rescue shield failed");
            Until(sim, () => sim.State.danger.settled, 30, "Second solo down without self-help did not lose shift");
            Check(sim.State.danger.outcome == "wipe" && Crew(sim).life == "dead" && sim.State.danger.losses == 1, "Wrong irreversible team outcome");
            No(sim, "beginwork", "recover_0"); int losses = sim.State.danger.losses; Ok(sim, "nextday");
            Check(Crew(sim).life == "healthy" && Crew(sim).health == 100 && sim.State.danger.losses == losses, "Dead host cannot start recovery preparation");
        }
        private static void Rescue()
        {
            HotelSimulation sim = FirstKind("steam", true); Open(sim);
            HotelIncidentState incident = sim.State.danger.incidents[0]; Warn(sim, incident);
            At(sim, HotelDangerRules.Source(incident), 27);
            Until(sim, () => Crew(sim, 27).life == "downed", 35, "Partner did not go down");
            Vector3 safeReach = Crew(sim, 27).position; safeReach.x += safeReach.x < 0 ? 1.4f : -1.4f;
            At(sim, safeReach); Ok(sim, "beginwork", "rescue_1"); No(sim, "beginwork", "recover_1", id: 27);
            Continue(sim, 2); Ok(sim, "cancelwork");
            Ok(sim, "beginwork", "recover_1", id: 27); No(sim, "beginwork", "rescue_1"); Ok(sim, "cancelwork", id: 27);
            int cash = sim.State.cash; Ok(sim, "beginwork", "rescue_1"); Continue(sim);
            Check(Crew(sim, 27).life == "healthy" && Crew(sim).rescues == 1 && sim.State.danger.medkits == 2 && sim.State.danger.selfRescues == 1 && sim.State.cash == cash,
                "Rescue duplicated supply/reward or consumed self-help");
            No(sim, "beginwork", "rescue_1"); No(sim, "beginwork", "rescue_0");
            Until(sim, () => Crew(sim, 27).life == "downed", 30, "Repeated exposure fixture did not down partner");
            Advance(sim, HotelDangerRules.DownedSeconds + 1);
            Check(Crew(sim, 27).life == "dead" && sim.State.danger.status == "active", "Individual death ended healthy colleague's shift");
            No(sim, "beginwork", "rescue_1"); No(sim, "beginwork", "recover_1", id: 27);
        }
        private static void MedicalSupplies()
        {
            HotelSimulation sim = FirstKind("steam"); Open(sim);
            At(sim, "firstaid"); No(sim, "beginwork", "firstaid");
            HotelIncidentState incident = sim.State.danger.incidents[0]; Warn(sim, incident);
            Until(sim, () => incident.status == "active", 12, "Steam did not activate");
            for (int i = 0; i < 3; i++)
            {
                At(sim, HotelDangerRules.Source(incident)); Advance(sim, .5f);
                Work(sim, "firstaid"); Same(100, Crew(sim).health, "First aid did not restore health");
                Check(sim.State.danger.medkits == 2 - i, "Wrong kit expenditure"); No(sim, "beginwork", "firstaid");
            }
            At(sim, HotelDangerRules.Source(incident)); Advance(sim, .5f); At(sim, "firstaid"); No(sim, "beginwork", "firstaid");
        }
        private static void Reconnect()
        {
            HotelSimulation sim = FirstKind("steam", true); Open(sim);
            HotelIncidentState incident = sim.State.danger.incidents[0]; Warn(sim, incident); At(sim, HotelDangerRules.Source(incident), 27);
            Until(sim, () => Crew(sim, 27).life == "downed", 35, "Partner down fixture");
            Vector3 body = Crew(sim, 27).position; float bleedout = Crew(sim, 27).bleedout;
            sim.Leave(27); sim.Join(88);
            Check(Crew(sim, 88).life == "downed" && Player(sim, 88).position == body, "New connection ID revived/moved partner");
            Same(bleedout, Crew(sim, 88).bleedout, "Reconnect refilled bleedout"); No(sim, "pose", id: 88);
            sim.Leave(88);
            HotelSimulation loaded = new HotelSimulation(JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(sim.State)));
            loaded.Join(42);
            Check(Crew(loaded, 42).life == "downed" && Player(loaded, 42).position == body, "Native JSON reload lost body/life");
            Same(bleedout, Crew(loaded, 42).bleedout, "Load reset medical timer");
            loaded.Leave(0); loaded.Leave(42); Advance(loaded, .1f);
            Check(loaded.State.danger.status == "active", "Empty connection list alone caused a wipe");
        }
        private static void ForfeitGate()
        {
            var sim=New(pair:true);No(sim,"abandonDanger");Open(sim);
            No(sim,"abandonDanger",id:27);No(sim,"abandonDanger");
            var host=Crew(sim);host.life="downed";host.health=0;host.bleedout=40;host.downs=1;
            string before=JsonUtility.ToJson(sim.State);
            Check(!HotelDangerRules.HostCanForfeit(sim.State),"Connected responder ignored");
            Check(before==JsonUtility.ToJson(sim.State),"Forfeit query mutated state");
            No(sim,"abandonDanger");sim.Leave(27);
            Check(HotelDangerRules.HostCanForfeit(sim.State),"Incapacitated isolated host lacks explicit escape");
            Ok(sim,"abandonDanger");No(sim,"abandonDanger");Ok(sim,"nextday");
            Check(!HotelDangerRules.HostCanForfeit(sim.State),"Forfeit escaped its active-phase gate");
        }
        private static void ForfeitDisconnected()
        {
            var sim=FirstKind("steam",true);Open(sim);
            var first=sim.State.danger.incidents[0];Warn(sim,first);At(sim,HotelDangerRules.Source(first));
            Until(sim,()=>Crew(sim).life=="downed",35,"Host exposure did not down");
            foreach(var incident in sim.State.danger.incidents.GetRange(0,2)) {
                Drop(sim,27);Warn(sim,incident);Work(sim,"isolate_"+incident.room,27);
                Pick(sim,incident.kind=="fumes"?"mop":"toolbox",27);Work(sim,"hazard_"+incident.room,27);Drop(sim,27);
            }
            Until(sim,()=>Crew(sim).life=="dead",46,"Host bleedout missing");
            No(sim,"abandonDanger");sim.Leave(27);
            Check(sim.State.danger.status=="active"&&HotelDangerRules.HostCanForfeit(sim.State),"Softlock fixture not reproduced");
            string folder=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"WorstHotelForfeit-"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folder);
            try {
                string path=Path.Combine(folder,"slot.json");HotelSaveStore.Save(sim.State,path);
                sim=HotelSimulation.ResumeDangerForPlay(HotelSaveStore.Load(path));sim.Join(0);
                string partner=JsonUtility.ToJson(Crew(sim,27));int cash=sim.State.cash;
                Ok(sim,"abandonDanger");
                Check(sim.State.phase=="summary"&&sim.State.danger.losses==1&&sim.State.cash==cash-sim.State.danger.penalty,
                    "Forfeit did not charge exactly one failure");
                Check(partner==JsonUtility.ToJson(Crew(sim,27)),"Forfeit injured or healed offline partner");
                No(sim,"abandonDanger");Ok(sim,"nextday");
                Check(Crew(sim).life=="healthy"&&sim.State.phase=="preparation","Dead host cannot resume hotel");
            } finally {
                Check(Path.GetDirectoryName(folder).TrimEnd(Path.DirectorySeparatorChar)==Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)&&
                    Path.GetFileName(folder).StartsWith("WorstHotelForfeit-"),"Unsafe fixture cleanup");
                Directory.Delete(folder,true);
            }
        }
        private static void PreparationPings()
        {
            var sim=New("relief",pair:true);int room=sim.State.danger.incidents[0].room;
            Ok(sim,"ping","board",id:27);
            Ok(sim,"ping","isolate_"+room);Hook(sim,"DangerPrepare");
            Check(sim.State.mvp.pings.Exists(p=>p.target=="isolate_"+room),"Same-day prepare cleared a live marker");
            Open(sim);At(sim,"desk");Ok(sim,"finish");Ok(sim,"nextday");
            Check(!sim.State.mvp.pings.Exists(p=>HotelDangerRules.IsWorkTarget(p.target)),"New forecast retained stale danger marker");
            Check(sim.State.mvp.pings.Exists(p=>p.target=="board"),"Danger preparation cleared ordinary marker");
            HotelSaveStore.Validate(sim.State);
        }
        private static void Victory()
        {
            HotelSimulation sim = New(); Open(sim); At(sim, "desk"); No(sim, "finish");
            GuestState guest = sim.State.guests.Find(g => g.stage == "queue"); Check(guest != null, "Opening guest missing");
            Serve(sim, guest, 101); Check(sim.State.danger.servicePoints == 2, "Check-in/luggage did not award factual one-shot service");
            ItemState bag = sim.State.items.Find(i => !i.consumed && i.kind == "bag" && i.ownerGuest == guest.id);
            At(sim, bag.position); Ok(sim, "pickup", bag.id);
            At(sim, "bag_101"); Ok(sim, "interact", "bag_101");
            Check(sim.State.danger.servicePoints == 2, "Repeated physical bag delivery farmed service points");
            At(sim, "bag_101"); No(sim, "interact", "bag_101");
            At(sim, "desk"); No(sim, "finish");
            foreach (HotelIncidentState i in sim.State.danger.incidents.GetRange(0, 2)) Repair(sim, i);
            At(sim, "desk"); No(sim, "finish"); // Both repairs precede the minimum contract duration.
            Until(sim, () => sim.State.danger.elapsed >= sim.State.danger.minimumSeconds, 200, "Minimum timer stopped");
            int cash = sim.State.cash; Ok(sim, "finish"); HotelDangerState d = sim.State.danger;
            Check(d.status == "won" && d.outcome == "completed" && d.wins == 1 && d.streak == 1 && d.bestStreak == 1 && d.paidReward == 180 &&
                sim.State.cash >= cash + 180 && d.incidents[2].status == "planned", "Missing/incorrect victory accounting");
            cash = sim.State.cash; No(sim, "finish"); Advance(sim, 50);
            Check(sim.State.cash == cash && d.wins == 1, "Repeated reward");
            Ok(sim, "nextday"); Check(d.status == "briefing" && d.wins == 1 && d.paidReward == 0, "New day lost lifetime outcome counters");
        }
        private static void Evacuation()
        {
            HotelSimulation sim = New("bold"); Prepare102(sim);
            // Production allocators arrange linked bookings/groups; no fabricated paid/result flags.
            object[] futureArgs = { 2, "danger_future_fixture", "" };
            Check((bool)Hook(sim, "TryReserveBatch", futureArgs), "Future booking fixture failed");
            var future = sim.State.mvp.reservations.FindAll(r => r.arrivalDay == sim.State.day + 1).ConvertAll(r => r.id);
            Open(sim); object[] walkinArgs = { 2, "walkin_group", "" };
            Check((bool)Hook(sim, "TryReserveBatch", walkinArgs), "Walk-in group fixture failed");
            string groupId = (string)walkinArgs[2];
            Until(sim, () => sim.State.guests.FindAll(g => g.stage == "queue" && g.mvp.groupId == groupId).Count == 2, 20, "Group did not physically arrive");
            var guests = sim.State.guests.FindAll(g => g.stage == "queue" && g.mvp.groupId == groupId);
            Serve(sim, guests[0], 101); Serve(sim, guests[1], 102);
            int cash = sim.State.cash, earned = sim.State.earned, served = sim.State.served;
            Work(sim, "alarm"); HotelDangerState d = sim.State.danger;
            Check(d.outcome == "evacuated" && d.status == "failed" && d.paidReward == 0 && d.chargedPenalty == 120, "Wrong evacuation outcome");
            Check(sim.State.cash == cash - 120 && sim.State.earned == earned && sim.State.served == served && guests.TrueForAll(g => !g.paid), "Evacuation paid interrupted stays");
            Check(Room(sim, 101).guestId == 0 && Room(sim, 102).guestId == 0 && Room(sim, 101).bed == 1, "Evacuation did not release/dirty rooms");
            Check(future.TrueForAll(id => sim.State.mvp.reservations.Exists(r => r.id == id && r.status == "confirmed")), "Evacuation cancelled next-day commitments");
            MvpGroupState group = sim.State.mvp.groups.Find(g => g.id == groupId);
            Check(group != null && group.admitted == 0 && group.refused == 2 && group.completed == 0, "Unpaid group evacuation double-counted admission/refusal");
            HotelSaveStore.Validate(sim.State);
        }
        private static void IncompleteAndRelief()
        {
            HotelSimulation sim = New(); Open(sim);
            foreach (HotelIncidentState i in sim.State.danger.incidents.GetRange(0, 2)) Repair(sim, i);
            Until(sim, () => sim.State.phase == "closing", 1200, "Shift did not close");
            At(sim, "desk"); Ok(sim, "finish");
            Check(sim.State.danger.outcome == "incomplete" && sim.State.danger.paidReward == 0, "Missing service was treated as success");
            sim = New("relief"); Open(sim); int cash = sim.State.cash;
            At(sim, "desk"); Ok(sim, "finish");
            Check(sim.State.danger.outcome == "relief" && sim.State.danger.wins == 0 && sim.State.danger.losses == 0 && sim.State.danger.paidReward == 0 &&
                sim.State.cash == cash, "Relief minted a contract victory or bonus");
        }
    }
}
