using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorstHotel
{
    // Parent native Unity runner calls RunAll. No NUnit, mocks or default user save path.
    public static class HotelMvpLongRunTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            try
            {
                TenDaysWithCheckpoints(passed);
                DebtRecovery(passed);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("MVP long-run failed. Completed progress:\n" + string.Join("\n", passed.ToArray()), error);
            }
            return passed;
        }

        private static void TenDaysWithCheckpoints(List<string> passed)
        {
            WithSlot(path =>
            {
                HotelSimulation sim = HotelSimulation.CreateNewMvp(24681357);
                var proof = new HotelMvpScenario.Evidence();
                int checkpointsWithBookings = 0;
                sim = SaveLoadResume(sim, path);
                foreach (int count in new[] { 3, 3, 4 })
                {
                    passed.AddRange(HotelMvpScenario.AdvanceDays(sim, count, proof));
                    if (sim.State.mvp.reservations.Exists(r => r.status == "confirmed" && r.arrivalDay > sim.State.day))
                        checkpointsWithBookings++;
                    sim = SaveLoadResume(sim, path);
                }
                passed.Add("Main-run evidence: " +
                    "paid=" + proof.PaidGuests + " income=" + proof.Income + " dirtyCarry=" + proof.DirtyDisposals +
                    " dirtyTowels=" + proof.DirtyTowelDisposals + " equipment=" + proof.EquipmentRepairs + " utility=" + proof.UtilityRepairs +
                    " continuing=" + proof.ContinuingBoundaryCrossings + " upgrades=" + proof.PurchasesMade.Count +
                    " steps=" + proof.Steps + " heartbeats=" + proof.Heartbeats + " peakSnapshot=" + proof.PeakSnapshotBytes +
                    " fulfilled=" + string.Join(",", new List<string>(proof.FulfilledKinds).ToArray()));
                Need(checkpointsWithBookings >= 2, "Fewer than two saved boundaries contained pending future bookings.");
                HotelMvpScenario.AssertTenDayCoverage(sim.State, proof);
                passed.Add("PASS: ten full days through Execute/held work; all semantic coverage gates; 0/3/6/10-day Save/Load/Resume checkpoints.");

                // Two independent model resumes take the same real command-driven next day.
                // This is deterministic model replay, not an OS process restart.
                int draws = sim.State.mvp.rngDraws;
                HotelSimulation left = HotelSimulation.ResumeForPlay(HotelSaveStore.Load(path));
                HotelSimulation right = HotelSimulation.ResumeForPlay(HotelSaveStore.Load(path));
                string durable = File.ReadAllText(path);
                HotelMvpScenario.AdvanceDays(left, 1);
                HotelMvpScenario.AdvanceDays(right, 1);
                Need(left.State.mvp.rngDraws > draws, "Replay never crossed a real RNG decision/demand preparation.");
                Need(JsonUtility.ToJson(left.State) == JsonUtility.ToJson(right.State), "Identical saved RNG/forecast replay diverged.");
                Need(File.ReadAllText(path) == durable, "Model replay wrote into its source checkpoint.");
                passed.Add("PASS: two model resumes reproduce a full additional day, including newly drawn demand/director state, byte-for-byte.");
            });
        }

        private static HotelSimulation SaveLoadResume(HotelSimulation sim, string path)
        {
            Need(sim.State.players.Count == 0 && sim.State.phase == "preparation", "Boundary checkpoint retained test actors or active work.");
            HotelMvpScenario.AssertCheckpoint(sim.State);
            string before = JsonUtility.ToJson(sim.State);
            int rng = sim.State.mvp.rngState, draws = sim.State.mvp.rngDraws, prepared = sim.State.mvp.preparedDay;
            HotelSaveStore.Save(sim.State, path);
            Need(File.Exists(path) && before == JsonUtility.ToJson(sim.State), "Save mutated its host world.");
            HotelState loaded = HotelSaveStore.Load(path);
            Need(JsonUtility.ToJson(loaded) == before, "Save/load changed contracts, bookings, dirt, balance or RNG.");
            HotelSimulation resumed = HotelSimulation.ResumeForPlay(loaded);
            Need(resumed.State.mvp.rngState == rng && resumed.State.mvp.rngDraws == draws && resumed.State.mvp.preparedDay == prepared &&
                JsonUtility.ToJson(resumed.State) == before, "Resume rerolled demand or changed the checkpoint.");
            HotelMvpScenario.AssertCheckpoint(resumed.State);
            return resumed;
        }

        private static void DebtRecovery(List<string> passed)
        {
            WithSlot(path =>
            {
                HotelSimulation sim = HotelSimulation.CreateNewMvp(1357911);
                // The only gameplay fixture mutation in this suite: an explicit initial debt.
                // Income, supplies, occupancy, work completion and subsequent cash use real commands.
                sim.State.cash = -200;
                HotelMvpScenario.AssertCheckpoint(sim.State);
                sim = SaveLoadResume(sim, path);
                sim.Join(0);
                Execute(sim, new HotelCommand("pose") { position = new Vector3(4.6f, .1f, -1.8f) });
                string before = JsonUtility.ToJson(sim.State);
                string error = sim.Execute(0, new HotelCommand("upgrade", "toolbox"));
                Need(error != "" && before == JsonUtility.ToJson(sim.State), "Debt allowed an optional purchase or changed the world on rejection.");
                sim.Leave(0);
                var proof = new HotelMvpScenario.Evidence();
                int initial = sim.State.cash;
                int restockCosts = 0;
                // Recovery is bounded: no endless waiting, injected income, or replenishment edits.
                for (int i = 0; i < 3; i++)
                {
                    passed.AddRange(HotelMvpScenario.AdvanceDays(sim, 1, proof, false));
                    Need(sim.State.expenses >= 30 && sim.State.linenStock > 0 && sim.State.towelStock > 0 && sim.State.mvp.coffeeStock > 0,
                        "Essential next-day supplies failed while recovering from debt.");
                    restockCosts += sim.State.expenses;
                    sim = SaveLoadResume(sim, path);
                }
                Need(proof.CheckIns > 0 && proof.PaidGuests > 0 && proof.Income > -initial + restockCosts,
                    "Debt scenario produced no service-funded recovery.");
                Need(sim.State.cash > 0, "Debt did not recover to a positive saved balance within three full days.");
                passed.Add("PASS: explicit -200 debt rejects optional purchase, continues physical service/restocking and saves a positive recovered balance; cash=" + sim.State.cash);
            });
        }

        private static void Execute(HotelSimulation sim, HotelCommand command)
        {
            string error = sim.Execute(0, command);
            Need(error == "", command.action + " failed: " + error);
        }

        private static void WithSlot(Action<string> body)
        {
            string temp = Path.GetFullPath(Path.GetTempPath());
            string name = "WorstHotel-MvpSoak-" + Guid.NewGuid().ToString("N");
            string directory = Path.GetFullPath(Path.Combine(temp, name));
            Directory.CreateDirectory(directory);
            try { body(Path.Combine(directory, "checkpoint.json")); }
            finally
            {
                // Recursive cleanup is limited to this invocation's exact GUID temp directory.
                Need(string.Equals(Path.GetDirectoryName(directory).TrimEnd(Path.DirectorySeparatorChar), temp.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(directory) == name, "Refusing cleanup outside the owned temporary slot.");
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static void Need(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("MVP long-run tests: " + message); }
    }
}
