using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    // Read-only presentation of the replicated world. No rewards, work leases or state changes here.
    public static class HotelPresentation
    {
        public static string ShortCheckInReason(string reason)
        {
            if(string.IsNullOrEmpty(reason))return "";
            if(reason.Contains("открытую смену"))return "Смена закрыта";
            if(reason.Contains("уже занят"))return "Занят гостем";
            if(reason.Contains("закрыт для продаж"))return "Закрыт для продаж";
            if(reason.Contains("кровать"))return "Нужно чистое бельё";
            if(reason.Contains("аварийное"))return "Течь / мокрый пол";
            if(reason.Contains("очереди"))return "Нет очереди";
            return reason;
        }
        public static string RoomProblems(RoomState room)
        {
            var problems = new List<string>();
            if (room.outOfService) problems.Add("Закрыт для продаж");
            if (room.bed != 2) problems.Add(room.guestId != 0 ? "Бельё — после выезда" : room.bed == 1 ? "Грязное бельё" : "Нет белья");
            if (!room.towel) problems.Add("Нет полотенца");
            if (room.trash) problems.Add("Мусор");
            if (room.leak) problems.Add("Течь");
            if (room.water > .001f) problems.Add("Мокрый пол");
            return problems.Count == 0 ? (room.guestId == 0 ? "Готов к приёму гостя" : "Гость отдыхает") : string.Join(" · ", problems);
        }

        public static string HeldLabel(HotelState state, ItemState item)
        {
            if (item == null) return "Руки свободны";
            string label = HotelGame.ItemName(item.kind);
            if (item.kind == "bag")
            {
                GuestState owner = state.guests.Find(g => g.id == item.ownerGuest);
                label += owner == null ? " · владелец не найден" : " · " + owner.name + (owner.room == 0 ? " · ещё не заселён" : " → № " + owner.room);
            }
            if (item.kind == "cart") label += " · багаж " + state.items.FindAll(i => !i.consumed && i.placedRoom == -1).Count + "/2";
            return label;
        }

        public static string FocusText(HotelState state, ulong playerId, string targetId, string fallback)
        {
            if (state == null || string.IsNullOrEmpty(targetId)) return fallback;
            PlayerState player = state.players.Find(p => p.id == playerId);
            ItemState held = state.items.Find(i => !i.consumed && i.id == player?.held);
            ItemState item = state.items.Find(i => !i.consumed && i.id == targetId);
            if (item != null)
            {
                string label = HeldLabel(state, item);
                if (item.holder >= 0) return label + " · у сотрудника";
                if (held?.kind == "cart" && item.kind == "bag") return label + " · загрузить в тележку";
                return label + (held == null ? " · взять" : " · сначала Q: освободить руки");
            }
            if (targetId == "linen" || targetId == "towels")
            {
                int stock = targetId == "linen" ? state.linenStock : state.towelStock;
                string label = targetId == "linen" ? "Чистое бельё" : "Полотенца";
                return label + " · осталось " + stock + (held != null ? " · сначала освободите руки" : stock == 0 ? " · пополнение между сменами" : " · взять");
            }
            if (targetId == "hamper") return held?.kind == "dirtylinen" ? "Сдать грязное бельё" : "Корзина для грязного белья";
            if (targetId == "bin") return held?.kind == "trashbag" ? "Выбросить мешок мусора" : "Контейнер для мешков мусора";
            if (targetId == "desk") return "Ресепшен · регистрация и управление сменой";
            if (targetId == "board") return "Доска управления · улучшения между сменами";
            string[] parts = targetId.Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int number)) return fallback;
            RoomState room = state.rooms.Find(r => r.number == number);
            if (room == null) return fallback;
            if (state.players.Exists(p => p.id != playerId && p.workTarget == targetId)) return "Здесь уже работает коллега";
            switch (parts[0])
            {
                case "bed":
                    if (room.guestId != 0) return "Кровать занята · дождитесь выезда гостя";
                    if (room.bed == 2) return "Кровать чистая · ничего делать не нужно";
                    if (room.bed == 1) return held == null ? "Удерживайте E: снять грязное бельё" : "Грязное бельё · нужны свободные руки";
                    return held?.kind == "linen" ? "Удерживайте E: застелить кровать" : "Пустая кровать · принесите чистое бельё со склада";
                case "sink": return !room.leak ? "Раковина исправна" : held?.kind == "toolbox" ? "Удерживайте E: починить раковину" : "Течь · принесите ящик инструментов";
                case "water": return room.water <= .001f ? "Пол сухой" : held?.kind == "mop" ? "Удерживайте E: вытереть воду" : "Мокрый пол · принесите швабру";
                case "trash": return !room.trash ? "Мусора нет" : held == null ? "Удерживайте E: собрать мусор" : "Мусор · нужны свободные руки";
                case "towel":
                    var guest = state.guests.Find(g => g.id == room.guestId);
                    if (room.towel && guest?.towelRequested != true) return "Полотенце есть · дополнительного запроса нет";
                    return held?.kind == "towel" ? "Доставить полотенце в № " + number : "Нужно полотенце · возьмите его на складе";
                case "bag":
                    if (held?.kind == "cart") return "Выгрузить багаж гостя № " + number;
                    if (held?.kind != "bag") return "Багажная подставка № " + number;
                    return room.guestId == held.ownerGuest ? "Доставить чемодан в № " + number : "Чужой номер · проверьте владельца чемодана";
                case "door": return "Номер " + number + " · посмотреть состояние";
                default: return fallback;
            }
        }

        public static string GuestIssues(HotelState state, GuestState guest)
        {
            var lines = new List<string>();
            if (guest.stage == "queue") lines.Add("Ждёт заселения у стойки. Выберите доступный номер.");
            if (guest.stage == "walking" || guest.stage == "staying")
            {
                if (!guest.luggageDelivered) lines.Add("Нужен собственный чемодан → багажная подставка № " + guest.room + ".");
                if (guest.towelRequested) lines.Add("Дополнительное полотенце со склада → стойка в номере.");
                RoomState room = state.rooms.Find(r => r.number == guest.room);
                if (room?.leak == true) lines.Add("Течёт раковина: инструменты + удерживать E. Потом вытереть пол шваброй.");
                if (room != null && room.water > .001f && !room.leak) lines.Add("Источник течи устранён, но вода осталась: нужна швабра.");
                if (room?.trash == true) lines.Add("В номере мусор: собрать свободными руками и отнести в бак.");
            }
            if (guest.stage == "checkout") lines.Add("Идёт к стойке / ожидает оплату. Выезд оформляется у ресепшена.");
            if (guest.stage == "gone" || guest.stage == "leaving") lines.Add("Гость уже уехал. Его впечатления остались в отзыве.");
            if (lines.Count == 0) lines.Add("Открытых запросов нет.");
            lines.Add("Компенсация: 25 ₽, один раз. Улучшает впечатление, но не устраняет проблему.");
            return string.Join("\n\n", lines);
        }

        public static bool TryTarget(HotelState state, string targetId, out Vector3 position)
        {
            position = Vector3.zero;
            if (state == null || string.IsNullOrEmpty(targetId)) return false;
            var item = state.items.Find(i => i.id == targetId && !i.consumed);
            if (item != null) { position = item.position; return true; }
            if (targetId.StartsWith("guest_", StringComparison.Ordinal) && int.TryParse(targetId.Substring(6), out int id))
            {
                var guest = state.guests.Find(g => g.id == id && g.stage != "gone");
                if (guest == null) return false;
                position = guest.position + Vector3.up; return true;
            }
            if (targetId == "desk" || targetId == "board" || targetId == "linen" || targetId == "towels" || targetId == "hamper" || targetId == "bin" || targetId == "tools")
            { position = HotelLayout.Target(targetId); return true; }
            string[] parts = targetId.Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int number) || !state.rooms.Exists(r => r.number == number)) return false;
            if (parts[0] != "bed" && parts[0] != "sink" && parts[0] != "water" && parts[0] != "trash" && parts[0] != "towel" && parts[0] != "bag" && parts[0] != "door") return false;
            position = HotelLayout.Target(targetId); return true;
        }
    }
}
