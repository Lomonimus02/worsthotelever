using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    // Run by the parent's native Unity batch runner; JsonUtility is intentionally not mocked.
    public static class HotelMvpPersistenceTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "v2 factory, native JSON roundtrip and pure validation", Roundtrip);
            Run(passed, "v2 held items/work leases released on load without mutating host", ReleasedPlayers);
            Run(passed, "v2 corruption recovers backup but does not destroy it", Corruption);
            Run(passed, "Future envelope/state/content/RNG never falls back or overwrites", FutureVersions);
            Run(passed, "v1/v2 envelope dispatch, version mismatch and missing MVP", Dispatch);
            Run(passed, "v1 promotion preserves original primary and backup archive", MigrationArchive);
            Run(passed, "Original v1 JSON without item size/condition promotes safely", LegacyMissingItemFields);
            Run(passed, "v1 recovery promotion archives corrupt primary and valid backup", RecoveredMigrationArchive);
            Run(passed, "Archive failure aborts migration before replacing the slot", ArchiveFailure);
            Run(passed, "World identity, file locks and version downgrade preserve checkpoint", CheckpointProtection);
            Run(passed, "Multi-day family and wrong-room luggage survive v2 reload", ContinuingStay);
            Run(passed, "Four small/two large cart bags obey weighted capacity", CartCapacity);
            Run(passed, "Requests/complaints/reviews/director persist with references", HospitalityRoundtrip);
            Run(passed, "Planned/walk-in groups and pruned terminal history remain valid", GroupsAndHistory);
            Run(passed, "v2 invalid references, enums, NaN and leases are rejected", InvalidReferences);
            Run(passed, "Stay/reservation half-open intervals reject conflicts", ReservationIntervals);
            Run(passed, "Bounded histories and exact UTF-16 snapshot wire budget", Limits);
            return passed;
        }

        private static void Roundtrip()
        {
            WithSlot(path =>
            {
                HotelState state = HotelSimulation.CreateNewMvp(24681357).State;
                string before = JsonUtility.ToJson(state);
                int draws = state.mvp.rngDraws, rng = state.mvp.rngState;
                HotelSaveStore.Validate(state); HotelMvpValidation.Validate(state);
                Need(before == JsonUtility.ToJson(state) && state.mvp.rngDraws == draws && state.mvp.rngState == rng, "Validation mutated a snapshot/RNG.");
                HotelSaveStore.Save(state, path);
                Envelope envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path));
                Need(envelope.version == 2 && envelope.payload == before, "Envelope does not preserve v2 payload.");
                HotelState loaded = HotelSaveStore.Load(path);
                Need(loaded.version == 2 && loaded.contentVersion == 2 && loaded.mvp != null && loaded.rooms.Count == 6, "v2 schema lost in native JSON.");
                Need(JsonUtility.ToJson(loaded) == before, "Roundtrip changed an unheld snapshot.");
                Need(!loaded.rooms.Find(r => r.number == 105).mvp.owned && loaded.rooms.Find(r => r.number == 106).mvp.capacity == 2, "Locked rooms/capacity lost.");
            });
        }

        private static void ReleasedPlayers()
        {
            WithSlot(path =>
            {
                HotelState state = New();
                ItemState toolbox = state.items.Find(i => i.kind == "toolbox");
                ItemState mop = state.items.Find(i => i.kind == "mop");
                RoomState room = state.rooms[0]; room.leak = true; room.water = .5f;
                room.mvp.equipment.Find(e => e.kind == "sink").localFault = true;
                room.mvp.equipment.Find(e => e.kind == "sink").episode = 1;
                Hold(state, toolbox, 0, "sink_101"); Hold(state, mop, 1, "water_101");
                string before = JsonUtility.ToJson(state);
                HotelSaveStore.Save(state, path);
                HotelState loaded = HotelSaveStore.Load(path);
                Need(before == JsonUtility.ToJson(state), "Saving mutated active players/work.");
                Need(loaded.players.Count == 0 && loaded.items.TrueForAll(i => i.holder == -1), "Load retained hands or work leases.");
                Need(loaded.rooms[0].leak && loaded.rooms[0].water == .5f, "Loading finished physical work.");
            });
        }

        private static void Corruption()
        {
            WithSlot(path =>
            {
                HotelState state = New(); HotelSaveStore.Save(state, path);
                state.cash = 333; HotelSaveStore.Save(state, path);
                string backup = File.ReadAllText(path + ".bak");
                File.WriteAllText(path, "{broken");
                HotelState recovered = HotelSaveStore.Load(path);
                Need(recovered.cash == 400 && recovered.notice.Contains("резервная"), "Corrupt primary was not recovered.");
                recovered.cash = 321; HotelSaveStore.Save(recovered, path);
                Need(File.ReadAllText(path + ".bak") == backup, "Repair destroyed good backup.");
                Envelope envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path));
                envelope.checksum = "wrong"; File.WriteAllText(path, JsonUtility.ToJson(envelope));
                Need(HotelSaveStore.Load(path).cash == 400, "Bad checksum bypassed backup recovery.");
                File.WriteAllText(path + ".bak", "{}");
                Throws<InvalidDataException>(() => HotelSaveStore.Load(path));
                Need(File.Exists(path) && File.Exists(path + ".bak"), "Corruption handling deleted slot files.");
            });
        }

        private static void FutureVersions()
        {
            WithSlot(path =>
            {
                HotelState good = New(); HotelSaveStore.Save(good, path); HotelSaveStore.Save(good, path);
                string primary = File.ReadAllText(path), backup = File.ReadAllText(path + ".bak");
                foreach (Action<HotelState> future in new Action<HotelState>[] {
                    s => s.version = 4, s => s.contentVersion = 4, s => s.mvp.rngVersion = 2 })
                {
                    HotelState state = Copy(good); future(state); state.rooms = null;
                    Throws<NotSupportedException>(() => HotelSaveStore.Validate(state));
                    // Even simultaneous structural/checksum damage must not downgrade future data.
                    WriteEnvelope(path, state, 2, false);
                    string unsupported = File.ReadAllText(path);
                    Throws<NotSupportedException>(() => HotelSaveStore.Load(path));
                    Throws<NotSupportedException>(() => HotelSaveStore.Save(good, path));
                    Need(File.ReadAllText(path) == unsupported && File.ReadAllText(path + ".bak") == backup, "Future payload changed files.");
                }
                File.WriteAllText(path, "{\"format\":\"unknown\",\"version\":4}");
                Throws<NotSupportedException>(() => HotelSaveStore.Load(path));
                Throws<NotSupportedException>(() => HotelSaveStore.Save(good, path));
                File.WriteAllText(path, JsonUtility.ToJson(new Envelope { version = 2, checksum = "wrong",
                    payload = "{\"version\":4,\"contentVersion\":4,\"rooms\":{\"futureShape\":true}}" }));
                Throws<NotSupportedException>(() => HotelSaveStore.Load(path));
                File.WriteAllText(path, primary);
                File.WriteAllText(path + ".bak", "{\"format\":\"WorstHotelSave\",\"version\":4}");
                Throws<NotSupportedException>(() => HotelSaveStore.Save(good, path));
                Need(File.ReadAllText(path) == primary, "Future backup overwritten by valid primary save.");
                File.WriteAllText(path, "{}");
                Throws<NotSupportedException>(() => HotelSaveStore.Load(path));
            });
        }

        private static void Dispatch()
        {
            WithSlot(path =>
            {
                HotelState legacy = new HotelSimulation(null, HotelGuestCatalog.Legacy()).State;
                HotelSaveStore.Save(legacy, path);
                Need(JsonUtility.FromJson<Envelope>(File.ReadAllText(path)).version == 1 && HotelSaveStore.Load(path).version == 1, "Legacy save was silently promoted.");
                HotelState state = New(); state.mvp = null;
                Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
                WriteEnvelope(path, state, 2); Throws<InvalidDataException>(() => HotelSaveStore.Load(path));
                state = New(); WriteEnvelope(path, state, 1);
                Throws<InvalidDataException>(() => HotelSaveStore.Load(path));
                WriteEnvelope(path, legacy, 2); Throws<InvalidDataException>(() => HotelSaveStore.Load(path));
                legacy.contentVersion = 2; Throws<InvalidDataException>(() => HotelSaveStore.Validate(legacy));
                state.version = 0; Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            });
        }

        private static void MigrationArchive()
        {
            WithSlot(path =>
            {
                var legacy = new HotelSimulation(null, HotelGuestCatalog.Legacy());
                HotelSaveStore.Save(legacy.State, path); legacy.State.cash = 345; HotelSaveStore.Save(legacy.State, path);
                byte[] primary = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
                HotelState loaded = HotelSaveStore.Load(path); string world = loaded.worldId;
                HotelSimulation resumed = HotelSimulation.ResumeForPlay(loaded);
                Need(ReferenceEquals(loaded, resumed.State) && loaded.worldId == world && loaded.version == 2, "Promotion changed root/world identity.");
                Need(BytesEqual(primary, File.ReadAllBytes(path)), "Factory migration wrote the slot.");
                HotelSaveStore.Save(loaded, path);
                string[] archives = Directory.GetDirectories(Path.Combine(Path.GetDirectoryName(path), "archive"));
                Need(archives.Length == 1, "No unique v1 migration archive.");
                Need(BytesEqual(primary, File.ReadAllBytes(Path.Combine(archives[0], "primary.json"))) &&
                    BytesEqual(backup, File.ReadAllBytes(Path.Combine(archives[0], "backup.json"))), "Original checkpoint bytes not preserved.");
                Need(HotelSaveStore.Load(path).worldId == world && HotelSaveStore.Load(path).cash == 345, "Migration changed economy/world.");
                loaded.cash = 320; HotelSaveStore.Save(loaded, path); HotelSaveStore.Save(loaded, path);
                Need(Directory.GetDirectories(Path.Combine(Path.GetDirectoryName(path), "archive")).Length == 1, "Ordinary v2 save archived again.");
                Need(BytesEqual(primary, File.ReadAllBytes(Path.Combine(archives[0], "primary.json"))), "Later v2 writes changed immutable archive.");
            });
        }

        private static void RecoveredMigrationArchive()
        {
            WithSlot(path =>
            {
                HotelState old = new HotelSimulation(null, HotelGuestCatalog.Legacy()).State;
                HotelSaveStore.Save(old, path); old.cash = 300; HotelSaveStore.Save(old, path);
                File.WriteAllText(path, "{broken original primary");
                byte[] primary = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
                HotelState recovered = HotelSimulation.ResumeForPlay(HotelSaveStore.Load(path)).State;
                HotelSaveStore.Save(recovered, path);
                string archive = Directory.GetDirectories(Path.Combine(Path.GetDirectoryName(path), "archive"))[0];
                Need(BytesEqual(primary, File.ReadAllBytes(Path.Combine(archive, "primary.json"))) &&
                    BytesEqual(backup, File.ReadAllBytes(Path.Combine(archive, "backup.json"))), "Recovered migration did not preserve both originals.");
                Need(BytesEqual(backup, File.ReadAllBytes(path + ".bak")), "Corrupt primary replaced good legacy backup.");
            });
        }

        private static void LegacyMissingItemFields()
        {
            WithSlot(path =>
            {
                HotelState legacy = new HotelSimulation(null, HotelGuestCatalog.Legacy()).State;
                string payload = JsonUtility.ToJson(legacy).Replace(",\"size\":\"small\"", "").Replace(",\"condition\":\"clean\"", "");
                Need(!payload.Contains("\"size\"") && !payload.Contains("\"condition\""), "Historical fixture retained new item fields.");
                WritePayload(path, payload, 1);
                HotelState loaded = HotelSaveStore.Load(path);
                Need(loaded.version == 1 && loaded.cash == legacy.cash, "Legacy validation required v2 item fields.");
                HotelState promoted = HotelSimulation.ResumeForPlay(loaded).State;
                HotelSaveStore.Save(promoted, path);
                Need(promoted.items.TrueForAll(i => i.size == "small" && i.condition == "clean"), "Migration did not canonicalize legacy item fields.");
                Need(HotelSaveStore.Load(path).version == 2, "Historical JSON failed native migration roundtrip.");
            });
        }

        private static void ArchiveFailure()
        {
            WithSlot(path =>
            {
                HotelState old = new HotelSimulation(null, HotelGuestCatalog.Legacy()).State;
                HotelSaveStore.Save(old, path); HotelSaveStore.Save(old, path);
                string primary = File.ReadAllText(path), backup = File.ReadAllText(path + ".bak");
                HotelState upgraded = HotelSimulation.ResumeForPlay(HotelSaveStore.Load(path)).State;
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), "archive"), "blocks archive directory");
                Throws<IOException>(() => HotelSaveStore.Save(upgraded, path));
                Need(File.ReadAllText(path) == primary && File.ReadAllText(path + ".bak") == backup, "Archive failure replaced an original.");
                Need(Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp").Length == 0, "Failed save leaked temporary snapshot.");
            });
        }

        private static void CheckpointProtection()
        {
            WithSlot(path =>
            {
                HotelState state = New(); HotelSaveStore.Save(state, path); HotelSaveStore.Save(state, path);
                string primary = File.ReadAllText(path), backup = File.ReadAllText(path + ".bak");
                Throws<InvalidOperationException>(() => HotelSaveStore.Save(New(), path));
                HotelState old = new HotelSimulation(null, HotelGuestCatalog.Legacy()).State; old.worldId = state.worldId;
                Throws<InvalidOperationException>(() => HotelSaveStore.Save(old, path));
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Throws<IOException>(() => HotelSaveStore.Load(path));
                    Throws<IOException>(() => HotelSaveStore.Save(state, path));
                }
                using (var locked = new FileStream(path + ".bak", FileMode.Open, FileAccess.Read, FileShare.None))
                    Throws<IOException>(() => HotelSaveStore.Save(state, path));
                state.rooms[0].mvp.equipment[0].wear = float.NaN;
                Throws<InvalidDataException>(() => HotelSaveStore.Save(state, path));
                Need(File.ReadAllText(path) == primary && File.ReadAllText(path + ".bak") == backup, "Failed save changed checkpoint files.");
            });
        }

        private static void ContinuingStay()
        {
            WithSlot(path =>
            {
                HotelState state = StayFixture(); GuestState guest = state.guests[0];
                ItemState bag = state.items.Find(i => i.ownerGuest == guest.id);
                bag.placedRoom = 103; bag.position = HotelLayout.Target("bag_103"); guest.luggageDelivered = false;
                state.rooms[0].mvp.towelUseProgress = .4f;
                HotelSaveStore.Save(state, path);
                HotelState loaded = HotelSaveStore.Load(path);
                Need(loaded.day == 2 && loaded.guests[0].mvp.arrivalDay == 1 && loaded.guests[0].mvp.departureDay == 3 &&
                    loaded.guests[0].mvp.billableNights == 2 && loaded.guests[0].mvp.agreedTariff == 135, "Continuing contract changed.");
                Need(loaded.guests[1].mvp.partySize == 2 && loaded.guests[1].room == 102, "Family party lost.");
                ItemState wrong = loaded.items.Find(i => i.id == bag.id);
                Need(wrong.ownerGuest == guest.id && wrong.placedRoom == 103 && !loaded.guests[0].luggageDelivered, "Wrong bag was rejected or reassigned.");
                Need(loaded.rooms[0].mvp.towelUseProgress == .4f, "Towel-use progress not persisted.");
            });
        }

        private static void CartCapacity()
        {
            WithSlot(path =>
            {
                HotelState state = New(); state.cartUpgrade = true;
                state.items.Add(new ItemState { id = "cart_1", kind = "cart", position = HotelLayout.Spawn });
                for (int i = 0; i < 4; i++)
                {
                    GuestState g = AddGuest(state, 0, 1, 2);
                    ItemState bag = state.items.Find(x => x.ownerGuest == g.id); bag.placedRoom = -1;
                }
                HotelSaveStore.Save(state, path);
                HotelState loaded = HotelSaveStore.Load(path);
                Need(loaded.items.FindAll(i => i.placedRoom == -1).Count == 4, "Four small bags lost.");
                var positions = new HashSet<Vector3>();
                foreach (ItemState bag in loaded.items) if (bag.placedRoom == -1) Need(positions.Add(bag.position), "Cargo collapsed onto duplicate slots.");
                state.items.Find(i => i.kind == "bag").size = "large";
                Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
                HotelState twoLarge = New(); twoLarge.cartUpgrade = true;
                twoLarge.items.Add(new ItemState { id = "cart_1", kind = "cart", position = HotelLayout.Spawn });
                for (int i = 0; i < 2; i++)
                {
                    GuestState guest = AddGuest(twoLarge, 0, 1, 2);
                    ItemState bag = twoLarge.items.Find(x => x.ownerGuest == guest.id); bag.size = "large"; bag.placedRoom = -1;
                }
                HotelSaveStore.Validate(twoLarge);
            });
        }

        private static void HospitalityRoundtrip()
        {
            WithSlot(path =>
            {
                HotelState state = StayFixture(); int guestId = state.guests[0].id;
                state.guests[0].mvp.requestIssued = true;
                state.mvp.requests.Add(new MvpRequestState { id = "request_test", guestId = guestId, kind = "coffee", target = "coffee_101", createdAt = 1000, dueAt = 1100 });
                state.mvp.complaints.Add(new MvpComplaintState { id = "complaint_test", guestId = guestId, category = "coffee", causeKey = "request:request_test", exposure = 30, escalation = 1 });
                state.mvp.director.events.Add(new MvpEventState { id = "event_test", kind = "rush", category = "guest", startedAt = 1100, until = 1250 });
                state.mvp.director.cooldowns.Add(new MvpCooldownState { id = "event_rush", until = 1400 });
                state.mvp.director.recent.Add("rush");
                state.mvp.pings.Add(new MvpPingState { playerId = 0, target = "coffee_101", until = state.mvp.elapsed + 6 });
                state.mvp.reviews.Add(new MvpReviewState { guestId = 100, day = 1, stars = 4, text = "Тихо, уютно. ☕" }); state.nextGuest = 101;
                HotelSaveStore.Save(state, path);
                HotelState loaded = HotelSaveStore.Load(path);
                Need(JsonUtility.ToJson(loaded) == JsonUtility.ToJson(state), "Hospitality/director crossrefs did not roundtrip.");
            });
        }

        private static void InvalidReferences()
        {
            foreach (Action<HotelState> change in new Action<HotelState>[] {
                s => s.rooms[1].number = 101,
                s => s.rooms[0].mvp.owned = false,
                s => s.rooms[0].mvp.capacity = 2,
                s => s.rooms[0].mvp.equipment.RemoveAt(0),
                s => s.rooms[0].mvp.equipment[1].kind = "sink",
                s => s.rooms[0].mvp.equipment[0].wear = float.NaN,
                s => s.rooms[0].mvp.equipment[0].quality = 3,
                s => s.rooms[0].mvp.towelUseProgress = 1.01f,
                s => s.rooms[0].mvp.bedQuality = 2,
                s => s.mvp.utilities.powerWear = float.PositiveInfinity,
                s => s.items.RemoveAll(i => i.kind == "plunger"),
                s => s.items[0].consumed = true,
                s => s.items[0].holder = 42,
                s => s.items.Add(new ItemState { id = "fake", kind = "dirtytowel" }),
                s => s.items.Add(new ItemState { id = "fake", kind = "coffeecup" }),
                s => s.guests[0].mvp.partySize = 2,
                s => s.guests[0].mvp.agreedTariff = 0,
                s => s.guests[0].mvp.billableNights = 1,
                s => s.guests[0].mvp.reservationId = "missing",
                s => s.guests[0].room = 105,
                s => s.guests[0].luggageDelivered = false,
                s => s.items.Find(i => i.kind == "bag").ownerGuest = 999,
                s => s.mvp.reservations[0].room = 105,
                s => s.mvp.reservations[0].status = "whatever",
                s => s.mvp.reservations[0].guestId = 999,
                s => s.mvp.schedule[0].reservationId = "missing",
                s => s.mvp.schedule[0].contract.agreedTariff++,
                s => s.mvp.requests.Add(new MvpRequestState { id = "bad", guestId = 999, kind = "towel", target = "towel_101" }),
                s => s.mvp.complaints.Add(new MvpComplaintState { id = "bad", guestId = 999, category = "dirt", causeKey = "room:dirt:101" }),
                s => s.mvp.pings.Add(new MvpPingState { target = "arbitrary text", until = 1201 }),
                s => s.mvp.director.recent.Add("unknown"),
                s => s.mvp.rngState = 0 })
            {
                HotelState state = StayFixture(); change(state);
                Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            }
            HotelState leased = New(); ItemState toolbox = leased.items.Find(i => i.kind == "toolbox");
            Hold(leased, toolbox, 0, "sink_101");
            leased.players.Add(new PlayerState { id = 1, position = HotelLayout.Spawn, workTarget = "sink_101" });
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(leased));
        }

        private static void GroupsAndHistory()
        {
            WithSlot(path =>
            {
                HotelState state = New();
                var planned = new MvpGroupState { id = "group_planned", arrivalDay = 2, source = "planned" };
                state.mvp.groups.Add(planned);
                for (int n = 101; n <= 102; n++)
                {
                    MvpReservationState r = Booking(state, n, 2, 3, "group_" + n);
                    r.groupId = r.contract.groupId = planned.id; planned.reservationIds.Add(r.id);
                    state.mvp.schedule.Add(new MvpScheduleState { id = "arrival_group_" + n, day = 2, groupId = planned.id,
                        reservationId = r.id, source = "tourist_group", contract = JsonUtility.FromJson<MvpGuestState>(JsonUtility.ToJson(r.contract)) });
                }
                var unexpected = new MvpGroupState { id = "group_walkin", arrivalDay = 1, source = "walkin_group" };
                state.mvp.groups.Add(unexpected);
                for (int n = 1; n <= 2; n++)
                    state.mvp.schedule.Add(new MvpScheduleState { id = "arrival_walkin_" + n, day = 1, groupId = unexpected.id, source = "walkin_group",
                        contract = new MvpGuestState { partyId = "party_walkin_" + n, groupId = unexpected.id } });
                HotelSaveStore.Save(state, path);
                Need(JsonUtility.ToJson(HotelSaveStore.Load(path)) == JsonUtility.ToJson(state), "Group topology did not roundtrip.");
                planned.reservationIds.Add("missing"); Throws<InvalidDataException>(() => HotelSaveStore.Validate(state)); planned.reservationIds.Remove("missing");
                planned.admitted = 3; Throws<InvalidDataException>(() => HotelSaveStore.Validate(state)); planned.admitted = 0;
                state.mvp.schedule[0].groupId = "missing"; Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));

                state = StayFixture();
                // An admitted walk-in's original schedule never gains the later reservation ID.
                state.mvp.schedule[0].reservationId = state.mvp.schedule[0].contract.reservationId = "";
                HotelSaveStore.Validate(state);
                // Terminal schedule and reservation records can survive after guest pruning.
                GuestState retired = state.guests[0];
                state.rooms.Find(r => r.number == retired.room).guestId = 0;
                state.guests.Remove(retired);
                ItemState retiredBag = state.items.Find(i => i.ownerGuest == retired.id);
                retiredBag.consumed = true; retiredBag.placedRoom = 0;
                state.mvp.reservations[0].status = "completed"; state.mvp.reservations[0].guestId = 0;
                state.mvp.schedule[0].guestId = 0;
                GuestState observer = state.guests[0];
                state.mvp.complaints.Add(new MvpComplaintState { id = "old_wrongbag", guestId = observer.id, category = "luggage",
                    causeKey = "wrongbag:removed_bag", status = "resolved", recovered = true, recoveredAt = 1100 });
                state.mvp.complaints.Add(new MvpComplaintState { id = "old_noise", guestId = observer.id, category = "noise",
                    causeKey = "noise:101:" + retired.id, status = "resolved", recoveredAt = 1100 });
                HotelSaveStore.Validate(state);
                state.mvp.complaints[0].status = "active"; Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            });
        }

        private static void ReservationIntervals()
        {
            HotelState state = StayFixture();
            MvpReservationState next = Booking(state, 101, 3, 4, "next");
            HotelSaveStore.Validate(state); // [1,3) and [3,4) are adjacent, not overlapping.
            next.arrivalDay = next.contract.arrivalDay = 2;
            next.contract.billableNights = 2;
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            next.status = "cancelled"; HotelSaveStore.Validate(state);
            state.mvp.reservations.Remove(next);
            // An unbooked occupant still blocks a future reservation for its interval.
            GuestState guest = state.guests[0]; guest.mvp.reservationId = "";
            state.mvp.schedule.RemoveAll(s => s.guestId == guest.id);
            state.mvp.reservations.RemoveAll(r => r.guestId == guest.id);
            Booking(state, 101, 2, 4, "conflict");
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
        }

        private static void Limits()
        {
            HotelState state = New();
            for (int i = 0; i <= HotelMvpValidation.MaxGuests; i++) AddGuest(state, 0, 1, 2);
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            state = New(); state.notice = new string('x', 1025);
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            state = New();
            for (int i = 0; i <= HotelMvpValidation.MaxItems; i++) state.items.Add(new ItemState { id = "towel_" + i, kind = "towel" });
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            // Stay within each history's count/text limit while exceeding the actual wire bytes.
            state = New(); state.ledger.Clear(); state.reviews.Clear();
            for (int i = 0; i < 256; i++) state.ledger.Add(new string('Ж', 512));
            Need((long)Encoding.Unicode.GetByteCount(JsonUtility.ToJson(state)) + 16 > HotelMvpValidation.MaxSnapshotBytes, "Oversize fixture is too small.");
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            while (SnapshotBytes(state) > HotelMvpValidation.MaxSnapshotBytes) state.ledger.RemoveAt(state.ledger.Count - 1);
            int spare = (HotelMvpValidation.MaxSnapshotBytes - SnapshotBytes(state)) / 2;
            state.notice += new string('Ж', spare);
            Need(state.notice.Length <= 1024 && SnapshotBytes(state) == HotelMvpValidation.MaxSnapshotBytes, "Could not construct exact UTF-16 boundary.");
            HotelSaveStore.Validate(state);
            state.notice += "Ж";
            Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
            state.notice = "";
            state.ledger.Clear(); HotelSaveStore.Validate(state);
            state.mvp.requests = null; Throws<InvalidDataException>(() => HotelSaveStore.Validate(state));
        }

        private static HotelState New()
        {
            HotelState state = HotelSimulation.CreateNewMvp(173).State;
            state.mvp.reservations.Clear(); state.mvp.schedule.Clear(); state.mvp.groups.Clear();
            return state;
        }

        private static HotelState StayFixture()
        {
            HotelState state = New(); state.day = 2; state.phase = "open"; state.time = 120; state.mvp.elapsed = 1200;
            AddBookedGuest(state, 101, 1, 3); AddBookedGuest(state, 102, 1, 3, true);
            return state;
        }

        private static GuestState AddGuest(HotelState state, int room, int arrival, int departure, bool family = false)
        {
            int id = state.nextGuest++;
            var contract = new MvpGuestState { archetype = family ? "family" : "tourist", partySize = family ? 2 : 1,
                partyId = "party_test_" + id, arrivalDay = arrival, departureDay = departure, billableNights = departure - arrival, agreedTariff = 135 };
            var guest = new GuestState { id = id, room = room, name = "Гость " + id, kind = contract.archetype, trait = "Терпеливый",
                stage = room == 0 ? "queue" : "staying", position = HotelLayout.Spawn, mvp = contract, luggageDelivered = room != 0 };
            state.guests.Add(guest);
            if (room != 0) state.rooms.Find(r => r.number == room).guestId = id;
            state.items.Add(new ItemState { id = "luggage_test_" + id, kind = "bag", ownerGuest = id, placedRoom = room,
                position = room == 0 ? HotelLayout.Spawn : HotelLayout.Target("bag_" + room) });
            return guest;
        }

        private static void AddBookedGuest(HotelState state, int room, int arrival, int departure, bool family = false)
        {
            GuestState guest = AddGuest(state, room, arrival, departure, family);
            guest.mvp.reservationId = "reservation_test_" + guest.id;
            MvpGuestState quote = JsonUtility.FromJson<MvpGuestState>(JsonUtility.ToJson(guest.mvp));
            state.mvp.reservations.Add(new MvpReservationState { id = guest.mvp.reservationId, room = room, guestId = guest.id,
                arrivalDay = arrival, departureDay = departure, partyId = guest.mvp.partyId, status = "staying", contract = quote });
            state.mvp.schedule.Add(new MvpScheduleState { id = "schedule_test_" + guest.id, reservationId = guest.mvp.reservationId,
                guestId = guest.id, day = arrival, status = "arrived", source = "booking",
                contract = JsonUtility.FromJson<MvpGuestState>(JsonUtility.ToJson(quote)) });
        }

        private static MvpReservationState Booking(HotelState state, int room, int arrival, int departure, string suffix)
        {
            var quote = new MvpGuestState { partyId = "party_" + suffix, reservationId = "reservation_" + suffix,
                arrivalDay = arrival, departureDay = departure, billableNights = departure - arrival };
            var booking = new MvpReservationState { id = quote.reservationId, partyId = quote.partyId, room = room,
                arrivalDay = arrival, departureDay = departure, contract = quote };
            state.mvp.reservations.Add(booking);
            return booking;
        }

        private static void Hold(HotelState state, ItemState item, ulong id, string work)
        {
            item.holder = (long)id; item.placedRoom = 0;
            state.players.Add(new PlayerState { id = id, held = item.id, workTarget = work, workProgress = .3f, workLastSeen = .1f,
                position = new Vector3(HotelLayout.Target(work).x, .1f, HotelLayout.Target(work).z) });
        }

        [Serializable] private sealed class Envelope
        {
            public string format = "WorstHotelSave", checksum, payload;
            public int version;
        }

        private static void WriteEnvelope(string path, HotelState state, int version, bool validChecksum = true)
        {
            WritePayload(path, JsonUtility.ToJson(state), version, validChecksum);
        }

        private static void WritePayload(string path, string payload, int version, bool validChecksum = true)
        {
            string checksum;
            using (SHA256 sha = SHA256.Create()) checksum = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(path, JsonUtility.ToJson(new Envelope { version = version, payload = payload, checksum = validChecksum ? checksum : "wrong" }));
        }

        private static HotelState Copy(HotelState state) { return JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(state)); }
        private static int SnapshotBytes(HotelState state) { return Encoding.Unicode.GetByteCount(JsonUtility.ToJson(state)) + 16; }
        private static bool BytesEqual(byte[] a, byte[] b)
        { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        private static void WithSlot(Action<string> test)
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WorstHotelMvpPersistence-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            try { test(Path.Combine(root, "hotel.json")); }
            finally
            {
                // Tests own only this freshly-created unique directory, never the real game slot.
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("WorstHotelMvpPersistence-", StringComparison.Ordinal))
                    throw new IOException("Refusing to clean an unexpected test directory.");
                Directory.Delete(root, true);
            }
        }
        private static void Run(List<string> passed, string name, Action test)
        { try { test(); passed.Add(name); } catch (Exception e) { throw new Exception("HotelMvpPersistenceTests FAILED: " + name, e); } }
        private static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Throws<T>(Action test) where T : Exception
        { try { test(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    }
}
