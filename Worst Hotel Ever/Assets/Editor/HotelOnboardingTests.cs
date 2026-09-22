using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WorstHotel
{
    public static class HotelOnboardingTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "Onboarding: read-only projection, defaults and completion", Projection);
            Run(passed, "Onboarding: check-in success, rejection and second player", CheckInSkill);
            Run(passed, "Onboarding: owned luggage and requested extra towel only", ServiceSkills);
            Run(passed, "Onboarding: completed work only, no tutorial rewards", WorkSkills);
            Run(passed, "Onboarding: checkout and next day require successful commands", DaySkills);
            Run(passed, "Onboarding: host skip/resume without distance or progress reset", SkipResume);
            Run(passed, "Onboarding: held items guide to current usable targets", HeldGuidance);
            Run(passed, "Onboarding: departed guests and old work targets are not sticky", Retargeting);
            Run(passed, "Onboarding: phase guidance and shared check-in rules", Readiness);
            Run(passed, "Onboarding: legacy version 1 JSON and durable progress", Persistence);
            return passed;
        }

        private static void Run(List<string> passed, string name, Action test)
        {
            try { test(); passed.Add(name); }
            catch (Exception error) { throw new Exception("HotelOnboardingTests FAILED: " + name, error); }
        }
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static HotelSimulation New()
        {
            var sim = new HotelSimulation(); sim.Join(0); sim.Join(1); return sim;
        }
        private static void Ok(HotelSimulation sim, ulong id, string action, string target = "", int number = 0)
        {
            string error = sim.Execute(id, new HotelCommand(action, target, number));
            Assert(error == "", action + ": " + error);
            HotelSaveStore.Validate(sim.State);
        }
        private static void No(HotelSimulation sim, ulong id, string action, string target = "", int number = 0)
        {
            string before = JsonUtility.ToJson(sim.State);
            Assert(sim.Execute(id, new HotelCommand(action, target, number)) != "", "Command should fail: " + action);
            Assert(before == JsonUtility.ToJson(sim.State), "Rejected command changed state or progress: " + action);
        }
        private static PlayerState Player(HotelSimulation sim, ulong id = 0) { return sim.State.players.Find(p => p.id == id); }
        private static RoomState Room(HotelSimulation sim, int number) { return sim.State.rooms.Find(r => r.number == number); }
        private static void At(HotelSimulation sim, ulong id, Vector3 position)
        {
            position.y = .1f;
            Assert(sim.Execute(id, new HotelCommand("pose") { position = position }) == "", "Invalid test position");
        }
        private static void At(HotelSimulation sim, ulong id, string target) { At(sim, id, HotelLayout.Target(target)); }
        private static void Pick(HotelSimulation sim, ulong id, string kind)
        {
            ItemState item = sim.State.items.Find(i => i.kind == kind && !i.consumed && i.holder == -1);
            Assert(item != null, "Missing " + kind);
            At(sim, id, item.position); Ok(sim, id, "pickup", item.id);
        }
        private static void Drop(HotelSimulation sim, ulong id = 0)
        {
            Assert(sim.Execute(id, new HotelCommand("drop") { position = Player(sim, id).position }) == "", "Drop failed");
        }
        private static void Advance(HotelSimulation sim, float seconds)
        {
            int steps = (int)Math.Ceiling(seconds * 10);
            for (int i = 0; i < steps; i++) sim.Tick(.1f);
        }
        private static void Work(HotelSimulation sim, ulong id, string target)
        {
            At(sim, id, target); Ok(sim, id, "beginwork", target);
            for (int i = 0; i < 80 && Player(sim, id).workTarget != ""; i++)
            { Ok(sim, id, "heartbeat", target); sim.Tick(.1f); }
            Assert(Player(sim, id).workTarget == "", "Work not finished");
        }
        private static void Open(HotelSimulation sim) { At(sim, 0, "desk"); Ok(sim, 0, "open"); }
        private static void Flags(HotelSimulation sim, HotelTutorialSkill expected)
        {
            Assert(sim.State.tutorialFlags == (int)expected, "Expected skills " + expected + ", got " + (HotelTutorialSkill)sim.State.tutorialFlags);
        }
        private static HotelHint Hint(HotelSimulation sim, ulong id = 0)
        {
            string before = JsonUtility.ToJson(sim.State);
            HotelHint hint = HotelOnboarding.GetHint(sim.State, id);
            Assert(before == JsonUtility.ToJson(sim.State), "UI derivation mutated authoritative state");
            Assert(hint != null && hint.total == 8 && !hint.finished && !string.IsNullOrEmpty(hint.body) && !string.IsNullOrEmpty(hint.title), "Missing active hint");
            return hint;
        }

        private static void Projection()
        {
            HotelSimulation sim = New();
            Assert(!sim.State.tutorialSkipped && sim.State.tutorialFlags == 0, "New-world defaults");
            HotelHint hint = Hint(sim);
            Assert(hint.targetId == "desk" && hint.body.Contains("E") && hint.body.Contains("Открыть") && hint.completed == 0, "Opening guidance");
            Assert(HotelOnboarding.GetHint(null, 0) == null && HotelOnboarding.GetHint(sim.State, 99) == null, "Unconnected preview");
            sim.State.tutorialFlags = (1 << 28) | (int)HotelTutorialSkill.CleanBed;
            Assert(Hint(sim).completed == 1, "Unknown bits counted as skills");
            sim.State.tutorialFlags = (int)HotelTutorialSkill.All;
            Assert(HotelOnboarding.GetHint(sim.State, 0) == null, "Complete help stays visible");
            sim.State.tutorialFlags = 0; sim.State.day = 2;
            Assert(HotelOnboarding.GetHint(sim.State, 0) == null, "First-shift assistance shown on day two");
        }

        private static void CheckInSkill()
        {
            HotelSimulation sim = New();
            No(sim, 1, "checkin", number: 103); Flags(sim, HotelTutorialSkill.None);
            Open(sim); Flags(sim, HotelTutorialSkill.None);
            At(sim, 1, "desk"); No(sim, 1, "checkin", number: 102); Flags(sim, HotelTutorialSkill.None);
            Ok(sim, 1, "checkin", number: 103); Flags(sim, HotelTutorialSkill.CheckIn);
            No(sim, 0, "checkin", number: 103);
            Assert(Hint(sim).completed == 1 && Hint(sim, 1).completed == 1, "Skill not shared with team");
            Assert(sim.State.cash == 400 && sim.State.earned == 0 && sim.State.expenses == 0, "Check-in tutorial reward");
        }

        private static void ServiceSkills()
        {
            HotelSimulation sim = New(); Open(sim); At(sim, 1, "desk"); Ok(sim, 1, "checkin", number: 103);
            Pick(sim, 1, "bag"); At(sim, 1, "bag_101"); No(sim, 1, "interact", "bag_101");
            Flags(sim, HotelTutorialSkill.CheckIn);
            At(sim, 1, "bag_103"); Ok(sim, 1, "interact", "bag_103");
            HotelTutorialSkill current = HotelTutorialSkill.CheckIn | HotelTutorialSkill.DeliverBag;
            Flags(sim, current);
            At(sim, 1, "towels"); Ok(sim, 1, "interact", "towels");
            At(sim, 1, "towel_103"); Ok(sim, 1, "interact", "towel_103"); Flags(sim, current);
            Advance(sim, 50);
            Assert(sim.State.guests[0].towelRequested, "Real extra-towel request never arrived");
            At(sim, 1, "towels"); Ok(sim, 1, "interact", "towels");
            At(sim, 1, "towel_101"); No(sim, 1, "interact", "towel_101"); Flags(sim, current);
            At(sim, 1, "towel_103"); Ok(sim, 1, "interact", "towel_103");
            Flags(sim, current | HotelTutorialSkill.ExtraTowel);
            Assert(sim.State.cash == 400 && sim.State.towelStock == 10, "Onboarding minted money or extra towels");
        }

        private static void WorkSkills()
        {
            HotelSimulation sim = New();
            At(sim, 1, "bed_102"); Ok(sim, 1, "beginwork", "bed_102");
            sim.Tick(.3f); Assert(Hint(sim, 1).completed == 0, "Partial work counted");
            Ok(sim, 1, "cancelwork"); sim.Tick(3); Flags(sim, HotelTutorialSkill.None);
            Work(sim, 1, "bed_102"); Flags(sim, HotelTutorialSkill.None);
            At(sim, 1, "hamper"); Ok(sim, 1, "interact", "hamper");
            At(sim, 1, "linen"); Ok(sim, 1, "interact", "linen");
            Work(sim, 1, "bed_102"); Flags(sim, HotelTutorialSkill.CleanBed);
            Room(sim, 104).leak = true; Room(sim, 104).water = .5f;
            At(sim, 1, "sink_104"); No(sim, 1, "beginwork", "sink_104");
            Pick(sim, 1, "toolbox"); At(sim, 1, "sink_104"); Ok(sim, 1, "beginwork", "sink_104");
            sim.Tick(6); Flags(sim, HotelTutorialSkill.CleanBed);
            Work(sim, 1, "sink_104"); Flags(sim, HotelTutorialSkill.CleanBed | HotelTutorialSkill.RepairLeak);
            Assert(Room(sim, 104).water == .5f, "Onboarding repair changed water behavior");
            Drop(sim, 1); Pick(sim, 1, "mop"); Work(sim, 1, "water_104");
            Flags(sim, HotelTutorialSkill.CleanBed | HotelTutorialSkill.RepairLeak | HotelTutorialSkill.MopWater);
            Assert(sim.State.cash == 400 && sim.State.expenses == 0 && sim.State.linenStock == 11, "Tutorial changed work economy");
        }

        private static void DaySkills()
        {
            HotelSimulation sim = New(); Open(sim); Ok(sim, 0, "checkin", number: 103);
            No(sim, 0, "checkout", number: sim.State.guests[0].id); No(sim, 0, "nextday");
            Flags(sim, HotelTutorialSkill.CheckIn);
            Advance(sim, 205);
            GuestState guest = sim.State.guests[0];
            Assert(guest.stage == "checkout" && !guest.paid, "Test missed checkout window");
            At(sim, 1, "desk"); int beforePayment = sim.State.cash;
            int expectedPayment = 80 + Mathf.RoundToInt(Mathf.Clamp(guest.satisfaction - 12 - (guest.memories.Contains("Не дождался дополнительного полотенца") ? 0 : 14), 0, 100) * .5f);
            Ok(sim, 1, "checkout", number: guest.id);
            Flags(sim, HotelTutorialSkill.CheckIn | HotelTutorialSkill.CheckOut);
            Assert(sim.State.cash == beforePayment + expectedPayment && sim.State.served == 1, "Checkout amount changed by onboarding");
            No(sim, 1, "checkout", number: guest.id);
            At(sim, 0, "desk"); Ok(sim, 0, "finish");
            No(sim, 1, "nextday"); Ok(sim, 0, "nextday");
            Flags(sim, HotelTutorialSkill.CheckIn | HotelTutorialSkill.CheckOut | HotelTutorialSkill.NextDay);
            Assert(HotelOnboarding.GetHint(sim.State, 1) == null, "Next-day hint still visible");

            sim = New(); Open(sim); Ok(sim, 0, "checkin", number: 101); Advance(sim, 310);
            Assert(sim.State.guests[0].paid, "Expected unattended checkout");
            Flags(sim, HotelTutorialSkill.CheckIn);
            At(sim, 0, "desk"); Ok(sim, 0, "finish"); Flags(sim, HotelTutorialSkill.CheckIn);
        }

        private static void SkipResume()
        {
            HotelSimulation sim = New(); At(sim, 0, HotelLayout.RoomCenter(104));
            string before = JsonUtility.ToJson(sim.State);
            No(sim, 1, "skipTutorial"); Ok(sim, 0, "skipTutorial"); Ok(sim, 0, "skipTutorial");
            Assert(HotelOnboarding.GetHint(sim.State, 0) == null && HotelOnboarding.GetHint(sim.State, 1) == null, "Skip not shared");
            No(sim, 1, "resumeTutorial"); Ok(sim, 0, "resumeTutorial"); Ok(sim, 0, "resumeTutorial");
            Assert(before == JsonUtility.ToJson(sim.State), "Skip/resume changed gameplay state");
            Ok(sim, 0, "skipTutorial");
            At(sim, 1, "linen"); Ok(sim, 1, "interact", "linen"); Work(sim, 1, "bed_104");
            Flags(sim, HotelTutorialSkill.CleanBed);
            Ok(sim, 0, "resumeTutorial"); Flags(sim, HotelTutorialSkill.CleanBed);
            Assert(Hint(sim).completed == 1, "Resume reset earned skills");
        }

        private static void HeldGuidance()
        {
            HotelSimulation sim = New();
            At(sim, 1, "linen"); Ok(sim, 1, "interact", "linen");
            Assert(Hint(sim, 1).targetId == "bed_104", "Held linen did not override preparation hint");
            Work(sim, 1, "bed_104"); Work(sim, 1, "bed_102");
            Assert(Hint(sim, 1).targetId == "hamper", "Dirty linen direction");
            At(sim, 1, "hamper"); Ok(sim, 1, "interact", "hamper"); Work(sim, 1, "trash_102");
            Assert(Hint(sim, 1).targetId == "bin", "Trash direction");
            At(sim, 1, "bin"); Ok(sim, 1, "interact", "bin");
            Room(sim, 104).leak = true; Room(sim, 104).water = .4f;
            Pick(sim, 1, "toolbox"); Assert(Hint(sim, 1).targetId == "sink_104", "Toolbox target hardcoded");
            Work(sim, 1, "sink_104"); Assert(Hint(sim, 1).targetId == "" && Hint(sim, 1).body.Contains("Q"), "No-work tool hint points to repaired sink");
            Drop(sim, 1); Pick(sim, 1, "mop"); Assert(Hint(sim, 1).targetId == "water_104", "Mop lost remaining water");
            Work(sim, 1, "water_104"); Drop(sim, 1);
            At(sim, 1, "towels"); Ok(sim, 1, "interact", "towels"); Assert(Hint(sim, 1).targetId == "towel_103", "Towel target");
            At(sim, 1, "towel_103"); Ok(sim, 1, "interact", "towel_103");
            Open(sim); Pick(sim, 1, "bag"); Assert(Hint(sim, 1).targetId == "desk", "Unassigned bag invents room");
            Ok(sim, 0, "checkin", number: 104); Assert(Hint(sim, 1).targetId == "bag_104", "Bag guidance not based on owner assignment");
            At(sim, 1, "bag_104"); Ok(sim, 1, "interact", "bag_104");

            // Another worker's held linen must not be offered as a pickup to the local player.
            At(sim, 0, "linen"); Ok(sim, 0, "interact", "linen");
            sim.State.linenStock = 0;
            HotelHint forOther = Hint(sim, 1);
            Assert(forOther.targetId != Player(sim).held, "Hint offers a teammate's held item");
        }

        private static void Retargeting()
        {
            HotelSimulation sim = New(); Open(sim); Ok(sim, 0, "checkin", number: 103);
            int firstId = sim.State.guests[0].id;
            Assert(Hint(sim).targetId == "luggage_" + firstId, "First bag hint missing");
            Advance(sim, 50);
            Assert(Hint(sim).targetId == "towels" && Hint(sim).body.Contains("103"), "Real request not prioritized");
            Advance(sim, 275);
            Assert(sim.State.guests[0].stage == "gone", "Guest not gone");
            GuestState queue = sim.State.guests.Find(g => g.stage == "queue");
            Assert(queue != null && queue.id != firstId, "No later queue to retarget");
            HotelHint changed = Hint(sim);
            Assert(changed.targetId == "desk" && changed.body.Contains(queue.name), "Hint stuck on departed guest");
            Ok(sim, 0, "checkin", number: 101);
            Assert(Hint(sim).targetId == "luggage_" + queue.id, "Replacement guest's luggage not selected");
            Pick(sim, 1, "toolbox"); Room(sim, 104).leak = true; Room(sim, 102).leak = true;
            At(sim, 0, "board");
            // Read-only projection must preserve another employee's active work lease as well.
            At(sim, 1, "sink_104"); Ok(sim, 1, "beginwork", "sink_104"); sim.Tick(.2f);
            float progress = Player(sim, 1).workProgress;
            Assert(Hint(sim, 1).targetId == "sink_104" && Player(sim, 1).workProgress == progress, "Hint reset/switched active work");
        }

        private static void Readiness()
        {
            HotelSimulation sim = New(); At(sim, 0, "desk");
            CheckReason(sim, 101, "Заселение доступно в открытую смену.");
            Open(sim);
            CheckReason(sim, 105, "Такого номера нет.");
            RoomState room = Room(sim, 104);
            room.outOfService = true; CheckReason(sim, 104, "Номер закрыт для продаж.");
            room.outOfService = false; CheckReason(sim, 104, "Сначала застелите чистую кровать.");
            room.bed = 2; room.leak = true; CheckReason(sim, 104, "Сначала устраните аварийное состояние номера.");
            room.leak = false; room.water = .651f; CheckReason(sim, 104, "Сначала устраните аварийное состояние номера.");
            room.water = .65f;
            string before = JsonUtility.ToJson(sim.State);
            Assert(HotelSimulation.CheckInBlockReason(sim.State, 104) == "" && before == JsonUtility.ToJson(sim.State), "Boundary readiness or side effect");
            Ok(sim, 0, "checkin", number: 104); CheckReason(sim, 104, "Номер уже занят.");
            CheckReason(sim, 101, "В очереди нет гостей.");
            sim.State.phase = "closing";
            Assert(Hint(sim).targetId == "desk" && Hint(sim).body.Contains("итог"), "Closing lacks next phase");
            Ok(sim, 0, "finish");
            Assert(Hint(sim).targetId == "desk" && Hint(sim).body.Contains("следующему дню"), "Summary lacks next day");
            Assert(Hint(sim, 1).body.Contains("Хозяин"), "Client offered host-only phase action");

            sim = New(); foreach (RoomState r in sim.State.rooms) r.bed = 0;
            Assert(Hint(sim).targetId == "linen", "Preparation suggests opening when no room can accept a guest");
        }

        private static void CheckReason(HotelSimulation sim, int number, string expected)
        {
            string before = JsonUtility.ToJson(sim.State);
            Assert(HotelSimulation.CheckInBlockReason(sim.State, number) == expected, "Wrong UI block reason");
            Assert(before == JsonUtility.ToJson(sim.State), "Readiness mutated state");
            Assert(sim.Execute(0, new HotelCommand("checkin", number: number)) == expected, "UI and command checks diverged");
            Assert(before == JsonUtility.ToJson(sim.State), "Rejected registration mutated progress");
        }

        [Serializable] private class LegacyEnvelope
        {
            public string format = "WorstHotelSave";
            public int version = 1;
            public string checksum, payload;
        }
        private static void Persistence()
        {
            string directory = Path.Combine(Path.GetTempPath(), "WorstHotelOnboarding-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "hotel.json");
                HotelSimulation sim = New();
                string oldJson = Regex.Replace(JsonUtility.ToJson(sim.State), "\\\"tutorialFlags\\\":0,|\\\"tutorialSkipped\\\":false,", "");
                Assert(!oldJson.Contains("tutorialFlags") && !oldJson.Contains("tutorialSkipped"), "Legacy fixture still has new fields");
                var legacy = new LegacyEnvelope { payload = oldJson };
                using (SHA256 hash = SHA256.Create()) legacy.checksum = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(oldJson))).Replace("-", "").ToLowerInvariant();
                File.WriteAllText(path, JsonUtility.ToJson(legacy));
                HotelState old = HotelSaveStore.Load(path);
                Assert(old.version == 1 && old.tutorialFlags == 0 && !old.tutorialSkipped && old.cash == 400, "Legacy v1 save did not default new fields");

                sim = new HotelSimulation(old); sim.Join(0); sim.Join(1);
                At(sim, 1, "linen"); Ok(sim, 1, "interact", "linen"); Work(sim, 1, "bed_104");
                Open(sim); No(sim, 1, "skipTutorial"); Ok(sim, 0, "skipTutorial");
                At(sim, 1, "desk"); Ok(sim, 1, "checkin", number: 104);
                HotelTutorialSkill learned = HotelTutorialSkill.CleanBed | HotelTutorialSkill.CheckIn;
                Flags(sim, learned);
                HotelSaveStore.Save(sim.State, path);
                HotelState loaded = HotelSaveStore.Load(path);
                Assert(loaded.version == 1 && loaded.tutorialSkipped && loaded.tutorialFlags == (int)learned, "Progress/skip not preserved by existing SaveStore");
                Assert(loaded.players.Count == 0 && loaded.cash == sim.State.cash && loaded.linenStock == 11, "Persistence changed gameplay");
                sim = new HotelSimulation(loaded); sim.Join(0); sim.Join(1);
                Assert(HotelOnboarding.GetHint(sim.State, 0) == null, "Load re-enabled skipped help");
                Ok(sim, 0, "resumeTutorial");
                Assert(Hint(sim, 1).completed == 2, "Loaded skills not shown to second player");
                HotelSaveStore.Save(sim.State, path);
                loaded = HotelSaveStore.Load(path);
                Assert(!loaded.tutorialSkipped && loaded.tutorialFlags == (int)learned, "Resume/progress not persisted");
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
