using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace WorstHotel
{
    // The Unity batch runner invokes RunAll. These tests use the production simulation,
    // Resources/JsonUtility and persistence; there is no substitute Unity runtime.
    public static class HotelMvpOperationsTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "MVP upgrade resource, strict schema and immutable queries", Catalog);
            Run(passed, "MVP six rooms and unavailable room guards", Ownership);
            Run(passed, "MVP utility effectiveness preserves local faults", EquipmentQueries);
            Run(passed, "MVP bed, towels, bin and physical disposal", Cleaning);
            Run(passed, "MVP water and dirt cleanup remain separate", Floors);
            Run(passed, "MVP repair tools, fault episodes and utility restoration", Repairs);
            Run(passed, "MVP timed coffee, cancellation and outage fallback", CoffeeWork);
            Run(passed, "MVP requested deliveries consume once and reward once", RequestedDelivery);
            Run(passed, "MVP finite supplies and ordinary towel replacement", Supplies);
            Run(passed, "MVP upgrade authority, phases, vacancy and finances", PurchaseGuards);
            Run(passed, "MVP eight upgrades have physical effects", Purchases);
            Run(passed, "MVP finishes change appearance without cleaning", Finishes);
            Run(passed, "MVP wrong-room luggage retains its owner and can be corrected", Luggage);
            Run(passed, "MVP cart uses small/large capacity units", Cart);
            Run(passed, "MVP occupancy wear, towel use and frozen preparation", Wear);
            Run(passed, "MVP physical state survives native save/load", SaveRoundtrip);
            return passed;
        }

        private static void Run(List<string> passed, string name, Action test)
        {
            try { test(); passed.Add(name); }
            catch (Exception error) { throw new Exception("HotelMvpOperationsTests FAILED: " + name, error); }
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static HotelSimulation New()
        {
            HotelSimulation sim = HotelSimulation.CreateNewMvp(7193);
            sim.Join(0); sim.Join(1);
            Ok(sim, "endGuidedOpening");
            return sim;
        }
        private static PlayerState Player(HotelSimulation sim, ulong id = 0) => sim.State.players.Find(p => p.id == id);
        private static RoomState Room(HotelSimulation sim, int number = 101) => sim.State.rooms.Find(r => r.number == number);
        private static ItemState Held(HotelSimulation sim) => sim.State.items.Find(i => i.id == Player(sim).held && !i.consumed);
        private static MvpEquipmentState Equipment(HotelSimulation sim, string kind, int number = 101) => Room(sim, number).mvp.equipment.Find(e => e.kind == kind);
        private static void Ok(HotelSimulation sim, string action, string target = "", int number = 0, ulong id = 0)
        {
            string error = sim.Execute(id, new HotelCommand(action, target, number));
            Check(error == "", action + " " + target + ": " + error);
            HotelSaveStore.Validate(sim.State);
        }
        private static void No(HotelSimulation sim, string action, string target = "", int number = 0, ulong id = 0)
        {
            string before = JsonUtility.ToJson(sim.State);
            Check(sim.Execute(id, new HotelCommand(action, target, number)) != "", "Expected rejection: " + action + " " + target);
            Check(JsonUtility.ToJson(sim.State) == before, "Rejected command mutated world: " + action + " " + target);
        }
        private static void At(HotelSimulation sim, Vector3 position, ulong id = 0)
        {
            position.y = .1f;
            Check(sim.Execute(id, new HotelCommand("pose") { position = position }) == "", "Pose rejected: " + position);
        }
        private static void At(HotelSimulation sim, string target, ulong id = 0) => At(sim, HotelLayout.Target(target), id);
        private static void Interact(HotelSimulation sim, string target) { At(sim, target); Ok(sim, "interact", target); }
        private static void Pick(HotelSimulation sim, string kind)
        {
            ItemState item = sim.State.items.Find(i => !i.consumed && i.kind == kind && i.holder == -1);
            Check(item != null, "Missing " + kind); PickItem(sim, item);
        }
        private static void PickItem(HotelSimulation sim, ItemState item) { At(sim, item.position); Ok(sim, "pickup", item.id); }
        private static void Drop(HotelSimulation sim)
        {
            Check(sim.Execute(0, new HotelCommand("drop") { position = Player(sim).position }) == "", "Drop rejected");
            HotelSaveStore.Validate(sim.State);
        }
        private static void Begin(HotelSimulation sim, string target) { At(sim, target); Ok(sim, "beginwork", target); }
        private static void Continue(HotelSimulation sim, float seconds)
        {
            for (float t = 0; t < seconds - .0001f && Player(sim).workTarget != ""; t += .1f)
            {
                Ok(sim, "heartbeat", Player(sim).workTarget);
                sim.Tick(Mathf.Min(.1f, seconds - t));
            }
            HotelSaveStore.Validate(sim.State);
        }
        private static void Work(HotelSimulation sim, string target)
        {
            Begin(sim, target); Continue(sim, 8);
            Check(Player(sim).workTarget == "", "Work did not finish: " + target);
        }
        private static void Buy(HotelSimulation sim, string id, int room = 0) { At(sim, "board"); Ok(sim, "upgrade", id, room); }
        private static object Hook(HotelSimulation sim, string name, params object[] args)
        {
            MethodInfo method = typeof(HotelSimulation).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Check(method != null, "Missing production hook " + name);
            return method.Invoke(sim, args);
        }
        private static bool Fault(HotelSimulation sim, int room, string kind) => (bool)Hook(sim, "TryRaiseFault", room, kind);
        private static GuestState Occupant(HotelSimulation sim, int room = 101)
        {
            int id = sim.State.nextGuest++;
            var guest = new GuestState { id = id, name = "Operations fixture " + id, kind = "tourist", trait = "patient",
                stage = room == 0 ? "queue" : "staying", room = room, satisfaction = 60,
                position = room == 0 ? HotelLayout.Spawn : HotelLayout.RoomCenter(room),
                mvp = new MvpGuestState { arrivalDay = sim.State.day, departureDay = sim.State.day + 1, requestDelay = 800, requestIssued = true } };
            sim.State.guests.Add(guest);
            if (room != 0) Room(sim, room).guestId = id;
            sim.State.items.Add(new ItemState { id = "operations_bag_" + id, kind = "bag", ownerGuest = id, position = new Vector3(2.8f, .25f, -4.4f) });
            HotelSaveStore.Validate(sim.State);
            return guest;
        }
        private static MvpRequestState Request(HotelSimulation sim, GuestState guest, string kind)
        {
            var request = new MvpRequestState { id = "operations_request_" + sim.State.mvp.nextId++, guestId = guest.id,
                kind = kind, target = (kind == "luggage" ? "bag" : kind == "cleaning" ? "clean" : kind) + "_" + guest.room,
                createdAt = sim.State.mvp.elapsed, dueAt = sim.State.mvp.elapsed + 100 };
            sim.State.mvp.requests.Add(request);
            if (kind == "towel") guest.towelRequested = true;
            return request;
        }

        private static void Catalog()
        {
            TextAsset resource = Resources.Load<TextAsset>("upgrades-mvp");
            Check(resource != null && HotelUpgradeCatalog.Parse(resource.text).Length == 8, "Missing editable upgrade catalogue");
            HotelUpgradeDefinition[] entries = HotelUpgradeCatalog.All;
            int price = HotelUpgradeCatalog.Find("cart").price;
            foreach (HotelUpgradeDefinition entry in entries) entry.price = 1;
            Check(HotelUpgradeCatalog.Find("cart").price == price, "Catalogue leaked mutable entries");
            foreach (string invalid in new[] { "{}", resource.text.Replace("room106", "room105"), resource.text.Replace("800", "-1") })
            {
                bool rejected = false;
                try { HotelUpgradeCatalog.Parse(invalid); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Invalid catalogue accepted");
            }
        }
        private static void Ownership()
        {
            HotelSimulation sim = New();
            Check(sim.State.rooms.Count == 6 && !Room(sim, 105).mvp.owned && !Room(sim, 106).mvp.owned, "Six-room ownership");
            Check(Room(sim, 106).mvp.capacity == 2 && Room(sim, 105).mvp.capacity == 1, "Unlock capacity");
            No(sim, "interact", "towel_105"); No(sim, "beginwork", "bed_106");
            No(sim, "interact", "sink_999");
            string before = JsonUtility.ToJson(sim.State);
            Check(!Fault(sim, 105, "sink") && !Fault(sim, 999, "lamp"), "Fault in unavailable room");
            Check(before == JsonUtility.ToJson(sim.State), "Unavailable fault changed world");
            Check(!HotelOperationsRules.Tasks(sim.State).Exists(t => t.Contains("105") || t.Contains("106")), "Unowned room advertised as task");
        }
        private static void EquipmentQueries()
        {
            HotelSimulation sim = New();
            Check(Fault(sim, 101, "sink") && Fault(sim, 0, "water"), "Fault setup");
            Check(!Fault(sim, 101, "sink") && !Fault(sim, 0, "water"), "Duplicate fault episode");
            string before = JsonUtility.ToJson(sim.State);
            var copy = HotelOperationsRules.Equipment(sim.State, 101, "sink"); copy.localFault = false; copy.wear = 0;
            Check(!HotelOperationsRules.EffectiveEquipment(sim.State, 102, "toilet"), "Utility outage ignored");
            Check(HotelOperationsRules.EffectiveEquipment(sim.State, 102, "lamp"), "Water disabled power equipment");
            Check(!HotelOperationsRules.EffectiveEquipment(sim.State, 105, "tv"), "Unowned equipment works");
            Check(HotelOperationsRules.Tasks(sim.State).FindAll(t => t.Contains("utility_water")).Count == 1, "Shared fault task duplicated per room");
            Check(before == JsonUtility.ToJson(sim.State), "Read-only queries changed world");
        }
        private static void Cleaning()
        {
            HotelSimulation sim = New(); RoomState room = Room(sim, 102);
            room.mvp.dirtyTowels = 2; room.mvp.binFill = .95f; room.trash = true;
            Work(sim, "bed_102"); Check(room.bed == 0 && Held(sim).kind == "dirtylinen", "Dirty bed did not make physical linen");
            At(sim, "bin"); No(sim, "interact", "bin"); Interact(sim, "hamper");
            Interact(sim, "linen"); Work(sim, "bed_102");
            Check(room.bed == 2 && sim.State.linenStock == 11 && Held(sim) == null, "Clean linen was not consumed");
            Begin(sim, "trash_102"); Continue(sim, 2.2f);
            Check(room.trash && Held(sim) == null, "Full bin did not require longer collection"); Continue(sim, 1);
            Check(!room.trash && room.mvp.binFill == 0 && Held(sim).kind == "trashbag", "Bin did not produce rubbish bag");
            At(sim, "hamper"); No(sim, "interact", "hamper"); Interact(sim, "bin");
            Work(sim, "dirtytowel_102"); Check(room.mvp.dirtyTowels == 1 && Held(sim).kind == "towel" && Held(sim).condition == "dirty", "Dirty towel collection");
            At(sim, "towel_103"); No(sim, "interact", "towel_103");
            At(sim, "bin"); No(sim, "interact", "bin"); Interact(sim, "hamper");
            Check(room.mvp.dirtyTowels == 1 && room.bed == 2, "Disposal altered remaining room contents");
        }
        private static void Floors()
        {
            HotelSimulation sim = New(); RoomState room = Room(sim); room.water = .6f; room.mvp.dirt = .5f;
            Fault(sim, 101, "sink"); Pick(sim, "mop");
            Work(sim, "water_101"); Check(room.water == 0 && room.mvp.dirt == .5f && room.leak, "Mopping repaired or removed dirt");
            Work(sim, "clean_101"); Check(room.mvp.dirt == 0 && room.leak, "Cleaning repaired leak");
            At(sim, "water_101"); No(sim, "beginwork", "water_101");
        }
        private static void Repairs()
        {
            HotelSimulation sim = New(); RoomState room = Room(sim); room.water = .6f;
            foreach (string kind in new[] { "sink", "toilet", "tv", "lamp" }) Check(Fault(sim, 101, kind), "Raise " + kind);
            Fault(sim, 0, "water"); Fault(sim, 0, "power"); Pick(sim, "toolbox");
            At(sim, "toilet_101"); No(sim, "beginwork", "toilet_101");
            Work(sim, "utility_water"); Work(sim, "utility_power");
            Check(Equipment(sim, "sink").localFault && Equipment(sim, "tv").localFault, "Shared repair fixed local fault");
            foreach (string kind in new[] { "sink", "tv", "lamp" })
            {
                Work(sim, kind + "_101");
                Check(!Equipment(sim, kind).localFault && Equipment(sim, kind).episode == 1 && Equipment(sim, kind).wear == .15f, "Bad repair: " + kind);
            }
            Check(room.water == .6f && !room.leak, "Repair removed puddle");
            Drop(sim); Pick(sim, "plunger"); Work(sim, "toilet_101");
            Check(!Equipment(sim, "toilet").localFault && HotelOperationsRules.EffectiveEquipment(sim.State, 101, "toilet"), "Plunger repair");
            Check(Fault(sim, 101, "toilet") && Equipment(sim, "toilet").episode == 2, "New failure did not get new episode");
        }
        private static void CoffeeWork()
        {
            HotelSimulation sim = New(); int stock = sim.State.mvp.coffeeStock;
            Begin(sim, "coffee"); Continue(sim, 1); Ok(sim, "cancelwork");
            Check(sim.State.mvp.coffeeStock == stock && Held(sim) == null, "Cancelled brew consumed stock");
            Begin(sim, "coffee"); sim.Tick(2);
            Check(Player(sim).workTarget == "" && sim.State.mvp.coffeeStock == stock, "Expired lease brewed coffee");
            Begin(sim, "coffee"); Continue(sim, 3);
            Check(Held(sim) == null && sim.State.mvp.coffeeStock == stock, "Manual coffee completed early"); Continue(sim, 3.2f);
            Check(Held(sim).kind == "coffee" && sim.State.mvp.coffeeStock == stock - 1, "Manual brew failed"); Drop(sim);
            sim.State.cash = 1000; Buy(sim, "coffee"); Begin(sim, "coffee"); Continue(sim, 2.7f);
            Check(Held(sim) != null && Held(sim).kind == "coffee", "Machine did not speed brewing"); Drop(sim);
            Fault(sim, 0, "power"); Fault(sim, 0, "water"); Begin(sim, "coffee"); Continue(sim, 3);
            Check(Held(sim) == null, "Machine ignored electricity outage"); Continue(sim, 3.2f);
            Check(Held(sim) != null && sim.State.mvp.coffeeStock == stock - 3, "Manual outage fallback unavailable");
        }
        private static void RequestedDelivery()
        {
            HotelSimulation sim = New(); GuestState guest = Occupant(sim);
            MvpRequestState coffee = Request(sim, guest, "coffee");
            Work(sim, "coffee"); string cup = Held(sim).id;
            At(sim, "coffee_103"); No(sim, "interact", "coffee_103"); Check(Held(sim).id == cup && coffee.status == "open", "Wrong room consumed coffee");
            Interact(sim, "coffee_101");
            Check(coffee.status == "fulfilled" && coffee.rewarded && Held(sim) == null, "Actual coffee delivery did not fulfill request");
            float satisfaction = guest.satisfaction; sim.Tick(.1f);
            At(sim, "coffee_101"); No(sim, "interact", "coffee_101"); Check(guest.satisfaction == satisfaction, "Delivery rewarded twice");
            MvpRequestState towel = Request(sim, guest, "towel");
            Check(Room(sim).towel, "Fixture lacks standard towel"); Interact(sim, "towels"); Interact(sim, "towel_101");
            Check(towel.status == "fulfilled" && towel.rewarded && !guest.towelRequested && Held(sim) == null, "Additional towel request failed");
            Check(Room(sim).mvp.dirtyTowels == 0, "Clean delivery fabricated dirty towels");
        }
        private static void Supplies()
        {
            HotelSimulation sim = New(); RoomState room = Room(sim, 103); room.towel = false; room.mvp.towelUseProgress = .8f;
            Interact(sim, "towels"); Check(sim.State.towelStock == 11, "Towel stock not finite");
            At(sim, "towel_101"); No(sim, "interact", "towel_101"); Interact(sim, "towel_103");
            Check(room.towel && room.mvp.towelUseProgress == 0, "Fresh ordinary towel inherited old use");
            sim.State.towelStock = 0; At(sim, "towels"); No(sim, "interact", "towels");
            sim.State.mvp.coffeeStock = 0; At(sim, "coffee"); No(sim, "beginwork", "coffee");
        }
        private static void PurchaseGuards()
        {
            HotelSimulation sim = New(); sim.State.cash = 10000;
            No(sim, "upgrade", "cart"); At(sim, "board", 1); No(sim, "upgrade", "cart", id: 1);
            At(sim, "board"); No(sim, "upgrade", "unknown"); No(sim, "upgrade", "bed", 105);
            sim.State.cash = -1; No(sim, "upgrade", "toolbox"); No(sim, "finishRoom", "warm", 101);
            sim.State.cash = 10000;
            GuestState guest = Occupant(sim); No(sim, "upgrade", "bed", 101); No(sim, "upgrade", "tv", 101); No(sim, "finishRoom", "warm", 101);
            Ok(sim, "toggleRoom", number: 101); Check(Room(sim).guestId == guest.id && guest.room == 101, "Closing sales expelled guest");
            sim.State.phase = "open"; No(sim, "upgrade", "cart"); No(sim, "finishRoom", "warm", 103);
        }
        private static void Purchases()
        {
            HotelSimulation sim = New(); sim.State.cash = 10000; int expenses = sim.State.expenses, cost = 0;
            RoomState room = Room(sim, 102); int linen = room.bed, capacity = room.mvp.capacity;
            Fault(sim, 102, "tv"); Fault(sim, 102, "toilet");
            foreach (string id in new[] { "toolbox", "cart", "linen", "coffee", "bed", "tv", "room105", "room106" })
            { cost += HotelUpgradeCatalog.Find(id).price; Buy(sim, id, id == "bed" || id == "tv" ? 102 : 0); No(sim, "upgrade", id, id == "bed" || id == "tv" ? 102 : 0); }
            Check(sim.State.cash == 10000 - cost && sim.State.expenses == expenses + cost, "Purchase accounting");
            Check(sim.State.items.FindAll(i => i.kind == "toolbox" && !i.consumed).Count == 2 && sim.State.items.Exists(i => i.kind == "cart"), "Purchased tools not physical");
            Check(room.mvp.bedQuality == 2 && room.bed == linen && room.mvp.capacity == capacity, "Bed upgrade changed capacity/linen");
            Check(room.mvp.tvQuality == 2 && !Equipment(sim, "tv", 102).localFault && Equipment(sim, "tv", 102).wear == 0 && Equipment(sim, "toilet", 102).localFault, "TV replacement affected other equipment");
            Check(Room(sim, 105).mvp.owned && Room(sim, 106).mvp.owned && Room(sim, 106).mvp.capacity == 2, "Physical expansion");
            room.bed = 0; Interact(sim, "linen"); Begin(sim, "bed_102"); Continue(sim, 2.2f);
            Check(room.bed == 2 && Held(sim) == null, "Better linen did not reduce bed work to two seconds");
            At(sim, "door_106"); Ok(sim, "interact", "door_106");
        }
        private static void Finishes()
        {
            HotelSimulation sim = New(); sim.State.cash = 1000; RoomState room = Room(sim, 102); room.mvp.dirt = .5f;
            int bed = room.bed; bool trash = room.trash; At(sim, "board"); Ok(sim, "finishRoom", "warm", 102);
            Check(room.mvp.finishId == "warm" && room.bed == bed && room.trash == trash && room.mvp.dirt == .5f && sim.State.cash == 900, "Finish cleaned physical state");
            No(sim, "finishRoom", "warm", 102); No(sim, "finishRoom", "unknown", 102);
            Ok(sim, "finishRoom", "original", 102); Check(sim.State.cash == 860, "Restore finish price");
        }
        private static void Luggage()
        {
            HotelSimulation sim = New(); GuestState guest = Occupant(sim); MvpRequestState request = Request(sim, guest, "luggage");
            ItemState bag = sim.State.items.Find(i => i.ownerGuest == guest.id && i.kind == "bag"); PickItem(sim, bag); Interact(sim, "bag_103");
            Check(bag.placedRoom == 103 && bag.ownerGuest == guest.id && !guest.luggageDelivered && request.status == "open", "Misdelivery changed ownership/fulfillment");
            Check(HotelOperationsRules.Tasks(sim.State).Exists(t => t.Contains("Исправить багаж")), "Misdelivery has no correction task");
            PickItem(sim, bag); Interact(sim, "bag_101");
            Check(bag.ownerGuest == guest.id && guest.luggageDelivered && request.status == "fulfilled", "Bag correction did not fulfill request");
            PickItem(sim, bag); Check(!guest.luggageDelivered, "Pickup retained delivered projection"); Drop(sim);
        }
        private static void Cart()
        {
            HotelSimulation sim = New(); sim.State.cash = 1000; Buy(sim, "cart"); GuestState first = Occupant(sim);
            var bags = new List<ItemState> { sim.State.items.Find(i => i.kind == "bag" && i.ownerGuest == first.id) };
            for (int i = 0; i < 3; i++) { GuestState guest = Occupant(sim, 0); bags.Add(sim.State.items.Find(b => b.kind == "bag" && b.ownerGuest == guest.id)); }
            bags[0].size = "large"; Pick(sim, "cart");
            for (int i = 0; i < 3; i++) PickItem(sim, bags[i]);
            Check(HotelOperationsRules.CartLoad(sim.State) == 4, "Large/small cart weight");
            At(sim, bags[3].position); No(sim, "pickup", bags[3].id);
            Interact(sim, "bag_101");
            Check(HotelOperationsRules.CartLoad(sim.State) == 2 && first.luggageDelivered && Held(sim).kind == "cart", "Cart delivery lost holder/owner");
            PickItem(sim, bags[3]); Check(HotelOperationsRules.CartLoad(sim.State) == 3, "Freed capacity not reusable");
            Drop(sim); Check(bags[1].placedRoom == -1 && bags[2].placedRoom == -1 && bags[3].placedRoom == -1, "Dropping cart lost cargo");
        }
        private static void Wear()
        {
            HotelSimulation sim = New(); Occupant(sim); RoomState room = Room(sim);
            Equipment(sim, "sink").wear = .9999f; room.mvp.towelUseProgress = .999f;
            string before = JsonUtility.ToJson(sim.State); Hook(sim, "OperationsStep", 10f);
            Check(before == JsonUtility.ToJson(sim.State), "Preparation advanced physical wear");
            sim.State.phase = "open"; Hook(sim, "OperationsStep", 1f);
            Check(Equipment(sim, "sink").localFault && Equipment(sim, "sink").episode == 1 && room.leak, "Use did not raise canonical fault");
            Check(room.mvp.dirt > 0 && room.mvp.binFill > 0 && room.trash && room.mvp.dirtyTowels == 1 && !room.towel, "Occupation did not create physical hygiene work");
            float tvWear = Equipment(sim, "tv").wear; Fault(sim, 0, "power"); Hook(sim, "OperationsStep", 1f);
            Check(Equipment(sim, "tv").wear == tvWear && Equipment(sim, "sink").episode == 1 && room.water > 0, "Outage wear or repeated fault episode");
            Fault(sim, 0, "water"); float water = room.water; Hook(sim, "OperationsStep", 1f);
            Check(room.water == water && room.leak, "Water outage changed leak or continued flooding"); HotelSaveStore.Validate(sim.State);
        }
        private static void SaveRoundtrip()
        {
            HotelSimulation sim = New(); sim.State.cash = 10000; Buy(sim, "room106"); Buy(sim, "bed", 106); Buy(sim, "coffee");
            At(sim, "board"); Ok(sim, "finishRoom", "cool", 106); Fault(sim, 106, "toilet"); Fault(sim, 0, "water");
            Room(sim, 106).mvp.dirt = .6f; Room(sim, 106).mvp.dirtyTowels = 2; Room(sim, 106).mvp.towelUseProgress = .4f;
            Work(sim, "dirtytowel_106"); string dirtyId = Held(sim).id;
            string directory = Path.Combine(Path.GetTempPath(), "WorstHotelMvpOperations-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "hotel.json"); HotelSaveStore.Save(sim.State, path);
                HotelState saved = HotelSaveStore.Load(path); HotelSaveStore.Validate(saved);
                Check(saved.players.Count == 0 && saved.items.Find(i => i.id == dirtyId).holder == -1 && Held(sim).id == dirtyId, "Save changed live ownership or load retained it");
                HotelSimulation resumed = HotelSimulation.ResumeForPlay(saved); resumed.Join(0);
                RoomState room = Room(resumed, 106);
                Check(room.mvp.owned && room.mvp.capacity == 2 && room.mvp.bedQuality == 2 && room.mvp.finishId == "cool", "Furniture/unlock lost");
                Check(room.mvp.dirt == .6f && room.mvp.dirtyTowels == 1 && room.mvp.towelUseProgress == .4f && Equipment(resumed, "toilet", 106).localFault && resumed.State.mvp.utilities.waterFault, "Physical consequence lost");
                PickItem(resumed, resumed.State.items.Find(i => i.id == dirtyId)); Interact(resumed, "hamper");
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
