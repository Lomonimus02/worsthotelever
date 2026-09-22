using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        private void PacingStep(float dt)
        {
            if (State.mvp == null || State.mvp.director == null || !Finite(dt) || dt <= 0 ||
                (State.phase != "open" && State.phase != "closing")) return;
            MvpDirectorState director = State.mvp.director;
            float now = State.mvp.elapsed;
            PacingPrune(now);
            director.workload = HotelWorkload.Measure(State);
            director.band = HotelMvpDirector.Band(director.workload);
            float smoothing = director.workload > director.stress ? 20 : 100;
            director.stress = Mathf.Clamp(Mathf.Lerp(director.stress, director.workload,
                1 - Mathf.Exp(-Mathf.Min(dt, 30) / smoothing)), 0, 100);

            // Closing resolves records and stress, but cannot add more consequences.
            if (State.phase != "open") return;
            if (HotelDangerRules.Enabled(State) && State.danger.status == "active" &&
                (State.danger.incidents.Exists(h => h.status == "warning" || h.status == "active") || State.danger.crew.Exists(c => c.joined && c.life != "healthy")))
            {
                director.nextDecision = Math.Max(director.nextDecision, now + 35);
                return;
            }
            if (HotelMvpDirector.Guided(State))
            {
                director.nextDecision = Math.Max(director.nextDecision, now + 45);
                return;
            }
            if (director.workload >= HotelMvpDirector.OverloadThreshold || director.stress >= 70)
            {
                director.recoveryUntil = Math.Max(director.recoveryUntil, now + HotelMvpDirector.RecoverySeconds);
                director.nextDecision = Math.Max(director.nextDecision, director.recoveryUntil);
                return;
            }
            if (director.workload >= HotelMvpDirector.HighThreshold || director.stress >= 65)
            {
                director.nextDecision = Math.Max(director.nextDecision, now + 35);
                return;
            }
            if (now < director.nextDecision || now < director.globalCooldown || now < director.recoveryUntil) return;
            // Do not spring an arrival/fault at the final minutes of the shift.
            if (State.time >= State.dayLength * .8f)
            {
                director.nextDecision = Math.Max(now + 45, now + State.dayLength - State.time);
                return;
            }

            float potential = HotelMvpDirector.DifficultyPotential(State);
            var kinds = new List<string>();
            var weights = new List<int>();
            int nothing = 55 + Mathf.RoundToInt(director.stress * .7f);
            int total = nothing;
            foreach (string kind in HotelMvpDirector.Kinds)
            {
                if (!PacingAvailable(kind, now)) continue;
                int weight = HotelMvpDirector.Weight(kind, State, potential);
                if (director.recent.Contains(kind)) weight /= 2;
                if (weight <= 0) continue;
                kinds.Add(kind); weights.Add(weight); total += weight;
            }
            // Every attempted decision schedules just one future decision. No catch-up loop
            // or reroll after a rejected capacity check; all random draws live in the save.
            director.nextDecision = now + 110 - 35 * potential + MvpRandom(31);
            if (kinds.Count == 0) return;
            int draw = MvpRandom(total);
            if (draw < nothing) return;
            draw -= nothing;
            for (int i = 0; i < kinds.Count; i++)
            {
                if (draw >= weights[i]) { draw -= weights[i]; continue; }
                PacingStart(kinds[i], now, potential);
                return;
            }
        }

        private void PacingUpgradeGrace()
        {
            if (State.mvp == null || State.mvp.director == null) return;
            MvpDirectorState director = State.mvp.director;
            // Preparation does not advance elapsed: the player gets the whole window on open.
            director.recoveryUntil = Math.Max(director.recoveryUntil, State.mvp.elapsed + HotelMvpDirector.UpgradeGraceSeconds);
            director.globalCooldown = Math.Max(director.globalCooldown, director.recoveryUntil);
            director.nextDecision = Math.Max(director.nextDecision, director.recoveryUntil);
        }

        private bool PacingAvailable(string kind, float now)
        {
            MvpDirectorState director = State.mvp.director;
            if (director.events.Count >= HotelMvpDirector.MaxHistory && !director.events.Exists(e => e.status != "active")) return false;
            if (PacingCooldown("category_" + HotelMvpDirector.Category(kind), now) || PacingCooldown("event_" + kind, now)) return false;
            if (director.recent.Count > 0 && director.recent[director.recent.Count - 1] == kind) return false;
            if (HotelMvpDirector.Costly(kind) && (HotelMvpDirector.GroupPressure(State) ||
                director.events.Exists(e => e.status == "active" && HotelMvpDirector.Costly(e.kind)))) return false;
            if (kind == "water")
                return !director.events.Exists(e => e.status == "active" && e.kind == "water") && PacingWaterRooms().Count > 0;
            if (State.time > State.dayLength * .65f) return false;
            // Reservations, party size and current-day capacity are decided transactionally
            // by Hospitality, including any continuing stay. We never create guest records.
            return State.rooms.Exists(r => r.mvp != null && r.mvp.owned && !r.outOfService);
        }

        private bool PacingCooldown(string id, float now)
        {
            return State.mvp.director.cooldowns.Exists(c => c.id == id && c.until > now);
        }

        private void PacingStart(string kind, float now, float potential)
        {
            int room = 0;
            bool started;
            if (kind == "walkin_group")
            {
                int count = potential >= .65f ? 3 : 2;
                started = TryReserveBatch(count, "walkin_group", out _);
            }
            else if (kind == "rush")
            {
                // A short extra arrival burst, bounded by the allocator even if only one fits.
                started = TryScheduleArrival("tourist", "rush");
                if (started) TryScheduleArrival(potential >= .4f ? "business" : "tourist", "rush");
            }
            else if (kind == "vip") started = TryScheduleArrival("vip", "vip");
            else
            {
                List<int> rooms = PacingWaterRooms();
                if (rooms.Count == 0) return;
                room = rooms[MvpRandom(rooms.Count)];
                started = TryRaiseFault(room, "sink");
            }
            if (!started) return; // No fabricated event/reward if ordinary rules reject it.

            MvpDirectorState director = State.mvp.director;
            string category = HotelMvpDirector.Category(kind);
            director.events.Add(new MvpEventState { id = MvpId("event"), kind = kind, category = category,
                startedAt = now, until = now + HotelMvpDirector.Duration(kind), room = room });
            director.recent.Add(kind);
            while (director.recent.Count > HotelMvpDirector.MaxRecent) director.recent.RemoveAt(0);
            director.globalCooldown = now + 115 - 30 * potential;
            PacingSetCooldown("category_" + category, now + (category == "guest" ? 240 - 30 * potential : 240));
            PacingSetCooldown("event_" + kind, now + HotelMvpDirector.EventCooldown(kind));
            State.notice = kind == "water" ? "Небольшая протечка в номере " + room + ": нужны ремонт и уборка воды." :
                "Внеплановое событие: " + HotelMvpDirector.EventName(kind) + ". Прибытия доступны в расписании.";
            Log("Событие: " + HotelMvpDirector.EventName(kind) + (room == 0 ? "." : " · № " + room));
            PacingPrune(now);
        }

        private List<int> PacingWaterRooms()
        {
            var rooms = new List<int>();
            foreach (RoomState room in State.rooms)
            {
                if (room.mvp == null || !room.mvp.owned || room.outOfService || room.leak || room.water > .1f) continue;
                MvpEquipmentState sink = room.mvp.equipment.Find(e => e.kind == "sink");
                if (sink != null && sink.installed && !sink.localFault) rooms.Add(room.number);
            }
            rooms.Sort(); // Equivalent snapshots/list ordering choose the same physical source.
            return rooms;
        }

        private void PacingSetCooldown(string id, float until)
        {
            MvpCooldownState cooldown = State.mvp.director.cooldowns.Find(c => c.id == id);
            if (cooldown == null) State.mvp.director.cooldowns.Add(new MvpCooldownState { id = id, until = until });
            else cooldown.until = Math.Max(cooldown.until, until);
        }

        private void PacingPrune(float now)
        {
            MvpDirectorState director = State.mvp.director;
            foreach (MvpEventState value in director.events)
            {
                if (value.status != "active") continue;
                if (value.kind == "water")
                {
                    RoomState room = Room(value.room);
                    MvpEquipmentState sink = room?.mvp?.equipment.Find(e => e.kind == "sink");
                    if (room == null || (sink != null && !sink.localFault && !room.leak && room.water <= .001f))
                        value.status = "resolved";
                }
                // Guest events own an arrival-pressure window, not their guests' lifetime.
                // Continuing group work still reserves the costly-event budget independently.
                else if (now >= value.until) value.status = "completed";
            }
            while (director.events.Count > HotelMvpDirector.MaxHistory)
            {
                int terminal = director.events.FindIndex(e => e.status != "active");
                if (terminal < 0) break; // Never drop an unresolved cause to make room for another event.
                director.events.RemoveAt(terminal);
            }
            while (director.recent.Count > HotelMvpDirector.MaxRecent) director.recent.RemoveAt(0);
            director.cooldowns.RemoveAll(c => c.until <= now || !HotelMvpDirector.KnownCooldown(c.id));
        }
    }
}
