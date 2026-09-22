using System;
using System.Globalization;
using UnityEngine;

namespace WorstHotel
{
    // All balance and projections live here. Queries never heal, spend supplies or draw RNG.
    public static class HotelDangerRules
    {
        public const float FullHealth = 100, WarningSeconds = 10, DownedSeconds = 45, RescueShield = 7;
        public const float FullSafety = 100, RepairSafety = 35, CriticalSeconds = 30, IncidentGrace = 25;
        public const float RescueHealth = 60, IsolationSeconds = 2, RepairSeconds = 6, FumesRepairSeconds = 5;
        public const float RescueSeconds = 5, RecoverySeconds = 6, FirstAidSeconds = 4, AlarmSeconds = 3;
        public const float ElectricPeriod = 2.5f, ElectricDamage = 16, SteamDamage = 7, FumesDamage = 4;
        public const float FirstIncidentAt = 45, IncidentSpacing = 55;
        public const int InitialMedkits = 3, InitialSelfRescues = 1, MaxServicePoints = 10000, TriggerJitter = 16;
        public static readonly Vector3 FirstAid = new Vector3(-6.5f, 1.1f, -3.3f);
        public static readonly Vector3 Alarm = new Vector3(1.45f, 1.25f, -6.4f);

        public static bool Enabled(HotelState state)
        { return state != null && state.version == 3 && state.contentVersion == 3 && state.mvp != null && state.danger != null && state.danger.schema == 1; }
        public static int Slot(ulong id) { return id == 0 ? 0 : 1; }
        public static HotelCrewState Crew(HotelState state, ulong id)
        { return Enabled(state) && state.danger.crew != null ? state.danger.crew.Find(c => c != null && c.slot == Slot(id)) : null; }
        public static bool CanAct(HotelState state, ulong id)
        {
            if (!Enabled(state)) return true;
            HotelCrewState crew = Crew(state, id);
            return crew != null && crew.joined && crew.life == "healthy" && crew.health > 0;
        }
        public static bool HostCanForfeit(HotelState state)
        {
            if (!Enabled(state) || state.danger.status != "active" || state.danger.settled ||
                (state.phase != "open" && state.phase != "closing")) return false;
            HotelCrewState host = Crew(state, 0);
            // Offline crew retain their lives. Only a connected responder blocks this escape.
            return host != null && host.joined && !CanAct(state, 0) &&
                (state.players == null || !state.players.Exists(p => p != null && CanAct(state, p.id)));
        }
        public static HotelIncidentState Incident(HotelState state, int room)
        { return Enabled(state) && state.danger.incidents != null ? state.danger.incidents.Find(i => i != null && i.room == room) : null; }
        public static Vector3 Source(HotelIncidentState incident)
        {
            if (incident == null) return Vector3.zero;
            return HotelLayout.RoomTarget(incident.kind == "electric" ? "tv" : incident.kind == "steam" ? "sink" : "trash", incident.room);
        }
        public static Vector3 Control(int room)
        { return HotelLayout.Door(room) + new Vector3(room % 2 == 1 ? -.6f : .6f, 1.05f, -.4f); }
        public static float Radius(HotelIncidentState incident)
        {
            if (incident == null) return 0;
            float desired = incident.kind == "electric" ? 1 : incident.kind == "steam" ? 1.1f : Mathf.Min(.7f, .45f + incident.activeSeconds * .01f);
            Vector3 source = Source(incident), center = HotelLayout.RoomCenter(incident.room);
            // Clip the authored disc to this room's rectangular shell, not the corridor or
            // neighbouring room. Electric also leaves capsule clearance around its control.
            float wall = Mathf.Min(Mathf.Abs(source.x) - 1.7f, 7.65f - Mathf.Abs(source.x));
            wall = Mathf.Min(wall, 3.25f - Mathf.Abs(source.z - center.z));
            return Mathf.Max(0, Mathf.Min(desired, wall));
        }
        public static bool IsHot(HotelIncidentState incident)
        { return incident != null && incident.status == "active" && !incident.isolated; }
        public static bool IsWorkTarget(string target)
        {
            return target == "firstaid" || target == "alarm" || (!string.IsNullOrEmpty(target) &&
                (target.StartsWith("hazard_", StringComparison.Ordinal) || target.StartsWith("isolate_", StringComparison.Ordinal) ||
                 target.StartsWith("rescue_", StringComparison.Ordinal) || target.StartsWith("recover_", StringComparison.Ordinal)));
        }
        internal static bool Number(string target, string prefix, int min, int max, out int value)
        {
            value = 0;
            return target != null && target.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(target.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
                value >= min && value <= max && target == prefix + value.ToString(CultureInfo.InvariantCulture);
        }
        public static bool TryTarget(HotelState state, string target, out Vector3 position)
        {
            position = Vector3.zero;
            if (!Enabled(state)) return false;
            if (target == "firstaid") { position = FirstAid; return true; }
            if (target == "alarm") { position = Alarm; return true; }
            if (Number(target, "hazard_", 101, 106, out int room) || Number(target, "isolate_", 101, 106, out room))
            {
                RoomState owned = state.rooms.Find(r => r.number == room);
                HotelIncidentState incident = Incident(state, room);
                if (owned?.mvp == null || !owned.mvp.owned || incident == null) return false;
                position = target.StartsWith("hazard_", StringComparison.Ordinal) ? Source(incident) : Control(room);
                return true;
            }
            if (Number(target, "rescue_", 0, 1, out int slot) || Number(target, "recover_", 0, 1, out slot))
            {
                HotelCrewState crew = state.danger.crew.Find(c => c.slot == slot);
                if (crew == null || !crew.joined) return false;
                position = crew.position; // Self-help is a virtual focus; a body also survives disconnect.
                return true;
            }
            return false;
        }
        internal static bool Reachable(Vector3 from, Vector3 to)
        {
            from.y = to.y = .1f;
            if (!HotelSimulation.Finite(from) || !HotelSimulation.Finite(to) ||
                Vector3.Distance(from, to) > HotelSimulation.InteractionDistance) return false;
            for (int i = 0; i <= 16; i++) if (!HotelSimulation.Walkable(Vector3.Lerp(from, to, i / 16f))) return false;
            return true;
        }
        internal static bool Contains(HotelIncidentState incident, Vector3 position)
        {
            Vector3 source = Source(incident), center = HotelLayout.RoomCenter(incident.room);
            if (Math.Sign(position.x) != Math.Sign(center.x) || Mathf.Abs(position.x) < 1.7f || Mathf.Abs(position.x) > 7.65f ||
                Mathf.Abs(position.z - center.z) > 3.25f) return false;
            source.y = position.y = .1f;
            return Vector3.Distance(source, position) <= Radius(incident) && Reachable(position, source);
        }

        public static string WorkError(HotelState state, ulong id, string target)
        {
            if (!TryTarget(state, target, out Vector3 point)) return "Цель помощи или аварии недоступна.";
            HotelDangerState danger = state.danger;
            if (danger.status != "active" || danger.settled || (state.phase != "open" && state.phase != "closing"))
                return "Помощь и аварийные работы доступны во время смены.";
            PlayerState player = state.players.Find(p => p.id == id);
            HotelCrewState actor = Crew(state, id);
            if (player == null || actor == null || !actor.joined) return "Сотрудник не участвует в смене.";
            ItemState held = state.items.Find(i => !i.consumed && i.holder == (long)id);
            bool recovery = Number(target, "recover_", 0, 1, out int slot);
            if (recovery)
            {
                if (slot != actor.slot || actor.life != "downed" || actor.bleedout <= 0) return "Самопомощь доступна только самому потерявшему сознание.";
            }
            else if (!CanAct(state, id)) return "Сотрудник не может работать: нужна помощь.";
            if (!Reachable(player.position, point)) return "Подойдите ближе: помощь невозможна через стену.";

            if (recovery || Number(target, "rescue_", 0, 1, out slot))
            {
                HotelCrewState patient = danger.crew.Find(c => c.slot == slot);
                if (patient == null || !patient.joined || patient.life != "downed" || patient.bleedout <= 0)
                    return "Спасать можно только потерявшего сознание сотрудника.";
                if (!recovery && slot == actor.slot) return "Для себя используйте аварийную самопомощь.";
                if (held != null) return "Для помощи нужны свободные руки.";
                if (danger.medkits <= 0) return "Общие аптечки закончились.";
                if (recovery && danger.selfRescues <= 0) return "Аварийная самопомощь уже использована в этой смене.";
                if (state.players.Exists(p => p.id != id && (p.workTarget == "rescue_" + slot || p.workTarget == "recover_" + slot)))
                    return "Этому сотруднику уже оказывают помощь.";
                return "";
            }
            if (target == "firstaid")
            {
                if (held != null) return "Освободите руки для аптечки.";
                if (actor.health >= FullHealth) return "Здоровье полное: аптечка не нужна.";
                return danger.medkits > 0 ? "" : "Общие аптечки закончились.";
            }
            if (target == "alarm") return held == null ? "" : "Для эвакуации нужны свободные руки.";
            bool isolate = Number(target, "isolate_", 101, 106, out int room);
            if (!isolate) Number(target, "hazard_", 101, 106, out room);
            HotelIncidentState incident = Incident(state, room);
            if (incident.status != "warning" && incident.status != "active") return "Эта авария сейчас не требует работы.";
            if (isolate)
            {
                if (incident.isolated) return "Источник уже изолирован; устраните аварию.";
                return held == null ? "" : "Для отсекателя нужны свободные руки.";
            }
            if (!incident.isolated) return "Сначала изолируйте источник у входа в номер.";
            string tool = incident.kind == "fumes" ? "mop" : "toolbox";
            return held != null && held.kind == tool ? "" : tool == "mop" ? "Для едкого остатка нужна швабра." : "Для источника нужен ящик инструментов.";
        }
        public static float WorkSeconds(HotelState state, string target)
        {
            if (target == "firstaid") return FirstAidSeconds;
            if (target == "alarm") return AlarmSeconds;
            if (Number(target, "recover_", 0, 1, out _)) return RecoverySeconds;
            if (Number(target, "rescue_", 0, 1, out _)) return RescueSeconds;
            if (Number(target, "isolate_", 101, 106, out _)) return IsolationSeconds;
            if (Number(target, "hazard_", 101, 106, out int room))
                return Incident(state, room)?.kind == "fumes" ? FumesRepairSeconds : RepairSeconds;
            return 1;
        }
        public static string KindName(string kind)
        { return kind == "electric" ? "электрическая дуга" : kind == "steam" ? "горячая труба" : kind == "fumes" ? "едкие испарения" : "неизвестная авария"; }
        public static string ModeName(string mode)
        { return mode == "bold" ? "Высокий риск" : mode == "relief" ? "Передышка" : "Обычный контракт"; }
        public static int IncidentGoal(string mode) { return mode == "relief" ? 0 : mode == "bold" ? 3 : 2; }
        public static int ServiceGoal(string mode) { return mode == "relief" ? 0 : mode == "bold" ? 4 : 2; }
        public static float MinimumSeconds(string mode) { return mode == "relief" ? 0 : mode == "bold" ? 260 : 180; }
        public static int Reward(string mode) { return mode == "relief" ? 0 : mode == "bold" ? 320 : 180; }
        public static int Penalty(string mode) { return mode == "relief" ? 0 : mode == "bold" ? 120 : 80; }
        public static int SimultaneousIncidents(string mode) { return mode == "relief" ? 0 : mode == "bold" ? 2 : 1; }
        public static float SafetyLoss(string kind) { return kind == "electric" ? .65f : kind == "steam" ? .9f : .5f; }
        internal static float Damage(string kind, float from, float to)
        {
            if (to <= from) return 0;
            if (kind == "electric") return ElectricDamage * (Mathf.FloorToInt((to + .00001f) / ElectricPeriod) - Mathf.FloorToInt((from + .00001f) / ElectricPeriod));
            return (to - from) * (kind == "steam" ? SteamDamage : FumesDamage);
        }
        public static bool PowerAvailable(HotelState state, int room)
        {
            if (state?.mvp?.utilities == null || state.mvp.utilities.powerFault) return false;
            HotelIncidentState incident = Incident(state, room);
            return incident == null || incident.kind != "electric" || !IsIsolated(incident);
        }
        public static bool WaterAvailable(HotelState state, int room)
        {
            if (state?.mvp?.utilities == null || state.mvp.utilities.waterFault) return false;
            HotelIncidentState incident = Incident(state, room);
            return incident == null || incident.kind != "steam" || !IsIsolated(incident);
        }
        private static bool IsIsolated(HotelIncidentState incident)
        { return incident.isolated && (incident.status == "warning" || incident.status == "active"); }
        internal static bool GoalsMet(HotelState state)
        {
            HotelDangerState d = state.danger;
            if (d.servicePoints < d.requiredService || d.elapsed < d.minimumSeconds || d.resolved < d.requiredIncidents ||
                !d.crew.Exists(c => c.joined) || d.crew.Exists(c => c.joined && c.life != "healthy")) return false;
            for (int i = 0; i < d.requiredIncidents; i++)
                if (i >= d.incidents.Count || d.incidents[i].status != "resolved") return false;
            return true;
        }
        public static string Objective(HotelState state)
        {
            if (!Enabled(state)) return "Опасные контракты недоступны.";
            HotelDangerState d = state.danger;
            if (d.settled) return d.reason;
            if (d.mode == "relief") return "Передышка: обычное обслуживание без контрактной премии.";
            string text = ModeName(d.mode) + ": сервис " + d.servicePoints + "/" + d.requiredService +
                ", аварии " + d.resolved + "/" + d.requiredIncidents + ", время " + Mathf.FloorToInt(d.elapsed) + "/" + Mathf.CeilToInt(d.minimumSeconds) + " сек.";
            if (d.safety <= 0) text += " КРИТИЧЕСКИ: устраните источник или эвакуируйтесь за " + Mathf.CeilToInt(d.criticalRemaining) + " сек.";
            if (d.crew.Exists(c => c.joined && c.life != "healthy")) text += " Команда не в полном составе: требуется помощь; погибший исключает победу.";
            return text;
        }
    }
}
