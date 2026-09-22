using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WorstHotel
{
    // Invoked by the parent's native batch runner. No scene, networking or test package needed.
    public static class HotelMvpHospitalityTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "Hospitality: four archetypes, five mechanical traits, isolated contracts", Catalogue);
            Run(passed, "Hospitality: party capacity, quality and interval overlap are independent", CapacityAndIntervals);
            Run(passed, "Hospitality: saved forecast, same-day price demand and no reroll", ForecastAndPrice);
            Run(passed, "Hospitality: selected checkin is atomic and arrivals survive serialization", SelectedGuestAndReload);
            Run(passed, "Hospitality: four physical requests reward once", Requests);
            Run(passed, "Hospitality: factual root complaints, escalation, repair and compensation", Complaints);
            Run(passed, "Hospitality: neighbouring noise and bounded congestion recovery", NoiseAndCongestion);
            Run(passed, "Hospitality: relocation preserves tariff and luggage with valid routes", Relocation);
            Run(passed, "Hospitality: overnight finish, immutable tariff and exactly one settlement", Overnight);
            Run(passed, "Hospitality: booked groups atomic, walk-in groups unreserved", Groups);
            Run(passed, "Hospitality: refusal and finish cancel missing arrivals without resurrection", RefusalAndFinish);
            Run(passed, "Hospitality: guide first guest, luggage then towel, no pressure", GuidedOpening);
            Run(passed, "Hospitality: stale references and history remain bounded", Retirement);
            Run(passed, "Hospitality: checkout rejects non-finite or distant positions", CheckoutPosition);
            return passed;
        }

        private static void Run(List<string> passed, string name, Action action)
        {
            try { action(); passed.Add(name); }
            catch (Exception e) { throw new Exception(name, e is TargetInvocationException && e.InnerException != null ? e.InnerException : e); }
        }
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static object Hook(HotelSimulation sim, string method, params object[] args)
        {
            MethodInfo target = typeof(HotelSimulation).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(target != null, "Missing hospitality hook " + method);
            return target.Invoke(sim, args);
        }
        private static HotelSimulation New()
        {
            HotelSimulation sim = HotelSimulation.CreateNewMvp(7241);
            HotelState s = sim.State;
            s.guidedOpening = false; s.guidedStage = HotelDirector.Released;
            s.mvp.schedule.Clear(); s.mvp.reservations.Clear(); s.mvp.groups.Clear(); s.mvp.preparedDay = s.day;
            s.cash = 1000;
            foreach (RoomState r in s.rooms) { r.bed = 2; r.trash = false; r.mvp.binFill = r.mvp.dirt = 0; r.towel = true; }
            sim.Join(0); sim.Join(1);
            At(sim, 0, "desk"); At(sim, 1, "desk");
            return sim;
        }
        private static void At(HotelSimulation sim, ulong player, string target)
        {
            Vector3 p = HotelLayout.Target(target); p.y = .1f;
            Assert(sim.Execute(player, new HotelCommand("pose") { position = p }) == "", "Fixture pose failed.");
        }
        private static void Ok(HotelSimulation sim, string action, int guestId = 0, int room = 0, string target = "", ulong player = 0)
        {
            string result = sim.Execute(player, new HotelCommand(action, target, room) { guestId = guestId });
            Assert(result == "", action + ": " + result);
            HotelSaveStore.Validate(sim.State);
        }
        private static void No(HotelSimulation sim, string action, int guestId = 0, int room = 0, string target = "", ulong player = 0)
        {
            string before = JsonUtility.ToJson(sim.State);
            string result = sim.Execute(player, new HotelCommand(action, target, room) { guestId = guestId });
            Assert(result != "", "Expected rejection: " + action);
            Assert(JsonUtility.ToJson(sim.State) == before, "Rejected command changed state: " + action);
        }
        private static GuestState Arrive(HotelSimulation sim, string archetype = "tourist", string trait = "patient", int nights = 1)
        {
            HotelState s = sim.State; s.phase = "open";
            MvpGuestState c = HotelMvpGuestCatalog.Load().Snapshot(archetype, trait, s.mvp.priceMode, s.day, nights);
            c.partyId = "fixture_party_" + s.nextGuest;
            var entry = new MvpScheduleState { id = "fixture_arrival_" + s.nextGuest, day = s.day, time = 0, contract = c };
            s.mvp.schedule.Add(entry);
            Hook(sim, "HospitalityStep", 0f);
            GuestState g = s.guests.Find(x => x.id == entry.guestId);
            Assert(g != null, "Scheduled guest did not arrive.");
            return g;
        }
        private static GuestState Stay(HotelSimulation sim, int room, string archetype = "tourist", string trait = "patient", int nights = 1)
        {
            GuestState g = Arrive(sim, archetype, trait, nights);
            At(sim, 0, "desk"); Ok(sim, "checkin", g.id, room);
            g.position = HotelLayout.RoomCenter(room); g.stage = "staying";
            return g;
        }
        private static ItemState Bag(HotelSimulation sim, GuestState g) { return sim.State.items.Find(i => i.kind == "bag" && i.ownerGuest == g.id && !i.consumed); }
        private static RoomState Room(HotelSimulation sim, int n) { return sim.State.rooms.Find(r => r.number == n); }
        private static void DeliverBag(HotelSimulation sim, GuestState g)
        {
            ItemState bag = Bag(sim, g); bag.placedRoom = g.room; bag.position = HotelLayout.RoomTarget("bag", g.room);
            Hook(sim, "RefreshRequestFulfillment");
        }
        private static void Step(HotelSimulation sim, float dt)
        {
            if (!HotelDirector.ClockHeld(sim.State))
            {
                sim.State.mvp.elapsed += dt;
                if (sim.State.phase == "open") sim.State.time = Math.Min(sim.State.dayLength, sim.State.time + dt);
            }
            Hook(sim, "HospitalityStep", dt); Hook(sim, "RefreshRequestFulfillment");
        }

        private static void Catalogue()
        {
            HotelMvpGuestCatalog c = HotelMvpGuestCatalog.Load();
            foreach (string a in HotelMvpGuestCatalog.ArchetypeIds)
                foreach (string t in HotelMvpGuestCatalog.TraitIds)
                {
                    MvpGuestState value = c.Snapshot(a, t, "normal", 2, 2);
                    Assert(value.partySize == (a == "family" ? 2 : 1) && value.departureDay == 4 && value.billableNights == 2, "Wrong party or exclusive stay.");
                    MvpGuestState copy = HotelMvpGuestCatalog.Copy(value); copy.patience = 0;
                    Assert(value.patience > 0, "Guest shares mutable contract.");
                }
            Assert(c.Snapshot("tourist", "patient", "normal", 1, 1).patience > c.Snapshot("tourist", "impatient", "normal", 1, 1).patience, "Patience is cosmetic.");
            Assert(c.Snapshot("tourist", "messy", "normal", 1, 1).messRate > c.Snapshot("tourist", "friendly", "normal", 1, 1).messRate, "Messy is cosmetic.");
            Assert(c.Snapshot("business", "demanding", "high", 1, 1).noiseTolerance < c.Snapshot("business", "friendly", "normal", 1, 1).noiseTolerance, "Demanding is cosmetic.");
            Assert(c.Snapshot("vip", "patient", "normal", 1, 1).minTvQuality == 2, "VIP has no mandatory TV quality.");
        }

        private static void CapacityAndIntervals()
        {
            HotelSimulation sim = New(); GuestState family = Arrive(sim, "family");
            Room(sim, 101).mvp.bedQuality = 2; Room(sim, 101).upgraded = true;
            Assert(HotelHospitalityRules.AssignmentBlockReason(sim.State, family.id, 101).Contains("вместимость"), "Quality increased bed capacity.");
            Assert(HotelHospitalityRules.AssignmentBlockReason(sim.State, family.id, 102) == "", "Double room rejected family.");
            No(sim, "checkin", family.id, 101); Ok(sim, "checkin", family.id, 102);
            MvpGuestState future = HotelMvpGuestCatalog.Load().Snapshot("tourist", "patient", "normal", 2, 1);
            Assert(HotelHospitalityRules.BookingBlockReason(sim.State, future, 102) == "", "Adjacent half-open intervals overlap.");
            future.arrivalDay = 1;
            Assert(HotelHospitalityRules.BookingBlockReason(sim.State, future, 102) != "", "Existing stay double-booked.");
            GuestState vip = Arrive(sim, "vip");
            Assert(HotelHospitalityRules.AssignmentBlockReason(sim.State, vip.id, 103) != "", "VIP accepted insufficient quality.");
        }

        private static void ForecastAndPrice()
        {
            HotelSimulation sim = New(); sim.State.day = 2;
            Hook(sim, "PrepareDemand");
            string forecast = JsonUtility.ToJson(sim.State.mvp);
            Hook(sim, "PrepareDemand");
            Assert(forecast == JsonUtility.ToJson(sim.State.mvp), "Preparation redrew forecast.");
            string bookings = JsonUtility.ToJson(new ReservationList { values = sim.State.mvp.reservations });
            var ids = sim.State.mvp.schedule.ConvertAll(x => x.id + ":" + x.time + ":" + x.contract.archetype + ":" + x.contract.trait);
            int draws = sim.State.mvp.rngDraws, next = sim.State.mvp.nextId;
            At(sim, 0, "board"); Ok(sim, "price", target: "low");
            int low = sim.State.mvp.schedule.FindAll(x => x.day == 2 && x.source == "walkin" && x.status == "planned").Count;
            MvpScheduleState first = sim.State.mvp.schedule.Find(x => x.day == 2 && x.source == "walkin");
            Assert(low >= 2 && first != null, "Fixture lacks prospective demand.");
            int cheap = first.contract.agreedTariff;
            Ok(sim, "price", target: "high");
            int high = sim.State.mvp.schedule.FindAll(x => x.day == 2 && x.source == "walkin" && x.status == "planned").Count;
            Assert(high < low && first.contract.agreedTariff > cheap, "Preparation price did not affect today's demand/tariff.");
            Ok(sim, "price", target: "low");
            Assert(sim.State.mvp.schedule.FindAll(x => x.day == 2 && x.source == "walkin" && x.status == "planned").Count == low, "Price toggles duplicated/lost offers.");
            Assert(first.contract.agreedTariff == cheap && sim.State.mvp.rngDraws == draws && sim.State.mvp.nextId == next, "Price rerolled or compounded quote.");
            Assert(string.Join("|", ids.ToArray()) == string.Join("|", sim.State.mvp.schedule.ConvertAll(x => x.id + ":" + x.time + ":" + x.contract.archetype + ":" + x.contract.trait).ToArray()), "Price changed identities/timing/traits.");
            Assert(bookings == JsonUtility.ToJson(new ReservationList { values = sim.State.mvp.reservations }), "Price rewrote promised bookings.");
            HotelState roundtrip = JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(sim.State));
            HotelSimulation resumed = new HotelSimulation(roundtrip);
            string saved = JsonUtility.ToJson(roundtrip.mvp); Hook(resumed, "PrepareDemand");
            Assert(saved == JsonUtility.ToJson(roundtrip.mvp), "Reload regenerated prospective pool.");
        }
        [Serializable] private sealed class ReservationList { public List<MvpReservationState> values; }

        private static void SelectedGuestAndReload()
        {
            HotelSimulation sim = New(); GuestState first = Arrive(sim), second = Arrive(sim, "business");
            Ok(sim, "checkin", second.id, 101);
            Assert(first.stage == "queue" && second.room == 101, "Selected guest replaced by first in queue.");
            No(sim, "checkin", first.id, 101, player: 1);
            No(sim, "checkin", 123456, 102);
            int arrivals = sim.State.arrivals, count = sim.State.guests.Count;
            HotelSimulation resumed = new HotelSimulation(JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(sim.State)));
            Hook(resumed, "HospitalityStep", 0f);
            Assert(resumed.State.arrivals == arrivals && resumed.State.guests.Count == count, "Reload spawned an arrival twice.");
            MvpScheduleState entry = sim.State.mvp.schedule.Find(x => x.guestId == second.id);
            float patience = entry.contract.patience; second.mvp.patience = 5;
            Assert(entry.contract.patience == patience, "Guest and schedule share contract.");
        }

        private static void Requests()
        {
            HotelSimulation sim = New(); GuestState g = Stay(sim, 101); g.satisfaction = 30;
            Hook(sim, "IssueRequest", g, "towel"); Hook(sim, "IssueRequest", g, "coffee");
            RoomState r = Room(sim, 101); r.mvp.dirt = .8f;
            Hook(sim, "IssueRequest", g, "cleaning"); Hook(sim, "RefreshRequestFulfillment");
            Assert(g.towelRequested && sim.State.mvp.requests.Find(x => x.kind == "towel").status == "open", "Preexisting towel satisfied extra request.");
            Assert(sim.State.mvp.requests.Find(x => x.kind == "coffee").status == "open", "Coffee fulfilled without delivery.");
            Bag(sim, g).placedRoom = 102; Hook(sim, "RefreshRequestFulfillment"); Assert(!g.luggageDelivered, "Wrong bag destination credited.");
            DeliverBag(sim, g);
            r.mvp.dirt = r.mvp.binFill = 0; r.trash = false; r.mvp.dirtyTowels = 0; r.water = 0;
            sim.State.mvp.requests.Find(x => x.kind == "towel").status = "fulfilled";
            sim.State.mvp.requests.Find(x => x.kind == "coffee").status = "fulfilled";
            Hook(sim, "RefreshRequestFulfillment");
            Assert(sim.State.mvp.requests.Count == 4 && sim.State.mvp.requests.TrueForAll(x => x.status == "fulfilled" && x.rewarded), "Not all requests fulfilled.");
            Assert(!g.towelRequested && (sim.State.tutorialFlags & (int)HotelTutorialSkill.ExtraTowel) != 0, "Towel tutorial/mirror lost.");
            float satisfaction = g.satisfaction; Hook(sim, "RefreshRequestFulfillment");
            Assert(g.satisfaction == satisfaction, "Repeated physical refresh duplicated reward.");
            HotelSimulation loaded = new HotelSimulation(JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(sim.State)));
            Hook(loaded, "RefreshRequestFulfillment"); Assert(loaded.State.guests[0].satisfaction == satisfaction, "Reload duplicated request reward.");
        }

        private static void Complaints()
        {
            HotelSimulation sim = New(); GuestState a = Stay(sim, 101), b = Stay(sim, 102);
            DeliverBag(sim, a); DeliverBag(sim, b); a.satisfaction = b.satisfaction = 80;
            sim.State.mvp.utilities.powerFault = true; sim.State.mvp.utilities.powerEpisode = 1;
            MvpEquipmentState tv = Room(sim, 101).mvp.equipment.Find(x => x.kind == "tv"); tv.localFault = true; tv.episode = 1;
            Hook(sim, "RefreshRequestFulfillment");
            MvpComplaintState sharedA = sim.State.mvp.complaints.Find(c => c.guestId == a.id && c.causeKey == "utility:power:1");
            MvpComplaintState sharedB = sim.State.mvp.complaints.Find(c => c.guestId == b.id && c.causeKey == "utility:power:1");
            Assert(sharedA != null && sharedB != null && sharedA.causeKey == sharedB.causeKey, "Shared outage lost root identity.");
            Hook(sim, "ReconcileHospitalityComplaints", a, 500f);
            Assert(sharedA.escalation == 2, "Complaint never escalated.");
            At(sim, 0, "desk"); Ok(sim, "compensate", a.id);
            Assert(tv.localFault && sim.State.mvp.utilities.powerFault && sharedA.status == "active", "Compensation magically repaired cause.");
            No(sim, "compensate", a.id);
            sim.State.mvp.utilities.powerFault = false; Hook(sim, "RefreshRequestFulfillment");
            Assert(sharedA.status == "resolved" && tv.localFault, "Utility recovery repaired local TV.");
            Assert(sim.State.mvp.complaints.Exists(c => c.guestId == a.id && c.causeKey == "equipment:tv:101:1" && c.status == "active"), "Local TV complaint disappeared.");
            float before = a.satisfaction; Hook(sim, "RefreshRequestFulfillment"); Assert(a.satisfaction == before, "Recovery farmed satisfaction.");
            sim.State.mvp.utilities.powerFault = true; Hook(sim, "RefreshRequestFulfillment");
            sim.State.mvp.utilities.powerFault = false; Hook(sim, "RefreshRequestFulfillment");
            Assert(a.satisfaction == before, "Same cause reactivation farmed recovery.");
        }

        private static void Relocation()
        {
            HotelSimulation sim = New(); GuestState g = Stay(sim, 101, nights: 2); DeliverBag(sim, g);
            int tariff = g.mvp.agreedTariff; string booking = g.mvp.reservationId;
            Ok(sim, "relocate", g.id, 104);
            Assert(Room(sim, 101).guestId == 0 && Room(sim, 104).guestId == g.id && g.mvp.agreedTariff == tariff, "Relocation broke occupancy/tariff.");
            Assert(Bag(sim, g).placedRoom == 101 && Bag(sim, g).ownerGuest == g.id && !g.luggageDelivered, "Relocation teleported/transferred luggage.");
            Assert(sim.State.mvp.reservations.Find(r => r.id == booking).room == 104, "Remaining booking not moved.");
            for (int i = 0; i < 1000 && g.stage == "walking"; i++)
            {
                Step(sim, .1f);
                // Full public validator includes authored walkability, even at room doors.
                HotelSaveStore.Validate(sim.State);
            }
            Assert(g.stage == "staying" && Vector3.Distance(g.position, HotelLayout.RoomCenter(104)) < .01f, "Relocated guest stuck/walked through wall.");
            DeliverBag(sim, g); Assert(g.luggageDelivered, "Moved bag did not correct delivery.");
        }

        private static void NoiseAndCongestion()
        {
            HotelSimulation sim = New();
            GuestState observer = Stay(sim, 101, "business", "impatient");
            GuestState source = Stay(sim, 102, "family", "messy");
            DeliverBag(sim, observer); DeliverBag(sim, source);
            source.mvp.serviceElapsed = 20; Step(sim, .1f);
            MvpComplaintState noise = sim.State.mvp.complaints.Find(c => c.guestId == observer.id && c.causeKey == "noise:102:" + source.id);
            Assert(noise != null && noise.status == "active", "Nearby actual noise did not affect sensitive guest.");
            source.mvp.serviceElapsed = 80; Step(sim, .1f);
            Assert(noise.status == "resolved", "Quiet period did not resolve exposure.");
            float satisfaction = observer.satisfaction;
            source.mvp.serviceElapsed = 20; Step(sim, .1f);
            source.mvp.serviceElapsed = 80; Step(sim, .1f);
            Assert(observer.satisfaction == satisfaction, "Recurring same-stay noise farmed recovery.");
            HotelSaveStore.Validate(sim.State);

            sim = New(); GuestState queued = Arrive(sim), moving = Arrive(sim);
            queued.position = moving.position;
            Vector3 start = moving.position;
            Ok(sim, "checkin", moving.id, 101);
            for (int i = 0; i < 60; i++) Step(sim, .1f);
            Assert(Vector3.Distance(moving.position, start) > .5f, "Queue permanently blocked the route.");
            HotelSaveStore.Validate(sim.State);
        }

        private static void Overnight()
        {
            HotelSimulation sim = New(); GuestState g = Stay(sim, 102, "family", nights: 2); DeliverBag(sim, g);
            int tariff = g.mvp.agreedTariff, paid = tariff * 2, cash = sim.State.cash;
            Ok(sim, "finish");
            Assert(g.stage == "staying" && !g.paid && Room(sim, 102).guestId == g.id && sim.State.cash == cash, "Finish settled an overnight guest.");
            Ok(sim, "nextday");
            Assert(g.room == 102 && Bag(sim, g).ownerGuest == g.id, "Day change lost occupant/baggage.");
            At(sim, 0, "board"); Ok(sim, "price", target: "high"); Assert(g.mvp.agreedTariff == tariff, "New price changed existing tariff.");
            At(sim, 0, "desk"); Ok(sim, "open");
            g.stage = "checkout"; g.position = new Vector3(.7f, 0, -2.3f);
            cash = sim.State.cash; Ok(sim, "checkout", g.id);
            Assert(sim.State.cash == cash + paid && g.paid && sim.State.mvp.reviews.FindAll(r => r.guestId == g.id).Count == 1, "Wrong/multiple stay payment/review.");
            No(sim, "checkout", g.id);
            Hook(sim, "FinishHospitality"); Assert(sim.State.cash == cash + paid, "Finish paid settled stay twice.");
        }

        private static void Groups()
        {
            HotelSimulation sim = New();
            string before = JsonUtility.ToJson(sim.State);
            object[] tooBig = { 5, "tourist_group", "" };
            Assert(!(bool)Hook(sim, "TryReserveBatch", tooBig) && before == JsonUtility.ToJson(sim.State), "Failed batch partly allocated or consumed RNG.");
            object[] args = { 2, "tourist_group", "" };
            Assert((bool)Hook(sim, "TryReserveBatch", args), "Bookable group rejected.");
            string id = (string)args[2];
            Assert(sim.State.mvp.reservations.Count == 2 && sim.State.mvp.groups.Find(g => g.id == id).reservationIds.Count == 2, "Group not linked to normal reservations.");
            Assert(sim.State.mvp.reservations[0].room != sim.State.mvp.reservations[1].room, "Group double-booked one room.");
            Assert(!ReferenceEquals(sim.State.mvp.reservations[0].contract, sim.State.mvp.schedule[0].contract), "Booking and schedule share mutable contract.");
            sim.State.phase = "open";
            int bookings = sim.State.mvp.reservations.Count;
            object[] unexpected = { 2, "walkin_group", "" };
            Assert((bool)Hook(sim, "TryReserveBatch", unexpected), "Unexpected group rejected despite today's free rooms.");
            Assert(sim.State.mvp.reservations.Count == bookings, "Unexpected walk-ins silently reserved rooms.");
            sim.State.time = 20; Hook(sim, "HospitalityStep", 0f);
            Assert(sim.State.guests.Count == 2 && sim.State.guests.TrueForAll(g => g.mvp.groupId == (string)unexpected[2]), "Group is not normal linked guests.");
            GuestState refused = sim.State.guests[0]; Ok(sim, "refuse", refused.id);
            Assert(sim.State.mvp.groups.Find(g => g.id == (string)unexpected[2]).refused == 1 && sim.State.guests[1].stage == "queue", "Refusal deleted another group member.");
            Assert(sim.State.mvp.reviews.Find(r => r.guestId == refused.id).stars <= 2, "Unserved guest awarded a happy-stay review.");
        }

        private static void RefusalAndFinish()
        {
            HotelSimulation sim = New(); object[] group = { 2, "tourist_group", "" }; Hook(sim, "TryReserveBatch", group);
            MvpReservationState future = sim.State.mvp.reservations[0];
            At(sim, 0, "board"); No(sim, "refuse", target: future.id);
            At(sim, 0, "desk"); float reputation = sim.State.mvp.reputation;
            Ok(sim, "refuse", target: future.id); Assert(sim.State.mvp.reputation < reputation && future.status == "refused", "Future cancellation has no consequence.");
            No(sim, "refuse", target: future.id);
            GuestState overnight = Stay(sim, 104, nights: 2);
            MvpGuestState c = HotelMvpGuestCatalog.Load().Snapshot("tourist", "patient", "normal", 1, 1);
            Hook(sim, "CommitHospitalityBooking", c, 101, "booking", "", 600f);
            MvpReservationState missing = sim.State.mvp.reservations.Find(r => r.arrivalDay == 1 && r.room == 101);
            Ok(sim, "finish");
            Assert(missing.status == "missed" && !sim.State.mvp.schedule.Exists(x => x.day == 1 && x.status == "planned"), "Finish left a scheduled arrival live.");
            Assert(overnight.stage == "staying" && sim.State.mvp.schedule.Exists(x => x.day == 2 && x.status == "planned"), "Finish cancelled tomorrow/overnight.");
            Ok(sim, "nextday"); At(sim, 0, "board"); Ok(sim, "price", target: "low");
            Assert(!sim.State.mvp.schedule.Exists(x => x.day == 1 && x.status == "planned"), "Price resurrected missed earlier-day arrival.");
        }

        private static void GuidedOpening()
        {
            HotelSimulation sim = HotelSimulation.CreateNewMvp(42); sim.Join(0); At(sim, 0, "desk"); Ok(sim, "open");
            Assert(sim.State.guests.Count == 1, "Guide spawned a crowd.");
            GuestState g = sim.State.guests[0]; Assert(g.mvp.archetype == "tourist" && g.mvp.trait == "patient", "Wrong first guest.");
            Step(sim, 600); Assert(g.waited == 0 && sim.State.time == 0 && sim.State.guests.Count == 1, "Guide accumulated optional pressure.");
            Ok(sim, "checkin", g.id, 101); g.stage = "staying"; g.position = HotelLayout.RoomCenter(101);
            Step(sim, 100); Assert(!g.towelRequested, "Guide requested towel before bag.");
            DeliverBag(sim, g); Step(sim, 60);
            Assert(g.towelRequested && sim.State.mvp.requests.Exists(r => r.kind == "towel" && r.status == "open"), "Guide never requested towel after bag.");
            Assert(sim.State.mvp.complaints.Count == 0 && sim.State.guests.Count == 1, "Guide generated complaints/crowd.");
        }

        private static void Retirement()
        {
            HotelSimulation sim = New(); GuestState g = Stay(sim, 101); DeliverBag(sim, g);
            g.stage = "checkout"; g.position = new Vector3(.7f, 0, -2.3f); Ok(sim, "checkout", g.id);
            g.stage = "gone"; Hook(sim, "PruneHospitality");
            Assert(sim.State.guests.Contains(g), "Removed owner before consumed baggage record.");
            HotelSaveStore.Validate(sim.State);
            sim.State.items.RemoveAll(i => i.consumed); Hook(sim, "PruneHospitality");
            Assert(!sim.State.guests.Contains(g) && !sim.State.mvp.requests.Exists(r => r.guestId == g.id) && !sim.State.mvp.complaints.Exists(c => c.guestId == g.id), "Terminal references leaked.");
            Assert(sim.State.mvp.reviews.Exists(r => r.guestId == g.id), "Retirement lost review.");
            sim.State.day += 4; Hook(sim, "PruneHospitality");
            Assert(sim.State.mvp.schedule.Count == 0 && sim.State.mvp.reservations.Count == 0, "Old unreferenced history leaked.");
            HotelSaveStore.Validate(sim.State);
        }

        private static void CheckoutPosition()
        {
            HotelSimulation sim = New(); GuestState g = Stay(sim, 101); g.stage = "checkout";
            No(sim, "checkout", g.id);
            int cash = sim.State.cash; g.position = new Vector3(float.NaN, 0, 0);
            Assert(sim.Execute(0, new HotelCommand("checkout") { guestId = g.id }) != "" && sim.State.cash == cash && !g.paid, "Non-finite checkout paid guest.");
        }
    }
}
