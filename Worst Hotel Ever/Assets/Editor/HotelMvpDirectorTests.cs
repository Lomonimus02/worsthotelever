using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WorstHotel
{
    // Registered by the parent in HotelBuild.Validate. No NUnit or scene/player dependency.
    public static class HotelMvpDirectorTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "MVP pacing: utility causes deduplicate while cleanup and delivery remain work", WorkloadCauses);
            Run(passed, "MVP pacing: Operations faults and Hospitality issue roots describe the same work", LiveIssueCauses);
            Run(passed, "MVP pacing: pure projections and real team/logistics capacity", PureAndCapacity);
            Run(passed, "MVP pacing: bounded progression and smoothed stress with recovery", ProgressionAndStress);
            Run(passed, "MVP pacing: preparation, guide, overload and upgrade grace suppress events", Protection);
            Run(passed, "MVP pacing: all four events use normal services and persist cooldowns", FourEvents);
            Run(passed, "MVP pacing: capacity rejection leaves no event, cooldown, reward or retry", RejectedEvent);
            Run(passed, "MVP pacing: no consecutive repeats or overlapping costly events", Exclusion);
            Run(passed, "MVP pacing: nothing is valid and saved RNG resumes identical decisions", SavedRandomness);
            Run(passed, "MVP pacing: water waits for repair and cleanup; terminal history is bounded", ResolutionAndBounds);
            return passed;
        }

        private static void Run(List<string> passed, string name, Action test)
        {
            try { test(); passed.Add(name); }
            catch (Exception error) { throw new Exception("HotelMvpDirectorTests FAILED: " + name, error.GetBaseException()); }
        }
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Same(float expected, float actual, string message)
        {
            Assert(Math.Abs(expected - actual) < .001f, message + ": " + expected + " != " + actual);
        }

        private static HotelSimulation New(int seed = 71237)
        {
            HotelSimulation sim = HotelSimulation.CreateNewMvp(seed);
            HotelState state = sim.State;
            state.day = 6; state.dayLength = 1080; state.time = 300; state.phase = "open";
            state.guidedOpening = false; state.guidedStage = HotelDirector.Released;
            state.guidedGuestId = state.guidedLeakRoom = 0;
            state.guidedRepairDone = state.guidedMopDone = false;
            state.arrivals = state.served = 0;
            state.mvp.elapsed = 300; state.mvp.preparedDay = state.day; state.mvp.reputation = 4;
            state.mvp.director = new MvpDirectorState { nextDecision = 0 };
            state.mvp.schedule.Clear(); state.mvp.reservations.Clear(); state.mvp.groups.Clear();
            state.mvp.requests.Clear(); state.mvp.complaints.Clear(); state.guests.Clear();
            state.mvp.utilities.waterFault = state.mvp.utilities.powerFault = false;
            state.items.RemoveAll(i => i.kind == "bag" || i.consumed);
            foreach (RoomState room in state.rooms)
            {
                room.guestId = 0; room.bed = 2; room.towel = true;
                room.trash = room.leak = room.outOfService = false; room.water = 0;
                room.mvp.dirt = room.mvp.binFill = room.mvp.noise = 0; room.mvp.dirtyTowels = 0;
                room.mvp.bedQuality = room.mvp.tvQuality = 2; room.upgraded = true;
                foreach (MvpEquipmentState equipment in room.mvp.equipment)
                { equipment.installed = true; equipment.localFault = false; equipment.wear = 0; equipment.quality = 2; }
            }
            sim.Join(0);
            HotelSaveStore.Validate(state);
            return sim;
        }

        private static void Pace(HotelSimulation sim, float dt = 1)
        {
            float elapsed = sim.State.mvp.elapsed;
            Call(sim, "PacingStep", dt);
            Same(elapsed, sim.State.mvp.elapsed, "Pacing advanced the lifecycle clock");
        }
        private static void Call(HotelSimulation sim, string name, params object[] args)
        {
            MethodInfo method = typeof(HotelSimulation).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert(method != null, "Missing private lifecycle hook " + name);
            method.Invoke(sim, args);
        }
        private static float Potential(HotelState state)
        {
            return (float)typeof(HotelMvpDirector).GetMethod("DifficultyPotential", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { state });
        }
        private static GuestState Occupant(HotelState state, int number)
        {
            var guest = new GuestState { id = state.nextGuest++, room = number, name = "Проверка", stage = "staying",
                luggageDelivered = true, position = HotelLayout.RoomCenter(number),
                mvp = new MvpGuestState { arrivalDay = state.day, departureDay = state.day + 1 } };
            state.guests.Add(guest); state.rooms.Find(r => r.number == number).guestId = guest.id;
            return guest;
        }

        private static void WorkloadCauses()
        {
            HotelSimulation sim = New(); sim.Join(1);
            HotelState state = sim.State;
            GuestState first = Occupant(state, 101), second = Occupant(state, 102);
            state.mvp.utilities.waterFault = true;
            float physicalFault = HotelWorkload.Measure(state);
            state.mvp.complaints.Add(new MvpComplaintState { id = "c1", category = "toilet", causeKey = "utility:water:1", guestId = first.id });
            state.mvp.complaints.Add(new MvpComplaintState { id = "c2", category = "toilet", causeKey = "utility:water:1", guestId = second.id, escalation = 2 });
            Same(physicalFault, HotelWorkload.Measure(state), "Shared utility complaints multiplied repair work");
            RoomState room = state.rooms.Find(r => r.number == 101);
            room.water = .4f; room.mvp.dirt = .4f; room.mvp.dirtyTowels = 1; room.towel = false;
            first.luggageDelivered = false;
            float actualTasks = HotelWorkload.Measure(state);
            Assert(actualTasks > physicalFault + 15, "Utility cause swallowed cleanup/delivery work");
            state.mvp.requests.Add(new MvpRequestState { id = "r1", kind = "luggage", guestId = first.id, dueAt = 9999 });
            state.mvp.requests.Add(new MvpRequestState { id = "r2", kind = "towel", guestId = first.id, dueAt = 9999 });
            Same(actualTasks, HotelWorkload.Measure(state), "One delivery counted as physical job and request");
            state.mvp.requests.Add(new MvpRequestState { id = "r3", kind = "coffee", guestId = first.id, dueAt = 9999 });
            Assert(HotelWorkload.Measure(state) > actualTasks, "Independent coffee delivery missing");
            state.items.Add(new ItemState { id = "dirty_delivery", kind = "towel", condition = "dirty" });
            float looseDelivery = HotelWorkload.Measure(state);
            state.items.Find(i => i.id == "dirty_delivery").consumed = true;
            Assert(HotelWorkload.Measure(state) < looseDelivery, "Actual disposal not counted independently");
            room.mvp.equipment.Find(e => e.kind == "lamp").localFault = true;
            float lampFault = HotelWorkload.Measure(state);
            state.mvp.complaints.Add(new MvpComplaintState { id = "lamp_complaint", category = "tv", causeKey = "equipment:lamp:101:1", guestId = first.id });
            Same(lampFault, HotelWorkload.Measure(state), "Lamp category fabricated a second TV repair");

            // Isolate the wet floor: no dirt task exists to accidentally absorb a wrongly
            // categorized complaint. Separate puddles must still require separate cleanup.
            HotelSimulation wet = New(); wet.Join(1);
            GuestState wetGuest = Occupant(wet.State, 101), neighbor = Occupant(wet.State, 102);
            wet.State.rooms.Find(r => r.number == 101).water = .4f;
            float onePuddle = HotelWorkload.Measure(wet.State);
            wet.State.mvp.complaints.Add(new MvpComplaintState { id = "wet_1", category = "dirt", causeKey = "room:water:101", guestId = wetGuest.id });
            Same(onePuddle, HotelWorkload.Measure(wet.State), "Wet-floor complaint fabricated a separate dirt-cleaning job");
            wet.State.rooms.Find(r => r.number == 102).water = .4f;
            float twoPuddles = HotelWorkload.Measure(wet.State);
            Assert(twoPuddles > onePuddle, "Two actual cleanup locations collapsed into one cause");
            wet.State.mvp.complaints.Add(new MvpComplaintState { id = "wet_2", category = "dirt", causeKey = "room:water:102", guestId = neighbor.id });
            Same(twoPuddles, HotelWorkload.Measure(wet.State), "Second wet-floor complaint duplicated its own physical task");
        }

        private static void LiveIssueCauses()
        {
            HotelSimulation sim = New(); sim.Join(1);
            HotelState state = sim.State;
            GuestState first = Occupant(state, 101), second = Occupant(state, 102);
            Call(sim, "TryRaiseFault", 0, "water");
            Call(sim, "TryRaiseFault", 0, "power");
            foreach (string kind in new[] { "sink", "toilet", "tv", "lamp" })
                Call(sim, "TryRaiseFault", 101, kind);
            RoomState room = state.rooms.Find(r => r.number == 101);
            Assert(room.leak && room.mvp.equipment.TrueForAll(e => e.localFault), "Operations fault fixture did not use the physical sources");
            room.water = .4f; room.mvp.dirt = .4f; room.mvp.dirtyTowels = 1;
            float physical = HotelWorkload.Measure(state);
            int sharedWater = 0, sharedPower = 0, local = 0, puddles = 0, dirt = 0;
            foreach (GuestState guest in new[] { first, second })
                foreach (MvpGuestIssue issue in HotelHospitalityRules.Issues(state, guest))
                {
                    if (issue.causeKey.StartsWith("utility:water:", StringComparison.Ordinal)) sharedWater++;
                    else if (issue.causeKey.StartsWith("utility:power:", StringComparison.Ordinal)) sharedPower++;
                    else if (issue.causeKey.StartsWith("equipment:", StringComparison.Ordinal))
                    {
                        local++;
                        if (issue.causeKey.StartsWith("equipment:lamp:", StringComparison.Ordinal))
                            Assert(issue.category == "tv", "Lamp display category changed; recheck source-based normalization");
                    }
                    else if (issue.causeKey == "room:water:101") puddles++;
                    else if (issue.causeKey == "room:dirt:101") dirt++;
                    else continue; // This fixture intentionally has no luggage items/requests.
                    state.mvp.complaints.Add(new MvpComplaintState { id = "live_" + state.mvp.complaints.Count,
                        category = issue.category, causeKey = issue.causeKey, guestId = guest.id });
                }
            Assert(sharedWater == 2 && sharedPower == 2 && local == 3 && puddles == 1 && dirt == 1,
                "Hospitality issue fixture no longer covers shared utilities, local faults and both floor causes");
            Same(physical, HotelWorkload.Measure(state), "Live complaint categories multiplied their Operations root causes");

            // Sink has no guest complaint yet; its legacy leak projection must still merge
            // with its equipment fault, independently of actual water remaining on the floor.
            room.leak = false;
            Same(physical, HotelWorkload.Measure(state), "Sink localFault and leak projection counted two repairs");
            room.leak = true;
            room.water = 0;
            state.mvp.complaints.RemoveAll(c => c.causeKey == "room:water:101");
            Assert(HotelWorkload.Measure(state) < physical, "Physical puddle was collapsed into utility/sink repair");
        }

        private static void PureAndCapacity()
        {
            HotelSimulation sim = New(); HotelState state = sim.State;
            RoomState room = state.rooms.Find(r => r.number == 101);
            room.bed = 1; room.trash = true; room.water = .3f;
            float solo = HotelWorkload.Measure(state);
            sim.Join(1); float team = HotelWorkload.Measure(state);
            Assert(team < solo && team > 0, "Two employees did not increase capacity");
            state.cartUpgrade = true; state.secondToolbox = true; state.mvp.coffeeMachine = true;
            Assert(HotelWorkload.Measure(state) < team, "Logistics/tool capacity ignored");
            float beforeLocked = HotelWorkload.Measure(state);
            RoomState locked = state.rooms.Find(r => r.number == 106);
            locked.mvp.owned = false; locked.mvp.dirt = 1; locked.leak = true; locked.water = 1;
            Same(beforeLocked, HotelWorkload.Measure(state), "Unowned room creates work");
            string snapshot = JsonUtility.ToJson(state);
            for (int i = 0; i < 5; i++) { HotelWorkload.Measure(state); HotelMvpDirector.Status(state); }
            Assert(snapshot == JsonUtility.ToJson(state), "Projection mutated state/RNG");
        }

        private static void ProgressionAndStress()
        {
            HotelSimulation sim = New(); HotelState state = sim.State;
            state.day = 1; float early = Potential(state);
            state.day = 20; float later = Potential(state);
            Assert(later >= early + .3f, "Days do not meaningfully affect difficulty potential");
            state.rooms.Find(r => r.number == 105).mvp.owned = true;
            state.rooms.Find(r => r.number == 106).mvp.owned = true;
            Assert(Potential(state) > later && Potential(state) <= 1, "Expansion progression absent or unbounded");
            float grown = Potential(state); state.day = 9999;
            Same(grown, Potential(state), "Endless difficulty accelerates forever");

            RoomState room = state.rooms.Find(r => r.number == 101);
            room.mvp.dirt = .8f; room.water = .5f;
            state.mvp.director.nextDecision = 99999;
            Pace(sim, 1);
            float stress = state.mvp.director.stress;
            Assert(stress > 0 && stress < state.mvp.director.workload, "Stress did not smooth its input");
            room.mvp.dirt = room.water = 0;
            Pace(sim, 1);
            Assert(state.mvp.director.stress > 0 && state.mvp.director.stress < stress, "Stress did not retain and decay recent pressure");
        }

        private static void Protection()
        {
            HotelSimulation sim = New(); HotelState state = sim.State;
            state.phase = "preparation";
            string before = JsonUtility.ToJson(state); Pace(sim, 20);
            Assert(before == JsonUtility.ToJson(state), "Preparation ran pacing clocks or draws");
            state.phase = "open"; state.day = 1; state.guidedOpening = true; state.guidedStage = HotelDirector.Service;
            int draws = state.mvp.rngDraws;
            Pace(sim, 20);
            Assert(state.mvp.rngDraws == draws && state.mvp.director.events.Count == 0, "Guided introduction received random pressure");
            state.guidedOpening = false; state.day = 6;
            foreach (RoomState room in state.rooms)
            {
                if (!room.mvp.owned) continue;
                room.bed = 1; room.trash = true; room.water = 1; room.leak = true; room.mvp.dirt = 1; room.mvp.dirtyTowels = 3;
            }
            state.mvp.director.nextDecision = 0;
            Assert(HotelWorkload.Measure(state) >= HotelMvpDirector.OverloadThreshold, "Fixture did not overload team");
            Pace(sim);
            Assert(state.mvp.director.events.Count == 0 && state.mvp.rngDraws == draws && state.mvp.director.recoveryUntil > state.mvp.elapsed,
                "Overload added events or failed to grant recovery");
            Assert(state.rooms.Find(r => r.number == 101).leak, "Soft assistance erased a real problem");

            sim = New(); state = sim.State; state.phase = "preparation";
            state.mvp.director.stress = 30; draws = state.mvp.rngDraws;
            Call(sim, "PacingUpgradeGrace");
            float until = state.mvp.director.recoveryUntil;
            Assert(until >= state.mvp.elapsed + HotelMvpDirector.UpgradeGraceSeconds, "Upgrade did not create a power window");
            Pace(sim, 30); Same(until, state.mvp.director.recoveryUntil, "Preparation spent grace");
            state.phase = "open"; state.mvp.elapsed = until - 1; Pace(sim);
            Assert(state.mvp.director.events.Count == 0 && state.mvp.rngDraws == draws && state.mvp.director.stress > 0,
                "Grace added pressure or wiped stress history");
            state.phase = "closing"; state.mvp.elapsed = until + 100; state.mvp.director.nextDecision = 0; Pace(sim);
            Assert(state.mvp.director.events.Count == 0 && state.mvp.rngDraws == draws, "Closing introduced an event");
        }

        private static void BlockOthers(HotelState state, string allowed)
        {
            foreach (string kind in new[] { "rush", "walkin_group", "vip", "water" })
                if (kind != allowed) state.mvp.director.cooldowns.Add(new MvpCooldownState { id = "event_" + kind, until = 1000000 });
        }
        private static MvpEventState ForceEvent(HotelSimulation sim, string kind)
        {
            BlockOthers(sim.State, kind);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                sim.State.mvp.elapsed = Math.Max(sim.State.mvp.elapsed, sim.State.mvp.director.nextDecision + 1);
                Pace(sim, .1f);
                MvpEventState value = sim.State.mvp.director.events.Find(e => e.kind == kind);
                if (value != null) return value;
            }
            throw new Exception("No successful " + kind + " event in an eligible, empty hotel");
        }

        private static void FourEvents()
        {
            foreach (string kind in new[] { "rush", "walkin_group", "vip", "water" })
            {
                HotelSimulation sim = New(); HotelState state = sim.State; int cash = state.cash;
                MvpEventState value = ForceEvent(sim, kind);
                Assert(value.status == "active" && value.id != null && value.until > value.startedAt, "Incomplete event record " + kind);
                Assert(state.cash == cash && state.served == 0, "Event fabricated settlement/rewards");
                Assert(state.mvp.director.globalCooldown > state.mvp.elapsed &&
                    state.mvp.director.cooldowns.Exists(c => c.id == "category_" + value.category && c.until > state.mvp.elapsed) &&
                    state.mvp.director.cooldowns.Exists(c => c.id == "event_" + kind && c.until > state.mvp.elapsed), "Missing saved cooldown layer");
                if (kind == "water")
                {
                    RoomState room = state.rooms.Find(r => r.number == value.room);
                    Assert(room.mvp.owned && room.mvp.equipment.Exists(e => e.kind == "sink" && e.localFault), "Water bypassed Operations fault state");
                }
                else Assert(state.mvp.schedule.Count > 0 || state.mvp.reservations.Count > 0, "Guest event bypassed Hospitality scheduling");
                if (kind == "walkin_group") Assert(state.mvp.groups.Count > 0, "Unexpected group has no normal group record");
                HotelSaveStore.Validate(state);
                int draws = state.mvp.rngDraws;
                Pace(sim, .1f);
                Assert(state.mvp.director.events.Count == 1 && state.mvp.rngDraws == draws, "Cooldown allowed immediate new event/draw");
            }
        }

        private static void RejectedEvent()
        {
            HotelSimulation sim = New(); HotelState state = sim.State;
            foreach (RoomState room in state.rooms) room.outOfService = true;
            string snapshot = JsonUtility.ToJson(state);
            foreach (string kind in new[] { "rush", "walkin_group", "vip", "water" })
            {
                Call(sim, "PacingStart", kind, state.mvp.elapsed, .7f);
                Assert(snapshot == JsonUtility.ToJson(state), "Failed " + kind + " event changed state or retried another event");
            }
        }

        private static void Exclusion()
        {
            HotelSimulation sim = New(); HotelState state = sim.State;
            BlockOthers(state, "rush"); state.mvp.director.recent.Add("rush");
            Pace(sim);
            Assert(state.mvp.director.events.Count == 0 && state.mvp.schedule.Count == 0, "Consecutive event repeated");
            sim = New(); state = sim.State;
            state.mvp.director.events.Add(new MvpEventState { id = "existing", kind = "walkin_group", category = "guest", until = 99999 });
            state.mvp.director.cooldowns.Add(new MvpCooldownState { id = "event_water", until = 99999 });
            for (int i = 0; i < 8; i++)
            {
                state.mvp.elapsed = Math.Max(state.mvp.elapsed, state.mvp.director.nextDecision + 1); Pace(sim);
            }
            Assert(state.mvp.director.events.Count == 1 && state.mvp.schedule.Count == 0, "Overlapping high-cost events admitted");
            sim = New(); state = sim.State;
            state.mvp.groups.Add(new MvpGroupState { id = "planned_group", source = "planned", arrivalDay = state.day, status = "arriving" });
            state.mvp.director.cooldowns.Add(new MvpCooldownState { id = "event_water", until = 99999 });
            Pace(sim);
            Assert(state.mvp.director.events.Count == 0, "Planned group pressure allowed a second costly event");
        }

        private static void SavedRandomness()
        {
            bool sawNothing = false, sawEvent = false;
            for (int seed = 1; seed <= 48; seed++)
            {
                HotelSimulation sim = New(seed); Pace(sim, .1f);
                if (sim.State.mvp.director.events.Count == 0) sawNothing = true; else sawEvent = true;
            }
            Assert(sawNothing && sawEvent, "Weighted decisions lack a real nothing/event alternative");

            HotelSimulation original = New(54321);
            string saved = JsonUtility.ToJson(original.State);
            HotelSimulation loaded = new HotelSimulation(JsonUtility.FromJson<HotelState>(saved));
            for (int i = 0; i < 12; i++)
            {
                float next = Math.Max(original.State.mvp.elapsed + 30, original.State.mvp.director.nextDecision + 1);
                original.State.mvp.elapsed = loaded.State.mvp.elapsed = next;
                Pace(original, .1f); Pace(loaded, .1f);
                Assert(JsonUtility.ToJson(original.State) == JsonUtility.ToJson(loaded.State), "Reload rerolled director or helper RNG at decision " + i);
            }

            original = New(54321); ForceEvent(original, "water");
            Call(original, "PacingUpgradeGrace");
            saved = JsonUtility.ToJson(original.State);
            loaded = new HotelSimulation(JsonUtility.FromJson<HotelState>(saved));
            int savedDraws = original.State.mvp.rngDraws;
            Pace(original, .1f); Pace(loaded, .1f);
            Assert(loaded.State.mvp.rngDraws == savedDraws && loaded.State.mvp.director.events.Count == 1 &&
                loaded.State.mvp.director.recent.Contains("water") && loaded.State.mvp.director.recoveryUntil > loaded.State.mvp.elapsed,
                "Reload lost active pressure, anti-repeat history or upgrade grace");
            Assert(JsonUtility.ToJson(original.State) == JsonUtility.ToJson(loaded.State), "Active cooldown/grace save changed resumed behavior");
        }

        private static void ResolutionAndBounds()
        {
            HotelSimulation sim = New(); HotelState state = sim.State;
            MvpEventState water = ForceEvent(sim, "water");
            state.phase = "closing"; state.mvp.elapsed = water.until + 100;
            Pace(sim); Assert(water.status == "active", "Unresolved water event expired by timer");
            RoomState room = state.rooms.Find(r => r.number == water.room);
            room.mvp.equipment.Find(e => e.kind == "sink").localFault = false; room.leak = false; room.water = .2f;
            Pace(sim); Assert(water.status == "active", "Repair erased residual cleanup pressure");
            room.water = 0; Pace(sim); Assert(water.status == "resolved", "Dry repaired source did not end water event");
            for (int i = 0; i < 80; i++)
            {
                state.mvp.director.events.Add(new MvpEventState { id = "old_" + i, kind = "vip", category = "guest", status = "completed" });
                state.mvp.director.recent.Add("vip");
                state.mvp.director.cooldowns.Add(new MvpCooldownState { id = "expired_" + i, until = 1 });
            }
            state.mvp.director.events.Add(new MvpEventState { id = "still_active", kind = "rush", category = "guest", until = state.mvp.elapsed + 100 });
            Pace(sim);
            Assert(state.mvp.director.events.Count <= HotelMvpDirector.MaxHistory && state.mvp.director.recent.Count <= HotelMvpDirector.MaxRecent &&
                state.mvp.director.cooldowns.Count <= 6, "Unbounded director records");
            Assert(state.mvp.director.events.Exists(e => e.id == "still_active" && e.status == "active"), "Pruning deleted live pressure");
        }
    }
}
