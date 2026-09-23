using System;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    // Read-only priorities: quiet when safe, one actionable warning when not.
    public sealed class HotelHudAlert
    {
        public string kind = "", title = "", detail = "", target = "";
        public bool urgent;
    }

    public static class HotelHudModel
    {
        public static HotelHudAlert Alert(HotelState state, ulong playerId, bool panelOpen)
        {
            var result = new HotelHudAlert();
            if (!HotelDangerRules.Enabled(state) || HotelPresentation.DangerTerminal(state)) return result;
            var danger = state.danger;
            var self = HotelDangerRules.Crew(state, playerId);
            bool critical = danger.status == "active" && danger.safety <= 0;
            string collapse = critical ? " · до провала " + Seconds(danger.criticalRemaining) : "";
            if (self?.life == "downed")
            {
                string reason = HotelDangerRules.WorkError(state, playerId, "recover_" + self.slot);
                return new HotelHudAlert { kind = "downed", urgent = true,
                    title = "Нужна помощь · " + Seconds(self.bleedout) + collapse,
                    detail = reason == "" ? (panelOpen ? "Закройте журнал: удерживайте E · 1 аптечка" : "Удерживайте E для самопомощи · 1 аптечка") : reason + " · ждите коллегу" };
            }
            if (self?.life == "dead")
                return new HotelHudAlert { kind = "dead", urgent = true, title = "Вы погибли до конца смены" + collapse,
                    detail = HotelDangerRules.HostCanForfeit(state) && playerId == 0 ? "TAB → признать провал или дождаться коллеги" : "Коллега ещё может закончить смену · TAB — журнал" };
            if (critical)
                return new HotelHudAlert { kind = "critical", urgent = true, title = "До провала смены · " + Seconds(danger.criticalRemaining),
                    detail = "Устраните источник или включите тревогу у выхода", target = "alarm" };
            var colleague = danger.crew.FirstOrDefault(c => c.joined && c.slot != HotelDangerRules.Slot(playerId) && c.life == "downed");
            if (colleague != null)
                return new HotelHudAlert { kind = "rescue", urgent = true, title = "Коллега упал · " + Seconds(colleague.bleedout),
                    detail = danger.medkits > 0 ? "Свободные руки → к коллеге → удерживайте E · 1 аптечка" : "Аптечки закончились · TAB — состояние команды",
                    target = "rescue_" + colleague.slot };
            var at = state.players.Find(p => p.id == playerId)?.position ?? HotelLayout.Spawn;
            var incidents = danger.incidents.Where(i => !i.isolated && (i.status == "warning" || i.status == "active"))
                .OrderBy(i => i.status == "active" ? 0 : 1).ThenBy(i => (HotelDangerRules.Source(i) - at).sqrMagnitude).ToList();
            if (incidents.Count == 0) return result;
            var hazard = incidents[0];
            string kind = hazard.kind == "electric" ? "Электричество" : hazard.kind == "steam" ? "Горячий пар" : "Испарения";
            string extra = incidents.Count > 1 ? " · ещё угроза: № " + incidents[1].room : "";
            return new HotelHudAlert { kind = "incident", urgent = hazard.status == "active",
                title = kind + " · № " + hazard.room + (hazard.status == "warning" ? " · через " + Seconds(hazard.warningRemaining) : " · опасно") + extra,
                detail = "Не входите в зону · отсекатель у двери: свободные руки + E", target = "isolate_" + hazard.room };
        }

        public static string Seconds(float value) => Mathf.CeilToInt(Mathf.Max(0, value)) + " с";
        public static string Clock(HotelState state)
        {
            if (state.phase == "preparation") return "Подготовка";
            if (state.phase == "summary") return "Смена окончена";
            if (HotelDirector.ClockHeld(state)) return "Обучение · без спешки";
            if (state.phase == "closing") return "Пора закрываться";
            int seconds = Mathf.CeilToInt(Mathf.Max(0, state.dayLength - state.time));
            return "До закрытия " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        public static string Objective(HotelState state, ulong id)
        {
            if (HotelDangerRules.Enabled(state) && !HotelDangerRules.CanAct(state, id)) return "";
            if (HotelPresentation.DangerTerminal(state)) return "";
            var hint=HotelOnboarding.GetHint(state,id);
            if(hint!=null)return hint.title;
            if(state.tutorialSkipped)return ""; // Explicitly hidden assistance stays hidden.
            if(HotelDangerRules.Enabled(state)) {
                var repair=state.danger.incidents.FirstOrDefault(i=>i.isolated&&i.status=="active");
                if(repair!=null)return "№ "+repair.room+": "+(repair.kind=="fumes"?"уберите остаток шваброй":"почините источник инструментами");
            }
            if(state.phase=="preparation")return "Подготовьте номера и откройте отель у стойки";
            if(state.phase=="closing")return "Оформите выезды и завершите смену у стойки";
            return (state.mvp==null?HotelSimulation.BuildTasks(state):HotelPresentation.MvpTasks(state)).FirstOrDefault()??"";
        }

        public static bool IsJournalPanel(string panel)
        {
            switch(panel) {
                case "tasks":case "operations":case "reception":case "rooms":case "guests":case "guest":
                case "management":case "schedule":case "briefing":case "summary":case "finish-confirm":
                case "refuse-confirm":case "abandon-confirm":case "pace-confirm":return true;
                default:return false;
            }
        }
        public static bool ShowLesson(HotelState state,ulong id) => HotelDangerRules.CanAct(state,id) &&
            !HotelPresentation.DangerTerminal(state) && Alert(state,id,true).kind=="";
        public static bool ShowToast(HotelState state,string text)
        {
            if(string.IsNullOrEmpty(text))return false;
            if(HotelPresentation.DangerTerminal(state)&&text==state.danger.reason)return false;
            if(state==null||state.phase!="preparation")return true;
            return text!="День "+state.day+": проверьте прогноз, цены и номера на доске. Подготовка без таймера." &&
                text!="День "+state.day+". Грязь, поломки и ночующие гости сохранены. Запасы пополнены, прогноз готов." &&
                text!="День "+state.day+". Запасы пополнены; грязь и поломки остались. Подготовьтесь и откройте отель.";
        }

        public static string Focus(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            // Shorten only the input prefix; never truncate failure reasons, ownership or costs.
            const string prefix="Удерживайте E: ";
            return text.StartsWith(prefix,StringComparison.Ordinal)?text.Substring(prefix.Length):text;
        }
        public static string FocusKey(string text, string id) => string.IsNullOrEmpty(id) ? "!" :
            (text ?? "").StartsWith("Удерживайте E:", StringComparison.Ordinal) ? "E · держать" : "E";
    }
}
