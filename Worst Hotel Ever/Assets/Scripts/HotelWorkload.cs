using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    // A read-only estimate of actionable work, not a count of UI task/complaint rows.
    public static class HotelWorkload
    {
        public static float Measure(HotelState state)
        {
            if (state == null || state.mvp == null) return 0;
            var work = new Dictionary<string, float>(StringComparer.Ordinal);
            MvpUtilityState utilities = state.mvp.utilities;
            if (utilities != null && utilities.waterFault) Add(work, "utility:water", 2.4f);
            if (utilities != null && utilities.powerFault) Add(work, "utility:power", 2.2f);

            foreach (RoomState room in state.rooms)
            {
                if (!Owned(room)) continue;
                float distance = Distance(room);
                // Out-of-service rooms still contain real cleanup; unowned rooms do not.
                if (room.guestId == 0 && room.bed != 2)
                    Add(work, Key("bed", room.number), distance * (room.bed == 1 ? 1.8f : 1.1f));
                if (!room.towel) Add(work, Key("towel", room.number), distance * .9f);
                if (room.trash || room.mvp.binFill > .05f)
                    Add(work, Key("trash", room.number), distance * (.7f + .4f * Mathf.Clamp01(room.mvp.binFill)));
                if (room.mvp.dirt > .05f)
                    Add(work, Key("clean", room.number), distance * (.7f + Mathf.Clamp01(room.mvp.dirt)));
                if (room.mvp.dirtyTowels > 0)
                    Add(work, Key("dirtytowel", room.number), distance * (.4f + .3f * Math.Min(room.mvp.dirtyTowels, 6)));
                if (room.water > .001f)
                    Add(work, Key("water", room.number), distance * (.7f + 1.3f * Mathf.Clamp01(room.water)));
                if (room.leak) Add(work, Key("sink", room.number), distance * 1.7f);
                if (room.mvp.equipment != null)
                    foreach (MvpEquipmentState equipment in room.mvp.equipment)
                    {
                        if (equipment == null || !equipment.installed || !equipment.localFault) continue;
                        float cost = equipment.kind == "sink" || equipment.kind == "toilet" ? 1.7f : 1;
                        Add(work, Key(equipment.kind, room.number), distance * cost);
                    }
            }

            foreach (GuestState guest in state.guests)
            {
                if (!Active(guest)) continue;
                float complexity = guest.mvp == null ? 0 : guest.mvp.archetype == "vip" ? .35f :
                    guest.mvp.archetype == "family" ? .25f : guest.mvp.archetype == "business" ? .15f : 0;
                Add(work, Key("presence", guest.id), .15f + complexity);
                if (guest.stage == "queue")
                {
                    float patience = guest.mvp == null ? 180 : Math.Max(1, guest.mvp.patience);
                    Add(work, Key("queue", guest.id), 1.2f + .8f * Mathf.Clamp01(guest.waited / patience));
                }
                else if (guest.stage == "checkout") Add(work, Key("checkout", guest.id), 1.1f);
                else
                {
                    RoomState room = state.rooms.Find(r => r.number == guest.room);
                    if (!guest.luggageDelivered) Add(work, Key("bag", guest.id), Distance(room));
                    if (guest.towelRequested) Add(work, Key("towel", guest.room), Distance(room) * .9f);
                }
            }

            foreach (MvpRequestState request in state.mvp.requests)
            {
                if (request == null || request.status != "open") continue;
                GuestState guest = state.guests.Find(g => g.id == request.guestId);
                if (!Active(guest)) continue;
                RoomState room = state.rooms.Find(r => r.number == guest.room);
                string key = RequestKey(guest, room, request.kind);
                float cost = request.kind == "coffee" ? 1.2f : request.kind == "towel" ? .9f : 1;
                float urgency = state.mvp.elapsed > request.dueAt ? 1.35f : 1;
                Add(work, key, Distance(room) * cost * urgency);
            }

            foreach (ItemState item in state.items)
            {
                if (item == null || item.consumed) continue;
                if (item.kind == "dirtylinen" || item.kind == "trashbag" || item.kind == "dirtytowel" ||
                    (item.kind == "towel" && item.condition == "dirty"))
                    Add(work, "dispose:" + item.id, .55f);
            }

            // Multiple guests can describe one failed water main. Merge those rows with
            // the physical repair, while preserving distinct puddles, towels and deliveries.
            foreach (MvpComplaintState complaint in state.mvp.complaints)
            {
                if (complaint == null || complaint.status != "active") continue;
                GuestState guest = state.guests.Find(g => g.id == complaint.guestId);
                if (!Active(guest)) continue;
                string key = ComplaintKey(state, guest, complaint);
                float urgency = .65f + .25f * Mathf.Clamp(complaint.escalation, 0, 2);
                Add(work, key, urgency);
            }

            // Events are context, not another copy of the guests/faults they introduced.
            float total = 0;
            foreach (float cost in work.Values) total += cost;
            int players = Mathf.Clamp(state.players.Count, 1, 2);
            float capacity = 12 + (players - 1) * 8;
            if (state.secondToolbox && players > 1) capacity += 1.2f;
            if (state.cartUpgrade) capacity += 1.5f;
            if (state.mvp.coffeeMachine) capacity += .8f;
            if (state.mvp.betterLinen) capacity += .5f;
            return Mathf.Clamp(total * 100 / capacity, 0, 100);
        }

        private static string ComplaintKey(HotelState state, GuestState guest, MvpComplaintState complaint)
        {
            string cause = complaint.causeKey ?? "";
            string category = complaint.category ?? "";
            if (UtilityCause(cause, "water") ||
                (category == "water" && state.mvp.utilities != null && state.mvp.utilities.waterFault)) return "utility:water";
            if (UtilityCause(cause, "power") ||
                (category == "power" && state.mvp.utilities != null && state.mvp.utilities.powerFault)) return "utility:power";
            // Use the actual source, not the complaint's display category: a lamp complaint
            // is categorized as "tv", and a wet-floor complaint is categorized as "dirt".
            string[] parts = cause.Split(':');
            if (parts.Length >= 3 && parts[0] == "equipment" && int.TryParse(parts[2], out int equipmentRoom))
                return Key(parts[1], equipmentRoom);
            if (parts.Length == 3 && parts[0] == "room" && int.TryParse(parts[2], out int sourceRoom))
            {
                RoomState source = state.rooms.Find(r => r.number == sourceRoom);
                return parts[1] == "dirt" ? CleanupKey(source, sourceRoom) : Key(parts[1], sourceRoom);
            }
            if (parts.Length == 2 && parts[0] == "wrongbag")
            {
                ItemState bag = state.items.Find(i => i.id == parts[1] && !i.consumed);
                if (bag != null) return Key("bag", bag.ownerGuest);
            }
            if (parts.Length == 2 && parts[0] == "request")
            {
                MvpRequestState request = state.mvp.requests.Find(r => r.id == parts[1]);
                if (request != null)
                    return RequestKey(guest, state.rooms.Find(r => r.number == guest.room), request.kind);
            }
            // Keep noise and other unresolved causes keyed by their source, not observer.
            if (category == "luggage") return Key("bag", guest.id);
            if (category == "towel" || category == "towels") return Key("towel", guest.room);
            if (category == "coffee") return Key("coffee", guest.id);
            if (category == "cleaning" || category == "dirt") return CleanupKey(state.rooms.Find(r => r.number == guest.room), guest.room);
            if (category == "sink" || category == "toilet" || category == "tv" || category == "lamp" || category == "trash")
                return Key(category, guest.room);
            if (category == "queue" || category == "waiting")
                return Key(guest.stage == "checkout" ? "checkout" : "queue", guest.id);
            return "cause:" + (cause.Length != 0 ? cause : complaint.id ?? (category + ":" + guest.id));
        }

        private static bool UtilityCause(string cause, string utility)
        {
            string colon = "utility:" + utility, underscore = "utility_" + utility;
            return cause == colon || cause.StartsWith(colon + ":", StringComparison.Ordinal) ||
                cause == underscore || cause.StartsWith(underscore + ":", StringComparison.Ordinal) ||
                cause.StartsWith(underscore + "_", StringComparison.Ordinal);
        }

        private static string RequestKey(GuestState guest, RoomState room, string kind)
        {
            return kind == "luggage" ? Key("bag", guest.id) : kind == "towel" ? Key("towel", guest.room) :
                kind == "cleaning" ? CleanupKey(room, guest.room) : Key("coffee", guest.id);
        }
        private static string CleanupKey(RoomState room, int number)
        {
            if (room?.mvp != null)
            {
                if (room.mvp.dirt > .05f) return Key("clean", number);
                if (room.water > .001f) return Key("water", number);
                if (room.mvp.dirtyTowels > 0) return Key("dirtytowel", number);
                if (room.trash || room.mvp.binFill > .05f) return Key("trash", number);
            }
            return Key("clean", number);
        }

        private static bool Active(GuestState guest)
        {
            return guest != null && (guest.stage == "queue" || guest.stage == "walking" || guest.stage == "staying" || guest.stage == "checkout");
        }
        private static bool Owned(RoomState room) { return room != null && room.mvp != null && room.mvp.owned; }
        private static float Distance(RoomState room) { return room == null ? 1 : 1 + Mathf.Clamp((room.number - 101) / 2, 0, 2) * .08f; }
        private static string Key(string kind, int id) { return kind + ":" + id; }
        private static void Add(Dictionary<string, float> work, string key, float cost)
        {
            if (!work.TryGetValue(key, out float previous) || previous < cost) work[key] = cost;
        }
    }
}
