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
        // Parent dispatch: scenario == "tablet-ui" -> ReviewTabletUI(), then existing Finish().
        // Keyboard events travel through ordinary HotelGame.Update. Poses, pickups, hint/world resets
        // and incident timing below are explicitly labelled fixtures, never claimed as user input.
        // Render evidence is read after a real Repaint, never SendMessage("OnGUI").
        IEnumerator ReviewTabletUI()
        {
            var args = Environment.GetCommandLineArgs();
            string testSave = Path.GetFullPath(Path.Combine(directory,
                "smoke-save-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".json"));
            if (scenario != "tablet-ui" || !game.Automated || Array.IndexOf(args, "-whe-session-tests") < 0 ||
                !string.Equals(Path.GetFullPath(game.Session.SavePath), testSave, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Tablet fixture requires the explicit session-test flag and per-process smoke save");
            if (!Check(game.Session.IsHost && !game.Session.UseLegacyFixture && !game.Session.UseMvpFixture &&
                HotelDangerRules.Enabled(game.Session.State) && Keyboard.current != null && game.UI.TeachingHints != null,
                "Tablet UI requires production v3 host, InputSystem keyboard and TeachingHints")) yield break;
            var state = game.Session.State;
            if (!Check(state.day == 1 && state.phase == "preparation" && state.guidedOpening &&
                !state.tutorialSkipped && game.Held == null, "Tablet UI expects a fresh guided production preparation")) yield break;
            if (!MvpInteractionFixture("TABLET_VALIDATE_INITIAL", () => HotelSaveStore.Validate(state))) yield break;
            checks.Add("TABLET_PRODUCTION_V3_WITH_PROCESS_ISOLATED_SAVE");

            var keyboard = Keyboard.current;
            bool keyboardEnabled = keyboard.enabled;
            var background = InputSystem.settings.backgroundBehavior;
            var hints = game.UI.TeachingHints;
            bool hintsEnabled = hints.Enabled;
            try
            {
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.EnableDevice(keyboard);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                if (!MvpInteractionFixture("TABLET_INITIAL_POSE", () => MvpInteractionPose(HotelLayout.Spawn))) yield break;
                yield return TabletResolution(1280);
                if (finished) yield break;
                game.LookAtForTest(game.View.transform.position + Vector3.forward * 6 + Vector3.up * .1f);

                // Reset the scheduler for repeatable input evidence. The UI may already have
                // consumed welcome; its current contextual lesson is equally valid here.
                int tutorialFlags = state.tutorialFlags, guidedStage = state.guidedStage;
                bool tutorialSkipped = state.tutorialSkipped, guidedOpening = state.guidedOpening;
                hints.Enabled = true;
                hints.Reset(Time.unscaledTime);
                checks.Add("FIXTURE_TABLET_RESET_LOCAL_HINT_FOR_H_INPUT");
                yield return TabletWaitForHint();
                if (finished) yield break;
                yield return TabletCapture("hint-visible", "", true, false);
                if (finished) yield break;
                yield return JournalKey(keyboard, Key.H, "", "H_DISMISSES_TABLET_HINT");
                if (finished) yield break;
                if (!Check(!hints.Visible && hints.Enabled && !game.UI.TeachingHintDrawnForTest,
                    "Real H did not hide the current hint, or disabled all future hints")) yield break;
                yield return TabletCapture("hint-dismissed", "", false, false);
                if (finished) yield break;
                if (!Check(state.tutorialFlags == tutorialFlags && state.guidedStage == guidedStage &&
                    state.tutorialSkipped == tutorialSkipped && state.guidedOpening == guidedOpening,
                    "Local H dismissal changed shared tutorial or learning pace")) yield break;
                checks.Add("TABLET_REAL_H_HIDES_HINT_WITHOUT_CHANGING_SHARED_TUTORIAL_OR_PACE");

                hints.Reset(Time.unscaledTime);
                checks.Add("FIXTURE_TABLET_RESET_LOCAL_HINT_FOR_MENU_SUPPRESSION");
                yield return TabletWaitForHint();
                if (finished) yield break;
                yield return JournalKey(keyboard, Key.Tab, "tasks", "TAB_OPENS_TABLET_AND_HIDES_HINT");
                if (finished) yield break;
                if (!Check(!hints.Visible, "Scheduler remained visible while tablet owns input")) yield break;
                yield return TabletCapture("tasks-from-hint", "tasks", false, true);
                if (finished) yield break;
                yield return JournalKey(keyboard, Key.Escape, "", "ESC_CLOSES_TABLET");
                if (finished) yield break;
                if (!Check(!hints.Visible && !game.UI.TeachingHintDrawnForTest,
                    "Closing tablet resurrected its interrupted hint")) yield break;
                checks.Add("TABLET_MENU_SUPPRESSES_HINT_WITHOUT_REPLAY");

                // Keep whitelist captures deterministic; the pure suite covers cooldown/repetition.
                hints.Enabled = false;
                checks.Add("FIXTURE_TABLET_LOCAL_HINTS_DISABLED_FOR_WHITELIST_CAPTURES");
                // Session reset is covered by the pure scheduler suite. Never change the durable
                // world identity here: this player must still save to its original isolated slot.
                foreach (int width in new[] { 1280, 960, 1920 })
                {
                    yield return TabletResolution(width);
                    if (finished) yield break;
                    if (!MvpInteractionTool("")) yield break;
                    if (!MvpInteractionFixture("TABLET_IDLE_POSE_" + width, () => MvpInteractionPose(HotelLayout.Spawn))) yield break;
                    yield return new WaitForSecondsRealtime(.15f);
                    game.LookAtForTest(game.View.transform.position + Vector3.forward * 6 + Vector3.up * .1f);
                    yield return null; yield return null;
                    if (!Check(game.FocusId == "" && game.FocusLabel == "" && game.Held == null &&
                        HotelHudModel.Alert(state, game.Session.LocalId, false).kind == "", "Idle fixture retained focus, held item or alert")) yield break;
                    // A live notification and offscreen ping must not reintroduce toast/marker HUD.
                    if (!MvpInteractionFixture("TABLET_PENDING_NOTICE_AND_OFFSCREEN_PING_" + width, () =>
                    {
                        FixtureAt("bed_102"); FixtureCommand("ping", "bed_102");
                        MvpInteractionPose(HotelLayout.Spawn);
                        game.LookAtForTest(game.View.transform.position + Vector3.forward * 6 + Vector3.up * .1f);
                        game.Notify("TABLET TEST: notification belongs in the staff tablet.");
                    })) yield break;
                    yield return TabletCapture("idle", "", false, false);
                    if (finished) yield break;

                    yield return JournalKey(keyboard, Key.Tab, "tasks", "TAB_OPENS_TABLET_" + width);
                    if (finished) yield break;
                    yield return TabletCapture("tasks", "tasks", false, true);
                    if (finished) yield break;
                    yield return JournalKey(keyboard, Key.Tab, "", "TAB_CLOSES_TABLET_" + width);
                    if (finished) yield break;

                    if (!MvpInteractionFixture("TABLET_FOCUS_POSE_" + width,
                        () => MvpInteractionPose(new Vector3(4.4f, .1f, 5.5f)))) yield break;
                    yield return new WaitForSecondsRealtime(.15f);
                    game.LookAtForTest(HotelLayout.RoomTarget("bed", 102));
                    yield return null; yield return null;
                    if (!Check(game.FocusId == "bed_102" && game.FocusLabel != "", "Focus fixture missed the actual bed ray")) yield break;
                    yield return TabletCapture("focus", "", false, false);
                    if (finished) yield break;

                    if (!MvpInteractionTool("toolbox")) yield break;
                    if (!MvpInteractionFixture("TABLET_HELD_POSE_" + width, () => MvpInteractionPose(HotelLayout.Spawn))) yield break;
                    yield return new WaitForSecondsRealtime(.15f);
                    game.LookAtForTest(game.View.transform.position + Vector3.forward * 6 + Vector3.up * .1f);
                    yield return null; yield return null;
                    if (!Check(game.Held?.kind == "toolbox" && game.FocusId == "", "Held fixture lost its toolbox or retained focus")) yield break;
                    yield return TabletCapture("held", "", false, false);
                    if (finished) yield break;
                }
                checks.Add("TABLET_IDLE_FOCUS_HELD_WHITELIST_ALL_RESOLUTIONS");

                yield return JournalKey(keyboard, Key.Escape, "pause", "ESC_OPENS_HARDWARE_MENU");
                if (finished) yield break;
                yield return TabletCapture("pause", "pause", false, true);
                if (finished) yield break;
                yield return JournalKey(keyboard, Key.Tab, "tasks", "TAB_FROM_MENU_OPENS_TABLET");
                if (finished) yield break;
                yield return JournalKey(keyboard, Key.Escape, "", "ESC_RETURNS_FROM_TABLET_TO_GAMEPLAY");
                if (finished) yield break;

                if (!MvpInteractionTool("")) yield break;
                if (!MvpInteractionFixture("TABLET_SCHEDULE_REAL_DANGER", () =>
                {
                    FixtureAt("desk"); FixtureCommand("open"); FixtureCommand("endGuidedOpening");
                    foreach (var candidate in state.danger.incidents) candidate.triggerAt = 10000;
                    if (state.danger.incidents.Count == 0) throw new Exception("Production contract has no incident");
                    state.danger.incidents[0].triggerAt = state.danger.elapsed;
                    state.danger.graceUntil = state.danger.elapsed;
                    MvpInteractionPose(HotelLayout.Spawn);
                    HotelSaveStore.Validate(state);
                })) yield break;
                var incident = state.danger.incidents[0];
                float until = Time.realtimeSinceStartup + 4;
                while (incident.status == "planned" && Time.realtimeSinceStartup < until) yield return null;
                if (!Check(incident.status == "warning", "Scheduled tablet danger never reached warning")) yield break;
                if (!MvpInteractionFixture("TABLET_SHORTEN_WARNING_ONLY", () => incident.warningRemaining = .01f)) yield break;
                until = Time.realtimeSinceStartup + 4;
                while (incident.status != "active" && Time.realtimeSinceStartup < until) yield return null;
                if (!Check(incident.status == "active", "Warning did not enter actual active danger")) yield break;
                foreach (int width in new[] { 1280, 960, 1920 })
                {
                    yield return TabletResolution(width);
                    if (finished) yield break;
                    if (!Check(HotelHudModel.Alert(state, game.Session.LocalId, false).kind == "incident",
                        "Danger capture has no actionable incident")) yield break;
                    yield return TabletCapture("danger", "", false, false);
                    if (finished) yield break;
                    yield return JournalKey(keyboard, Key.Tab, "tasks", "TAB_OPENS_ACTIONABLE_DANGER_" + width);
                    if (finished) yield break;
                    yield return TabletCapture("danger-tasks", "tasks", false, true, true);
                    if (finished) yield break;
                    yield return JournalKey(keyboard, Key.Escape, "", "ESC_CLOSES_ACTIONABLE_DANGER_" + width);
                    if (finished) yield break;
                    if (!Check(string.IsNullOrEmpty(game.UI.AlertInTabletForTest),
                        "Tablet alert observer was not cleared on gameplay Repaint")) yield break;
                }
                checks.Add("TABLET_DANGER_HUD_QUIET_ALERT_RENDERED_IN_TABLET_ALL_RESOLUTIONS");

                // Real disk lock: a current hazard must not mask persistent save-failure feedback.
                game.OpenPanel("pause");
                try {
                    using(var saveLock=new FileStream(game.Session.SavePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
                        expectedIo=true;
                        if(!Check(!game.Session.Save()&&!string.IsNullOrEmpty(game.Session.SaveError),"Locked tablet save did not fail explicitly"))yield break;
                        yield return TabletCapture("save-error","pause",false,true,true);
                        if(finished)yield break;
                        if(!Check(game.UI.SaveErrorInTabletForTest==game.Session.SaveError,"Active danger masked tablet save failure"))yield break;
                    }
                } finally {expectedIo=false;}
                if(!Check(game.Session.Save()&&game.Session.SaveError=="","Tablet save could not recover after unlocking"))yield break;
                checks.Add("TABLET_PERSISTENT_SAVE_ERROR_REMAINS_WITH_ACTIVE_ALERT_AND_RECOVERS");
                game.OpenPanel("");

                yield return TabletResolution(960);
                if (finished) yield break;
                if (!Check(game.Panel == "", "Summary test did not start in gameplay")) yield break;
                // Trigger an actual terminal transition on the normal simulation clock. Do not
                // call OpenPanel after this fixture: that would conceal a summary-auto-popup bug.
                if (!MvpInteractionFixture("TABLET_SHORTEN_CRITICAL_WINDOW", () =>
                {
                    state.danger.safety = 0;
                    state.danger.criticalRemaining = .15f;
                })) yield break;
                until = Time.realtimeSinceStartup + 5;
                while (state.phase != "summary" && Time.realtimeSinceStartup < until) yield return null;
                if (!Check(state.phase == "summary" && state.danger.settled, "Live danger did not settle the shift")) yield break;
                yield return new WaitForSecondsRealtime(.3f);
                yield return TabletCapture("summary-no-popup", "", false, false);
                if (finished) yield break;
                checks.Add("TABLET_SUMMARY_NEVER_AUTO_OPENS");
                yield return JournalKey(keyboard, Key.Tab, "summary", "TAB_EXPLICITLY_OPENS_SHIFT_SUMMARY");
                if (finished) yield break;
                yield return TabletCapture("summary", "summary", false, true);
                if (finished) yield break;
                yield return JournalKey(keyboard, Key.Escape, "", "ESC_CLOSES_SHIFT_SUMMARY");
                if (finished) yield break;

                if (!MvpInteractionFixture("TABLET_VALIDATE_FINAL", () => HotelSaveStore.Validate(state))) yield break;
                if (!Check(game.Session.Save(), "Tablet final process-isolated save failed")) yield break;
                checks.Add("TABLET_FINAL_ISOLATED_SAVE_SUCCEEDED");
                if (!Check(errors.Count == 0, "Tablet UI emitted Unity/IMGUI errors")) yield break;
                checks.Add("TABLET_REAL_TAB_ESCAPE_NAVIGATION");
                checks.Add("TABLET_1280x800_960x600_1920x1080_CAPTURED");
                checks.Add("TABLET_NO_UNITY_OR_IMGUI_ERRORS");
                checks.Add("LIMITATION: InputSystem 1.17 cannot generate IMGUI input; native mouse clicks are a separate parent-run check.");
                checks.Add("LIMITATION: observers certify submitted render content, not pixel clipping, contrast or absence of uninstrumented draws; review captures.");
                checks.Add("LIMITATION: fixtures do not prove notification-history navigation, settings clicks, host-forfeit/recovery buttons or network-client UI; retain their existing checks.");
            }
            finally
            {
                hints.Enabled = hintsEnabled;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                if (!keyboardEnabled) InputSystem.DisableDevice(keyboard);
                InputSystem.settings.backgroundBehavior = background;
            }
        }

        IEnumerator TabletWaitForHint()
        {
            float until = Time.realtimeSinceStartup + HotelTeachingHints.InitialDelay + 4;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                yield return new WaitForEndOfFrame();
                if (game.UI.TeachingHints.Visible && game.UI.TeachingHintDrawnForTest) break;
            }
            var hints = game.UI.TeachingHints;
            if (!Check(hints.Visible && !string.IsNullOrEmpty(hints.CurrentKey) &&
                !string.IsNullOrEmpty(hints.Title) && !string.IsNullOrEmpty(hints.Body) &&
                game.UI.TeachingHintDrawnForTest, "Scheduler did not draw a current hint with a complete payload")) yield break;
            checks.Add("TABLET_RENDERED_HINT key=" + hints.CurrentKey + " title=" + hints.Title);
        }

        IEnumerator TabletResolution(int width)
        {
            int height = width == 1920 ? 1080 : width == 1280 ? 800 : 600;
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.7f);
            if (!Check(Screen.width == width && Screen.height == height,
                "Tablet requested " + width + "x" + height + ", got " + Screen.width + "x" + Screen.height)) yield break;
        }

        IEnumerator TabletCapture(string label, string expectedPanel, bool hint, bool tablet, bool alert = false)
        {
            // Never changes the panel or scheduler: stale flags and automatic panel changes must fail.
            yield return null;
            yield return new WaitForEndOfFrame();
            if (!Check(game.Panel == expectedPanel && game.UI.TabletDrawnForTest == tablet &&
                game.UI.TeachingHintDrawnForTest == hint && game.UI.TeachingHints.Visible == hint,
                "Tablet render mismatch at " + label + ": panel=" + game.Panel + ", tablet=" +
                game.UI.TabletDrawnForTest + ", hint=" + game.UI.TeachingHintDrawnForTest)) yield break;
            if (expectedPanel == "" && !Check(game.UI.HudElementCountForTest == 4,
                "Gameplay HUD must draw exactly shift/time/health/stamina at " + label +
                "; observed " + game.UI.HudElementCountForTest)) yield break;
            string renderedAlert = game.UI.AlertInTabletForTest ?? "";
            if (alert)
            {
                var expectedAlert = HotelHudModel.Alert(game.Session.State, game.Session.LocalId, true);
                // The Repaint observer exposes the kind of the submitted current alert.
                if (!Check(expectedAlert.kind != "" && renderedAlert == expectedAlert.kind,
                    "Tablet header did not submit its current actionable alert at " + label)) yield break;
            }
            else if (!Check(renderedAlert == "", "Unexpected/stale tablet alert observer at " + label)) yield break;

            var capture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                if (!Check(capture != null && capture.width == Screen.width && capture.height == Screen.height,
                    "Tablet screenshot has unexpected dimensions at " + label)) yield break;
                int lit = 0;
                var pixels = capture.GetPixels32();
                for (int i = 0; i < pixels.Length; i += 64)
                    if (pixels[i].r + pixels[i].g + pixels[i].b > 70) lit++;
                if (!Check(lit > 100, "Blank tablet screenshot at " + label)) yield break;
                string file = "ui-" + Screen.width + "-tablet-" + label + ".png";
                File.WriteAllBytes(Path.Combine(directory, file), capture.EncodeToPNG());
                checks.Add("TABLET_CAPTURE " + file + " panel=" + (expectedPanel == "" ? "gameplay" : expectedPanel) +
                    " hud=" + game.UI.HudElementCountForTest + " hint=" + hint + " tablet=" + tablet + " alert=" + alert);
            }
            finally { if (capture != null) Destroy(capture); }
        }
    }
}
