using System;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        private bool TryOperationsCommand(PlayerState p, HotelCommand c, out string error)
        {
            error = "";
            if (State.mvp == null || (c.action != "upgrade" && c.action != "finishRoom" && c.action != "toggleRoom")) return false;
            if (p.id != 0) { error = "Это действие подтверждает хозяин отеля."; return true; }
            if (!Near(p, HotelLayout.Target("board"))) { error = "Подойдите к доске управления."; return true; }
            string target = c.target ?? "";
            if (c.action == "toggleRoom")
            {
                RoomState room = Room(c.number);
                if (room == null || room.mvp == null || !room.mvp.owned) { error = "Этот номер ещё не принадлежит отелю."; return true; }
                room.outOfService = !room.outOfService;
                return true; // Closing sales never removes the current occupant.
            }
            if (c.action == "finishRoom")
            {
                error = HotelOperationsRules.FinishBlockReason(State, target, c.number);
                if (error != "") return true;
                int price = HotelUpgradeCatalog.FinishPrice(target);
                Room(c.number).mvp.finishId = target;
                OperationsCharge(price, "Отделка " + c.number + " / " + target);
                PacingUpgradeGrace();
                return true;
            }
            error = HotelOperationsRules.PurchaseBlockReason(State, target, c.number);
            if (error != "") return true;
            HotelUpgradeDefinition upgrade = HotelUpgradeCatalog.Find(target);
            switch (target)
            {
                case "toolbox":
                    State.secondToolbox = true;
                    State.items.Add(new ItemState { id = MvpId("toolbox"), kind = "toolbox", position = HotelLayout.Target("tools") + new Vector3(.8f, 0, 0), condition = "clean", size = "small" });
                    break;
                case "cart":
                    State.cartUpgrade = true;
                    State.items.Add(new ItemState { id = MvpId("cart"), kind = "cart", position = new Vector3(2.3f, .2f, -3.4f), condition = "clean", size = "large" });
                    break;
                case "linen": State.mvp.betterLinen = true; break;
                case "coffee": State.mvp.coffeeMachine = true; break;
                case "bed":
                    Room(c.number).mvp.bedQuality = 2;
                    Room(c.number).upgraded = true;
                    break;
                case "tv":
                    RoomState furnished = Room(c.number);
                    MvpEquipmentState tv = HotelOperationsRules.FindEquipment(State, c.number, "tv");
                    furnished.mvp.tvQuality = tv.installed ? Math.Min(2, furnished.mvp.tvQuality + 1) : 1;
                    tv.installed = true;
                    tv.quality = furnished.mvp.tvQuality;
                    tv.localFault = false;
                    tv.wear = 0; // Physical replacement, not a repair of unrelated equipment.
                    break;
                case "room105": case "room106":
                    RoomState purchased = Room(target == "room105" ? 105 : 106);
                    purchased.mvp.owned = true;
                    purchased.outOfService = false;
                    break;
            }
            OperationsCharge(upgrade.price, upgrade.name + (target == "bed" || target == "tv" ? " / " + c.number : ""));
            PacingUpgradeGrace();
            RefreshRequestFulfillment();
            return true;
        }

        private void OperationsCharge(int price, string description)
        {
            State.cash -= price;
            State.expenses += price;
            Log(description + ": −" + price);
            State.notice = description + ". Покупка установлена и сохранится в отеле.";
        }

        private bool TryOperationsInteract(PlayerState p, string target, out string error)
        {
            error = "";
            if (State.mvp == null) return false;
            if (target == "linen" || target == "towels")
            {
                if (!Near(p, HotelLayout.Target(target))) error = "Подойдите к полке.";
                else if (Held(p) != null) error = "Сначала освободите руки.";
                else if ((target == "linen" ? State.linenStock : State.towelStock) <= 0) error = "Запас закончился. Пополнение между сменами.";
                else
                {
                    Cancel(p);
                    if (target == "linen") State.linenStock--; else State.towelStock--;
                    OperationsGive(p, target == "linen" ? "linen" : "towel", "clean");
                    RefreshRequestFulfillment();
                }
                return true;
            }
            if (target == "hamper" || target == "bin")
            {
                ItemState held = Held(p);
                if (!Near(p, HotelLayout.Target(target))) error = "Подойдите к приёмнику.";
                else if (held == null || (target == "hamper" ? held.kind != "dirtylinen" && !(held.kind == "towel" && held.condition == "dirty") : held.kind != "trashbag"))
                    error = "В руках нет подходящего предмета.";
                else { Consume(p, held); RefreshRequestFulfillment(); }
                return true;
            }
            if (target == "coffee" || target == "utility_water" || target == "utility_power")
            {
                error = BeginWork(p, target);
                return true;
            }
            if (!TryOperationsRoomTarget(target, out string kind, out RoomState room)) return false;
            if (room == null || room.mvp == null || !room.mvp.owned) { error = "Этот номер ещё не принадлежит отелю."; return true; }
            if (!Near(p, HotelLayout.Target(target))) { error = "Подойдите ближе к объекту."; return true; }
            if (kind == "door") return true;
            if (kind == "towel") error = OperationsDeliverTowel(p, room);
            else if (kind == "coffee") error = OperationsDeliverCoffee(p, room);
            else if (kind == "bag") error = OperationsPlaceBag(p, room);
            else error = BeginWork(p, target);
            return true;
        }

        private string OperationsWorkError(PlayerState p, string target)
        {
            if (State.mvp == null) return "Работа недоступна в этом отеле.";
            if (target == "coffee")
            {
                if (!Near(p, HotelLayout.Target(target))) return "Подойдите к месту приготовления кофе.";
                if (Held(p) != null) return "Для готовой чашки нужны свободные руки.";
                return State.mvp.coffeeStock > 0 ? "" : "Кофе закончился. Запас пополнится между сменами.";
            }
            ItemState held = Held(p);
            if (target == "utility_water" || target == "utility_power")
            {
                if (!Near(p, HotelLayout.Target(target))) return "Подойдите к техническому узлу.";
                bool broken = target == "utility_water" ? State.mvp.utilities.waterFault : State.mvp.utilities.powerFault;
                if (!broken) return "Технический узел исправен.";
                return held != null && held.kind == "toolbox" ? "" : "Для технического узла нужен ящик инструментов.";
            }
            if (!TryOperationsRoomTarget(target, out string kind, out RoomState room)) return "Здесь нет длительной работы.";
            if (room == null || room.mvp == null || !room.mvp.owned) return "Этот номер ещё не принадлежит отелю.";
            if (!Near(p, HotelLayout.Target(target))) return "Подойдите ближе и удерживайте E.";
            switch (kind)
            {
                case "bed":
                    if (room.guestId != 0) return "Кровать занята гостем. Дождитесь выезда.";
                    if (room.bed == 2) return "Кровать уже чистая.";
                    if (room.bed == 1) return held == null ? "" : "Для грязного белья нужны свободные руки.";
                    return held != null && held.kind == "linen" && held.condition == "clean" ? "" : "Возьмите чистое бельё с полки.";
                case "trash":
                    if (!room.trash && room.mvp.binFill <= .001f) return "Мусора нет.";
                    return held == null ? "" : "Для мешка мусора нужны свободные руки.";
                case "dirtytowel":
                    if (room.mvp.dirtyTowels <= 0) return "Использованных полотенец нет.";
                    return held == null ? "" : "Для использованного полотенца нужны свободные руки.";
                case "water":
                    if (room.water <= .001f) return "Пол уже сухой.";
                    return held != null && held.kind == "mop" ? "" : "Нужна швабра.";
                case "clean":
                    if (room.mvp.dirt <= .001f) return "Грязи на полу нет.";
                    return held != null && held.kind == "mop" ? "" : "Для мытья пола нужна швабра.";
                case "sink": case "toilet": case "tv": case "lamp":
                    MvpEquipmentState equipment = HotelOperationsRules.FindEquipment(State, room.number, kind);
                    if (equipment == null || !equipment.installed) return "Оборудование не установлено.";
                    if (!equipment.localFault)
                    {
                        if (!HotelOperationsRules.EffectiveEquipment(State, room.number, kind))
                            return kind == "sink" || kind == "toilet" ? "Местной поломки нет: восстановите общую подачу воды." : "Местной поломки нет: восстановите общее электричество.";
                        return "Оборудование исправно.";
                    }
                    string tool = kind == "toilet" ? "plunger" : "toolbox";
                    return held != null && held.kind == tool ? "" : kind == "toilet" ? "Для засора нужен вантуз." : "Нужен ящик инструментов.";
                default: return "Этот объект не требует длительной работы.";
            }
        }

        private float OperationsWorkDuration(string target)
        {
            if (target == "coffee") return State.mvp.coffeeMachine && !State.mvp.utilities.powerFault ? 2.5f : 6;
            if (target == "utility_water" || target == "utility_power") return 6;
            if (!TryOperationsRoomTarget(target, out string kind, out RoomState room) || room == null) return 1;
            switch (kind)
            {
                case "bed": return room.bed == 1 ? 2 : State.mvp.betterLinen ? 2 : 3;
                case "trash": return room.mvp.binFill >= .9f ? 3 : 2;
                case "dirtytowel": return 1.5f;
                case "sink": return 5;
                case "toilet": return 4;
                case "tv": case "lamp": return 4;
                case "water": return 3;
                case "clean": return 4;
                default: return 1;
            }
        }

        private void OperationsCompleteWork(PlayerState p)
        {
            string target = p.workTarget;
            if (OperationsWorkError(p, target) != "") { Cancel(p); return; }
            if (target == "coffee")
            {
                State.mvp.coffeeStock--;
                OperationsGive(p, "coffee", "clean");
            }
            else if (target == "utility_water") { State.mvp.utilities.waterFault = false; State.mvp.utilities.waterWear = .15f; }
            else if (target == "utility_power") { State.mvp.utilities.powerFault = false; State.mvp.utilities.powerWear = .15f; }
            else if (TryOperationsRoomTarget(target, out string kind, out RoomState room))
            {
                switch (kind)
                {
                    case "bed":
                        if (room.bed == 1) { room.bed = 0; OperationsGive(p, "dirtylinen", "dirty"); }
                        else
                        {
                            room.bed = 2; Consume(p, Held(p));
                            HotelOnboarding.Record(State, HotelTutorialSkill.CleanBed);
                        }
                        break;
                    case "trash": room.trash = false; room.mvp.binFill = 0; OperationsGive(p, "trashbag", "dirty"); break;
                    case "dirtytowel": room.mvp.dirtyTowels--; OperationsGive(p, "towel", "dirty"); break;
                    case "water":
                        room.water = 0;
                        HotelDirector.Mopped(State, room);
                        HotelOnboarding.Record(State, HotelTutorialSkill.MopWater);
                        break;
                    case "clean": room.mvp.dirt = 0; break;
                    case "sink": case "toilet": case "tv": case "lamp":
                        MvpEquipmentState equipment = HotelOperationsRules.FindEquipment(State, room.number, kind);
                        equipment.localFault = false;
                        equipment.wear = .15f; // Repairs restore condition, never alter quality/capacity.
                        if (kind == "sink")
                        {
                            room.leak = false;
                            HotelDirector.Repaired(State, room);
                            HotelOnboarding.Record(State, HotelTutorialSkill.RepairLeak);
                        }
                        break;
                }
            }
            Cancel(p);
            RefreshRequestFulfillment();
        }

        private string OperationsDeliverTowel(PlayerState p, RoomState room)
        {
            ItemState towel = Held(p);
            if (towel == null || towel.kind != "towel" || towel.condition != "clean") return "Принесите чистое полотенце с полки.";
            GuestState guest = Guest(room.guestId);
            MvpRequestState request = OperationsOpenRequest(guest, "towel");
            if (room.towel && request == null) return "Полотенце уже есть; дополнительного запроса нет.";
            if (!room.towel) room.mvp.towelUseProgress = 0;
            room.towel = true;
            if (request != null) request.status = "fulfilled";
            Consume(p, towel);
            RefreshRequestFulfillment();
            return "";
        }
        private string OperationsDeliverCoffee(PlayerState p, RoomState room)
        {
            ItemState coffee = Held(p);
            if (coffee == null || coffee.kind != "coffee" || coffee.condition != "clean") return "Сначала приготовьте кофе у стойки.";
            GuestState guest = Guest(room.guestId);
            MvpRequestState request = OperationsOpenRequest(guest, "coffee");
            if (request == null) return "В этом номере нет активного заказа кофе.";
            request.status = "fulfilled";
            Consume(p, coffee);
            RefreshRequestFulfillment();
            return "";
        }
        private MvpRequestState OperationsOpenRequest(GuestState guest, string kind)
        {
            if (guest == null || guest.mvp == null || guest.paid || (guest.stage != "walking" && guest.stage != "staying")) return null;
            return State.mvp.requests.Find(r => r.guestId == guest.id && r.kind == kind && r.status == "open");
        }
        private string OperationsPlaceBag(PlayerState p, RoomState room)
        {
            ItemState held = Held(p);
            if (held == null) return "Принесите чемодан или тележку с багажом.";
            ItemState bag = held;
            if (held.kind == "cart")
            {
                bag = State.items.Find(i => !i.consumed && i.kind == "bag" && i.placedRoom == -1 && i.ownerGuest == room.guestId);
                if (bag == null) bag = State.items.Find(i => !i.consumed && i.kind == "bag" && i.placedRoom == -1);
            }
            if (bag == null || bag.kind != "bag") return "В руках нет чемодана.";
            GuestState owner = Guest(bag.ownerGuest);
            if (owner == null || owner.paid || owner.stage == "gone" || owner.stage == "leaving") return "Владелец этого багажа уже покинул отель.";
            Cancel(p);
            bag.holder = -1;
            bag.placedRoom = room.number;
            bag.position = HotelLayout.RoomTarget("bag", room.number);
            if (held == bag) p.held = "";
            owner.luggageDelivered = HotelOperationsRules.LuggageDelivered(State, owner);
            if (owner.luggageDelivered) HotelOnboarding.Record(State, HotelTutorialSkill.DeliverBag);
            State.notice = owner.room == room.number && room.guestId == owner.id
                ? "Чемодан гостя " + owner.name + " доставлен в номер " + room.number + "."
                : "Чемодан гостя " + owner.name + " оставлен в чужом номере " + room.number + ". Его можно забрать и перенести.";
            UpdateCartCargo();
            RefreshRequestFulfillment();
            return "";
        }
        private void OperationsGive(PlayerState p, string kind, string condition)
        {
            var item = new ItemState { id = MvpId(kind), kind = kind, condition = condition, size = "small",
                holder = (long)p.id, position = p.position + Vector3.up * .8f };
            State.items.Add(item);
            p.held = item.id;
        }

        private void OperationsStep(float dt)
        {
            if (State.mvp == null || !Finite(dt) || dt <= 0 || (State.phase != "open" && State.phase != "closing")) return;
            bool paused = HotelDirector.ClockHeld(State);
            int occupied = 0;
            foreach (RoomState room in State.rooms)
            {
                if (room.mvp == null || !room.mvp.owned) continue;
                MvpEquipmentState sink = HotelOperationsRules.FindEquipment(State, room.number, "sink");
                room.leak = sink != null && sink.installed && sink.localFault;
                if (room.leak && !State.mvp.utilities.waterFault) room.water = Mathf.Min(1, room.water + dt * .007f);
                GuestState guest = Guest(room.guestId);
                if (guest == null || guest.stage != "staying" || guest.mvp == null || paused) continue;
                occupied++;
                float usage = Mathf.Clamp(guest.mvp.messRate * guest.mvp.partySize, .25f, 4);
                room.mvp.dirt = Mathf.Min(1, room.mvp.dirt + dt * .0007f * usage);
                room.mvp.binFill = Mathf.Min(1, room.mvp.binFill + dt * .0008f * usage);
                room.trash = room.mvp.binFill > .001f;
                if (room.towel && room.mvp.dirtyTowels < 12)
                {
                    room.mvp.towelUseProgress = Mathf.Min(1, room.mvp.towelUseProgress + dt * usage / 300f);
                    if (room.mvp.towelUseProgress >= 1)
                    {
                        room.mvp.towelUseProgress = 0;
                        room.towel = false;
                        room.mvp.dirtyTowels++;
                    }
                }
                foreach (MvpEquipmentState equipment in room.mvp.equipment)
                {
                    if (!equipment.installed || equipment.localFault) continue;
                    float wearRate = equipment.kind == "sink" ? .00045f : equipment.kind == "toilet" ? .00035f : equipment.kind == "tv" ? .0002f : .00012f;
                    // Utility failures suspend use, not the local fault/condition of every consumer.
                    if (!HotelOperationsRules.EffectiveEquipment(State, room.number, equipment.kind)) continue;
                    equipment.wear = Mathf.Min(1, equipment.wear + dt * wearRate * usage / Math.Max(1, equipment.quality));
                    if (equipment.wear >= 1) TryRaiseFault(room.number, equipment.kind);
                }
            }
            if (occupied > 0 && !paused)
            {
                MvpUtilityState utility = State.mvp.utilities;
                if (!utility.waterFault) utility.waterWear = Mathf.Min(1, utility.waterWear + dt * occupied * .000025f);
                if (!utility.powerFault) utility.powerWear = Mathf.Min(1, utility.powerWear + dt * occupied * .00002f);
                if (utility.waterWear >= 1) TryRaiseFault(0, "water");
                if (utility.powerWear >= 1) TryRaiseFault(0, "power");
            }
        }

        private bool TryRaiseFault(int number, string kind)
        {
            if (State.mvp == null) return false;
            if (number == 0)
            {
                MvpUtilityState utilities = State.mvp.utilities;
                if (kind == "water" && !utilities.waterFault)
                {
                    utilities.waterFault = true; utilities.waterEpisode++;
                    State.notice = "В отеле пропала вода. Проверьте общий технический узел у склада.";
                    return true;
                }
                if (kind == "power" && !utilities.powerFault)
                {
                    utilities.powerFault = true; utilities.powerEpisode++;
                    State.notice = "В отеле пропало электричество. Проверьте общий технический узел у склада.";
                    return true;
                }
                return false;
            }
            RoomState room = Room(number);
            MvpEquipmentState equipment = HotelOperationsRules.FindEquipment(State, number, kind);
            if (room == null || room.mvp == null || !room.mvp.owned || equipment == null || !equipment.installed || equipment.localFault ||
                (kind != "sink" && kind != "toilet" && kind != "tv" && kind != "lamp")) return false;
            equipment.localFault = true;
            equipment.episode++;
            if (kind == "sink") room.leak = true;
            State.notice = "Поломка в номере " + number + ": " + (kind == "sink" ? "течёт раковина" : kind == "toilet" ? "засор унитаза" : kind == "tv" ? "не работает телевизор" : "не работает лампа") + ".";
            return true;
        }

        private bool TryOperationsRoomTarget(string target, out string kind, out RoomState room)
        {
            kind = ""; room = null;
            if (string.IsNullOrEmpty(target)) return false;
            string[] parts = target.Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int number)) return false;
            kind = parts[0];
            if (kind != "bed" && kind != "trash" && kind != "sink" && kind != "water" && kind != "towel" && kind != "bag" && kind != "door" &&
                kind != "toilet" && kind != "tv" && kind != "lamp" && kind != "coffee" && kind != "clean" && kind != "dirtytowel") return false;
            room = Room(number);
            return true; // Known target family with a missing room must reject instead of falling through.
        }
    }
}
