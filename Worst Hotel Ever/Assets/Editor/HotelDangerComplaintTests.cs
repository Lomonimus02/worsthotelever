using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace WorstHotel
{
    // Native runner only. Preparation may ready rooms; guests, warnings, isolation,
    // relocation/checkout and repairs are produced by commands and authoritative ticks.
    public static class HotelDangerComplaintTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            foreach (string kind in new[] { "electric", "steam", "fumes" })
            {
                Run(passed, kind + ": checkout repair preserves deferred complaint through save/wire", () => RepairWhileDeferred(kind, false));
                Run(passed, kind + ": relocation repair preserves deferred complaint through save/wire", () => RepairWhileDeferred(kind, true));
            }
            Run(passed, "Resolved danger cause tolerates a pruned guest; snapshot reference policy stays explicit", PrunedGuestCause);
            Run(passed, "Continuing relocated guest: real victory/nextday retires old danger without recovery reward", ContinuingGuestNextDay);
            return passed;
        }

        private static void ContinuingGuestNextDay()
        {
            HotelSimulation sim = Prepared("electric");
            HotelState state = sim.State;
            // Author only the stay duration before arrival; check-in creates the matching booking.
            MvpScheduleState offer = state.mvp.schedule.Find(s => s.day == state.day && s.status == "planned" && s.time == 0);
            Need(offer != null && string.IsNullOrEmpty(offer.reservationId), "Expected the unreserved opening offer.");
            offer.contract.departureDay = offer.contract.arrivalDay + 2; offer.contract.billableNights = 2;
            HotelSaveStore.Validate(state);
            HotelIncidentState first = state.danger.incidents[0], last = state.danger.incidents[1];
            At(sim, HotelLayout.Target("desk")); Command(sim, "open");
            GuestState guest = state.guests.Find(g => g.id == offer.guestId);
            Need(guest != null, "Continuing guest did not arrive.");
            Command(sim, "checkin", number: last.room, guestId: guest.id);
            MvpReservationState booking = state.mvp.reservations.Find(r => r.id == guest.mvp.reservationId);
            Need(booking != null && booking.departureDay == offer.contract.departureDay &&
                booking.contract.billableNights == 2 && guest.mvp.billableNights == 2, "Stay duration links disagree.");
            ItemState bag = state.items.Find(i => !i.consumed && i.kind == "bag" && i.ownerGuest == guest.id);
            Need(bag != null, "Guest luggage missing.");
            At(sim, bag.position); Command(sim, "pickup", bag.id);
            At(sim, HotelLayout.RoomTarget("bag", last.room)); Command(sim, "interact", "bag_" + last.room);
            Until(sim, () => guest.stage == "staying", 40, "Continuing guest did not reach the room.");
            Until(sim, () => first.status == "warning", 80, "First warning missing.");
            Work(sim, "isolate_" + first.room, HotelDangerRules.Control(first.room));
            ItemState tool = state.items.Find(i => !i.consumed && i.holder == -1 && i.kind == "toolbox");
            Need(tool != null, "First repair tool missing.");
            At(sim, tool.position); Command(sim, "pickup", tool.id);
            Work(sim, "hazard_" + first.room, HotelDangerRules.Source(first));
            PlayerState player = state.players.Find(p => p.id == 0);
            Need(sim.Execute(0, new HotelCommand("drop") { position = player.position }) == "", "Could not release the first repair tool.");

            string cause = "danger:" + last.kind + ":" + last.room + ":" + state.danger.serial;
            Until(sim, () => state.mvp.complaints.Exists(c => c.guestId == guest.id && c.causeKey == cause), 180, "Last warning complaint missing.");
            Work(sim, "isolate_" + last.room, HotelDangerRules.Control(last.room));
            Until(sim, () => state.danger.elapsed >= state.danger.minimumSeconds, 300, "Contract minimum time did not elapse.");
            Need(state.danger.servicePoints >= state.danger.requiredService, "Real check-in and bag delivery did not meet service goal.");
            At(sim, HotelLayout.Target("desk")); Command(sim, "relocate", number: 205 - last.room, guestId: guest.id);
            MvpComplaintState complaint = state.mvp.complaints.Find(c => c.guestId == guest.id && c.causeKey == cause);
            Need(complaint != null && complaint.status == "active" && !complaint.recovered, "Expected deferred hazard complaint.");
            string deferred = JsonUtility.ToJson(complaint);
            tool = state.items.Find(i => !i.consumed && i.holder == -1 && i.kind == (last.kind == "fumes" ? "mop" : "toolbox"));
            Need(tool != null, "Last repair tool missing.");
            At(sim, tool.position); Command(sim, "pickup", tool.id);
            Work(sim, "hazard_" + last.room, HotelDangerRules.Source(last));
            Need(guest.stage == "walking" && JsonUtility.ToJson(complaint) == deferred, "Repair lost the deferred walking complaint.");
            At(sim, HotelLayout.Target("desk")); Command(sim, "finish");
            Need(state.phase == "summary" && state.danger.status == "won" && state.danger.outcome == "completed" &&
                guest.stage == "walking" && !guest.paid && JsonUtility.ToJson(complaint) == deferred, "Actual victory did not retain the continuing deferred guest.");
            int serial = state.danger.serial;
            float satisfaction = guest.satisfaction, elapsed = state.mvp.elapsed;
            string memories = string.Join("\n", guest.memories.ToArray());
            var expected = JsonUtility.FromJson<MvpComplaintState>(deferred);
            expected.status = "resolved"; expected.recoveredAt = elapsed;
            Command(sim, "nextday");
            Need(state.phase == "preparation" && state.danger.serial == serial + 1 && state.danger.status == "briefing" &&
                state.danger.incidents.TrueForAll(i => i.status == "planned"), "Next day did not prepare a fresh forecast.");
            Need(state.guests.Exists(g => g.id == guest.id) && guest.stage == "walking" && !guest.paid &&
                guest.satisfaction == satisfaction && string.Join("\n", guest.memories.ToArray()) == memories, "Overnight retirement changed the continuing guest or granted satisfaction.");
            Need(JsonUtility.ToJson(complaint) == JsonUtility.ToJson(expected) && !complaint.recovered, "Old danger history was lost or rewarded at the boundary.");
            Roundtrip(state, complaint.id, JsonUtility.ToJson(expected));
        }

        private static void PrunedGuestCause()
        {
            HotelState state = HotelSimulation.CreateNewDanger(173).State;
            HotelIncidentState incident = state.danger.incidents[0];
            var complaint = new MvpComplaintState { id = "pruned_danger_history", guestId = state.nextGuest++, status = "resolved",
                category = incident.kind == "electric" ? "tv" : incident.kind == "steam" ? "toilet" : "dirt",
                causeKey = "danger:" + incident.kind + ":" + incident.room + ":" + state.danger.serial };
            state.mvp.complaints.Add(complaint);
            var rooms = new Dictionary<int, RoomState>();
            foreach (RoomState room in state.rooms) rooms.Add(room.number, room);
            // Isolate the guard under review: no surviving guest, luggage, booking or request.
            MethodInfo cause = typeof(HotelMvpValidation).GetMethod("ComplaintCause", BindingFlags.Static | BindingFlags.NonPublic);
            Need(cause != null, "ComplaintCause validation hook missing.");
            string before = JsonUtility.ToJson(state);
            cause.Invoke(null, new object[] { complaint, state, rooms, new Dictionary<int, GuestState>(), new Dictionary<string, MvpRequestState>() });
            Need(JsonUtility.ToJson(state) == before && !complaint.recovered, "Historical cause validation manufactured recovery or changed history.");
            // The enclosing validator still forbids orphan complaints, including resolved ones.
            // Do not misreport the cause-level guard as permitting such a full saved snapshot.
            try { HotelSaveStore.Validate(state); }
            catch (InvalidDataException error)
            { Need(error.Message == "Invalid complaint/reference/cause.", "Unexpected orphan-reference rejection: " + error.Message); return; }
            throw new Exception("Snapshot orphan-complaint policy changed; update this explicit boundary test.");
        }

        private static void RepairWhileDeferred(string kind, bool relocate)
        {
            HotelSimulation sim = Prepared(kind);
            HotelState state = sim.State;
            HotelIncidentState incident = state.danger.incidents[0];
            At(sim, HotelLayout.Target("desk")); Command(sim, "open");
            GuestState guest = state.guests.Find(g => g.stage == "queue");
            Need(guest != null, "Opening guest did not arrive.");
            Command(sim, "checkin", number: incident.room, guestId: guest.id);
            Until(sim, () => guest.stage == "staying", 40, "Guest never reached the hazard room.");
            string cause = "danger:" + kind + ":" + incident.room + ":" + state.danger.serial;
            Until(sim, () => state.mvp.complaints.Exists(c => c.guestId == guest.id && c.causeKey == cause),
                80, "Warning did not create a factual guest complaint.");
            Need(incident.status == "warning", "Fixture missed the warning window.");
            Work(sim, "isolate_" + incident.room, HotelDangerRules.Control(incident.room));
            Need(incident.isolated, "Real isolation did not complete.");

            if (relocate)
            {
                // Opposite side and another row: the real walking route is longer than repair.
                int destination = 205 - incident.room; // 101<->104, 102<->103.
                At(sim, HotelLayout.Target("desk"));
                Command(sim, "relocate", number: destination, guestId: guest.id);
                Need(guest.stage == "walking" && guest.room == destination, "Relocation command did not start walking.");
            }
            else Until(sim, () => guest.stage == "checkout", state.dayLength + 1, "Guest never started natural checkout.");

            MvpComplaintState complaint = state.mvp.complaints.Find(c => c.guestId == guest.id && c.causeKey == cause);
            Need(complaint != null && complaint.status == "active" && !complaint.recovered && complaint.recoveredAt == 0,
                "Complaint was already recovered before the deferred repair.");
            string history = JsonUtility.ToJson(complaint);
            string stage = guest.stage;
            ItemState tool = state.items.Find(i => !i.consumed && i.holder == -1 && i.kind == (kind == "fumes" ? "mop" : "toolbox"));
            Need(tool != null, "Repair tool missing.");
            At(sim, tool.position); Command(sim, "pickup", tool.id);
            Work(sim, "hazard_" + incident.room, HotelDangerRules.Source(incident));
            Need(incident.status == "resolved" && !incident.isolated, "Real repair did not resolve the incident.");
            Need(guest.stage == stage, "Guest left the deferred stage before the assertion.");
            Need(JsonUtility.ToJson(complaint) == history, "Repair changed historical complaint or manufactured recovery.");
            Roundtrip(state, complaint.id, history);
            StrictReferences(state, complaint.id, incident.room, kind);
        }

        private static HotelSimulation Prepared(string kind)
        {
            HotelSimulation sim = null;
            for (int seed = 1; seed <= 128; seed++)
            {
                var candidate = HotelSimulation.CreateNewDanger(seed);
                if (candidate.State.danger.incidents[0].kind == kind) { sim = candidate; break; }
            }
            Need(sim != null, "No deterministic first-incident fixture for " + kind);
            // Only room readiness is authored. No guest stages, complaints, incident progress,
            // clocks, work leases or outcome/recovery flags are manufactured in this fixture.
            foreach (RoomState room in sim.State.rooms)
            {
                if (!room.mvp.owned) continue;
                room.bed = 2; room.trash = room.leak = room.outOfService = false;
                room.towel = true; room.water = 0; room.upgraded = true;
                room.mvp.bedQuality = room.mvp.tvQuality = 2;
                room.mvp.dirt = room.mvp.binFill = room.mvp.towelUseProgress = 0;
                room.mvp.dirtyTowels = 0;
                foreach (MvpEquipmentState equipment in room.mvp.equipment)
                { equipment.installed = true; equipment.localFault = false; equipment.quality = 2; equipment.wear = 0; }
            }
            sim.Join(0); Command(sim, "endGuidedOpening");
            HotelSaveStore.Validate(sim.State);
            return sim;
        }

        private static void Roundtrip(HotelState state, string complaintId, string history)
        {
            string before = JsonUtility.ToJson(state);
            HotelSaveStore.Validate(state);
            string wire = HotelSnapshotCodec.Decode(HotelSnapshotCodec.Encode(before));
            Need(wire == before, "Snapshot codec changed the authoritative state.");
            HotelState decoded = JsonUtility.FromJson<HotelState>(wire);
            HotelSaveStore.Validate(decoded); // v3 has no legacy normalization.
            Need(JsonUtility.ToJson(decoded.mvp.complaints.Find(c => c.id == complaintId)) == history, "Wire lost deferred complaint history.");
            string root = Path.GetFullPath(Path.GetTempPath());
            string name = "WorstHotelDangerComplaint-" + Guid.NewGuid().ToString("N");
            string directory = Path.GetFullPath(Path.Combine(root, name));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "checkpoint.json");
                HotelSaveStore.Save(state, path);
                HotelState loaded = HotelSaveStore.Load(path);
                HotelSaveStore.Validate(loaded);
                Need(loaded.players.Count == 0, "Save load retained disposable connections.");
                Need(JsonUtility.ToJson(loaded.mvp.complaints.Find(c => c.id == complaintId)) == history, "Save load recovered or lost historical complaint.");
                Need(JsonUtility.ToJson(loaded.danger) == JsonUtility.ToJson(state.danger), "Save load changed danger progress.");
                Need(JsonUtility.ToJson(state) == before, "Validation/save mutated the live hotel.");
            }
            finally
            {
                Need(string.Equals(Path.GetDirectoryName(directory).TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase) && Path.GetFileName(directory) == name, "Unsafe temporary fixture cleanup path.");
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void StrictReferences(HotelState original, string complaintId, int room, string kind)
        {
            RejectCause(original, complaintId, (s, c) => s.guests.Find(g => g.id == c.guestId).stage = "staying");
            RejectCause(original, complaintId, (s, c) => c.causeKey = "danger:" + kind + ":" + room + ":" + (s.danger.serial + 1));
            RejectCause(original, complaintId, (s, c) => { s.day++; s.danger.day++; s.danger.serial++; }); // Valid older serial, not current.
            RejectCause(original, complaintId, (s, c) => c.causeKey = "danger:" + kind + ":0" + room + ":" + s.danger.serial);
            RejectCause(original, complaintId, (s, c) => c.causeKey = "danger:" + kind + ":" + (205 - room) + ":" + s.danger.serial);
            RejectCause(original, complaintId, (s, c) =>
            {
                string other = kind == "electric" ? "steam" : "electric";
                c.category = other == "steam" ? "toilet" : "tv";
                c.causeKey = "danger:" + other + ":" + room + ":" + s.danger.serial;
            });
            RejectCause(original, complaintId, (s, c) => c.category = kind == "electric" ? "toilet" : "tv");
        }

        private static void RejectCause(HotelState original, string complaintId, Action<HotelState, MvpComplaintState> change)
        {
            // Corruption probes are detached copies of the real command-generated regression.
            HotelState copy = JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(original));
            change(copy, copy.mvp.complaints.Find(c => c.id == complaintId));
            try { HotelSaveStore.Validate(copy); }
            catch (InvalidDataException error)
            { Need(error.Message == "Invalid complaint cause/reference.", "Probe failed an unrelated invariant: " + error.Message); return; }
            throw new Exception("Invalid historical danger cause was accepted.");
        }

        private static void At(HotelSimulation sim, Vector3 position)
        {
            position.y = .1f;
            Need(sim.Execute(0, new HotelCommand("pose") { position = position }) == "", "Fixture pose rejected: " + position);
        }
        private static void Command(HotelSimulation sim, string action, string target = "", int number = 0, int guestId = 0)
        {
            string error = sim.Execute(0, new HotelCommand(action, target, number) { guestId = guestId });
            Need(error == "", action + " " + target + ": " + error);
            HotelSaveStore.Validate(sim.State);
        }
        private static void Work(HotelSimulation sim, string target, Vector3 position)
        {
            At(sim, position); Command(sim, "beginwork", target);
            PlayerState player = sim.State.players.Find(p => p.id == 0);
            float worked = 0;
            for (; worked < 10 && player.workTarget == target; worked += .1f)
            { Command(sim, "heartbeat", target); sim.Tick(.1f); }
            Need(player.workTarget == "" && worked + .01f >= HotelDangerRules.WorkSeconds(sim.State, target), "Hold did not complete: " + target);
            HotelSaveStore.Validate(sim.State);
        }
        private static void Until(HotelSimulation sim, Func<bool> condition, float seconds, string message)
        {
            for (float elapsed = 0; !condition() && elapsed < seconds; elapsed += .1f) sim.Tick(.1f);
            Need(condition(), message); HotelSaveStore.Validate(sim.State);
        }
        private static void Run(List<string> passed, string name, Action test)
        { try { test(); passed.Add(name); } catch (Exception error) { throw new Exception("HotelDangerComplaintTests FAILED: " + name, error); } }
        private static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
