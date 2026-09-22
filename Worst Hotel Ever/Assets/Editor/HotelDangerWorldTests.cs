using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace WorstHotel
{
    // Source-authored checks only; the integrator runs this suite and inspects the Windows player.
    public static class HotelDangerWorldTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            using (var f = new Fixture())
            {
                Run(passed, "Danger world: fixed stations and six doorway controls are raycastable", () => Controls(f));
                Run(passed, "Danger world: source rays select emergency targets, not old fixtures", () => Sources(f));
                Run(passed, "Danger world: warning/hot/isolated/resolved use exact bounded rule geometry", () => Zones(f));
                Run(passed, "Danger world: durable prone bodies survive disconnect and suppress upright colliders", () => Bodies(f));
                Run(passed, "Danger world: snapshots remain read-only, pooled and classic-compatible", () => Reuse(f));
            }
            Run(passed, "Danger feedback: confirmed host/client edges and silent baselines", Feedback);
            Run(passed, "Danger audio: lazy bounded voices restore classic nine clips/eight sources", Audio);
            Run(passed, "Danger feedback: bounded original waveforms and neutral optional hand motion", SamplesAndHands);
            return passed;
        }

        static HotelState State()
        {
            var s = new HotelState { version = 3, contentVersion = 3, phase = "open", mvp = new MvpHotelState { schema = 2 },
                danger = new HotelDangerState { schema = 1, serial = 1, day = 1, status = "active", mode = "bold" } };
            for (int n = 101; n <= 106; n++)
            {
                var r = new RoomState { number = n, mvp = new MvpRoomState { capacity = n % 2 == 0 ? 2 : 1 } };
                foreach (string kind in new[] { "sink", "toilet", "tv", "lamp" }) r.mvp.equipment.Add(new MvpEquipmentState { kind = kind });
                s.rooms.Add(r);
            }
            s.players.Add(new PlayerState { id = 0, position = HotelLayout.Spawn });
            s.players.Add(new PlayerState { id = 77, position = new Vector3(0, .1f, 11) });
            s.danger.crew.Add(new HotelCrewState { slot = 0, joined = true, position = HotelLayout.Spawn });
            s.danger.crew.Add(new HotelCrewState { slot = 1, joined = true, position = s.players[1].position });
            s.danger.incidents.Add(new HotelIncidentState { room = 101, kind = "electric", status = "warning", warningRemaining = 10 });
            s.danger.incidents.Add(new HotelIncidentState { room = 102, kind = "steam" });
            s.danger.incidents.Add(new HotelIncidentState { room = 103, kind = "fumes" });
            return s;
        }

        static void Controls(Fixture f)
        {
            HotelState s = State(); f.Apply(s);
            Near(f.Named("First aid station").position, new Vector3(-6.5f, 1.1f, -3.3f), "Aid moved");
            Near(f.Named("Evacuation alarm").position, new Vector3(1.45f, 1.25f, -6.4f), "Alarm moved");
            f.Ray(new Vector3(-6.5f, 1.75f, -4.45f), f.Named("First aid cabinet").position, "firstaid");
            f.Ray(new Vector3(1.45f, 1.75f, -5.2f), f.Named("Alarm guarded button").position, "alarm");
            for (int n = 101; n <= 106; n++)
            {
                Vector3 center = HotelLayout.RoomCenter(n), control = HotelDangerRules.Control(n);
                Near(f.Named("Emergency control " + n).position, control, "Control not at Rules.Control");
                f.Ray(new Vector3(0, 1.75f, center.z - .15f), control, "isolate_" + n);
                Assert(!f.PhysicsWorld.CapsuleCast(new Vector3(0, .43f, center.z), new Vector3(0, 1.47f, center.z), .28f,
                    new Vector3(Mathf.Sign(center.x), 0, 0), out RaycastHit hit, Mathf.Abs(center.x), ~0, QueryTriggerInteraction.Ignore),
                    "Emergency control blocks doorway " + n + ": " + hit.collider?.name);
            }
            s.rooms[5].mvp.owned = false; f.Apply(s);
            Assert(!f.Named("Emergency control 106").gameObject.activeInHierarchy, "Locked room exposes a control");
        }

        static void Sources(Fixture f)
        {
            HotelState s = State();
            foreach (HotelIncidentState incident in s.danger.incidents)
            {
                foreach (HotelIncidentState other in s.danger.incidents) other.status = "planned";
                incident.status = "active"; incident.isolated = true; f.Apply(s);
                int index = s.danger.incidents.IndexOf(incident);
                Transform view = f.Named("Incident view " + index);
                Transform face = f.Child(view, "Emergency service face");
                Vector3 source = HotelDangerRules.Source(incident);
                Vector3 eye = source + (incident.kind == "electric" ? Vector3.back * 1.4f : Vector3.forward * 1.45f);
                if (incident.kind == "steam") eye.x -= 1.05f;
                eye.y = 1.75f;
                f.Ray(eye, face.position, "hazard_" + incident.room);
                f.Ray(eye, source, "hazard_" + incident.room);
                foreach (Collider collider in view.GetComponentsInChildren<Collider>())
                    Assert(collider.GetComponent<HotelTarget>()?.id == "hazard_" + incident.room, "Source collider lost canonical target");
            }
        }

        static void Zones(Fixture f)
        {
            HotelState s = State(); HotelIncidentState incident = s.danger.incidents[0];
            foreach (string stage in new[] { "warning", "active", "isolated", "resolved" })
            {
                incident.status = stage == "isolated" ? "active" : stage; incident.isolated = stage == "isolated";
                f.Apply(s); Transform view = f.Named("Incident view 0");
                Transform zone = f.Child(view, "Authoritative danger boundary");
                Near(view.position, HotelDangerRules.Source(incident), "Source moved from rules");
                Near(zone.position, new Vector3(view.position.x, .055f, view.position.z), "Zone is not on the source floor");
                Assert(Mathf.Abs(zone.localScale.x - HotelDangerRules.Radius(incident)) < .00001f && zone.localScale.x == zone.localScale.z,
                    "Danger radius was visually exaggerated");
                Assert(zone.GetComponentsInChildren<Collider>(true).Length == 0, "Decorative boundary blocks input or walking");
                Assert(f.Child(view, "Active source markers").gameObject.activeSelf == HotelDangerRules.IsHot(incident), "Hot effects disagree with rules");
                Assert(zone.gameObject.activeSelf == (stage != "resolved"), "Resolved source left a danger zone");
                if (stage == "resolved") foreach (Collider c in view.GetComponentsInChildren<Collider>(true)) Assert(!c.enabled, "Resolved marker steals old fixture input");
            }
            foreach (HotelIncidentState i in s.danger.incidents)
            {
                Vector3 source = HotelDangerRules.Source(i), control = HotelDangerRules.Control(i.room); source.y = control.y = 0;
                Assert(Vector3.Distance(source, control) > HotelDangerRules.Radius(i) + .28f, "Control lies inside a damaging capsule approach");
            }
        }

        static void Bodies(Fixture f)
        {
            HotelState s = State(); f.Apply(s);
            Transform upright = f.Named("player_77"); Assert(upright.gameObject.activeInHierarchy, "Healthy partner is hidden");
            HotelCrewState crew = s.danger.crew[1]; crew.life = "downed"; crew.health = 0; crew.bleedout = 35; f.Apply(s);
            Transform body = f.Named("Durable crew body 1"); int identity = body.GetInstanceID();
            Assert(body.gameObject.activeInHierarchy && !upright.gameObject.activeInHierarchy, "Downed partner kept a standing duplicate");
            Assert(!body.GetComponent<CapsuleCollider>().enabled, "Prone body retained upright collider");
            Assert(body.GetComponent<BoxCollider>().bounds.max.y < .26f, "Rescue collider cannot be stepped over");
            f.Ray(new Vector3(1, 1.75f, 11), crew.position, "rescue_1");
            s.players.RemoveAt(1); f.Apply(s);
            Assert(body.gameObject.activeInHierarchy && body.GetInstanceID() == identity, "Disconnect removed rescue body");
            crew.life = "dead"; crew.bleedout = 0; f.Apply(s);
            Assert(body.gameObject.activeInHierarchy && body.GetComponent<HotelTarget>().label.Contains("погиб"), "Death is not visually distinct");
            s.players.Add(new PlayerState { id = 99, position = crew.position }); f.Apply(s);
            Assert(!f.Named("player_99").gameObject.activeInHierarchy && body.gameObject.activeInHierarchy, "New connection ID resurrected upright employee");
            crew.life = "healthy"; crew.health = 60; f.Apply(s);
            Assert(!body.gameObject.activeInHierarchy && f.Named("player_99").gameObject.activeInHierarchy, "Recovery did not restore the normal figure");
        }

        static void Reuse(Fixture f)
        {
            HotelState s = State(); f.Apply(s);
            string json = JsonUtility.ToJson(s); Transform[] before = f.Host.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < 50; i++) f.Apply(i % 2 == 0 ? s : JsonUtility.FromJson<HotelState>(json));
            Assert(JsonUtility.ToJson(s) == json, "World mutated durable state");
            Transform[] after = f.Host.GetComponentsInChildren<Transform>(true);
            Assert(before.Length == after.Length, "Repeated snapshots allocated objects");
            for (int i = 0; i < before.Length; i++) Assert(before[i] == after[i], "Pooled identity changed");
            s.version = s.contentVersion = 2; s.danger = null; f.Apply(s);
            Assert(!f.Named("Danger / emergency equipment and crew").gameObject.activeInHierarchy, "Classic snapshot retained emergency colliders");
            Assert(f.Named("player_77").gameObject.activeInHierarchy, "Classic partner behavior changed");
        }

        static void Feedback()
        {
            HotelState s = State(); var feedback = new HotelFeedback();
            Assert(feedback.Observe(s, 0) == HotelCue.None, "Load replayed danger history");
            s.danger.crew[0].health = 84; Assert((feedback.Observe(s, 0) & HotelCue.Injury) != 0, "Confirmed injury silent");
            Assert(feedback.Observe(JsonUtility.FromJson<HotelState>(JsonUtility.ToJson(s)), 0) == HotelCue.None, "Client snapshot duplicated injury");
            s.danger.crew[1].life = "downed"; s.danger.crew[1].health = 0; s.danger.crew[1].bleedout = 45;
            Assert((feedback.Observe(s, 0) & HotelCue.Downed) != 0, "Partner collapse silent");
            s.danger.crew[1].life = "healthy"; s.danger.crew[1].health = 60; s.danger.crew[1].bleedout = 0;
            Assert((feedback.Observe(s, 0) & HotelCue.Revived) != 0, "Confirmed revival silent");
            s.players[0].workTarget = "isolate_101"; feedback.BeginWork(); feedback.Observe(s, 0);
            s.players[0].workTarget = ""; Assert((feedback.Observe(s, 0) & HotelCue.Complete) == 0, "Rejected/cancelled isolation sounded successful");
            s.players[0].workTarget = "isolate_101"; feedback.Observe(s, 0);
            s.danger.incidents[0].isolated = true; s.players[0].workTarget = "";
            Assert((feedback.Observe(s, 0) & HotelCue.Complete) != 0, "Confirmed isolation silent");
            s.danger.outcome = "evacuated"; s.danger.status = "failed";
            Assert((feedback.Observe(s, 0) & HotelCue.Alarm) != 0, "Confirmed evacuation silent");
            Assert(feedback.Observe(s, 0) == HotelCue.None, "Alarm repeated every snapshot");
            feedback.Reset(); Assert(feedback.Observe(s, 0) == HotelCue.None, "Reload replayed evacuation");
        }

        static void Audio()
        {
            var host = new GameObject("Danger audio native fixture");
            try
            {
                HotelAudio audio = host.AddComponent<HotelAudio>();
                if (audio.ClipCount == 0) host.SendMessage("Awake");
                Assert(audio.ClipCount == 9 && host.GetComponentsInChildren<AudioSource>().Length == 8, "Classic audio baseline changed");
                HotelState s = State(); audio.Apply(s, HotelCue.None, "", false);
                int clips = audio.ClipCount, sources = host.GetComponentsInChildren<AudioSource>().Length;
                Assert(clips == 19 && sources == 12, "Danger voices are not bounded");
                for (int i = 0; i < 20; i++) audio.Apply(s, HotelCue.None, "", false);
                Assert(audio.ClipCount == clips && host.GetComponentsInChildren<AudioSource>().Length == sources, "Danger apply allocated repeated voices");
                s.version = s.contentVersion = 2; s.danger = null; audio.Apply(s, HotelCue.None, "", false);
                Assert(audio.ClipCount == 9 && host.GetComponentsInChildren<AudioSource>().Length == 8, "Returning to classic retained danger audio allocations");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        static void SamplesAndHands()
        {
            foreach (string kind in new[] { "injury", "downed", "revive", "death", "alarm", "warning", "critical", "danger_electric", "danger_steam", "danger_fumes" })
            {
                float[] data = HotelAudio.Samples(kind, .8f); float energy = 0;
                foreach (float sample in data) { Assert(!float.IsNaN(sample) && !float.IsInfinity(sample) && Mathf.Abs(sample) <= .8f, "Invalid " + kind + " samples"); energy += sample * sample; }
                Assert(energy > .01f && Mathf.Abs(data[0]) < .0001f && Mathf.Abs(data[data.Length - 1]) < .0001f, "Silent/abrupt " + kind + " waveform");
            }
            foreach (string target in new[] { "hazard_101", "isolate_101", "rescue_1", "recover_0", "firstaid", "alarm" })
            {
                HotelFeedback.HandPose(target, .27f, true, out Vector3 offset, out Quaternion rotation);
                Assert(offset.magnitude < .1f && Quaternion.Angle(rotation, Quaternion.identity) < 30, "Excessive emergency hand motion");
                HotelFeedback.HandPose(target, .27f, false, out offset, out rotation);
                Assert(offset == Vector3.zero && rotation == Quaternion.identity, "Emergency motion-off is not neutral");
            }
        }

        static void Run(List<string> passed, string name, Action check)
        { try { check(); passed.Add(name); } catch (Exception e) { throw new Exception("HotelDangerWorldTests FAILED: " + name, e); } }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Near(Vector3 actual, Vector3 expected, string message) { Assert((actual - expected).sqrMagnitude < .000001f, message); }

        sealed class Fixture : IDisposable
        {
            internal readonly GameObject Host = new GameObject("Danger world / isolated preview");
            internal readonly HotelWorld World;
            internal readonly PhysicsScene PhysicsWorld;
            readonly Scene scene;
            internal Fixture()
            {
                scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    SceneManager.MoveGameObjectToScene(Host, scene); PhysicsWorld = scene.GetPhysicsScene();
                    Assert(PhysicsWorld.IsValid() && !PhysicsWorld.Equals(Physics.defaultPhysicsScene), "Preview physics is not isolated");
                    World = Host.AddComponent<HotelWorld>(); World.Build();
                }
                catch { Dispose(); throw; }
            }
            internal void Apply(HotelState state) { World.Apply(state, 0); Physics.SyncTransforms(); }
            internal Transform Named(string name) => Child(Host.transform, name);
            internal Transform Child(Transform parent, string name)
            {
                foreach (Transform t in parent.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
                throw new Exception("Missing " + name);
            }
            internal void Ray(Vector3 eye, Vector3 aim, string target)
            {
                Assert(PhysicsWorld.Raycast(eye, (aim - eye).normalized, out RaycastHit hit, 3.15f, ~(1 << 2), QueryTriggerInteraction.Ignore), "No physical ray hit for " + target);
                Assert(hit.collider.GetComponentInParent<HotelTarget>()?.id == target,
                    "Ray for " + target + " intercepted by " + hit.collider.name + "/" + hit.collider.GetComponentInParent<HotelTarget>()?.id);
            }
            public void Dispose()
            {
                if (Host != null) UnityEngine.Object.DestroyImmediate(Host);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
