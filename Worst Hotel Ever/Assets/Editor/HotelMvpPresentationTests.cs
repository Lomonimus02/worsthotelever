using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    // Parent runner invokes RunAll; no scene, process, clock or save-file side effects.
    public static class HotelMvpPresentationTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            ExplicitGuestSelection(); passed.Add("MVP selection preserves guest identity for check-in, relocation and refusal");
            EquipmentAndUtilities(); passed.Add("Equipment presentation distinguishes local faults, utilities and quality");
            PhysicalTargets(); passed.Add("All MVP targets resolve; locked rooms and stale targets cannot be suggested");
            DirtyTowelsAndCoffee(); passed.Add("Dirty towel condition and hold-to-brew prompts match physical commands");
            CartCapacity(); passed.Add("Cart help and label use four slots and large-bag weight");
            ContextualHelp(); passed.Add("MVP hints preserve work, select available tools and ignore unowned rooms");
            ContractsAndRequests(); passed.Add("Party, fixed tariff, exclusive departure and four requests are explained");
            ReadOnlyProjection(); passed.Add("UI projection leaves RNG, pings, bookings, guests and work leases unchanged");
            LegacyProjection(); passed.Add("Legacy presentation and onboarding remain on the original branch");
            return passed;
        }
        static HotelState Fixture()
        {
            var state = new HotelState { version = 2, contentVersion = 2, phase = "open", dayLength = 1080, mvp = new MvpHotelState() };
            for (int number = 101; number <= 106; number++)
            {
                var room = new RoomState { number = number, mvp = new MvpRoomState { owned = number <= 104, capacity = number % 2 == 0 ? 2 : 1 } };
                foreach (string kind in new[] { "sink", "toilet", "tv", "lamp" }) room.mvp.equipment.Add(new MvpEquipmentState { kind = kind });
                state.rooms.Add(room);
            }
            state.players.Add(new PlayerState { id = 0, position = HotelLayout.Target("desk") });
            state.players.Add(new PlayerState { id = 1, position = HotelLayout.Target("board") });
            return state;
        }
        static GuestState Guest(int id, int party = 1)
        {
            return new GuestState { id = id, name = "Гость " + id, mvp = new MvpGuestState { partySize = party, archetype = party == 2 ? "family" : "tourist" } };
        }
        static void ExplicitGuestSelection()
        {
            var state = Fixture(); var first = Guest(11); var chosen = Guest(22, 2);
            state.guests.Add(first); state.guests.Add(chosen);
            string before = JsonUtility.ToJson(state);
            Require(HotelPresentation.AssignmentReason(state, 0, 101) != "", "Selection silently defaults to queue head");
            Require(HotelPresentation.AssignmentReason(state, chosen.id, 101) == HotelHospitalityRules.AssignmentBlockReason(state, chosen.id, 101), "Check-in reason diverged from authority");
            Require(HotelPresentation.AssignmentReason(state, chosen.id, 101) != "", "Family shown a single room");
            Require(HotelPresentation.AssignmentReason(state, chosen.id, 102) == "", "Family cannot select a ready double room");
            foreach (string action in new[] { "checkin", "relocate", "refuse", "checkout", "compensate" })
            {
                var command = HotelPresentation.GuestCommand(action, chosen.id, 104);
                Require(command.action == action && command.guestId == chosen.id && command.number == 104, "Guest command lost selected identity or room");
            }
            Require(before == JsonUtility.ToJson(state), "Selection mutated guest/room");
            chosen.stage = "staying"; chosen.room = 102; state.rooms[1].guestId = chosen.id;
            Require(HotelPresentation.AssignmentReason(state, chosen.id, 104) == "", "Relocation incorrectly uses queue-only validator");
            Require(HotelPresentation.AssignmentReason(state, chosen.id, 102) != "", "Relocation offered current room");
            chosen.stage = "gone";
            Require(HotelPresentation.AssignmentReason(state, chosen.id, 104) != "", "Departed selection replaced with another queue guest");
            Require(HotelPresentation.StationReason(state, 1, false, "board", true, true) != "", "Client shown host-only authority");
            Require(HotelPresentation.StationReason(state, 0, true, "desk", false, false) == "", "Employee at desk incorrectly blocked");
            Require(HotelPresentation.StationReason(state, 1, false, "desk", false, false) != "", "Remote desk action shown as available");
        }
        static void EquipmentAndUtilities()
        {
            var state = Fixture(); var sink = state.rooms[0].mvp.equipment.Find(e => e.kind == "sink");
            state.mvp.utilities.waterFault = true;
            Require(!HotelPresentation.RoomProblems(state.rooms[0]).Contains("Готов к приёму"), "Local room summary falsely promises admission without utility/guest checks");
            Require(HotelPresentation.EquipmentStatus(state, 101, "sink").Contains("общей воды"), "Utility outage shown as local repair");
            sink.localFault = true;
            Require(HotelPresentation.EquipmentStatus(state, 101, "sink").Contains("местная поломка"), "Local fault hidden by utility outage");
            Require(HotelPresentation.FocusText(state, 0, "sink_101", "").Contains("инструмент"), "Sink fault lacks required tool");
            state.mvp.utilities.powerFault = true;
            Require(HotelPresentation.EquipmentStatus(state, 101, "tv").Contains("питания"), "TV blackout explanation missing");
            state.rooms[0].mvp.equipment.Find(e => e.kind == "toilet").localFault = true;
            Require(HotelPresentation.FocusText(state, 0, "toilet_101", "").Contains("вантуз"), "Toilet asks for wrong tool");
            state.rooms[0].mvp.dirtyTowels = 2; state.rooms[0].mvp.dirt = .5f;
            string problems = HotelPresentation.RoomProblems(state.rooms[0]);
            Require(problems.Contains("полотенец: 2") && problems.Contains("50%"), "Room omits dirt or dirty towels");
        }
        static void PhysicalTargets()
        {
            var state = Fixture();
            foreach (string target in new[] { "coffee", "utility_water", "utility_power", "toilet_101", "tv_102", "lamp_103", "clean_104", "dirtytowel_101", "coffee_102", "door_106" })
                Require(HotelPresentation.TryTarget(state, target, out Vector3 position) && position == HotelLayout.Target(target), "Missing MVP target: " + target);
            foreach (string target in new[] { "unknown_101", "bed_999", "bed_105", "clean_106", "utility_fake", "guest_99" })
                Require(!HotelPresentation.TryTarget(state, target, out _), "Invalid target offered: " + target);
            state.guests.Add(Guest(8));
            Require(HotelPresentation.TryTarget(state, "guest_8", out _), "Live guest ping target missing");
            state.guests[0].stage = "gone";
            Require(!HotelPresentation.TryTarget(state, "guest_8", out _), "Gone guest target remains");
            state.items.Add(new ItemState { id = "luggage_8", kind = "bag", position = Vector3.one });
            Require(HotelPresentation.TryTarget(state, "luggage_8", out Vector3 bag) && bag == Vector3.one, "Live item ping target missing");
            state.items[0].consumed = true;
            Require(!HotelPresentation.TryTarget(state, "luggage_8", out _), "Consumed item target remains");
        }
        static void DirtyTowelsAndCoffee()
        {
            var state = Fixture();
            var towel = new ItemState { id = "dirty", kind = "towel", condition = "dirty", holder = 0 };
            state.items.Add(towel); state.players[0].held = towel.id;
            Require(HotelPresentation.HeldLabel(state, towel).Contains("Грязное"), "Dirty towel shown as clean supply");
            Require(HotelPresentation.FocusText(state, 0, "hamper", "").Contains("Сдать"), "Dirty towel cannot be directed to hamper");
            Require(HotelPresentation.FocusText(state, 0, "towel_101", "").Contains("грязное"), "Dirty towel offered to guest");
            Require(HotelOnboarding.GetHint(state, 0).targetId == "hamper", "Hint suggests delivery of dirty towel");
            state.players[0].held = ""; towel.holder = -1;
            Require(HotelPresentation.FocusText(state, 0, "coffee", "").Contains("Удерживайте"), "Brewing incorrectly advertised as instant");
            state.rooms[0].mvp.dirtyTowels = 1;
            Require(HotelPresentation.FocusText(state, 0, "dirtytowel_101", "").Contains("Удерживайте"), "Collection incorrectly advertised as instant");
            state.mvp.coffeeStock = 0;
            Require(HotelPresentation.FocusText(state, 0, "coffee", "").Contains("закончился"), "Empty coffee stock not explained");
        }
        static void ContextualHelp()
        {
            var state = Fixture();
            state.players[0].workTarget = "clean_104"; state.players[0].workProgress = .35f;
            string before = JsonUtility.ToJson(state);
            Require(HotelOnboarding.GetHint(state, 0).targetId == "clean_104", "Help overrides own active work");
            Require(before == JsonUtility.ToJson(state), "Help changed own work lease");
            state.players[0].workTarget = "";
            state.rooms[4].mvp.dirt = 1; state.rooms[4].mvp.dirtyTowels = 2;
            Require(!HotelOnboarding.GetHint(state, 0).targetId.Contains("105"), "Help sends player to locked room");
            state.rooms[0].mvp.equipment.Find(e => e.kind == "toilet").localFault = true;
            state.items.Add(new ItemState { id = "plunger_1", kind = "plunger" });
            Require(HotelOnboarding.GetHint(state, 0).targetId == "plunger_1", "Help omits available plunger");
            state.items[0].holder = 1; state.players[1].held = "plunger_1";
            Require(HotelOnboarding.GetHint(state, 0).targetId == "toilet_101", "Help offers teammate-held plunger pickup");
            var staying = Guest(9); staying.stage = "staying"; staying.room = 102; state.guests.Add(staying);
            state.rooms[1].guestId = staying.id; state.rooms[1].mvp.dirtyTowels = 1;
            state.mvp.requests.Add(new MvpRequestState { id = "cleaning9", guestId = 9, kind = "cleaning", target = "clean_102" });
            Require(HotelOnboarding.GetHint(state, 0).targetId == "dirtytowel_102", "Cleaning request sends player to already-clean floor instead of dirty towels");
            state.tutorialSkipped = true; Require(HotelOnboarding.GetHint(state, 0) == null, "Skipped help reappears");
        }
        static void CartCapacity()
        {
            var state = Fixture(); state.cartUpgrade = true;
            var cart = new ItemState { id = "cart1", kind = "cart", holder = 0 };
            state.items.Add(cart); state.players[0].held = cart.id;
            state.items.Add(new ItemState { id = "bag1", kind = "bag", size = "large", placedRoom = -1 });
            state.items.Add(new ItemState { id = "bag2", kind = "bag", size = "large", placedRoom = -1 });
            var waitingBag = new ItemState { id = "bag3", kind = "bag", size = "small" }; state.items.Add(waitingBag);
            Require(HotelPresentation.HeldLabel(state, cart).Contains("4/4"), "MVP cart still counts two legacy bags");
            Require(HotelPresentation.FocusText(state, 0, waitingBag.id, "").Contains("заполнена"), "Full cart suggests another load");
            Require(HotelOnboarding.GetHint(state, 0).targetId != waitingBag.id, "Full cart guidance suggests impossible pickup");
        }
        static void ContractsAndRequests()
        {
            var state = Fixture(); var guest = Guest(1, 2);
            guest.mvp.agreedTariff = 137; guest.mvp.trait = "demanding";
            string contract = HotelPresentation.GuestContract(guest.mvp);
            Require(contract.Contains("Семья") && contract.Contains("Требовательный") && contract.Contains("2 чел."), "Guest identity omits archetype, trait or party");
            Require(contract.Contains("до дня 2") && contract.Contains("137 ₽ за проживание"), "Exclusive departure or fixed tariff misrepresented");
            state.mvp.priceMode = "high";
            Require(contract == HotelPresentation.GuestContract(guest.mvp), "Price change alters contract projection");
            Require(HotelPresentation.Requirements(guest.mvp).Contains("мест ≥ 2"), "Capacity requirement missing");
            foreach (string kind in new[] { "towel", "coffee", "cleaning", "luggage" })
                Require(HotelPresentation.RequestName(kind) != kind, "Untranslated request: " + kind);
            foreach (string trait in new[] { "patient", "impatient", "messy", "demanding", "friendly" })
                Require(HotelPresentation.TraitName(trait) != trait, "Untranslated trait: " + trait);
            state.mvp.elapsed = 200;
            var request = new MvpRequestState { dueAt = 100 };
            Require(HotelPresentation.RequestDeadline(state, request).Contains("просрочен на 100"), "Deadline uses day timer instead of elapsed service time");
            request.status = "fulfilled";
            Require(!HotelPresentation.RequestDeadline(state, request).Contains("просрочен"), "Fulfilled request still counts down");
        }
        static void ReadOnlyProjection()
        {
            var state = Fixture(); var guest = Guest(5); state.guests.Add(guest);
            state.mvp.pings.Add(new MvpPingState { target = "utility_water", playerId = 1, until = 2 });
            state.mvp.groups.Add(new MvpGroupState { id = "group1", reservationIds = new List<string> { "booking1" } });
            state.mvp.reservations.Add(new MvpReservationState { id = "booking1", arrivalDay = 2, departureDay = 4, room = 102, contract = new MvpGuestState() });
            state.mvp.schedule.Add(new MvpScheduleState { id = "arrival1", day = 2, time = 120, contract = new MvpGuestState() });
            state.mvp.requests.Add(new MvpRequestState { id = "request1", guestId = 5, kind = "coffee", dueAt = 90 });
            string before = JsonUtility.ToJson(state);
            for (int frame = 0; frame < 3; frame++)
            {
                HotelPresentation.GuestIssues(state, guest); HotelPresentation.MvpTasks(state);
                HotelPresentation.AssignmentReason(state, 5, 101); HotelOnboarding.GetHint(state, 0);
                HotelWorkload.Measure(state); HotelMvpDirector.Status(state);
                foreach (var room in state.rooms) HotelPresentation.RoomProblems(room);
                foreach (var booking in state.mvp.reservations) HotelPresentation.GuestContract(booking.contract);
                foreach (var ping in state.mvp.pings) HotelPresentation.TryTarget(state, ping.target, out _);
                foreach (var upgrade in HotelUpgradeCatalog.All) HotelOperationsRules.PurchaseBlockReason(state, upgrade.id, 101);
            }
            Require(before == JsonUtility.ToJson(state), "Presentation mutated serialized state/RNG/pings");
        }
        static void LegacyProjection()
        {
            var state = new HotelState();
            state.players.Add(new PlayerState { id = 0 });
            state.rooms.Add(new RoomState { number = 101, bed = 1 });
            string before = JsonUtility.ToJson(state);
            Require(HotelPresentation.FocusText(state, 0, "bed_101", "").Contains("снять грязное"), "Legacy bed instruction changed");
            Require(HotelPresentation.RoomProblems(state.rooms[0]) == "Грязное бельё", "Legacy room label changed");
            Require(!HotelPresentation.TryTarget(state, "coffee", out _), "MVP target leaked into old game");
            Require(HotelOnboarding.GetHint(state, 0).targetId == "bed_101", "Legacy onboarding changed");
            Require(before == JsonUtility.ToJson(state), "Legacy projection mutated state");
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
