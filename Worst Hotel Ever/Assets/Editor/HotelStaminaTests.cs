using System;
using System.Collections.Generic;

namespace WorstHotel
{
    // Deterministic native runner suite: no scene, keyboard, clock, saves or preferences.
    public static class HotelStaminaTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "Stamina: old movement speeds and 7.5-second full sprint", Endurance);
            Run(passed, "Stamina: delayed five-second recovery clamps at full", Recovery);
            Run(passed, "Stamina: held Shift cannot restart below twenty after exhaustion", Hysteresis);
            Run(passed, "Stamina: actual horizontal distance controls drain; idle and walls do not", Displacement);
            Run(passed, "Stamina: menus/focus/cart rest; incapacitation freezes without refill", InputAndLife);
            Run(passed, "Stamina: invalid and stalled timesteps cannot corrupt or overdraw", DeltaBounds);
            Run(passed, "Stamina: same session preserves reserve; new world/day/session resets", Sessions);
            Run(passed, "Stamina: drain and recovery agree across frame partitions", FramePartitions);
            return passed;
        }

        static void Endurance()
        {
            var s = new HotelStamina();
            Near(s.Amount, 100, "Fresh reserve"); Near(s.Normalized, 1, "Fresh normalized reserve");
            Near(s.Speed(false, false), 3.7f, "Walk speed");
            Near(s.Speed(true, false), 5.1f, "Sprint speed");
            Near(s.Speed(true, true), 2.6f, "Cart speed with Shift");
            Near(s.Speed(false, true), 2.6f, "Cart speed without Shift");
            float elapsed = Deplete(s);
            Require(elapsed >= 7.49f && elapsed <= 7.52f, "Full sprint lasted " + elapsed);
            Near(s.Amount, 0, "Empty reserve"); Near(s.Normalized, 0, "Empty normalized reserve");
            Require(!s.CanSprint && !s.IsSprinting, "Exhaustion permits sprint");
            Near(s.Speed(true, false), 3.7f, "Exhausted movement must still walk");
        }

        static void Recovery()
        {
            var s = new HotelStamina(); Deplete(s);
            Rest(s, .6f, .01f); Near(s.Amount, 0, "Recovered before delay");
            Rest(s, .3f, .01f); Near(s.Amount, 5, "Only time beyond the .65-second delay counts");
            Rest(s, 4.7f, .01f); Near(s.Amount, 99, "Recovery rate");
            Rest(s, .2f, .01f); Near(s.Amount, 100, "Recovery exceeded cap");
            Rest(s, 2, .05f); Near(s.Amount, 100, "Idle exceeded cap");
        }

        static void Hysteresis()
        {
            var s = new HotelStamina(); Deplete(s);
            // Simulate held Shift and walking throughout the exhausted recovery window.
            for (int i = 0; i < 160; i++)
            {
                Require(s.Exhausted && !s.CanSprint, "Restarted below twenty");
                Near(s.Speed(true, false), 3.7f, "Exhausted Shift speed");
                s.Tick(.01f, true, true, true, false, 3.7f * .01f);
            }
            Near(s.Amount, 19, "Reserve before restart threshold");
            for (int i = 0; i < 10 && s.Exhausted; i++) s.Tick(.01f, true, true, true, false, 3.7f * .01f);
            Require(s.CanSprint && s.Amount >= 20 && s.Amount < 20.3f, "Threshold did not rearm");
            float before = s.Amount;
            s.Tick(.05f, true, true, true, false, 5.1f * .05f);
            Require(s.IsSprinting && s.Amount < before && s.Amount > 19, "Rearmed sprint did not consume reserve");
            s.StopSprint(); Require(!s.IsSprinting, "Input cancellation retained sprint flag");
            Near(s.Amount, before - 100f / 7.5f * .05f, "Stopping input refilled reserve");
        }

        static void Displacement()
        {
            var full = new HotelStamina(); var slide = new HotelStamina(); var wall = new HotelStamina();
            for (int i = 0; i < 40; i++)
            {
                full.Tick(.05f, true, true, true, false, 5.1f * .05f);
                slide.Tick(.05f, true, true, true, false, 5.1f * .05f * .5f);
                wall.Tick(.05f, true, true, true, false, 0);
            }
            Near(100 - full.Amount, (100 - slide.Amount) * 2, "Sliding must cost its actual distance");
            Near(wall.Amount, 100, "Stationary Shift drained"); Require(!wall.IsSprinting, "Wall reported sprinting");
            wall.Tick(.05f, true, true, true, false, .00001f);
            Near(wall.Amount, 100, "Controller jitter drained");
            foreach (float distance in new[] { float.NaN, float.PositiveInfinity, -1f })
                wall.Tick(.05f, true, true, true, false, distance);
            Near(wall.Amount, 100, "Invalid displacement poisoned reserve");
            wall.Tick(.05f, true, true, true, false, 1000);
            Near(wall.Amount, 100 - 100f / 7.5f * .05f, "Displacement outlier exceeded one frame's cost");
        }

        static void InputAndLife()
        {
            var s = new HotelStamina(); RunFor(s, 3, .01f);
            float before = s.Amount;
            for (int i = 0; i < 100; i++) s.Tick(.05f, true, false, true, false, 5.1f * .05f);
            Near(s.Amount, before, "Downed/dead reserve changed"); Require(!s.IsSprinting, "Incapacitated sprint");
            Rest(s, .6f, .01f); Near(s.Amount, before, "Incapacitation skipped recovery delay");
            // Closed input covers tablet, pause, settings, missing keyboard and lost focus.
            for (int i = 0; i < 100; i++) s.Tick(.05f, false, true, true, false, 5.1f * .05f);
            Near(s.Amount, 100, "Menus/focus did not recover normally"); Require(!s.IsSprinting, "Closed input sprint");
            RunFor(s, 3, .01f);
            for (int i = 0; i < 100; i++) s.Tick(.05f, true, true, true, true, 2.6f * .05f);
            Near(s.Amount, 100, "Cart Shift drained or blocked recovery"); Require(!s.IsSprinting, "Cart sprint");
        }

        static void DeltaBounds()
        {
            var s = new HotelStamina();
            foreach (float dt in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                s.Tick(dt, true, true, true, false, 100);
            Near(s.Amount, 100, "Invalid time changed reserve");
            s.Tick(60, true, true, true, false, 100);
            Near(s.Amount, 100 - 100f / 7.5f * .05f, "Stall caused unbounded drain");
            float before = s.Amount;
            s.Tick(60, false, true, false, false, 0);
            Near(s.Amount, before, "Stall bypassed recovery delay");
            Near(HotelStamina.ClampDelta(60), .05f, "Motor timestep cap");
        }

        static void Sessions()
        {
            var s = new HotelStamina(); s.ObserveSession("hotel-a", 1); RunFor(s, 2, .01f);
            float before = s.Amount;
            s.ObserveSession("hotel-a", 1); s.StopSprint(); s.ObserveSession("hotel-a", 1);
            Near(s.Amount, before, "Repeated snapshots/panel close reset stamina");
            s.ObserveSession("hotel-a", 2); Near(s.Amount, 100, "New day did not reset");
            Deplete(s); s.ObserveSession("hotel-b", 2);
            Near(s.Amount, 100, "New world did not reset"); Require(!s.Exhausted, "New world retained exhaustion");
            RunFor(s, 2, .01f); s.Reset(); s.ObserveSession("hotel-b", 2);
            Near(s.Amount, 100, "Reconnect/session reset");
            RunFor(s, .1f, .01f); Require(s.IsSprinting, "Reset retained delay/exhaustion");
        }

        static void FramePartitions()
        {
            float? expected = null;
            foreach (float dt in new[] { .01f, .02f, .05f })
            {
                var s = new HotelStamina(); RunFor(s, 4, dt); Rest(s, .9f, dt);
                Near(s.Amount, 100 - 100f / 7.5f * 4 + 5, "Partitioned recovery");
                if (expected.HasValue) Near(s.Amount, expected.Value, "Frame rate changed resource");
                expected = s.Amount;
            }
        }

        static float Deplete(HotelStamina s)
        {
            int steps = 0;
            while (!s.Exhausted && steps < 800) { s.Tick(.01f, true, true, true, false, 5.1f * .01f); steps++; }
            Require(s.Exhausted, "Sprint never depleted");
            return steps * .01f;
        }
        static void RunFor(HotelStamina s, float seconds, float dt)
        {
            int steps = (int)Math.Round(seconds / dt);
            for (int i = 0; i < steps; i++) s.Tick(dt, true, true, true, false, 5.1f * dt);
        }
        static void Rest(HotelStamina s, float seconds, float dt)
        {
            int steps = (int)Math.Round(seconds / dt);
            for (int i = 0; i < steps; i++) s.Tick(dt, true, true, false, false, 0);
        }
        static void Near(float actual, float expected, string message)
        {
            Require(!float.IsNaN(actual) && !float.IsInfinity(actual) && Math.Abs(actual - expected) < .01f,
                message + ": expected " + expected + ", actual " + actual);
        }
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        static void Run(List<string> passed, string name, Action test)
        {
            try { test(); passed.Add(name); }
            catch (Exception e) { throw new Exception(name + ": " + e.Message, e); }
        }
    }
}
