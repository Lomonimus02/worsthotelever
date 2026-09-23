using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    // Pure scheduler tests: explicit time, plain snapshots, no scene/input/save/network side effects.
    // Parent integration: add HotelTabletTests.RunAll() to the existing editor runner.
    public static class HotelTabletTests
    {
        const float Epsilon = .01f;
        const string Welcome = "welcome";
        const string Lesson = "Prepare the room";

        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "Tablet hints: timing contract and delayed welcome payload", Bootstrap);
            Run(passed, "Tablet hints: lifetime expires without repeating the key", Expiry);
            Run(passed, "Tablet hints: dismissal leaves a full quiet interval", DismissAndQuiet);
            Run(passed, "Tablet hints: changed body cannot bypass stable-key suppression", StableKey);
            Run(passed, "Tablet hints: contextual changes cannot interrupt a visible hint", NoReplacement);
            Run(passed, "Tablet hints: tablet/menu hides current and does not consume unseen candidates", Menu);
            Run(passed, "Tablet hints: time spent in a menu cannot resurrect expired content", MenuExpiry);
            Run(passed, "Tablet hints: disabling hides current without resetting dismissal history", Toggle);
            Run(passed, "Tablet hints: host/client preferences and dismissal are independent", LocalInstances);
            Run(passed, "Tablet hints: client lesson and local toggle preserve host tutorial and pace", ClientContext);
            Run(passed, "Tablet hints: reset clears shown keys and preserves enabled preference", Reset);
            Run(passed, "Tablet hints: missing candidate never draws or consumes a lesson", EmptyCandidate);
            Run(passed, "Tablet hints: local lesson ignores legacy shared opt-out without mutating it", LocalOverride);
            return passed;
        }

        static HotelTeachingHints Fresh(float now = 0)
        {
            var hints = new HotelTeachingHints();
            hints.Enabled = true;
            hints.Reset(now);
            Require(!hints.Visible, "Reset left a visible hint");
            return hints;
        }

        static void Tick(HotelTeachingHints hints, float now, string key = Welcome, bool allowed = true,
            string body = "Open the staff tablet with Tab.")
        {
            hints.Tick(now, key, key == Welcome ? "Staff tablet" : key, body, allowed);
        }

        static void Shown(HotelTeachingHints hints, string key)
        {
            Require(hints.Visible && hints.CurrentKey == key, "Expected visible key: " + key);
            Require(!string.IsNullOrEmpty(hints.Title) && !string.IsNullOrEmpty(hints.Body), "Visible hint lost its payload");
        }

        static void Bootstrap()
        {
            Require(HotelTeachingHints.Lifetime == 8f && HotelTeachingHints.QuietTime == 24f &&
                HotelTeachingHints.InitialDelay == 2f, "Scheduler timing contract changed");
            var hints = Fresh(100);
            Tick(hints, 100);
            Tick(hints, 100 + HotelTeachingHints.InitialDelay - Epsilon);
            Require(!hints.Visible, "Welcome appeared before initial delay");
            Tick(hints, 100 + HotelTeachingHints.InitialDelay);
            Shown(hints, Welcome);
            Require(hints.Title == "Staff tablet" && hints.Body == "Open the staff tablet with Tab.",
                "Scheduler did not preserve the supplied title/body");
        }

        static void Expiry()
        {
            var hints = Fresh();
            float shown = HotelTeachingHints.InitialDelay;
            Tick(hints, shown);
            Tick(hints, shown + HotelTeachingHints.Lifetime - Epsilon);
            Shown(hints, Welcome);
            Tick(hints, shown + HotelTeachingHints.Lifetime);
            Require(!hints.Visible, "Hint survived its lifetime boundary");
            Tick(hints, shown + HotelTeachingHints.Lifetime + HotelTeachingHints.QuietTime * 3);
            Require(!hints.Visible, "Expired key repeated without a session reset");
        }

        static void DismissAndQuiet()
        {
            var hints = Fresh();
            float shown = HotelTeachingHints.InitialDelay;
            Tick(hints, shown);
            float dismissed = shown + 1;
            hints.Dismiss(dismissed);
            Require(!hints.Visible && hints.Enabled, "Dismiss must immediately hide without disabling future hints");
            Tick(hints, dismissed + HotelTeachingHints.QuietTime - Epsilon, Lesson);
            Require(!hints.Visible, "Another lesson appeared inside the quiet interval");
            Tick(hints, dismissed + HotelTeachingHints.QuietTime + Epsilon, Lesson);
            Shown(hints, Lesson);
            hints.Dismiss(dismissed + HotelTeachingHints.QuietTime + 1);
            Tick(hints, dismissed + HotelTeachingHints.QuietTime * 4);
            Require(!hints.Visible, "Dismissed welcome repeated later in the session");
        }

        static void StableKey()
        {
            var hints = Fresh();
            Tick(hints, HotelTeachingHints.InitialDelay, Lesson, body: "Room 102 needs work.");
            Shown(hints, Lesson);
            hints.Dismiss(3);
            hints.Tick(100, Lesson, Lesson, "Room 104 now needs work; the lesson is unchanged.", true);
            Require(!hints.Visible, "Changing contextual body bypassed suppression for the same stable key");
            hints.Tick(101, "Take the toolbox", "Take the toolbox", "Repair the faulty sink.", true);
            Shown(hints, "Take the toolbox");
            hints.Dismiss(102);
            Tick(hints, 200, Lesson);
            Require(!hints.Visible, "Returning from another lesson repeated an already dismissed key");
        }

        static void NoReplacement()
        {
            var hints = Fresh();
            Tick(hints, HotelTeachingHints.InitialDelay);
            Tick(hints, HotelTeachingHints.InitialDelay + 1, Lesson);
            Shown(hints, Welcome);
            Tick(hints, HotelTeachingHints.InitialDelay + HotelTeachingHints.Lifetime, Lesson);
            Require(!hints.Visible, "Expiry immediately replaced a hint with another lesson");
            Tick(hints, HotelTeachingHints.InitialDelay + HotelTeachingHints.Lifetime +
                HotelTeachingHints.QuietTime + Epsilon, Lesson);
            Shown(hints, Lesson);
        }

        static void Menu()
        {
            var hints = Fresh();
            Tick(hints, HotelTeachingHints.InitialDelay);
            Shown(hints, Welcome);
            Tick(hints, 3, Lesson, false);
            Require(!hints.Visible, "Opening tablet/menu retained the visible hint");
            Tick(hints, 100, Lesson, false);
            Require(!hints.Visible, "Hint appeared while tablet/menu owns input");
            Tick(hints, 101, Lesson);
            Shown(hints, Lesson);
            hints.Dismiss(102);
            Tick(hints, 200);
            Require(!hints.Visible, "Closing tablet/menu replayed the previously shown welcome");
        }

        static void MenuExpiry()
        {
            var hints = Fresh();
            Tick(hints, HotelTeachingHints.InitialDelay);
            Tick(hints, 100, Welcome, false);
            Require(!hints.Visible, "Expired hint stayed visible in menu");
            Tick(hints, 200);
            Require(!hints.Visible, "Returning to gameplay resurrected an expired hint");
            Tick(hints, 201, Lesson);
            Shown(hints, Lesson);
        }

        static void Toggle()
        {
            var hints = Fresh();
            Tick(hints, HotelTeachingHints.InitialDelay);
            hints.Enabled = false;
            Tick(hints, 3);
            Require(!hints.Visible, "Disabled hint was still visible after Tick");
            Tick(hints, 100, Lesson);
            Require(!hints.Visible, "Disabled scheduler showed a new key");
            hints.Enabled = true;
            Tick(hints, 200);
            Require(!hints.Visible, "Re-enabling reset suppression for an already shown key");
            Tick(hints, 201, Lesson);
            Shown(hints, Lesson);
        }

        static void LocalInstances()
        {
            var host = Fresh();
            var client = Fresh();
            Tick(host, HotelTeachingHints.InitialDelay);
            Tick(client, HotelTeachingHints.InitialDelay);
            host.Dismiss(3);
            Shown(client, Welcome);
            client.Enabled = false;
            Tick(client, 4);
            Require(host.Enabled && !client.Visible, "Client preference leaked into the host instance");
            Tick(host, 100, Lesson);
            Shown(host, Lesson);
            Tick(client, 100, Lesson);
            Require(!client.Visible, "Client ignored its local opt-out");
            client.Enabled = true;
            Tick(client, 101, Lesson);
            Shown(client, Lesson);
            client.Dismiss(102);
            Shown(host, Lesson);
        }

        static void ClientContext()
        {
            // Plain authoritative snapshot only; no simulation factory, NGO or save path.
            var state = new HotelState { contentVersion = 1, guidedOpening = true, phase = "preparation" };
            state.players.Add(new PlayerState { id = 0 });
            state.players.Add(new PlayerState { id = 77 });
            state.rooms.Add(new RoomState { number = 101, bed = 2 });
            string before = JsonUtility.ToJson(state);
            var hostLesson = HotelOnboarding.GetHint(state, 0);
            var clientLesson = HotelOnboarding.GetHint(state, 77);
            Require(hostLesson != null && clientLesson != null && hostLesson.body != clientLesson.body,
                "Fixture did not produce distinct host/client teaching context");
            var host = Fresh();
            var client = Fresh();
            host.Tick(2, hostLesson.title, hostLesson.title, hostLesson.body, true);
            client.Tick(2, clientLesson.title, clientLesson.title, clientLesson.body, true);
            Require(client.Visible && client.Body == clientLesson.body, "Client did not receive its own contextual lesson");
            client.Dismiss(3);
            client.Enabled = false;
            client.Reset(4);
            Tick(client, 100);
            Shown(host, hostLesson.title);
            Require(!client.Visible && JsonUtility.ToJson(state) == before,
                "Local hint controls changed authoritative tutorial progress, shared skip or learning pace");
        }

        static void LocalOverride()
        {
            var state=new HotelState{contentVersion=1,phase="preparation",tutorialSkipped=true,guidedOpening=true};
            state.players.Add(new PlayerState{id=77});state.rooms.Add(new RoomState{number=101,bed=2});
            string before=JsonUtility.ToJson(state);
            Require(HotelOnboarding.GetHint(state,77)==null,"Legacy caller lost shared opt-out");
            var hint=HotelOnboarding.GetHint(state,77,true);
            Require(hint!=null&&!string.IsNullOrEmpty(hint.body),"Local opted-in client lost its hint");
            Require(JsonUtility.ToJson(state)==before,"Local projection mutated shared learning state");
        }
        static void Reset()
        {
            var hints = Fresh();
            Tick(hints, HotelTeachingHints.InitialDelay);
            hints.Dismiss(3);
            Tick(hints, 100, Lesson);
            hints.Enabled = false;
            hints.Reset(200);
            Require(!hints.Enabled && !hints.Visible, "World/session reset changed the player's opt-out");
            Tick(hints, 250);
            Require(!hints.Visible, "Reset re-enabled teaching");
            hints.Enabled = true;
            hints.Reset(300);
            Tick(hints, 300 + HotelTeachingHints.InitialDelay - Epsilon);
            Require(!hints.Visible, "Reset lost the initial quiet delay");
            Tick(hints, 300 + HotelTeachingHints.InitialDelay);
            Shown(hints, Welcome);
            hints.Dismiss(303);
            Tick(hints, 400, Lesson);
            Shown(hints, Lesson);
        }

        static void EmptyCandidate()
        {
            var hints = Fresh();
            hints.Tick(100, "", "", "", true);
            Require(!hints.Visible, "Missing lesson produced an empty hint");
            Tick(hints, 101, Lesson);
            Shown(hints, Lesson);
        }

        static void Run(List<string> passed, string name, Action test)
        {
            try { test(); passed.Add(name); }
            catch (Exception error) { throw new Exception("HotelTabletTests FAILED: " + name, error); }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
