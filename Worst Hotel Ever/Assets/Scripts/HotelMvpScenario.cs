using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    // Explicitly invoked test helper; no MonoBehaviour, startup hook, file IO or production input path.
    // Positions go through Execute("pose"). This exercises authority, not walking/input/network UX.
    public static class HotelMvpScenario
    {
        private static readonly string[] Purchases = { "bed", "tv", "room105", "room106", "toolbox", "linen", "coffee", "cart" };

        public sealed class Evidence
        {
            public int Days, Steps, Commands, Heartbeats, CheckIns, CheckOuts, RepeatPaymentRejections;
            public int PaidGuests, Income, DirtyDisposals, DirtyTowelDisposals, EquipmentRepairs, UtilityRepairs;
            public int ContinuingBoundaryCrossings, PendingBookingObservations, PeakSnapshotBytes;
            public int RewardStabilityProbes;
            public readonly HashSet<string> PurchasesMade = new HashSet<string>();
            public readonly HashSet<string> FulfilledKinds = new HashSet<string>();
            public readonly HashSet<string> WorkKinds = new HashSet<string>();
            public readonly HashSet<string> Archetypes = new HashSet<string>();
            internal readonly HashSet<int> Paid = new HashSet<int>();
            internal readonly Dictionary<int, string> Contracts = new Dictionary<int, string>();
            internal readonly Dictionary<string, bool> Rewarded = new Dictionary<string, bool>();
            internal readonly Dictionary<int, string> Reviews = new Dictionary<int, string>();
        }

        // Starts in preparation, completes count FULL shifts, returns in the next preparation.
        // Joins local test actors 0/1, alternates work actor by day, and releases both before returning.
        public static List<string> AdvanceDays(HotelSimulation sim, int count)
        { return AdvanceDays(sim, count, new Evidence()); }

        public static List<string> AdvanceDays(HotelSimulation sim, int count, Evidence evidence, bool buyUpgrades = true)
        {
            Need(sim != null && evidence != null && count >= 1 && count <= 10, "Invalid scenario arguments.");
            Need(sim.State.mvp != null && sim.State.phase == "preparation", "AdvanceDays needs MVP preparation.");
            Need(Math.Abs(sim.State.dayLength - 1080) < .01f, "The soak must use full 1080-second days.");
            var bot = new Bot(sim, evidence, buyUpgrades);
            var lines = new List<string>();
            try
            {
                for (int i = 0; i < count; i++) lines.Add(bot.Day());
            }
            finally { sim.Leave(0); sim.Leave(1); }
            lines.Add(AssertCheckpoint(sim.State));
            return lines;
        }

        // Pure assertion: safe for a caller's isolated checkpoint before Save and after Load/Resume.
        public static string AssertCheckpoint(HotelState state)
        {
            string before = JsonUtility.ToJson(state);
            HotelSaveStore.Validate(state);
            Need(state.mvp != null, "MVP payload missing.");
            HotelMvpValidation.Validate(state);
            Need(before == JsonUtility.ToJson(state), "Validation mutated state or RNG.");
            byte[] wire = HotelSnapshotCodec.Encode(before);
            Need(wire.Length + 16 <= HotelMvpValidation.MaxSnapshotBytes, "Compressed snapshot exceeded transport bound.");
            Need(Encoding.Unicode.GetByteCount(before) + 16 <= HotelMvpValidation.MaxSnapshotBytes, "Expanded snapshot exceeded bound.");
            Need(HotelSnapshotCodec.Decode(wire) == before, "Snapshot codec changed the world.");
            Need(state.guests.Count <= HotelMvpValidation.MaxGuests && state.items.Count <= HotelMvpValidation.MaxItems,
                "Guest/item state grew beyond its bounds.");
            Need(state.mvp.requests.Count <= HotelMvpValidation.MaxRequests && state.mvp.complaints.Count <= HotelMvpValidation.MaxComplaints &&
                state.mvp.reservations.Count <= HotelMvpValidation.MaxReservations && state.mvp.schedule.Count <= HotelMvpValidation.MaxSchedule &&
                state.mvp.groups.Count <= HotelMvpValidation.MaxGroups && state.mvp.reviews.Count <= HotelMvpValidation.MaxReviews &&
                state.mvp.director.events.Count <= HotelMvpValidation.MaxEvents && state.mvp.director.cooldowns.Count <= HotelMvpValidation.MaxCooldowns &&
                state.mvp.director.recent.Count <= HotelMvpValidation.MaxRecent && state.mvp.pings.Count <= HotelMvpValidation.MaxPings,
                "Hospitality/director history is unbounded.");
            var occupants = new HashSet<int>();
            foreach (RoomState room in state.rooms)
            {
                if (room.guestId == 0) continue;
                Need(occupants.Add(room.guestId), "One guest occupies multiple rooms.");
                GuestState guest = state.guests.Find(g => g.id == room.guestId);
                Need(guest != null && !guest.paid && Occupying(guest) && guest.room == room.number && room.mvp.owned,
                    "Occupied room lost its live unpaid guest.");
            }
            foreach (GuestState guest in state.guests)
                if (Occupying(guest)) Need(!guest.paid && occupants.Contains(guest.id), "Live guest has no unique room.");
            for (int i = 0; i < state.mvp.reservations.Count; i++)
            {
                MvpReservationState a = state.mvp.reservations[i];
                if (!Committed(a)) continue;
                for (int j = i + 1; j < state.mvp.reservations.Count; j++)
                {
                    MvpReservationState b = state.mvp.reservations[j];
                    Need(!Committed(b) || a.room != b.room || a.arrivalDay >= b.departureDay || b.arrivalDay >= a.departureDay,
                        "Committed reservation intervals overlap in room " + a.room + ".");
                }
            }
            var reviews = new HashSet<int>();
            foreach (MvpReviewState review in state.mvp.reviews)
                Need(reviews.Add(review.guestId) && review.stars >= 1 && review.stars <= 5, "Duplicate or invalid guest review.");
            return "checkpoint day=" + state.day + " phase=" + state.phase + " guests=" + state.guests.Count +
                " items=" + state.items.Count + " reservations=" + state.mvp.reservations.Count +
                " snapshot=" + (Encoding.Unicode.GetByteCount(before) + 16) + " wire=" + (wire.Length + 16) +
                " rngDraws=" + state.mvp.rngDraws;
        }

        public static void AssertTenDayCoverage(HotelState state, Evidence proof)
        {
            Need(proof.Days == 10 && state.day == 11, "Ten complete days were not played.");
            Need(proof.CheckIns >= 10 && proof.CheckOuts >= 10 && proof.PaidGuests >= 10 && proof.Income > 0,
                "Opening/finishing shifts without actual stays does not satisfy the soak.");
            Need(proof.RepeatPaymentRejections == proof.CheckOuts, "Every manual payment needs a duplicate-payment probe.");
            Need(proof.ContinuingBoundaryCrossings > 0 && proof.PendingBookingObservations > 0, "No continuing stay/future booking exercised.");
            foreach (string kind in new[] { "towel", "coffee", "cleaning", "luggage" })
                Need(proof.FulfilledKinds.Contains(kind), "No real fulfilled request of kind " + kind + ".");
            foreach (string id in Purchases) Need(proof.PurchasesMade.Contains(id), "Ten-day run never afforded/completed upgrade " + id + ".");
            Need(state.rooms.Find(r => r.number == 105).mvp.owned && state.rooms.Find(r => r.number == 106).mvp.owned,
                "Expansion ownership was lost.");
            Need(proof.DirtyDisposals > 0 && proof.DirtyTowelDisposals > 0 && proof.EquipmentRepairs > 0 && proof.UtilityRepairs > 0,
                "Missing real dirty-carry, equipment or utility repair coverage.");
            Need(proof.WorkKinds.Contains("bed") && proof.WorkKinds.Contains("clean") && proof.WorkKinds.Contains("coffee") &&
                proof.Heartbeats > 100 && proof.Steps >= 108000, "Timed work/full-duration evidence is missing.");
            Need(proof.RewardStabilityProbes > 0, "No fulfilled continuing request checked for repeated rewards.");
            AssertCheckpoint(state);
        }

        private static bool Occupying(GuestState g) { return g.stage == "walking" || g.stage == "staying" || g.stage == "checkout"; }
        private static bool Committed(MvpReservationState r) { return r.status == "confirmed" || r.status == "arrived" || r.status == "staying"; }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException("MVP soak: " + message); }
        private static string Contract(MvpGuestState g)
        {
            // The rest of MvpGuestState is mutable service bookkeeping or an assigned reservation link.
            return g.archetype + "/" + g.trait + "/" + g.partySize + "/" + g.arrivalDay + "/" + g.departureDay + "/" +
                g.agreedTariff + "/" + g.billableNights + "/" + g.minBedQuality + "/" + g.minTvQuality + "/" +
                g.preferredBedQuality + "/" + g.preferredTvQuality + "/" + g.requiresWater + "/" + g.requiresPower + "/" +
                g.patience + "/" + g.requestDelay + "/" + g.requestGrace + "/" + g.preferredRequest + "/" +
                g.noiseTolerance + "/" + g.messRate;
        }

        private sealed class Bot
        {
            private readonly HotelSimulation sim;
            private readonly Evidence proof;
            private readonly bool buyUpgrades;
            private readonly HashSet<string> bought = new HashSet<string>();
            private HotelState S { get { return sim.State; } }
            private ulong actor;
            private int daySteps, dayCommands;
            private float nextAudit;

            public Bot(HotelSimulation simulation, Evidence evidence, bool purchaseUpgrades)
            {
                sim = simulation; proof = evidence; buyUpgrades = purchaseUpgrades;
                Need(S.players.TrueForAll(p => p.id <= 1), "Scenario requires isolated local test actors.");
                sim.Join(0); sim.Join(1);
                foreach (GuestState guest in S.guests) if (guest.paid) proof.Paid.Add(guest.id);
                foreach (string id in Purchases) if (Owned(id)) bought.Add(id);
                Observe(); Audit();
            }

            public string Day()
            {
                int day = S.day, beforeIncome = proof.Income, beforePaid = proof.PaidGuests, beforeRequests = proof.Rewarded.Count;
                actor = (ulong)(day % 2); daySteps = dayCommands = 0; nextAudit = S.mvp.elapsed + 60;
                Need(S.phase == "preparation", "Day did not start in preparation.");
                if (S.guidedOpening && S.guidedStage < HotelDirector.Released)
                    Command(0, new HotelCommand("endGuidedOpening")); // Explicit supported opt-out; onboarding has its own tests.
                if (buyUpgrades) BuyAvailable();
                RepairUtilities();
                foreach (RoomState room in S.rooms) if (room.mvp.owned) PrepareRoom(room);
                CheckFrozenPreparation();
                At(0, "desk"); Command(0, new HotelCommand("open"));
                Need(S.phase == "open", "Open did not start the shift.");
                int cycles = 0;
                while (S.phase == "open")
                {
                    Need(++cycles <= 20000 && daySteps <= 18000 && dayCommands <= 150000, Context("Day progress budget exhausted"));
                    Service();
                    Step(); // Even instantaneous deliveries/check-ins advance simulated time.
                }
                Need(S.phase == "closing" && S.time >= 1079.99f, "A day was finished early.");
                for (int i = 0; i < 2400 && S.guests.Exists(g => Occupying(g) && g.mvp.departureDay <= day + 1); i++)
                { CheckOutReady(); Step(); }
                Need(!S.guests.Exists(g => Occupying(g) && g.mvp.departureDay <= day + 1), "Due guest never reached checkout.");
                var continuing = new Dictionary<int, string>();
                foreach (GuestState guest in S.guests)
                    if (Occupying(guest) && guest.mvp.departureDay > day + 1)
                        continuing.Add(guest.id, Continuation(guest));
                ClearHands();
                At(0, "desk"); Command(0, new HotelCommand("finish"));
                Need(S.phase == "summary", "Finish did not produce summary.");
                AssertContinuing(continuing);
                Audit();
                int earned = S.earned, cash = S.cash;
                Command(0, new HotelCommand("nextday"));
                Need(S.day == day + 1 && S.phase == "preparation" && S.cash == cash - S.expenses && S.earned == 0,
                    "Next day reset/income/restock accounting changed.");
                AssertContinuing(continuing);
                proof.ContinuingBoundaryCrossings += continuing.Count;
                proof.Days++;
                Audit();
                return "day=" + day + " elapsed=1080 steps=" + daySteps + " paid=" + (proof.PaidGuests - beforePaid) +
                    " income=" + (proof.Income - beforeIncome) + " earned=" + earned + " requestsSeen=" + (proof.Rewarded.Count - beforeRequests) +
                    " continuing=" + continuing.Count + " cashAfterRestock=" + S.cash;
            }

            private void Service()
            {
                if (RepairUtilities() || CheckOutReady() || CheckInOne() || DeliverBag()) return;
                foreach (MvpRequestState request in new List<MvpRequestState>(S.mvp.requests))
                {
                    GuestState guest = S.guests.Find(g => g.id == request.guestId);
                    if (request.status != "open" || guest == null || (guest.stage != "walking" && guest.stage != "staying")) continue;
                    RoomState room = S.rooms.Find(r => r.number == guest.room);
                    if (request.kind == "towel" && Supply("towel", "towels"))
                    { At(actor, request.target); Command(actor, new HotelCommand("interact", request.target)); return; }
                    if (request.kind == "coffee" && S.mvp.coffeeStock > 0)
                    {
                        ClearHands(); Work("coffee", "");
                        if (request.status == "open" && (guest.stage == "walking" || guest.stage == "staying"))
                        { At(actor, request.target); Command(actor, new HotelCommand("interact", request.target)); }
                        else ClearHands();
                        return;
                    }
                    if (request.kind == "cleaning") { CleanRoom(room); return; }
                }
                foreach (RoomState room in S.rooms)
                {
                    if (!room.mvp.owned) continue;
                    if (RepairRoom(room)) return;
                    if (room.guestId == 0 && room.bed != 2 && (room.bed == 1 || S.linenStock > 0)) { Bed(room); return; }
                    if (room.water > .02f) { Work("water_" + room.number, "mop"); return; }
                    if (room.mvp.dirtyTowels > 0) { Work("dirtytowel_" + room.number, ""); ClearHands(); return; }
                    if (!room.towel && Supply("towel", "towels"))
                    { At(actor, "towel_" + room.number); Command(actor, new HotelCommand("interact", "towel_" + room.number)); return; }
                    if (room.mvp.dirt > .2f) { Work("clean_" + room.number, "mop"); return; }
                    if (room.mvp.binFill >= .35f || (room.guestId == 0 && room.trash))
                    { Work("trash_" + room.number, ""); ClearHands(); return; }
                }
                for (int i = 0; i < 19 && S.phase == "open"; i++) Step();
            }

            private bool CheckInOne()
            {
                foreach (GuestState guest in S.guests)
                {
                    if (guest.stage != "queue") continue;
                    // Keep scarce capacity/quality for guests who require it; prefer the assigned reservation.
                    var candidates = new List<RoomState>(S.rooms);
                    MvpReservationState reservation = S.mvp.reservations.Find(r => r.id == guest.mvp.reservationId);
                    candidates.Sort((a, b) => RoomScore(a, guest, reservation).CompareTo(RoomScore(b, guest, reservation)));
                    foreach (RoomState room in candidates)
                    {
                        if (HotelHospitalityRules.AssignmentBlockReason(S, guest.id, room.number) != "") continue;
                        At(actor, "desk");
                        Command(actor, new HotelCommand("checkin", "", room.number) { guestId = guest.id });
                        Need(guest.room == room.number && room.guestId == guest.id && guest.stage == "walking", "Check-in links failed.");
                        proof.CheckIns++; return true;
                    }
                }
                return false;
            }

            private static int RoomScore(RoomState room, GuestState guest, MvpReservationState reservation)
            {
                if (reservation != null && reservation.room == room.number) return -1000;
                return (room.mvp.capacity - guest.mvp.partySize) * 100 + room.mvp.bedQuality * 10 + room.mvp.tvQuality;
            }

            private bool CheckOutReady()
            {
                foreach (GuestState guest in S.guests)
                {
                    Vector3 delta = guest.position - new Vector3(.7f, 0, -2.3f); delta.y = 0;
                    if (guest.stage != "checkout" || delta.magnitude > .15f) continue;
                    At(actor, "desk");
                    int cash = S.cash, earned = S.earned, expected = guest.mvp.agreedTariff * guest.mvp.billableNights;
                    Command(actor, new HotelCommand("checkout") { guestId = guest.id });
                    Need(guest.paid && S.cash == cash + expected && S.earned == earned + expected, "Payment differs from agreed nights/tariff.");
                    proof.CheckOuts++;
                    string after = JsonUtility.ToJson(S);
                    string error = sim.Execute(actor, new HotelCommand("checkout") { guestId = guest.id });
                    Need(error != "" && JsonUtility.ToJson(S) == after, "Duplicate checkout paid or mutated the world.");
                    proof.RepeatPaymentRejections++; return true;
                }
                return false;
            }

            private bool DeliverBag()
            {
                foreach (GuestState guest in S.guests)
                {
                    if ((guest.stage != "walking" && guest.stage != "staying") || guest.luggageDelivered) continue;
                    ItemState bag = S.items.Find(i => !i.consumed && i.kind == "bag" && i.ownerGuest == guest.id && i.placedRoom != guest.room);
                    Need(bag != null, "Admitted guest lost undelivered luggage.");
                    ClearHands(); Move(actor, bag.position); Command(actor, new HotelCommand("pickup", bag.id));
                    At(actor, "bag_" + guest.room); Command(actor, new HotelCommand("interact", "bag_" + guest.room));
                    Need(bag.ownerGuest == guest.id && bag.placedRoom == guest.room && bag.holder == -1 && guest.luggageDelivered,
                        "Real luggage delivery did not preserve owner/placement.");
                    return true;
                }
                return false;
            }

            private bool RepairUtilities()
            {
                bool changed = false;
                if (S.mvp.utilities.waterFault) { Work("utility_water", "toolbox"); proof.UtilityRepairs++; changed = true; }
                if (S.mvp.utilities.powerFault) { Work("utility_power", "toolbox"); proof.UtilityRepairs++; changed = true; }
                return changed;
            }

            private bool RepairRoom(RoomState room)
            {
                foreach (MvpEquipmentState equipment in room.mvp.equipment)
                {
                    if (!equipment.installed || !equipment.localFault) continue;
                    int quality = equipment.quality, episode = equipment.episode;
                    Work(equipment.kind + "_" + room.number, equipment.kind == "toilet" ? "plunger" : "toolbox");
                    Need(!equipment.localFault && equipment.quality == quality && equipment.episode == episode,
                        "Repair changed quality/episode instead of restoring equipment.");
                    proof.EquipmentRepairs++; return true;
                }
                return false;
            }

            private void PrepareRoom(RoomState room)
            {
                for (int i = 0; i < 4 && RepairRoom(room); i++) { }
                if (room.guestId == 0) Bed(room);
                CleanRoom(room);
                if (!room.towel && Supply("towel", "towels"))
                { At(actor, "towel_" + room.number); Command(actor, new HotelCommand("interact", "towel_" + room.number)); }
            }

            private void Bed(RoomState room)
            {
                if (room.bed == 1) { Work("bed_" + room.number, ""); ClearHands(); }
                if (room.bed == 0 && Supply("linen", "linen")) Work("bed_" + room.number, "linen");
            }

            private void CleanRoom(RoomState room)
            {
                if (room.water > .001f) Work("water_" + room.number, "mop");
                if (room.mvp.dirt > .001f) Work("clean_" + room.number, "mop");
                for (int i = 0; i < 16 && room.mvp.dirtyTowels > 0; i++)
                { Work("dirtytowel_" + room.number, ""); ClearHands(); }
                // Trash is last: its completion checks fulfillment before the next use increment.
                if (room.trash || room.mvp.binFill > .001f) { Work("trash_" + room.number, ""); ClearHands(); }
            }

            private bool Supply(string kind, string station)
            {
                if (Held() != null && Held().kind == kind && Held().condition == "clean") return true;
                ClearHands();
                ItemState loose = S.items.Find(i => !i.consumed && i.holder == -1 && i.kind == kind && i.condition == "clean");
                if (loose != null) { Move(actor, loose.position); Command(actor, new HotelCommand("pickup", loose.id)); return true; }
                if (kind == "linen" ? S.linenStock <= 0 : S.towelStock <= 0) return false;
                At(actor, station); Command(actor, new HotelCommand("interact", station));
                Need(Held() != null && Held().kind == kind && Held().condition == "clean", "Supply was not physically issued.");
                return true;
            }

            private void Work(string target, string tool)
            {
                if (tool == "") ClearHands();
                else if (Held() == null || Held().kind != tool)
                {
                    ClearHands();
                    ItemState item = S.items.Find(i => !i.consumed && i.kind == tool && i.holder == -1);
                    Need(item != null, "Required tool/supply missing: " + tool);
                    Move(actor, item.position); Command(actor, new HotelCommand("pickup", item.id));
                }
                At(actor, target); Command(actor, new HotelCommand("beginwork", target));
                PlayerState player = S.players.Find(p => p.id == actor);
                Need(player.workTarget == target && player.workProgress == 0, "Work did not acquire a fresh lease.");
                int ticks = 0;
                while (!string.IsNullOrEmpty(player.workTarget))
                {
                    Need(player.workTarget == target && ++ticks <= 150, "Work lease failed to complete: " + target);
                    Command(actor, new HotelCommand("heartbeat", target)); proof.Heartbeats++; Step();
                }
                Need(ticks >= 10, "Timed work completed without held time: " + target);
                string kind = target.Split('_')[0];
                RoomState room = null;
                int number;
                if (int.TryParse(target.Substring(target.LastIndexOf('_') + 1), out number)) room = S.rooms.Find(r => r.number == number);
                if (target == "coffee") Need(Held() != null && Held().kind == "coffee", "Brewing produced no cup.");
                else if (target == "utility_water") Need(!S.mvp.utilities.waterFault, "Water utility remained broken.");
                else if (target == "utility_power") Need(!S.mvp.utilities.powerFault, "Power utility remained broken.");
                else if (kind == "bed") Need(room.bed == 2 || (room.bed == 0 && Held() != null && Held().kind == "dirtylinen"), "Bed work had no physical outcome.");
                else if (kind == "trash" || kind == "dirtytowel") Need(Held() != null && Held().kind == (kind == "trash" ? "trashbag" : "towel") && Held().condition == "dirty", "Dirty carry was not created.");
                else if (kind == "clean") Need(room.mvp.dirt < .01f, "Floor was not cleaned.");
                else if (kind == "water") Need(room.water < .01f, "Water was not mopped.");
                else Need(room.mvp.equipment.Exists(e => e.kind == kind && !e.localFault), "Equipment work did not repair its target.");
                proof.WorkKinds.Add(kind);
            }

            private ItemState Held() { return S.items.Find(i => !i.consumed && i.holder == (long)actor); }

            private void ClearHands()
            {
                ItemState held = Held();
                if (held == null) return;
                if (held.kind == "dirtylinen" || (held.kind == "towel" && held.condition == "dirty") || held.kind == "trashbag")
                {
                    string target = held.kind == "trashbag" ? "bin" : "hamper";
                    At(actor, target); Command(actor, new HotelCommand("interact", target));
                    Need(Held() == null && (held.consumed || !S.items.Contains(held)), "Dirty item was not accepted by its receiver.");
                    proof.DirtyDisposals++; if (held.kind == "towel") proof.DirtyTowelDisposals++;
                }
                else
                {
                    At(actor, "tools");
                    Command(actor, new HotelCommand("drop") { position = S.players.Find(p => p.id == actor).position });
                    Need(Held() == null, "Drop did not free hands.");
                }
            }

            private void BuyAvailable()
            {
                foreach (string id in Purchases)
                {
                    if (bought.Contains(id)) continue;
                    int number = 0;
                    if (id == "bed" || id == "tv")
                    {
                        RoomState room = S.rooms.Find(r => r.number == 102 && r.guestId == 0);
                        if (room == null) room = S.rooms.Find(r => r.mvp.owned && r.guestId == 0 && HotelOperationsRules.PurchaseBlockReason(S, id, r.number) == "");
                        if (room == null) continue;
                        number = room.number;
                    }
                    HotelUpgradeDefinition offer = HotelUpgradeCatalog.Find(id);
                    Need(offer != null, "Upgrade missing from data catalog: " + id);
                    if (S.cash < offer.price || HotelOperationsRules.PurchaseBlockReason(S, id, number) != "") continue;
                    At(0, "board"); int cash = S.cash;
                    Command(0, new HotelCommand("upgrade", id, number));
                    Need(S.cash == cash - offer.price && Owned(id), "Affordable upgrade failed to charge/install: " + id);
                    bought.Add(id); proof.PurchasesMade.Add(id);
                }
            }

            private void CheckFrozenPreparation()
            {
                Step(); // Allow pending consumed-item pruning/complaint reconciliation first.
                float time = S.time, elapsed = S.mvp.elapsed;
                int rng = S.mvp.rngState, draws = S.mvp.rngDraws;
                var happiness = new Dictionary<int, float>();
                foreach (GuestState guest in S.guests) happiness.Add(guest.id, guest.satisfaction);
                for (int i = 0; i < 10; i++) Step();
                Need(S.time == time && S.mvp.elapsed == elapsed && S.mvp.rngState == rng && S.mvp.rngDraws == draws,
                    "Preparation advanced guest time or rerolled RNG.");
                foreach (GuestState guest in S.guests)
                    Need(guest.satisfaction == happiness[guest.id], "Repeated fulfilled-request reconciliation awarded satisfaction again.");
                if (S.mvp.requests.Exists(r => r.rewarded)) proof.RewardStabilityProbes++;
            }

            private bool Owned(string id)
            {
                switch (id)
                {
                    case "toolbox": return S.secondToolbox;
                    case "cart": return S.cartUpgrade;
                    case "linen": return S.mvp.betterLinen;
                    case "coffee": return S.mvp.coffeeMachine;
                    case "bed": return S.rooms.Exists(r => r.mvp.owned && r.mvp.bedQuality >= 2);
                    case "tv": return S.rooms.Exists(r => r.mvp.owned && r.mvp.tvQuality >= 2);
                    case "room105": return S.rooms.Find(r => r.number == 105).mvp.owned;
                    case "room106": return S.rooms.Find(r => r.number == 106).mvp.owned;
                    default: return false;
                }
            }

            private void At(ulong id, string target) { Move(id, HotelLayout.Target(target)); }
            private void Move(ulong id, Vector3 position)
            {
                position.y = .1f;
                Command(id, new HotelCommand("pose") { position = position });
                Need((S.players.Find(p => p.id == id).position - position).sqrMagnitude < .0001f, "Validated pose did not apply.");
            }

            private void Command(ulong id, HotelCommand command)
            {
                int cash = S.cash, earned = S.earned, expenses = S.expenses, day = S.day;
                string error = sim.Execute(id, command);
                Need(error == "", Context(command.action + " " + command.target + ": " + error));
                proof.Commands++; dayCommands++;
                int payments = Observe();
                if (S.day == day)
                {
                    Need(S.cash - cash == S.earned - earned - (S.expenses - expenses), Context("Command cash conservation failed"));
                    Need(S.earned - earned == payments, Context("Command credited income without one newly paid stay"));
                }
            }

            private void Step()
            {
                int cash = S.cash, earned = S.earned, expenses = S.expenses;
                sim.Tick(.1f); proof.Steps++; daySteps++;
                int payments = Observe();
                Need(S.cash - cash == S.earned - earned - (S.expenses - expenses) && S.earned - earned == payments,
                    Context("Tick income is not exactly the sum of newly paid contracts"));
                if (S.mvp.elapsed >= nextAudit) { Audit(); nextAudit = S.mvp.elapsed + 60; }
            }

            private int Observe()
            {
                int payments = 0;
                foreach (GuestState guest in S.guests)
                {
                    Need(!proof.Paid.Contains(guest.id) || guest.paid, "Paid guest became payable again: " + guest.id);
                    if (guest.paid && proof.Paid.Add(guest.id))
                    {
                        payments += checked(guest.mvp.agreedTariff * guest.mvp.billableNights);
                        proof.PaidGuests++; proof.Income += guest.mvp.agreedTariff * guest.mvp.billableNights;
                    }
                }
                foreach (MvpRequestState request in S.mvp.requests)
                {
                    bool previous;
                    if (proof.Rewarded.TryGetValue(request.id, out previous)) Need(!previous || request.rewarded, "Request reward guard rolled back.");
                    proof.Rewarded[request.id] = request.rewarded;
                    if (request.rewarded)
                    { Need(request.status == "fulfilled", "Rewarded request is not fulfilled."); proof.FulfilledKinds.Add(request.kind); }
                }
                return payments;
            }

            private void Audit()
            {
                AssertCheckpoint(S);
                proof.PeakSnapshotBytes = Math.Max(proof.PeakSnapshotBytes, Encoding.Unicode.GetByteCount(JsonUtility.ToJson(S)) + 16);
                foreach (GuestState guest in S.guests)
                {
                    string key = Contract(guest.mvp), previous;
                    if (proof.Contracts.TryGetValue(guest.id, out previous)) Need(previous == key, "Agreed guest contract changed during service/resume.");
                    proof.Contracts[guest.id] = key; proof.Archetypes.Add(guest.mvp.archetype);
                }
                foreach (MvpReviewState review in S.mvp.reviews)
                {
                    string key = JsonUtility.ToJson(review), previous;
                    if (proof.Reviews.TryGetValue(review.guestId, out previous)) Need(previous == key, "Guest review was rewritten/reissued.");
                    proof.Reviews[review.guestId] = key;
                }
                if (S.mvp.reservations.Exists(r => r.status == "confirmed" && r.arrivalDay > S.day)) proof.PendingBookingObservations++;
            }

            private string Continuation(GuestState guest)
            {
                var bagIds = new List<string>();
                foreach (ItemState bag in S.items) if (!bag.consumed && bag.ownerGuest == guest.id) bagIds.Add(JsonUtility.ToJson(bag));
                bagIds.Sort(StringComparer.Ordinal);
                return guest.room + ":" + guest.paid + ":" + guest.luggageDelivered + ":" + Contract(guest.mvp) + ":" + string.Join("|", bagIds.ToArray());
            }

            private void AssertContinuing(Dictionary<int, string> expected)
            {
                foreach (KeyValuePair<int, string> entry in expected)
                {
                    GuestState guest = S.guests.Find(g => g.id == entry.Key);
                    Need(guest != null && Occupying(guest) && !guest.paid && Continuation(guest) == entry.Value,
                        "Finish/next-day lost or settled a continuing stay.");
                }
            }

            private string Context(string message) { return message + " [day=" + S.day + " time=" + S.time + " phase=" + S.phase + "]"; }
        }
    }
}
