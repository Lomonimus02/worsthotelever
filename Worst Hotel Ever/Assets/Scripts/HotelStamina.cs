using System;

namespace WorstHotel
{
    // Local movement resource only: never serialized into hotel saves or network commands.
    public sealed class HotelStamina
    {
        public const float Capacity = 100f, ResumeThreshold = 20f;
        public const float WalkSpeed = 3.7f, SprintSpeed = 5.1f, CartSpeed = 2.6f;
        public const float SprintSeconds = 7.5f, RecoveryPerSecond = 20f, RecoveryDelay = .65f;
        public const float MaxDeltaTime = .05f;
        public float Amount { get; private set; } = Capacity;
        public float Normalized => Amount / Capacity;
        public bool Exhausted { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool CanSprint => !Exhausted && Amount > 0;
        float recoveryRemaining;
        string world;
        int day;
        bool hasSession;

        public void ObserveSession(string worldId, int currentDay)
        {
            if (hasSession && world == worldId && day == currentDay) return;
            Reset();
            world = worldId; day = currentDay; hasSession = true;
        }

        public void Reset()
        {
            Amount = Capacity; Exhausted = IsSprinting = false; recoveryRemaining = 0;
            world = null; day = 0; hasSession = false;
        }

        public void StopSprint() { IsSprinting = false; }

        public float Speed(bool shiftHeld, bool carryingCart)
        {
            return carryingCart ? CartSpeed : shiftHeld && CanSprint ? SprintSpeed : WalkSpeed;
        }

        public static float ClampDelta(float dt)
        {
            return float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0 ? 0 : Math.Min(dt, MaxDeltaTime);
        }

        // Distance is measured around CharacterController.Move only, before any pose correction.
        // Partial wall slides cost proportionally; gravity, teleports and stationary Shift cost nothing.
        public void Tick(float dt, bool inputActive, bool canAct, bool shiftHeld, bool carryingCart, float horizontalDistance)
        {
            IsSprinting = false;
            dt = ClampDelta(dt);
            if (dt == 0 || !canAct) return;
            bool moved = !float.IsNaN(horizontalDistance) && !float.IsInfinity(horizontalDistance) &&
                horizontalDistance > dt * .05f;
            if (inputActive && shiftHeld && !carryingCart && CanSprint && moved)
            {
                float fraction = Math.Min(1f, horizontalDistance / (SprintSpeed * dt));
                Amount = Math.Max(0, Amount - Capacity / SprintSeconds * dt * fraction);
                recoveryRemaining = RecoveryDelay;
                IsSprinting = true;
                if (Amount == 0) { Exhausted = true; IsSprinting = false; }
                return;
            }

            // Menus/focus loss are rest, not a paused simulation or an instant refill.
            float rest = Math.Max(0, dt - recoveryRemaining);
            recoveryRemaining = Math.Max(0, recoveryRemaining - dt);
            Amount = Math.Min(Capacity, Amount + RecoveryPerSecond * rest);
            if (Exhausted && Amount >= ResumeThreshold) Exhausted = false;
        }
    }
}
