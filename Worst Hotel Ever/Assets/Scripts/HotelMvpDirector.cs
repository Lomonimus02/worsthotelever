using System;
using UnityEngine;

namespace WorstHotel
{
    // Policy and UI projection only. The simulation partial owns mutations and RNG draws.
    public static class HotelMvpDirector
    {
        public const int MaxHistory = 24, MaxRecent = 3;
        public const float HealthyThreshold = 25, HighThreshold = 55, OverloadThreshold = 80;
        public const float UpgradeGraceSeconds = 150, RecoverySeconds = 75;
        internal static readonly string[] Kinds = { "rush", "walkin_group", "vip", "water" };

        public static string Status(HotelState state)
        {
            if (state == null || state.mvp == null || state.mvp.director == null) return "Режиссёр событий недоступен.";
            if (state.phase == "preparation") return "Подготовка: случайные события и их сроки ждут открытия.";
            if (state.phase == "summary") return "Смена завершена: новые события не добавляются.";
            if (state.phase == "open" && Guided(state)) return "Учебный темп: случайные события ждут завершения вводной части.";
            MvpDirectorState director = state.mvp.director;
            float workload = HotelWorkload.Measure(state);
            string load = "Нагрузка " + Mathf.RoundToInt(workload) + "/100 · " + BandLabel(Band(workload)) + ". ";
            if (state.phase == "closing") return load + "Приём закрыт: завершите текущие дела.";
            if (workload >= OverloadThreshold) return load + "Перегрузка: дополнительные события приостановлены.";
            if (director.recoveryUntil > state.mvp.elapsed)
                return load + "Передышка: ещё " + Mathf.CeilToInt(director.recoveryUntil - state.mvp.elapsed) + " сек. игрового времени.";
            if (workload >= HighThreshold || director.stress >= 65)
                return load + "Сначала разгрузите команду; недавнее напряжение ещё учитывается.";
            MvpEventState active = director.events.Find(e => e != null && e.status == "active" && Costly(e.kind));
            if (active != null) return load + "Сейчас: " + EventName(active.kind) + ". Второй крупный наплыв подождёт.";
            if (GroupPressure(state)) return load + "Команда обслуживает группу; новый крупный наплыв подождёт.";
            active = director.events.Find(e => e != null && e.status == "active" && e.kind == "water");
            if (active != null) return load + "Водяная авария в № " + active.room + ": устраните источник и уберите воду.";
            return load + "Спокойный интервал; следующее решение может не добавить событие.";
        }

        internal static bool Guided(HotelState state)
        {
            return state.guidedOpening && state.day == 1 && state.guidedStage < HotelDirector.Released;
        }
        internal static string Band(float workload)
        {
            return workload >= OverloadThreshold ? "overload" : workload >= HighThreshold ? "high" :
                workload >= HealthyThreshold ? "healthy" : "low";
        }
        private static string BandLabel(string band)
        {
            return band == "overload" ? "перегрузка" : band == "high" ? "высокая" : band == "healthy" ? "рабочий темп" : "низкая";
        }
        internal static string Category(string kind) { return kind == "water" ? "operational" : "guest"; }
        internal static bool Costly(string kind) { return kind == "rush" || kind == "walkin_group" || kind == "vip"; }
        internal static bool GroupPressure(HotelState state)
        {
            foreach (MvpGroupState group in state.mvp.groups)
            {
                if (group.arrivalDay > state.day || group.status == "completed") continue;
                if (group.status == "arriving" || group.status == "staying" || group.status == "cleanup") return true;
                if (group.status == "expected" && state.mvp.schedule.Exists(s => s.groupId == group.id &&
                    s.day == state.day && s.status == "planned" && s.time <= state.time + 45)) return true;
            }
            return false;
        }
        internal static string EventName(string kind)
        {
            return kind == "rush" ? "наплыв гостей" : kind == "walkin_group" ? "неожиданная группа" :
                kind == "vip" ? "прибытие VIP" : "небольшая протечка";
        }

        // Bounded potential changes event mix and recovery spacing, never work duration,
        // existing guest contracts or an unbounded arrival rate. Workload is separate.
        internal static float DifficultyPotential(HotelState state)
        {
            int owned = 0, occupied = 0, improved = 0;
            foreach (RoomState room in state.rooms)
            {
                if (room.mvp == null || !room.mvp.owned) continue;
                owned++;
                if (room.guestId != 0) occupied++;
                if (room.mvp.bedQuality > 1 || room.mvp.tvQuality > 1) improved++;
            }
            float days = Mathf.Clamp01((state.day - 1) / 12f) * .35f;
            float size = Mathf.Clamp01((owned - 4) / 2f) * .2f;
            float guests = occupied / (float)Math.Max(1, owned) * .15f;
            float amenities = (state.mvp.betterLinen ? .04f : 0) + (state.mvp.coffeeMachine ? .06f : 0) +
                improved / (float)Math.Max(1, owned) * .1f;
            float reputation = Mathf.Clamp01((state.mvp.reputation - 3) / 2) * .1f;
            return Mathf.Clamp01(days + size + guests + amenities + reputation);
        }

        internal static int Weight(string kind, HotelState state, float potential)
        {
            if (kind == "rush") return 20 + Mathf.RoundToInt(potential * 10);
            if (kind == "water") return 16;
            if (kind == "walkin_group") return state.day >= 3 ? 8 + Mathf.RoundToInt(potential * 18) : 0;
            if (kind == "vip") return state.day >= 2 && state.mvp.reputation >= 3.5f ? 8 + Mathf.RoundToInt(potential * 14) : 0;
            return 0;
        }
        internal static float EventCooldown(string kind)
        {
            return kind == "rush" ? 300 : kind == "water" ? 360 : 420;
        }
        internal static float Duration(string kind)
        {
            return kind == "rush" ? 150 : kind == "walkin_group" ? 210 : kind == "vip" ? 180 : 120;
        }
        internal static bool KnownCooldown(string id)
        {
            if (id == "category_guest" || id == "category_operational") return true;
            foreach (string kind in Kinds) if (id == "event_" + kind) return true;
            return false;
        }
    }
}
