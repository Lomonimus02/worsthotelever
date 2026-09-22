using System;
using System.Collections.Generic;

namespace WorstHotel
{
    // Read-only queries. Equipment returns a snapshot: consumers must not mutate the room via this API.
    public static class HotelOperationsRules
    {
        public const int CartCapacity = 4;
        public static MvpEquipmentState Equipment(HotelState state, int number, string kind)
        {
            MvpEquipmentState value = FindEquipment(state, number, kind);
            return value == null ? null : new MvpEquipmentState { kind = value.kind, installed = value.installed, localFault = value.localFault,
                quality = value.quality, episode = value.episode, wear = value.wear };
        }
        internal static MvpEquipmentState FindEquipment(HotelState state, int number, string kind)
        {
            RoomState room = FindRoom(state, number);
            return room == null || room.mvp == null || room.mvp.equipment == null ? null : room.mvp.equipment.Find(e => e.kind == kind);
        }
        internal static RoomState FindRoom(HotelState state, int number)
        {
            return state == null || state.rooms == null ? null : state.rooms.Find(r => r.number == number);
        }
        public static bool EffectiveEquipment(HotelState state, int number, string kind)
        {
            RoomState room = FindRoom(state, number);
            MvpEquipmentState item = FindEquipment(state, number, kind);
            if (state == null || state.mvp == null || state.mvp.utilities == null || room == null || room.mvp == null || !room.mvp.owned ||
                item == null || !item.installed || item.localFault) return false;
            if (kind == "sink" || kind == "toilet") return HotelDangerRules.WaterAvailable(state, number);
            if (kind == "tv" || kind == "lamp") return HotelDangerRules.PowerAvailable(state, number);
            return false;
        }
        public static string PurchaseBlockReason(HotelState state, string id, int number)
        {
            if (state == null || state.mvp == null) return "Это улучшение доступно в отеле MVP.";
            if (state.phase != "preparation") return "Улучшения устанавливают во время подготовки.";
            HotelUpgradeDefinition upgrade = HotelUpgradeCatalog.Find(id);
            if (upgrade == null) return "Неизвестное улучшение.";
            if (id == "toolbox" && state.secondToolbox || id == "cart" && state.cartUpgrade || id == "linen" && state.mvp.betterLinen || id == "coffee" && state.mvp.coffeeMachine)
                return "Улучшение уже куплено.";
            if (id == "room105" || id == "room106")
            {
                RoomState room = FindRoom(state, id == "room105" ? 105 : 106);
                if (room == null || room.mvp == null) return "Комната отсутствует в плане отеля.";
                if (room.mvp.owned) return "Этот номер уже открыт.";
                if (room.guestId != 0) return "Нельзя изменить занятый номер.";
                if (room.mvp.equipment == null || room.mvp.equipment.Count != 4) return "Сначала восстановите оборудование номера.";
            }
            if (id == "bed" || id == "tv")
            {
                RoomState room = FindRoom(state, number);
                if (room == null || room.mvp == null || !room.mvp.owned) return "Выберите принадлежащий отелю номер.";
                if (room.guestId != 0) return "Дождитесь выезда гостя перед заменой мебели.";
                if (id == "bed" && room.mvp.bedQuality >= 2) return "В номере уже установлена удобная кровать.";
                if (id == "tv")
                {
                    MvpEquipmentState tv = FindEquipment(state, number, "tv");
                    if (tv == null) return "В номере отсутствует слот телевизора.";
                    if (tv.installed && room.mvp.tvQuality >= 2) return "В номере уже установлен улучшенный телевизор.";
                }
            }
            if (state.cash < upgrade.price) return "Недостаточно денег: нужно " + upgrade.price + ".";
            return "";
        }
        public static string FinishBlockReason(HotelState state, string finish, int number)
        {
            if (state == null || state.mvp == null) return "Отделка доступна в отеле MVP.";
            if (state.phase != "preparation") return "Отделку меняют во время подготовки.";
            int price = HotelUpgradeCatalog.FinishPrice(finish);
            if (price == 0) return "Неизвестный вариант отделки.";
            RoomState room = FindRoom(state, number);
            if (room == null || room.mvp == null || !room.mvp.owned) return "Выберите принадлежащий отелю номер.";
            if (room.guestId != 0) return "Дождитесь выезда гостя перед ремонтом отделки.";
            if (room.mvp.finishId == finish) return "В номере уже такая отделка.";
            if (state.cash < price) return "Недостаточно денег: нужно " + price + ".";
            return "";
        }
        public static int CartLoad(HotelState state)
        {
            if (state == null || state.items == null) return 0;
            int load = 0;
            foreach (ItemState item in state.items) if (!item.consumed && item.kind == "bag" && item.placedRoom == -1) load += item.size == "large" ? 2 : 1;
            return load;
        }
        public static string CartLoadBlockReason(HotelState state, ItemState bag)
        {
            if (state == null || state.mvp == null || !state.cartUpgrade) return "В отеле нет багажной тележки.";
            if (bag == null || bag.consumed || bag.kind != "bag") return "На тележку можно поставить чемодан.";
            if (bag.holder >= 0) return "Чемодан уже держит сотрудник.";
            if (bag.placedRoom == -1) return "Этот чемодан уже на тележке.";
            return CartLoad(state) + (bag.size == "large" ? 2 : 1) > CartCapacity ? "Тележка заполнена: большой чемодан занимает два места из четырёх." : "";
        }
        public static bool LuggageDelivered(HotelState state, GuestState guest)
        {
            if (state == null || state.items == null || guest == null || guest.room == 0 || guest.stage == "queue" || guest.stage == "gone" || guest.stage == "leaving") return false;
            RoomState room = FindRoom(state, guest.room);
            if (room == null || room.mvp == null || !room.mvp.owned || room.guestId != guest.id) return false;
            bool any = false;
            foreach (ItemState item in state.items)
            {
                if (item.kind != "bag" || item.ownerGuest != guest.id || item.consumed) continue;
                any = true;
                if (item.holder != -1 || item.placedRoom != guest.room) return false;
            }
            return any;
        }
        public static List<string> Tasks(HotelState state)
        {
            var tasks = new List<string>();
            if (state == null || state.mvp == null) return tasks;
            if (state.mvp.utilities.waterFault) tasks.Add("Общая подача воды: починить водяной узел у склада ящиком инструментов");
            if (state.mvp.utilities.powerFault) tasks.Add("Общее электричество: починить электрощит у склада ящиком инструментов");
            foreach (RoomState room in state.rooms)
            {
                if (room.mvp == null || !room.mvp.owned) continue;
                string title = "Номер " + room.number + ": ";
                if (room.guestId == 0 && room.bed == 1) tasks.Add(title + "снять грязное бельё и отнести в приёмник");
                if (room.guestId == 0 && room.bed == 0) tasks.Add(title + "застелить чистое бельё");
                if (!room.towel) tasks.Add(title + "доставить чистое полотенце");
                if (room.mvp.dirtyTowels > 0) tasks.Add(title + "собрать использованные полотенца → приёмник (" + room.mvp.dirtyTowels + ")");
                if (room.mvp.dirt > .05f) tasks.Add(title + "вымыть грязный пол шваброй");
                if (room.water > .001f) tasks.Add(title + "убрать воду шваброй");
                if (room.trash || room.mvp.binFill >= .35f) tasks.Add(title + (room.mvp.binFill >= .9f ? "вынести переполненную корзину" : "собрать мусор") + " → контейнер");
                foreach (MvpEquipmentState equipment in room.mvp.equipment)
                {
                    if (!equipment.installed || !equipment.localFault) continue;
                    string label = equipment.kind == "sink" ? "раковину" : equipment.kind == "toilet" ? "унитаз" : equipment.kind == "tv" ? "телевизор" : "лампу";
                    tasks.Add(title + "починить " + label + (equipment.kind == "toilet" ? " (вантуз)" : " (ящик инструментов)"));
                }
            }
            if (state.items.Exists(i => !i.consumed && (i.kind == "dirtylinen" || i.kind == "towel" && i.condition == "dirty"))) tasks.Add("Отнести грязное бельё и использованные полотенца в приёмник у склада");
            if (state.items.Exists(i => !i.consumed && i.kind == "trashbag")) tasks.Add("Отнести мешки мусора в контейнер у склада");
            foreach (GuestState guest in state.guests)
            {
                if (guest.stage == "gone" || guest.stage == "leaving") continue;
                foreach (ItemState bag in state.items)
                    if (!bag.consumed && bag.kind == "bag" && bag.ownerGuest == guest.id && bag.placedRoom > 0 && bag.placedRoom != guest.room)
                        tasks.Add("Исправить багаж: " + guest.name + ", чемодан оставлен в " + bag.placedRoom + (guest.room == 0 ? "; сначала зарегистрировать гостя" : " → " + guest.room));
            }
            if (state.mvp.requests.Exists(r => r.kind == "coffee" && r.status == "open")) tasks.Add("Приготовить кофе у кофейной стойки в лобби, затем отнести заказавшему гостю");
            return tasks;
        }
    }
}
