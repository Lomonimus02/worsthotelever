using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    // Pure projections; the parent native runner calls RunAll. No scene or save-file writes.
    public static class HotelDangerPresentationTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            LegacyProjection(); passed.Add("Danger presentation preserves classic targets and intent");
            CanonicalTargets(); passed.Add("Danger targets project shared geometry and reject noncanonical IDs");
            HoldPrompts(); passed.Add("Hazard, isolation, first-aid and alarm prompts use authoritative work rules");
            RescuePrompts(); passed.Add("Rescue and virtual self-help explain health, supplies and exclusive care");
            IntentGuard(); passed.Add("Down/dead/terminal snapshots cannot retain ordinary work intent");
            TerminalContinuation(); passed.Add("Dead distant host can continue every settled danger outcome");
            PriorityTargets(); passed.Add("Critical evacuation and downed colleague take priority over maintenance");
            ContractTerms(); passed.Add("Contract cards use shared mode tuning without local reward calculation");
            ReadOnlyProjection(); passed.Add("Danger UI queries leave health, clocks, supplies, progress and RNG unchanged");
            return passed;
        }
        static HotelState Fixture()
        {
            var state = new HotelState { version = 3, contentVersion = 3, phase = "open", dayLength = 1080,
                mvp = new MvpHotelState(), danger = new HotelDangerState { schema = 1, status = "active", requiredIncidents = 2, requiredService = 2, minimumSeconds = 180 } };
            for (int n = 101; n <= 106; n++)
            {
                var room = new RoomState { number = n, mvp = new MvpRoomState { owned = n <= 104 } };
                foreach (string kind in new[] { "sink", "toilet", "tv", "lamp" }) room.mvp.equipment.Add(new MvpEquipmentState { kind = kind });
                state.rooms.Add(room);
            }
            state.players.Add(new PlayerState { id = 0, position = HotelLayout.Spawn });
            state.players.Add(new PlayerState { id = 77, position = HotelLayout.Spawn + Vector3.right * .7f });
            state.danger.crew.Add(new HotelCrewState { slot = 0, joined = true, position = state.players[0].position });
            state.danger.crew.Add(new HotelCrewState { slot = 1, joined = true, position = state.players[1].position });
            state.danger.incidents.Add(new HotelIncidentState { room = 101, kind = "electric", status = "active" });
            state.danger.incidents.Add(new HotelIncidentState { room = 102, kind = "steam", status = "planned" });
            state.danger.incidents.Add(new HotelIncidentState { room = 103, kind = "fumes", status = "planned" });
            return state;
        }
        static void At(HotelState state, string target)
        {
            Require(HotelDangerRules.TryTarget(state, target, out Vector3 point), "Missing fixture target " + target);
            state.players[0].position = point; state.danger.crew[0].position = point;
        }
        static void Hold(HotelState state, string kind)
        {
            state.items.Clear(); state.players[0].held = "";
            if (kind == "") return;
            state.items.Add(new ItemState { id = "testtool", kind = kind, holder = 0 }); state.players[0].held = "testtool";
        }
        static void LegacyProjection()
        {
            var state = new HotelState { mvp = null, danger = null };
            state.rooms.Add(new RoomState { number = 101, bed = 1 }); state.players.Add(new PlayerState { id = 0 });
            Require(HotelPresentation.FocusText(state, 0, "bed_101", "").Contains("снять грязное бельё"), "Classic bed prompt changed");
            Require(!HotelPresentation.TryTarget(state, "alarm", out _), "Danger target leaked into v1");
            Require(HotelPresentation.DangerWorkIntentAllowed(state, 0, "bed_101"), "Classic input blocked");
            state.version = state.contentVersion = 2; state.mvp = new MvpHotelState();
            Require(!HotelPresentation.DangerTerminal(state) && HotelPresentation.TryTarget(state, "coffee", out _), "Classic MVP path changed");
        }
        static void CanonicalTargets()
        {
            var state = Fixture();
            foreach (string target in new[] { "firstaid", "alarm", "hazard_101", "isolate_101", "hazard_102", "isolate_103", "rescue_1", "recover_0" })
                Require(HotelPresentation.TryTarget(state, target, out Vector3 p) && HotelDangerRules.TryTarget(state, target, out Vector3 expected) && p == expected, "Projection geometry diverged: " + target);
            foreach (string target in new[] { "rescue_2", "recover_01", "recover_-1", "hazard_0101", "isolate_999", "hazard_105", "hazard_101_x" })
                Require(!HotelPresentation.TryTarget(state, target, out _), "Malformed/unowned target accepted: " + target);
            state.players.RemoveAt(1);
            Require(HotelPresentation.TryTarget(state, "rescue_1", out Vector3 body) && body == state.danger.crew[1].position, "Disconnected durable body lost");
        }
        static void HoldPrompts()
        {
            var state = Fixture(); At(state, "isolate_101");
            string label = HotelPresentation.FocusText(state, 0, "isolate_101", "");
            Require(label.Contains("Удерживайте E") && label.Contains(HotelPresentation.Seconds(HotelDangerRules.WorkSeconds(state, "isolate_101"))), "Isolation advertised as instant");
            At(state, "hazard_101");
            Require(HotelPresentation.FocusText(state, 0, "hazard_101", "").Contains(HotelDangerRules.WorkError(state, 0, "hazard_101")), "Isolation prerequisite omitted");
            state.danger.incidents[0].isolated = true; Hold(state, "toolbox");
            Require(HotelPresentation.FocusText(state, 0, "hazard_101", "").Contains("Удерживайте E"), "Isolated source repair unavailable");
            Hold(state, ""); At(state, "firstaid");
            Require(HotelPresentation.FocusText(state, 0, "firstaid", "").Contains("полное"), "Full-health medkit waste suggested");
            state.danger.crew[0].health = 48;
            Require(HotelPresentation.FocusText(state, 0, "firstaid", "").Contains("расход: 1 аптечка"), "Healing price absent");
            At(state, "alarm");
            label = HotelPresentation.FocusText(state, 0, "alarm", "");
            Require(label.Contains("Удерживайте E") && label.Contains("провален") && label.Contains("премии нет"), "Alarm looks like a harmless UI button");
        }
        static void RescuePrompts()
        {
            var state = Fixture(); var colleague = state.danger.crew[1]; colleague.health = 0; colleague.life = "downed"; colleague.bleedout = 30;
            At(state, "rescue_1");
            Require(HotelPresentation.FocusText(state, 0, "rescue_1", "").Contains("Удерживайте E"), "Rescue prompt unavailable");
            state.players[1].workTarget = "recover_1";
            Require(HotelPresentation.FocusText(state, 0, "rescue_1", "").Contains("уже оказывают"), "Exclusive self-help hidden");
            state.players[1].workTarget = ""; colleague.life = "dead"; colleague.bleedout = 0;
            Require(!HotelPresentation.FocusText(state, 0, "rescue_1", "").Contains("Удерживайте E"), "Dead crew falsely rescuable");
            var self = state.danger.crew[0]; self.life = "downed"; self.health = 0; self.bleedout = 30;
            Require(HotelPresentation.FocusText(state, 0, "recover_0", "").Contains("Удерживайте E"), "Self-help blocked by general KO gate");
            state.danger.selfRescues = 0;
            Require(HotelPresentation.FocusText(state, 0, "recover_0", "").Contains("уже использована"), "Spent emergency recovery unexplained");
            state.danger.selfRescues = 1; state.danger.medkits = 0;
            Require(HotelPresentation.FocusText(state, 0, "recover_0", "").Contains("закончились"), "Empty shared medkits unexplained");
        }
        static void IntentGuard()
        {
            var state = Fixture();
            Require(HotelPresentation.DangerWorkIntentAllowed(state, 0, "bed_101"), "Healthy legacy work denied");
            var self = state.danger.crew[0]; self.life = "downed"; self.health = 0; self.bleedout = 20;
            foreach (string target in new[] { "bed_101", "alarm", "rescue_1", "recover_1", "recover_00" })
                Require(!HotelPresentation.DangerWorkIntentAllowed(state, 0, target), "KO retained forbidden intent: " + target);
            Require(HotelPresentation.DangerWorkIntentAllowed(state, 0, "recover_0"), "Canonical self-recovery denied");
            self.life = "dead"; self.bleedout = 0;
            Require(!HotelPresentation.DangerWorkIntentAllowed(state, 0, "recover_0"), "Dead player retained work loop");
            self.life = "healthy"; self.health = 100; state.phase = "summary"; state.danger.settled = true;
            Require(!HotelPresentation.DangerWorkIntentAllowed(state, 0, "alarm"), "Terminal state retained alarm intent");
        }
        static void TerminalContinuation()
        {
            var state = Fixture(); state.players[0].position = HotelLayout.RoomCenter(104);
            state.danger.crew[0].life = "dead"; state.danger.crew[0].health = 0;
            Require(HotelPresentation.DangerNextDayReason(state, true) != "", "Next day offered during active shift");
            state.phase = "summary"; state.danger.settled = true;
            foreach (string outcome in new[] { "completed", "evacuated", "wipe", "collapse", "incomplete", "relief" })
            {
                state.danger.outcome = outcome;
                Require(HotelPresentation.DangerNextDayReason(state, true) == "", "Dead distant host locked out after " + outcome);
                Require(HotelPresentation.DangerNextDayReason(state, false) != "", "Partner given host nextday authority");
                Require(HotelPresentation.DangerOutcomeName(outcome) != "ИТОГИ КОНТРАКТА", "Untranslated result " + outcome);
            }
        }
        static void PriorityTargets()
        {
            var state = Fixture();
            Require(HotelPresentation.DangerPriorityTarget(state, 0) == "isolate_101", "Guide sends actor into unisolated source");
            state.danger.incidents[0].isolated = true;
            Require(HotelPresentation.DangerPriorityTarget(state, 0) == "hazard_101", "Guide repeats latched isolation");
            state.danger.crew[1].life = "downed"; state.danger.crew[1].health = 0; state.danger.crew[1].bleedout = 30;
            Require(HotelPresentation.DangerPriorityTarget(state, 0) == "rescue_1", "Service outranks downed colleague");
            state.danger.safety = 0;
            Require(HotelPresentation.DangerPriorityTarget(state, 0) == "alarm", "Critical evacuation marker absent");
            state.danger.crew[0].life = "downed"; state.danger.crew[0].health = 0; state.danger.crew[0].bleedout = 20;
            Require(HotelPresentation.DangerPriorityTarget(state, 0) == "", "Downed player told to walk");
        }
        static void ContractTerms()
        {
            foreach (string mode in new[] { "standard", "bold" })
            {
                string terms = HotelPresentation.DangerModeTerms(mode);
                Require(terms.Contains("Сервис: " + HotelDangerRules.ServiceGoal(mode)) && terms.Contains("аварии: " + HotelDangerRules.IncidentGoal(mode)), "Mode card duplicated stale goals");
                Require(terms.Contains("+" + HotelDangerRules.Reward(mode)) && terms.Contains("−" + HotelDangerRules.Penalty(mode)), "Mode card omitted reward/cost");
            }
            Require(HotelPresentation.DangerModeTerms("relief").Contains("без опасных"), "Relief advertised as rewarded danger contract");
        }
        static void ReadOnlyProjection()
        {
            var state = Fixture(); state.danger.crew[1].life = "downed"; state.danger.crew[1].health = 0; state.danger.crew[1].bleedout = 25;
            state.mvp.rngState = 4567; state.mvp.rngDraws = 12; state.danger.criticalRemaining = 16;
            string before = JsonUtility.ToJson(state);
            for (int i = 0; i < 3; i++)
            {
                HotelPresentation.MvpTasks(state); HotelPresentation.DangerPriorityTarget(state, 0);
                HotelPresentation.DangerTerminal(state); HotelPresentation.DangerNextDayReason(state, true);
                HotelPresentation.DangerCrewLabel(state, state.danger.crew[1], 0);
                foreach (string target in new[] { "firstaid", "alarm", "isolate_101", "hazard_101", "rescue_1", "recover_0" })
                {
                    HotelPresentation.TryTarget(state, target, out _); HotelPresentation.FocusText(state, 0, target, "");
                    HotelPresentation.DangerTargetName(state, target); HotelPresentation.DangerWorkIntentAllowed(state, 0, target);
                }
                foreach (var incident in state.danger.incidents) HotelPresentation.DangerIncidentLabel(incident);
                HotelDangerRules.Objective(state);
            }
            Require(before == JsonUtility.ToJson(state), "Presentation mutated authoritative danger/RNG snapshot");
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
