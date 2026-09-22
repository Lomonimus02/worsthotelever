using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    // Native JsonUtility + actual SaveStore. Invoked only by the parent's Unity runner.
    public static class HotelDangerPersistenceTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "v3 native roundtrip, explicit schema and pure validation/RNG", Roundtrip);
            Run(passed, "v1/v2 normalization preserves classic schema; v3 requires danger", Compatibility);
            Run(passed, "v1/v2 promotion archives original primary and backup before v3 replace", MigrationArchives);
            Run(passed, "v3 archive failure, locks, world identity and downgrade preserve old files", FileProtection);
            Run(passed, "v3 corruption recovery and future envelope/content/nested schema fail closed", CorruptionAndFuture);
            Run(passed, "Durable injuries/death/critical clocks survive disposable players and reconnect IDs", DurableCrew);
            Run(passed, "v3 failure/win settlement guards and saved forecasts survive resume without reroll", OutcomesAndRng);
            Run(passed, "v3 rejects malformed crew, incidents, counters and timers", InvalidDanger);
            Run(passed, "v3 canonical danger pings and exclusive work/tool/recovery leases", TargetsAndLeases);
            Run(passed, "v3 factual hazard complaint references retain earlier resolved serials", HazardComplaints);
            Run(passed, "v3 retains the exact shared UTF-16 snapshot budget", WireBoundary);
            return passed;
        }

        private static void Roundtrip()
        {
            WithSlot(path =>
            {
                HotelState state = HotelSimulation.CreateNewDanger(8171).State;
                Need(state.version == 3 && state.contentVersion == 3 && state.mvp.schema == 2 && state.danger.schema == 1, "Factory schema mismatch.");
                string before = JsonUtility.ToJson(state);
                for (int i = 0; i < 3; i++) { HotelSaveStore.Validate(state); HotelMvpValidation.Validate(state); HotelDangerValidation.Validate(state); }
                Need(JsonUtility.ToJson(state) == before, "Validation mutated state or RNG.");
                HotelSaveStore.Save(state, path);
                Envelope envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path));
                Need(envelope.version == 3 && envelope.payload == before, "Envelope/payload version mismatch.");
                Need(JsonUtility.ToJson(HotelSaveStore.Load(path)) == before, "Unheld v3 native roundtrip changed state.");
                Need(HotelSnapshotCodec.Decode(HotelSnapshotCodec.Encode(before)) == before, "v3 wire roundtrip changed state.");
            });
        }

        private static void Compatibility()
        {
            WithSlot(path =>
            {
                foreach (HotelState classic in new[] { new HotelSimulation().State, HotelSimulation.CreateNewMvp(123).State })
                {
                    // Simulates a default inline object created by Unity while reading an old DTO.
                    classic.danger = new HotelDangerState { schema = 0, safety = 0 };
                    classic.danger.crew.Add(new HotelCrewState { slot = 0, joined = true, life = "dead", health = 0 });
                    WritePayload(path, JsonUtility.ToJson(classic), classic.version);
                    HotelState loaded = HotelSaveStore.Load(path);
                    Need(loaded.version == classic.version && loaded.danger == null, "Old save unexpectedly enabled danger.");
                    Need(new HotelSimulation(loaded).State.danger == null, "Classic constructor initialized danger.");
                }
                HotelState state = Fresh(); state.danger = null;
                Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
                WritePayload(path, JsonUtility.ToJson(state), 3);
                Throws<InvalidDataException>(() => HotelSaveStore.Load(path));
                state = Fresh(); state.danger.schema = 0; Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
                state = Fresh(); state.contentVersion = 2; Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
                state = Fresh(); WritePayload(path, JsonUtility.ToJson(state), 2); Throws<InvalidDataException>(() => HotelSaveStore.Load(path));
            });
        }

        private static void MigrationArchives()
        {
            foreach (int version in new[] { 1, 2 })
                WithSlot(path =>
                {
                    HotelState old = version == 1 ? new HotelSimulation().State : HotelSimulation.CreateNewMvp(31).State;
                    HotelSaveStore.Save(old, path); old.cash = 333; HotelSaveStore.Save(old, path);
                    string primary = File.ReadAllText(path), backup = File.ReadAllText(path + ".bak"), world = old.worldId;
                    HotelState upgraded = HotelSimulation.ResumeDangerForPlay(HotelSaveStore.Load(path)).State;
                    Need(upgraded.version == 3 && upgraded.worldId == world && upgraded.cash == 333, "Safe-phase promotion lost identity/economy.");
                    HotelSaveStore.Save(upgraded, path);
                    string[] archives = Directory.GetDirectories(Path.Combine(Path.GetDirectoryName(path), "archive"));
                    Need(archives.Length == 1, "Migration did not create one unique archive.");
                    Need(File.ReadAllText(Path.Combine(archives[0], "primary.json")) == primary &&
                        File.ReadAllText(Path.Combine(archives[0], "backup.json")) == backup, "Migration did not preserve both original byte streams.");
                    Need(HotelSaveStore.Load(path).version == 3, "Promoted slot is unreadable.");
                });
            WithSlot(path =>
            {
                HotelState old = HotelSimulation.CreateNewMvp(51).State;
                HotelSaveStore.Save(old, path); HotelSaveStore.Save(old, path);
                File.WriteAllText(path, "{broken-original-primary");
                string primary = File.ReadAllText(path), backup = File.ReadAllText(path + ".bak");
                HotelState upgraded = HotelSimulation.ResumeDangerForPlay(HotelSaveStore.Load(path)).State;
                HotelSaveStore.Save(upgraded, path);
                string archive = Directory.GetDirectories(Path.Combine(Path.GetDirectoryName(path), "archive"))[0];
                Need(File.ReadAllText(Path.Combine(archive, "primary.json")) == primary &&
                    File.ReadAllText(Path.Combine(archive, "backup.json")) == backup && File.ReadAllText(path + ".bak") == backup,
                    "Recovered migration erased corrupt primary or its good backup.");
            });
        }

        private static void FileProtection()
        {
            WithSlot(path =>
            {
                HotelState old = HotelSimulation.CreateNewMvp(91).State;
                HotelSaveStore.Save(old, path); HotelSaveStore.Save(old, path);
                string primary = File.ReadAllText(path), backup = File.ReadAllText(path + ".bak");
                HotelState upgraded = HotelSimulation.ResumeDangerForPlay(HotelSaveStore.Load(path)).State;
                string archivePath = Path.Combine(Path.GetDirectoryName(path), "archive");
                File.WriteAllText(archivePath, "archive creation must fail");
                Throws<IOException>(() => HotelSaveStore.Save(upgraded, path));
                Need(File.ReadAllText(path) == primary && File.ReadAllText(path + ".bak") == backup, "Archive failure touched the original slot.");
            });
            WithSlot(path =>
            {
                HotelState state = Fresh(); HotelSaveStore.Save(state, path); HotelSaveStore.Save(state, path);
                string primary = File.ReadAllText(path), backup = File.ReadAllText(path + ".bak");
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Throws<IOException>(() => HotelSaveStore.Save(state, path));
                Need(File.ReadAllText(path) == primary && File.ReadAllText(path + ".bak") == backup, "Locked write changed checkpoint.");
                HotelState other = Fresh(); other.worldId = Guid.NewGuid().ToString("N");
                Throws<InvalidOperationException>(() => HotelSaveStore.Save(other, path));
                HotelState older = HotelSimulation.CreateNewMvp(91).State; older.worldId = state.worldId;
                Throws<InvalidOperationException>(() => HotelSaveStore.Save(older, path));
                Need(File.ReadAllText(path) == primary && File.ReadAllText(path + ".bak") == backup, "Identity/downgrade protection changed files.");
            });
        }

        private static void CorruptionAndFuture()
        {
            WithSlot(path =>
            {
                HotelState state = Fresh(); HotelSaveStore.Save(state, path); state.cash = 333; HotelSaveStore.Save(state, path);
                string backup = File.ReadAllText(path + ".bak"); File.WriteAllText(path, "{broken");
                HotelState restored = HotelSaveStore.Load(path);
                Need(restored.version == 3 && restored.cash == 400 && restored.danger.schema == 1, "v3 backup recovery failed.");
                HotelSaveStore.Save(restored, path);
                Need(File.ReadAllText(path + ".bak") == backup, "v3 repair overwrote the good backup.");
                foreach (Action<HotelState> change in new Action<HotelState>[] {
                    s => s.version = 4, s => s.contentVersion = 4, s => s.danger.schema = 2, s => s.mvp.schema = 3, s => s.mvp.rngVersion = 2 })
                {
                    HotelState future = Fresh(); change(future); future.rooms = null;
                    Throws<NotSupportedException>(() => HotelSaveStore.Validate(future));
                    WritePayload(path, JsonUtility.ToJson(future), 3, false);
                    string unsupported = File.ReadAllText(path);
                    Throws<NotSupportedException>(() => HotelSaveStore.Load(path));
                    Throws<NotSupportedException>(() => HotelSaveStore.Save(restored, path));
                    Need(File.ReadAllText(path) == unsupported && File.ReadAllText(path + ".bak") == backup, "Future schema fell back or overwrote files.");
                }
                WritePayload(path, "{\"version\":3,\"contentVersion\":3,\"danger\":{\"schema\":2},\"rooms\":{\"futureShape\":true}}", 3, false);
                Throws<NotSupportedException>(() => HotelSaveStore.Load(path));
                WritePayload(path, JsonUtility.ToJson(restored), 4, false);
                Throws<NotSupportedException>(() => HotelSaveStore.Load(path));
                WritePayload(path, JsonUtility.ToJson(restored), 3);
                File.WriteAllText(path + ".bak", "{\"version\":4}");
                string goodPrimary = File.ReadAllText(path);
                Throws<NotSupportedException>(() => HotelSaveStore.Save(restored, path));
                Need(File.ReadAllText(path) == goodPrimary, "Future backup allowed a destructive save.");
                File.WriteAllText(path, "{}"); Throws<NotSupportedException>(() => HotelSaveStore.Load(path));
            });
        }

        private static void DurableCrew()
        {
            WithSlot(path =>
            {
                HotelState state = Active(); HotelCrewState host = Crew(state, 0), partner = Crew(state, 1);
                host.joined = partner.joined = true;
                host.health = 37; host.position = new Vector3(-.5f, .1f, -4.8f);
                partner.life = "downed"; partner.health = 0; partner.bleedout = 17; partner.downs = 1;
                partner.position = new Vector3(.5f, .1f, -4.8f);
                state.danger.safety = 0; state.danger.criticalRemaining = 12; state.danger.medkits = 2; state.danger.selfRescues = 0;
                var player = new PlayerState { id = 0, position = host.position };
                state.players.Add(player); Hold(state, player, "toolbox");
                state.players.Add(new PlayerState { id = 73, position = partner.position });
                string durable = JsonUtility.ToJson(state.danger), before = JsonUtility.ToJson(state);
                HotelSaveStore.Save(state, path); HotelState loaded = HotelSaveStore.Load(path);
                Need(before == JsonUtility.ToJson(state), "Save released or healed the live host.");
                Need(loaded.players.Count == 0 && loaded.items.TrueForAll(i => i.holder == -1) && JsonUtility.ToJson(loaded.danger) == durable,
                    "Load discarded crew health/position or completed medical work.");
                HotelSimulation resumed = HotelSimulation.ResumeDangerForPlay(loaded); resumed.Join(0); resumed.Join(9001);
                Need(Crew(resumed.State, 0).health == 37 && Crew(resumed.State, 1).life == "downed" && Crew(resumed.State, 1).bleedout == 17,
                    "Reconnect with a new connection ID healed or replaced durable crew.");
                Need(resumed.State.players.Find(p => p.id == 9001).position == partner.position, "Reconnect lost the downed body position.");
                resumed.Leave(9001); resumed.Join(9002);
                Need(JsonUtility.ToJson(resumed.State.danger) == durable, "Leave/rejoin changed medical/critical/medical-supply state.");
                HotelSaveStore.Validate(resumed.State);
                resumed.Leave(0); resumed.Leave(9002);
                partner = Crew(resumed.State, 1); partner.life = "dead"; partner.bleedout = 0;
                HotelSaveStore.Save(resumed.State, path);
                resumed = HotelSimulation.ResumeDangerForPlay(HotelSaveStore.Load(path)); resumed.Join(456);
                Need(Crew(resumed.State, 1).life == "dead" && Crew(resumed.State, 1).health == 0, "Dead partner revived after save/rebind.");
                HotelSaveStore.Validate(resumed.State);
            });
        }

        private static void OutcomesAndRng()
        {
            WithSlot(path =>
            {
                foreach (bool won in new[] { false, true })
                {
                    HotelState state = Active(); state.phase = "summary";
                    HotelDangerState d = state.danger; HotelCrewState c = Crew(state, 0); c.joined = true;
                    d.settled = true; d.reason = won ? "Контракт выполнен" : "Команда потеряна";
                    if (won)
                    {
                        d.status = "won"; d.outcome = "completed"; d.wins = d.streak = d.bestStreak = 1;
                        d.elapsed = d.minimumSeconds; state.mvp.elapsed = d.elapsed; state.time = d.elapsed;
                        d.servicePoints = d.requiredService; d.resolved = d.requiredIncidents;
                        for (int i = 0; i < d.requiredIncidents; i++) d.incidents[i].status = "resolved";
                        d.paidReward = d.reward; state.cash += d.reward; state.earned += d.reward;
                    }
                    else
                    {
                        d.status = "failed"; d.outcome = "wipe"; d.losses = 1; d.chargedPenalty = d.penalty;
                        state.cash -= d.penalty; state.expenses += d.penalty;
                        c.life = "dead"; c.health = 0; c.downs = 1;
                    }
                    // Separate world per branch, without using StartNew or replacing another world.
                    string branch = path + (won ? ".won" : ".failed");
                    HotelSaveStore.Save(state, branch);
                    string before = JsonUtility.ToJson(state);
                    for (int i = 0; i < 3; i++)
                    {
                        HotelSimulation resumed = HotelSimulation.ResumeDangerForPlay(HotelSaveStore.Load(branch));
                        Need(JsonUtility.ToJson(resumed.State) == before, "Resume changed terminal reward/penalty/forecast or RNG.");
                        HotelSaveStore.Save(resumed.State, branch);
                    }
                }
            });
        }

        private static void InvalidDanger()
        {
            foreach (Action<HotelState> corrupt in new Action<HotelState>[] {
                s => s.danger = null, s => s.danger.schema = 0, s => s.danger.crew = null,
                s => s.danger.crew.RemoveAt(1), s => s.danger.crew[1].slot = 0,
                s => s.danger.crew[0].health = float.NaN, s => s.danger.crew[0].health = 101,
                s => s.danger.crew[0].health = 0, s => s.danger.crew[0].bleedout = 1,
                s => s.danger.crew[0].life = "revived", s => s.danger.crew[0].position = new Vector3(500, 0, 500),
                s => s.danger.safety = float.PositiveInfinity, s => s.danger.criticalRemaining = -1,
                s => s.danger.medkits = 4, s => s.danger.selfRescues = 2, s => s.danger.serial = 0,
                s => s.danger.day++, s => s.danger.streak = 1, s => s.danger.servicePoints = -1,
                s => s.danger.mode = "cheat", s => s.danger.reward++, s => s.danger.status = "completed",
                s => s.danger.incidents = null, s => s.danger.incidents.RemoveAt(2), s => s.danger.incidents[0].room = 105,
                s => s.danger.incidents[1].room = s.danger.incidents[0].room,
                s => s.danger.incidents[1].kind = s.danger.incidents[0].kind,
                s => s.danger.incidents[0].kind = "water", s => s.danger.incidents[0].triggerAt = float.NaN,
                s => s.danger.incidents[0].warningRemaining = 11, s => s.danger.incidents[0].isolated = true,
                s => s.danger.resolved = 1, s => s.danger.paidReward = 1, s => s.danger.settled = true,
                s => s.danger.reason = new string('x', 513) })
            {
                HotelState state = Fresh(); corrupt(state); string before = JsonUtility.ToJson(state);
                Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
                Need(JsonUtility.ToJson(state) == before, "Rejected danger data was silently repaired.");
            }
            HotelState duplicate = Active(); Crew(duplicate, 1).joined = true;
            duplicate.players.Add(new PlayerState { id = 71, position = Crew(duplicate, 1).position });
            duplicate.players.Add(new PlayerState { id = 72, position = Crew(duplicate, 1).position });
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(duplicate));
        }

        private static void TargetsAndLeases()
        {
            HotelState state = Active(); string hazard = "hazard_" + state.danger.incidents[0].room;
            Crew(state, 0).joined = Crew(state, 1).joined = true;
            foreach (string target in new[] { hazard, "isolate_" + state.danger.incidents[0].room, "rescue_1", "recover_0", "firstaid", "alarm" })
            {
                state.mvp.pings.Clear(); state.mvp.pings.Add(new MvpPingState { playerId = 0, target = target, until = state.mvp.elapsed + 6 });
                HotelSaveStore.Validate(state);
            }
            foreach (string target in new[] { "hazard_0101", "isolate_105", "rescue_2", "recover_01", "firstaid_fake", "hazard_999" })
            {
                state.mvp.pings[0].target = target;
                Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            }
            HotelState classic = HotelSimulation.CreateNewMvp(13).State;
            classic.mvp.pings.Add(new MvpPingState { playerId = 0, target = "firstaid", until = 6 });
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(classic));
            WithSlot(path =>
            {
                state = Active(); HotelIncidentState incident = state.danger.incidents[0];
                incident.status = "active"; incident.activeSeconds = 1;
                PlayerState p = Worker(state, 0, "isolate_" + incident.room, HotelDangerRules.Control(incident.room));
                HotelSaveStore.Validate(state);
                incident.isolated = true; p.workTarget = "hazard_" + incident.room;
                p.position = Floor(HotelDangerRules.Source(incident)); Crew(state, 0).position = p.position;
                Hold(state, p, incident.kind == "fumes" ? "mop" : "toolbox");
                HotelSaveStore.Validate(state);
                string durable = JsonUtility.ToJson(state.danger); HotelSaveStore.Save(state, path);
                HotelState loaded = HotelSaveStore.Load(path);
                Need(loaded.players.Count == 0 && JsonUtility.ToJson(loaded.danger) == durable, "Loading completed a danger repair lease.");
                ItemState held = state.items.Find(i => i.id == p.held); held.holder = -1; p.held = "";
                Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            });
            state = Active(); Down(state, 1);
            Worker(state, 0, "rescue_1", Crew(state, 1).position); HotelSaveStore.Validate(state);
            Worker(state, 8, "recover_1", Crew(state, 1).position);
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            state.players.RemoveAt(0); HotelSaveStore.Validate(state);
            state.danger.selfRescues = 0;
            HotelSaveStore.Validate(state); // Another actor can spend the shared resource after this lease's tick.
            Need(HotelDangerRules.WorkError(state, 8, "recover_1") != "", "Exhausted self-help became executable.");
        }

        private static void HazardComplaints()
        {
            HotelState state = Active(); state.day = state.danger.day = 2; state.danger.serial = 2;
            int id = state.nextGuest++;
            var guest = new GuestState { id = id, name = "Cause fixture", kind = "tourist", trait = "patient", position = HotelLayout.Spawn,
                mvp = new MvpGuestState { partyId = "cause_party", arrivalDay = 2, departureDay = 3 } };
            state.guests.Add(guest);
            state.items.Add(new ItemState { id = "luggage_" + id, kind = "bag", ownerGuest = id, position = HotelLayout.Spawn });
            HotelIncidentState incident = state.danger.incidents[0]; incident.status = "active"; incident.activeSeconds = 1;
            var complaint = new MvpComplaintState { id = "cause_test", guestId = id,
                category = incident.kind == "electric" ? "tv" : incident.kind == "steam" ? "toilet" : "dirt",
                causeKey = "danger:" + incident.kind + ":" + incident.room + ":2" };
            state.mvp.complaints.Add(complaint); HotelSaveStore.Validate(state);
            string current = complaint.causeKey;
            complaint.causeKey = current.Substring(0, current.Length - 1) + "1";
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            complaint.status = "resolved"; HotelSaveStore.Validate(state);
            complaint.causeKey = current.Substring(0, current.Length - 1) + "3";
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            complaint.causeKey = current; complaint.status = "active"; incident.status = "resolved"; state.danger.resolved = 1;
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            complaint.status = "resolved"; HotelSaveStore.Validate(state);
            state.version = state.contentVersion = 2;
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
        }

        private static void WireBoundary()
        {
            HotelState state = Fresh(); state.ledger.Clear(); state.reviews.Clear();
            for (int i = 0; i < 256; i++) state.ledger.Add(new string('漢', 512));
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            while (Bytes(state) > HotelMvpValidation.MaxSnapshotBytes) state.ledger.RemoveAt(state.ledger.Count - 1);
            int spare = (HotelMvpValidation.MaxSnapshotBytes - Bytes(state)) / 2;
            state.notice += new string('漢', spare);
            Need(state.notice.Length <= 1024 && Bytes(state) == HotelMvpValidation.MaxSnapshotBytes, "Exact v3 byte fixture failed.");
            HotelSaveStore.Validate(state);
            string before = JsonUtility.ToJson(state);
            Need(HotelSnapshotCodec.Decode(HotelSnapshotCodec.Encode(before)) == before, "Bounded CJK v3 wire roundtrip failed.");
            state.notice += "漢"; Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
        }

        private static HotelState Fresh() { return HotelSimulation.CreateNewDanger(173).State; }
        private static HotelState Active()
        {
            HotelState state = Fresh(); state.phase = "open"; state.guidedOpening = false;
            state.danger.status = "active"; state.danger.elapsed = 120; state.mvp.elapsed = 120; state.time = 120;
            return state;
        }
        private static HotelCrewState Crew(HotelState state, int slot) { return state.danger.crew.Find(c => c.slot == slot); }
        private static void Down(HotelState state, int slot)
        {
            HotelCrewState crew = Crew(state, slot); crew.joined = true; crew.life = "downed"; crew.health = 0; crew.bleedout = 30; crew.downs = 1;
        }
        private static Vector3 Floor(Vector3 position) { position.y = .1f; return position; }
        private static PlayerState Worker(HotelState state, ulong id, string target, Vector3 position)
        {
            HotelCrewState crew = Crew(state, id == 0 ? 0 : 1); crew.joined = true; crew.position = Floor(position);
            var p = new PlayerState { id = id, position = crew.position, workTarget = target, workProgress = .25f, workLastSeen = .1f };
            state.players.Add(p); return p;
        }
        private static void Hold(HotelState state, PlayerState p, string kind)
        {
            ItemState item = state.items.Find(i => i.kind == kind); item.holder = (long)p.id; item.position = p.position + Vector3.up * .8f;
            p.held = item.id;
        }
        private static int Bytes(HotelState state) { return Encoding.Unicode.GetByteCount(JsonUtility.ToJson(state)) + 16; }
        [Serializable] private sealed class Envelope { public string format = "WorstHotelSave", payload, checksum; public int version; }
        private static void WritePayload(string path, string payload, int version, bool checksum = true)
        {
            string hash;
            using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(path, JsonUtility.ToJson(new Envelope { version = version, payload = payload, checksum = checksum ? hash : "bad" }));
        }
        private static void WithSlot(Action<string> test)
        {
            string parent = Path.GetFullPath(Path.GetTempPath());
            string name = "WorstHotelDangerPersistence-" + Guid.NewGuid().ToString("N");
            string directory = Path.GetFullPath(Path.Combine(parent, name)); Directory.CreateDirectory(directory);
            try { test(Path.Combine(directory, "checkpoint.json")); }
            finally
            {
                Need(string.Equals(Path.GetDirectoryName(directory).TrimEnd(Path.DirectorySeparatorChar), parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(directory) == name, "Refusing cleanup outside the owned temporary fixture.");
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
        private static void Run(List<string> passed, string name, Action test)
        { try { test(); passed.Add(name); } catch (Exception e) { throw new Exception("HotelDangerPersistenceTests FAILED: " + name, e); } }
        private static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    }
}
