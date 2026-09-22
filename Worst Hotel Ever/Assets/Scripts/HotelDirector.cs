using System;

namespace WorstHotel
{
    // Stages and target IDs are authoritative saved state. Read-only UI queries never advance them.
    public static class HotelDirector
    {
        public const int Welcome = 0, Service = 1, Preparation = 2, Leak = 3, Released = 4;
        public const float ReleaseArrivalDelay = 45, NormalArrivalInterval = 85;
        private const int ServiceSkills = (int)(HotelTutorialSkill.CheckIn | HotelTutorialSkill.DeliverBag | HotelTutorialSkill.ExtraTowel);

        public static bool IsGuided(HotelState state)
        {
            return state != null && state.contentVersion == 1 && state.guidedOpening && state.day == 1 && state.guidedStage < Released;
        }
        public static bool ClockHeld(HotelState state) { return IsGuided(state) && state.phase == "open"; }

        public static string Status(HotelState state)
        {
            if (state == null) return "Отель не загружен";
            if (!IsGuided(state)) return state.phase == "preparation" ? "Подготовка без таймера" : "Обычный темп смены";
            if (state.phase == "preparation") return "Откройте отель — начнём спокойно";
            if (state.phase != "open") return "Итоги вводной смены";
            if (state.guidedStage == Leak)
                return !state.guidedRepairDone ? "Часы ждут: почините раковину в " + state.guidedLeakRoom : "Часы ждут: вытрите воду в " + state.guidedLeakRoom;
            if ((state.tutorialFlags & (int)HotelTutorialSkill.CheckIn) == 0) return "Часы ждут: зарегистрируйте первого гостя";
            if ((state.tutorialFlags & (int)HotelTutorialSkill.DeliverBag) == 0) return "Часы ждут: доставьте багаж в номер";
            if ((state.tutorialFlags & (int)HotelTutorialSkill.ExtraTowel) == 0)
                return state.guests.Exists(g => g.towelRequested && (g.stage == "staying" || g.stage == "walking"))
                    ? "Часы ждут: принесите запрошенное полотенце" : "Часы ждут: гость освоится и попросит полотенце";
            if ((state.tutorialFlags & (int)HotelTutorialSkill.CleanBed) == 0) return "Часы ждут: застелите свободную кровать";
            return "Часы ждут: гость дойдёт до номера";
        }

        public static string DayBrief(HotelState state)
        {
            if (state == null) return "Создайте или загрузите отель.";
            if (state.contentVersion == 0) return "Обычная смена: гости, багаж, полотенца, уборка и ремонт. Подготовка не ограничена по времени.";
            if (state.day == 1 && IsGuided(state))
                return "Первый день начнётся спокойно: один терпеливый гость, багаж и дополнительное полотенце. Подготовьте чистую кровать, почините первую течь и вытрите воду. Пока осваиваете основы, часы смены и сроки отъезда стоят. Затем начнётся обычный темп; всего сегодня до трёх гостей. Хозяин может закончить вводный режим или смену в любой момент.";
            if (state.day == 2)
                return "День 2: до четырёх гостей с разным терпением и сроком проживания. Вода, грязь и покупки сохранились; бельё и полотенца пополнены. Во время подготовки можно выбрать полезное улучшение на доске: второй ящик инструментов, тележку или кровати. Затем откройте отель на стойке — часы работают в обычном темпе.";
            return "Обычный темп: до " + (state.day == 1 ? "трёх" : "четырёх") + " гостей. Подготовьте номера, следите за запросами и принимайте оплату при выезде; неубранное останется на следующий день.";
        }

        internal static void EndGuidedOpening(HotelState state)
        {
            if (!state.guidedOpening) return;
            bool held = IsGuided(state);
            state.guidedOpening = false; // Permanent opt-out, independent of help visibility.
            if (!held) return;
            state.guidedStage = Released;
            state.nextArrivalTime = state.arrivals == 0 ? state.time : state.time + ReleaseArrivalDelay;
            state.notice = "Вводный режим завершён. Часы и сроки гостей работают обычно; новые прибытия идут постепенно.";
        }

        internal static bool ArrivalDue(HotelState state)
        {
            if (state.arrivals >= (state.day == 1 ? 3 : 4)) return false;
            if (state.contentVersion == 0) return state.time >= state.arrivals * NormalArrivalInterval;
            if (IsGuided(state))
            {
                if (!state.guests.Exists(AvailableForLesson)) return true;
                return state.arrivals < (state.guidedStage >= Preparation ? 2 : 1);
            }
            return state.time >= state.nextArrivalTime;
        }
        internal static void Arrived(HotelState state, GuestState guest)
        {
            if (state.contentVersion == 0) return;
            state.nextArrivalTime = state.time + NormalArrivalInterval;
            if (IsGuided(state) && state.guidedGuestId == 0)
            {
                state.guidedGuestId = guest.id;
                state.guidedStage = Service;
            }
        }

        internal static void Advance(HotelState state)
        {
            if (state.contentVersion == 0 || state.phase != "open") return;
            if (!IsGuided(state))
            {
                if (!state.dailyLeakIssued && !state.rooms.Exists(r => r.leak))
                {
                    GuestState guest = state.guests.Find(g => g.stage == "staying" && g.stay >= 80);
                    if (guest != null) IssueLeak(state, guest, false);
                }
                return;
            }
            if (state.guidedStage < Leak && !state.guests.Exists(g => g.id == state.guidedGuestId && AvailableForLesson(g)))
            {
                GuestState replacement = state.guests.Find(AvailableForLesson);
                if (replacement != null) state.guidedGuestId = replacement.id;
                else if (state.arrivals >= 3)
                {
                    state.guidedStage = Released;
                    state.nextArrivalTime = state.time + ReleaseArrivalDelay;
                    state.notice = "Гости вводной части уже ушли. Продолжайте обычную смену или подведите итоги; выполненные навыки сохранены.";
                    return;
                }
            }
            if (state.guidedStage < Preparation && (state.tutorialFlags & ServiceSkills) == ServiceSkills)
                state.guidedStage = Preparation;
            if (state.guidedStage == Preparation && (state.tutorialFlags & (int)HotelTutorialSkill.CleanBed) != 0)
            {
                GuestState guest = state.guests.Find(g => g.id == state.guidedGuestId && g.stage == "staying");
                // Stable target may have departed in an older snapshot; use a real current occupant.
                if (guest == null) guest = state.guests.Find(g => g.stage == "staying");
                if (guest != null) IssueLeak(state, guest, true);
            }
            if (state.guidedStage == Leak && state.guidedRepairDone)
            {
                RoomState room = state.rooms.Find(r => r.number == state.guidedLeakRoom);
                if (room != null && !room.leak && room.water <= .001f)
                {
                    // If a restored/parallel result is already dry, do not require a nonexistent puddle.
                    // This acknowledges the resolved source, without awarding a player skill.
                    state.guidedMopDone = true;
                    state.guidedStage = Released;
                    state.nextArrivalTime = state.time + ReleaseArrivalDelay;
                    state.notice = "Основы освоены: часы пошли. Следующий гость прибудет примерно через 45 секунд; помогите текущим гостям и примите оплату при выезде.";
                }
            }
        }
        private static bool AvailableForLesson(GuestState guest)
        {
            return !guest.paid && (guest.stage == "queue" || guest.stage == "walking" || guest.stage == "staying");
        }
        private static void IssueLeak(HotelState state, GuestState guest, bool guided)
        {
            RoomState room = state.rooms.Find(r => r.number == guest.room && r.guestId == guest.id);
            if (room == null) return;
            room.leak = true;
            state.dailyLeakIssued = true;
            if (!guest.memories.Contains("Раковина начала протекать")) guest.memories.Add("Раковина начала протекать");
            if (guided)
            {
                state.guidedStage = Leak;
                state.guidedLeakRoom = room.number;
                state.guidedRepairDone = false;
                state.guidedMopDone = false;
            }
            state.notice = "Протечка в номере " + room.number + ": нужны инструменты, затем швабра.";
        }
        internal static void Repaired(HotelState state, RoomState room)
        {
            if (IsGuided(state) && state.guidedStage == Leak && state.guidedLeakRoom == room.number)
                state.guidedRepairDone = true;
        }
        internal static void Mopped(HotelState state, RoomState room)
        {
            if (IsGuided(state) && state.guidedStage == Leak && state.guidedLeakRoom == room.number && state.guidedRepairDone && !room.leak)
                state.guidedMopDone = true;
        }
    }
}
