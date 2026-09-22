using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WorstHotel
{
    public static class HotelDirectorTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "Director: authored resource and isolated catalogue definitions", Catalogue);
            Run(passed, "Director: new-world defaults and slow first arrival protection", Protection);
            Run(passed, "Director: solo service, cleanup, checkout, purchase and normal day two", () => FullCycle(false));
            Run(passed, "Director: second employee can complete every lesson", () => FullCycle(true));
            Run(passed, "Director: prepared-before-open fast team and real room selection", FastTeam);
            Run(passed, "Director: slow team has no patience or departure backlog", SlowService);
            Run(passed, "Director: mid-leak save and released snapshot do not repeat events", SaveDuringLeak);
            Run(passed, "Director: each guided stage survives save and reload", StageSnapshots);
            Run(passed, "Director: concurrent mop-first and repair-first completion", ParallelCompletion);
            Run(passed, "Director: residual water and already-dry outcomes do not lock pacing", DryResidual);
            Run(passed, "Director: help visibility, permanent opt-out and finish escape", OptOut);
            Run(passed, "Director: invalid/missing catalogue and defensive copies", InvalidCatalogue);
            Run(passed, "Director: active guest snapshots survive catalogue edits or disappearance", SnapshotStability);
            Run(passed, "Director: ordinary profile patience and departure differ mechanically", ProfileBehavior);
            Run(passed, "Director: legacy JSON does not enable pacing and future content is protected", Compatibility);
            Run(passed, "Director: departed lesson guest retargets without phantom rewards", LostGuest);
            return passed;
        }

        private static void Run(List<string> passed, string name, Action test)
        {
            try { test(); passed.Add(name); }
            catch (Exception error) { throw new Exception("HotelDirectorTests FAILED: " + name, error); }
        }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static HotelSimulation New(bool multiplayer = false)
        {
            HotelGuestCatalog catalog = HotelGuestCatalog.Load();
            Assert(catalog.Version == 1 && catalog.Warning == "", "Authored Resources/guest-profiles.json did not load");
            var sim = new HotelSimulation(null, catalog); sim.Join(0); if (multiplayer) sim.Join(1); return sim;
        }
        private static void Ok(HotelSimulation sim, ulong player, string action, string target = "", int number = 0)
        {
            string error = sim.Execute(player, new HotelCommand(action, target, number));
            Assert(error == "", action + " " + target + ": " + error);
            HotelSaveStore.Validate(sim.State);
        }
        private static void Advance(HotelSimulation sim, float duration)
        {
            for (int i = 0; i < (int)Math.Ceiling(duration * 10); i++) sim.Tick(.1f);
            HotelSaveStore.Validate(sim.State);
        }
        private static void At(HotelSimulation sim, ulong id, string target)
        {
            Vector3 position = HotelLayout.Target(target); position.y = .1f;
            Assert(sim.Execute(id, new HotelCommand("pose") { position = position }) == "", "Fixture position rejected");
        }
        private static PlayerState Player(HotelSimulation sim, ulong id) { return sim.State.players.Find(p => p.id == id); }
        private static RoomState Room(HotelSimulation sim, int number) { return sim.State.rooms.Find(r => r.number == number); }
        private static void No(HotelSimulation sim, ulong player, string action)
        {
            string before = JsonUtility.ToJson(sim.State);
            Assert(sim.Execute(player, new HotelCommand(action)) != "", "Expected rejection: " + action);
            Assert(before == JsonUtility.ToJson(sim.State), "Rejected command changed world");
        }
        private static void Pick(HotelSimulation sim, ulong player, string kind, int owner = 0)
        {
            ItemState item = sim.State.items.Find(i => !i.consumed && i.holder < 0 && i.kind == kind && (owner == 0 || i.ownerGuest == owner));
            Assert(item != null, "Missing " + kind);
            Vector3 position = item.position; position.y = .1f;
            Assert(sim.Execute(player, new HotelCommand("pose") { position = position }) == "", "Item approach rejected");
            Ok(sim, player, "pickup", item.id);
        }
        private static void Drop(HotelSimulation sim, ulong player)
        {
            Assert(sim.Execute(player, new HotelCommand("drop") { position = Player(sim, player).position }) == "", "Drop failed");
        }
        private static void Work(HotelSimulation sim, ulong player, string target)
        {
            At(sim, player, target); Ok(sim, player, "beginwork", target);
            for (int i = 0; i < 80 && Player(sim, player).workTarget != ""; i++)
            { Ok(sim, player, "heartbeat", target); sim.Tick(.1f); }
            Assert(Player(sim, player).workTarget == "", "Work did not finish");
            HotelSaveStore.Validate(sim.State);
        }
        private static void CleanBed(HotelSimulation sim, ulong player, int number)
        {
            if (Room(sim, number).bed == 1)
            {
                Work(sim, player, "bed_" + number); At(sim, player, "hamper"); Ok(sim, player, "interact", "hamper");
            }
            if (Room(sim, number).bed == 0)
            {
                At(sim, player, "linen"); Ok(sim, player, "interact", "linen"); Work(sim, player, "bed_" + number);
            }
        }
        private static void Deliver(HotelSimulation sim, ulong player, GuestState guest)
        {
            Pick(sim, player, "bag", guest.id); At(sim, player, "bag_" + guest.room); Ok(sim, player, "interact", "bag_" + guest.room);
        }
        private static void Towel(HotelSimulation sim, ulong player, GuestState guest)
        {
            Assert(guest.towelRequested, "No real request");
            At(sim, player, "towels"); Ok(sim, player, "interact", "towels");
            At(sim, player, "towel_" + guest.room); Ok(sim, player, "interact", "towel_" + guest.room);
        }
        private static GuestState FirstService(HotelSimulation sim, ulong worker, int room = 103)
        {
            At(sim, 0, "desk"); Ok(sim, 0, "open");
            GuestState first = sim.State.guests[0];
            At(sim, worker, "desk"); Ok(sim, worker, "checkin", number: room);
            Advance(sim, 60);
            Assert(first.stage == "staying" && !first.towelRequested && sim.State.arrivals == 1 && first.stay == 0, "Request/departure preceded luggage lesson");
            Deliver(sim, worker, first); Advance(sim, first.requestDelay + 1);
            Assert(first.towelRequested && Room(sim, room).towel == (room != 103), "Request did not follow delivered bag or erased standing towel");
            Towel(sim, worker, first); Advance(sim, .2f);
            return first;
        }
        private static HotelSimulation AtLeak(bool multiplayer = false, bool prepared = false)
        {
            HotelSimulation sim = New(multiplayer); ulong worker = multiplayer ? 1UL : 0UL;
            sim.State.nextGuest = 17; // A real non-1 guest verifies stable ownership/target selection.
            if (prepared) CleanBed(sim, worker, 104);
            FirstService(sim, worker);
            if (!prepared)
            {
                Assert(sim.State.arrivals == 2 && !sim.State.dailyLeakIssued && sim.State.guidedStage == HotelDirector.Preparation, "Second arrival/leak gate order");
                HotelHint hint = HotelOnboarding.GetHint(sim.State, worker);
                Assert(hint != null && hint.targetId != "desk", "Queue hint hid the required preparation gate");
                CleanBed(sim, worker, 104);
            }
            Advance(sim, .2f);
            Assert(sim.State.guidedStage == HotelDirector.Leak && sim.State.guidedLeakRoom == 103 && Room(sim, 103).leak && sim.State.dailyLeakIssued, "Leak not issued at real first guest room");
            Assert(sim.State.cash == 400 && sim.State.earned == 0 && sim.State.expenses == 0, "Director minted a lesson reward");
            return sim;
        }
        private static void FixAndMop(HotelSimulation sim, ulong worker)
        {
            int room = sim.State.guidedLeakRoom;
            Pick(sim, worker, "toolbox"); Work(sim, worker, "sink_" + room);
            Assert(!Room(sim, room).leak && Room(sim, room).water > .001f && HotelDirector.ClockHeld(sim.State), "Repair swallowed cleanup lesson");
            Drop(sim, worker); Pick(sim, worker, "mop"); Work(sim, worker, "water_" + room); Drop(sim, worker);
            Assert(!HotelDirector.ClockHeld(sim.State) && sim.State.guidedStage == HotelDirector.Released, "Foundations did not release normal time");
        }
        private static void FullCycle(bool multiplayer)
        {
            HotelSimulation sim = AtLeak(multiplayer); ulong worker = multiplayer ? 1UL : 0UL;
            FixAndMop(sim, worker);
            Assert((sim.State.tutorialFlags & 63) == 63, "Shared team skills not recorded from worker");
            float releasedAt = sim.State.time;
            Assert(sim.State.arrivals == 2, "Release spawned backlog immediately");
            At(sim, worker, "desk"); Ok(sim, worker, "checkin", number: 101);
            Advance(sim, 44);
            Assert(sim.State.arrivals == 2, "Overlap arrived before promised release spacing");
            Advance(sim, 2);
            Assert(sim.State.arrivals == 3 && sim.State.time > releasedAt, "Bounded overlap missing");
            At(sim, worker, "desk"); Ok(sim, worker, "checkin", number: 104);
            Advance(sim, 165);
            GuestState first = sim.State.guests.Find(g => g.id == sim.State.guidedGuestId);
            Assert(first.stage == "checkout" && !first.paid, "First real checkout missed");
            At(sim, worker, "desk"); Ok(sim, worker, "checkout", number: first.id);
            Assert(sim.State.cash > 400 && sim.State.served == 1, "Payment did not come from actual checkout");
            At(sim, 0, "desk"); Ok(sim, 0, "finish");
            Assert(sim.State.served == 3 && sim.State.reviews.Count == 3 && Room(sim, 103).bed == 1, "Summary lost guests or turnover");
            int closingCash = sim.State.cash;
            Ok(sim, 0, "nextday");
            Assert(sim.State.day == 2 && sim.State.cash < closingCash && !HotelDirector.IsGuided(sim.State), "Day two did not use normal supply costs/pacing");
            HotelHint dayTwoHint = HotelOnboarding.GetHint(sim.State, worker);
            Assert(dayTwoHint != null && dayTwoHint.title.Contains("День 2") && dayTwoHint.targetId == "board", "Day two purchase guidance missing after completed skills");
            At(sim, 0, "board"); Ok(sim, 0, "upgrade", "toolbox");
            Assert(sim.State.secondToolbox && sim.State.items.FindAll(i => i.kind == "toolbox" && !i.consumed).Count == 2, "Upgrade not physical");
            CleanBed(sim, worker, 104);
            At(sim, 0, "desk"); Ok(sim, 0, "open"); Advance(sim, 190);
            Assert(sim.State.time > 189 && sim.State.arrivals == 3 && sim.State.guests.Exists(g => g.trait == "Спешит") && sim.State.guests.Exists(g => g.kind == "tidy"), "Day two did not run authored normal profiles");
            Advance(sim, 80);
            Assert(sim.State.arrivals == 4, "Day two capacity/schedule");
        }
        private static void FastTeam()
        {
            HotelSimulation sim = AtLeak(false, true);
            Assert(sim.State.time == 0 && sim.State.arrivals == 2, "Fast prep must not wait a fixed minute or create extra arrivals");
            At(sim, 0, "board"); Ok(sim, 0, "toggleRoom", number: 103);
            Assert(Room(sim, 103).outOfService && Room(sim, 103).guestId != 0, "OOS lost occupied room");
            FixAndMop(sim, 0);
            Assert(sim.State.arrivals == 2, "OOS source caused director backlog");
        }
        private static void SlowService()
        {
            HotelSimulation sim = New(true); At(sim, 0, "desk"); Ok(sim, 0, "open");
            At(sim, 1, "desk"); Ok(sim, 1, "checkin", number: 101); Advance(sim, 800);
            GuestState guest = sim.State.guests[0];
            Assert(!guest.towelRequested && guest.stage == "staying" && guest.stay == 0 && guest.waited == 0, "Slow luggage lesson timed out");
            Deliver(sim, 1, guest); Advance(sim, 700);
            Assert(guest.towelRequested && guest.requestWait == 0 && !guest.memories.Contains("Не дождался дополнительного полотенца"), "Protected request accumulated complaint backlog");
            Towel(sim, 1, guest); CleanBed(sim, 1, 104); Advance(sim, 500);
            Assert(sim.State.arrivals == 2 && sim.State.guests.TrueForAll(g => g.stage == "queue" || g.stage == "staying"), "Slow repair lost current guests");
            FixAndMop(sim, 1); Advance(sim, 10);
            Assert(guest.stay < 11 && !guest.paid && sim.State.guests[1].waited < 11, "Unfreeze applied protected elapsed time retroactively");
        }
        private static void WithSave(Action<string> test)
        {
            string directory = Path.Combine(Path.GetTempPath(), "WorstHotelDirector-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { test(Path.Combine(directory, "hotel.json")); }
            finally { Directory.Delete(directory, true); }
        }
        private static void SaveDuringLeak()
        {
            WithSave(path =>
            {
                HotelSimulation sim = AtLeak(true); int guestId = sim.State.guidedGuestId;
                Pick(sim, 1, "toolbox"); At(sim, 1, "sink_103"); Ok(sim, 1, "beginwork", "sink_103");
                for (int i = 0; i < 20; i++) { Ok(sim, 1, "heartbeat", "sink_103"); sim.Tick(.1f); }
                HotelSaveStore.Save(sim.State, path);
                HotelState loaded = HotelSaveStore.Load(path);
                Assert(loaded.players.Count == 0 && loaded.guidedGuestId == guestId && loaded.guidedLeakRoom == 103 && !loaded.guidedRepairDone, "Mid-work save lost stage or incorrectly completed work");
                sim = new HotelSimulation(loaded); sim.Join(0); sim.Join(1); Advance(sim, 30);
                Assert(sim.State.guests.Find(g => g.id == guestId).memories.FindAll(m => m == "Раковина начала протекать").Count == 1 && sim.State.arrivals == 2, "Reload duplicated leak or arrival");
                FixAndMop(sim, 1);
                float due = sim.State.nextArrivalTime;
                HotelSaveStore.Save(sim.State, path); sim = new HotelSimulation(HotelSaveStore.Load(path)); sim.Join(0);
                Advance(sim, 1);
                Assert(!Room(sim, 103).leak && sim.State.nextArrivalTime == due && sim.State.cash == 400 && sim.State.served == 0, "Released save reran event/reward/scheduling");
            });
        }
        private static void ParallelCompletion()
        {
            for (int repairer = 0; repairer <= 1; repairer++)
            {
                HotelSimulation sim = AtLeak(true); ulong repair = (ulong)repairer, mop = (ulong)(1 - repairer);
                Pick(sim, repair, "toolbox"); Pick(sim, mop, "mop");
                At(sim, repair, "sink_103"); Ok(sim, repair, "beginwork", "sink_103");
                for (int i = 0; i < 21; i++) { Ok(sim, repair, "heartbeat", "sink_103"); sim.Tick(.1f); }
                At(sim, mop, "water_103"); Ok(sim, mop, "beginwork", "water_103");
                bool simultaneous = false;
                for (int i = 0; i < 40 && (Player(sim, repair).workTarget != "" || Player(sim, mop).workTarget != ""); i++)
                {
                    bool repairing = Player(sim, repair).workTarget != "", mopping = Player(sim, mop).workTarget != "";
                    if (repairing) Ok(sim, repair, "heartbeat", "sink_103");
                    if (mopping) Ok(sim, mop, "heartbeat", "water_103");
                    sim.Tick(.1f);
                    if (repairing && mopping && Player(sim, repair).workTarget == "" && Player(sim, mop).workTarget == "") simultaneous = true;
                }
                Assert(simultaneous, "Fixture did not exercise same-Step worker completion");
                Assert(!Room(sim, 103).leak && Room(sim, 103).water <= .001f && !HotelDirector.ClockHeld(sim.State), "Concurrent completion softlocked for player order " + repairer);
                Assert((sim.State.tutorialFlags & (int)HotelTutorialSkill.MopWater) != 0, "Concurrent mop lost real skill evidence");
            }
        }
        private static HotelSimulation Reload(HotelSimulation sim, string path)
        {
            int stage = sim.State.guidedStage, arrivals = sim.State.arrivals, skills = sim.State.tutorialFlags;
            float time = sim.State.time, due = sim.State.nextArrivalTime;
            HotelSaveStore.Save(sim.State, path);
            var resumed = new HotelSimulation(HotelSaveStore.Load(path)); resumed.Join(0);
            Assert(resumed.State.guidedStage == stage && resumed.State.arrivals == arrivals && resumed.State.tutorialFlags == skills &&
                resumed.State.time == time && resumed.State.nextArrivalTime == due, "Reload changed stage, skills, arrivals or schedule");
            return resumed;
        }
        private static void StageSnapshots()
        {
            WithSave(path =>
            {
                HotelSimulation sim = Reload(New(), path);
                Assert(sim.State.guidedStage == HotelDirector.Welcome, "Preparation snapshot");
                At(sim, 0, "desk"); Ok(sim, 0, "open"); sim = Reload(sim, path);
                Assert(sim.State.guests.Count == 1 && sim.State.guidedStage == HotelDirector.Service, "Queue snapshot");
                At(sim, 0, "desk"); Ok(sim, 0, "checkin", number: 103); sim = Reload(sim, path);
                GuestState first = sim.State.guests[0];
                Deliver(sim, 0, first); Advance(sim, 50); sim = Reload(sim, path);
                first = sim.State.guests[0]; Assert(first.towelRequested && first.luggageDelivered, "Pending request snapshot");
                Towel(sim, 0, first); Advance(sim, .1f); sim = Reload(sim, path);
                Assert(sim.State.guidedStage == HotelDirector.Preparation && sim.State.arrivals == 2, "Preparation prerequisite snapshot");
                CleanBed(sim, 0, 104); sim = Reload(sim, path);
                Assert(sim.State.guidedStage == HotelDirector.Leak && Room(sim, 103).leak, "Leak source snapshot");
                Pick(sim, 0, "toolbox"); Work(sim, 0, "sink_103"); sim = Reload(sim, path);
                Assert(sim.State.guidedRepairDone && !sim.State.guidedMopDone && Room(sim, 103).water > .001f, "Repair-only snapshot");
                Pick(sim, 0, "mop"); Work(sim, 0, "water_103"); sim = Reload(sim, path); Advance(sim, 1);
                Assert(sim.State.guidedStage == HotelDirector.Released && sim.State.arrivals == 2 && !Room(sim, 103).leak, "Release snapshot repeated events");
                Assert(sim.State.guests[0].memories.FindAll(m => m == "Раковина начала протекать").Count == 1, "Stage reload duplicated leak memory");
            });
        }
        private static void DryResidual()
        {
            HotelSimulation sim = AtLeak();
            Pick(sim, 0, "mop"); Work(sim, 0, "water_103"); Drop(sim, 0);
            Assert(Room(sim, 103).leak && HotelDirector.ClockHeld(sim.State), "Mopping active source skipped repair");
            Pick(sim, 0, "toolbox"); Work(sim, 0, "sink_103");
            Assert(Room(sim, 103).water > .001f && HotelDirector.ClockHeld(sim.State), "Mop-before-repair swallowed newly leaked water");
            // A valid restored residual below the work predicate must not require an impossible action.
            Room(sim, 103).water = .0005f;
            Advance(sim, .1f);
            Assert(!HotelDirector.ClockHeld(sim.State) && Room(sim, 103).water == .0005f, "Residual locked pacing or director minted/removed water");
        }
        private static void OptOut()
        {
            HotelSimulation sim = New(true); At(sim, 0, "desk"); Ok(sim, 0, "open"); Advance(sim, 500);
            At(sim, 0, "bed_104");
            No(sim, 1, "endGuidedOpening"); Ok(sim, 0, "skipTutorial");
            Assert(HotelDirector.ClockHeld(sim.State) && sim.State.guidedOpening, "Help skip changed pace");
            Ok(sim, 0, "resumeTutorial"); Assert(HotelDirector.ClockHeld(sim.State), "Resume changed pace");
            Ok(sim, 0, "endGuidedOpening");
            string once = JsonUtility.ToJson(sim.State); Ok(sim, 0, "endGuidedOpening");
            Assert(JsonUtility.ToJson(sim.State) == once, "Opt-out not idempotent");
            Ok(sim, 0, "resumeTutorial"); Advance(sim, 44);
            Assert(!sim.State.guidedOpening && sim.State.arrivals == 1 && sim.State.guests[0].waited < 45, "Opt-out burst or backlog");
            Advance(sim, 2); Assert(sim.State.arrivals == 2, "Opt-out never resumed normal arrivals");

            sim = New(); At(sim, 0, "desk"); Ok(sim, 0, "open"); Ok(sim, 0, "finish"); Ok(sim, 0, "nextday");
            Assert(sim.State.day == 2 && !HotelDirector.IsGuided(sim.State), "Finish cannot escape incomplete lesson");
            sim = New(); Ok(sim, 0, "endGuidedOpening"); At(sim, 0, "desk"); Ok(sim, 0, "open"); Advance(sim, 1);
            Assert(sim.State.arrivals == 1 && sim.State.time > 0, "Pre-open opt-out stopped first arrival");
        }
        private static string AuthoredJson() { return Resources.Load<TextAsset>("guest-profiles").text; }
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        private static void InvalidCatalogue()
        {
            string json = AuthoredJson();
            foreach (string invalid in new[] { "", "{}", "{broken", json.Replace("\"patient\"", "\"tidy\""),
                json.Replace("\"patienceLimit\": 240", "\"patienceLimit\": 5"), json.Replace("\"extra_towel\"", "\"unknown\""),
                json.Replace("\"graceSeconds\": 55", "\"graceSeconds\": -1") })
                Throws<InvalidDataException>(() => HotelGuestCatalog.FromJson(invalid));
            HotelGuestCatalog missing = HotelGuestCatalog.FromJsonOrLegacy(null);
            Assert(missing.Version == 0 && missing.Warning != "" && missing.GetProfile("patient") == null, "Missing catalogue not reported as legacy fallback");
            var sim = new HotelSimulation(null, missing); sim.Join(0); At(sim, 0, "desk"); Ok(sim, 0, "open");
            Assert(sim.State.guests[0].profileVersion == 0 && sim.CatalogWarning != "", "Fallback secretly created a fake authored profile");
            HotelGuestCatalog catalog = HotelGuestCatalog.Load();
            Assert(catalog.GetProfile("missing") == null && catalog.GetRequest("missing") == null, "Missing entry silently borrowed another profile");
        }
        private static void SnapshotStability()
        {
            WithSave(path =>
            {
                HotelSimulation sim = New(); sim.State.day = 2;
                At(sim, 0, "desk"); Ok(sim, 0, "open"); Ok(sim, 0, "checkin", number: 101);
                GuestState original = sim.State.guests[0]; float stay = original.stayDuration, delay = original.requestDelay;
                HotelSaveStore.Save(sim.State, path);
                string changed = AuthoredJson().Replace("\"staySeconds\": 180", "\"staySeconds\": 240").Replace("\"delaySeconds\": 18", "\"delaySeconds\": 60")
                    .Replace("\"staySeconds\": 110", "\"staySeconds\": 90");
                sim = new HotelSimulation(HotelSaveStore.Load(path), HotelGuestCatalog.FromJson(changed)); sim.Join(0);
                Assert(sim.State.guests[0].stayDuration == stay && sim.State.guests[0].requestDelay == delay, "Reload rebalanced existing guest");
                Advance(sim, 86);
                Assert(sim.State.guests[1].kind == "hurried" && sim.State.guests[1].stayDuration == 90, "New arrival ignored updated catalogue");
                Advance(sim, 110);
                Assert(sim.State.guests[0].stage == "checkout", "Saved guest departure used edited 240-second catalogue");
                sim = new HotelSimulation(HotelSaveStore.Load(path), HotelGuestCatalog.Legacy("fixture missing resource"));
                Advance(sim, 86);
                Assert(sim.State.guests[0].profileVersion == 1 && sim.State.guests[0].stayDuration == stay && sim.State.guests[1].profileVersion == 0, "Missing catalogue rewrote persisted snapshot");
            });
        }
        private static void ProfileBehavior()
        {
            HotelSimulation sim = New(); sim.State.day = 2;
            At(sim, 0, "desk"); Ok(sim, 0, "open"); Advance(sim, 192);
            GuestState patient = sim.State.guests[0], hurried = sim.State.guests[1];
            Assert(patient.stage == "queue" && (hurried.stage == "leaving" || hurried.stage == "gone") && hurried.patienceLimit < patient.patienceLimit, "Patience differences are only cosmetic");
            Assert(patient.requestDelay != hurried.requestDelay && patient.stayDuration != hurried.stayDuration, "Request/stay snapshots not different");
            string shortStay = AuthoredJson().Replace("\"staySeconds\": 180", "\"staySeconds\": 60");
            sim = new HotelSimulation(null, HotelGuestCatalog.FromJson(shortStay)); sim.Join(0); sim.State.day = 2;
            At(sim, 0, "desk"); Ok(sim, 0, "open"); Ok(sim, 0, "checkin", number: 101); Advance(sim, 80);
            Assert(sim.State.guests[0].stage == "checkout" && sim.State.guests[0].towelRequested, "Authored stay/request timing not used by simulation");
        }
        [Serializable] private sealed class Envelope
        {
            public string format = "WorstHotelSave";
            public int version = 1;
            public string payload, checksum;
        }
        private static void WriteSnapshot(string path, string payload)
        {
            var envelope = new Envelope { payload = payload };
            using (SHA256 hash = SHA256.Create()) envelope.checksum = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(path, JsonUtility.ToJson(envelope));
        }
        private static void Compatibility()
        {
            WithSave(path =>
            {
                var sim = new HotelSimulation(null, HotelGuestCatalog.Legacy()); sim.State.contentVersion = 0; sim.State.guidedOpening = false; sim.Join(0);
                At(sim, 0, "desk"); Ok(sim, 0, "open"); Ok(sim, 0, "checkin", number: 101); Advance(sim, 40);
                string fields = "contentVersion|guidedOpening|guidedStage|guidedGuestId|guidedLeakRoom|guidedRepairDone|guidedMopDone|dailyLeakIssued|nextArrivalTime|profileVersion|profileId|requestId|requestKind|patienceWarning|patienceLimit|requestDelay|requestGrace|stayDuration|luggageGrace|checkoutWarning|checkoutLimit|requestElapsed|requestWait";
                string legacy = Regex.Replace(JsonUtility.ToJson(sim.State), "\"(?:" + fields + ")\"\\s*:\\s*(?:\"[^\"]*\"|true|false|null|-?[0-9.Ee+]+),?", "");
                legacy = Regex.Replace(legacy, ",\\s*([}\\]])", "$1");
                Assert(!legacy.Contains("contentVersion") && !legacy.Contains("guidedOpening") && !legacy.Contains("profileVersion") && !legacy.Contains("requestElapsed"), "Legacy fixture retained new fields");
                WriteSnapshot(path, legacy);
                HotelState loaded = HotelSaveStore.Load(path);
                Assert(loaded.contentVersion == 0 && !loaded.guidedOpening && loaded.guests[0].profileVersion == 0 && loaded.guests[0].towelRequested, "Old midshift was reinterpreted as guided opening");
                sim = new HotelSimulation(loaded); float time = sim.State.time; Advance(sim, 1);
                Assert(sim.State.time > time && !HotelDirector.ClockHeld(sim.State), "Old clock stopped");
                HotelSaveStore.Save(sim.State, path); string valid = File.ReadAllText(path);
                loaded.contentVersion = 2; WriteSnapshot(path, JsonUtility.ToJson(loaded)); string future = File.ReadAllText(path);
                Throws<NotSupportedException>(() => HotelSaveStore.Load(path));
                loaded.contentVersion = 0;
                Throws<NotSupportedException>(() => HotelSaveStore.Save(loaded, path));
                Assert(File.ReadAllText(path) == future && File.Exists(path + ".bak"), "Future content was downgraded via backup");
                File.WriteAllText(path, valid);
                sim = New(); At(sim, 0, "desk"); Ok(sim, 0, "open"); sim.State.guests[0].profileVersion = 2;
                Throws<NotSupportedException>(() => HotelSaveStore.Validate(sim.State));
                sim.State.guests[0].profileVersion = 1; sim.State.guests[0].requestDelay = float.NaN;
                Throws<InvalidDataException>(() => HotelSaveStore.Validate(sim.State));
                sim = New(); sim.State.guidedStage = HotelDirector.Leak;
                Throws<InvalidDataException>(() => HotelSaveStore.Validate(sim.State));
            });
        }
        private static void LostGuest()
        {
            HotelSimulation sim = New(); At(sim, 0, "desk"); Ok(sim, 0, "open");
            GuestState old = sim.State.guests[0]; old.stage = "gone";
            sim.State.items.Find(i => i.ownerGuest == old.id).consumed = true;
            Advance(sim, .3f);
            Assert(sim.State.arrivals == 2 && sim.State.guidedGuestId != old.id && sim.State.guests[1].stage == "queue", "Director retained a departed lesson guest");
            Assert(sim.State.tutorialFlags == 0 && sim.State.cash == 400, "Retarget fabricated service/rewards");
            At(sim, 0, "desk"); Ok(sim, 0, "checkin", number: 101); Advance(sim, 20);
            HotelHint hint = HotelOnboarding.GetHint(sim.State, 0);
            Assert(hint != null && hint.targetId == "luggage_" + sim.State.guidedGuestId, "Retargeted hint points to old luggage");
        }
        private static void Catalogue()
        {
            HotelGuestCatalog catalog = HotelGuestCatalog.Load();
            Assert(catalog.Version == 1, "Missing catalogue");
            HotelGuestProfile patient = catalog.GetProfile("patient"), hurried = catalog.GetProfile("hurried"), tidy = catalog.GetProfile("tidy");
            Assert(patient != null && hurried != null && tidy != null, "Required profile missing");
            Assert(patient.patienceLimit > hurried.patienceLimit && patient.staySeconds > hurried.staySeconds && tidy.staySeconds != patient.staySeconds, "Profiles have cosmetic-only differences");
            HotelRequestDefinition request = catalog.GetRequest(patient.requestId);
            Assert(request.delaySeconds != catalog.GetRequest(hurried.requestId).delaySeconds, "Request timing not authored per profile");
            patient.staySeconds = 999; request.delaySeconds = 999;
            Assert(catalog.GetProfile("patient").staySeconds != 999 && catalog.GetRequest("patient_towel").delaySeconds != 999, "Catalogue leaked mutable definitions");
        }
        private static void Protection()
        {
            HotelSimulation sim = New();
            Assert(sim.State.contentVersion == 1 && sim.State.guidedOpening && HotelDirector.IsGuided(sim.State) && !HotelDirector.ClockHeld(sim.State), "New hotel does not opt in only when open");
            At(sim, 0, "desk"); Ok(sim, 0, "open"); Advance(sim, 600);
            Assert(sim.State.guests.Count == 1 && sim.State.guests[0].stage == "queue" && sim.State.guests[0].waited == 0 && sim.State.time == 0, "Slow first team lost its guest or clock");
            Assert(sim.State.guests[0].kind == "patient" && HotelDirector.ClockHeld(sim.State), "First guest is not protected patient");
            string before = JsonUtility.ToJson(sim.State);
            Assert(HotelDirector.Status(sim.State).Contains("зарегистрируйте") && HotelDirector.DayBrief(sim.State).Contains("трёх"), "Pacing not explained");
            Assert(JsonUtility.ToJson(sim.State) == before, "UI APIs changed director state");
        }
    }
}
