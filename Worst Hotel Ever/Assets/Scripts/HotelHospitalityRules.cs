using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed class MvpGuestIssue
    {
        public string category, causeKey, text;
        public MvpGuestIssue(string category, string causeKey, string text)
        { this.category = category; this.causeKey = causeKey; this.text = text; }
    }

    // Read-only projections: safe for replaced client snapshots and for an in-place host state.
    public static class HotelHospitalityRules
    {
        public const int MaxGuests = 24, MaxReservations = 96, MaxSchedule = 96, MaxGroups = 16;
        public static bool Occupying(GuestState g)
        { return g != null && (g.stage == "walking" || g.stage == "staying" || g.stage == "checkout"); }
        public static bool Committed(MvpReservationState r)
        { return r != null && (r.status == "confirmed" || r.status == "arrived" || r.status == "staying"); }
        public static bool Overlap(int a, int b, int c, int d) { return a < d && c < b; }

        public static string BookingBlockReason(HotelState s, MvpGuestState c, int number,
            string ignoreReservation = "", int ignoreGuest = 0)
        {
            RoomState r = s == null ? null : s.rooms.Find(x => x.number == number);
            if (s == null || s.mvp == null || r == null || r.mvp == null || !r.mvp.owned) return "Номер ещё не открыт.";
            if (r.outOfService) return "Номер закрыт для новых продаж.";
            if (c == null || c.arrivalDay < 1 || c.departureDay <= c.arrivalDay) return "Неверный срок проживания.";
            if (r.mvp.capacity < c.partySize) return "Нужна вместимость " + c.partySize + " чел.; в номере " + r.mvp.capacity + ".";
            if (r.mvp.bedQuality < c.minBedQuality) return "Гостю нужна кровать качества " + c.minBedQuality + ".";
            MvpEquipmentState tv = Equipment(r, "tv");
            if (c.minTvQuality > 0 && (tv == null || !tv.installed || r.mvp.tvQuality < c.minTvQuality))
                return "Гостю нужен установленный TV качества " + c.minTvQuality + ".";
            foreach (MvpReservationState booking in s.mvp.reservations)
                if (Committed(booking) && booking.id != ignoreReservation && booking.room == number &&
                    Overlap(c.arrivalDay, c.departureDay, booking.arrivalDay, booking.departureDay))
                    return "Номер забронирован на пересекающийся срок.";
            foreach (GuestState g in s.guests)
                if (g.id != ignoreGuest && Occupying(g) && g.room == number && g.mvp != null &&
                    Overlap(c.arrivalDay, c.departureDay, g.mvp.arrivalDay, g.mvp.departureDay))
                    return "В этом интервале номер занят проживающим гостем.";
            return "";
        }

        public static string AssignmentBlockReason(HotelState s, int guestId, int number)
        {
            if (s == null || s.mvp == null || s.phase != "open") return "Заселение доступно в открытую смену.";
            GuestState g = s.guests.Find(x => x.id == guestId);
            if (g == null || g.mvp == null || g.stage != "queue") return "Выберите ожидающего гостя.";
            if (s.day >= g.mvp.departureDay) return "Срок предложения уже закончился.";
            string result = RoomForGuestBlockReason(s, g, number, false);
            return result;
        }

        public static string RoomForGuestBlockReason(HotelState s, GuestState g, int number, bool relocating)
        {
            if (g == null || g.mvp == null) return "Гость не найден.";
            string reason = BookingBlockReason(s, g.mvp, number, g.mvp.reservationId, g.id);
            if (reason != "") return reason;
            RoomState r = s.rooms.Find(x => x.number == number);
            if (r.guestId != 0) return "Номер уже занят.";
            if (relocating && number == g.room) return "Гость уже живёт в этом номере.";
            if (r.bed != 2) return "Сначала застелите чистую кровать.";
            if (r.leak || r.water > .65f) return "Сначала устраните аварийное состояние номера.";
            if (g.mvp.requiresWater && (s.mvp.utilities.waterFault || !Effective(s, r, "sink") || !Effective(s, r, "toilet")))
                return "Гостю нужна исправная вода, раковина и туалет.";
            if (g.mvp.requiresPower && s.mvp.utilities.powerFault) return "Гостю нужно электричество.";
            if (g.mvp.minTvQuality > 0 && !Effective(s, r, "tv")) return "Обещанный TV сейчас не работает.";
            return "";
        }

        public static MvpEquipmentState Equipment(RoomState room, string kind)
        { return room == null || room.mvp == null ? null : room.mvp.equipment.Find(x => x.kind == kind); }
        public static bool Effective(HotelState s, RoomState r, string kind)
        {
            MvpEquipmentState e = Equipment(r, kind);
            if (e == null || !e.installed || e.localFault) return false;
            return kind == "sink" || kind == "toilet" ? !s.mvp.utilities.waterFault : !s.mvp.utilities.powerFault;
        }
        public static bool CleanForRequest(RoomState r)
        { return r != null && r.mvp != null && r.mvp.dirt <= .05f && r.mvp.binFill <= .05f && !r.trash && r.mvp.dirtyTowels == 0 && r.water <= .01f; }
        public static bool BagDelivered(HotelState s, GuestState g)
        {
            bool found = false;
            foreach (ItemState bag in s.items)
            {
                if (bag.kind != "bag" || bag.ownerGuest != g.id || bag.consumed) continue;
                found = true;
                if (!Occupying(g) || bag.holder >= 0 || bag.placedRoom != g.room) return false;
            }
            return found;
        }

        public static List<MvpGuestIssue> Issues(HotelState s, GuestState g)
        {
            var list = new List<MvpGuestIssue>();
            if (s == null || s.mvp == null || g == null || g.mvp == null || g.paid || g.stage == "gone" || g.stage == "leaving") return list;
            if (g.stage == "queue" && g.waited >= g.mvp.patience * .45f)
                Add(list, "waiting", "queue:" + g.id, "Долго жду заселения");
            if (g.stage == "checkout" && g.waited >= 45)
                Add(list, "waiting", "checkout:" + g.id, "Долго жду оформления выезда");
            if (Occupying(g))
            {
                bool wrong = s.items.Exists(i => !i.consumed && i.kind == "bag" && i.ownerGuest == g.id && i.placedRoom > 0 && i.placedRoom != g.room);
                bool lateBag = s.mvp.requests.Exists(r => r.guestId == g.id && r.kind == "luggage" && r.status == "open" && s.mvp.elapsed >= r.dueAt);
                if (!BagDelivered(s, g) && (wrong || lateBag || g.mvp.serviceElapsed >= g.mvp.requestGrace))
                    Add(list, "luggage", "luggage:" + g.id, wrong ? "Мой багаж в чужом номере" : "Багаж ещё не доставлен");
                foreach (MvpRequestState request in s.mvp.requests)
                    if (request.guestId == g.id && request.status == "open" && request.kind != "luggage" && s.mvp.elapsed >= request.dueAt)
                        Add(list, request.kind, "request:" + request.id, "Не выполнен запрос: " + RequestName(request.kind));
            }
            if (g.stage != "staying") return list; // A walking guest has not encountered room conditions yet.
            RoomState room = s.rooms.Find(x => x.number == g.room);
            if (room == null || room.mvp == null) return list;
            string n = ":" + room.number;
            if (room.mvp.dirt >= .35f || room.mvp.binFill >= .75f || (room.trash && room.mvp.binFill <= .001f) || room.mvp.dirtyTowels > 0)
                Add(list, "dirt", "room:dirt" + n, "В номере грязно");
            if (room.water > .2f) Add(list, "dirt", "room:water" + n, "Мокрый пол");
            if (!room.towel && !s.mvp.requests.Exists(x => x.guestId == g.id && x.kind == "towel" && x.status == "open"))
                Add(list, "towel", "room:towel" + n, "Нет чистого полотенца");
            if (s.mvp.utilities.waterFault)
                Add(list, "toilet", "utility:water:" + s.mvp.utilities.waterEpisode, "Нет общей воды");
            if (s.mvp.utilities.powerFault)
                Add(list, "tv", "utility:power:" + s.mvp.utilities.powerEpisode, "Нет электричества");
            foreach (string kind in new[] { "toilet", "tv", "lamp" })
            {
                MvpEquipmentState e = Equipment(room, kind);
                if (e != null && e.installed && e.localFault)
                    Add(list, kind == "toilet" ? "toilet" : "tv", "equipment:" + kind + n + ":" + e.episode,
                        kind == "toilet" ? "Сломан туалет" : kind == "tv" ? "Сломан TV" : "Сломана лампа");
            }
            foreach (ItemState bag in s.items)
                if (!bag.consumed && bag.kind == "bag" && bag.placedRoom == room.number && bag.ownerGuest != g.id)
                    Add(list, "luggage", "wrongbag:" + bag.id, "В номере чужой багаж");
            foreach (RoomState source in s.rooms)
            {
                if (source.mvp == null || !source.mvp.owned || source.number == room.number || source.guestId == 0) continue;
                if (!s.guests.Exists(guest => guest.id == source.guestId && guest.stage == "staying")) continue;
                float dz = Math.Abs(HotelLayout.RoomCenter(source.number).z - HotelLayout.RoomCenter(room.number).z);
                if (dz <= 7.01f && source.mvp.noise * (dz < 1 ? .8f : .55f) > g.mvp.noiseTolerance)
                    Add(list, "noise", "noise:" + source.number + ":" + source.guestId, "Шумят соседи из " + source.number);
            }
            return list;
        }

        private static void Add(List<MvpGuestIssue> issues, string category, string cause, string text)
        { if (!issues.Exists(x => x.causeKey == cause)) issues.Add(new MvpGuestIssue(category, cause, text)); }
        public static string GuestIssues(HotelState s, GuestState g)
        { return string.Join("; ", Issues(s, g).ConvertAll(x => x.text).ToArray()); }
        public static string RequestName(string kind)
        { switch (kind) { case "coffee": return "кофе"; case "cleaning": return "уборка"; case "luggage": return "багаж"; default: return "полотенце"; } }
        public static List<string> Tasks(HotelState s)
        {
            var tasks = new List<string>();
            if (s == null || s.mvp == null) return tasks;
            foreach (GuestState g in s.guests)
            {
                if (g.stage == "queue") tasks.Add("Заселить: " + g.name + (g.mvp != null && g.mvp.partySize > 1 ? " · 2 человека" : ""));
                if (g.stage == "checkout") tasks.Add("Оформить выезд: " + g.name);
            }
            foreach (MvpRequestState r in s.mvp.requests)
                if (r.status == "open")
                {
                    GuestState g = s.guests.Find(x => x.id == r.guestId);
                    if (g != null) tasks.Add(RequestName(r.kind) + ": " + g.name + " → " + g.room);
                }
            foreach (MvpComplaintState c in s.mvp.complaints)
                if (c.status == "active")
                {
                    GuestState g = s.guests.Find(x => x.id == c.guestId);
                    if (g != null) tasks.Add("Жалоба: " + g.name + " · " + c.category + (c.escalation > 0 ? " · требует внимания" : ""));
                }
            return tasks;
        }
    }
}
