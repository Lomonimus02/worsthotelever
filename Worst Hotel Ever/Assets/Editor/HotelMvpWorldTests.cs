using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace WorstHotel
{
    /// <summary>Bounded native edit-mode tests; parent owns running Unity and visual acceptance.</summary>
    public static class HotelMvpWorldTests
    {
        public static List<string> RunAll()
        {
            var passed = new List<string>();
            using (var fixture = new WorldFixture())
            {
                Run(passed, fixture, "MVP world: six shells, clear corridor and authoritative target roots", Geometry);
                Run(passed, fixture, "MVP world: physical purchase barriers unlock without rebuilding", Ownership);
                Run(passed, fixture, "MVP world: all six door-to-centre capsule routes remain open", Routes);
                Run(passed, fixture, "MVP world: legacy snapshots relock the expansion and restore legacy fixtures", Legacy);
                Run(passed, fixture, "MVP world: local faults and shared utilities remain independent", EquipmentAndUtilities);
                Run(passed, fixture, "MVP world: floor dirt, dirty towels, bin fill and residual water are separate", Hygiene);
                Run(passed, fixture, "MVP world: capacity, furniture quality, linen and finish are visible", Furnishing);
                Run(passed, fixture, "MVP world: coffee source and fulfilled current-guest service", Coffee);
                Run(passed, fixture, "MVP world: item size, condition, targets, local hiding and held colliders", Items);
                Run(passed, fixture, "MVP world: repeated snapshots are read-only and retain hierarchy identities", StableProjection);
            }
            RunFactoryChecks(); passed.Add("MVP world: portable plunger/coffee/dirty textile and large bag factories");
            return passed;
        }

        static HotelState State()
        {
            var state = new HotelState { version = 2, contentVersion = 2, mvp = new MvpHotelState() };
            for (int n = 101; n <= 106; ++n)
            {
                var room = new RoomState { number = n, mvp = new MvpRoomState { owned = n <= 104, capacity = n % 2 == 0 ? 2 : 1 } };
                foreach (string kind in new[] { "sink", "toilet", "tv", "lamp" })
                    room.mvp.equipment.Add(new MvpEquipmentState { kind = kind });
                state.rooms.Add(room);
            }
            return state;
        }

        static void Geometry(WorldFixture f, HotelState state)
        {
            for (int n = 101; n <= 106; ++n)
            {
                Vector3 expected = new Vector3(n % 2 == 0 ? 4.7f : -4.7f, 0, 5.5f + ((n - 101) / 2) * 7);
                Near(HotelLayout.RoomCenter(n), expected, "Room centre moved");
                Transform furniture = f.Named("Room " + n + " / furnishings");
                Assert(furniture != null, "Missing furnished room " + n);
                foreach (string kind in new[] { "toilet", "tv", "lamp", "sink" })
                {
                    Transform target = f.Named("Equipment status " + kind + "_" + n);
                    Near(target.position, HotelLayout.RoomTarget(kind, n), "Wrong equipment target " + kind + n);
                    Assert(target.GetComponent<HotelTarget>().id == kind + "_" + n, "Equipment target identity changed");
                    CheckColliders(target, kind + "_" + n);
                }
                foreach (string kind in new[] { "coffee", "clean", "dirtytowel" })
                {
                    string name = kind == "coffee" ? "Coffee service " : kind == "clean" ? "Floor cleaning " : "Dirty towel collection ";
                    Transform target = f.Named(name + n);
                    Near(target.position, HotelLayout.RoomTarget(kind, n), "Wrong service target " + kind + n);
                    CheckColliders(target, kind + "_" + n);
                }
            }
            Near(f.Named("Water main").position, HotelLayout.Target("utility_water"), "Water station moved");
            Near(f.Named("Power distribution").position, HotelLayout.Target("utility_power"), "Power station moved");
            Near(f.Named("Lobby coffee station").position, HotelLayout.Target("coffee"), "Coffee source moved");
            Assert(!f.HasName("Future elevator surround") && !f.HasName("Future lift left"), "Old lift still blocks expansion");
            Physics.SyncTransforms();
            Assert(!Blocked(f, new Vector3(0, 0, 2.4f), new Vector3(0, 0, 22.4f), out _), "Corridor is obstructed");
            Assert(f.PhysicsWorld.Raycast(new Vector3(0, 1.4f, 22), Vector3.forward, out RaycastHit back, 2, ~0, QueryTriggerInteraction.Ignore), "North wall is missing");
            Assert(back.point.z >= 22.98f && back.point.z <= 23.12f, "North boundary is not at z23.1");
        }

        static void Ownership(WorldFixture f, HotelState state)
        {
            Transform door = f.Named("Locked door 105"); int doorId = door.GetInstanceID();
            int count = f.Host.GetComponentsInChildren<Transform>(true).Length;
            Physics.SyncTransforms();
            Assert(Blocked(f, new Vector3(0, 0, 19.5f), HotelLayout.RoomCenter(105), out RaycastHit hit), "Unowned room has no physical guard");
            Assert(hit.collider.GetComponentInParent<HotelTarget>().id == "door_105", "Unexpected purchase barrier target");
            Assert(!f.Named("Room 105 / furnishings").gameObject.activeInHierarchy, "Unowned furniture is active");
            state.rooms[4].mvp.owned = true; f.Apply(state);
            Assert(!door.gameObject.activeSelf && f.Named("Room 105 / furnishings").gameObject.activeInHierarchy, "Purchase did not activate furniture and open passage");
            Physics.SyncTransforms();
            Assert(!Blocked(f, new Vector3(0, 0, 19.5f), HotelLayout.RoomCenter(105), out _), "Purchased room remains blocked");
            Assert(door.GetInstanceID() == doorId && f.Host.GetComponentsInChildren<Transform>(true).Length == count, "Purchase rebuilt room geometry");
            Assert(f.Named("Locked door 106").gameObject.activeSelf, "Buying 105 unlocked 106");
        }

        static void Routes(WorldFixture f, HotelState state)
        {
            foreach (RoomState room in state.rooms) room.mvp.owned = true;
            f.Apply(state); Physics.SyncTransforms();
            foreach (RoomState room in state.rooms)
            {
                Vector3 center = HotelLayout.RoomCenter(room.number);
                Assert(!Blocked(f, new Vector3(0, 0, center.z), center, out RaycastHit hit), "Room route blocked for " + room.number + ": " + (hit.collider == null ? "unknown" : hit.collider.name));
            }
        }

        static void Legacy(WorldFixture f, HotelState state)
        {
            foreach (RoomState room in state.rooms) room.mvp.owned = true;
            f.Apply(state);
            var legacy = new HotelState();
            for (int n = 101; n <= 104; ++n) legacy.rooms.Add(new RoomState { number = n });
            f.Apply(legacy);
            Assert(f.Named("Locked door 105").gameObject.activeSelf && f.Named("Locked door 106").gameObject.activeSelf, "Legacy snapshot left expansion open");
            Assert(!f.Named("MVP service stations").gameObject.activeSelf, "Legacy snapshot retained MVP service commands");
            Assert(!f.Named("MVP room fixtures 101").gameObject.activeSelf, "Legacy room retained MVP fixtures");
            Assert(f.Child(f.Named("Room 101 / furnishings"), "Legacy room fixtures").gameObject.activeSelf, "Legacy fixtures were not restored");
        }

        static void EquipmentAndUtilities(WorldFixture f, HotelState state)
        {
            RoomState room = state.rooms[0];
            room.leak = true; room.water = .8f;
            Equipment(room, "sink").localFault = true; Equipment(room, "tv").localFault = true;
            Equipment(room, "toilet").wear = .95f; Equipment(room, "toilet").localFault = true; Equipment(room, "lamp").localFault = true;
            state.mvp.utilities.powerFault = true; state.mvp.utilities.waterFault = true; f.Apply(state);
            Assert(f.Child(f.Named("Equipment status sink_101"), "Local fault").gameObject.activeSelf, "Water outage erased local sink fault");
            Assert(f.Child(f.Named("Equipment status tv_101"), "Local fault").gameObject.activeSelf, "Power outage erased local TV fault");
            Assert(f.Child(f.Named("Equipment status tv_102"), "Utility unavailable").gameObject.activeSelf, "Shared power outage did not reach another room");
            Assert(!f.Child(f.Named("Room 101 / conditions"), "Visible leak").gameObject.activeSelf, "Cut water still gushes from sink");
            Assert(f.Named("Puddle 101").gameObject.activeSelf, "Utility cutoff erased residual water");
            Assert(f.Child(f.Named("Equipment status toilet_101"), "Wear mark 2").gameObject.activeSelf, "High wear is not visible");
            Assert(f.Child(f.Named("Equipment status toilet_101"), "Blocked toilet contents").gameObject.activeInHierarchy, "Toilet fault has no blocked-bowl state");
            state.mvp.utilities.powerFault = false; state.mvp.utilities.waterFault = false; f.Apply(state);
            Assert(f.Child(f.Named("Equipment status tv_101"), "Local fault").gameObject.activeSelf, "Utility repair magically fixed TV");
            Assert(!f.Child(f.Named("Equipment status tv_102"), "Utility unavailable").gameObject.activeSelf, "Utility repair did not restore unaffected room");
            Assert(f.Child(f.Named("Room 101 / conditions"), "Visible leak").gameObject.activeSelf, "Restored water did not restore unresolved sink leak");
            Assert(f.Child(f.Named("Room lamp 101"), "Warm bulb").GetComponent<Renderer>().sharedMaterial.name.EndsWith("Dark"), "Power repair magically fixed the locally broken lamp");
            Assert(f.Child(f.Named("Room lamp 102"), "Warm bulb").GetComponent<Renderer>().sharedMaterial.name.EndsWith("Glow"), "Power repair failed to light healthy lamp");
            Equipment(room, "lamp").installed = false; f.Apply(state);
            Assert(!f.Named("Room lamp 101").gameObject.activeSelf, "Missing lamp still has a physical model");
            Assert(f.Child(f.Named("Equipment status lamp_101"), "Empty equipment slot").gameObject.activeSelf, "Missing lamp has no service marker");
        }

        static void Hygiene(WorldFixture f, HotelState state)
        {
            RoomState room = state.rooms[0]; room.mvp.dirt = 1; room.mvp.dirtyTowels = 3; room.mvp.binFill = 1; room.water = .7f;
            f.Apply(state);
            Transform dirt = f.Named("Floor cleaning 101"), towels = f.Named("Dirty towel collection 101"), overflow = f.Named("Bin overflow 101");
            Assert(f.Child(dirt, "Dry dirt patch 9").gameObject.activeSelf, "Dry dirt patches are missing");
            Assert(f.Child(towels, "Used towel 2").gameObject.activeSelf, "Dirty towels are missing");
            Assert(f.Child(overflow, "Overflow litter 2").gameObject.activeSelf, "Full bin has no overflow");
            room.mvp.dirt = 0; f.Apply(state);
            Assert(!f.Child(dirt, "Dry dirt patch 0").gameObject.activeSelf, "Clean floor still has dry dirt");
            Assert(f.Child(towels, "Used towel 0").gameObject.activeSelf && f.Named("Puddle 101").gameObject.activeSelf, "Floor cleaning erased unrelated towels/water");
            room.mvp.dirtyTowels = 0; room.mvp.binFill = 0; room.water = 0; f.Apply(state);
            Assert(!f.Child(towels, "Used towel 0").gameObject.activeSelf && !f.Child(overflow, "Overflow litter 0").gameObject.activeSelf && !f.Named("Puddle 101").gameObject.activeSelf, "Independent cleanup did not clear visuals");
        }

        static void Furnishing(WorldFixture f, HotelState state)
        {
            Transform single = f.Child(f.Named("Bed 101"), "Capacity geometry"), doubleBed = f.Child(f.Named("Bed 102"), "Capacity geometry");
            Assert(single.localScale == Vector3.one && doubleBed.localScale.z > 1.5f, "Capacity does not change physical bed size");
            Near(f.Named("Bed 102").position, HotelLayout.RoomTarget("bed", 102), "Double capacity moved the authoritative bed root");
            RoomState room = state.rooms[0]; room.mvp.bedQuality = 2; room.mvp.tvQuality = 2; room.mvp.finishId = "cool"; state.mvp.betterLinen = true; f.Apply(state);
            Transform conditions = f.Named("Room 101 / conditions");
            Assert(f.Child(conditions, "Bed upgrade / brass finials").gameObject.activeSelf, "Better bed has no visible upgrade");
            Assert(f.Child(conditions, "Upgraded TV trim").gameObject.activeSelf, "Better TV has no visible upgrade");
            Assert(f.Child(conditions, "Premium linen embroidery").gameObject.activeSelf, "Better linen has no visible upgrade");
            Assert(f.Child(conditions, "Selectable wall finish").GetComponent<Renderer>().sharedMaterial.name.EndsWith("Blue"), "Cool finish did not change wall material");
            room.mvp.finishId = "warm"; f.Apply(state);
            Assert(f.Child(conditions, "Selectable rug inset").GetComponent<Renderer>().sharedMaterial.name.EndsWith("Red"), "Warm finish did not change rug material");
        }

        static void Coffee(WorldFixture f, HotelState state)
        {
            Assert(f.Named("Manual coffee service").gameObject.activeSelf, "Baseline coffee is unavailable before upgrade");
            state.mvp.coffeeMachine = true; state.mvp.coffeeStock = 0; f.Apply(state);
            Assert(f.Named("Upgraded coffee machine").gameObject.activeSelf && !f.Named("Manual coffee service").gameObject.activeSelf, "Coffee upgrade is not visible");
            RoomState room = state.rooms[0]; room.guestId = 8;
            state.mvp.requests.Add(new MvpRequestState { guestId = 8, kind = "coffee", status = "open" }); f.Apply(state);
            Transform cup = f.Child(f.Named("Coffee service 101"), "Delivered coffee");
            Assert(!cup.gameObject.activeSelf, "Unfulfilled request displayed a delivered cup");
            state.mvp.requests[0].status = "fulfilled"; f.Apply(state);
            Assert(cup.gameObject.activeSelf, "Fulfilled coffee is not on the guest table");
            room.guestId = 9; f.Apply(state);
            Assert(!cup.gameObject.activeSelf, "Previous guest's coffee request leaked into new occupancy");
        }

        static void Items(WorldFixture f, HotelState state)
        {
            var bag = new ItemState { id = "test_bag", kind = "bag", size = "large", position = new Vector3(2.7f, .25f, -4) };
            var towel = new ItemState { id = "test_towel", kind = "towel", condition = "dirty", position = new Vector3(3.5f, .25f, -4) };
            state.items.Add(bag); state.items.Add(towel); state.players.Add(new PlayerState { id = 0, position = HotelLayout.Spawn }); f.Apply(state);
            Transform bagRoot = f.Named("test_bag / bag"), towelRoot = f.Named("test_towel / towel"); int identity = bagRoot.GetInstanceID();
            Assert(bagRoot.localScale.y > 1.3f, "Large suitcase is not larger");
            Assert(towelRoot.GetComponent<HotelTarget>().label.Contains("Грязное"), "Dirty towel target says clean");
            CheckColliders(bagRoot, "test_bag"); CheckColliders(towelRoot, "test_towel");
            bag.holder = 0; f.Apply(state);
            Assert(!bagRoot.gameObject.activeSelf && !bagRoot.GetComponent<Collider>().enabled, "Local held bag is visible or collidable");
            bag.holder = 1; state.players.Add(new PlayerState { id = 1, position = new Vector3(1.8f, 0, -3) }); f.Apply(state);
            Assert(bagRoot.gameObject.activeSelf && !bagRoot.GetComponent<Collider>().enabled, "Remote held bag disappeared or kept collider");
            bag.holder = -1; bag.size = "small"; towel.condition = "clean"; f.Apply(state);
            Assert(bagRoot.GetInstanceID() == identity && bagRoot.localScale == Vector3.one && bagRoot.GetComponent<Collider>().enabled, "Drop/size update rebuilt bag or failed collider restoration");
            Assert(towelRoot.GetComponent<HotelTarget>().label.Contains("Чистое"), "Clean towel retained dirty label");
        }

        static void StableProjection(WorldFixture f, HotelState state)
        {
            foreach (RoomState room in state.rooms) room.mvp.owned = true;
            state.rooms[1].mvp.dirt = .7f; state.rooms[1].mvp.dirtyTowels = 2; f.Apply(state);
            Transform[] before = f.Host.GetComponentsInChildren<Transform>(true);
            Collider[] colliders = f.Host.GetComponentsInChildren<Collider>(true);
            string json = JsonUtility.ToJson(state);
            for (int i = 0; i < 160; ++i) f.Apply(state);
            Assert(json == JsonUtility.ToJson(state), "World projection mutated state");
            SameObjects(before, f.Host.GetComponentsInChildren<Transform>(true)); SameObjects(colliders, f.Host.GetComponentsInChildren<Collider>(true));
            f.World.Build(); SameObjects(before, f.Host.GetComponentsInChildren<Transform>(true));
        }

        static void RunFactoryChecks()
        {
            foreach (string kind in new[] { "plunger", "coffee", "coffeecup", "dirtytowel", "towel" })
            {
                GameObject item = HotelWorld.MakeItem(kind, "small", kind == "towel" ? "dirty" : "clean");
                try
                {
                    Assert(item.GetComponent<Collider>() != null && item.GetComponentsInChildren<Renderer>().Length > 2, "Factory failed for " + kind);
                    CheckColliders(item.transform, kind);
                }
                finally { UnityEngine.Object.DestroyImmediate(item); }
            }
            GameObject small = HotelWorld.MakeItem("bag"), large = HotelWorld.MakeItem("bag", "large", "clean");
            try { Assert(large.transform.localScale.y > small.transform.localScale.y, "Carry factory lost large-bag size"); }
            finally { UnityEngine.Object.DestroyImmediate(small); UnityEngine.Object.DestroyImmediate(large); }
        }

        static MvpEquipmentState Equipment(RoomState room, string kind) => room.mvp.equipment.Find(e => e.kind == kind);
        static bool Blocked(WorldFixture fixture, Vector3 from, Vector3 to, out RaycastHit hit)
        {
            // Exclude the 15 cm step band; paper decals/cleaning proxies are beneath controller stepOffset.
            Vector3 direction = to - from;
            return fixture.PhysicsWorld.CapsuleCast(from + Vector3.up * .43f, from + Vector3.up * 1.47f, .28f,
                direction.normalized, out hit, direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
        }
        static void CheckColliders(Transform root, string id)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true); Assert(colliders.Length > 0, "No collider for " + id);
            foreach (Collider collider in colliders)
                Assert(collider.GetComponent<HotelTarget>() != null && collider.GetComponent<HotelTarget>().id == id, "Collider lost target " + id);
        }
        static void SameObjects<T>(T[] a, T[] b) where T : UnityEngine.Object
        {
            Assert(a.Length == b.Length, "Hierarchy count changed");
            for (int i = 0; i < a.Length; ++i) Assert(a[i] == b[i], "Hierarchy identity changed");
        }
        static void Near(Vector3 a, Vector3 b, string message) { Assert((a - b).sqrMagnitude < .000001f, message); }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Run(List<string> passed, WorldFixture fixture, string name, Action<WorldFixture, HotelState> test)
        {
            try { HotelState state = State(); fixture.Apply(state); test(fixture, state); passed.Add(name); }
            catch (Exception error) { throw new Exception("HotelMvpWorldTests FAILED: " + name, error); }
        }
        sealed class WorldFixture : IDisposable
        {
            internal readonly GameObject Host = new GameObject("HotelMvpWorldTests / isolated world");
            internal readonly HotelWorld World;
            internal readonly PhysicsScene PhysicsWorld;
            readonly Scene scene;
            internal WorldFixture()
            {
                scene = SceneManager.CreateScene("HotelMvpWorldTests-" + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                try
                {
                    SceneManager.MoveGameObjectToScene(Host, scene); PhysicsWorld = scene.GetPhysicsScene();
                    World = Host.AddComponent<HotelWorld>(); World.Build();
                }
                catch { Dispose(); throw; }
            }
            internal void Apply(HotelState state) { World.Apply(state, 0); }
            internal Transform Named(string name) => Child(Host.transform, name);
            internal bool HasName(string name)
            {
                foreach (Transform t in Host.GetComponentsInChildren<Transform>(true)) if (t.name == name) return true;
                return false;
            }
            internal Transform Child(Transform root, string name)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
                throw new Exception("Missing visual: " + name);
            }
            public void Dispose()
            {
                if (Host != null) UnityEngine.Object.DestroyImmediate(Host);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
