using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorstHotel
{
    // No NUnit/package dependency: the integration batch runner calls RunAll and logs the returned names.
    public static class HotelSimulationTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "Initial hotel, two players, preparation clock", InitialHotel);
            Run(passed, "Distance, walls, host authority, invalid commands", CommandValidation);
            Run(passed, "Exclusive item ownership and disconnect recovery", Ownership);
            Run(passed, "Finite stock and failed delivery does not consume", StockAndDelivery);
            Run(passed, "Dirty bed, linen, trash and physical disposal", Cleaning);
            Run(passed, "Heartbeat expiry, duplicate begin and exclusive work", WorkLeases);
            Run(passed, "Tool repair and mop are separate operations", RepairAndWater);
            Run(passed, "Check-in collision and corridor/door guest routes", GuestRoutes);
            Run(passed, "Luggage ownership, wrong room and real towel request", GuestService);
            Run(passed, "Local complaints, compensation and one-time payment", Economy);
            Run(passed, "Cart loads two bags and delivers without losing owners", Cart);
            Run(passed, "One-time upgrades, expenses and physical fulfillment", Upgrades);
            Run(passed, "Day end, debt recovery, replenishment, persistent dirt", Days);
            Run(passed, "Unattended shift reaches a recoverable summary", Unattended);
            Run(passed, "Save roundtrip preserves world and releases held objects", SaveRoundtrip);
            Run(passed, "Backup recovery, corrupt primary repair and checksum", BackupRecovery);
            Run(passed, "Future schema and invalid state never overwrite checkpoint", SaveValidation);
            Run(passed, "Saved moving guest resumes through the door", SavedRoute);
            Run(passed, "Two complete shifts remain valid after every command", TwoShifts);
            return passed;
        }

        private static void Run(List<string> passed, string name, Action test)
        {
            try { test(); passed.Add(name); }
            catch (Exception error) { throw new Exception("HotelSimulationTests FAILED: " + name, error); }
        }
        private static HotelSimulation New()
        {
            var sim = new HotelSimulation();
            sim.Join(0); sim.Join(1);
            return sim;
        }
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        private static void Ok(HotelSimulation sim, ulong id, string action, string target = "", int number = 0)
        {
            string result = sim.Execute(id, new HotelCommand(action, target, number));
            Assert(result == "", action + " " + target + " " + number + ": " + result);
            HotelSaveStore.Validate(sim.State);
        }
        private static void No(HotelSimulation sim, ulong id, string action, string target = "", int number = 0)
        {
            string before = JsonUtility.ToJson(sim.State);
            string result = sim.Execute(id, new HotelCommand(action, target, number));
            Assert(result != "", "Expected rejection: " + action);
            Assert(before == JsonUtility.ToJson(sim.State), "Rejected command changed world: " + action);
        }
        private static void At(HotelSimulation sim, ulong id, Vector3 position)
        {
            position.y = .1f;
            Assert(sim.Execute(id, new HotelCommand("pose") { position = position }) == "", "Pose rejected: " + position);
        }
        private static void At(HotelSimulation sim, ulong id, string target) { At(sim, id, HotelLayout.Target(target)); }
        private static PlayerState Player(HotelSimulation sim, ulong id = 0) { return sim.State.players.Find(p => p.id == id); }
        private static RoomState Room(HotelSimulation sim, int n) { return sim.State.rooms.Find(r => r.number == n); }
        private static ItemState Item(HotelSimulation sim, string kind) { return sim.State.items.Find(i => i.kind == kind && !i.consumed); }
        private static void Pick(HotelSimulation sim, ulong id, string kind)
        {
            ItemState item = sim.State.items.Find(i => i.kind == kind && !i.consumed && i.holder < 0 && (kind != "bag" || i.placedRoom == 0));
            Assert(item != null, "Missing item " + kind);
            At(sim, id, item.position);
            Ok(sim, id, "pickup", item.id);
        }
        private static void Drop(HotelSimulation sim, ulong id = 0)
        {
            Assert(sim.Execute(id, new HotelCommand("drop") { position = Player(sim, id).position }) == "", "Drop failed");
            HotelSaveStore.Validate(sim.State);
        }
        private static void Work(HotelSimulation sim, string target, ulong id = 0)
        {
            At(sim, id, target);
            Ok(sim, id, "beginwork", target);
            for (int i = 0; i < 80 && Player(sim, id).workTarget != ""; i++)
            {
                Ok(sim, id, "heartbeat", target);
                sim.Tick(.1f);
            }
            Assert(Player(sim, id).workTarget == "", "Work never finished: " + target);
            HotelSaveStore.Validate(sim.State);
        }
        private static void Advance(HotelSimulation sim, float seconds)
        {
            for (float t = 0; t < seconds; t += .1f) sim.Tick(Mathf.Min(.1f, seconds - t));
            HotelSaveStore.Validate(sim.State);
        }
        private static void Open(HotelSimulation sim)
        {
            At(sim, 0, "desk"); Ok(sim, 0, "open");
        }
        private static GuestState CheckIn(HotelSimulation sim, int room = 101)
        {
            At(sim, 0, "desk"); Ok(sim, 0, "checkin", number: room);
            return sim.State.guests.Find(g => g.room == room && g.stage == "walking");
        }
        private static void InitialHotel()
        {
            HotelSimulation sim = New();
            sim.Join(0); sim.Join(2); sim.Join(ulong.MaxValue);
            Assert(sim.State.players.Count == 2 && sim.State.rooms.Count == 4, "Capacity");
            Assert(Vector3.Distance(Player(sim).position, Player(sim, 1).position) >= .9f, "Players spawned on top of each other");
            Assert(Room(sim, 101).bed == 2 && Room(sim, 102).bed == 1, "Initial preparation");
            Advance(sim, 30);
            Assert(sim.State.time == 0 && sim.State.guests.Count == 0, "Preparation advanced demand");
            Assert(sim.Tasks().Exists(t => t.Contains("102") && t.Contains("бельё")), "Tasks not derived from room");
        }
        private static void CommandValidation()
        {
            HotelSimulation sim = New();
            No(sim, 0, "open");
            At(sim, 1, "desk"); No(sim, 1, "open");
            At(sim, 0, new Vector3(-5.8f, 0, 1.6f));
            Room(sim, 101).leak = true;
            No(sim, 0, "beginwork", "sink_101");
            At(sim, 0, "desk"); No(sim, 0, "upgrade", "cart");
            No(sim, 0, "unknown"); No(sim, 9, "open");
            Assert(sim.Execute(0, new HotelCommand("pose") { position = new Vector3(float.NaN, 0, 0) }) != "", "NaN accepted");
            Assert(sim.Execute(0, new HotelCommand("pose") { position = new Vector3(20, 0, 20) }) != "", "Outside accepted");
            sim.Tick(float.NaN); sim.Tick(-1);
            Assert(sim.State.time == 0, "Invalid dt advanced time");
        }
        private static void Ownership()
        {
            HotelSimulation sim = New();
            string id = Item(sim, "toolbox").id;
            At(sim, 0, Item(sim, "toolbox").position); At(sim, 1, Player(sim).position);
            Ok(sim, 0, "pickup", id); Ok(sim, 0, "pickup", id);
            No(sim, 1, "pickup", id);
            sim.Leave(0); sim.Leave(0);
            Assert(Item(sim, "toolbox").holder == -1, "Disconnect retained ownership");
            Ok(sim, 1, "pickup", id);
            sim.Join(0);
            Assert(Player(sim).held == "", "Reconnect duplicated hand");
        }
        private static void StockAndDelivery()
        {
            HotelSimulation sim = New();
            At(sim, 0, "towels"); Ok(sim, 0, "interact", "towels");
            Assert(sim.State.towelStock == 11, "Stock");
            At(sim, 0, "towel_101"); No(sim, 0, "interact", "towel_101");
            At(sim, 0, "towel_103"); Ok(sim, 0, "interact", "towel_103");
            No(sim, 0, "interact", "towel_103");
            sim.State.towelStock = 0;
            At(sim, 0, "towels"); No(sim, 0, "interact", "towels");
            Assert(sim.State.cash == 400, "Free towel generated money");
        }
        private static void Cleaning()
        {
            HotelSimulation sim = New();
            Work(sim, "bed_102");
            Assert(Room(sim, 102).bed == 0 && Item(sim, "dirtylinen").holder == 0, "Stripping linen");
            At(sim, 0, "bin"); No(sim, 0, "interact", "bin");
            At(sim, 0, "hamper"); Ok(sim, 0, "interact", "hamper");
            At(sim, 0, "linen"); Ok(sim, 0, "interact", "linen");
            Work(sim, "bed_102");
            Assert(Room(sim, 102).bed == 2 && sim.State.linenStock == 11, "Making bed");
            Assert(Room(sim, 102).trash, "Bed work also cleaned trash");
            Work(sim, "trash_102");
            Assert(Item(sim, "trashbag").holder == 0 && !Room(sim, 102).trash, "Trash collection");
            At(sim, 0, "bin"); Ok(sim, 0, "interact", "bin");
            Assert(!sim.Tasks().Exists(t => t.Contains("102") && (t.Contains("мусор") || t.Contains("бельё"))), "Stale cleanup task");
        }
        private static void WorkLeases()
        {
            HotelSimulation sim = New();
            At(sim, 0, "bed_102"); At(sim, 1, "bed_102");
            Ok(sim, 0, "beginwork", "bed_102");
            sim.Tick(.4f);
            float progress = Player(sim).workProgress;
            Ok(sim, 0, "beginwork", "bed_102");
            Assert(Player(sim).workProgress == progress, "Duplicate begin reset progress");
            No(sim, 1, "beginwork", "bed_102");
            sim.Tick(5);
            Assert(Room(sim, 102).bed == 1 && Player(sim).workTarget == "", "Work finished after heartbeat expired");
            Ok(sim, 1, "beginwork", "bed_102");
            At(sim, 1, "desk");
            Assert(Player(sim, 1).workTarget == "", "Work persisted outside range");
            No(sim, 1, "heartbeat", "bed_102");
            Work(sim, "bed_102");
        }
        private static void RepairAndWater()
        {
            HotelSimulation sim = New();
            Room(sim, 101).leak = true; Room(sim, 101).water = .7f;
            At(sim, 0, "sink_101"); No(sim, 0, "beginwork", "sink_101");
            Pick(sim, 0, "toolbox"); Work(sim, "sink_101");
            Assert(!Room(sim, 101).leak && Room(sim, 101).water == .7f, "Repair removed water");
            Drop(sim); Pick(sim, 0, "mop"); Work(sim, "water_101");
            Assert(Room(sim, 101).water == 0, "Mop failed");
            Room(sim, 101).leak = true; Room(sim, 101).water = .3f;
            Work(sim, "water_101");
            Assert(Room(sim, 101).leak, "Mop repaired sink");
            Room(sim, 103).towel = true;
            Open(sim); Advance(sim, 1);
            Assert(Room(sim, 101).water > 0, "Active leak did not make more water");
        }
        private static void GuestRoutes()
        {
            HotelSimulation sim = New(); Open(sim);
            GuestState guest = CheckIn(sim);
            At(sim, 1, "desk"); No(sim, 1, "checkin", number: 101);
            Vector3 previous = guest.position;
            bool sawCorridor = false, sawDoor = false;
            for (int i = 0; i < 200 && guest.stage == "walking"; i++)
            {
                sim.Tick(.1f);
                Assert(Vector3.Distance(previous, guest.position) <= .161f, "NPC teleported");
                AssertRoutePosition(guest.position);
                if (guest.position.z > 2 && Math.Abs(guest.position.x) < .1f) sawCorridor = true;
                if (Math.Abs(guest.position.x + 1.5f) < .2f && Math.Abs(guest.position.z - 5.5f) < .01f) sawDoor = true;
                previous = guest.position;
            }
            Assert(guest.stage == "staying" && sawCorridor && sawDoor, "Incomplete arrival route");
            Advance(sim, 161);
            Assert(guest.stage == "checkout", "Guest never checked out");
            for (int i = 0; i < 200; i++) { sim.Tick(.1f); AssertRoutePosition(guest.position); }
            Assert(guest.position.z < 0 && Math.Abs(guest.position.x) < 1, "Guest missed reception");
        }
        private static void GuestService()
        {
            HotelSimulation sim = New(); Open(sim); GuestState guest = CheckIn(sim);
            Pick(sim, 1, "bag");
            Assert(Item(sim, "bag").ownerGuest == guest.id && Item(sim, "bag").holder == 1, "Ownership confused with holder");
            At(sim, 1, "bag_102"); No(sim, 1, "interact", "bag_102");
            At(sim, 1, "bag_101"); Ok(sim, 1, "interact", "bag_101");
            Assert(guest.luggageDelivered && Item(sim, "bag").placedRoom == 101, "Bag not delivered");
            Advance(sim, 45);
            Assert(guest.towelRequested && Room(sim, 101).towel, "Extra request consumed room's towel");
            At(sim, 0, "towels"); Ok(sim, 0, "interact", "towels");
            At(sim, 0, "towel_101"); Ok(sim, 0, "interact", "towel_101");
            Assert(!guest.towelRequested && sim.State.cash == 400, "Towel request not resolved or minted cash");
            Assert(!sim.Tasks().Exists(t => t.Contains("Дополнительное полотенце") && t.Contains("101")), "Stale request task");
        }
        private static void Economy()
        {
            HotelSimulation sim = New(); Open(sim); GuestState guest = CheckIn(sim);
            Room(sim, 103).leak = true; Room(sim, 103).water = .9f;
            Advance(sim, 20);
            Assert(!guest.memories.Contains("Пришлось ходить по мокрому полу"), "Guest penalized for another room");
            Room(sim, 101).leak = true; Room(sim, 101).water = .8f;
            Advance(sim, 1);
            float sad = guest.satisfaction;
            Advance(sim, 1);
            Assert(guest.satisfaction == sad, "Complaint penalized every frame");
            At(sim, 0, "desk"); Ok(sim, 0, "compensate", number: guest.id); No(sim, 0, "compensate", number: guest.id);
            Assert(Room(sim, 101).leak && Room(sim, 101).water >= .8f, "Compensation repaired room");
            No(sim, 0, "checkout", number: guest.id);
            Advance(sim, 170);
            Ok(sim, 0, "checkout", number: guest.id);
            int cash = sim.State.cash;
            No(sim, 0, "checkout", number: guest.id);
            Assert(sim.State.cash == cash && sim.State.served == 1 && sim.State.reviews.Count == 1, "Duplicate payment/review");
            Assert(Room(sim, 101).guestId == 0 && Room(sim, 101).bed == 1 && Room(sim, 101).trash, "Checkout did not leave turnover");
        }
        private static void Cart()
        {
            HotelSimulation sim = New();
            At(sim, 0, "board"); Ok(sim, 0, "upgrade", "cart");
            Open(sim); CheckIn(sim, 101); Advance(sim, 85); CheckIn(sim, 103);
            Pick(sim, 0, "cart");
            foreach (ItemState bag in sim.State.items.FindAll(i => i.kind == "bag" && !i.consumed))
            {
                At(sim, 0, bag.position); Ok(sim, 0, "pickup", bag.id);
                No(sim, 0, "pickup", bag.id);
            }
            Assert(sim.State.items.FindAll(i => i.placedRoom == -1).Count == 2, "Missing cargo");
            At(sim, 0, "bag_101"); Ok(sim, 0, "interact", "bag_101");
            At(sim, 0, "bag_103"); Ok(sim, 0, "interact", "bag_103");
            Assert(sim.State.guests.TrueForAll(g => g.luggageDelivered), "Cart failed delivery");
            Drop(sim); HotelSaveStore.Validate(sim.State);
        }
        private static void Upgrades()
        {
            HotelSimulation sim = New(); sim.State.cash = 1000;
            At(sim, 0, "board"); At(sim, 1, "board");
            No(sim, 1, "upgrade", "toolbox");
            foreach (string upgrade in new[] { "toolbox", "beds", "cart" })
            { Ok(sim, 0, "upgrade", upgrade); No(sim, 0, "upgrade", upgrade); }
            Assert(sim.State.cash == 60 && sim.State.expenses == 940, "Purchase amount");
            Assert(sim.State.items.FindAll(i => i.kind == "toolbox").Count == 2 && sim.State.items.FindAll(i => i.kind == "cart").Count == 1, "Duplicate fulfillment");
            Assert(sim.State.rooms.TrueForAll(r => r.upgraded), "Bed effect missing");
        }
        private static void Days()
        {
            HotelSimulation sim = New(); Open(sim); CheckIn(sim);
            Room(sim, 103).water = .6f; Room(sim, 103).leak = true;
            At(sim, 0, "desk"); Ok(sim, 0, "finish"); No(sim, 0, "finish");
            sim.State.cash = 0; sim.State.linenStock = 0; sim.State.towelStock = 0;
            Ok(sim, 0, "nextday");
            Assert(sim.State.cash < 0 && sim.State.linenStock == 12 && sim.State.towelStock == 12, "Debt recovery failed");
            int balance = sim.State.cash; No(sim, 0, "nextday");
            Assert(sim.State.cash == balance && sim.State.day == 2, "Double replenishment");
            Advance(sim, 30);
            Assert(Room(sim, 103).leak && Room(sim, 103).water == .6f && Room(sim, 101).bed == 1, "Preparation reset consequences");
            Work(sim, "bed_101"); At(sim, 0, "hamper"); Ok(sim, 0, "interact", "hamper");
            At(sim, 0, "linen"); Ok(sim, 0, "interact", "linen"); Work(sim, "bed_101");
            Open(sim); Assert(sim.State.day == 2 && sim.State.guests.Count == 1, "Second day stuck");
        }
        private static void Unattended()
        {
            HotelSimulation sim = New(); Open(sim); CheckIn(sim);
            Advance(sim, 500);
            Assert(sim.State.phase == "closing" && sim.State.arrivals == 3, "Day did not close");
            Assert(sim.State.guests.TrueForAll(g => g.stage == "gone" || g.stage == "leaving"), "Unattended guest softlock");
            At(sim, 0, "desk"); Ok(sim, 0, "finish"); Ok(sim, 0, "nextday");
            Assert(sim.State.phase == "preparation", "No recovery path");
        }
        private static void WithSave(Action<string> test)
        {
            string directory = Path.Combine(Path.GetTempPath(), "WorstHotelTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { test(Path.Combine(directory, "hotel.json")); }
            finally { Directory.Delete(directory, true); }
        }
        private static void SaveRoundtrip()
        {
            WithSave(path =>
            {
                Assert(HotelSaveStore.Load(path) == null, "Missing slot not empty");
                HotelSimulation sim = New(); Open(sim); GuestState guest = CheckIn(sim);
                Pick(sim, 0, "bag"); Pick(sim, 1, "mop");
                Advance(sim, 5);
                Room(sim, 104).leak = true; Room(sim, 104).water = .42f;
                HotelSaveStore.Save(sim.State, path);
                HotelState loaded = HotelSaveStore.Load(path);
                Assert(loaded.players.Count == 0 && loaded.items.TrueForAll(i => i.holder == -1), "Load retained connected ownership");
                Assert(Player(sim).held != "" && Player(sim, 1).held != "", "Save mutated live hands");
                Assert(loaded.worldId == sim.State.worldId && loaded.time == sim.State.time && loaded.cash == sim.State.cash, "Snapshot changed world");
                Assert(loaded.rooms.Find(r => r.number == 104).water == .42f && loaded.guests[0].id == guest.id, "Consequences or guest lost");
                Assert(loaded.items.Find(i => i.kind == "bag").ownerGuest == guest.id, "Luggage owner lost");
                var resumed = new HotelSimulation(loaded); resumed.Join(0); Pick(resumed, 0, "bag");
            });
        }
        private static void BackupRecovery()
        {
            WithSave(path =>
            {
                HotelSimulation sim = New(); HotelSaveStore.Save(sim.State, path);
                sim.State.cash = 350; HotelSaveStore.Save(sim.State, path);
                Assert(File.Exists(path + ".bak"), "Backup missing");
                File.WriteAllText(path, "{broken");
                HotelState recovered = HotelSaveStore.Load(path);
                Assert(recovered.cash == 400 && recovered.notice.Contains("резервная"), "Did not recover previous checkpoint");
                recovered.cash = 375; HotelSaveStore.Save(recovered, path);
                Assert(HotelSaveStore.Load(path).cash == 375, "Could not repair corrupt primary");
                File.WriteAllText(path, File.ReadAllText(path).Replace("375", "376"));
                Assert(HotelSaveStore.Load(path).cash == 400, "Checksum failure bypassed or good backup overwritten");
                File.WriteAllText(path + ".bak", "{}");
                Throws<InvalidDataException>(() => HotelSaveStore.Load(path));
                Assert(File.Exists(path), "Corrupt file was deleted");
            });
        }
        private static void SaveValidation()
        {
            WithSave(path =>
            {
                HotelSimulation sim = New(); HotelSaveStore.Save(sim.State, path);
                string original = File.ReadAllText(path);
                Room(sim, 101).water = float.NaN;
                Throws<InvalidDataException>(() => HotelSaveStore.Save(sim.State, path));
                Assert(File.ReadAllText(path) == original, "Invalid snapshot overwrote checkpoint");
                Room(sim, 101).water = 0;
                sim.State.rooms[1].number = 101;
                Throws<InvalidDataException>(() => HotelSaveStore.Validate(sim.State));
                sim.State.rooms[1].number = 102;
                Throws<InvalidOperationException>(() => HotelSaveStore.Save(new HotelSimulation().State, path));
                File.WriteAllText(path, "{\"format\":\"WorstHotelSave\",\"version\":99}");
                Throws<NotSupportedException>(() => HotelSaveStore.Load(path));
                Throws<NotSupportedException>(() => HotelSaveStore.Save(sim.State, path));
                Assert(File.ReadAllText(path).Contains("99"), "Future version overwritten");
            });
        }
        private static void SavedRoute()
        {
            WithSave(path =>
            {
                HotelSimulation sim = New(); Open(sim); CheckIn(sim, 103); Advance(sim, 6);
                HotelSaveStore.Save(sim.State, path);
                var resumed = new HotelSimulation(HotelSaveStore.Load(path));
                GuestState guest = resumed.State.guests[0];
                for (int i = 0; i < 200 && guest.stage == "walking"; i++)
                {
                    resumed.Tick(.1f);
                    AssertRoutePosition(guest.position);
                }
                Assert(guest.stage == "staying" && guest.room == 103, "Loaded guest route stuck");
            });
        }
        private static void TwoShifts()
        {
            WithSave(path =>
            {
                HotelSimulation sim = New();
                for (int day = 1; day <= 2; day++)
                {
                    foreach (RoomState room in sim.State.rooms)
                    {
                        if (room.bed == 1)
                        {
                            Work(sim, "bed_" + room.number); At(sim, 0, "hamper"); Ok(sim, 0, "interact", "hamper");
                        }
                        if (room.bed == 0)
                        {
                            At(sim, 0, "linen"); Ok(sim, 0, "interact", "linen"); Work(sim, "bed_" + room.number);
                        }
                        if (room.leak) { Pick(sim, 0, "toolbox"); Work(sim, "sink_" + room.number); Drop(sim); }
                        if (room.water > 0) { Pick(sim, 0, "mop"); Work(sim, "water_" + room.number); Drop(sim); }
                    }
                    Open(sim);
                    int count = day == 1 ? 3 : 4;
                    for (int i = 0; i < count; i++)
                    {
                        if (i > 0) Advance(sim, 85);
                        CheckIn(sim, 101 + i);
                        Pick(sim, 0, "bag"); At(sim, 0, "bag_" + (101 + i)); Ok(sim, 0, "interact", "bag_" + (101 + i));
                    }
                    Advance(sim, 240);
                    At(sim, 0, "desk"); Ok(sim, 0, "finish");
                    Assert(sim.State.served == count && sim.State.earned > 0, "Shift did not settle all guests");
                    if (day == 1) { At(sim, 0, "board"); Ok(sim, 0, "upgrade", "toolbox"); }
                    At(sim, 0, "desk"); Ok(sim, 0, "nextday");
                    HotelSaveStore.Save(sim.State, path);
                    sim = new HotelSimulation(HotelSaveStore.Load(path)); sim.Join(0); sim.Join(1);
                }
                Assert(sim.State.day == 3 && sim.State.secondToolbox && sim.State.reviews.Count == 7, "Two-day persistence failed");
            });
        }
        private static void AssertRoutePosition(Vector3 position)
        {
            Assert(!(Math.Abs(position.x) < 1.4f && position.z > -1.25f && position.z < -.15f), "NPC crossed reception desk");
            if (position.z > 2 && Math.Abs(position.x) > 1.35f && Math.Abs(position.x) < 1.7f)
                Assert(Math.Abs(position.z - 5.5f) < .7f || Math.Abs(position.z - 12.5f) < .7f, "NPC crossed room wall outside door");
            Assert(!(position.z > 1.85f && position.z < 2.25f && Math.Abs(position.x) > 1.35f), "NPC crossed lobby wall");
        }
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
    }
}
