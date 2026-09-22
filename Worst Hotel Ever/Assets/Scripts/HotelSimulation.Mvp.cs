using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        private bool promoteAfterShift;

        // Explicit production factories leave the pre-MVP regression fixtures unambiguous.
        public static HotelSimulation CreateNewMvp(int? seed = null)
        {
            var simulation = new HotelSimulation();
            simulation.PromoteForPlay(seed);
            return simulation;
        }

        public static HotelSimulation ResumeForPlay(HotelState loaded)
        {
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));
            var simulation = new HotelSimulation(loaded);
            if (loaded.mvp == null)
            {
                if (loaded.phase == "preparation") simulation.TryPromoteLegacy();
                else simulation.promoteAfterShift = true;
            }
            return simulation;
        }

        private void PromoteForPlay(int? seed = null)
        {
            if (State.mvp != null) return;
            if (State.phase != "preparation") throw new InvalidOperationException("Переход версии доступен после текущей смены.");
            // Build separately: failure cannot leave half of a legacy hotel promoted.
            HotelState candidate = JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(State));
            candidate.version = 2;
            candidate.contentVersion = 2;
            candidate.dayLength = 1080;
            int value = seed ?? unchecked((int)DateTime.UtcNow.Ticks);
            candidate.mvp = new MvpHotelState { schema = 2, rngState = value == 0 ? 1597463007 : value };
            foreach (RoomState room in candidate.rooms) InitializeMvpRoom(room, true);
            for (int n = 105; n <= HotelLayout.LastRoom; n++)
            {
                var room = new RoomState { number = n };
                InitializeMvpRoom(room, false);
                candidate.rooms.Add(room);
            }
            candidate.items.RemoveAll(i => i.consumed);
            foreach (ItemState item in candidate.items)
            {
                // JsonUtility can leave fields absent from pre-MVP JSON at their CLR defaults.
                if (string.IsNullOrEmpty(item.size)) item.size = item.kind == "cart" ? "large" : "small";
                if (string.IsNullOrEmpty(item.condition)) item.condition = item.kind == "dirtylinen" || item.kind == "trashbag" ? "dirty" : "clean";
            }
            candidate.items.Add(new ItemState { id = "plunger_1", kind = "plunger", position = HotelLayout.Target("tools") + new Vector3(0, 0, -.65f) });
            // Legacy preparation has no promised living guests; terminal records have no new contract.
            candidate.guests.RemoveAll(g => g.stage == "gone" || g.stage == "leaving");
            foreach (PlayerState player in candidate.players) { player.workTarget = ""; player.workProgress = player.workLastSeen = 0; }
            var prepared = new HotelSimulation(candidate, catalog);
            prepared.PrepareDemand();
            candidate.mvp.preparedDay = candidate.day;
            candidate.notice = "День " + candidate.day + ": проверьте прогноз, цены и номера на доске. Подготовка без таймера.";
            HotelSaveStore.Validate(candidate);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(candidate), State);
            heartbeats.Clear();
            promoteAfterShift = false;
        }

        private void TryPromoteLegacy()
        {
            try { PromoteForPlay(); }
            catch (System.IO.InvalidDataException)
            {
                // Old saves allowed much larger inventories/journals. Never discard data
                // or strand that hotel just to fit v2: keep its old rules and retry later.
                promoteAfterShift = true;
                State.notice = "Старый отель продолжает работать по прежним правилам. Его данные пока не помещаются в MVP: уберите лишние вещи. Переход повторится в следующей подготовке.";
            }
        }

        private static void InitializeMvpRoom(RoomState room, bool owned)
        {
            room.mvp = new MvpRoomState {
                owned = owned, capacity = room.number % 2 == 0 ? 2 : 1,
                bedQuality = room.upgraded ? 2 : 1, binFill = room.trash ? .8f : 0
            };
            foreach (string kind in new[] { "sink", "toilet", "tv", "lamp" })
                room.mvp.equipment.Add(new MvpEquipmentState { kind = kind, localFault = kind == "sink" && room.leak, episode = kind == "sink" && room.leak ? 1 : 0 });
        }

        private bool TryMvpCommand(PlayerState player, HotelCommand command, out string error)
        {
            if (command.action == "ping")
            {
                if (!MvpTargetExists(command.target)) { error = "Отметить можно объект отеля."; return true; }
                State.mvp.pings.RemoveAll(p => p.playerId == player.id);
                State.mvp.pings.Add(new MvpPingState { playerId = player.id, target = command.target, until = State.mvp.elapsed + 6 });
                error = ""; return true;
            }
            if (command.action == "toggleRoom")
            {
                RoomState room = Room(command.number);
                if (room?.mvp == null || !room.mvp.owned) { error = "Этот номер ещё не куплен."; return true; }
            }
            if (TryOperationsCommand(player, command, out error)) return true;
            return TryHospitalityCommand(player, command, out error);
        }

        private bool MvpTargetExists(string target)
        {
            if (HotelDangerRules.Enabled(State) && HotelDangerRules.TryTarget(State, target, out _)) return true;
            if (string.IsNullOrEmpty(target)) return false;
            if (target.StartsWith("guest_", StringComparison.Ordinal) && int.TryParse(target.Substring(6), out int guestId))
                return State.guests.Exists(g => g.id == guestId && g.stage != "gone");
            if (State.items.Exists(i => i.id == target && !i.consumed)) return true;
            if (target == "desk" || target == "board" || target == "coffee" || target == "linen" || target == "towels" ||
                target == "hamper" || target == "bin" || target == "tools" || target == "utility_water" || target == "utility_power") return true;
            return TryRoomTarget(target, out _, out RoomState room) && room.mvp.owned;
        }

        private bool OwnedArea(Vector3 position)
        {
            if (Math.Abs(position.x) < 1.6f || position.z < 2) return true;
            int number = position.x < 0 ? 101 : 102;
            number += 2 * Mathf.Clamp(Mathf.FloorToInt((position.z - 2) / 7), 0, 2);
            RoomState room = Room(number);
            return room != null && (room.mvp == null || room.mvp.owned);
        }

        private void MvpStep(float dt)
        {
            if (HotelDangerRules.Enabled(State)) DangerStep(dt);
            bool running = State.phase == "open" || State.phase == "closing";
            if (!running)
            {
                // Preparation freezes hotel time, but a cooperative marker still expires in six seconds.
                foreach (MvpPingState ping in State.mvp.pings) ping.until -= dt;
                State.mvp.pings.RemoveAll(p => p.until <= State.mvp.elapsed);
                RefreshRequestFulfillment();
                PruneConsumedItems();
                UpdateCartCargo();
                return;
            }
            HotelDirector.Advance(State);
            bool held = HotelDirector.ClockHeld(State);
            if (!held)
            {
                State.mvp.elapsed += dt;
                if (State.phase == "open") State.time = Mathf.Min(State.dayLength, State.time + dt);
            }
            else foreach (MvpPingState ping in State.mvp.pings) ping.until -= dt;
            State.mvp.pings.RemoveAll(p => p.until <= State.mvp.elapsed);
            OperationsStep(dt);
            HospitalityStep(dt);
            RefreshRequestFulfillment();
            PacingStep(dt);
            if (State.phase == "open" && State.time >= State.dayLength)
            {
                State.phase = "closing";
                State.notice = "Приём закрыт. Оформите выезды и подведите итоги. Ночующие гости останутся.";
            }
            PruneConsumedItems();
            UpdateCartCargo();
        }

        private void PruneConsumedItems()
        {
            // Consumed items have no remaining ownership or visual role; do not grow every delivery forever.
            State.items.RemoveAll(i => i.consumed && i.holder < 0);
        }

        private string MvpOpen()
        {
            if (State.phase != "preparation") return "Сначала завершите подготовку.";
            if (!State.rooms.Exists(r => r.mvp.owned && (r.guestId != 0 || (!r.outOfService && r.bed == 2 && !r.leak && r.water <= .65f))))
                return "Подготовьте хотя бы один открытый чистый номер. Ремонт и бельё доступны в подготовке.";
            State.phase = "open";
            if (HotelDangerRules.Enabled(State)) DangerOpened();
            State.notice = "Отель открыт. Проверьте расписание и встречайте гостей.";
            HospitalityStep(0);
            return "";
        }

        private string MvpFinish()
        {
            if (State.phase != "open" && State.phase != "closing") return "Сейчас нет смены для завершения.";
            FinishHospitality();
            foreach (PlayerState player in State.players) Cancel(player);
            State.phase = "summary";
            State.notice = "День завершён. Доход " + State.earned + ", расходы " + State.expenses + ". Проживающие несколько дней сохраняют номера.";
            return "";
        }

        private string MvpNextDay()
        {
            if (State.phase != "summary") return "Сначала подведите итоги смены.";
            int linen = Math.Max(0, 12 - State.linenStock - State.items.FindAll(i => !i.consumed && i.kind == "linen").Count);
            int towels = Math.Max(0, 12 - State.towelStock - State.items.FindAll(i => !i.consumed && i.kind == "towel" && i.condition != "dirty").Count);
            int coffee = Math.Max(0, 12 - State.mvp.coffeeStock - State.items.FindAll(i => !i.consumed && i.kind == "coffee").Count);
            int cost = 30 + linen * 4 + towels * 2 + coffee * 3;
            State.day++;
            State.time = 0;
            State.arrivals = State.served = State.earned = 0;
            State.expenses = cost;
            State.cash -= cost; // Essential replenishment remains available on credit, not optional upgrades.
            State.linenStock += linen; State.towelStock += towels; State.mvp.coffeeStock += coffee;
            State.guidedStage = HotelDirector.Released;
            State.guidedGuestId = State.guidedLeakRoom = 0;
            State.guidedRepairDone = State.guidedMopDone = State.dailyLeakIssued = false;
            State.nextArrivalTime = 0;
            State.phase = "preparation";
            PruneConsumedItems();
            PruneHospitality();
            PrepareDemand();
            State.mvp.preparedDay = State.day;
            Log("День " + State.day + ": снабжение и содержание −" + cost);
            State.notice = "День " + State.day + ". Грязь, поломки и ночующие гости сохранены. Запасы пополнены, прогноз готов.";
            HotelOnboarding.Record(State, HotelTutorialSkill.NextDay);
            if (HotelDangerRules.Enabled(State)) DangerPrepare();
            else if (dangerPromotionPending) TryPromoteDanger();
            return "";
        }

        private string MvpId(string prefix)
        {
            string id;
            do { id = prefix + "_" + State.mvp.nextId++; }
            while (State.items.Exists(i => i.id == id) || State.mvp.reservations.Exists(r => r.id == id || r.partyId == id) ||
                State.mvp.schedule.Exists(s => s.id == id || s.contract.partyId == id) || State.guests.Exists(g => g.mvp?.partyId == id) ||
                State.mvp.groups.Exists(g => g.id == id) || State.mvp.requests.Exists(r => r.id == id) ||
                State.mvp.complaints.Exists(c => c.id == id) || State.mvp.director.events.Exists(e => e.id == id));
            return id;
        }

        private int MvpRandom(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            uint value = unchecked((uint)State.mvp.rngState);
            if (value == 0) value = 1597463007;
            value ^= value << 13; value ^= value >> 17; value ^= value << 5;
            State.mvp.rngState = unchecked((int)value);
            State.mvp.rngDraws++;
            return (int)(value % (uint)exclusiveMax);
        }
    }
}
