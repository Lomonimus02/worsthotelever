using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>Native edit-mode checks. No scene, save, camera, game loop or network is required.</summary>
    public static class HotelFigureTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly Type FigureType = typeof(HotelWorld).Assembly.GetType("WorstHotel.HotelFigure", true);
        static readonly MethodInfo ReactMethod = FigureType.GetMethod("React", PrivateInstance);
        static readonly MethodInfo PoseMethod = FigureType.GetMethod("Pose", PrivateInstance);
        static readonly MethodInfo AnimateMethod = FigureType.GetMethod("Animate", PrivateInstance);

        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Run(passed, "Figure: queue and checkout wait without stale towel gestures", Waiting);
            Run(passed, "Figure: request raises one hand and clears when fulfilled", RequestAndDelivery);
            Run(passed, "Figure: dissatisfaction changes face and preserves an active request", Dissatisfaction);
            Run(passed, "Figure: stage transitions clear stale request poses", StageTransitions);
            Run(passed, "Figure: reaction changes blend without a pose snap", SmoothTransition);
            Run(passed, "Figure: reactions preserve the existing route, turning and leg cycle", WalkIsUnchanged);
            Run(passed, "Figure: employee carry and work animation ignore guest reactions", Employees);
            Run(passed, "Figure: repeated reactions preserve targets, colliders and object identities", StableHierarchy);
            Run(passed, "Figure: World.Apply feeds reactions without mutating snapshots or recreating guests", WorldIntegration);
            return passed;
        }

        static void Waiting()
        {
            foreach (string stage in new[] { "queue", "checkout" })
                using (var guest = new Figure(false, 1))
                using (var staleTowel = new Figure(false, 1))
                {
                    guest.React(stage, 100, false); staleTowel.React(stage, 100, true);
                    guest.Step(120); staleTowel.Step(120);
                    Assert(Quaternion.Angle(guest.Right.localRotation, Quaternion.Euler(-32, -20, -12)) < .2f, "Waiting hands are not held at the waist");
                    Assert(Quaternion.Angle(guest.Left.localRotation, Quaternion.Euler(-32, 20, 12)) < .2f, "Waiting left hand is not at the waist");
                    Same(guest.Right.localRotation, staleTowel.Right.localRotation, "Stale towel flag replaced " + stage + " pose");
                }
        }

        static void RequestAndDelivery()
        {
            using (var guest = new Figure(false, 2))
            {
                Vector3 restBrow = guest.LeftBrow.localPosition;
                Vector3 restMouth = guest.Mouth.localScale;
                guest.React("staying", 100, true); guest.Step(120);
                Assert(Quaternion.Angle(guest.Right.localRotation, Quaternion.Euler(-132, -10, -20)) < 12, "Request did not raise the right hand near the face");
                Assert(guest.LeftBrow.localPosition.y > restBrow.y + .02f, "Request did not raise eyebrows");
                Assert(guest.Mouth.localScale.y > restMouth.y * 1.6f, "Request mouth stayed neutral");
                guest.React("staying", 100, false); guest.Step(120);
                Same(guest.Right.localRotation, Quaternion.Euler(0, 0, -7), "Fulfilled request left the hand raised");
                Near(guest.LeftBrow.localPosition, restBrow, "Fulfilled request left the brow raised");
                Near(guest.Mouth.localScale, restMouth, "Fulfilled request left the mouth distorted");
            }
        }

        static void Dissatisfaction()
        {
            using (var guest = new Figure(false, 3))
            {
                Quaternion restBrow = guest.LeftBrow.localRotation;
                Vector3 restPosition = guest.LeftBrow.localPosition;
                guest.React("staying", 0, false); guest.Step(120);
                Same(guest.LeftBrow.localRotation, Quaternion.Euler(0, 0, -24), "Low satisfaction did not lower the inner left brow");
                Same(guest.RightBrow.localRotation, Quaternion.Euler(0, 0, 24), "Low satisfaction did not lower the inner right brow");
                Same(guest.Mouth.localRotation, Quaternion.Euler(0, 0, -16), "Dissatisfied face stayed neutral");
                Assert(Quaternion.Angle(guest.Right.localRotation, Quaternion.Euler(0, 0, -7)) > 45, "Complaint silhouette is not distinct");
                guest.React("staying", 0, true); guest.Step(120);
                Assert(Quaternion.Angle(guest.Right.localRotation, Quaternion.Euler(-132, -10, -20)) < 12, "Complaint hid the outstanding towel request");
                Same(guest.LeftBrow.localRotation, Quaternion.Euler(0, 0, -24), "Request incorrectly cleared dissatisfaction");
                Near(guest.LeftBrow.localPosition, restPosition, "Angry request retained a cheerful brow raise");
                guest.React("staying", 45, false); guest.Step(150);
                Same(guest.LeftBrow.localRotation, restBrow, "Recovered satisfaction did not restore the seeded face");
                Same(guest.Mouth.localRotation, Quaternion.identity, "Recovered satisfaction left a frown");
            }
        }

        static void StageTransitions()
        {
            foreach (string stage in new[] { "queue", "checkout", "walking", "leaving", "gone", "unknown", null })
                using (var guest = new Figure(false, 0))
                {
                    guest.React("staying", 100, true); guest.Step(90);
                    Quaternion raised = guest.Right.localRotation;
                    guest.React(stage, 100, true); guest.Step(120);
                    Assert(Quaternion.Angle(guest.Right.localRotation, raised) > 65, "Stale request survived stage " + stage);
                }
        }

        static void SmoothTransition()
        {
            using (var guest = new Figure(false, 1))
            {
                guest.Step(30);
                Quaternion before = guest.Right.localRotation;
                guest.React("staying", 100, true);
                Same(before, guest.Right.localRotation, "Applying state snapped a limb before animation");
                guest.Step(1);
                float firstStep = Quaternion.Angle(before, guest.Right.localRotation);
                Assert(firstStep > 1 && firstStep < 20, "One frame did not smoothly begin the gesture: " + firstStep);
                guest.Step(120);
                Assert(Quaternion.Angle(before, guest.Right.localRotation) > 100, "Blending never reached the raised-hand silhouette");
                before = guest.Right.localRotation;
                guest.React("walking", 100, false); guest.Step(1);
                Assert(Quaternion.Angle(before, guest.Right.localRotation) < 20, "Clearing the request snapped the arm");
            }
        }

        static void WalkIsUnchanged()
        {
            using (var neutral = new Figure(false, 2))
            using (var reacting = new Figure(false, 2))
            {
                reacting.React("staying", 0, true);
                neutral.Step(90); reacting.Step(90);
                reacting.React("walking", 0, true);
                for (int i = 0; i < 180; ++i)
                {
                    Vector3 at = new Vector3(1 + i * .022f, 0, 2 + i * .012f);
                    if (i >= 120) at += Vector3.right * 5; // Include existing teleport recovery.
                    neutral.Pose(at, null); reacting.Pose(at, null);
                    neutral.Step(1); reacting.Step(1);
                    Near(neutral.Root.transform.position, reacting.Root.transform.position, "Reaction changed route position");
                    Same(neutral.Root.transform.rotation, reacting.Root.transform.rotation, "Reaction changed route turning");
                    Same(neutral.Bone("leftLeg").localRotation, reacting.Bone("leftLeg").localRotation, "Reaction replaced the left walk cycle");
                    Same(neutral.Bone("rightLeg").localRotation, reacting.Bone("rightLeg").localRotation, "Reaction replaced the right walk cycle");
                }
                Assert(Quaternion.Angle(reacting.Right.localRotation, neutral.Right.localRotation) < 1, "Walking retained a broad stationary gesture");
            }
        }

        static void Employees()
        {
            using (var employee = new Figure(true, 1))
            using (var reference = new Figure(true, 1))
            {
                employee.React("staying", 0, true);
                for (int i = 0; i < 90; ++i)
                {
                    bool carry = i < 45, work = !carry;
                    employee.Pose(new Vector3(1, 0, 2), 37, 28, carry, work);
                    reference.Pose(new Vector3(1, 0, 2), 37, 28, carry, work);
                    employee.Step(1); reference.Step(1);
                    Same(employee.Right.localRotation, reference.Right.localRotation, "Guest reaction changed employee carry/work");
                    Same(employee.Left.localRotation, reference.Left.localRotation, "Guest reaction changed employee left arm");
                    Same(employee.Bone("head").localRotation, reference.Bone("head").localRotation, "Guest reaction changed employee look pitch");
                    Same(employee.Mouth.localRotation, reference.Mouth.localRotation, "Guest reaction changed employee expression");
                }
            }
        }

        static void StableHierarchy()
        {
            using (var guest = new Figure(false, 3))
            {
                Transform[] nodes = guest.Root.GetComponentsInChildren<Transform>(true);
                Renderer[] renderers = guest.Root.GetComponentsInChildren<Renderer>(true);
                Collider[] colliders = guest.Root.GetComponentsInChildren<Collider>(true);
                var capsule = guest.Root.GetComponent<CapsuleCollider>();
                Vector3 center = capsule.center; float radius = capsule.radius, height = capsule.height;
                var target = guest.Root.AddComponent<HotelTarget>(); target.id = "guest_37"; target.label = "Тестер";
                for (int i = 0; i < 300; ++i)
                {
                    guest.React(i % 3 == 0 ? "queue" : "staying", i % 2 == 0 ? 0 : 100, i % 3 == 1);
                    guest.Step(3);
                }
                SameObjects(nodes, guest.Root.GetComponentsInChildren<Transform>(true), "Transforms changed during reactions");
                SameObjects(renderers, guest.Root.GetComponentsInChildren<Renderer>(true), "Renderers changed during reactions");
                SameObjects(colliders, guest.Root.GetComponentsInChildren<Collider>(true), "Colliders changed during reactions");
                Near(capsule.center, center, "Capsule center moved");
                Assert(capsule.radius == radius && capsule.height == height && capsule.enabled && !capsule.isTrigger, "Capsule geometry/flags changed");
                Assert(target.id == "guest_37" && target.label == "Тестер", "Guest target changed");
                Near(guest.Root.transform.position, new Vector3(1, 0, 2), "Stationary reaction moved the interaction root");
                guest.React("staying", 100, false); guest.Step(150);
                Same(guest.Right.localRotation, Quaternion.Euler(0, 0, -7), "Long reaction sequence accumulated rotation drift");
            }
        }

        static void WorldIntegration()
        {
            var host = new GameObject("HotelFigureTests / isolated world");
            try
            {
                HotelWorld world = host.AddComponent<HotelWorld>();
                var guest = new GuestState { id = 37, name = "Тестер", room = 101, stage = "queue", position = HotelLayout.RoomCenter(101) };
                var state = new HotelState(); state.guests.Add(guest);
                for (int n = 101; n <= 104; ++n) state.rooms.Add(new RoomState { number = n });
                world.Apply(state, 0);
                HotelTarget target = null;
                foreach (HotelTarget candidate in host.GetComponentsInChildren<HotelTarget>(true))
                    if (candidate.id == "guest_37") { target = candidate; break; }
                Assert(target != null, "World did not create the guest interaction target");
                GameObject root = target.gameObject;
                Component rig = root.GetComponent(FigureType);
                Transform arm = (Transform)FigureType.GetField("rightArm", PrivateInstance).GetValue(rig);
                int count = host.GetComponentsInChildren<Transform>(true).Length;
                guest.stage = "staying"; guest.towelRequested = true; guest.satisfaction = 0;
                string before = JsonUtility.ToJson(state);
                for (int i = 0; i < 120; ++i)
                {
                    world.Apply(state, 0);
                    AnimateMethod.Invoke(rig, new object[] { 1f / 60, i / 60f });
                }
                Assert(JsonUtility.ToJson(state) == before, "Presentation mutated the authoritative snapshot");
                Assert(host.GetComponentsInChildren<Transform>(true).Length == count, "World.Apply rebuilt guest visuals");
                Assert(root.GetComponent<HotelTarget>() == target && target.id == "guest_37", "World.Apply replaced the guest target");
                Assert(Quaternion.Angle(arm.localRotation, Quaternion.Euler(-132, -10, -20)) < 12, "World.Apply did not feed the outstanding request to the rig");
                guest.stage = "gone"; world.Apply(state, 0);
                Assert(root == null || !root.activeSelf, "Gone guest retained an active reaction/collider");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        sealed class Figure : IDisposable
        {
            internal readonly GameObject Root;
            readonly Component rig;
            float clock;
            internal Figure(bool employee, int seed)
            {
                Root = HotelWorld.MakePerson(employee, seed); rig = Root.GetComponent(FigureType);
                Pose(new Vector3(1, 0, 2), 0);
            }
            internal Transform Bone(string name) => (Transform)FigureType.GetField(name, PrivateInstance).GetValue(rig);
            internal Transform Left => Bone("leftArm");
            internal Transform Right => Bone("rightArm");
            internal Transform LeftBrow => Bone("leftBrow");
            internal Transform RightBrow => Bone("rightBrow");
            internal Transform Mouth => Bone("mouth");
            internal void React(string stage, float satisfaction, bool towel) => ReactMethod.Invoke(rig, new object[] { stage, satisfaction, towel });
            internal void Pose(Vector3 position, float? yaw, float pitch = 0, bool carry = false, bool work = false)
                => PoseMethod.Invoke(rig, new object[] { position, yaw, pitch, carry, work });
            internal void Step(int frames)
            {
                for (int i = 0; i < frames; ++i)
                {
                    clock += 1f / 60;
                    AnimateMethod.Invoke(rig, new object[] { 1f / 60, clock });
                }
            }
            public void Dispose() { UnityEngine.Object.DestroyImmediate(Root); }
        }

        static void SameObjects<T>(T[] before, T[] after, string message) where T : UnityEngine.Object
        {
            Assert(before.Length == after.Length, message);
            for (int i = 0; i < before.Length; ++i) Assert(before[i] == after[i], message);
        }
        static void Near(Vector3 a, Vector3 b, string message) { Assert((a - b).sqrMagnitude < .00000001f, message); }
        static void Same(Quaternion a, Quaternion b, string message) { Assert(Quaternion.Angle(a, b) < .2f, message); }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Run(List<string> passed, string name, Action check)
        {
            try { check(); passed.Add(name); }
            catch (Exception error) { throw new Exception("HotelFigureTests FAILED: " + name, error); }
        }
    }
}
