using System;
using System.IO;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        private bool dangerPromotionPending;

        public static HotelSimulation CreateNewDanger(int? seed = null)
        {
            var simulation = CreateNewMvp(seed);
            simulation.PromoteDanger();
            return simulation;
        }

        public static HotelSimulation ResumeDangerForPlay(HotelState loaded)
        {
            var simulation = ResumeForPlay(loaded);
            simulation.dangerPromotionPending = simulation.State.version < 3;
            if (simulation.dangerPromotionPending && simulation.State.phase == "preparation") simulation.TryPromoteDanger();
            return simulation;
        }

        private void TryPromoteDanger()
        {
            if (State.version >= 3) { dangerPromotionPending = false; return; }
            if (State.mvp == null || State.phase != "preparation") { dangerPromotionPending = true; return; }
            try { PromoteDanger(); }
            catch (InvalidDataException)
            {
                dangerPromotionPending = true;
                State.notice = "Отель продолжает прежнюю смену без новых опасностей. Переход повторится в подготовке, когда сохранение поместится в новую схему.";
            }
        }

        private void PromoteDanger()
        {
            if (State.version == 3) return;
            if (State.mvp == null || State.phase != "preparation") throw new InvalidOperationException("Опасные контракты вводятся только в подготовке.");
            HotelState original = State;
            HotelState candidate = JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(original));
            candidate.version = candidate.contentVersion = 3;
            candidate.danger = new HotelDangerState { schema = 1 };
            // Prepare only a detached candidate. A failed validation cannot partially migrate a hotel.
            State = candidate;
            try { DangerPrepare(); HotelSaveStore.Validate(candidate); }
            finally { State = original; }
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(candidate), original);
            heartbeats.Clear();
            dangerPromotionPending = false;
        }
    }
}
