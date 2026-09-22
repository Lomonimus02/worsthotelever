using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        private void DangerPrepare()
        {
            if (!HotelDangerRules.Enabled(State) || State.phase != "preparation") return;
            HotelDangerState d = State.danger;
            if (d.day == State.day && d.serial > 0) return; // No forecast/medical refill by reopening preparation.
            State.mvp.pings.RemoveAll(p => HotelDangerRules.IsWorkTarget(p.target));
            // Continuing guests can carry deferred room complaints through the summary.
            // The old shift's sources end here, without granting a service recovery reward.
            foreach (MvpComplaintState complaint in State.mvp.complaints)
                if (complaint.status == "active" && complaint.causeKey != null && complaint.causeKey.StartsWith("danger:", StringComparison.Ordinal))
                { complaint.status = "resolved"; complaint.recoveredAt = State.mvp.elapsed; }
            d.day = State.day; d.serial++;
            d.status = "briefing"; d.outcome = d.reason = ""; d.settled = false;
            d.elapsed = d.graceUntil = 0; d.safety = HotelDangerRules.FullSafety;
            d.criticalRemaining = HotelDangerRules.CriticalSeconds;
            d.resolved = d.servicePoints = d.paidReward = d.chargedPenalty = 0;
            d.medkits = HotelDangerRules.InitialMedkits; d.selfRescues = HotelDangerRules.InitialSelfRescues;
            if (d.mode != "standard" && d.mode != "bold" && d.mode != "relief") d.mode = "standard";
            DangerModeParameters();
            d.crew.Clear();
            for (int slot = 0; slot < 2; slot++)
            {
                Vector3 spawn = HotelLayout.Spawn + new Vector3(slot == 0 ? -.5f : .5f, 0, 0);
                PlayerState player = State.players.Find(p => HotelDangerRules.Slot(p.id) == slot);
                d.crew.Add(new HotelCrewState { slot = slot, joined = player != null, position = spawn, health = HotelDangerRules.FullHealth });
                if (player == null) continue;
                Cancel(player); player.position = spawn;
                ItemState held = Held(player);
                if (held != null) held.position = spawn + Vector3.up * .8f;
            }
            d.incidents.Clear();
            var rooms = State.rooms.FindAll(r => r.mvp != null && r.mvp.owned);
            rooms.Sort((a, b) => a.number.CompareTo(b.number));
            var kinds = new List<string> { "electric", "steam", "fumes" };
            for (int index = 0; index < 3 && rooms.Count > 0; index++)
            {
                int roomIndex = MvpRandom(rooms.Count), kindIndex = MvpRandom(kinds.Count);
                d.incidents.Add(new HotelIncidentState { room = rooms[roomIndex].number, kind = kinds[kindIndex],
                    triggerAt = HotelDangerRules.FirstIncidentAt + index * HotelDangerRules.IncidentSpacing + MvpRandom(HotelDangerRules.TriggerJitter) });
                rooms.RemoveAt(roomIndex); kinds.RemoveAt(kindIndex);
            }
        }

        private void DangerModeParameters()
        {
            HotelDangerState d = State.danger;
            d.requiredIncidents = HotelDangerRules.IncidentGoal(d.mode); d.requiredService = HotelDangerRules.ServiceGoal(d.mode);
            d.minimumSeconds = HotelDangerRules.MinimumSeconds(d.mode);
            d.reward = HotelDangerRules.Reward(d.mode); d.penalty = HotelDangerRules.Penalty(d.mode);
        }
        private void DangerOpened()
        {
            if (!HotelDangerRules.Enabled(State) || State.phase != "open" || State.danger.status != "briefing" || State.danger.settled) return;
            State.danger.status = "active";
        }
        private void DangerJoined(PlayerState player)
        {
            // Base Join limits connection count, not durable roles. If the host is absent,
            // a second partner must not claim the already connected partner's body/lease.
            if (State.players.Exists(p => p.id != player.id && HotelDangerRules.Slot(p.id) == HotelDangerRules.Slot(player.id)))
            { State.players.Remove(player); return; }
            HotelCrewState crew = HotelDangerRules.Crew(State, player.id);
            if (crew == null) return;
            if (crew.joined) player.position = crew.position;
            else { crew.position = player.position; crew.joined = true; }
            // A new connection is not a new life, even if its NGO partner ID changed.
            if (crew.life != "healthy") DangerReleaseCrew(crew);
        }
        private void DangerLeaving(PlayerState player) { DangerPose(player); }
        private void DangerPose(PlayerState player)
        {
            HotelCrewState crew = HotelDangerRules.Crew(State, player.id);
            if (crew != null && crew.life == "healthy") crew.position = player.position;
        }
        private void DangerService()
        {
            if (!HotelDangerRules.Enabled(State) || State.danger.status != "active" || State.danger.settled ||
                (State.phase != "open" && State.phase != "closing")) return;
            State.danger.servicePoints = Math.Min(HotelDangerRules.MaxServicePoints, State.danger.servicePoints + 1);
        }

        private bool TryDangerCommand(PlayerState player, HotelCommand command, out string error)
        {
            error = "";
            if (!HotelDangerRules.Enabled(State)) return false;
            HotelDangerState d = State.danger;
            string target = command.target ?? "";
            if (command.action == "nextday" && State.phase == "summary" && d.settled)
            {
                error = player.id == 0 ? MvpNextDay() : "Следующий день начинает хозяин отеля.";
                return true; // Dead host needs neither a pose nor reception proximity.
            }
            if (command.action == "abandonDanger")
            {
                if (player.id != 0) error = "Отказ от контракта подтверждает хозяин отеля.";
                else if (!HotelDangerRules.HostCanForfeit(State)) error = "Отказ доступен недееспособному хозяину, когда в сети нет сотрудника, способного продолжать смену.";
                else DangerFail("evacuated", "Хозяин отказался от контракта: команда недоступна, в сети нет дееспособного сотрудника.");
                return true;
            }
            if (command.action == "cancelwork") { Cancel(player); return true; }
            bool recovery = HotelDangerRules.Number(target, "recover_", 0, 1, out int slot) && slot == HotelDangerRules.Slot(player.id);
            bool recoveryInput = recovery && (command.action == "interact" || command.action == "beginwork" ||
                (command.action == "heartbeat" && player.workTarget == target));
            if (!HotelDangerRules.CanAct(State, player.id) && !recoveryInput)
            { error = "Сотрудник недееспособен: нужна помощь; погибший вернётся в следующей подготовке."; return true; }
            if (command.action == "dangerMode")
            {
                if (player.id != 0) error = "Контракт выбирает хозяин отеля.";
                else if (State.phase != "preparation" || d.status != "briefing") error = "Контракт можно менять только в подготовке.";
                else if (!Near(player, HotelLayout.Target("board"))) error = "Подойдите к доске контрактов.";
                else if (target != "standard" && target != "bold" && target != "relief") error = "Неизвестный режим контракта.";
                else { d.mode = target; DangerModeParameters(); }
                return true;
            }
            if ((command.action == "interact" || command.action == "beginwork") && HotelDangerRules.IsWorkTarget(target))
            { error = BeginWork(player, target); return true; }
            if (command.action == "heartbeat" && HotelDangerRules.IsWorkTarget(target))
            {
                if (player.workTarget != target) { error = "Сначала начните работу."; return true; }
                error = HotelDangerRules.WorkError(State, player.id, target);
                if (error != "") return true;
                return false; // Existing authoritative heartbeat/lease implementation owns progress.
            }
            if (command.action != "finish") return false;
            if (player.id != 0) error = "Итоги подводит хозяин отеля.";
            else if (d.settled || d.status != "active" || (State.phase != "open" && State.phase != "closing")) error = "Смена уже завершена или ещё не открыта.";
            else if (!Near(player, HotelLayout.Target("desk"))) error = "Подойдите к стойке регистрации.";
            else if (d.mode == "relief") DangerFinishRelief();
            else if (HotelDangerRules.GoalsMet(State)) DangerWin();
            else if (State.phase == "closing") DangerFail("incomplete", "Контракт не выполнен к завершению смены.");
            else error = HotelDangerRules.Objective(State) + " Для досрочной эвакуации используйте пожарную тревогу.";
            return true;
        }

        private void DangerStep(float dt)
        {
            if (!HotelDangerRules.Enabled(State) || !Finite(dt) || dt <= 0 || State.danger.status != "active" || State.danger.settled ||
                (State.phase != "open" && State.phase != "closing") || HotelDirector.ClockHeld(State)) return;
            HotelDangerState d = State.danger;
            d.elapsed += dt;
            var shieldBefore = new float[2];
            foreach (HotelCrewState crew in d.crew)
            {
                shieldBefore[crew.slot] = crew.shield;
                if (!crew.joined) continue;
                crew.shield = Mathf.Max(0, crew.shield - dt);
                if (crew.life != "downed") continue;
                crew.bleedout = Mathf.Max(0, crew.bleedout - dt);
                if (crew.bleedout > 0) continue;
                crew.life = "dead"; crew.health = crew.shield = 0;
                DangerReleaseCrew(crew);
                State.notice = "Сотрудник " + (crew.slot + 1) + " погиб: окно помощи истекло.";
            }
            float beforeSafety = d.safety;
            foreach (HotelIncidentState incident in d.incidents)
            {
                if (incident.isolated || (incident.status != "warning" && incident.status != "active")) continue;
                float activeTime = dt;
                if (incident.status == "warning")
                {
                    activeTime = Mathf.Max(0, dt - incident.warningRemaining);
                    incident.warningRemaining = Mathf.Max(0, incident.warningRemaining - dt);
                    if (incident.warningRemaining > 0) continue;
                    incident.status = "active";
                    State.notice = "ОПАСНО: " + HotelDangerRules.KindName(incident.kind) + " в № " + incident.room + ". Отсекатель у входа.";
                }
                incident.activeSeconds += activeTime;
                d.safety = Mathf.Max(0, d.safety - HotelDangerRules.SafetyLoss(incident.kind) * activeTime);
                foreach (HotelCrewState crew in d.crew)
                {
                    if (!crew.joined || crew.life != "healthy" || !HotelDangerRules.Contains(incident, crew.position)) continue;
                    float unshielded = Mathf.Max(0, activeTime - Mathf.Max(0, shieldBefore[crew.slot] - (dt - activeTime)));
                    float damage = HotelDangerRules.Damage(incident.kind, incident.activeSeconds - unshielded, incident.activeSeconds);
                    if (damage <= 0) continue;
                    crew.health = Mathf.Max(0, crew.health - damage); crew.lastHit = incident.kind;
                    if (crew.health > 0) continue;
                    crew.life = "downed"; crew.bleedout = HotelDangerRules.DownedSeconds; crew.shield = 0; crew.downs++;
                    DangerReleaseCrew(crew);
                    State.notice = "Сотрудник " + (crew.slot + 1) + " без сознания! Помощь за " + HotelDangerRules.DownedSeconds + " сек.";
                }
            }
            if (d.safety <= 0)
            {
                if (beforeSafety > 0)
                {
                    d.criticalRemaining = HotelDangerRules.CriticalSeconds;
                    State.notice = "КРИТИЧЕСКОЕ СОСТОЯНИЕ: устраните источник или включите эвакуацию за " + HotelDangerRules.CriticalSeconds + " секунд!";
                }
                else d.criticalRemaining = Mathf.Max(0, d.criticalRemaining - dt);
                if (d.criticalRemaining <= 0) { DangerFail("collapse", "Отель аварийно закрыт: критическое окно истекло."); return; }
            }
            bool participating = d.crew.Exists(c => c.joined);
            bool healthy = d.crew.Exists(c => c.joined && c.life == "healthy");
            bool selfHelp = d.medkits > 0 && d.selfRescues > 0 && d.crew.Exists(c => c.joined && c.life == "downed" && c.bleedout > 0);
            if (participating && !healthy && !selfHelp)
            {
                foreach (HotelCrewState crew in d.crew)
                    if (crew.joined && crew.life == "downed")
                    { crew.life = "dead"; crew.bleedout = crew.shield = 0; DangerReleaseCrew(crew); }
                DangerFail("wipe", "Команда потеряна: некому продолжать работу или оказывать помощь.");
                return;
            }
            if (d.mode == "relief" || d.elapsed < d.graceUntil) return;
            int active = d.incidents.FindAll(i => i.status == "warning" || i.status == "active").Count;
            for (int i = 0; i < d.requiredIncidents && i < d.incidents.Count && active < HotelDangerRules.SimultaneousIncidents(d.mode); i++)
            {
                HotelIncidentState incident = d.incidents[i];
                if (incident.status != "planned" || d.elapsed < incident.triggerAt) continue;
                incident.status = "warning"; incident.warningRemaining = HotelDangerRules.WarningSeconds;
                State.notice = "ПРЕДУПРЕЖДЕНИЕ: " + HotelDangerRules.KindName(incident.kind) + " в № " + incident.room +
                    ". Есть " + HotelDangerRules.WarningSeconds + " сек.; изолируйте источник у входа.";
                active++;
            }
        }

        private void DangerCompleteWork(PlayerState player)
        {
            string target = player.workTarget;
            if (HotelDangerRules.WorkError(State, player.id, target) != "") { Cancel(player); return; }
            HotelDangerState d = State.danger;
            HotelCrewState actor = HotelDangerRules.Crew(State, player.id);
            if (target == "alarm") { Cancel(player); DangerFail("evacuated", "Команда включила эвакуацию. Контракт прекращён без премии."); return; }
            if (target == "firstaid")
            {
                d.medkits--; actor.health = HotelDangerRules.FullHealth; actor.lastHit = "";
                State.notice = "Первая помощь оказана. Общих аптечек: " + d.medkits + ".";
            }
            else if (HotelDangerRules.Number(target, "recover_", 0, 1, out int slot) || HotelDangerRules.Number(target, "rescue_", 0, 1, out slot))
            {
                HotelCrewState patient = d.crew.Find(c => c.slot == slot);
                bool self = patient.slot == actor.slot;
                d.medkits--; if (self) d.selfRescues--;
                patient.life = "healthy"; patient.health = HotelDangerRules.RescueHealth;
                patient.bleedout = 0; patient.shield = HotelDangerRules.RescueShield; patient.lastHit = "";
                if (!self) actor.rescues++;
                DangerCancelPatientLeases(slot);
                State.notice = "Сотрудник " + (slot + 1) + " спасён. Защита " + HotelDangerRules.RescueShield + " сек.: покиньте опасную зону!";
            }
            else
            {
                bool isolate = HotelDangerRules.Number(target, "isolate_", 101, 106, out int room);
                if (!isolate) HotelDangerRules.Number(target, "hazard_", 101, 106, out room);
                HotelIncidentState incident = HotelDangerRules.Incident(State, room);
                if (isolate)
                {
                    incident.isolated = true;
                    State.notice = "Источник в № " + room + " изолирован. Урон остановлен; теперь устраните причину.";
                }
                else
                {
                    incident.status = "resolved"; incident.warningRemaining = 0; incident.isolated = false;
                    d.resolved++; d.safety = Mathf.Min(HotelDangerRules.FullSafety, d.safety + HotelDangerRules.RepairSafety);
                    d.criticalRemaining = HotelDangerRules.CriticalSeconds;
                    d.graceUntil = Mathf.Max(d.graceUntil, d.elapsed + HotelDangerRules.IncidentGrace);
                    State.notice = "Авария в № " + room + " устранена. Обычные поломки и уборка остаются отдельными задачами.";
                }
            }
            Cancel(player);
            RefreshRequestFulfillment();
        }

        private void DangerCancelPatientLeases(int slot)
        {
            foreach (PlayerState player in State.players)
                if (player.workTarget == "rescue_" + slot || player.workTarget == "recover_" + slot) Cancel(player);
        }
        private void DangerReleaseCrew(HotelCrewState crew)
        {
            foreach (PlayerState player in State.players)
            {
                if (HotelDangerRules.Slot(player.id) != crew.slot) continue;
                // Drop uses the existing physical ownership/logistics path; it also cancels
                // work and updates factual luggage fulfillment without manufacturing rewards.
                if (Held(player) != null) Drop(player, player.position);
                Cancel(player);
            }
            DangerCancelPatientLeases(crew.slot);
        }
        private void DangerWin()
        {
            HotelDangerState d = State.danger;
            if (d.settled || d.status != "active" || !HotelDangerRules.GoalsMet(State)) return;
            d.settled = true; d.status = "won"; d.outcome = "completed";
            d.paidReward = d.reward; State.cash += d.paidReward; State.earned += d.paidReward;
            d.wins++; d.streak++; d.bestStreak = Math.Max(d.bestStreak, d.streak);
            d.reason = "Контракт выполнен. Команда в строю; премия +" + d.paidReward + ".";
            MvpFinish(); State.notice = d.reason; Log(d.reason);
        }
        private void DangerFinishRelief()
        {
            HotelDangerState d = State.danger;
            if (d.settled || d.status != "active") return;
            // 'won' is the non-failed terminal transport state; outcome=relief explicitly
            // means no contract victory, reward, streak increment or loss counter.
            d.settled = true; d.status = "won"; d.outcome = "relief";
            d.reason = "Передышка завершена. Контрактной победы и премии нет.";
            MvpFinish(); State.notice = d.reason;
        }
        private void DangerFail(string outcome, string reason)
        {
            HotelDangerState d = State.danger;
            if (d.settled || d.status != "active") return;
            d.settled = true; d.status = "failed"; d.outcome = outcome; d.losses++; d.streak = 0;
            d.chargedPenalty = d.penalty; State.cash -= d.chargedPenalty; State.expenses += d.chargedPenalty;
            d.reason = reason + " Восстановление −" + d.chargedPenalty + ". Купленные номера и улучшения сохранены.";
            foreach (GuestState guest in State.guests)
            {
                if (guest.mvp == null || guest.paid || !HotelHospitalityRules.Occupying(guest)) continue;
                RoomState room = Room(guest.room);
                if (room != null && room.guestId == guest.id)
                {
                    room.guestId = 0; room.bed = 1;
                    if (room.towel) { room.towel = false; room.mvp.dirtyTowels = Math.Min(8, room.mvp.dirtyTowels + 1); }
                }
                MvpReservationState booking = State.mvp.reservations.Find(r => r.id == guest.mvp.reservationId);
                if (booking != null) booking.status = "cancelled";
                MvpGroupState group = State.mvp.groups.Find(g => g.id == guest.mvp.groupId);
                if (group != null) { group.admitted = Math.Max(0, group.admitted - 1); group.refused++; }
                guest.satisfaction = Mathf.Min(guest.satisfaction, 25);
                HospitalityMemoryOnce(guest, "Проживание прервано аварийной эвакуацией", 0);
                RecordHospitalityReview(guest);
                EndHospitalityService(guest); guest.stage = "leaving"; guest.position = Exit;
                RetireBags(guest.id);
            }
            // Unpaid occupants are terminal before this call, so the normal finish handles
            // queues/planned arrivals without settling interrupted stays at their full tariff.
            MvpFinish(); State.notice = d.reason; Log(d.reason);
        }
    }
}
