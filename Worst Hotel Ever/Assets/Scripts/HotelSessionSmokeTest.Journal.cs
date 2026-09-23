using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class HotelSessionSmokeTest
    {
        // Opt-in standalone fixture. Only TAB/ESC below are input evidence; positioning,
        // item acquisition and CapturePanels use explicit fixtures. Never uses the OS mouse.
        IEnumerator ReviewJournalUI()
        {
            var args = Environment.GetCommandLineArgs();
            string testSave = Path.GetFullPath(Path.Combine(directory,
                "smoke-save-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".json"));
            if (scenario != "journal-ui" || !game.Automated || Array.IndexOf(args, "-whe-session-tests") < 0 ||
                !string.Equals(Path.GetFullPath(game.Session.SavePath), testSave, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Journal fixture requires the explicit session-test flag and per-process smoke save");
            if (!Check(game.Session.IsHost && !game.Session.UseLegacyFixture && !game.Session.UseMvpFixture &&
                HotelDangerRules.Enabled(game.Session.State) && Keyboard.current != null,
                "Journal UI requires the production v3 host and an InputSystem keyboard")) yield break;
            var state = game.Session.State;
            if (!Check(state.day == 1 && state.phase == "preparation" && state.guidedOpening &&
                !state.tutorialSkipped && game.Held == null, "Journal UI expects fresh guided production preparation")) yield break;
            if (!MvpInteractionFixture("JOURNAL_VALIDATE_PRODUCTION_V3", () => HotelSaveStore.Validate(state))) yield break;
            checks.Add("JOURNAL_PRODUCTION_V3_WITH_PROCESS_ISOLATED_SAVE");

            var keyboard = Keyboard.current;
            bool keyboardEnabled = keyboard.enabled;
            var background = InputSystem.settings.backgroundBehavior;
            try
            {
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.EnableDevice(keyboard);
                foreach (int width in new[] { 1280, 960 })
                {
                    if (!MvpInteractionTool("")) yield break;
                    if (!MvpInteractionFixture("JOURNAL_IDLE_POSE", () => MvpInteractionPose(HotelLayout.Spawn))) yield break;
                    yield return new WaitForSecondsRealtime(.15f);
                    // Look along the lobby at eye level, above the low reception collider.
                    game.LookAtForTest(game.View.transform.position + Vector3.forward * 6 + Vector3.up * .1f);
                    yield return null; yield return null;
                    if (!Check(game.FocusId == "" && game.FocusLabel == "" && game.Held == null &&
                        HotelHudModel.Alert(state, game.Session.LocalId, false).kind == "", "Idle capture contains focus/carry/danger")) yield break;
                    yield return CapturePanels(width, "journal-idle-", new[] { "" });
                    if (finished || errors.Count != 0) yield break;
                    if(!MvpInteractionFixture("JOURNAL_OFFSCREEN_PING_SAME_AS_LESSON",()=>{
                        FixtureAt("bed_102");FixtureCommand("ping","bed_102");MvpInteractionPose(HotelLayout.Spawn);
                    }))yield break;
                    yield return null;yield return null;
                    game.LookAtForTest(game.View.transform.position+Vector3.forward*6+Vector3.up*.1f);
                    yield return CapturePanels(width,"journal-ping-",new[]{""});
                    if(finished||errors.Count!=0)yield break;

                    // Real keyboard path through HotelGame.Update, not direct panel assignment.
                    yield return JournalKey(keyboard, Key.Tab, "tasks", "TAB_OPENS_JOURNAL_" + width);
                    if (finished) yield break;
                    yield return JournalTaskTutorialBody(width, "empty-hands");
                    if (finished) yield break;
                    yield return JournalKey(keyboard, Key.Tab, "", "TAB_CLOSES_JOURNAL_" + width);
                    if (finished) yield break;
                    yield return JournalKey(keyboard, Key.Escape, "pause", "ESC_OPENS_MENU_" + width);
                    if (finished) yield break;
                    yield return JournalKey(keyboard, Key.Tab, "tasks", "TAB_FROM_MENU_OPENS_JOURNAL_" + width);
                    if (finished) yield break;
                    yield return JournalKey(keyboard, Key.Escape, "", "ESC_CLOSES_JOURNAL_" + width);
                    if (finished) yield break;
                    yield return JournalKey(keyboard, Key.Escape, "pause", "ESC_REOPENS_MENU_" + width);
                    if (finished) yield break;
                    yield return JournalKey(keyboard, Key.Escape, "", "ESC_CLOSES_MENU_" + width);
                    if (finished) yield break;

                    yield return CapturePanels(width, "journal-", new[] { "tasks" });
                    if (finished || errors.Count != 0) yield break;
                    yield return CapturePanels(width, "journal-", new[] { "briefing" });
                    if (finished || errors.Count != 0 || !JournalBriefingEvidence(width, "empty-hands")) yield break;
                    yield return JournalKey(keyboard, Key.Tab, "", "TAB_CLOSES_HELP_PAGE_" + width);
                    if (finished) yield break;
                    yield return CapturePanels(width, "journal-", new[] { "management", "pause", "settings" });
                    if (finished || errors.Count != 0) yield break;

                    if (!MvpInteractionFixture("JOURNAL_BED_APPROACH", () => MvpInteractionPose(new Vector3(4.4f, .1f, 5.5f)))) yield break;
                    yield return new WaitForSecondsRealtime(.15f);
                    game.LookAtForTest(HotelLayout.RoomTarget("bed", 102));
                    yield return null; yield return null;
                    if (!Check(game.FocusId == "bed_102" && game.FocusLabel != "", "Journal focus fixture missed the physical bed ray")) yield break;
                    yield return CapturePanels(width, "journal-focus-", new[] { "" });
                    if (finished || errors.Count != 0) yield break;

                    if (!MvpInteractionTool("toolbox")) yield break;
                    if (!MvpInteractionFixture("JOURNAL_CARRY_POSE", () => MvpInteractionPose(HotelLayout.Spawn))) yield break;
                    yield return new WaitForSecondsRealtime(.15f);
                    game.LookAtForTest(game.View.transform.position + Vector3.forward * 6 + Vector3.up * .1f);
                    yield return null; yield return null;
                    if (!Check(game.Held?.kind == "toolbox" && game.FocusId == "", "Journal carry fixture has no toolbox or retained focus")) yield break;
                    yield return CapturePanels(width, "journal-held-", new[] { "" });
                    if (finished || errors.Count != 0) yield break;
                    // Capture the changed tutorial in Help; the Tasks explanation is folded by default.
                    game.OpenPanel("tasks");
                    checks.Add("FIXTURE_JOURNAL_HELD_TASKS_PANEL_" + width);
                    yield return JournalTaskTutorialBody(width, "toolbox");
                    if (finished) yield break;
                    yield return CapturePanels(width, "journal-held-", new[] { "tasks", "briefing" });
                    if (finished || errors.Count != 0 || !JournalBriefingEvidence(width, "toolbox")) yield break;
                }
                if (!MvpInteractionTool("")) yield break;
                Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(.7f);
                foreach(string panel in new[]{"","briefing"}) {
                    game.OpenPanel(panel);
                    yield return new WaitForSecondsRealtime(.3f);
                    yield return new WaitForEndOfFrame();
                    var capture=ScreenCapture.CaptureScreenshotAsTexture();
                    try {
                        if(!Check(capture.width==1920&&capture.height==1080,"Wide UI capture has unexpected dimensions"))yield break;
                        File.WriteAllBytes(Path.Combine(directory,"ui-1920-journal-"+(panel==""?"hud":panel)+".png"),capture.EncodeToPNG());
                    } finally { Destroy(capture); }
                }
                checks.Add("JOURNAL_1920x1080_HUD_AND_HELP_CAPTURED");
                if (!MvpInteractionFixture("JOURNAL_VALIDATE_FINAL", () => HotelSaveStore.Validate(state))) yield break;
                if (!Check(errors.Count == 0, "Journal emitted Unity/IMGUI errors")) yield break;
                checks.Add("JOURNAL_1280x800_960x600_IDLE_FOCUS_HELD_HELP_TASKS_MANAGEMENT_MENU_SETTINGS_CAPTURED");
                checks.Add("JOURNAL_NO_UNITY_OR_IMGUI_ERRORS");
                checks.Add("LIMITATION: fixture poses/pickups, reflected fold state and panel captures are not mouse navigation or a human usability test");
                checks.Add("LIMITATION: render observer verifies submitted full text; pixel visibility, clipping and contrast still require screenshot review");
            }
            finally
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                if (!keyboardEnabled) InputSystem.DisableDevice(keyboard);
                InputSystem.settings.backgroundBehavior = background;
            }
        }

        IEnumerator JournalKey(Keyboard keyboard, Key key, string expectedPanel, string label)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null; yield return null;
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                yield return null; yield return null;
            }
            finally { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); }
            yield return null; yield return null;
            yield return new WaitForEndOfFrame();
            if (!Check(game.Panel == expectedPanel && game.InputActive == (expectedPanel == "") &&
                Cursor.visible && Cursor.lockState == CursorLockMode.None,
                label + ": panel=" + game.Panel + ", input=" + game.InputActive + "; automated cursor must remain unlocked")) yield break;
            checks.Add("REAL_INPUTSYSTEM_" + label);
        }

        IEnumerator JournalTaskTutorialBody(int width, string fixture)
        {
            // Wait for a fresh Layout/Repaint before reading the actual-draw observer.
            yield return null;
            yield return new WaitForEndOfFrame();
            if (!Check(game.Panel == "tasks" && game.UI.JournalTutorialBodyForTest == "",
                "Default Tasks fold emitted tutorial body before expansion")) yield break;
            checks.Add("JOURNAL_TASKS_BODY_HIDDEN_BY_DEFAULT_" + width + "_" + fixture);
            var field = typeof(HotelUI).GetField("folds", BindingFlags.Instance | BindingFlags.NonPublic);
            var folds = field?.GetValue(game.UI) as Dictionary<string, bool>;
            if (!Check(folds != null, "Journal reflection fixture cannot find HotelUI.folds")) yield break;
            bool hadValue = folds.TryGetValue("current-lesson", out bool previous);
            if (!Check(!previous, "Current lesson fold was not closed at fixture entry")) yield break;
            try
            {
                // Fixture state only: this does not exercise the fold button or a mouse click.
                folds["current-lesson"] = true;
                checks.Add("FIXTURE_JOURNAL_REFLECTION_OPEN_CURRENT_LESSON_" + width + "_" + fixture);
                yield return null;
                yield return new WaitForEndOfFrame();
                if (!JournalTutorialBodyEvidence("tasks", width, fixture)) yield break;
            }
            finally
            {
                if (hadValue) folds["current-lesson"] = previous;
                else folds.Remove("current-lesson");
                checks.Add("FIXTURE_JOURNAL_REFLECTION_RESET_CURRENT_LESSON_" + width + "_" + fixture);
            }
            yield return null;
            yield return new WaitForEndOfFrame();
            if (!Check(game.Panel == "tasks" && game.UI.JournalTutorialBodyForTest == "",
                "Closing Tasks fold retained stale tutorial body in the repaint observer")) yield break;
            checks.Add("JOURNAL_TASKS_FOLD_RESET_BEFORE_DEFAULT_CAPTURE_" + width + "_" + fixture);
        }

        bool JournalBriefingEvidence(int width, string fixture)
        {
            return JournalTutorialBodyEvidence("briefing", width, fixture);
        }

        bool JournalTutorialBodyEvidence(string panel, int width, string fixture)
        {
            var hint = HotelOnboarding.GetHint(game.Session.State, game.Session.LocalId);
            if (!Check(game.Panel == panel && hint != null && !string.IsNullOrEmpty(hint.body) &&
                game.UI.JournalTutorialBodyForTest == hint.body,
                "Journal " + panel + " did not draw the complete currently selected tutorial body")) return false;
            checks.Add("JOURNAL_FULL_TUTORIAL_BODY_DRAWN_" + panel.ToUpperInvariant() + "_" + width + "_" + fixture);
            checks.Add("EXPECTED_TUTORIAL_TITLE " + width + " " + fixture + ": " + hint.title);
            checks.Add("EXPECTED_TUTORIAL_BODY " + width + " " + fixture + ": " + hint.body);
            return true;
        }
    }
}
