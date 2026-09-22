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
            if (room.mvp != null && !room.mvp.owned) return "Не приобретён · покупка у доски управления";
            var problems = new List<string>();
            if (room.outOfService) problems.Add("Закрыт для продаж");
            if (room.bed != 2) problems.Add(room.guestId != 0 ? "Бельё — после выезда" : room.bed == 1 ? "Грязное бельё" : "Нет белья");
            if (!room.towel) problems.Add("Нет полотенца");
            if (room.trash) problems.Add("Мусор");
            if (room.leak) problems.Add("Течь");
            if (room.water > .001f) problems.Add("Мокрый пол");
            if (room.mvp != null)
            {
                if (room.mvp.dirt > .001f) problems.Add("Грязь " + Percent(room.mvp.dirt));
                if (room.mvp.dirtyTowels > 0) problems.Add("Грязных полотенец: " + room.mvp.dirtyTowels);
                if (room.mvp.binFill > .001f) problems.Add("Корзина " + Percent(room.mvp.binFill));
                foreach (var equipment in room.mvp.equipment)
                    if (equipment.localFault) problems.Add(EquipmentName(equipment.kind) + ": поломка");
            }
            if(problems.Count == 0 && room.mvp != null)return "Локальных проблем нет · проверьте общие системы и требования гостя";
            return problems.Count == 0 ? (room.guestId == 0 ? "Готов к приёму гостя" : "Гость отдыхает") : string.Join(" · ", problems);
        }

        public static string HeldLabel(HotelState state, ItemState item)
        {
            if (item == null) return "Руки свободны";
            string label = state.mvp == null ? HotelGame.ItemName(item.kind) : ItemLabel(item.kind);
            if (state.mvp != null && item.kind == "towel" && item.condition == "dirty") label = "Грязное полотенце";
            if (item.kind == "bag")
            {
                GuestState owner = state.guests.Find(g => g.id == item.ownerGuest);
                label += owner == null ? " · владелец не найден" : " · " + owner.name + (owner.room == 0 ? " · ещё не заселён" : " → № " + owner.room);
            }
            if (item.kind == "cart") label += state.mvp == null ? " · багаж " + state.items.FindAll(i => !i.consumed && i.placedRoom == -1).Count + "/2" :
                " · занято мест " + HotelOperationsRules.CartLoad(state) + "/" + HotelOperationsRules.CartCapacity + " · большой чемодан = 2 места";
            if (state.mvp != null && item.kind == "bag") label += item.size == "large" ? " · большой" : " · малый";
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
                if (held?.kind == "cart" && item.kind == "bag")
                {
                    string block = state.mvp == null ? "" : HotelOperationsRules.CartLoadBlockReason(state, item);
                    return label + (block == "" ? " · загрузить в тележку" : " · " + block);
                }
                return label + (held == null ? " · взять" : " · сначала Q: освободить руки");
            }
            if (state.mvp != null)
            {
                string mvpText = MvpFocusText(state, playerId, targetId, held);
                if (mvpText != null) return mvpText;
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
            if (state.mvp != null) return HotelHospitalityRules.GuestIssues(state, guest);
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
            if (targetId == "desk" || targetId == "board" || targetId == "linen" || targetId == "towels" || targetId == "hamper" || targetId == "bin" || targetId == "tools" ||
                (state.mvp != null && (targetId == "coffee" || targetId == "utility_water" || targetId == "utility_power")))
            { position = HotelLayout.Target(targetId); return true; }
            string[] parts = targetId.Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int number) || !state.rooms.Exists(r => r.number == number)) return false;
            if (state.mvp != null && state.rooms.Find(r => r.number == number)?.mvp?.owned == false && parts[0] != "door") return false;
            bool extra = state.mvp != null && (parts[0] == "toilet" || parts[0] == "tv" || parts[0] == "lamp" || parts[0] == "coffee" || parts[0] == "clean" || parts[0] == "dirtytowel");
            if (!extra && parts[0] != "bed" && parts[0] != "sink" && parts[0] != "water" && parts[0] != "trash" && parts[0] != "towel" && parts[0] != "bag" && parts[0] != "door") return false;
            position = HotelLayout.Target(targetId); return true;
        }

        public static string Percent(float value) { return Mathf.RoundToInt(Mathf.Clamp01(value) * 100) + "%"; }
        public static string ItemLabel(string kind)
        {
            switch (kind) { case "coffee": return "Чашка кофе"; case "dirtytowel": return "Грязное полотенце"; case "plunger": return "Вантуз"; default: return HotelGame.ItemName(kind); }
        }
        public static string EquipmentName(string kind)
        {
            switch (kind) { case "sink": return "Раковина"; case "toilet": return "Туалет"; case "tv": return "Телевизор"; case "lamp": return "Лампа"; default: return kind ?? "Оборудование"; }
        }
        public static string ArchetypeName(string value)
        {
            switch (value) { case "tourist": return "Турист"; case "business": return "Деловой гость"; case "family": return "Семья"; case "vip": return "VIP"; default: return value ?? "Гость"; }
        }
        public static string TraitName(string value)
        {
            switch (value) { case "patient": return "Терпеливый"; case "impatient": return "Нетерпеливый"; case "messy": return "Неряшливый"; case "demanding": return "Требовательный"; case "friendly": return "Дружелюбный"; default: return value ?? ""; }
        }
        public static string RequestName(string value)
        {
            switch (value) { case "towel": return "Полотенце"; case "coffee": return "Кофе"; case "cleaning": return "Уборка"; case "luggage": return "Багаж"; default: return value ?? "Запрос"; }
        }
        public static string PriceName(string value) { return value == "low" ? "Низкие" : value == "high" ? "Высокие" : "Обычные"; }
        public static string FinishName(string value) { return value == "warm" ? "Тёплая" : value == "cool" ? "Холодная" : "Исходная"; }
        public static string SourceName(string value)
        {
            switch (value) { case "booking": return "Бронирование"; case "planned": return "Плановая группа"; case "walkin": return "С улицы"; case "walkin_group": return "Внеплановая группа"; case "group": return "Группа"; case "vip": return "VIP-визит"; case "rush": return "Наплыв гостей"; default: return value ?? ""; }
        }
        public static string StatusName(string value)
        {
            switch (value)
            {
                case "queue": return "Ждёт заселения"; case "walking": return "Идёт в номер"; case "staying": return "Проживает";
                case "checkout": return "Ожидает расчёта"; case "leaving": return "Уходит"; case "gone": return "Уехал";
                case "planned": case "expected": return "Ожидается"; case "confirmed": return "Подтверждено";
                case "arrived": return "Прибыл"; case "arriving": return "Прибывают"; case "cleanup": return "Завершение обслуживания"; case "admitted": case "checkedin": return "Заселён";
                case "active": return "Активно"; case "open": return "Открыт"; case "fulfilled": case "completed": return "Выполнено";
                case "cancelled": return "Отменено"; case "refused": return "Отказ"; case "resolved": return "Устранено";
                case "missed": case "expired": return "Срок истёк"; default: return value ?? "";
            }
        }
        public static string GuestContract(MvpGuestState contract)
        {
            if (contract == null) return "Условия прежней смены";
            return ArchetypeName(contract.archetype) + " · " + TraitName(contract.trait) + " · " + contract.partySize + " чел.\n" +
                "Заезд: день " + contract.arrivalDay + " · ночей: " + contract.billableNights + " · выезд до дня " + contract.departureDay +
                "\nСогласовано: " + contract.agreedTariff + " ₽ / ночь · " + (contract.agreedTariff * contract.billableNights) + " ₽ за проживание";
        }
        public static string Requirements(MvpGuestState contract)
        {
            if (contract == null) return "Чистая кровать и исправный номер.";
            return "Обязательно: мест ≥ " + contract.partySize + ", кровать ≥ " + contract.minBedQuality + ", ТВ ≥ " + contract.minTvQuality +
                (contract.requiresWater ? ", вода" : "") + (contract.requiresPower ? ", электричество" : "") +
                ".\nПожелания: кровать " + contract.preferredBedQuality + ", ТВ " + contract.preferredTvQuality +
                "; терпение " + Mathf.RoundToInt(contract.patience) + " с; переносимость шума " + Percent(contract.noiseTolerance) + ".";
        }
        public static string EquipmentStatus(HotelState state, int room, string kind)
        {
            MvpEquipmentState equipment = HotelOperationsRules.Equipment(state, room, kind);
            if (equipment == null || !equipment.installed) return EquipmentName(kind) + " · не установлено";
            string status = equipment.localFault ? "местная поломка" : HotelOperationsRules.EffectiveEquipment(state, room, kind) ? "работает" :
                kind == "sink" || kind == "toilet" ? "нет общей воды" : "нет общего питания";
            return EquipmentName(kind) + " · " + status + " · качество " + equipment.quality + " · износ " + Percent(equipment.wear);
        }
        public static string RequestDeadline(HotelState state, MvpRequestState request)
        {
            if (request.status != "open") return StatusName(request.status);
            int left = Mathf.CeilToInt(request.dueAt - state.mvp.elapsed);
            return left >= 0 ? "осталось " + left + " с" : "просрочен на " + (-left) + " с";
        }
        public static HotelCommand GuestCommand(string action, int guestId, int room = 0)
        {
            return new HotelCommand(action, "", room) { guestId = guestId };
        }
        public static string AssignmentReason(HotelState state, int guestId, int room)
        {
            if (guestId <= 0) return "Сначала явно выберите гостя во вкладке «Гости».";
            GuestState guest = state.guests.Find(g => g.id == guestId);
            if (guest == null || (guest.stage != "queue" && guest.stage != "walking" && guest.stage != "staying")) return "Выбранный гость больше не ожидает заселения или переселения.";
            if (guest.stage != "queue")
            {
                if (state.phase != "open" && state.phase != "closing") return "Переселение доступно во время обслуживания.";
                return HotelHospitalityRules.RoomForGuestBlockReason(state, guest, room, true);
            }
            return HotelHospitalityRules.AssignmentBlockReason(state, guestId, room);
        }
        public static string StationReason(HotelState state, ulong playerId, bool host, string station, bool hostOnly, bool preparationOnly)
        {
            if (hostOnly && !host) return "Это действие выполняет хозяин отеля.";
            if (preparationOnly && state.phase != "preparation") return "Доступно во время подготовки к смене.";
            PlayerState player = state.players.Find(p => p.id == playerId);
            Vector3 delta = player == null ? Vector3.one * 100 : player.position - HotelLayout.Target(station); delta.y = 0;
            return delta.magnitude > HotelSimulation.InteractionDistance ? station == "desk" ? "Подойдите к стойке регистрации." : "Подойдите к доске управления." : "";
        }
        public static List<string> MvpTasks(HotelState state)
        {
            var result = new List<string>();
            foreach (string line in HotelHospitalityRules.Tasks(state)) if (!result.Contains(line)) result.Add(line);
            foreach (string line in HotelOperationsRules.Tasks(state)) if (!result.Contains(line)) result.Add(line);
            return result;
        }
        static string MvpFocusText(HotelState state, ulong playerId, string target, ItemState held)
        {
            if (state.players.Exists(p => p.id != playerId && p.workTarget == target)) return "Здесь уже работает коллега";
            if (target == "hamper" && (held?.kind == "dirtytowel" || held?.kind == "towel" && held.condition == "dirty")) return "Сдать грязное полотенце · E";
            if (target == "board") return "Доска · цены, улучшения, отделка и прогноз";
            if (target == "coffee")
                return held != null ? "Кофе · сначала освободите руки" : state.mvp.coffeeStock <= 0 ? "Кофе закончился · пополнение между днями" :
                    "Удерживайте E: приготовить кофе · порций: " + state.mvp.coffeeStock;
            if (target == "utility_water" || target == "utility_power")
            {
                bool water = target == "utility_water", fault = water ? state.mvp.utilities.waterFault : state.mvp.utilities.powerFault;
                return (water ? "Водоснабжение" : "Электрощит") + (!fault ? " исправно" : held?.kind == "toolbox" ? " · удерживайте E: восстановить" : " · авария, нужен ящик инструментов");
            }
            string[] parts = target.Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int number)) return null;
            RoomState room = state.rooms.Find(r => r.number == number);
            if (room?.mvp == null) return null;
            if (!room.mvp.owned) return "№ " + number + " ещё не куплен · улучшения у доски";
            string kind = parts[0];
            if (kind == "sink" || kind == "toilet" || kind == "tv" || kind == "lamp")
            {
                var equipment = HotelOperationsRules.Equipment(state, number, kind);
                if (equipment == null || !equipment.installed) return EquipmentName(kind) + " не установлено";
                if (!equipment.localFault) return EquipmentStatus(state, number, kind);
                string tool = kind == "toilet" ? "plunger" : "toolbox";
                return EquipmentName(kind) + (held?.kind == tool ? " · удерживайте E: починить" : " · поломка, нужен " + (tool == "plunger" ? "вантуз" : "ящик инструментов"));
            }
            if (kind == "clean") return room.mvp.dirt <= .001f ? "Пол чистый" : held?.kind == "mop" ? "Удерживайте E: убрать грязь" : "Грязный пол · нужна швабра";
            if (kind == "dirtytowel") return room.mvp.dirtyTowels == 0 ? "Грязных полотенец нет" : held == null ? "Удерживайте E: собрать грязное полотенце, затем в приёмник" : "Грязные полотенца · нужны свободные руки";
            if (kind == "towel" && held?.kind == "towel" && held.condition == "dirty") return "Это грязное полотенце · сдайте его в приёмник и возьмите чистое";
            if (kind == "coffee") return held?.kind == "coffee" ? "Доставить кофе · E · № " + number : "Столик для кофе · приготовьте чашку в лобби";
            if (kind == "bag" && held?.kind == "bag" && held.ownerGuest != room.guestId) return "Чужой номер · багаж можно потерять; проверьте владельца";
            if (kind == "trash" && room.mvp.binFill > .001f) return held == null ? "Удерживайте E: опустошить корзину · " + Percent(room.mvp.binFill) : "Корзина · нужны свободные руки";
            return null;
        }
    }
}
