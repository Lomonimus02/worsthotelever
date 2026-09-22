using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class HotelSessionSmokeTest
    {
        // Opt-in Windows player test: setup/approach teleports are fixtures, not input evidence.
        // Every tested service/repair uses Game.Focus -> Keyboard E -> Game.Interact, with
        // ordinary Update heartbeats and wall-clock time. No synthetic simulation ticks.
        IEnumerator ReviewMvpInteraction()
        {
            float started = Time.realtimeSinceStartup;
            if (!Check(game.Session.IsHost && game.Session.State.mvp != null && Keyboard.current != null,
                "MVP interaction requires a Windows host with an InputSystem keyboard")) yield break;
            if (!MvpInteractionFixture("VALIDATE_INITIAL", () => HotelSaveStore.Validate(game.Session.State))) yield break;
            var state = game.Session.State;
            var room105 = state.rooms.Find(r => r.number == 105);
            var room106 = state.rooms.Find(r => r.number == 106);
            if (!Check(state.phase == "preparation" && state.guidedOpening && room105 != null && room106 != null &&
                !room105.mvp.owned && !room106.mvp.owned && game.Held == null,
                "MVP interaction expects a fresh guided preparation with both expansions locked")) yield break;

            var background = InputSystem.settings.backgroundBehavior;
            bool handMotion = game.HandMotion;
            try
            {
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.EnableDevice(Keyboard.current);
                game.HandMotion = true;
                game.OpenPanel("");
                MvpInteractionRelease();
                if (!MvpInteractionFixture("SETUP_CASH_AND_UPGRADE_COMMANDS_105_106", () =>
                {
                    state.cash = 6000;
                    FixtureAt("board");
                    FixtureCommand("upgrade", "room105");
                    FixtureCommand("upgrade", "room106");
                    HotelSaveStore.Validate(state);
                })) yield break;
                if (!Check(room105.mvp.owned && room106.mvp.owned, "Expansion purchase did not unlock both rooms")) yield break;
                // Start outside each doorway; W must carry the real CharacterController through it.
                yield return MvpInteractionEnterRoom(105);
                if (finished) yield break;
                yield return MvpInteractionEnterRoom(106);
                if (finished) yield break;

                MvpRequestState coffeeRequest = null;
                if (!MvpInteractionFixture("SETUP_CHECKIN_COFFEE_REQUEST_LOCAL_FAULTS_UTILITIES_DIRT_USED_TOWEL", () =>
                {
                    FixtureAt("desk");
                    FixtureCommand("open");
                    FixtureCommand("checkin", number: 105);
                    FixtureCommand("skipTutorial"); // Hide help, retain the quiet guided clock.
                    var guest = state.guests.Find(g => g.id == room105.guestId);
                    if (guest == null || guest.mvp == null) throw new Exception("No checked-in coffee recipient");
                    coffeeRequest = new MvpRequestState {
                        id = "interaction_coffee_" + state.mvp.nextId++, kind = "coffee", target = "coffee_105",
                        guestId = guest.id, createdAt = state.mvp.elapsed, dueAt = state.mvp.elapsed + 300
                    };
                    state.mvp.requests.Add(coffeeRequest);
                    foreach (string kind in new[] { "toilet", "tv", "lamp" })
                    {
                        var equipment = room106.mvp.equipment.Find(e => e.kind == kind);
                        if (equipment == null || !equipment.installed) throw new Exception("Missing " + kind + " in 106");
                        equipment.localFault = true;
                        equipment.episode++;
                    }
                    state.mvp.utilities.waterFault = true;
                    state.mvp.utilities.waterEpisode++;
                    state.mvp.utilities.powerFault = true;
                    state.mvp.utilities.powerEpisode++;
                    room106.mvp.dirt = .7f;
                    room106.mvp.dirtyTowels = 1;
                    HotelSaveStore.Validate(state);
                })) yield break;

                int stock = state.mvp.coffeeStock;
                yield return MvpInteractionE("coffee", new Vector3(6.5f, .1f, -1.9f),
                    HotelLayout.Target("coffee") + new Vector3(-.25f, .23f, .02f), 6,
                    () => state.mvp.coffeeStock == stock - 1 && game.Held != null && game.Held.kind == "coffee",
                    "REAL_E_BREWS_COFFEE");
                if (finished) yield break;
                var cup = game.Held;
                if (!MvpInteractionFixture("CAPTURE_REAL_BREWED_COFFEE_IN_HAND", () => CaptureFeedback("mvp-interaction-coffee"))) yield break;
                yield return MvpInteractionE("coffee_105", new Vector3(-3.1f, .1f, 20.75f),
                    HotelLayout.RoomTarget("coffee", 105) + Vector3.down * .065f, 0,
                    () => cup.consumed && game.Held == null && coffeeRequest.status == "fulfilled" && coffeeRequest.rewarded,
                    "REAL_E_DELIVERS_COFFEE_AND_REWARDS_REQUEST");
                if (finished) yield break;

                if (!MvpInteractionTool("plunger")) yield break;
                yield return MvpInteractionE("toilet_106", new Vector3(5.7f, .1f, 18.8f),
                    new Vector3(6.9f, .7f, 18.99f), 4,
                    () => !room106.mvp.equipment.Find(e => e.kind == "toilet").localFault,
                    "REAL_E_PLUNGER_REPAIRS_TOILET");
                if (finished) yield break;
                if (!Check(state.mvp.utilities.waterFault && !HotelOperationsRules.EffectiveEquipment(state, 106, "toilet"),
                    "Local toilet repair incorrectly repaired its water dependency")) yield break;

                if (!MvpInteractionTool("toolbox")) yield break;
                Vector3 tvAim = Vector3.zero, lampAim = Vector3.zero;
                if (!MvpInteractionFixture("LOCATE_ACTUAL_ELEVATED_TV_AND_LAMP_COLLIDERS", () =>
                {
                    tvAim = MvpInteractionColliderCenter("tv_106", "TV case");
                    lampAim = MvpInteractionColliderCenter("lamp_106", "Pleated warm lampshade");
                })) yield break;
                if (!Check(tvAim.y > 2, "TV test must aim at the elevated case, not a logical floor/root target")) yield break;
                yield return MvpInteractionE("tv_106", new Vector3(3.7f, .1f, 18.8f), tvAim, 4,
                    () => !room106.mvp.equipment.Find(e => e.kind == "tv").localFault,
                    "REAL_E_TOOLBOX_REPAIRS_ELEVATED_TV", "mvp-interaction-tv-work");
                if (finished) yield break;
                yield return MvpInteractionE("lamp_106", new Vector3(7.25f, .1f, 22.42f), lampAim, 4,
                    () => !room106.mvp.equipment.Find(e => e.kind == "lamp").localFault,
                    "REAL_E_TOOLBOX_REPAIRS_LAMP");
                if (finished) yield break;
                if (!Check(state.mvp.utilities.powerFault && !HotelOperationsRules.EffectiveEquipment(state, 106, "tv") &&
                    !HotelOperationsRules.EffectiveEquipment(state, 106, "lamp"),
                    "Local electrical repairs incorrectly repaired the shared power dependency")) yield break;

                yield return MvpInteractionE("utility_water", new Vector3(-7, .1f, -4.25f),
                    HotelLayout.Target("utility_water"), 6, () => !state.mvp.utilities.waterFault,
                    "REAL_E_TOOLBOX_REPAIRS_WATER_UTILITY");
                if (finished) yield break;
                yield return MvpInteractionE("utility_power", new Vector3(-6.25f, .1f, -4.2f),
                    HotelLayout.Target("utility_power"), 6, () => !state.mvp.utilities.powerFault,
                    "REAL_E_TOOLBOX_REPAIRS_POWER_UTILITY");
                if (finished) yield break;
                if (!Check(HotelOperationsRules.EffectiveEquipment(state, 106, "toilet") &&
                    HotelOperationsRules.EffectiveEquipment(state, 106, "tv") && HotelOperationsRules.EffectiveEquipment(state, 106, "lamp"),
                    "Repaired room equipment did not recover after its utilities were restored")) yield break;

                if (!MvpInteractionTool("mop")) yield break;
                yield return MvpInteractionE("clean_106", new Vector3(4.7f, .1f, 18.25f),
                    HotelLayout.RoomTarget("clean", 106), 4, () => room106.mvp.dirt <= .001f,
                    "REAL_E_MOP_CLEANS_FLOOR");
                if (finished) yield break;
                if (!MvpInteractionTool("")) yield break;
                yield return MvpInteractionE("dirtytowel_106", new Vector3(5.3f, .1f, 17.75f),
                    HotelLayout.RoomTarget("dirtytowel", 106) + Vector3.down * .15f, 1.5f,
                    () => room106.mvp.dirtyTowels == 0 && game.Held != null && game.Held.kind == "towel" && game.Held.condition == "dirty",
                    "REAL_E_COLLECTS_USED_TOWEL");
                if (finished) yield break;
                var dirtyTowel = game.Held;
                if (!MvpInteractionFixture("CAPTURE_REAL_USED_TOWEL_IN_HAND", () => CaptureFeedback("mvp-interaction-dirty-towel"))) yield break;
                yield return MvpInteractionE("hamper", new Vector3(-4.8f, .1f, -4.7f),
                    new Vector3(-4.8f, .46f, -3.5f), 0, () => dirtyTowel.consumed && game.Held == null,
                    "REAL_E_DISPOSES_USED_TOWEL_IN_HAMPER");
                if (finished) yield break;
                if (!MvpInteractionFixture("VALIDATE_FINAL", () => HotelSaveStore.Validate(state))) yield break;
                float elapsed = Time.realtimeSinceStartup - started;
                if (!Check(elapsed < 120, "MVP interaction exceeded its 120-second wall-clock budget")) yield break;
                checks.Add("MVP_INTERACTION_COMPLETE_REALTIME_SECONDS=" + elapsed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            }
            finally
            {
                MvpInteractionRelease();
                game.HandMotion = handMotion;
                InputSystem.settings.backgroundBehavior = background;
            }
        }

        bool MvpInteractionFixture(string label, Action action)
        {
            try { action(); checks.Add("FIXTURE_" + label); return true; }
            catch (Exception e) { MvpInteractionRelease(); return Check(false, label + ": " + e); }
        }

        bool MvpInteractionTool(string kind)
        {
            return MvpInteractionFixture("TOOL_SETUP_" + (kind == "" ? "EMPTY_HANDS" : kind), () =>
            {
                if (game.Held != null) FixtureDrop();
                if (kind != "") FixturePick(kind);
                HotelSaveStore.Validate(game.Session.State);
            });
        }

        void MvpInteractionRelease()
        {
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        }

        void MvpInteractionPose(Vector3 floor)
        {
            game.OpenPanel("");
            game.Teleport(floor);
            string error = game.Session.Simulation.Execute(0, new HotelCommand("pose") { position = floor });
            if (error != "") throw new Exception("Approach fixture pose: " + error);
        }

        Vector3 MvpInteractionColliderCenter(string target, string colliderName)
        {
            foreach (var collider in game.World.GetComponentsInChildren<Collider>())
            {
                var tag = collider.GetComponentInParent<HotelTarget>();
                if (collider.enabled && !collider.isTrigger && collider.name == colliderName && tag != null && tag.id == target)
                    return collider.bounds.center;
            }
            throw new Exception("Missing physical collider " + target + "/" + colliderName);
        }

        string MvpInteractionRayDetail()
        {
            string hitName = "nothing";
            if (Physics.Raycast(game.View.transform.position, game.View.transform.forward, out RaycastHit hit, 3.15f,
                ~(1 << 2), QueryTriggerInteraction.Ignore))
                hitName = hit.collider.name + " target=" + hit.collider.GetComponentInParent<HotelTarget>()?.id;
            return " focus=" + game.FocusId + " ray=" + hitName + " floor=" + game.Controller.transform.position +
                " held=" + game.Held?.kind + " work=" + game.LocalPlayer?.workTarget;
        }

        IEnumerator MvpInteractionEnterRoom(int number)
        {
            MvpInteractionRelease();
            Vector3 center = HotelLayout.RoomCenter(number);
            if (!MvpInteractionFixture("CORRIDOR_APPROACH_" + number,
                () => MvpInteractionPose(new Vector3(0, .1f, center.z)))) yield break;
            yield return new WaitForSecondsRealtime(.12f);
            game.LookAtForTest(new Vector3(center.x, game.View.transform.position.y, center.z));
            yield return null;
            float sign = number % 2 == 1 ? -1 : 1;
            float until = Time.realtimeSinceStartup + 1.25f;
            try
            {
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W));
                while (sign * game.Controller.transform.position.x < 2.25f && Time.realtimeSinceStartup < until)
                    yield return null;
            }
            finally { MvpInteractionRelease(); }
            yield return new WaitForSecondsRealtime(.1f); // Let the ordinary pose sender report the entry.
            Vector3 floor = game.Controller.transform.position;
            if (!Check(sign * floor.x >= 2.25f && sign * game.LocalPlayer.position.x >= 2.1f &&
                Mathf.Abs(floor.z - center.z) < .3f && floor.y < .3f,
                "Real W movement could not cross purchased doorway " + number + ": " + floor)) yield break;
            checks.Add("REAL_W_ENTERED_PURCHASED_ROOM_" + number);
        }

        IEnumerator MvpInteractionE(string target, Vector3 floor, Vector3 aim, float duration,
            Func<bool> completed, string label, string captureDuringWork = null)
        {
            MvpInteractionRelease();
            if (!MvpInteractionFixture("APPROACH_" + target, () => MvpInteractionPose(floor))) yield break;
            yield return new WaitForSecondsRealtime(.12f); // Settle the real capsule on the floor before aiming.
            game.LookAtForTest(aim);
            yield return null;
            yield return null;
            Vector3 standing = game.Controller.transform.position;
            if (!Check(game.InputActive && game.FocusId == target && standing.y >= -.1f && standing.y < .3f &&
                Vector2.Distance(new Vector2(standing.x, standing.z), new Vector2(floor.x, floor.z)) < .3f,
                "Cannot focus physical " + target + MvpInteractionRayDetail())) yield break;
            if (!Check(!completed(), "Outcome was already completed before E: " + label)) yield break;
            checks.Add("REAL_RAY_FOCUS_" + target);
            float start = Time.realtimeSinceStartup;
            bool sawWork = false, captured = false;
            string failure = "";
            try
            {
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
                do
                {
                    yield return null;
                    if (completed()) break;
                    if (!game.InputActive || game.FocusId != target)
                    { failure = "Lost physical focus while holding E: " + target + MvpInteractionRayDetail(); break; }
                    sawWork |= game.ConfirmedWork == target && game.LocalPlayer.workProgress > 0;
                    if (!captured && sawWork && captureDuringWork != null && Time.realtimeSinceStartup - start >= .35f)
                    {
                        captured = true;
                        if (!MvpInteractionFixture("CAPTURE_CONFIRMED_REAL_WORK_" + target, () => CaptureFeedback(captureDuringWork))) yield break;
                    }
                } while (Time.realtimeSinceStartup - start < duration + 1);
            }
            finally { MvpInteractionRelease(); }
            yield return null;
            yield return null;
            if (!Check(failure == "", failure)) yield break;
            if (!Check(completed() && (duration == 0 || sawWork),
                label + " did not complete using real E within " + (duration + 1) + " seconds" + MvpInteractionRayDetail())) yield break;
            if (!MvpInteractionFixture("VALIDATE_AFTER_" + target, () => HotelSaveStore.Validate(game.Session.State))) yield break;
            checks.Add(label + " seconds=" + (Time.realtimeSinceStartup - start).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
