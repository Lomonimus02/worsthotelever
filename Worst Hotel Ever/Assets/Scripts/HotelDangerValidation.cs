using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorstHotel
{
    // Pure snapshot checks. No normalization, gameplay, clock advancement or random draws.
    // Called after the common MVP room/guest checks and before danger target queries.
    public static class HotelDangerValidation
    {
        public const int MaxCrew = 2, MaxIncidents = 3;
        private const float MaxClock = 1000000000000f;
        private const int MaxCounter = 100000000;

        public static void Validate(HotelState state)
        {
            HotelSaveStore.RejectFutureVersions(state);
            Need(state != null && state.version == 3 && state.contentVersion == 3 && state.mvp != null && state.mvp.schema == 2 && state.danger != null,
                "Missing or mismatched danger schema.");
            HotelDangerState d = state.danger;
            Need(d.schema == 1, "Danger state is missing its explicit schema marker.");
            Need(d.day == state.day && d.day > 0 && d.serial > 0 && d.serial <= d.day, "Invalid danger day/serial.");
            Need(OneOf(d.mode, "relief", "standard", "bold") && OneOf(d.status, "briefing", "active", "won", "failed"), "Unknown contract mode/status.");
            Need(Counter(d.wins) && Counter(d.losses) && Counter(d.streak) && Counter(d.bestStreak) &&
                (long)d.wins + d.losses <= d.serial && d.streak <= d.bestStreak && d.bestStreak <= d.wins, "Invalid contract lifetime counters.");
            Need(Clock(d.elapsed) && Range(d.safety, 0, 100) && Range(d.criticalRemaining, 0, 30) && Clock(d.graceUntil), "Invalid danger clocks/safety.");
            Need(d.medkits >= 0 && d.medkits <= 3 && d.selfRescues >= 0 && d.selfRescues <= 1, "Invalid shared medical supplies.");
            Need(d.servicePoints >= 0 && d.servicePoints <= HotelDangerRules.MaxServicePoints && d.resolved >= 0 && d.resolved <= MaxIncidents, "Invalid contract progress.");
            Text(d.outcome, 32); Text(d.reason, 512);
            Tuning(d);
            Need(d.crew != null && d.crew.Count == MaxCrew && d.incidents != null && d.incidents.Count == MaxIncidents,
                "Missing crew or complete incident forecast.");
            Need(state.rooms != null && state.players != null, "Missing danger room/player context.");

            var slots = new HashSet<int>();
            foreach (HotelCrewState c in d.crew)
            {
                Need(c != null && c.slot >= 0 && c.slot < MaxCrew && slots.Add(c.slot), "Missing/duplicate durable crew slot.");
                Need(OneOf(c.life, "healthy", "downed", "dead") && Range(c.health, 0, 100) &&
                    Range(c.bleedout, 0, 45) && Range(c.shield, 0, 7) && Counter(c.downs) && Counter(c.rescues), "Invalid crew health/timers/counters.");
                Need(HotelSimulation.Walkable(c.position), "Durable crew position is outside the hotel.");
                Text(c.lastHit, 128);
                Need(c.life == "healthy" ? c.health > 0 && c.bleedout == 0 :
                    c.health == 0 && (c.life == "downed" ? c.bleedout > 0 : c.bleedout == 0), "Crew life disagrees with health/bleedout.");
                Need(c.life == "healthy" || (c.joined && c.downs > 0 && c.shield == 0), "Incapacitated crew lacks participation/down state.");
                if (!c.joined) Need(c.life == "healthy" && c.health == 100 && c.shield == 0 && c.downs == 0 && c.rescues == 0,
                    "Unjoined crew carries a previous injury or rescue.");
            }

            var occupiedSlots = new HashSet<int>();
            var recoverySlots = new HashSet<int>();
            foreach (PlayerState player in state.players)
            {
                Need(player != null && player.id <= long.MaxValue, "Invalid connected danger player.");
                int slot = HotelDangerRules.Slot(player.id);
                Need(occupiedSlots.Add(slot), "Two connections claim the same durable crew slot.");
                HotelCrewState c = d.crew.Find(x => x.slot == slot);
                Need(c != null && c.joined, "Connected player has no participating durable crew.");
                if (player.workTarget == "rescue_0" || player.workTarget == "recover_0") Need(recoverySlots.Add(0), "Two leases recover the same crew member.");
                if (player.workTarget == "rescue_1" || player.workTarget == "recover_1") Need(recoverySlots.Add(1), "Two leases recover the same crew member.");
                if (c.life != "healthy")
                {
                    Need(string.IsNullOrEmpty(player.held), "Incapacitated crew retained an item.");
                    Need(string.IsNullOrEmpty(player.workTarget) || (c.life == "downed" && player.workTarget == "recover_" + c.slot),
                        "Incapacitated crew retained an ordinary work lease.");
                }
            }

            var rooms = new HashSet<int>();
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            int resolved = 0, live = 0;
            for (int index = 0; index < d.incidents.Count; index++)
            {
                HotelIncidentState i = d.incidents[index];
                Need(i != null && rooms.Add(i.room) && state.rooms.Exists(r => r != null && r.number == i.room && r.mvp != null && r.mvp.owned),
                    "Incident references a duplicate/unowned room.");
                Need(OneOf(i.kind, "electric", "steam", "fumes") && kinds.Add(i.kind) &&
                    OneOf(i.status, "planned", "warning", "active", "resolved"), "Unknown/duplicate incident kind or status.");
                Need(Clock(i.triggerAt) && Range(i.warningRemaining, 0, 10) && Clock(i.activeSeconds) && i.activeSeconds <= d.elapsed + .11f,
                    "Invalid incident timing.");
                if (i.status == "planned") Need(!i.isolated && i.warningRemaining == 0 && i.activeSeconds == 0, "Planned incident is already running.");
                if (i.status == "warning") Need(i.warningRemaining > 0 && i.activeSeconds == 0, "Invalid warning countdown.");
                if (i.status == "active" || i.status == "resolved") Need(i.warningRemaining == 0, "Active/resolved incident retains a warning.");
                if (i.status == "resolved") Need(!i.isolated, "Resolved incident retains isolation.");
                if (index >= d.requiredIncidents) Need(i.status == "planned", "An unselected forecast incident started.");
                if (i.status == "resolved") resolved++;
                if (i.status == "warning" || i.status == "active") live++;
            }
            Need(d.resolved == resolved && live <= HotelDangerRules.SimultaneousIncidents(d.mode), "Incident counters/concurrency disagree.");
            Outcome(state);
        }

        private static void Tuning(HotelDangerState d)
        {
            int incidents = HotelDangerRules.IncidentGoal(d.mode), service = HotelDangerRules.ServiceGoal(d.mode);
            float seconds = HotelDangerRules.MinimumSeconds(d.mode);
            int reward = HotelDangerRules.Reward(d.mode), penalty = HotelDangerRules.Penalty(d.mode);
            Need(d.requiredIncidents == incidents && d.requiredService == service && d.minimumSeconds == seconds &&
                d.reward == reward && d.penalty == penalty, "Contract mode/tuning mismatch.");
            Need(d.paidReward >= 0 && d.paidReward <= reward && d.chargedPenalty >= 0 && d.chargedPenalty <= penalty,
                "Invalid contract settlement amounts.");
        }

        private static void Outcome(HotelState state)
        {
            HotelDangerState d = state.danger;
            bool terminal = d.status == "won" || d.status == "failed";
            Need(terminal == d.settled, "Terminal contract/settlement guard mismatch.");
            Need(d.status == "briefing" ? state.phase == "preparation" : terminal ? state.phase == "summary" :
                state.phase == "open" || state.phase == "closing", "Contract status disagrees with hotel phase.");
            if (!terminal)
            {
                Need(string.IsNullOrEmpty(d.outcome) && string.IsNullOrEmpty(d.reason) && d.paidReward == 0 && d.chargedPenalty == 0,
                    "Unsettled contract has an outcome/payment.");
                if (d.status == "briefing")
                {
                    Need(d.elapsed == 0 && d.graceUntil == 0 && d.resolved == 0 && d.servicePoints == 0 && d.safety == 100 && d.criticalRemaining == 30 &&
                        d.medkits == HotelDangerRules.InitialMedkits && d.selfRescues == HotelDangerRules.InitialSelfRescues &&
                        d.crew.TrueForAll(c => c.life == "healthy" && c.health == 100 && c.bleedout == 0 && c.shield == 0 && c.downs == 0 && c.rescues == 0) &&
                        d.incidents.TrueForAll(i => i.status == "planned"), "Preparation retained active danger/progress.");
                }
                return;
            }
            Need(!string.IsNullOrWhiteSpace(d.reason), "Terminal contract is missing its explanation.");
            if (d.mode == "relief" && d.status == "won")
            {
                Need(d.outcome == "relief" && d.paidReward == 0 && d.chargedPenalty == 0, "Relief contract awarded a payout/penalty.");
                return;
            }
            if (d.status == "won")
                Need(d.outcome == "completed" && d.paidReward == d.reward && d.chargedPenalty == 0 && d.wins > 0 && d.streak > 0 &&
                    d.resolved == d.requiredIncidents && d.servicePoints >= d.requiredService && d.elapsed >= d.minimumSeconds &&
                    d.crew.Exists(c => c.joined) && d.crew.TrueForAll(c => !c.joined || c.life == "healthy"), "Contract victory has unmet goals or invalid payment.");
            else
                Need(OneOf(d.outcome, "evacuated", "wipe", "collapse", "incomplete") && d.paidReward == 0 &&
                    d.chargedPenalty == d.penalty && d.losses > 0 && d.streak == 0, "Contract failure has an invalid outcome/penalty.");
        }

        // Pure Rule API is the single authority for canonical IDs and authored target geometry.
        internal static bool Target(HotelState state, string target)
        {
            return state.version == 3 && HotelDangerRules.IsWorkTarget(target) &&
                HotelDangerRules.TryTarget(state, target, out Vector3 position) && HotelSimulation.Finite(position);
        }

        internal static void ValidateWork(HotelState state, PlayerState player, ItemState held)
        {
            Need(Target(state, player.workTarget), "Unknown danger work target.");
            Need(state.danger.status == "active" && !state.danger.settled, "Danger work outside an active contract.");
            string target = player.workTarget;
            HotelCrewState actor = HotelDangerRules.Crew(state, player.id);
            bool recovery = target.StartsWith("recover_", StringComparison.Ordinal);
            Need(actor != null && actor.joined && (recovery ? actor.life == "downed" : HotelDangerRules.CanAct(state, player.id)),
                "Danger lease has an incapacitated/missing worker.");
            // Do not call WorkError here: another player's completion may exhaust a shared kit
            // after this lease's check in the same Step. The next heartbeat/Step cancels it;
            // Load discards all leases. Availability is gameplay, not persistent corruption.
            if (recovery || target.StartsWith("rescue_", StringComparison.Ordinal))
            {
                int slot = target[target.Length - 1] - '0'; // TryTarget already proved canonical 0/1.
                HotelCrewState patient = state.danger.crew.Find(c => c.slot == slot);
                Need(patient != null && patient.joined && patient.life == "downed" &&
                    (recovery ? patient.slot == actor.slot : patient.slot != actor.slot), "Invalid recovery/rescue participant.");
            }
            if (target.StartsWith("hazard_", StringComparison.Ordinal) || target.StartsWith("isolate_", StringComparison.Ordinal))
            {
                int room = int.Parse(target.Substring(target.IndexOf('_') + 1), System.Globalization.CultureInfo.InvariantCulture);
                HotelIncidentState incident = HotelDangerRules.Incident(state, room);
                Need(incident != null && (incident.status == "warning" || incident.status == "active"), "Lease lost its unresolved incident.");
                if (target.StartsWith("hazard_", StringComparison.Ordinal))
                {
                    Need(incident.isolated && held != null && held.kind == (incident.kind == "fumes" ? "mop" : "toolbox"),
                        "Danger repair lost isolation or its tool.");
                    return;
                }
                Need(!incident.isolated, "Isolation lease targets an already isolated incident.");
            }
            Need(held == null, "Danger interaction needs an empty hand.");
        }

        private static bool Counter(int value) { return value >= 0 && value < MaxCounter; }
        private static bool Clock(float value) { return Range(value, 0, MaxClock); }
        private static bool Range(float value, float min, float max) { return HotelSimulation.Finite(value) && value >= min && value <= max; }
        private static bool OneOf(string value, params string[] choices) { foreach (string choice in choices) if (value == choice) return true; return false; }
        private static void Text(string value, int max) { Need(value != null && value.Length <= max, "Missing/oversized danger text."); }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
