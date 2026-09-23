using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class HotelSessionSmokeTest
    {
        // Standalone production-v3 host. Only approach/cart/life setup uses fixtures;
        // measured movement, depletion and recovery use real Update/InputSystem frames.
        IEnumerator ReviewStamina()
        {
            var args = Environment.GetCommandLineArgs();
            string isolatedSave = Path.GetFullPath(Path.Combine(directory,
                "smoke-save-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".json"));
            if (scenario != "stamina" || !game.Automated || Array.IndexOf(args, "-whe-session-tests") < 0 ||
                !string.Equals(Path.GetFullPath(game.Session.SavePath), isolatedSave, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Stamina fixture requires scenario stamina and its process-isolated session-test save");
            if (!Check(game.Session.IsHost && HotelDangerRules.Enabled(game.Session.State) &&
                !game.Session.UseLegacyFixture && !game.Session.UseMvpFixture && game.Controller != null && Keyboard.current != null,
                "Stamina requires a production v3 host, controller and keyboard")) yield break;
            var state = game.Session.State;
            if (!Check(state.day == 1 && state.phase == "preparation" && state.guidedOpening && game.Held == null,
                "Stamina requires fresh guided preparation")) yield break;
            if (!MvpInteractionFixture("STAMINA_VALIDATE_INITIAL", () => HotelSaveStore.Validate(state))) yield break;
            var keyboard = Keyboard.current;
            bool keyboardEnabled = keyboard.enabled;
            var background = InputSystem.settings.backgroundBehavior;
            try
            {
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.EnableDevice(keyboard);
                game.SetInputFocusForTest(true);
                if (!StaminaApproach(4)) yield break;
                yield return new WaitForSecondsRealtime(.15f);
                var idle = new StaminaMotion();
                yield return StaminaHold(.3f, idle, Key.LeftShift);
                if (!Check(idle.distance < .02f && game.Stamina.Amount > 99.99f && !game.Stamina.IsSprinting,
                    "Idle Shift moved or drained")) yield break;

                var walk = new StaminaMotion();
                yield return StaminaHold(.7f, walk, Key.W);
                if (!StaminaSpeed(walk, 3.7f, "REAL_W_WALK")) yield break;
                if (!Check(game.Stamina.Amount > 99.99f, "Walking drained stamina")) yield break;
                var sprint = new StaminaMotion();
                yield return StaminaHold(.7f, sprint, Key.W, Key.LeftShift);
                if (!StaminaSpeed(sprint, 5.1f, "REAL_SHIFT_W_SPRINT")) yield break;
                float expectedReserve = 100 - sprint.distance / HotelStamina.SprintSpeed * (100 / HotelStamina.SprintSeconds);
                if (!Check(sprint.sawSprint && game.Stamina.Amount < 100 && Math.Abs(game.Stamina.Amount - expectedReserve) < .5f,
                    "Real sprint did not drain the resource")) yield break;
                checks.Add("STAMINA_REAL_IDLE_WALK_SPRINT_SPEEDS_AND_DRAIN");

                // Held movement remains injected while logical focus is false.
                // This covers our input gate, not Windows foreground switching.
                game.SetInputFocusForTest(false);
                float before = game.Stamina.Amount;
                var unfocused = new StaminaMotion();
                yield return StaminaHold(1.1f, unfocused, Key.W, Key.LeftShift);
                if (!Check(!game.InputActive && unfocused.distance < .02f && !unfocused.sawSprint &&
                    game.Stamina.Amount > before && game.Stamina.Amount <= 100, "Lost focus moved, drained or froze recovery")) yield break;
                game.SetInputFocusForTest(true);
                yield return StaminaRestToFull(); if (finished) yield break;
                checks.Add("STAMINA_LOGICAL_FOCUS_GATE_STOPS_MOTION_AND_RECOVERS");

                if (!StaminaApproach(4)) yield break;
                yield return new WaitForSecondsRealtime(.15f);
                yield return StaminaRunToExhaustion(keyboard); if (finished) yield break;
                var exhaustedWalk = new StaminaMotion();
                yield return StaminaHold(.3f, exhaustedWalk, Key.W, Key.LeftShift);
                if (!StaminaSpeed(exhaustedWalk, 3.7f, "REAL_EXHAUSTED_SHIFT_W_WALKS")) yield break;
                if (!Check(game.Stamina.Exhausted && game.Stamina.Amount < 20 && !exhaustedWalk.sawSprint,
                    "Holding Shift restarted immediately at zero")) yield break;

                // Open and close through the actual Tab path; do not rely on page names.
                yield return StaminaHold(.06f, new StaminaMotion(), Key.Tab, Key.W, Key.LeftShift);
                if (!Check(game.Panel != "" && !game.InputActive && game.Stamina.Amount < 40 && !game.Stamina.IsSprinting,
                    "Tab did not stop sprint or instantly refilled reserve")) yield break;
                before = game.Stamina.Amount;
                var tablet = new StaminaMotion();
                yield return StaminaHold(.2f, tablet, Key.W, Key.LeftShift);
                if (!Check(tablet.distance < .02f && !tablet.sawSprint && game.Stamina.Amount >= before && game.Stamina.Amount < 40,
                    "Tablet did not block movement or reset reserve")) yield break;
                yield return StaminaHold(.06f, new StaminaMotion(), Key.Tab);
                if (!Check(game.Panel == "" && game.InputActive && game.Stamina.Amount < 40,
                    "Tab close refilled reserve or failed to return input")) yield break;
                yield return StaminaResumeAtThreshold(keyboard); if (finished) yield break;
                checks.Add("STAMINA_TAB_RECOVERY_WITHOUT_RESET_AND_TWENTY_POINT_RESTART");

                // Escape has the same movement gate and continues gradual recovery.
                yield return StaminaHold(.06f, new StaminaMotion(), Key.Escape);
                before = game.Stamina.Amount;
                var menu = new StaminaMotion();
                yield return StaminaHold(1.1f, menu, Key.W, Key.LeftShift);
                if (!Check(game.Panel != "" && !game.InputActive && menu.distance < .02f && !menu.sawSprint &&
                    game.Stamina.Amount > before && game.Stamina.Amount < 100, "Menu blocked recovery or allowed sprint")) yield break;
                yield return StaminaHold(.06f, new StaminaMotion(), Key.Escape);
                if (!Check(game.Panel == "", "Escape did not close menu")) yield break;
                yield return StaminaRestToFull(); if (finished) yield break;

                // The corridor's north wall is real production geometry.
                if (!StaminaApproach(22.3f)) yield break;
                yield return new WaitForSecondsRealtime(.15f);
                yield return StaminaHold(.5f, new StaminaMotion(), Key.W, Key.LeftShift);
                before = game.Stamina.Amount;
                var wall = new StaminaMotion();
                yield return StaminaHold(.4f, wall, Key.W, Key.LeftShift);
                if (!Check(wall.distance < .025f && !wall.sawSprint && game.Stamina.Amount >= before - .1f,
                    "Pushing the wall drains stamina without movement")) yield break;
                checks.Add("STAMINA_REAL_WALL_BLOCK_HAS_NO_SPRINT_DRAIN");

                if (!MvpInteractionFixture("STAMINA_CART_PURCHASE_AND_PICKUP", () =>
                {
                    state.cash = Math.Max(state.cash, 3000);
                    FixtureAt("board"); FixtureCommand("upgrade", "cart"); FixturePick("cart");
                    HotelSaveStore.Validate(state);
                })) yield break;
                if (!StaminaApproach(4)) yield break;
                yield return new WaitForSecondsRealtime(.15f);
                before = game.Stamina.Amount;
                var cart = new StaminaMotion();
                yield return StaminaHold(.7f, cart, Key.W, Key.LeftShift);
                if (!StaminaSpeed(cart, 2.6f, "REAL_CART_SHIFT_W")) yield break;
                if (!Check(game.Held?.kind == "cart" && !cart.sawSprint && game.Stamina.Amount >= before - .01f,
                    "Cart Shift drains reserve")) yield break;
                if (!MvpInteractionFixture("STAMINA_CART_DROP", () => FixtureDrop())) yield break;
                if (!MvpInteractionFixture("STAMINA_GUIDED_OPEN_FOR_LIFE_FIXTURES", () =>
                {
                    FixtureAt("desk"); FixtureCommand("open");
                    if (!HotelDirector.ClockHeld(state)) throw new Exception("Life fixtures require the held guided clock");
                    HotelSaveStore.Validate(state);
                })) yield break;
                if (!StaminaApproach(10)) yield break;
                yield return new WaitForSecondsRealtime(.15f);
                yield return StaminaRestToFull(); if (finished) yield break;
                yield return StaminaHold(.8f, new StaminaMotion(), Key.W, Key.LeftShift);

                // Life is a setup fixture in the guided shift; measured frames still use Update.
                // Existing danger suites own real damage/rescue and durable network life tests.
                var crew = HotelDangerRules.Crew(state, game.Session.LocalId);
                if (!MvpInteractionFixture("STAMINA_DOWNED_LIFE", () =>
                {
                    crew.health = 0; crew.life = "downed"; crew.bleedout = 30; crew.shield = 0; crew.downs++;
                    crew.position = game.LocalPlayer.position;
                    HotelSaveStore.Validate(state);
                })) yield break;
                yield return null;
                before = game.Stamina.Amount;
                var downed = new StaminaMotion();
                yield return StaminaHold(.4f, downed, Key.W, Key.LeftShift);
                if (!Check(downed.distance < .02f && !downed.sawSprint && Math.Abs(game.Stamina.Amount - before) < .01f,
                    "Downed movement/drain/recovery continued")) yield break;
                if (!MvpInteractionFixture("STAMINA_DEAD_LIFE", () =>
                {
                    crew.life = "dead"; crew.bleedout = 0; HotelSaveStore.Validate(state);
                })) yield break;
                var dead = new StaminaMotion();
                yield return StaminaHold(.4f, dead, Key.W, Key.LeftShift);
                if (!Check(dead.distance < .02f && !dead.sawSprint && Math.Abs(game.Stamina.Amount - before) < .01f,
                    "Dead movement/drain/recovery continued")) yield break;
                if (!MvpInteractionFixture("STAMINA_REVIVED_LIFE", () =>
                {
                    crew.life = "healthy"; crew.health = 60; HotelSaveStore.Validate(state);
                })) yield break;
                yield return null; yield return null;
                if (!Check(game.Stamina.Amount < 98 && game.Stamina.Amount >= before - .01f,
                    "Revival refilled or reduced stamina")) yield break;
                checks.Add("STAMINA_DOWNED_DEAD_FREEZE_AND_REVIVAL_DOES_NOT_REFILL");
                if (!MvpInteractionFixture("STAMINA_VALIDATE_FINAL", () => HotelSaveStore.Validate(state))) yield break;
                checks.Add("LIMITATION: local stamina is not replicated, durable or host-enforced; native suite covers world/day reset");
                checks.Add("LIMITATION: approach/cart/life fixtures and logical focus injection are not physical pickup, rescue or OS-focus evidence");
                checks.Add("STAMINA_REAL_INPUT_SCENARIO_COMPLETE_NO_SHARED_PREFS_WRITTEN");
            }
            finally
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                game.SetInputFocusForTest(null);
                if (!keyboardEnabled) InputSystem.DisableDevice(keyboard);
                InputSystem.settings.backgroundBehavior = background;
            }
        }

        sealed class StaminaMotion
        {
            public float distance, seconds;
            public bool sawSprint;
        }

        IEnumerator StaminaHold(float seconds, StaminaMotion sample, params Key[] keys)
        {
            var keyboard = Keyboard.current;
            Vector3 previous = game.Controller.transform.position;
            float until = Time.realtimeSinceStartup + seconds;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            try
            {
                do
                {
                    yield return null;
                    Vector3 position = game.Controller.transform.position;
                    sample.distance += StaminaHorizontalDistance(previous, position);
                    sample.seconds += HotelStamina.ClampDelta(Time.unscaledDeltaTime);
                    sample.sawSprint |= game.Stamina.IsSprinting;
                    previous = position;
                } while (Time.realtimeSinceStartup < until);
            }
            finally { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); }
            yield return null; yield return null;
        }

        IEnumerator StaminaRunToExhaustion(Keyboard keyboard)
        {
            float until = Time.realtimeSinceStartup + 25;
            float distance = 0;
            Vector3 previous = game.Controller.transform.position;
            bool forward = true;
            try
            {
                while (!game.Stamina.Exhausted && Time.realtimeSinceStartup < until)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(forward ? Key.W : Key.S, Key.LeftShift));
                    yield return null;
                    Vector3 position = game.Controller.transform.position;
                    distance += StaminaHorizontalDistance(previous, position); previous = position;
                    if (position.z > 14) forward = false;
                    else if (position.z < 4) forward = true;
                }
            }
            finally { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); }
            yield return null; yield return null;
            if (!Check(game.Stamina.Exhausted && game.Stamina.Amount < .01f && distance >= 37.5f && distance <= 39.5f,
                "Real sprint did not exhaust over approximately 38.25m; distance=" + distance + " reserve=" + game.Stamina.Amount)) yield break;
            checks.Add("STAMINA_REAL_SHIFT_MOVEMENT_DEPLETES_FULL_RESERVE");
        }

        IEnumerator StaminaResumeAtThreshold(Keyboard keyboard)
        {
            float until = Time.realtimeSinceStartup + 5;
            bool restarted = false;
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
                while (Time.realtimeSinceStartup < until)
                {
                    bool wasExhausted = game.Stamina.Exhausted;
                    float before = game.Stamina.Amount;
                    yield return null;
                    if (wasExhausted && before < 20 && game.Stamina.IsSprinting)
                    { Check(false, "Real held Shift restarted below twenty"); yield break; }
                    if (game.Stamina.IsSprinting) { restarted = before >= 20 && game.Stamina.Amount < before; break; }
                }
            }
            finally { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); }
            yield return null; yield return null;
            if (!Check(restarted, "Real held Shift never resumed at threshold")) yield break;
        }

        IEnumerator StaminaRestToFull()
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            float until = Time.realtimeSinceStartup + 15;
            while (game.Stamina.Amount < 99.99f && Time.realtimeSinceStartup < until) yield return null;
            if (!Check(game.Stamina.Amount >= 99.99f && game.Stamina.Amount <= 100 && !game.Stamina.Exhausted,
                "Passive recovery failed to reach full reserve")) yield break;
        }

        bool StaminaApproach(float z)
        {
            return MvpInteractionFixture("STAMINA_CORRIDOR_APPROACH_" + z, () =>
            {
                MvpInteractionPose(new Vector3(0, .1f, z));
                game.LookAtForTest(game.View.transform.position + Vector3.forward * 4);
            });
        }

        bool StaminaSpeed(StaminaMotion sample, float expected, string label)
        {
            float speed = sample.seconds > 0 ? sample.distance / sample.seconds : 0;
            if (!Check(sample.seconds > .1f && Math.Abs(speed - expected) < .45f,
                label + " expected " + expected + "m/s, observed " + speed)) return false;
            checks.Add("STAMINA_" + label + "_SPEED=" + speed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }

        static float StaminaHorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0; return Vector3.Distance(a, b);
        }
    }
}
