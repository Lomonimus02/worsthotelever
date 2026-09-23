using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    // Plain snapshots only: the native runner calls RunAll; no scene, factory, save or input side effects.
    public static class HotelHudTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "HUD stays quiet for safe, isolated, legacy and settled worlds", Quiet);
            Run(passed, "HUD alerts survive panels without changing priority", PanelAlerts);
            Run(passed, "HUD self-help explains menu input, supplies and competing rescue", SelfHelp);
            Run(passed, "HUD death offers only valid host/client continuation", Dead);
            Run(passed, "HUD critical countdown outranks rescue and incidents", Critical);
            Run(passed, "HUD rescue outranks incidents and keeps offline bodies visible", Rescue);
            Run(passed, "HUD two-hazard priority uses severity then distance", Incidents);
            Run(passed, "HUD clock and objective follow phase, pacing and tutorial state", ClockAndObjective);
            Run(passed, "HUD focus shortens only the prefix and preserves reasons and penalties", Focus);
            Run(passed, "HUD queries leave every authoritative snapshot unchanged", ReadOnly);
            Run(passed, "HUD preserves actionable notices while hiding redundant preparation briefs", Notices);
            Run(passed, "Journal TAB closes all pages without treating pause or settings as pages", JournalPages);
            return passed;
        }

        static HotelState Fixture()
        {
            var state = new HotelState { version = 3, contentVersion = 3, phase = "preparation", dayLength = 1080,
                guidedOpening = true, mvp = new MvpHotelState(),
                danger = new HotelDangerState { schema = 1, requiredIncidents = 2, requiredService = 2,
                    minimumSeconds = 180, penalty = 137, reward = 180 } };
            for (int n = 101; n <= 106; n++)
            {
                var room = new RoomState { number = n, bed = n == 102 ? 1 : 2,
                    mvp = new MvpRoomState { owned = n <= 104 } };
                foreach (string kind in new[] { "sink", "toilet", "tv", "lamp" })
                    room.mvp.equipment.Add(new MvpEquipmentState { kind = kind });
                state.rooms.Add(room);
            }
            state.players.Add(new PlayerState { id = 0, position = HotelLayout.Spawn });
            state.players.Add(new PlayerState { id = 77, position = HotelLayout.Spawn + Vector3.right * .7f });
            state.danger.crew.Add(new HotelCrewState { slot = 0, joined = true, position = state.players[0].position });
            state.danger.crew.Add(new HotelCrewState { slot = 1, joined = true, position = state.players[1].position });
            state.danger.incidents.Add(new HotelIncidentState { room = 101, kind = "electric", warningRemaining = 8 });
            state.danger.incidents.Add(new HotelIncidentState { room = 102, kind = "steam", warningRemaining = 5 });
            state.danger.incidents.Add(new HotelIncidentState { room = 103, kind = "fumes", warningRemaining = 3 });
            return state;
        }

        static HotelState Active()
        {
            var state = Fixture();
            state.phase = "open"; state.guidedOpening = false; state.danger.status = "active";
            return state;
        }

        static void Down(HotelState state, int slot)
        {
            var crew = state.danger.crew[slot];
            crew.life = "downed"; crew.health = 0; crew.bleedout = 23;
        }

        static void Quiet()
        {
            var state = Fixture();
            foreach (bool panel in new[] { false, true }) RequireEmpty(HotelHudModel.Alert(state, 0, panel));
            state = Active(); state.danger.crew[0].health = 45; // Routine first aid is not an alert.
            RequireEmpty(HotelHudModel.Alert(state, 0, false));
            state.danger.incidents[0].status = "active"; state.danger.incidents[0].isolated = true;
            RequireEmpty(HotelHudModel.Alert(state, 0, true));
            state.phase = "summary"; state.danger.settled = true; state.danger.status = "failed";
            Down(state, 0); state.danger.safety = 0;
            RequireEmpty(HotelHudModel.Alert(state, 0, true));
            foreach (int version in new[] { 1, 2 })
            {
                var legacy = new HotelState { version = version, contentVersion = version,
                    mvp = version == 2 ? new MvpHotelState() : null };
                RequireEmpty(HotelHudModel.Alert(legacy, 0, true));
            }
        }

        static void PanelAlerts()
        {
            var state = Active(); state.danger.incidents[0].status = "warning";
            AssertPanelAlert(state, "incident");
            state.danger.incidents[0].status = "active"; AssertPanelAlert(state, "incident");
            Down(state, 1); AssertPanelAlert(state, "rescue");
            state.danger.safety = 0; AssertPanelAlert(state, "critical");
            Down(state, 0); AssertPanelAlert(state, "downed");
            state.danger.crew[0].life = "dead"; AssertPanelAlert(state, "dead");
        }

        static void AssertPanelAlert(HotelState state, string kind)
        {
            var hud = HotelHudModel.Alert(state, 0, false);
            var panel = HotelHudModel.Alert(state, 0, true);
            Require(hud.kind == kind && panel.kind == kind && hud.title == panel.title &&
                hud.target == panel.target && hud.urgent == panel.urgent && panel.detail != "",
                "Opening a panel lost or changed " + kind + " priority");
        }

        static void SelfHelp()
        {
            var state = Active(); Down(state, 0);
            var hud = HotelHudModel.Alert(state, 0, false);
            var panel = HotelHudModel.Alert(state, 0, true);
            Require(hud.kind == "downed" && hud.urgent && hud.title.Contains("23 с") &&
                hud.detail.Contains("Удерживайте E") && hud.detail.Contains("1 аптечка"), "Self-help duration/cost/action missing");
            Require(panel.detail.Contains("Закройте") && panel.detail.Contains("E"), "Panel falsely accepts held E");
            state.danger.medkits = 0; AssertRecoveryReason(state, "закончились");
            state.danger.medkits = 1; state.danger.selfRescues = 0; AssertRecoveryReason(state, "уже использована");
            state.danger.selfRescues = 1; state.players[1].workTarget = "rescue_0";
            AssertRecoveryReason(state, "уже оказывают");
            state.players[1].workTarget = "";
            Down(state, 1);
            Require(HotelHudModel.Alert(state, 77, true).kind == "downed", "Client sees colleague rescue instead of own self-help");
        }

        static void AssertRecoveryReason(HotelState state, string fragment)
        {
            string reason = HotelDangerRules.WorkError(state, 0, "recover_0");
            Require(reason.Contains(fragment), "Invalid self-help fixture: " + reason);
            foreach (bool panel in new[] { false, true })
            {
                var alert = HotelHudModel.Alert(state, 0, panel);
                Require(alert.detail.Contains(reason) && !alert.detail.Contains("Удерживайте E"), "Unavailable self-help lost its reason");
            }
        }

        static void Dead()
        {
            var state = Active(); state.danger.crew[0].life = "dead"; state.danger.crew[0].health = 0;
            var alert = HotelHudModel.Alert(state, 0, true);
            Require(alert.kind == "dead" && alert.target == "" && !alert.detail.Contains("самопомощ") &&
                !alert.detail.Contains("признать провал"), "Dead host with a connected responder gets wrong actions");
            state.players.RemoveAt(1); // A healthy but offline partner must not lock out forfeit.
            Require(HotelDangerRules.HostCanForfeit(state) && HotelHudModel.Alert(state, 0, true).detail.Contains("признать провал"),
                "Offline healthy partner hides host forfeit");
            state.danger.crew[1].life = "dead"; state.danger.crew[1].health = 0;
            Require(!HotelHudModel.Alert(state, 77, true).detail.Contains("признать провал"), "Client offered host-only forfeit");
        }

        static void Critical()
        {
            var state = Active(); state.danger.incidents[0].status = "active"; Down(state, 1);
            state.danger.safety = 0; state.danger.criticalRemaining = 11.2f;
            var alert = HotelHudModel.Alert(state, 0, true);
            Require(alert.kind == "critical" && alert.urgent && alert.target == "alarm" && alert.title.Contains("12 с"),
                "Critical window failed to outrank rescue/incident");
            Down(state, 0); alert = HotelHudModel.Alert(state, 0, true);
            Require(alert.kind == "downed" && alert.title.Contains("23 с") && alert.title.Contains("12 с"),
                "Own bleedout hides collapse countdown");
            state.danger.crew[0].life = "dead";
            Require(HotelHudModel.Alert(state, 0, true).title.Contains("12 с"), "Dead state hides collapse countdown");
        }

        static void Rescue()
        {
            var state = Active(); state.danger.incidents[0].status = "active"; Down(state, 1);
            var alert = HotelHudModel.Alert(state, 0, false);
            Require(alert.kind == "rescue" && alert.target == "rescue_1" && alert.urgent && alert.title.Contains("23 с"),
                "Incident outranks teammate bleedout");
            state.players.RemoveAt(1); state.danger.medkits = 0;
            alert = HotelHudModel.Alert(state, 0, true);
            Require(alert.kind == "rescue" && alert.detail.Contains("закончились"), "Offline body or empty supplies hidden");
        }

        static void Incidents()
        {
            var state = Active(); var near = state.danger.incidents[0]; var far = state.danger.incidents[1];
            state.players[0].position = HotelDangerRules.Source(near);
            near.status = "warning"; far.status = "active";
            var alert = HotelHudModel.Alert(state, 0, false);
            Require(alert.target == "isolate_102" && alert.urgent && alert.title.Contains("ещё угроза: № 101"),
                "Nearby warning outranks active hazard, or second hazard disappeared");
            near.status = "active"; alert = HotelHudModel.Alert(state, 0, true);
            Require(alert.target == "isolate_101" && alert.title.Contains("ещё угроза: № 102"), "Equal severity ignores distance");
            state.danger.incidents.Reverse();
            Require(HotelHudModel.Alert(state, 0, false).target == "isolate_101", "List order replaces distance priority");
            near.isolated = true; far.status = "warning";
            alert = HotelHudModel.Alert(state, 0, true);
            Require(alert.target == "isolate_102" && !alert.urgent && alert.title.Contains("5 с") && !alert.title.Contains("ещё угроза"),
                "Isolated incident masks remaining warning");
            far.status = "resolved"; RequireEmpty(HotelHudModel.Alert(state, 0, true));
            var fumes = state.danger.incidents.Find(i => i.kind == "fumes"); fumes.status = "warning";
            Require(HotelHudModel.Alert(state, 0, false).title.Contains("Испарения"), "Fumes mislabeled");
        }

        static void ClockAndObjective()
        {
            var state = Fixture();
            Require(HotelHudModel.Clock(state) == "Подготовка", "Preparation shows a running timer");
            var hint = HotelOnboarding.GetHint(state, 0);
            Require(hint != null && hint.body != "" && HotelHudModel.Objective(state, 0) == hint.title, "Objective differs from selected tutorial");
            state.tutorialSkipped = true;
            Require(HotelHudModel.Objective(state, 0) == "", "Hidden tutorial remains on HUD");
            state.tutorialSkipped = false; state.phase = "open";
            Require(HotelHudModel.Clock(state).Contains("Обучение"), "Guided clock promises a running countdown");
            state.guidedOpening = false; state.time = state.dayLength - 65;
            Require(HotelHudModel.Clock(state).EndsWith("01:05", StringComparison.Ordinal), "Countdown formatted incorrectly");
            state.time = state.dayLength + 1;
            Require(HotelHudModel.Clock(state).EndsWith("00:00", StringComparison.Ordinal), "Negative countdown");
            state.phase = "closing"; Require(HotelHudModel.Clock(state) == "Пора закрываться", "Closing phase missing");
            Down(state, 0); Require(HotelHudModel.Objective(state, 0) == "", "Downed player told to perform tutorial work");
            state.danger.crew[0].life = "dead"; Require(HotelHudModel.Objective(state, 0) == "", "Dead player gets tutorial");
            state.phase = "summary"; state.danger.settled = true;
            Require(HotelHudModel.Clock(state) == "Смена окончена" && HotelHudModel.Objective(state, 0) == "", "Terminal HUD suggests ordinary work");
        }

        static void Focus()
        {
            const string prefix = "Удерживайте E: ";
            Require(HotelHudModel.Focus(null) == "" && HotelHudModel.Focus("") == "", "Empty focus is not empty");
            var state = Active(); state.players[0].position = HotelDangerRules.Alarm;
            string alarm = HotelPresentation.FocusText(state, 0, "alarm", "");
            Require(alarm.StartsWith(prefix, StringComparison.Ordinal) && alarm.Contains("137"), "Alarm fixture lacks hold prefix/penalty");
            Require(HotelHudModel.Focus(alarm) == alarm.Substring(prefix.Length), "Alarm consequence/penalty altered");
            state.danger.medkits = 0; Down(state, 0); state.danger.crew[0].position = state.players[0].position;
            string unavailable = HotelPresentation.FocusText(state, 0, "recover_0", "");
            Require(HotelHudModel.Focus(unavailable) == unavailable && unavailable.Contains("закончились"), "Failure reason altered");
            string embedded = "Подсказка коллеги: Удерживайте E: здесь уже оказывают помощь · −137 ₽";
            Require(HotelHudModel.Focus(embedded) == embedded, "Focus removed an input phrase inside the body");
            Require(HotelHudModel.Focus(prefix + embedded) == embedded, "Focus removed more than the leading prefix");
            string owned = "Чемодан · Анна → № 102 · большой · сначала Q";
            Require(HotelHudModel.Focus(owned) == owned, "Ownership or drop reason shortened");
            Require(HotelHudModel.FocusKey(alarm, "alarm") == "E · держать" && HotelHudModel.FocusKey(owned, "bag") == "E" &&
                HotelHudModel.FocusKey(alarm, "") == "!" && HotelHudModel.FocusKey(null, null) == "!", "Focus key implies the wrong input");
        }

        static void Notices()
        {
            var state=Fixture();
            Require(!HotelHudModel.ShowToast(state,"День 1: проверьте прогноз, цены и номера на доске. Подготовка без таймера."),"Preparation intro still covers the hotel");
            Require(!HotelHudModel.ShowToast(state,""),"Empty toast has a panel");
            foreach(string message in new[]{"В отеле пропала вода. Проверьте общий технический узел у склада.",
                "Сотрудник 1 спасён. Защита 7 сек.: покиньте опасную зону!", "Источник в № 101 изолирован. Урон остановлен; теперь устраните причину.",
                "Не удалось сохранить отель.", "Вводный режим завершён. Часы и сроки гостей работают обычно."})
                Require(HotelHudModel.ShowToast(state,message),"Actionable notice hidden: "+message);
            state.day=3;state.phase="open";state.guidedOpening=false;state.danger.status="active";
            state.danger.incidents[0].status="active";state.danger.incidents[0].isolated=true;
            Require(HotelHudModel.Objective(state,0).Contains("почините источник"),"Isolated source has no next step after onboarding");
        }
        static void JournalPages()
        {
            foreach(string panel in new[]{"tasks","operations","reception","rooms","guests","guest","management","schedule","briefing","summary","finish-confirm","refuse-confirm","abandon-confirm","pace-confirm"})
                Require(HotelHudModel.IsJournalPanel(panel),"TAB would leave page open: "+panel);
            foreach(string panel in new[]{"","menu","pause","settings","new","connect","steam","recovery"})
                Require(!HotelHudModel.IsJournalPanel(panel),"Non-journal page toggled like a journal: "+panel);
        }
        static void ReadOnly()
        {
            var state = Fixture(); AssertReadOnly(state);
            state = Active(); state.danger.incidents[0].status = "active"; state.danger.incidents[1].status = "warning";
            state.mvp.rngState = 4567; state.mvp.rngDraws = 12; AssertReadOnly(state);
            Down(state, 1); AssertReadOnly(state);
            state.danger.safety = 0; state.danger.criticalRemaining = 16; AssertReadOnly(state);
            Down(state, 0); AssertReadOnly(state);
            state.danger.medkits = 0; AssertReadOnly(state);
            state.danger.crew[0].life = "dead"; AssertReadOnly(state);
            state.phase = "summary"; state.danger.settled = true; AssertReadOnly(state);
        }

        static void AssertReadOnly(HotelState state)
        {
            string before = JsonUtility.ToJson(state);
            for (int n = 0; n < 3; n++)
                foreach (ulong id in new ulong[] { 0, 77 })
                {
                    HotelHudModel.Alert(state, id, false); HotelHudModel.Alert(state, id, true);
                    HotelHudModel.Clock(state); HotelHudModel.Objective(state, id);
                    string focus = HotelPresentation.FocusText(state, id, "recover_" + HotelDangerRules.Slot(id), "");
                    HotelHudModel.Focus(focus); HotelHudModel.FocusKey(focus, "recover_" + HotelDangerRules.Slot(id));
                }
            Require(before == JsonUtility.ToJson(state), "HUD changed authoritative state, clocks, supplies or RNG");
        }

        static void RequireEmpty(HotelHudAlert alert)
        {
            Require(alert != null && alert.kind == "" && alert.title == "" && alert.detail == "" && alert.target == "" && !alert.urgent,
                "Quiet snapshot produced alert content");
        }
        static void Run(List<string> passed, string name, Action test)
        {
            try { test(); passed.Add(name); }
            catch (Exception e) { throw new Exception(name + ": " + e.Message, e); }
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
