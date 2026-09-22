using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    // The host calls this class serially. Commands never trust a client supplied owner or work duration.
    public sealed partial class HotelSimulation
    {
        public const float InteractionDistance = 2.6f;
        public const float HeartbeatTimeout = .8f;
        public const int ToolboxPrice = 220, BedsPrice = 400, CartPrice = 320;
        public HotelState State { get; private set; }
        public string CatalogWarning { get { return catalog.Warning; } }
        private readonly HotelGuestCatalog catalog;
        private double workClock;
        private readonly Dictionary<ulong, double> heartbeats = new Dictionary<ulong, double>();
        private static readonly Vector3 Reception = new Vector3(.7f, 0, -2.3f);
        private static readonly Vector3 Exit = new Vector3(0, 0, -6.3f);

        public HotelSimulation(HotelState state = null) : this(state, null) { }

        public HotelSimulation(HotelState state, HotelGuestCatalog guestCatalog)
        {
            catalog = guestCatalog ?? HotelGuestCatalog.Load();
            State = state ?? NewHotel();
            HotelSaveStore.NormalizeLegacy(State);
            HotelSaveStore.Validate(State);
            // A snapshot may contain a half-finished hold. A fresh host requires fresh input.
            foreach (PlayerState player in State.players) Cancel(player);
        }

        private static HotelState NewHotel()
        {
            var state = new HotelState { contentVersion = 1, guidedOpening = true };
            for (int number = 101; number <= 104; number++) state.rooms.Add(new RoomState { number = number });
            state.rooms[1].bed = 1;
            state.rooms[1].trash = true;
            state.rooms[2].towel = false;
            state.rooms[3].bed = 0;
            state.items.Add(new ItemState { id = "toolbox_1", kind = "toolbox", position = HotelLayout.Target("tools") });
            state.items.Add(new ItemState { id = "mop_1", kind = "mop", position = new Vector3(-3.2f, .4f, -2.1f) });
            return state;
        }

        public void Join(ulong id)
        {
            if (id > long.MaxValue || Player(id) != null || State.players.Count >= 2) return;
            Vector3 spawn = HotelLayout.Spawn + new Vector3(id == 0 ? -.5f : .5f, 0, 0);
            State.players.Add(new PlayerState { id = id, position = spawn });
        }

        public void Leave(ulong id)
        {
            PlayerState player = Player(id);
            if (player == null) return;
            foreach (ItemState item in State.items)
            {
                if (item.holder != (long)id) continue;
                item.holder = -1;
                item.position = SafePosition(player.position);
            }
            Cancel(player);
            State.players.Remove(player);
            UpdateCartCargo();
        }

        public string Execute(ulong playerId, HotelCommand command)
        {
            PlayerState player = Player(playerId);
            if (player == null) return "Игрок не подключён к отелю.";
            if (command == null || string.IsNullOrEmpty(command.action)) return "Пустая команда.";
            string target = command.target ?? "";
            if (State.mvp != null && TryMvpCommand(player, command, out string mvpError)) return mvpError;
            switch (command.action)
            {
                case "pose":
                    if (!Finite(command.position) || !Finite(command.yaw) || !Finite(command.pitch) || !Walkable(command.position) || !OwnedArea(command.position))
                        return "Позиция вне доступного отеля.";
                    player.position = new Vector3(command.position.x, .1f, command.position.z);
                    player.yaw = command.yaw % 360f;
                    player.pitch = Mathf.Clamp(command.pitch, -89, 89);
                    ItemState carried = Held(player);
                    if (carried != null) carried.position = player.position + Vector3.up * .8f;
                    if (!string.IsNullOrEmpty(player.workTarget) && WorkError(player, player.workTarget) != "") Cancel(player);
                    UpdateCartCargo();
                    return "";
                case "pickup": return Pickup(player, target);
                case "drop": return Drop(player, command.position);
                case "interact": return Interact(player, target);
                case "beginwork": return BeginWork(player, target);
                case "heartbeat":
                    if (player.workTarget != target || string.IsNullOrEmpty(target)) return "Сначала начните работу.";
                    string error = WorkError(player, target);
                    if (error != "") return error;
                    heartbeats[player.id] = workClock;
                    player.workLastSeen = 0;
                    return "";
                case "cancelwork": Cancel(player); return "";
                case "skipTutorial": case "resumeTutorial":
                    if (playerId != 0) return "Это действие подтверждает хозяин отеля.";
                    State.tutorialSkipped = command.action == "skipTutorial";
                    return "";
                case "endGuidedOpening":
                    if (playerId != 0) return "Это действие подтверждает хозяин отеля.";
                    HotelDirector.EndGuidedOpening(State);
                    return "";
                case "checkin":
                    if (!Near(player, HotelLayout.Target("desk"))) return "Подойдите к стойке регистрации.";
                    return CheckIn(command.number);
                case "checkout":
                    if (!Near(player, HotelLayout.Target("desk"))) return "Подойдите к стойке регистрации.";
                    return CheckOut(command.number);
                case "compensate":
                    if (!Near(player, HotelLayout.Target("desk"))) return "Подойдите к стойке регистрации.";
                    return Compensate(command.number);
                case "open": case "finish": case "nextday": case "upgrade": case "toggleRoom":
                    if (playerId != 0) return "Это действие подтверждает хозяин отеля.";
                    string station = command.action == "upgrade" || command.action == "toggleRoom" ? "board" : "desk";
                    if (!Near(player, HotelLayout.Target(station))) return station == "desk" ? "Подойдите к стойке регистрации." : "Подойдите к доске управления.";
                    if (command.action == "open") return Open();
                    if (command.action == "finish") return Finish();
                    if (command.action == "nextday") return NextDay();
                    if (command.action == "upgrade") return Upgrade(target);
                    RoomState room = Room(command.number);
                    if (room == null) return "Такого номера нет.";
                    room.outOfService = !room.outOfService;
                    return "";
                default: return "Неизвестная команда.";
            }
        }

        public void Tick(float dt)
        {
            if (!Finite(dt) || dt <= 0) return;
            // Long host stalls do not generate hours of offline progress or finish unattended work.
            float remaining = Mathf.Min(dt, 30f);
            while (remaining > .00001f)
            {
                float step = Mathf.Min(remaining, .1f);
                Step(step);
                remaining -= step;
            }
        }

        private void Step(float dt)
        {
            double start = workClock;
            workClock += dt;
            foreach (PlayerState player in State.players)
            {
                if (string.IsNullOrEmpty(player.workTarget)) continue;
                if (!heartbeats.TryGetValue(player.id, out double last) || WorkError(player, player.workTarget) != "")
                { Cancel(player); continue; }
                // Only the part of this frame covered by a live lease counts as work.
                float active = (float)Math.Max(0, Math.Min(workClock, last + HeartbeatTimeout) - start);
                player.workLastSeen = (float)(workClock - last);
                player.workProgress = Mathf.Min(1, player.workProgress + active / WorkDuration(player.workTarget));
                if (player.workProgress >= 1) CompleteWork(player);
                else if (workClock > last + HeartbeatTimeout) Cancel(player);
            }
            if (State.mvp != null) { MvpStep(dt); return; }
            if (State.phase != "open" && State.phase != "closing") return;
            HotelDirector.Advance(State);
            if (State.phase == "open")
            {
                if (!HotelDirector.ClockHeld(State)) State.time = Mathf.Min(State.dayLength, State.time + dt);
                SpawnDueGuests();
                if (State.time >= State.dayLength)
                {
                    State.phase = "closing";
                    State.notice = "Приём закрыт. Завершите обслуживание или подведите итоги на стойке.";
                }
                foreach (RoomState room in State.rooms)
                    if (room.leak) room.water = Mathf.Min(1, room.water + dt * .007f);
            }
            foreach (GuestState guest in State.guests) TickGuest(guest, dt);
            UpdateCartCargo();
        }

        private void SpawnDueGuests()
        {
            while (HotelDirector.ArrivalDue(State))
            {
                int sequence = State.arrivals++;
                int id = State.nextGuest++;
                string[] names = { "Анна", "Борис", "Вера", "Григорий", "Дарья", "Егор" };
                var guest = new GuestState
                {
                    id = id, name = names[(id - 1) % names.Length], kind = sequence == 0 ? "patient" : "tourist",
                    trait = sequence == 0 ? "Терпеливый" : sequence % 2 == 0 ? "Любит порядок" : "Спешит",
                    position = new Vector3(-.8f + sequence * .55f, 0, -3.3f - sequence * .5f)
                };
                if (State.contentVersion == 1)
                {
                    string profile = sequence == 0 ? "patient" : State.day == 1 ? (sequence == 1 ? "tidy" : "hurried") : (sequence % 2 == 1 ? "hurried" : "tidy");
                    catalog.ApplySnapshot(guest, profile);
                }
                State.guests.Add(guest);
                State.items.Add(new ItemState { id = "luggage_" + id, kind = "bag", ownerGuest = id,
                    position = new Vector3(2.5f + sequence * .6f, .25f, -4.5f) });
                State.notice = guest.name + " ждёт регистрации. Чемодан отмечен именем владельца.";
                HotelDirector.Arrived(State, guest);
            }
        }

        private void TickGuest(GuestState guest, float dt)
        {
            bool held = HotelDirector.ClockHeld(State);
            bool snapshot = guest.profileVersion == 1;
            switch (guest.stage)
            {
                case "queue":
                    if (!held) guest.waited += dt;
                    if (!held && guest.waited > (snapshot ? guest.patienceWarning : guest.kind == "patient" ? 90 : 55)) Remember(guest, "Долго ждал регистрации", -12);
                    if ((!held && guest.waited > (snapshot ? guest.patienceLimit : 210)) || State.phase == "closing")
                    {
                        Remember(guest, "Не удалось заселиться", -35);
                        Review(guest);
                        guest.stage = "leaving";
                        RetireBags(guest.id);
                    }
                    break;
                case "walking":
                    if (MoveRoute(guest, true, dt))
                    {
                        guest.stage = "staying";
                        RoomState room = Room(guest.room);
                        if (room.trash) Remember(guest, "В номере оставили мусор", -12);
                        if (!room.towel) Remember(guest, "В номере не было полотенца", -10);
                        if (room.water > .1f || room.leak) Remember(guest, "При заселении было сыро", -10);
                        if (room.upgraded) Remember(guest, "Удобная новая кровать", 8);
                        room.bed = 1;
                        room.trash = true;
                    }
                    break;
                case "staying":
                    if (!held) guest.stay += dt;
                    RoomState occupied = Room(guest.room);
                    // During guided basics the request clock starts only once this guest has a bag.
                    if (State.contentVersion == 1 && (!held || guest.luggageDelivered) && !guest.memories.Contains("Попросил дополнительное полотенце")) guest.requestElapsed += dt;
                    float requestAge = State.contentVersion == 1 ? guest.requestElapsed : guest.stay;
                    if (requestAge >= (snapshot ? guest.requestDelay : 25) && !guest.memories.Contains("Попросил дополнительное полотенце"))
                    {
                        guest.towelRequested = true;
                        Remember(guest, "Попросил дополнительное полотенце", 0);
                        State.notice = guest.name + ": дополнительное полотенце в номер " + guest.room + ".";
                    }
                    if (guest.towelRequested && !held) guest.requestWait += dt;
                    bool lateTowel = State.contentVersion == 1 ? guest.requestWait >= (snapshot ? guest.requestGrace : 40) : guest.stay >= 65;
                    if (!held && lateTowel && guest.towelRequested) Remember(guest, "Не дождался дополнительного полотенца", -14);
                    if (!held && guest.stay >= (snapshot ? guest.luggageGrace : 60) && !guest.luggageDelivered) Remember(guest, "Багаж доставляли долго", -15);
                    // One bounded leak event per day, persisted by the guest's memory.
                    if (State.contentVersion == 0 && guest.id % (State.day == 1 ? 3 : 4) == 1 && guest.stay >= 80 &&
                        !guest.memories.Contains("Раковина начала протекать"))
                    {
                        occupied.leak = true;
                        Remember(guest, "Раковина начала протекать", 0);
                        State.notice = "Протечка в номере " + occupied.number + ": нужны инструменты, затем швабра.";
                    }
                    if (occupied.water > .3f) Remember(guest, "Пришлось ходить по мокрому полу", -18);
                    if ((!held && guest.stay >= (snapshot ? guest.stayDuration : 160)) || State.phase == "closing")
                    {
                        guest.stage = "checkout";
                        guest.waited = 0;
                        State.notice = guest.name + " идёт к стойке для выезда.";
                    }
                    break;
                case "checkout":
                    if (MoveRoute(guest, false, dt))
                    {
                        guest.waited += dt;
                        if (guest.waited > (snapshot ? guest.checkoutWarning : 45)) Remember(guest, "Долго оформляли выезд", -10);
                        // An unattended desk cannot trap the shift forever.
                        if (guest.waited > (snapshot ? guest.checkoutLimit : 100)) Settle(guest);
                    }
                    break;
                case "leaving":
                    guest.position = Vector3.MoveTowards(guest.position, Exit, dt * 1.6f);
                    if (FlatDistance(guest.position, Exit) < .02f) guest.stage = "gone";
                    break;
            }
        }

        // Position on these axis-aligned segments is sufficient to rebuild a route after loading.
        // A room is entered/exited only through its door. No transient NavMesh index is saved.
        private bool MoveRoute(GuestState guest, bool inbound, float dt)
        {
            float distance = dt * 1.6f;
            Vector3 center = HotelLayout.RoomCenter(guest.room);
            for (int i = 0; i < 6 && distance > .00001f; i++)
            {
                Vector3 p = guest.position;
                Vector3 target;
                if (inbound)
                {
                    if (p.z < -2 - .001f) target = new Vector3(1.85f, 0, -2);
                    else if (p.z < 1 - .001f) target = new Vector3(1.85f, 0, 1);
                    else if (p.z < 2 - .001f) target = new Vector3(0, 0, 2);
                    else if (Math.Abs(p.z - center.z) > .001f) target = new Vector3(0, 0, center.z);
                    else if (Math.Abs(p.x) < 1.5f - .001f) target = HotelLayout.Door(guest.room);
                    else target = center;
                }
                else
                {
                    if (Math.Abs(p.x) > 1.5f + .001f && p.z > 2.001f) target = HotelLayout.Door(guest.room);
                    else if (Math.Abs(p.x) > .001f && p.z > 2.001f) target = new Vector3(0, 0, center.z);
                    else if (p.z > 2 + .001f) target = new Vector3(0, 0, 2);
                    else if (p.z > 1 + .001f) target = new Vector3(1.85f, 0, 1);
                    else if (p.z > -2 + .001f) target = new Vector3(1.85f, 0, -2);
                    else target = Reception;
                }
                float length = Vector3.Distance(p, target);
                guest.position = Vector3.MoveTowards(p, target, distance);
                distance -= length;
                if (FlatDistance(guest.position, inbound ? center : Reception) < .001f) return true;
            }
            return false;
        }

        // Shared read-only rules for the desk UI; command execution still checks desk distance.
        public static string CheckInBlockReason(HotelState state, int number)
        {
            if (state?.mvp != null) return HotelHospitalityRules.AssignmentBlockReason(state, state.guests.Find(g => g.stage == "queue")?.id ?? 0, number);
            if (state == null || state.phase != "open") return "Заселение доступно в открытую смену.";
            RoomState room = state.rooms.Find(r => r.number == number);
            if (room == null) return "Такого номера нет.";
            if (room.guestId != 0) return "Номер уже занят.";
            if (room.outOfService) return "Номер закрыт для продаж.";
            if (room.bed != 2) return "Сначала застелите чистую кровать.";
            if (room.leak || room.water > .65f) return "Сначала устраните аварийное состояние номера.";
            GuestState guest = state.guests.Find(g => g.stage == "queue");
            if (guest == null) return "В очереди нет гостей.";
            return "";
        }

        private string CheckIn(int number)
        {
            string blocked = CheckInBlockReason(State, number);
            if (blocked != "") return blocked;
            RoomState room = Room(number);
            GuestState guest = State.guests.Find(g => g.stage == "queue");
            guest.room = number;
            guest.stage = "walking";
            room.guestId = guest.id;
            State.notice = guest.name + " заселён в " + number + ". Доставьте его чемодан.";
            HotelOnboarding.Record(State, HotelTutorialSkill.CheckIn);
            return "";
        }

        private string CheckOut(int id)
        {
            GuestState guest = Guest(id);
            if (guest == null) return "Гость не найден.";
            if (guest.paid) return "Выезд уже оплачен.";
            if (guest.stage != "checkout") return "Гость пока не готов к выезду.";
            if (FlatDistance(guest.position, Reception) > .15f) return "Дождитесь гостя у стойки.";
            Settle(guest);
            HotelOnboarding.Record(State, HotelTutorialSkill.CheckOut);
            return "";
        }

        private void Settle(GuestState guest)
        {
            if (guest.paid) return;
            if (!guest.luggageDelivered) Remember(guest, "Багаж так и не доставили", -12);
            if (guest.towelRequested) Remember(guest, "Не дождался дополнительного полотенца", -14);
            int payment = 80 + Mathf.RoundToInt(guest.satisfaction * .5f) + (State.betterBeds ? 20 : 0);
            guest.paid = true;
            guest.stage = "leaving";
            guest.position = Reception;
            State.cash += payment;
            State.earned += payment;
            State.served++;
            RoomState room = Room(guest.room);
            if (room != null && room.guestId == guest.id)
            {
                room.guestId = 0;
                room.bed = 1;
                room.trash = true;
                room.towel = false;
            }
            Log("Выезд #" + guest.id + " · " + guest.name + ": +" + payment);
            Review(guest);
            RetireBags(guest.id);
        }

        private string Compensate(int id)
        {
            GuestState guest = Guest(id);
            if (guest == null || (guest.stage != "staying" && guest.stage != "checkout")) return "Компенсация доступна проживающему гостю.";
            if (guest.compensated) return "Этому гостю уже выдали компенсацию.";
            if (State.cash < 25) return "Для компенсации нужно 25.";
            guest.compensated = true;
            State.cash -= 25;
            State.expenses += 25;
            Remember(guest, "Персонал предложил компенсацию", 18);
            Log("Компенсация #" + id + ": −25");
            return "";
        }

        private string Open()
        {
            if (State.mvp != null) return MvpOpen();
            if (State.phase != "preparation") return "Смена уже открыта или ещё не подведены итоги.";
            if (!State.rooms.Exists(r => !r.outOfService && r.guestId == 0 && r.bed == 2 && !r.leak && r.water <= .65f))
                return "Подготовьте хотя бы один доступный номер с чистой кроватью.";
            State.phase = "open";
            State.notice = "Отель открыт. Гости ждут у стойки.";
            SpawnDueGuests();
            return "";
        }

        private string Finish()
        {
            if (State.mvp != null) return MvpFinish();
            if (State.phase != "closing" && State.phase != "open") return "Сейчас нет смены для завершения.";
            foreach (GuestState guest in State.guests)
            {
                if (guest.stage == "queue")
                {
                    Remember(guest, "Не удалось заселиться", -35);
                    Review(guest);
                    RetireBags(guest.id);
                }
                else if (guest.stage == "walking" || guest.stage == "staying" || guest.stage == "checkout") Settle(guest);
                guest.stage = "gone";
            }
            foreach (PlayerState player in State.players) Cancel(player);
            State.phase = "summary";
            State.notice = "Смена завершена. Обслужено: " + State.served + "; доход: " + State.earned + "; расходы: " + State.expenses + ".";
            return "";
        }

        private string NextDay()
        {
            if (State.mvp != null) return MvpNextDay();
            if (State.phase != "summary") return "Сначала завершите текущую смену.";
            int looseLinen = State.items.FindAll(i => !i.consumed && i.kind == "linen").Count;
            int looseTowels = State.items.FindAll(i => !i.consumed && i.kind == "towel").Count;
            int linen = Math.Max(0, 12 - State.linenStock - looseLinen);
            int towels = Math.Max(0, 12 - State.towelStock - looseTowels);
            int cost = 30 + linen * 4 + towels * 2;
            State.cash -= cost; // Supplies are available on credit; a bad shift is recoverable.
            State.linenStock += linen;
            State.towelStock += towels;
            State.day++;
            State.time = 0;
            State.arrivals = 0;
            State.nextArrivalTime = 0;
            State.dailyLeakIssued = false;
            State.guidedStage = HotelDirector.Released;
            State.guidedGuestId = 0;
            State.guidedLeakRoom = 0;
            State.guidedRepairDone = false;
            State.guidedMopDone = false;
            State.earned = 0;
            State.expenses = cost;
            State.served = 0;
            State.phase = "preparation";
            State.guests.Clear();
            State.items.RemoveAll(i => i.consumed);
            Log("День " + State.day + ": снабжение и содержание −" + cost);
            State.notice = "День " + State.day + ". Запасы пополнены; грязь и поломки остались. Подготовьтесь и откройте отель.";
            HotelOnboarding.Record(State, HotelTutorialSkill.NextDay);
            if (promoteAfterShift) TryPromoteLegacy();
            return "";
        }

        private string Upgrade(string target)
        {
            if (State.phase != "preparation" && State.phase != "summary") return "Улучшения покупают между сменами.";
            int price;
            bool owned;
            switch (target)
            {
                case "toolbox": price = ToolboxPrice; owned = State.secondToolbox; break;
                case "beds": price = BedsPrice; owned = State.betterBeds; break;
                case "cart": price = CartPrice; owned = State.cartUpgrade; break;
                default: return "Неизвестное улучшение.";
            }
            if (owned) return "Улучшение уже куплено.";
            if (State.cash < price) return "Недостаточно денег: нужно " + price + ".";
            State.cash -= price;
            State.expenses += price;
            if (target == "toolbox")
            {
                State.secondToolbox = true;
                State.items.Add(new ItemState { id = "toolbox_2", kind = "toolbox", position = HotelLayout.Target("tools") + new Vector3(.8f, 0, 0) });
            }
            else if (target == "beds")
            {
                State.betterBeds = true;
                foreach (RoomState room in State.rooms) room.upgraded = true;
            }
            else
            {
                State.cartUpgrade = true;
                State.items.Add(new ItemState { id = "cart_1", kind = "cart", position = new Vector3(2.3f, .2f, -3.4f) });
            }
            Log("Улучшение " + target + ": −" + price);
            return "";
        }

        private string Interact(PlayerState player, string target)
        {
            if (State.mvp != null && TryOperationsInteract(player, target, out string mvpError)) return mvpError;
            if (target == "desk" || target == "board") return "Откройте меню стойки или доски.";
            ItemState item = Item(target);
            if (item != null) return Pickup(player, target);
            if (target == "linen" || target == "towels")
            {
                if (!Near(player, HotelLayout.Target(target))) return "Подойдите к полке.";
                if (Held(player) != null) return "Сначала освободите руки.";
                bool linen = target == "linen";
                if ((linen ? State.linenStock : State.towelStock) <= 0) return "Запас закончился. Пополнение между сменами.";
                Cancel(player);
                if (linen) State.linenStock--; else State.towelStock--;
                Give(player, linen ? "linen" : "towel");
                return "";
            }
            if (target == "hamper" || target == "bin")
            {
                if (!Near(player, HotelLayout.Target(target))) return "Подойдите к приёмнику.";
                ItemState held = Held(player);
                if (held == null || held.kind != (target == "hamper" ? "dirtylinen" : "trashbag")) return "В руках нет подходящего предмета.";
                Consume(player, held);
                return "";
            }
            if (!TryRoomTarget(target, out string kind, out RoomState room)) return "Здесь нечего использовать.";
            if (!Near(player, HotelLayout.Target(target))) return "Подойдите ближе к объекту.";
            if (kind == "door") return "";
            if (kind == "towel") return DeliverTowel(player, room);
            if (kind == "bag") return DeliverBag(player, room);
            return BeginWork(player, target);
        }

        private string Pickup(PlayerState player, string id)
        {
            ItemState item = Item(id);
            if (item == null || item.consumed) return "Предмет уже использован или не найден.";
            if (item.holder >= 0) return item.holder == (long)player.id ? "" : "Предмет уже держит другой сотрудник.";
            if (!Near(player, item.position)) return "Подойдите ближе к предмету.";
            ItemState held = Held(player);
            if (held != null)
            {
                if (held.kind != "cart" || item.kind != "bag") return "Сначала освободите руки.";
                if (item.placedRoom == -1) return "Этот чемодан уже на тележке.";
                if (State.mvp != null)
                {
                    string error = HotelOperationsRules.CartLoadBlockReason(State, item);
                    if (error != "") return error;
                }
                else if (State.items.FindAll(i => !i.consumed && i.placedRoom == -1).Count >= 2) return "На тележке помещаются два чемодана.";
                item.placedRoom = -1; // Unique cart cargo; independent of ownerGuest and player holder.
                GuestState owner = Guest(item.ownerGuest);
                if (owner != null) owner.luggageDelivered = false;
                UpdateCartCargo();
                if (State.mvp != null) RefreshRequestFulfillment();
                return "";
            }
            Cancel(player);
            item.holder = (long)player.id;
            item.placedRoom = 0;
            player.held = item.id;
            item.position = player.position + Vector3.up * .8f;
            if (item.kind == "bag")
            {
                GuestState owner = Guest(item.ownerGuest);
                if (owner != null) owner.luggageDelivered = false;
            }
            if (State.mvp != null) RefreshRequestFulfillment();
            return "";
        }

        private string Drop(PlayerState player, Vector3 position)
        {
            ItemState item = Held(player);
            if (item == null) return "В руках ничего нет.";
            if (!Finite(position) || !Walkable(position) || !OwnedArea(position) || FlatDistance(player.position, position) > 2 ||
                !SameAccessibleArea(player.position, position)) return "Поставьте предмет рядом на доступный пол.";
            Cancel(player);
            item.holder = -1;
            item.position = new Vector3(position.x, .25f, position.z);
            item.placedRoom = 0;
            player.held = "";
            UpdateCartCargo();
            if (State.mvp != null) RefreshRequestFulfillment();
            return "";
        }

        private string DeliverTowel(PlayerState player, RoomState room)
        {
            ItemState item = Held(player);
            if (item == null || item.kind != "towel") return "Принесите чистое полотенце с полки.";
            GuestState guest = Guest(room.guestId);
            if (room.towel && (guest == null || !guest.towelRequested)) return "Полотенце уже есть; дополнительного запроса нет.";
            room.towel = true;
            if (guest != null && guest.towelRequested)
            {
                guest.towelRequested = false;
                Remember(guest, "Принесли дополнительное полотенце", 8);
                HotelOnboarding.Record(State, HotelTutorialSkill.ExtraTowel);
            }
            Consume(player, item);
            return "";
        }

        private string DeliverBag(PlayerState player, RoomState room)
        {
            ItemState held = Held(player);
            if (held == null) return "Принесите чемодан гостя или тележку с багажом.";
            ItemState bag = held.kind == "cart" ? State.items.Find(i => !i.consumed && i.placedRoom == -1 && i.ownerGuest == room.guestId) : held;
            if (bag == null || bag.kind != "bag") return "В руках нет чемодана этого гостя.";
            GuestState guest = Guest(bag.ownerGuest);
            if (guest == null || guest.room != room.number || room.guestId != guest.id ||
                (guest.stage != "walking" && guest.stage != "staying")) return "Этот чемодан принадлежит гостю другого номера.";
            Cancel(player);
            bag.holder = -1;
            bag.placedRoom = room.number;
            bag.position = HotelLayout.RoomTarget("bag", room.number);
            if (held == bag) player.held = "";
            guest.luggageDelivered = true;
            Remember(guest, "Багаж доставили в номер", 5);
            UpdateCartCargo();
            HotelOnboarding.Record(State, HotelTutorialSkill.DeliverBag);
            return "";
        }

        private string BeginWork(PlayerState player, string target)
        {
            string error = WorkError(player, target);
            if (error != "") return error;
            foreach (PlayerState other in State.players)
                if (other.id != player.id && other.workTarget == target) return "Здесь уже работает другой сотрудник.";
            if (player.workTarget != target)
            {
                Cancel(player);
                player.workTarget = target;
                player.workProgress = 0;
            }
            player.workLastSeen = 0;
            heartbeats[player.id] = workClock;
            return "";
        }

        private string WorkError(PlayerState player, string target)
        {
            if (State.mvp != null) return OperationsWorkError(player, target);
            if (!TryRoomTarget(target, out string kind, out RoomState room)) return "Здесь нет работы.";
            if (!Near(player, HotelLayout.Target(target))) return "Подойдите ближе и удерживайте E.";
            ItemState item = Held(player);
            switch (kind)
            {
                case "bed":
                    if (room.guestId != 0) return "Кровать занята гостем. Дождитесь выезда.";
                    if (room.bed == 2) return "Кровать уже чистая.";
                    if (room.bed == 1 && item != null) return "Для грязного белья нужны свободные руки.";
                    if (room.bed == 0 && (item == null || item.kind != "linen")) return "Возьмите чистое бельё с полки.";
                    return "";
                case "trash":
                    if (!room.trash) return "Мусора нет.";
                    return item == null ? "" : "Для мешка мусора нужны свободные руки.";
                case "sink":
                    if (!room.leak) return "Раковина исправна.";
                    return item != null && item.kind == "toolbox" ? "" : "Нужен ящик инструментов.";
                case "water":
                    if (room.water <= .001f) return "Пол уже сухой.";
                    return item != null && item.kind == "mop" ? "" : "Нужна швабра.";
                default: return "Этот объект не требует длительной работы.";
            }
        }

        private float WorkDuration(string target)
        {
            if (State.mvp != null) return OperationsWorkDuration(target);
            TryRoomTarget(target, out string kind, out RoomState room);
            if (kind == "sink") return 5;
            if (kind == "water") return 3;
            if (kind == "trash") return 2;
            return room.bed == 1 ? 2 : 3;
        }

        private void CompleteWork(PlayerState player)
        {
            if (State.mvp != null) { OperationsCompleteWork(player); return; }
            TryRoomTarget(player.workTarget, out string kind, out RoomState room);
            if (kind == "bed")
            {
                if (room.bed == 1) { room.bed = 0; Give(player, "dirtylinen"); }
                else
                {
                    room.bed = 2;
                    Consume(player, Held(player));
                    HotelOnboarding.Record(State, HotelTutorialSkill.CleanBed);
                }
            }
            else if (kind == "trash") { room.trash = false; Give(player, "trashbag"); }
            else if (kind == "sink")
            {
                room.leak = false;
                HotelDirector.Repaired(State, room);
                HotelOnboarding.Record(State, HotelTutorialSkill.RepairLeak);
                GuestState guest = Guest(room.guestId);
                if (guest != null) Remember(guest, "Персонал починил раковину", 6);
            }
            else if (kind == "water")
            {
                room.water = 0;
                HotelDirector.Mopped(State, room);
                HotelOnboarding.Record(State, HotelTutorialSkill.MopWater);
            }
            Cancel(player);
        }

        private void Give(PlayerState player, string kind)
        {
            var item = new ItemState { id = kind + "_" + Guid.NewGuid().ToString("N"), kind = kind,
                holder = (long)player.id, position = player.position + Vector3.up * .8f };
            State.items.Add(item);
            player.held = item.id;
        }

        private void Consume(PlayerState player, ItemState item)
        {
            Cancel(player);
            item.consumed = true;
            item.holder = -1;
            item.placedRoom = 0;
            player.held = "";
        }

        private void Cancel(PlayerState player)
        {
            player.workTarget = "";
            player.workProgress = 0;
            player.workLastSeen = 0;
            heartbeats.Remove(player.id);
        }

        private void UpdateCartCargo()
        {
            ItemState cart = State.items.Find(i => i.kind == "cart" && !i.consumed);
            if (cart == null) return;
            int slot = 0;
            foreach (ItemState item in State.items)
                if (!item.consumed && item.placedRoom == -1)
                {
                    item.position = cart.position + new Vector3(slot % 2 == 0 ? -.23f : .23f, .25f, slot >= 2 ? .35f : 0);
                    slot++;
                }
        }

        private void RetireBags(int guestId)
        {
            foreach (ItemState item in State.items)
            {
                if (item.kind != "bag" || item.ownerGuest != guestId || item.consumed) continue;
                if (item.holder >= 0)
                {
                    PlayerState player = Player((ulong)item.holder);
                    if (player != null) { player.held = ""; Cancel(player); }
                }
                item.consumed = true;
                item.holder = -1;
                item.placedRoom = 0;
            }
        }

        private void Remember(GuestState guest, string memory, float change)
        {
            if (guest.memories.Contains(memory)) return;
            guest.memories.Add(memory);
            guest.satisfaction = Mathf.Clamp(guest.satisfaction + change, 0, 100);
        }

        private void Review(GuestState guest)
        {
            string prefix = "#" + guest.id + " ";
            if (State.reviews.Exists(r => r.StartsWith(prefix, StringComparison.Ordinal))) return;
            int stars = Mathf.Clamp(1 + (int)(guest.satisfaction / 20), 1, 5);
            var impressions = guest.memories.FindAll(m => m != "Попросил дополнительное полотенце" && m != "Раковина начала протекать");
            State.reviews.Add(prefix + guest.name + ": " + stars + "/5. " + (impressions.Count == 0 ? "Спокойная остановка в отеле." : string.Join("; ", impressions.ToArray()) + "."));
            if (State.reviews.Count > 120) State.reviews.RemoveAt(0);
        }

        private void Log(string text)
        {
            State.ledger.Add(text);
            if (State.ledger.Count > 256) State.ledger.RemoveAt(0);
        }

        public List<string> Tasks() => BuildTasks(State);

        // Read-only projection shared by host and clients; never construct a simulator for UI.
        public static List<string> BuildTasks(HotelState State)
        {
            var tasks = new List<string>();
            if(State==null)return tasks;
            if (State.mvp != null) { tasks.AddRange(HotelOperationsRules.Tasks(State)); tasks.AddRange(HotelHospitalityRules.Tasks(State)); return tasks; }
            foreach (GuestState guest in State.guests)
            {
                if (guest.stage == "queue") tasks.Add("Заселить: " + guest.name + " (стойка регистрации)");
                if (guest.stage == "walking" || guest.stage == "staying")
                {
                    if (!guest.luggageDelivered) tasks.Add("Багаж: " + guest.name + " → номер " + guest.room);
                    if (guest.towelRequested) tasks.Add("Дополнительное полотенце: номер " + guest.room);
                }
                if (guest.stage == "checkout") tasks.Add("Оформить выезд: " + guest.name + " (стойка регистрации)");
            }
            foreach (RoomState room in State.rooms)
            {
                string n = "Номер " + room.number + ": ";
                if (room.guestId == 0 && room.bed == 1) tasks.Add(n + "снять грязное бельё → приёмник");
                if (room.guestId == 0 && room.bed == 0) tasks.Add(n + "застелить чистое бельё");
                if (room.trash) tasks.Add(n + "собрать мусор → контейнер");
                if (!room.towel) tasks.Add(n + "принести полотенце");
                if (room.leak) tasks.Add(n + "починить раковину (ящик инструментов)");
                if (room.water > .001f) tasks.Add(n + "вытереть воду (швабра)");
            }
            if (State.items.Exists(i => !i.consumed && i.kind == "dirtylinen")) tasks.Add("Отнести снятое грязное бельё в приёмник у склада");
            if (State.items.Exists(i => !i.consumed && i.kind == "trashbag")) tasks.Add("Отнести мешки мусора в контейнер у склада");
            if (State.phase == "preparation") tasks.Add("Открыть отель на стойке, когда команда готова");
            if (State.phase == "closing") tasks.Add("Подвести итоги смены на стойке; незавершённая уборка сохранится");
            if (State.phase == "summary") tasks.Add("Начать подготовку следующего дня на стойке; затем доступны улучшения на доске");
            return tasks;
        }

        private PlayerState Player(ulong id) { return State.players.Find(p => p.id == id); }
        private RoomState Room(int number) { return State.rooms.Find(r => r.number == number); }
        private GuestState Guest(int id) { return State.guests.Find(g => g.id == id); }
        private ItemState Item(string id) { return State.items.Find(i => i.id == id); }
        private ItemState Held(PlayerState player) { return State.items.Find(i => i.id == player.held && !i.consumed && i.holder == (long)player.id); }

        private bool TryRoomTarget(string target, out string kind, out RoomState room)
        {
            kind = "";
            room = null;
            if (string.IsNullOrEmpty(target)) return false;
            string[] parts = target.Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int number)) return false;
            kind = parts[0];
            if (kind != "bed" && kind != "trash" && kind != "towel" && kind != "bag" && kind != "sink" && kind != "water" && kind != "door" &&
                (State.mvp == null || (kind != "toilet" && kind != "tv" && kind != "lamp" && kind != "coffee" && kind != "clean" && kind != "dirtytowel"))) return false;
            room = Room(number);
            return room != null;
        }

        private static bool Near(PlayerState player, Vector3 target)
        {
            return FlatDistance(player.position, target) <= InteractionDistance && SameAccessibleArea(player.position, target);
        }

        private static bool SameAccessibleArea(Vector3 a, Vector3 b)
        {
            // Sample the short interaction segment against the authored room shells. This prevents
            // shelf/bed pickup through the lobby wall and cross-room work without a Physics dependency.
            for (int i = 0; i <= 16; i++) if (!Walkable(Vector3.Lerp(a, b, i / 16f))) return false;
            return true;
        }

        internal static bool Walkable(Vector3 p)
        {
            if (!Finite(p) || p.y < -.5f || p.y > 2.5f) return false;
            if (Math.Abs(p.x) <= 7.65f && p.z >= -6.8f && p.z <= 1.85f) return true;
            if (Math.Abs(p.x) <= 1.35f && p.z >= 1.5f && p.z <= 22.6f) return true;
            for (int n = HotelLayout.FirstRoom; n <= HotelLayout.LastRoom; n++)
            {
                Vector3 c = HotelLayout.RoomCenter(n);
                if (Math.Abs(p.z - c.z) > 3.25f) continue;
                if (Math.Sign(p.x) != Math.Sign(c.x)) continue;
                if (Math.Abs(p.x) >= 1.7f && Math.Abs(p.x) <= 7.65f) return true;
                if (Math.Abs(p.z - c.z) <= .7f && Math.Abs(p.x) >= 1.3f && Math.Abs(p.x) <= 1.75f) return true;
            }
            return false;
        }

        internal static Vector3 SafePosition(Vector3 position)
        {
            return Walkable(position) ? new Vector3(position.x, .25f, position.z) : HotelLayout.Spawn;
        }
        internal static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        internal static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    }
}
