using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorstHotel
{
    public static class HotelDirectorTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "Director: authored resource and isolated catalogue definitions", Catalogue);
            Run(passed, "Director: new-world defaults and slow first arrival protection", Protection);
            return passed;
        }

        private static void Run(List<string> passed, string name, Action test)
        {
            try { test(); passed.Add(name); }
            catch (Exception error) { throw new Exception("HotelDirectorTests FAILED: " + name, error); }
        }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static HotelSimulation New(bool multiplayer = false)
        {
            HotelGuestCatalog catalog = HotelGuestCatalog.Load();
            Assert(catalog.Version == 1 && catalog.Warning == "", "Authored Resources/guest-profiles.json did not load");
            var sim = new HotelSimulation(null, catalog); sim.Join(0); if (multiplayer) sim.Join(1); return sim;
        }
        private static void Ok(HotelSimulation sim, ulong player, string action, string target = "", int number = 0)
        {
            string error = sim.Execute(player, new HotelCommand(action, target, number));
            Assert(error == "", action + " " + target + ": " + error);
            HotelSaveStore.Validate(sim.State);
        }
        private static void Advance(HotelSimulation sim, float duration)
        {
            for (int i = 0; i < (int)Math.Ceiling(duration * 10); i++) sim.Tick(.1f);
            HotelSaveStore.Validate(sim.State);
        }
        private static void At(HotelSimulation sim, ulong id, string target)
        {
            Vector3 position = HotelLayout.Target(target); position.y = .1f;
            Assert(sim.Execute(id, new HotelCommand("pose") { position = position }) == "", "Fixture position rejected");
        }
        private static void Catalogue()
        {
            HotelGuestCatalog catalog = HotelGuestCatalog.Load();
            Assert(catalog.Version == 1, "Missing catalogue");
            HotelGuestProfile patient = catalog.GetProfile("patient"), hurried = catalog.GetProfile("hurried"), tidy = catalog.GetProfile("tidy");
            Assert(patient != null && hurried != null && tidy != null, "Required profile missing");
            Assert(patient.patienceLimit > hurried.patienceLimit && patient.staySeconds > hurried.staySeconds && tidy.staySeconds != patient.staySeconds, "Profiles have cosmetic-only differences");
            HotelRequestDefinition request = catalog.GetRequest(patient.requestId);
            Assert(request.delaySeconds != catalog.GetRequest(hurried.requestId).delaySeconds, "Request timing not authored per profile");
            patient.staySeconds = 999; request.delaySeconds = 999;
            Assert(catalog.GetProfile("patient").staySeconds != 999 && catalog.GetRequest("patient_towel").delaySeconds != 999, "Catalogue leaked mutable definitions");
        }
        private static void Protection()
        {
            HotelSimulation sim = New();
            Assert(sim.State.contentVersion == 1 && sim.State.guidedOpening && HotelDirector.IsGuided(sim.State) && !HotelDirector.ClockHeld(sim.State), "New hotel does not opt in only when open");
            At(sim, 0, "desk"); Ok(sim, 0, "open"); Advance(sim, 600);
            Assert(sim.State.guests.Count == 1 && sim.State.guests[0].stage == "queue" && sim.State.guests[0].waited == 0 && sim.State.time == 0, "Slow first team lost its guest or clock");
            Assert(sim.State.guests[0].kind == "patient" && HotelDirector.ClockHeld(sim.State), "First guest is not protected patient");
            string before = JsonUtility.ToJson(sim.State);
            Assert(HotelDirector.Status(sim.State).Contains("зарегистрируйте") && HotelDirector.DayBrief(sim.State).Contains("трёх"), "Pacing not explained");
            Assert(JsonUtility.ToJson(sim.State) == before, "UI APIs changed director state");
        }
    }
}
