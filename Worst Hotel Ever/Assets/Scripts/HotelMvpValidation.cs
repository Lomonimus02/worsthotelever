using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    // Structural validation only; danger targets use the agreed read-only rules API.
    // Never runs gameplay, repairs data or draws RNG.
    public static class HotelMvpValidation
    {
        // Both transports budget the expanded snapshot as UTF-16 JSON plus framing.
        public const int MaxSnapshotBytes = 262144;
        public const int MaxGuests = 24, MaxItems = 512, MaxRequests = 128, MaxComplaints = 192;
        public const int MaxReservations = 128, MaxSchedule = 128, MaxGroups = 16;
        // Pacing retains 24 compact terminal event records; the total byte cap still applies.
        public const int MaxReviews = 60, MaxEvents = 24, MaxCooldowns = 32, MaxRecent = 8, MaxPings = 2;
        private const float MaxClock = 1000000000000f;

        public static void Validate(HotelState state)
        {
            // Version checks deliberately precede every structural check, including null MVP data.
            HotelSaveStore.RejectFutureVersions(state);
            Need(state != null && (state.version == 2 || state.version == 3) && state.contentVersion == state.version && state.mvp != null,
                "Missing or mismatched MVP schema.");
            MvpHotelState mvp = state.mvp;
            Need(mvp.schema == 2, "MVP state is missing its explicit schema marker.");
            Id(state.worldId); Text(state.notice, 1024);
            Need(state.day > 0 && state.day < 1000000 && state.nextGuest > 0 && state.nextGuest < int.MaxValue - 10, "Invalid hotel counters.");
            Need(OneOf(state.phase, "preparation", "open", "closing", "summary"), "Unknown shift phase.");
            Need(Range(state.dayLength, 1, 86400) && Range(state.time, 0, state.dayLength + .01f), "Invalid shift clock.");
            Need(state.arrivals >= 0 && state.arrivals <= 10000 && state.served >= 0 && state.served <= 10000, "Invalid daily guest counts.");
            Need(Math.Abs((long)state.cash) < 100000000 && state.earned >= 0 && state.earned < 100000000 && state.expenses >= 0 && state.expenses < 100000000, "Invalid hotel money.");
            Need(Stock(state.linenStock) && Stock(state.towelStock) && Stock(mvp.coffeeStock), "Invalid supplies.");
            Need(OneOf(mvp.priceMode, "low", "normal", "high") && Range(mvp.reputation, 1, 5), "Invalid price/reputation.");
            Need(mvp.rngVersion == 1 && mvp.rngState != 0 && mvp.rngDraws >= 0 && mvp.nextId > 0 && mvp.nextId < int.MaxValue - 10, "Invalid persisted RNG/counter.");
            Need(Clock(mvp.elapsed) && mvp.preparedDay >= 0 && mvp.preparedDay <= state.day, "Invalid calendar/preparation clock.");
            Need(state.guidedStage >= HotelDirector.Welcome && state.guidedStage <= HotelDirector.Released &&
                state.guidedGuestId >= 0 && state.guidedGuestId < state.nextGuest, "Invalid guided stage/guest.");
            Need(state.guidedLeakRoom == 0 || (state.guidedLeakRoom >= 101 && state.guidedLeakRoom <= 106), "Invalid guided room.");
            Need(state.guidedStage != HotelDirector.Leak || (state.guidedLeakRoom != 0 && state.dailyLeakIssued), "Missing guided leak.");
            Need(!state.guidedRepairDone || (state.guidedStage >= HotelDirector.Leak && state.guidedLeakRoom != 0), "Invalid guided repair.");
            Need(!state.guidedMopDone || state.guidedRepairDone, "Invalid guided mop.");
            Need(Range(state.nextArrivalTime, 0, state.dayLength + HotelDirector.NormalArrivalInterval), "Invalid legacy arrival clock.");
            Strings(state.reviews, 120, 1024); Strings(state.ledger, 256, 512);
            Count(state.rooms, 6); Need(state.rooms.Count == 6, "Exactly six room records are required.");
            Count(state.guests, MaxGuests); Count(state.items, MaxItems); Count(state.players, 2);
            Count(mvp.requests, MaxRequests); Count(mvp.complaints, MaxComplaints);
            Count(mvp.reservations, MaxReservations); Count(mvp.schedule, MaxSchedule); Count(mvp.groups, MaxGroups);
            Count(mvp.reviews, MaxReviews); Count(mvp.pings, MaxPings);

            var rooms = Rooms(state);
            var guests = Guests(state, rooms);
            if (state.version == 3) HotelDangerValidation.Validate(state);
            PlayersAndItems(state, rooms, guests);
            Hospitality(state, rooms, guests);
            DirectorAndPings(state, rooms);
            Need((long)Encoding.Unicode.GetByteCount(JsonUtility.ToJson(state)) + 16 <= MaxSnapshotBytes, "Snapshot exceeds the shared 256 KiB wire budget.");
        }

        private static Dictionary<int, RoomState> Rooms(HotelState state)
        {
            var rooms = new Dictionary<int, RoomState>();
            foreach (RoomState room in state.rooms)
            {
                Need(room != null && room.number >= 101 && room.number <= 106 && !rooms.ContainsKey(room.number) && room.mvp != null, "Missing/duplicate room.");
                MvpRoomState m = room.mvp;
                Need(room.number > 104 || m.owned, "A starting room cannot be unowned.");
                Need(m.owned || room.guestId == 0, "Unowned room has an occupant.");
                Need(m.capacity == (room.number % 2 == 0 ? 2 : 1), "Invalid room capacity.");
                Need(Quality(m.bedQuality) && Quality(m.tvQuality) && room.upgraded == (m.bedQuality > 1), "Invalid furniture quality/projection.");
                Need(OneOf(m.finishId, "original", "warm", "cool"), "Unknown room finish.");
                Need(room.bed >= 0 && room.bed <= 2 && room.guestId >= 0 && Range(room.water, 0, 1), "Invalid room state.");
                Need(Range(m.dirt, 0, 1) && Range(m.binFill, 0, 1) && Range(m.noise, 0, 1) && Range(m.towelUseProgress, 0, 1) && m.dirtyTowels >= 0 && m.dirtyTowels <= 10000, "Invalid room dirt/noise/towel use.");
                Count(m.equipment, 4); Need(m.equipment.Count == 4, "Missing equipment slots.");
                var kinds = new HashSet<string>(StringComparer.Ordinal);
                foreach (MvpEquipmentState e in m.equipment)
                {
                    Need(e != null && OneOf(e.kind, "sink", "toilet", "tv", "lamp") && kinds.Add(e.kind), "Unknown/duplicate equipment slot.");
                    Need(Quality(e.quality) && e.episode >= 0 && Range(e.wear, 0, 1) && (e.installed || !e.localFault), "Invalid equipment quality/fault/wear.");
                    if (e.kind == "sink") Need(room.leak == e.localFault, "Sink fault/leak projection disagrees.");
                    if (e.kind == "tv") Need(m.tvQuality == e.quality, "TV slot/room quality disagrees.");
                }
                rooms.Add(room.number, room);
            }
            MvpUtilityState u = state.mvp.utilities;
            Need(u != null && u.waterEpisode >= 0 && u.powerEpisode >= 0 && Range(u.waterWear, 0, 1) && Range(u.powerWear, 0, 1), "Invalid utility state.");
            return rooms;
        }

        private static Dictionary<int, GuestState> Guests(HotelState state, Dictionary<int, RoomState> rooms)
        {
            var guests = new Dictionary<int, GuestState>();
            var parties = new HashSet<string>(StringComparer.Ordinal);
            foreach (GuestState guest in state.guests)
            {
                Need(guest != null && guest.id > 0 && guest.id < state.nextGuest && !guests.ContainsKey(guest.id), "Invalid/duplicate guest ID.");
                Need(OneOf(guest.stage, "queue", "walking", "staying", "checkout", "leaving", "gone"), "Unknown guest stage.");
                Need(HotelSimulation.Walkable(guest.position) && Range(guest.satisfaction, 0, 100) && Clock(guest.waited) && Clock(guest.stay), "Invalid guest position/timers/satisfaction.");
                Text(guest.name, 128, false); Text(guest.kind, 128); Text(guest.trait, 128);
                Strings(guest.memories, 64, 512);
                // Legacy display/profile fields can survive promotion; only the v2 contract uses
                // the new enum values. Still bound every serialized legacy field and float.
                Need(guest.profileVersion >= 0 && guest.profileVersion <= 1, "Invalid legacy guest profile.");
                Text(guest.profileId, 128); Text(guest.requestId, 128); Text(guest.requestKind, 128);
                foreach (float timer in new[] { guest.patienceWarning, guest.patienceLimit, guest.requestDelay, guest.requestGrace,
                    guest.stayDuration, guest.luggageGrace, guest.checkoutWarning, guest.checkoutLimit, guest.requestElapsed, guest.requestWait })
                    Need(Clock(timer), "Invalid legacy guest timer.");
                Contract(guest.mvp);
                Need(string.IsNullOrEmpty(guest.mvp.partyId) || parties.Add(guest.mvp.partyId), "Duplicate visible party.");
                Need(guest.mvp.arrivalDay <= state.day, "Guest has not arrived yet.");
                Need(guest.room == 0 || (rooms.ContainsKey(guest.room) && rooms[guest.room].mvp.owned), "Guest references an unavailable room.");
                if (Occupies(guest))
                {
                    Need(guest.room != 0 && rooms[guest.room].guestId == guest.id && !guest.paid, "Broken occupant/room link.");
                    Need(rooms[guest.room].mvp.capacity >= guest.mvp.partySize, "Guest party exceeds room capacity.");
                }
                if (guest.stage == "queue") Need(guest.room == 0 && !guest.paid, "Queued guest has a room/payment.");
                Need(!guest.paid || Terminal(guest), "Paid guest is still active.");
                guests.Add(guest.id, guest);
            }
            foreach (RoomState room in rooms.Values)
                if (room.guestId != 0)
                    Need(guests.TryGetValue(room.guestId, out GuestState guest) && guest.room == room.number && Occupies(guest), "Room references a missing occupant.");
            return guests;
        }

        private static void PlayersAndItems(HotelState state, Dictionary<int, RoomState> rooms, Dictionary<int, GuestState> guests)
        {
            var players = new Dictionary<ulong, PlayerState>();
            var leases = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlayerState player in state.players)
            {
                Need(player != null && player.id <= long.MaxValue && !players.ContainsKey(player.id), "Invalid/duplicate player ID.");
                Need(HotelSimulation.Walkable(player.position) && HotelSimulation.Finite(player.yaw) && HotelSimulation.Finite(player.pitch), "Invalid player pose.");
                Text(player.held, 128); Text(player.workTarget, 128);
                Need(Range(player.workProgress, 0, 1) && Clock(player.workLastSeen), "Invalid work lease timer.");
                if (string.IsNullOrEmpty(player.workTarget)) Need(player.workProgress == 0 && player.workLastSeen == 0, "Inactive work lease has progress.");
                else Need(WorkTarget(state, player.workTarget, rooms) && leases.Add(player.workTarget), "Invalid/duplicate work lease.");
                players.Add(player.id, player);
            }
            var items = new Dictionary<string, ItemState>(StringComparer.Ordinal);
            int toolboxes = 0, mops = 0, plungers = 0, carts = 0, cargo = 0;
            var bags = new Dictionary<int, ItemState>();
            foreach (ItemState item in state.items)
            {
                Need(item != null, "Null item."); Id(item.id);
                Need(!items.ContainsKey(item.id), "Duplicate item ID.");
                Need(OneOf(item.kind, "toolbox", "mop", "plunger", "cart", "bag", "linen", "towel", "dirtylinen", "trashbag", "coffee"), "Unknown item kind.");
                Need(OneOf(item.size, "small", "large") && OneOf(item.condition, "clean", "dirty"), "Invalid item size/condition.");
                Need(HotelSimulation.Finite(item.position) && item.holder >= -1, "Invalid item pose/holder.");
                Need(item.placedRoom == -1 || item.placedRoom == 0 || (rooms.ContainsKey(item.placedRoom) && rooms[item.placedRoom].mvp.owned), "Item placed in unavailable room.");
                Need(item.kind == "bag" ? item.ownerGuest > 0 && item.ownerGuest < state.nextGuest &&
                    (item.consumed ? !guests.ContainsKey(item.ownerGuest) || Terminal(guests[item.ownerGuest]) : guests.ContainsKey(item.ownerGuest)) : item.ownerGuest == 0,
                    "Invalid item ownership.");
                bool permanent = OneOf(item.kind, "toolbox", "mop", "plunger", "cart");
                Need(!item.consumed || (!permanent && item.holder == -1 && item.placedRoom == 0), "Consumed item retains a holder/location or is essential.");
                if (item.holder >= 0)
                    Need(!item.consumed && item.placedRoom == 0 && players.TryGetValue((ulong)item.holder, out PlayerState holder) && holder.held == item.id, "Broken item/hand link.");
                if (item.placedRoom != 0) Need(!item.consumed && item.kind == "bag" && item.holder == -1, "Only free bags may be placed or carried as cargo.");
                if (!item.consumed)
                {
                    if (item.kind == "toolbox") toolboxes++;
                    if (item.kind == "mop") mops++;
                    if (item.kind == "plunger") plungers++;
                    if (item.kind == "cart") carts++;
                    if (item.placedRoom == -1) { Need(state.cartUpgrade, "Cargo without cart upgrade."); cargo += item.size == "large" ? 2 : 1; }
                    if (item.kind == "bag")
                    {
                        Need(!bags.ContainsKey(item.ownerGuest) && !Terminal(guests[item.ownerGuest]), "Duplicate/retired guest bag.");
                        bags.Add(item.ownerGuest, item);
                    }
                }
                items.Add(item.id, item);
            }
            Need(toolboxes == (state.secondToolbox ? 2 : 1) && mops == 1 && plungers == 1 && carts == (state.cartUpgrade ? 1 : 0) && cargo <= 4, "Missing/duplicate essential tools or excessive cargo weight.");
            foreach (PlayerState player in players.Values)
            {
                ItemState held = null;
                if (!string.IsNullOrEmpty(player.held))
                    Need(items.TryGetValue(player.held, out held) && !held.consumed && held.holder == (long)player.id, "Broken hand/item link.");
                if (!string.IsNullOrEmpty(player.workTarget))
                {
                    if (state.version == 3 && HotelDangerRules.IsWorkTarget(player.workTarget))
                        HotelDangerValidation.ValidateWork(state, player, held);
                    else WorkTool(player.workTarget, held);
                }
            }
            foreach (GuestState guest in guests.Values)
            {
                if (Terminal(guest)) continue;
                Need(bags.TryGetValue(guest.id, out ItemState bag), "Missing guest luggage.");
                // Misdelivery is a recoverable gameplay mistake, never corruption or ownership transfer.
                bool delivered = guest.room > 0 && bag.placedRoom == guest.room && bag.holder == -1;
                Need(guest.luggageDelivered == delivered, "Luggage delivery projection disagrees with physical bag.");
            }
        }

        private static void Contract(MvpGuestState c)
        {
            Need(c != null, "Missing guest contract.");
            Need(OneOf(c.archetype, "tourist", "business", "family", "vip") && OneOf(c.trait, "patient", "impatient", "messy", "demanding", "friendly"), "Unknown guest archetype/trait.");
            Need(c.partySize == (c.archetype == "family" ? 2 : 1), "Invalid visible party size.");
            OptionalId(c.partyId); OptionalId(c.groupId); OptionalId(c.reservationId);
            Need(c.arrivalDay > 0 && c.departureDay > c.arrivalDay && c.departureDay < 1000000 && c.billableNights == c.departureDay - c.arrivalDay, "Invalid exclusive stay interval.");
            Need(c.agreedTariff > 0 && (long)c.agreedTariff * c.billableNights < 100000000, "Invalid agreed tariff/total.");
            Need(Quality(c.minBedQuality) && Quality(c.preferredBedQuality) && c.preferredBedQuality >= c.minBedQuality &&
                c.minTvQuality >= 0 && c.minTvQuality <= 2 && c.preferredTvQuality >= c.minTvQuality && c.preferredTvQuality <= 2, "Invalid contract quality requirements.");
            Need(Range(c.patience, .01f, 86400) && Range(c.requestDelay, 0, 86400) && Range(c.requestGrace, .01f, 86400) &&
                Range(c.noiseTolerance, 0, 1) && Range(c.messRate, 0, 100) && Clock(c.serviceElapsed) && Clock(c.blockedTime), "Invalid contract timing/needs.");
            Need(OneOf(c.preferredRequest, "towel", "coffee", "cleaning", "luggage"), "Unknown preferred request.");
        }

        private static void Hospitality(HotelState state, Dictionary<int, RoomState> rooms, Dictionary<int, GuestState> guests)
        {
            MvpHotelState m = state.mvp;
            var reservations = new Dictionary<string, MvpReservationState>(StringComparer.Ordinal);
            var schedules = new Dictionary<string, MvpScheduleState>(StringComparer.Ordinal);
            var groups = new Dictionary<string, MvpGroupState>(StringComparer.Ordinal);
            foreach (MvpGroupState group in m.groups)
            {
                Need(group != null, "Null group."); Id(group.id); Text(group.source, 40, false);
                Need(!groups.ContainsKey(group.id) && OneOf(group.status, "expected", "arriving", "staying", "cleanup", "completed"), "Invalid/duplicate group.");
                Need(group.arrivalDay > 0 && group.arrivalDay < 1000000 && group.admitted >= 0 && group.refused >= 0 && group.completed >= 0 &&
                    group.admitted <= MaxGuests && group.refused <= MaxGuests && group.completed <= group.admitted, "Invalid group counters/calendar.");
                Count(group.reservationIds, MaxReservations);
                var refs = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in group.reservationIds) { Id(id); Need(refs.Add(id), "Duplicate group reservation."); }
                groups.Add(group.id, group);
            }
            var bookingParties = new HashSet<string>(StringComparer.Ordinal);
            foreach (MvpReservationState r in m.reservations)
            {
                Need(r != null, "Null reservation."); Id(r.id); Id(r.partyId); Text(r.source, 40, false); OptionalId(r.groupId);
                Need(!reservations.ContainsKey(r.id) && bookingParties.Add(r.partyId) &&
                    OneOf(r.status, "confirmed", "arrived", "staying", "completed", "refused", "missed", "cancelled"), "Invalid/duplicate reservation/party.");
                Contract(r.contract);
                Need(r.contract.reservationId == r.id && r.contract.partyId == r.partyId && SameId(r.contract.groupId, r.groupId) &&
                    r.arrivalDay == r.contract.arrivalDay && r.departureDay == r.contract.departureDay && Range(r.arrivalTime, 0, state.dayLength), "Reservation quote/calendar mismatch.");
                Need(rooms.ContainsKey(r.room) && rooms[r.room].mvp.owned && rooms[r.room].mvp.capacity >= r.contract.partySize, "Reservation references unavailable/undersized room.");
                if (Committed(r)) Need(rooms[r.room].mvp.bedQuality >= r.contract.minBedQuality && rooms[r.room].mvp.tvQuality >= r.contract.minTvQuality,
                    "Committed reservation exceeds room quality.");
                GroupReference(r.groupId, r.arrivalDay, groups);
                Need(string.IsNullOrEmpty(r.groupId) || groups[r.groupId].reservationIds.Contains(r.id), "Group omitted its reservation.");
                Need(r.guestId == 0 || guests.ContainsKey(r.guestId), "Reservation references missing guest.");
                if (r.guestId != 0)
                {
                    GuestState g = guests[r.guestId];
                    Need(g.mvp.reservationId == r.id && SameQuote(g.mvp, r.contract), "Reservation/guest quote mismatch.");
                    if (r.status == "arrived") Need(g.stage == "queue", "Arrived reservation has no queued guest.");
                    if (r.status == "staying") Need(Occupies(g) && g.room == r.room, "Staying reservation/occupant mismatch.");
                    if (r.status == "completed") Need(g.paid && Terminal(g), "Completed reservation has unpaid/live guest.");
                    if (OneOf(r.status, "refused", "missed", "cancelled")) Need(Terminal(g), "Cancelled reservation has active guest.");
                }
                Need(r.status != "confirmed" || r.guestId == 0, "Confirmed reservation already has an arrived guest.");
                Need(!OneOf(r.status, "arrived", "staying") || r.guestId != 0, "Active reservation lost its guest.");
                reservations.Add(r.id, r);
            }
            var scheduledParties = new HashSet<string>(StringComparer.Ordinal);
            var scheduledBookings = new HashSet<string>(StringComparer.Ordinal);
            var scheduledGuests = new HashSet<int>();
            foreach (MvpScheduleState s in m.schedule)
            {
                Need(s != null, "Null arrival schedule."); Id(s.id); Text(s.source, 40, false); OptionalId(s.groupId); OptionalId(s.reservationId);
                Need(!schedules.ContainsKey(s.id) && OneOf(s.status, "planned", "arrived", "cancelled"), "Invalid/duplicate schedule row.");
                Contract(s.contract); Id(s.contract.partyId);
                Need(scheduledParties.Add(s.contract.partyId) && s.day == s.contract.arrivalDay && Range(s.time, 0, state.dayLength) &&
                    SameId(s.groupId, s.contract.groupId) && SameId(s.reservationId, s.contract.reservationId), "Schedule quote/calendar mismatch.");
                GroupReference(s.groupId, s.day, groups);
                if (!string.IsNullOrEmpty(s.reservationId))
                {
                    Need(reservations.TryGetValue(s.reservationId, out MvpReservationState r) && scheduledBookings.Add(s.reservationId), "Missing/duplicate scheduled reservation.");
                    Need(SameQuote(s.contract, r.contract) && s.guestId == r.guestId && s.day == r.arrivalDay && s.time == r.arrivalTime, "Schedule/reservation links disagree.");
                    Need(s.status != "planned" || r.status == "confirmed", "Planned arrival references inactive booking.");
                }
                Need(s.guestId == 0 || (guests.ContainsKey(s.guestId) && scheduledGuests.Add(s.guestId)), "Missing/duplicate scheduled guest.");
                Need(s.status == "arrived" || s.guestId == 0, "Unarrived schedule already has a guest.");
                if (s.guestId != 0) Need(SameQuote(s.contract, guests[s.guestId].mvp), "Scheduled and arrived contracts disagree.");
                // A terminal arrival may have guestId=0 after pruning; its immutable offer remains.
                schedules.Add(s.id, s);
            }
            foreach (GuestState guest in guests.Values)
            {
                GroupReference(guest.mvp.groupId, guest.mvp.arrivalDay, groups);
                if (!string.IsNullOrEmpty(guest.mvp.reservationId))
                    Need(reservations.TryGetValue(guest.mvp.reservationId, out MvpReservationState r) && r.guestId == guest.id && SameQuote(r.contract, guest.mvp), "Guest references missing/different reservation.");
            }
            foreach (MvpGroupState group in groups.Values)
            {
                foreach (string id in group.reservationIds)
                    Need(reservations.TryGetValue(id, out MvpReservationState r) && r.groupId == group.id, "Group references missing/different reservation.");
                var parties = new HashSet<string>(StringComparer.Ordinal);
                foreach (MvpScheduleState s in schedules.Values) if (s.groupId == group.id) parties.Add(s.contract.partyId);
                foreach (MvpReservationState r in reservations.Values) if (r.groupId == group.id) parties.Add(r.partyId);
                foreach (GuestState g in guests.Values) if (g.mvp.groupId == group.id) parties.Add(g.mvp.partyId);
                Need(parties.Count > 0 && group.admitted + group.refused <= parties.Count && group.completed <= group.admitted, "Group accounting exceeds its parties.");
                int admitted = 0, completed = 0, refused = 0;
                foreach (GuestState g in guests.Values)
                {
                    if (g.mvp.groupId != group.id) continue;
                    if (Occupies(g) || g.paid) admitted++;
                    if (g.paid) completed++;
                    if (Terminal(g) && !g.paid) refused++;
                }
                Need(group.admitted >= admitted && group.completed >= completed && group.refused >= refused, "Group counters omit current members.");
            }

            // Half-open intervals include continuing occupancy. A guest and its own reservation
            // are one commitment, while every other overlap is a double booking.
            for (int i = 0; i < m.reservations.Count; i++)
            {
                MvpReservationState a = m.reservations[i]; if (!Committed(a)) continue;
                for (int j = i + 1; j < m.reservations.Count; j++)
                {
                    MvpReservationState b = m.reservations[j];
                    Need(!Committed(b) || a.room != b.room || !Overlap(a.arrivalDay, a.departureDay, b.arrivalDay, b.departureDay), "Overlapping room reservations.");
                }
                foreach (GuestState g in guests.Values)
                    if (Occupies(g) && g.room == a.room && g.mvp.reservationId != a.id)
                        Need(!Overlap(a.arrivalDay, a.departureDay, g.mvp.arrivalDay, g.mvp.departureDay), "Reservation overlaps a continuing stay.");
            }
            RequestsComplaintsAndReviews(state, rooms, guests);
        }

        private static void RequestsComplaintsAndReviews(HotelState state, Dictionary<int, RoomState> rooms, Dictionary<int, GuestState> guests)
        {
            var requests = new Dictionary<string, MvpRequestState>(StringComparer.Ordinal);
            var services = new HashSet<string>(StringComparer.Ordinal);
            foreach (MvpRequestState r in state.mvp.requests)
            {
                Need(r != null, "Null request."); Id(r.id);
                Need(!requests.ContainsKey(r.id) && OneOf(r.kind, "towel", "coffee", "cleaning", "luggage") &&
                    OneOf(r.status, "open", "fulfilled", "cancelled") && guests.ContainsKey(r.guestId), "Invalid request/references.");
                Need(services.Add(r.guestId + ":" + r.kind), "Duplicate guest request/reward guard.");
                GuestState guest = guests[r.guestId];
                Need(Range(r.createdAt, 0, state.mvp.elapsed) && Clock(r.dueAt) && r.dueAt >= r.createdAt && (!r.rewarded || r.status == "fulfilled"), "Invalid request timing/reward.");
                string kind = r.kind == "luggage" ? "bag" : r.kind == "cleaning" ? "clean" : r.kind;
                Need(RoomTarget(r.target, rooms, kind), "Request has an invalid target.");
                if (r.status == "open") Need(Occupies(guest) && r.target == kind + "_" + guest.room, "Open request lost its active guest/room.");
                requests.Add(r.id, r);
            }
            foreach (GuestState g in guests.Values)
                Need(g.towelRequested == state.mvp.requests.Exists(r => r.guestId == g.id && r.kind == "towel" && r.status == "open"), "Towel request projection disagrees.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var causes = new HashSet<string>(StringComparer.Ordinal);
            foreach (MvpComplaintState c in state.mvp.complaints)
            {
                Need(c != null, "Null complaint."); Id(c.id); Text(c.causeKey, 256, false);
                Need(ids.Add(c.id) && guests.ContainsKey(c.guestId) && causes.Add(c.guestId + ":" + c.causeKey) &&
                    OneOf(c.category, "waiting", "luggage", "towel", "coffee", "cleaning", "dirt", "toilet", "tv", "noise") &&
                    OneOf(c.status, "active", "resolved"), "Invalid complaint/reference/cause.");
                Need(c.escalation >= 0 && c.escalation <= 2 && Clock(c.exposure) && Range(c.recoveredAt, 0, state.mvp.elapsed), "Invalid complaint recovery/escalation.");
                Need(c.status != "active" || !Terminal(guests[c.guestId]), "Terminal guest has an active complaint.");
                ComplaintCause(c, state, rooms, guests, requests);
            }
            var reviewed = new HashSet<int>();
            foreach (MvpReviewState r in state.mvp.reviews)
            {
                Need(r != null && r.guestId > 0 && r.guestId < state.nextGuest && reviewed.Add(r.guestId) &&
                    r.day > 0 && r.day <= state.day && r.stars >= 1 && r.stars <= 5, "Invalid/duplicate numeric review.");
                Text(r.text, 1024, false);
                // Numeric history deliberately outlives pruned guest records.
                if (guests.TryGetValue(r.guestId, out GuestState g)) Need(g.mvp.reviewRecorded && Terminal(g), "Review guard/terminal guest mismatch.");
            }
            foreach (GuestState g in guests.Values)
                if (g.mvp.reviewRecorded) Need(Terminal(g), "Living guest already reviewed the stay.");
        }

        private static void ComplaintCause(MvpComplaintState c, HotelState state, Dictionary<int, RoomState> rooms,
            Dictionary<int, GuestState> guests, Dictionary<string, MvpRequestState> requests)
        {
            string[] parts = c.causeKey.Split(':');
            bool active = c.status == "active", valid = false;
            if (parts.Length == 2)
            {
                if (parts[0] == "request")
                    valid = requests.TryGetValue(parts[1], out MvpRequestState r) && r.guestId == c.guestId && r.kind == c.category;
                else if (parts[0] == "wrongbag")
                {
                    Id(parts[1]);
                    ItemState bag = state.items.Find(i => i.id == parts[1]);
                    valid = c.category == "luggage" && (!active || (bag != null && bag.kind == "bag" && !bag.consumed && bag.ownerGuest != c.guestId));
                    // A resolved wrong-bag complaint can outlive the other party's removed bag.
                }
                else if (int.TryParse(parts[1], out int id) && id == c.guestId)
                    valid = (parts[0] == "luggage" && c.category == "luggage") || (OneOf(parts[0], "queue", "checkout") && c.category == "waiting");
            }
            else if (parts.Length == 3 && parts[0] == "utility" && int.TryParse(parts[2], out int episode) && episode >= 0)
                valid = (parts[1] == "water" && c.category == "toilet" && episode <= state.mvp.utilities.waterEpisode) ||
                    (parts[1] == "power" && c.category == "tv" && episode <= state.mvp.utilities.powerEpisode);
            else if (parts.Length == 3 && parts[0] == "room" && int.TryParse(parts[2], out int room))
                valid = rooms.ContainsKey(room) && rooms[room].mvp.owned &&
                    ((OneOf(parts[1], "dirt", "water") && c.category == "dirt") || (parts[1] == "towel" && c.category == "towel"));
            else if (parts.Length == 3 && parts[0] == "noise" && int.TryParse(parts[1], out int sourceRoom) && int.TryParse(parts[2], out int sourceGuest))
                valid = c.category == "noise" && rooms.ContainsKey(sourceRoom) && rooms[sourceRoom].mvp.owned && sourceGuest > 0 && sourceGuest < state.nextGuest &&
                    (!active || guests.ContainsKey(sourceGuest));
            else if (parts.Length == 4 && parts[0] == "equipment" && int.TryParse(parts[2], out int number) && int.TryParse(parts[3], out int fault) && fault >= 0 && rooms.ContainsKey(number))
            {
                MvpEquipmentState e = rooms[number].mvp.equipment.Find(x => x.kind == parts[1]);
                valid = rooms[number].mvp.owned && e != null && fault <= e.episode &&
                    ((parts[1] == "toilet" && c.category == "toilet") || (OneOf(parts[1], "tv", "lamp") && c.category == "tv"));
            }
            else if (state.version == 3 && parts.Length == 4 && parts[0] == "danger" &&
                int.TryParse(parts[2], out int hazardRoom) && int.TryParse(parts[3], out int serial) &&
                rooms.ContainsKey(hazardRoom) && rooms[hazardRoom].mvp.owned && state.danger != null)
            {
                valid = serial > 0 && serial <= state.danger.serial && parts[2] == hazardRoom.ToString() && parts[3] == serial.ToString() &&
                    ((parts[1] == "electric" && c.category == "tv") || (parts[1] == "steam" && c.category == "toilet") ||
                     (parts[1] == "fumes" && c.category == "dirt"));
                // Hospitality retains room complaints during relocation/checkout until the
                // guest reaches the new room or settles; a repair is not a recovery reward yet.
                bool deferred = active && guests.TryGetValue(c.guestId, out GuestState affected) && OneOf(affected.stage, "walking", "checkout");
                if (active) valid = valid && serial == state.danger.serial && state.danger.incidents.Exists(i =>
                    i.room == hazardRoom && i.kind == parts[1] &&
                    (i.status == "warning" || i.status == "active" || (i.status == "resolved" && deferred)));
            }
            Need(valid, "Invalid complaint cause/reference.");
        }

        private static void GroupReference(string id, int arrival, Dictionary<string, MvpGroupState> groups)
        {
            if (!string.IsNullOrEmpty(id)) Need(groups.TryGetValue(id, out MvpGroupState group) && group.arrivalDay == arrival, "Missing/mismatched group reference.");
        }
        private static bool SameId(string a, string b) { return (a ?? "") == (b ?? ""); }
        private static bool SameQuote(MvpGuestState a, MvpGuestState b)
        {
            // A walk-in gains a reservation at admission. Its earlier schedule stays immutable;
            // runtime service timers and one-time reward guards also belong to the guest alone.
            return a.archetype == b.archetype && a.trait == b.trait && SameId(a.partyId, b.partyId) && SameId(a.groupId, b.groupId) &&
                a.partySize == b.partySize && a.arrivalDay == b.arrivalDay && a.departureDay == b.departureDay && a.agreedTariff == b.agreedTariff &&
                a.billableNights == b.billableNights && a.minBedQuality == b.minBedQuality && a.minTvQuality == b.minTvQuality &&
                a.preferredBedQuality == b.preferredBedQuality && a.preferredTvQuality == b.preferredTvQuality && a.requiresWater == b.requiresWater &&
                a.requiresPower == b.requiresPower && a.patience == b.patience && a.requestDelay == b.requestDelay && a.requestGrace == b.requestGrace &&
                a.noiseTolerance == b.noiseTolerance && a.messRate == b.messRate && a.preferredRequest == b.preferredRequest;
        }
        private static bool Committed(MvpReservationState r) { return OneOf(r.status, "confirmed", "arrived", "staying"); }
        private static bool Overlap(int a, int b, int c, int d) { return a < d && c < b; }

        private static bool WorkTarget(HotelState state, string target, Dictionary<int, RoomState> rooms)
        {
            if (state.version == 3 && HotelDangerRules.IsWorkTarget(target))
                return HotelDangerValidation.Target(state, target);
            return OneOf(target, "coffee", "utility_water", "utility_power") || RoomTarget(target, rooms, "bed", "sink", "water", "trash", "toilet", "tv", "lamp", "clean", "dirtytowel");
        }

        private static void DirectorAndPings(HotelState state, Dictionary<int, RoomState> rooms)
        {
            MvpDirectorState d = state.mvp.director;
            Need(d != null && Clock(d.nextDecision) && Clock(d.globalCooldown) && Clock(d.recoveryUntil) &&
                Range(d.workload, 0, 100) && Range(d.stress, 0, 100) && OneOf(d.band, "low", "healthy", "high", "overload"), "Invalid director clocks/load.");
            Count(d.events, MaxEvents); Count(d.cooldowns, MaxCooldowns); Count(d.recent, MaxRecent);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (MvpEventState e in d.events)
            {
                Need(e != null, "Null director event."); Id(e.id);
                Need(ids.Add(e.id) && EventKind(e.kind) && e.category == (e.kind == "water" ? "operational" : "guest") &&
                    OneOf(e.status, "active", "resolved", "completed"), "Invalid/duplicate director event.");
                Need(Range(e.startedAt, 0, state.mvp.elapsed) && Clock(e.until) && e.until >= e.startedAt, "Invalid event interval.");
                Need(e.kind == "water" ? rooms.ContainsKey(e.room) && rooms[e.room].mvp.owned : e.room == 0, "Invalid event room.");
            }
            ids.Clear();
            foreach (MvpCooldownState c in d.cooldowns)
            {
                Need(c != null && OneOf(c.id, "category_guest", "category_operational", "event_rush", "event_walkin_group", "event_vip", "event_water") &&
                    ids.Add(c.id) && Clock(c.until), "Invalid/duplicate event cooldown.");
            }
            foreach (string kind in d.recent) Need(EventKind(kind), "Unknown recent event.");
            var authors = new HashSet<ulong>();
            foreach (MvpPingState p in state.mvp.pings)
            {
                Need(p != null && p.playerId <= long.MaxValue && authors.Add(p.playerId) && Range(p.until, 0, state.mvp.elapsed + 6.01f), "Invalid ping author/expiry.");
                Need(OneOf(p.target, "desk", "board", "coffee", "linen", "towels", "hamper", "bin", "tools", "utility_water", "utility_power") ||
                    RoomTarget(p.target, rooms, "bed", "sink", "water", "towel", "trash", "bag", "door", "toilet", "tv", "lamp", "coffee", "clean", "dirtytowel") ||
                    (state.version == 3 && HotelDangerValidation.Target(state, p.target)), "Unknown ping target.");
                // A marker can outlive a disconnected author. Its target and six-second expiry
                // remain authoritative; Load intentionally releases players without deleting it.
            }
        }

        private static bool EventKind(string kind) { return OneOf(kind, "rush", "walkin_group", "vip", "water"); }

        private static void WorkTool(string target, ItemState held)
        {
            string kind = target.Split('_')[0];
            if (kind == "sink" || kind == "tv" || kind == "lamp" || kind == "utility") Need(held != null && held.kind == "toolbox", "Repair lease lost its toolbox.");
            if (kind == "water" || kind == "clean") Need(held != null && held.kind == "mop", "Cleaning lease lost its mop.");
            if (kind == "toilet") Need(held != null && held.kind == "plunger", "Toilet lease lost its plunger.");
            if (kind == "coffee" || kind == "trash" || kind == "dirtytowel") Need(held == null, "Collection/brewing lease needs an empty hand.");
            if (kind == "bed") Need(held == null || (held.kind == "linen" && held.condition == "clean"), "Bed lease has incompatible item.");
        }

        private static bool RoomTarget(string target, Dictionary<int, RoomState> rooms, params string[] kinds)
        {
            if (string.IsNullOrEmpty(target)) return false;
            int split = target.LastIndexOf('_');
            if (split <= 0 || !int.TryParse(target.Substring(split + 1), out int room)) return false;
            return rooms.ContainsKey(room) && rooms[room].mvp.owned && target.Substring(split + 1) == room.ToString() && OneOf(target.Substring(0, split), kinds);
        }

        private static bool Occupies(GuestState g) { return OneOf(g.stage, "walking", "staying", "checkout"); }
        private static bool Terminal(GuestState g) { return OneOf(g.stage, "leaving", "gone"); }
        private static bool Quality(int value) { return value >= 1 && value <= 2; }
        private static bool Stock(int value) { return value >= 0 && value <= 10000; }
        private static bool Clock(float value) { return Range(value, 0, MaxClock); }
        private static bool Range(float value, float min, float max) { return HotelSimulation.Finite(value) && value >= min && value <= max; }
        private static bool OneOf(string value, params string[] choices) { foreach (string c in choices) if (value == c) return true; return false; }
        private static void Id(string value) { Text(value, 128, false); Need(!string.IsNullOrWhiteSpace(value), "Blank record ID."); }
        private static void OptionalId(string value) { if (!string.IsNullOrEmpty(value)) Id(value); }
        private static void Text(string value, int max, bool optional = true) { Need((optional && value == null) || (value != null && value.Length <= max && (optional || value.Length > 0)), "Missing/oversized text."); }
        private static void Count<T>(List<T> values, int max) { Need(values != null && values.Count <= max, "Missing/oversized collection."); }
        private static void Strings(List<string> values, int max, int length) { Count(values, max); foreach (string value in values) Text(value, length, false); }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
