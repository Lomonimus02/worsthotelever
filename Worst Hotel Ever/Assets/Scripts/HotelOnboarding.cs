using System;
using UnityEngine;

namespace WorstHotel
{
    // Bit values are part of save version 1. Add skills with new bits, never renumber existing ones.
    [Flags]
    public enum HotelTutorialSkill
    {
        None = 0,
        CheckIn = 1 << 0,
        DeliverBag = 1 << 1,
        ExtraTowel = 1 << 2,
        CleanBed = 1 << 3,
        RepairLeak = 1 << 4,
        MopWater = 1 << 5,
        CheckOut = 1 << 6,
        NextDay = 1 << 7,
        All = CheckIn | DeliverBag | ExtraTowel | CleanBed | RepairLeak | MopWater | CheckOut | NextDay
    }

    public sealed class HotelHint
    {
        public string title, body, targetId;
        public int completed, total;
        public bool finished;
    }

    // Read-only contextual help. HotelDirector, not this projection, owns guided pacing.
    public static class HotelOnboarding
    {
        public const int TotalSkills = 8;

        internal static void Record(HotelState state, HotelTutorialSkill skill)
        {
            state.tutorialFlags |= (int)skill;
        }

        public static HotelHint GetHint(HotelState state, ulong localPlayerId)
        {
            if (state == null || state.tutorialSkipped || state.day > (state.contentVersion == 1 || state.mvp != null ? 2 : 1) ||
                (state.contentVersion == 0 && (state.tutorialFlags & (int)HotelTutorialSkill.All) == (int)HotelTutorialSkill.All) ||
                state.players == null || state.rooms == null || state.guests == null || state.items == null) return null;
            PlayerState player = state.players.Find(p => p.id == localPlayerId);
            if (player == null) return null;

            ItemState held = state.items.Find(i => i.id == player.held && !i.consumed && i.holder == (long)localPlayerId);
            HotelHint hint = state.mvp != null ? MvpHint(state, player, held) : held == null ? FreeHandsHint(state, player) : HeldHint(state, player, held);
            if ((state.contentVersion == 1 || state.mvp != null) && state.day == 2) hint.title = "День 2 · " + hint.title;
            int bits = state.tutorialFlags & (int)HotelTutorialSkill.All;
            int completed = 0;
            while (bits != 0) { completed += bits & 1; bits >>= 1; }
            hint.completed = completed;
            hint.total = TotalSkills;
            hint.finished = false; // Finished/disabled assistance is represented by null, not a sticky panel.
            return hint;
        }

        private static HotelHint HeldHint(HotelState state, PlayerState player, ItemState held)
        {
            RoomState room;
            switch (held.kind)
            {
                case "dirtylinen": return Hint("Грязное бельё — в приёмник", "Отнесите бельё в приёмник у склада и нажмите E. Затем возьмите чистое бельё с полки.", "hamper");
                case "trashbag": return Hint("Мешок — в контейнер", "Отнесите собранный мусор в контейнер у склада и нажмите E, чтобы освободить руки.", "bin");
                case "linen":
                    room = NearestRoom(state, player, r => r.guestId == 0 && r.bed == 0, "bed");
                    if (room != null) return Hint("Застелите номер " + room.number, "Подойдите к голой кровати и удерживайте E до завершения. Одно чистое бельё расходуется на одну кровать.", "bed_" + room.number);
                    room = NearestRoom(state, player, r => r.guestId == 0 && r.bed == 1, "bed");
                    if (room != null) return Hint("Сначала снимите грязное бельё", "Положите чистое бельё рядом на пол кнопкой Q. Свободными руками удерживайте E у кровати номера " + room.number + ", затем отнесите грязное в приёмник.", "bed_" + room.number);
                    return PutDown("Сейчас нет свободной голой кровати. Сохраните чистое бельё для следующей уборки.");
                case "towel":
                    room = RequestedTowelRoom(state, player);
                    if (room == null) room = NearestRoom(state, player, r => !r.towel, "towel");
                    if (room != null) return Hint("Полотенце в номер " + room.number, "Подойдите к стойке полотенец в номере и нажмите E. Дополнительный запрос гостя закрывается одной доставкой.", "towel_" + room.number);
                    return PutDown("Во всех номерах есть полотенца, дополнительных запросов сейчас нет.");
                case "toolbox":
                    room = NearestRoom(state, player, r => r.leak, "sink");
                    if (room != null) return Hint("Почините раковину: " + room.number, "С ящиком в руках удерживайте E у протекающей раковины. Ремонт остановит течь, а оставшуюся воду нужно убрать шваброй.", "sink_" + room.number);
                    return PutDown("Сейчас нет свободной цели для ремонта. Оставьте инструменты на доступном полу, чтобы ими мог воспользоваться напарник.");
                case "mop":
                    room = NearestRoom(state, player, r => r.water > .001f, "water");
                    if (room != null) return Hint("Вытрите воду: " + room.number,
                        "Со шваброй удерживайте E над лужей до завершения." + (room.leak ? " Раковина ещё течёт: попросите напарника починить её, иначе вода появится снова." : " Раковина не течёт; уборка удалит оставшуюся воду."), "water_" + room.number);
                    return PutDown("Сейчас нет свободной лужи для уборки. Швабра убирает воду; протекающую раковину чинят ящиком инструментов.");
                case "bag": return BagDestination(state, held) ?? PutDown("Владелец этого чемодана уже не ждёт доставки в номер.");
                case "cart":
                    foreach (ItemState cargo in state.items)
                        if (!cargo.consumed && cargo.placedRoom == -1 && CanDeliverBag(state, cargo)) return BagDestination(state, cargo);
                    int loaded = state.items.FindAll(i => !i.consumed && i.placedRoom == -1).Count;
                    ItemState bag = NearestItem(state, player, i => i.kind == "bag" && i.placedRoom == 0 && WaitingForBag(state, i));
                    if (loaded < 2 && bag != null) return Hint("Загрузите чемодан на тележку", "Не отпускайте тележку. Подойдите к чемодану и нажмите E: на тележке помещаются два. Затем доставьте каждый в номер его владельца.", bag.id);
                    foreach (ItemState cargo in state.items)
                        if (!cargo.consumed && cargo.placedRoom == -1)
                        {
                            HotelHint destination = BagDestination(state, cargo);
                            if (destination != null) return destination;
                        }
                    return PutDown("Сейчас нет багажа для доставки. Тележку можно оставить рядом; уже загруженный багаж останется на ней.");
                default: return PutDown("Освободите руки перед следующим действием.");
            }
        }

        private static HotelHint FreeHandsHint(HotelState state, PlayerState player)
        {
            if (state.phase == "summary") return Hint("Следующий день", player.id == 0
                ? "На стойке нажмите E и выберите переход к следующему дню. Запасы пополнятся, а оставшиеся грязь и поломки сохранятся."
                : "Хозяин отеля завершает итоги на стойке и начинает следующий день. Вы можете помочь с оставшейся уборкой.", "desk");
            if (state.phase == "closing") return Hint("Подведите итоги смены", player.id == 0
                ? "Завершите срочное обслуживание. На стойке нажмите E и выберите завершение смены; из итогов можно перейти к следующему дню. Идеальная чистота не обязательна."
                : "Помогите завершить обслуживание. Хозяин отеля подводит итоги на стойке, затем начинает следующий день; неубранное сохранится.", "desk");
            if (state.phase == "preparation")
            {
                if (state.contentVersion == 1 && state.day == 2 && !state.secondToolbox && !state.cartUpgrade && !state.betterBeds &&
                    state.cash >= HotelSimulation.ToolboxPrice)
                    return Hint("Выберите полезное улучшение", player.id == 0
                        ? "Вчерашние проблемы сохранились. До открытия загляните на доску: второй ящик инструментов, тележка или кровати. Выберите то, чего не хватало команде; покупка необязательна. Затем подготовьте номера и откройте смену."
                        : "Обсудите с хозяином покупку на доске: второй ящик инструментов, тележку или кровати. Пока он выбирает, вы можете убрать вчерашние номера. Покупка необязательна.", "board");
                bool ready = state.rooms.Exists(r => r.guestId == 0 && !r.outOfService && r.bed == 2 && !r.leak && r.water <= .65f);
                if (!ready)
                {
                    HotelHint prepare = PrepareRoomHint(state, player, true);
                    if (prepare != null) return prepare;
                    if (state.rooms.Exists(r => r.guestId == 0 && r.outOfService)) return ReopenRoom(player);
                }
                return Hint(state.day == 1 ? "Откройте первую смену" : "Подготовьте и откройте отель", player.id == 0
                    ? "Осмотритесь и подготовьте отель. Когда команда готова, подойдите к стойке, нажмите E и выберите «Открыть»."
                    : "Помогите подготовить номера. Когда команда готова, хозяин отеля подходит к стойке, нажимает E и выбирает «Открыть».", "desk");
            }

            HotelHint guided = GuidedHint(state, player);
            if (guided != null) return guided;

            GuestState queue = state.guests.Find(g => g.stage == "queue");
            if (queue != null && state.phase == "open")
            {
                if (state.rooms.Exists(r => HotelSimulation.CheckInBlockReason(state, r.number) == ""))
                    return Hint("Гость ждёт регистрации", queue.name + " — следующий в очереди. Подойдите к стойке, нажмите E, выберите доступный номер и «Заселить».", "desk");
                HotelHint prepare = PrepareRoomHint(state, player, true);
                if (prepare != null) return prepare;
                if (state.rooms.Exists(r => r.guestId == 0 && r.outOfService)) return ReopenRoom(player);
            }

            RoomState request = RequestedTowelRoom(state, player);
            if (request != null) return SupplyHint(state, player, "towel", request.number, true);
            GuestState checkout = state.guests.Find(g => g.stage == "checkout" && !g.paid);
            if (checkout != null) return Hint("Оформите выезд гостя", "Дождитесь " + checkout.name + " у стойки. Нажмите E и «Принять оплату». Номер освободится, но потребует белья и уборки.", "desk");
            ItemState bag = NearestItem(state, player, i => i.kind == "bag" && CanDeliverBag(state, i) && !state.guests.Find(g => g.id == i.ownerGuest).luggageDelivered);
            if (bag != null)
            {
                GuestState owner = state.guests.Find(g => g.id == bag.ownerGuest);
                return Hint("Доставьте багаж", "Возьмите чемодан гостя " + owner.name + " кнопкой E. Отнесите к месту багажа в номере " + owner.room + " и снова нажмите E.", bag.id);
            }
            HotelHint repair = RepairHint(state, player);
            if (repair != null) return repair;
            HotelHint cleaning = PrepareRoomHint(state, player, false);
            if (cleaning != null) return cleaning;
            if (queue != null) return Hint("Дождитесь свободного номера", queue.name + " ждёт регистрации, но подходящего свободного номера сейчас нет. После выезда подготовьте кровать; текущие дела видны по Tab.", "desk");
            return Hint("Следите за отелем", "Гости сами подходят к стойке и сообщают о нуждах. Tab показывает актуальные задачи. Помощь реагирует на происходящее; ждать специального учебного события не нужно.", "");
        }

        private static HotelHint GuidedHint(HotelState state, PlayerState player)
        {
            if (!HotelDirector.ClockHeld(state)) return null;
            if (state.guidedStage == HotelDirector.Leak)
            {
                RoomState room = state.rooms.Find(r => r.number == state.guidedLeakRoom);
                if (room != null && room.leak) return ToolHint(state, player, "toolbox", room);
                if (room != null && room.water > .001f) return ToolHint(state, player, "mop", room);
                return Hint("Последствия устранены", "Раковина не течёт, пол сухой. Отель перейдёт к обычному темпу; текущая работа и результаты сохраняются.", "");
            }
            if (state.guidedStage == HotelDirector.Preparation && (state.tutorialFlags & (int)HotelTutorialSkill.CleanBed) == 0)
                return PrepareRoomHint(state, player, false) ?? Hint("Подготовьте чистую кровать", "Часы ждут подготовки свободной кровати. Можно работать в любом доступном номере; номер первого гостя не обязателен.", "");
            GuestState guest = state.guests.Find(g => g.id == state.guidedGuestId && (g.stage == "walking" || g.stage == "staying"));
            if (guest == null) return null;
            if (!guest.luggageDelivered)
            {
                ItemState bag = state.items.Find(i => !i.consumed && i.kind == "bag" && i.ownerGuest == guest.id && i.holder < 0);
                if (bag != null) return Hint("Доставьте багаж: " + guest.name, "Возьмите чемодан кнопкой E и отнесите к месту багажа в номере " + guest.room + ". После доставки гость попросит дополнительное полотенце; часы пока стоят.", bag.id);
            }
            if (guest.towelRequested) return SupplyHint(state, player, "towel", guest.room, true);
            if ((state.tutorialFlags & (int)HotelTutorialSkill.CleanBed) == 0)
            {
                HotelHint prepare = PrepareRoomHint(state, player, false);
                if (prepare != null) return prepare;
            }
            return Hint("Гость осваивается в номере", "После доставки багажа гость попросит полотенце. Подойдите к нужной цели, когда запрос появится; часы смены и сроки отъезда пока стоят.", "");
        }

        private static HotelHint PrepareRoomHint(HotelState state, PlayerState player, bool forArrival)
        {
            RoomState room = NearestRoom(state, player, r => r.guestId == 0 && (!forArrival || !r.outOfService) && r.bed == 1, "bed");
            if (room != null) return Hint("Снимите грязное бельё: " + room.number, "Подойдите к кровати со свободными руками и удерживайте E. Грязное бельё отнесите в приёмник у склада, затем принесите чистое.", "bed_" + room.number);
            room = NearestRoom(state, player, r => r.guestId == 0 && (!forArrival || !r.outOfService) && r.bed == 0, "bed");
            if (room != null) return SupplyHint(state, player, "linen", room.number, false);
            if (forArrival)
            {
                room = NearestRoom(state, player, r => r.guestId == 0 && !r.outOfService && r.leak, "sink");
                if (room != null) return ToolHint(state, player, "toolbox", room);
                room = NearestRoom(state, player, r => r.guestId == 0 && !r.outOfService && r.water > .65f, "water");
                if (room != null) return ToolHint(state, player, "mop", room);
                return null;
            }
            room = NearestRoom(state, player, r => r.trash, "trash");
            if (room != null) return Hint("Соберите мусор: " + room.number, "Свободными руками удерживайте E у мусора. Получившийся мешок отнесите в контейнер у склада и нажмите E.", "trash_" + room.number);
            room = NearestRoom(state, player, r => !r.towel, "towel");
            if (room != null) return SupplyHint(state, player, "towel", room.number, false);
            return null;
        }

        private static HotelHint RepairHint(HotelState state, PlayerState player)
        {
            RoomState room = NearestRoom(state, player, r => r.leak, "sink");
            if (room != null) return ToolHint(state, player, "toolbox", room);
            room = NearestRoom(state, player, r => r.water > .001f, "water");
            return room == null ? null : ToolHint(state, player, "mop", room);
        }

        private static HotelHint ToolHint(HotelState state, PlayerState player, string kind, RoomState room)
        {
            ItemState tool = NearestItem(state, player, i => i.kind == kind);
            bool repair = kind == "toolbox";
            string name = repair ? "ящик инструментов" : "швабру";
            if (tool == null) return Hint("Инструмент у напарника", "Для номера " + room.number + " нужен " + name + ". Договоритесь с напарником или возьмите другую задачу по Tab.", (repair ? "sink_" : "water_") + room.number);
            return Hint(repair ? "Остановите протечку" : "Уберите оставшуюся воду", "Возьмите " + name + " кнопкой E. В номере " + room.number + " удерживайте E у " + (repair ? "протекающей раковины. После ремонта потребуется швабра." : "лужи. Швабра убирает воду, но не чинит раковину."), tool.id);
        }

        private static HotelHint SupplyHint(HotelState state, PlayerState player, string kind, int number, bool request)
        {
            bool linen = kind == "linen";
            string supply = linen ? "чистое бельё" : "полотенце";
            string station = linen ? "linen" : "towels";
            ItemState loose = NearestItem(state, player, i => i.kind == kind);
            string title = request ? "Гость просит полотенце: " + number : "Подготовьте номер " + number;
            if (loose != null) return Hint(title, "Подберите лежащее " + supply + " кнопкой E и отнесите в номер " + number + (linen ? ". У голой кровати удерживайте E." : ". У стойки полотенец нажмите E."), loose.id);
            if ((linen ? state.linenStock : state.towelStock) <= 0)
                return Hint("Запас закончился", "На полке закончилось " + supply + ". Проверьте, нет ли его у напарника. Новые запасы приходят между днями; выберите другую доступную задачу по Tab.", station);
            return Hint(title, "Возьмите " + supply + " с полки у склада кнопкой E. Отнесите в номер " + number + (linen ? " и удерживайте E у голой кровати." : " и нажмите E у стойки полотенец."), station);
        }

        private static HotelHint BagDestination(HotelState state, ItemState bag)
        {
            GuestState owner = state.guests.Find(g => g.id == bag.ownerGuest);
            if (owner == null) return null;
            if (CanDeliverBag(state, bag)) return Hint("Багаж: " + owner.name + " → " + owner.room, "Отнесите чемодан к месту багажа в номере " + owner.room + " и нажмите E. " + (state.mvp == null ? "Доставка в чужой номер не принимается." : "Чужой номер вызовет жалобу: перенесите багаж к его владельцу."), "bag_" + owner.room);
            if (owner.stage == "queue") return Hint("Сначала зарегистрируйте владельца", owner.name + " ещё ждёт номер. На стойке нажмите E и " + (state.mvp == null ? "заселите гостей по порядку очереди" : "явно выберите этого гостя, затем подходящий номер") + "; затем появится место для багажа.", "desk");
            return null;
        }

        private static bool CanDeliverBag(HotelState state, ItemState bag)
        {
            GuestState owner = state.guests.Find(g => g.id == bag.ownerGuest);
            return owner != null && (owner.stage == "walking" || owner.stage == "staying") && state.rooms.Exists(r => r.number == owner.room && r.guestId == owner.id);
        }

        private static bool WaitingForBag(HotelState state, ItemState bag)
        {
            GuestState owner = state.guests.Find(g => g.id == bag.ownerGuest);
            return owner != null && !owner.luggageDelivered && (owner.stage == "queue" || CanDeliverBag(state, bag));
        }

        private static RoomState RequestedTowelRoom(HotelState state, PlayerState player)
        {
            return NearestRoom(state, player, r => state.guests.Exists(g => g.id == r.guestId && g.room == r.number && g.towelRequested && (g.stage == "walking" || g.stage == "staying")), "towel");
        }

        private static RoomState NearestRoom(HotelState state, PlayerState player, Predicate<RoomState> eligible, string kind)
        {
            RoomState nearest = null;
            float best = float.PositiveInfinity;
            foreach (RoomState room in state.rooms)
            {
                string target = kind + "_" + room.number;
                if (room.mvp?.owned == false || !eligible(room) || state.players.Exists(p => p.id != player.id && p.workTarget == target)) continue;
                float distance = target == player.workTarget ? -1 : (HotelLayout.Target(target) - player.position).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = room;
            }
            return nearest;
        }

        private static ItemState NearestItem(HotelState state, PlayerState player, Predicate<ItemState> eligible)
        {
            ItemState nearest = null;
            float best = float.PositiveInfinity;
            foreach (ItemState item in state.items)
            {
                if (item.consumed || item.holder >= 0 || !eligible(item)) continue;
                float distance = (item.position - player.position).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = item;
            }
            return nearest;
        }

        private static HotelHint ReopenRoom(PlayerState player)
        {
            return Hint("Нужен открытый номер", player.id == 0 ? "Подойдите к доске, нажмите E и откройте подходящий номер для продаж. Если он грязный, подготовьте кровать и устраните аварии." : "Попросите хозяина открыть свободный номер для продаж на доске. Вы можете помочь с бельём и ремонтом.", "board");
        }
        private static HotelHint MvpHint(HotelState state, PlayerState player, ItemState held)
        {
            if (!string.IsNullOrEmpty(player.workTarget))
                return Hint("Продолжайте работу", HotelPresentation.FocusText(state, player.id, player.workTarget, "Удерживайте E до завершения."), player.workTarget);
            if (held != null)
            {
                if (held.kind == "dirtytowel" || held.kind == "towel" && held.condition == "dirty") return Hint("Грязное полотенце — в приёмник", "У приёмника на складе нажмите E. Чистое полотенце возьмите с полки.", "hamper");
                if (held.kind == "cart")
                {
                    foreach (var cargo in state.items)
                        if (!cargo.consumed && cargo.kind == "bag" && cargo.placedRoom == -1 && CanDeliverBag(state, cargo)) return BagDestination(state, cargo);
                    var nextBag = NearestItem(state, player, i => i.kind == "bag" && WaitingForBag(state, i) && HotelOperationsRules.CartLoadBlockReason(state, i) == "");
                    if (nextBag != null) return Hint("Загрузите багаж", "E у чемодана — загрузить. Всего четыре места; большой чемодан занимает два. Затем E у подставки владельца.", nextBag.id);
                    return PutDown("Нет доступного багажа для загрузки. Загруженные чемоданы сохранятся на тележке.");
                }
                if (held.kind == "coffee")
                {
                    var request = state.mvp.requests.Find(r => r.status == "open" && r.kind == "coffee" && state.guests.Exists(g => g.id == r.guestId && (g.stage == "walking" || g.stage == "staying")));
                    var guest = request == null ? null : state.guests.Find(g => g.id == request.guestId);
                    return guest == null ? PutDown("Сейчас никто не просит кофе. Чашку можно оставить рядом.") : Hint("Кофе для " + guest.name, "Отнесите чашку к столику в номере " + guest.room + " и нажмите E.", "coffee_" + guest.room);
                }
                if (held.kind == "toolbox" || held.kind == "plunger")
                {
                    string target = MvpRepairTarget(state, player, held.kind);
                    if (target != "") return Hint("Восстановите оборудование", HotelPresentation.FocusText(state, player.id, target, "Удерживайте E."), target);
                    return PutDown("Для этого инструмента сейчас нет свободной поломки.");
                }
                if (held.kind == "mop" && NearestRoom(state, player, r => r.water > .001f, "water") == null)
                {
                    var dirty = NearestRoom(state, player, r => r.mvp != null && r.mvp.dirt > .001f, "clean");
                    if (dirty != null) return Hint("Уберите грязь: " + dirty.number, "Со шваброй удерживайте E на грязном участке пола. Лужи убираются отдельно.", "clean_" + dirty.number);
                }
                return HeldHint(state, player, held);
            }
            if (state.phase == "summary") return Hint("Итоги и следующий день", player.id == 0 ? "На стойке откройте итоги и перейдите к подготовке. Многодневные гости останутся; грязь и поломки сохранятся." : "Хозяин подводит итоги на стойке. Многодневные гости продолжают проживание.", "desk");
            if (state.phase == "closing") return Hint("Завершите обслуживание", "Примите оплату у выезжающих. Хозяин подводит итоги у стойки; продолжающие проживание гости останутся.", "desk");
            if (state.phase == "preparation")
            {
                var prepare = PrepareRoomHint(state, player, true);
                if (prepare != null) return prepare;
                return Hint("Подготовьте новую смену", "На доске: цены, прогноз, восемь улучшений и номера 105–106. Когда команда готова, хозяин открывает отель у стойки.", "board");
            }
            var guided = GuidedHint(state, player);
            if (guided != null) return guided;
            var checkout = state.guests.Find(g => g.stage == "checkout" && !g.paid);
            if (checkout != null) return Hint("Гость ждёт расчёта", checkout.name + ": подойдите к стойке и примите оплату во вкладке «Гости».", "desk");
            var queue = state.guests.Find(g => g.stage == "queue");
            if (queue != null) return Hint("Выберите гостя для заселения", "На стойке откройте «Гости», выберите человека, затем номер. Проверьте вместимость и требования; причина запрета показана рядом.", "desk");
            foreach (var request in state.mvp.requests)
            {
                if (request.status != "open") continue;
                var guest = state.guests.Find(g => g.id == request.guestId && (g.stage == "walking" || g.stage == "staying"));
                if (guest == null) continue;
                if (request.kind == "towel") return SupplyHint(state, player, "towel", guest.room, true);
                if (request.kind == "coffee") return Hint("Гость просит кофе: " + guest.room, "Освободите руки. В лобби удерживайте E у кофейной станции, затем доставьте чашку к столику гостя.", "coffee");
                if (request.kind == "cleaning")
                {
                    HotelHint cleaning = MvpCleaningHint(state, player, guest.room);
                    if (cleaning != null) return cleaning;
                }
            }
            var bag = NearestItem(state, player, i => i.kind == "bag" && CanDeliverBag(state, i) && !state.guests.Find(g => g.id == i.ownerGuest).luggageDelivered);
            if (bag != null) return Hint("Доставьте собственный багаж гостя", "Проверьте бирку чемодана. Чужой багаж нужно забрать и перенести в правильный номер.", bag.id);
            string repair = MvpRepairTarget(state, player, "toolbox");
            if (repair != "") return MvpToolHint(state, player, "toolbox", repair);
            repair = MvpRepairTarget(state, player, "plunger");
            if (repair != "") return MvpToolHint(state, player, "plunger", repair);
            var towelRoom = NearestRoom(state, player, r => r.mvp != null && r.mvp.dirtyTowels > 0, "dirtytowel");
            if (towelRoom != null) return Hint("Соберите грязные полотенца", "Свободными руками удерживайте E у полотенец в № " + towelRoom.number + ". Отнесите их в приёмник.", "dirtytowel_" + towelRoom.number);
            var dirtyRoom = NearestRoom(state, player, r => r.mvp != null && r.mvp.dirt > .001f, "clean");
            if (dirtyRoom != null) return MvpToolHint(state, player, "mop", "clean_" + dirtyRoom.number);
            return RepairHint(state, player) ?? PrepareRoomHint(state, player, false) ?? Hint("Отель под контролем", "Tab — номера, гости, операции и расписание. F или средняя кнопка мыши отмечает объект для напарника.", "");
        }
        private static HotelHint MvpToolHint(HotelState state, PlayerState player, string kind, string target)
        {
            ItemState tool = NearestItem(state, player, i => i.kind == kind);
            return tool == null ? Hint("Нужен инструмент", HotelPresentation.ItemLabel(kind) + " у коллеги. Отметьте задачу кнопкой F и договоритесь о работе.", target) :
                Hint("Возьмите инструмент", HotelPresentation.ItemLabel(kind) + ": E — взять, затем удерживайте E у цели. Tab показывает все задачи.", tool.id);
        }
        private static HotelHint MvpCleaningHint(HotelState state, PlayerState player, int number)
        {
            RoomState room = state.rooms.Find(r => r.number == number && r.mvp?.owned == true);
            if (room == null) return null;
            if (room.mvp.dirtyTowels > 0 && !state.players.Exists(p => p.id != player.id && p.workTarget == "dirtytowel_" + number))
                return Hint("Уборка: использованные полотенца", "В № " + number + " удерживайте E у грязных полотенец. Затем отнесите их в приёмник.", "dirtytowel_" + number);
            if ((room.trash || room.mvp.binFill > .05f) && !state.players.Exists(p => p.id != player.id && p.workTarget == "trash_" + number))
                return Hint("Уборка: вынесите мусор", "Свободными руками удерживайте E у корзины № " + number + ", затем отнесите мешок в бак.", "trash_" + number);
            if (room.water > .01f && !state.players.Exists(p => p.id != player.id && p.workTarget == "water_" + number))
                return MvpToolHint(state, player, "mop", "water_" + number);
            if (room.mvp.dirt > .05f && !state.players.Exists(p => p.id != player.id && p.workTarget == "clean_" + number))
                return MvpToolHint(state, player, "mop", "clean_" + number);
            return null;
        }
        private static string MvpRepairTarget(HotelState state, PlayerState player, string tool)
        {
            if (tool == "toolbox")
            {
                if (state.mvp.utilities.waterFault && !state.players.Exists(p => p.id != player.id && p.workTarget == "utility_water")) return "utility_water";
                if (state.mvp.utilities.powerFault && !state.players.Exists(p => p.id != player.id && p.workTarget == "utility_power")) return "utility_power";
            }
            foreach (string kind in tool == "plunger" ? new[] { "toilet" } : new[] { "sink", "tv", "lamp" })
            {
                var room = NearestRoom(state, player, r => HotelOperationsRules.Equipment(state, r.number, kind)?.localFault == true, kind);
                if (room != null) return kind + "_" + room.number;
            }
            return "";
        }
        private static HotelHint PutDown(string reason) { return Hint("Освободите руки", reason + " Q — положить предмет рядом на доступный пол.", ""); }
        private static HotelHint Hint(string title, string body, string target)
        {
            return new HotelHint { title = title, body = body, targetId = target };
        }
    }
}
