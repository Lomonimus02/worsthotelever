using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorstHotel
{
    // Parent runner calls RunAll. Source/structure checks, not subjective visual acceptance.
    public static class HotelTextureTests
    {
        static readonly string[] Files = { "wall-plaster", "wood", "carpet", "cloth", "metal", "ceramic",
            "rubber", "paper", "skin", "water", "tablet-casing", "tablet-screen" };
        static readonly string[] Items = { "bag", "toolbox", "mop", "linen", "towel", "dirtylinen",
            "dirtytowel", "trashbag", "cart", "plunger", "coffee", "coffeecup" };
        const string ResourceRoot = "Hotel/Textures/v1/";

        public static List<string> RunAll()
        {
            var passed = new List<string>();
            Sources(); passed.Add("GRIM_EDITOR_12_DISTINCT_SOURCE_PNGS_AND_RESOURCES");
            ImportPolicy(); passed.Add("GRIM_EDITOR_IMPORT_POLICY");
            MaterialCache(); passed.Add("GRIM_EDITOR_500_KEY_CACHE_STABLE_FINITE");
            Factories(); passed.Add("GRIM_EDITOR_FACTORY_SURFACES_AND_NONDEGENERATE_UV0");
            using (var fixture = new WorldFixture())
            {
                Signals(fixture); passed.Add("GRIM_EDITOR_REAL_CLEAN_DIRTY_AND_EQUIPMENT_SIGNALS");
                Projection(fixture); passed.Add("GRIM_EDITOR_WORLD_TEXTURES_STATE_GEOMETRY_STABLE");
            }
            return passed;
        }

        static void Sources()
        {
            Assert(Enum.GetValues(typeof(HotelTextureId)).Length == Files.Length, "Texture manifest changed");
            var instances = new HashSet<Texture2D>();
            var sourceHashes = new HashSet<string>();
            var decodedHashes = new HashSet<string>();
            for (int i = 0; i < Files.Length; ++i)
            {
                var id = (HotelTextureId)i;
                Assert(HotelTextureLibrary.SourceFileName(id) == Files[i] + ".png", "Wrong filename: " + id);
                Texture2D texture = HotelTextureLibrary.GetTexture(id);
                Assert(texture != null && texture != Texture2D.whiteTexture && texture != Texture2D.blackTexture &&
                    texture.width >= 64 && texture.height >= 64, "Missing/fallback texture: " + id);
                Assert(instances.Add(texture), "Texture reused across families: " + id);
                Assert(ReferenceEquals(texture, HotelTextureLibrary.GetTexture(id)) &&
                    texture == Resources.Load<Texture2D>(ResourceRoot + Files[i]), "Resource identity: " + id);
                string path = AssetDatabase.GetAssetPath(texture);
                Assert(path.Replace('\\', '/').EndsWith("/Resources/" + ResourceRoot + Files[i] + ".png",
                    StringComparison.Ordinal), "Texture is not its source asset: " + path);
                byte[] bytes = File.ReadAllBytes(path);
                using (var sha = SHA256.Create())
                    Assert(sourceHashes.Add(Convert.ToBase64String(sha.ComputeHash(bytes))), "Duplicate source PNG: " + id);

                // Decode the actual disk PNG into a disposable editor-only copy. Never enable
                // readability on the imported resource, invent samples, or call SetPixels.
                var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    Assert(ImageConversion.LoadImage(decoded, bytes, false), "Cannot decode source: " + path);
                    Color32[] pixels = decoded.GetPixels32();
                    var colors = new HashSet<uint>();
                    int low = 765, high = 0;
                    var rgba = new byte[pixels.Length * 4];
                    for (int p = 0; p < pixels.Length; ++p)
                    {
                        Color32 c = pixels[p];
                        rgba[p * 4] = c.r; rgba[p * 4 + 1] = c.g; rgba[p * 4 + 2] = c.b; rgba[p * 4 + 3] = c.a;
                        if (colors.Count < 32) colors.Add((uint)(c.r | c.g << 8 | c.b << 16 | c.a << 24));
                        int sum = c.r + c.g + c.b; low = Math.Min(low, sum); high = Math.Max(high, sum);
                    }
                    Assert(colors.Count >= 8 && high - low >= 6, "Flat source rather than surface detail: " + id);
                    using (var sha = SHA256.Create())
                        Assert(decodedHashes.Add(Convert.ToBase64String(sha.ComputeHash(rgba))), "Duplicate decoded pixels: " + id);
                }
                finally { UnityEngine.Object.DestroyImmediate(decoded); }
            }
        }

        static void ImportPolicy()
        {
            for (int i = 0; i < Files.Length; ++i)
            {
                Texture2D texture = HotelTextureLibrary.GetTexture((HotelTextureId)i);
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                Assert(importer != null, "No texture importer: " + Files[i]);
                bool world = i < 10;
                TextureWrapMode wrap = world ? TextureWrapMode.Mirror : i == 10 ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                int cap = world ? 512 : 1024;
                Assert(importer.textureType == TextureImporterType.Default && importer.sRGBTexture &&
                    !importer.isReadable && !texture.isReadable, "Default/sRGB/unreadable policy: " + Files[i]);
                Assert(importer.mipmapEnabled == world && (texture.mipmapCount > 1) == world,
                    "Mip policy: " + Files[i]);
                Assert(importer.wrapModeU == wrap && importer.wrapModeV == wrap &&
                    texture.wrapModeU == wrap && texture.wrapModeV == wrap, "Wrap policy: " + Files[i]);
                Assert(importer.maxTextureSize == cap && texture.width <= cap && texture.height <= cap,
                    "Texture size cap: " + Files[i]);
                var standalone = importer.GetPlatformTextureSettings("Standalone");
                Assert(!standalone.overridden || standalone.maxTextureSize <= cap, "Standalone override exceeds cap: " + Files[i]);
            }
        }

        static void MaterialCache()
        {
            var materials = new HashSet<Material>();
            foreach (HotelSurfaceFamily family in Enum.GetValues(typeof(HotelSurfaceFamily)))
                foreach (HotelInk ink in Enum.GetValues(typeof(HotelInk)))
                    foreach (HotelMappingMode mapping in Enum.GetValues(typeof(HotelMappingMode)))
                    {
                        Material m = HotelTextureLibrary.GetMaterial(family, ink, mapping);
                        Assert(materials.Add(m), "Distinct material keys alias: " + family + "/" + ink + "/" + mapping);
                        CheckMaterial(m);
                        Assert(m.GetTexture("_BaseMap") == HotelTextureLibrary.GetTexture((HotelTextureId)family), "Wrong physical family");
                        Assert(m.GetFloat("_MappingMode") == (float)mapping, "Wrong projection");
                        Assert(m.GetColor("_BaseColor") == HotelTextureLibrary.ColorOf(ink), "Tint changed: " + ink);
                    }
            Assert(materials.Count == 500, "Expected bounded 10 x 25 x 2 material domain");
            int live = SurfaceMaterialCount();
            for (int repeat = 0; repeat < 4; ++repeat)
                foreach (HotelSurfaceFamily family in Enum.GetValues(typeof(HotelSurfaceFamily)))
                    foreach (HotelInk ink in Enum.GetValues(typeof(HotelInk)))
                        foreach (HotelMappingMode mapping in Enum.GetValues(typeof(HotelMappingMode)))
                            Assert(materials.Contains(HotelTextureLibrary.GetMaterial(family, ink, mapping)), "Warm cache allocated a material");
            Reject(() => HotelTextureLibrary.GetTexture((HotelTextureId)(-1)));
            Reject(() => HotelTextureLibrary.SourceFileName((HotelTextureId)12));
            Reject(() => HotelTextureLibrary.GetMaterial((HotelSurfaceFamily)10, HotelInk.White, HotelMappingMode.UV0));
            Reject(() => HotelTextureLibrary.GetMaterial(HotelSurfaceFamily.Wood, (HotelInk)25, HotelMappingMode.UV0));
            Reject(() => HotelTextureLibrary.GetMaterial(HotelSurfaceFamily.Wood, HotelInk.White, (HotelMappingMode)2));
            Assert(SurfaceMaterialCount() == live, "Warm/invalid requests grew the cache");
        }

        static int SurfaceMaterialCount()
        {
            int count = 0;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                if (m.shader != null && m.shader.name == "WorstHotel/HotelSurface") ++count;
            return count;
        }

        static void Factories()
        {
            var meshes = new HashSet<Mesh>();
            foreach (string kind in Items)
            {
                GameObject root = HotelWorld.MakeItem(kind);
                try { Audit(root, true, meshes); }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            foreach (bool employee in new[] { false, true })
            {
                GameObject root = HotelWorld.MakePerson(employee, 3);
                try { Audit(root, true, meshes); }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            Assert(meshes.Count >= 5, "Factories did not cover the five procedural mesh shapes");
            foreach (Mesh mesh in meshes) CheckUV(mesh);
        }

        static void CheckUV(Mesh mesh)
        {
            Assert(mesh.isReadable, "Factory mesh cannot be inspected: " + mesh.name);
            Vector2[] uv = mesh.uv;
            Assert(uv.Length == mesh.vertexCount && uv.Length > 0, "Missing UV0: " + mesh.name);
            foreach (Vector2 p in uv) Assert(Finite(p.x) && Finite(p.y), "Nonfinite UV: " + mesh.name);
            int[] triangles = mesh.triangles; Vector3[] vertices = mesh.vertices;
            int geometric = 0, mapped = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude < 1e-12f) continue;
                ++geometric;
                Vector2 ab = uv[b] - uv[a], ac = uv[c] - uv[a];
                if (Mathf.Abs(ab.x * ac.y - ab.y * ac.x) > 1e-8f) ++mapped;
            }
            Assert(geometric > 0 && mapped == geometric, "Collapsed UV triangles: " + mesh.name + " " + mapped + "/" + geometric);
        }

        static void Signals(WorldFixture f)
        {
            HotelState state = HotelSimulation.CreateNewDanger(712).State;
            RoomState room = state.rooms.Find(r => r.number == 101);
            room.bed = 2; room.mvp.dirt = 0; room.mvp.dirtyTowels = 0; room.mvp.binFill = 0;
            var equipment = room.mvp.equipment.Find(e => e.kind == "sink");
            equipment.installed = true; equipment.localFault = false; room.leak = false;
            HotelSaveStore.Validate(state); f.World.Apply(state, 0);
            Transform bed = Find(f.Root.transform, "Bed 101");
            Transform clean = Find(bed, "Clean bedding"), dirty = Find(bed, "Dirty bedding");
            Assert(clean.gameObject.activeInHierarchy && !dirty.gameObject.activeSelf, "Clean bed projection missing");
            Material cleanFabric = Find(clean, "Clean duvet").GetComponent<Renderer>().sharedMaterial;
            Material dirtyFabric = Find(dirty, "Crumpled sheet").GetComponent<Renderer>().sharedMaterial;
            Assert(cleanFabric.GetTexture("_BaseMap") == HotelTextureLibrary.GetTexture(HotelTextureId.Cloth) &&
                dirtyFabric.GetTexture("_BaseMap") == cleanFabric.GetTexture("_BaseMap"), "Bed condition changed physical family");
            Assert(ColorDistance(cleanFabric, dirtyFabric) > .2f, "Clean/dirty bed lost its tint signal");
            Transform panel = Find(f.Root.transform, "Equipment status sink_101");
            var indicator = Find(panel, "Equipment indicator").GetComponent<Renderer>();
            Material ready = indicator.sharedMaterial;
            string readyLabel = Find(panel, "Service indicator").GetComponentInChildren<TextMesh>(true).text;
            room.bed = 1; room.mvp.dirt = .8f; room.mvp.dirtyTowels = 2; room.mvp.binFill = .9f;
            equipment.localFault = true; room.leak = true;
            string snapshot = JsonUtility.ToJson(state);
            f.World.Apply(state, 0);
            Assert(snapshot == JsonUtility.ToJson(state), "Signal projection mutated state");
            Assert(!clean.gameObject.activeSelf && dirty.gameObject.activeInHierarchy, "Dirty bed projection missing");
            Assert(Find(panel, "Local fault").gameObject.activeInHierarchy &&
                ColorDistance(ready, indicator.sharedMaterial) > .2f &&
                readyLabel != Find(panel, "Service indicator").GetComponentInChildren<TextMesh>(true).text,
                "Fault lost its separate geometry, tint or actual label");
            Assert(ready.GetTexture("_BaseMap") == indicator.sharedMaterial.GetTexture("_BaseMap"), "Signal changed physical family");
            Assert(Find(Find(f.Root.transform, "Floor cleaning 101"), "Dry dirt patch 0").gameObject.activeInHierarchy,
                "Dirt has no visible production geometry");
            GameObject a = HotelWorld.MakeItem("towel", "small", "clean"), b = HotelWorld.MakeItem("towel", "small", "dirty");
            try
            {
                Material ca = Find(a.transform, "Folded textile").GetComponent<Renderer>().sharedMaterial;
                Material cb = Find(b.transform, "Folded textile").GetComponent<Renderer>().sharedMaterial;
                Assert(ColorDistance(ca, cb) > .2f && ca.GetTexture("_BaseMap") == cb.GetTexture("_BaseMap"), "Laundry condition lost its material signal");
                Assert(a.GetComponent<HotelTarget>().label != b.GetComponent<HotelTarget>().label &&
                    Find(b.transform, "Laundry stain") != null, "Laundry condition lost its label/stain geometry");
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
            HotelSaveStore.Validate(state);
        }

        static void Projection(WorldFixture f)
        {
            HotelState state = HotelSimulation.CreateNewDanger(712).State;
            f.World.Apply(state, 0);
            string stateBefore = JsonUtility.ToJson(state);
            Transform[] nodes = f.Root.GetComponentsInChildren<Transform>(true);
            MeshFilter[] filters = f.Root.GetComponentsInChildren<MeshFilter>(true);
            Collider[] colliders = f.Root.GetComponentsInChildren<Collider>(true);
            var positions = new Vector3[nodes.Length]; var rotations = new Quaternion[nodes.Length]; var scales = new Vector3[nodes.Length];
            var meshes = new Mesh[filters.Length]; var collision = new string[colliders.Length];
            for (int i = 0; i < nodes.Length; ++i) { positions[i] = nodes[i].localPosition; rotations[i] = nodes[i].localRotation; scales[i] = nodes[i].localScale; }
            for (int i = 0; i < filters.Length; ++i) meshes[i] = filters[i].sharedMesh;
            for (int i = 0; i < colliders.Length; ++i) collision[i] = EditorJsonUtility.ToJson(colliders[i]);
            var seen = new HashSet<Mesh>(); Audit(f.Root, false, seen);
            int materialCount = SurfaceMaterialCount();
            for (int repeat = 0; repeat < 24; ++repeat) f.World.Apply(state, 0);
            f.World.Build();
            Assert(stateBefore == JsonUtility.ToJson(state), "Texture projection changed authoritative state");
            Assert(SurfaceMaterialCount() == materialCount, "Repeated projection allocated surface materials");
            Same(nodes, f.Root.GetComponentsInChildren<Transform>(true));
            Same(filters, f.Root.GetComponentsInChildren<MeshFilter>(true));
            Same(colliders, f.Root.GetComponentsInChildren<Collider>(true));
            for (int i = 0; i < nodes.Length; ++i)
                Assert(nodes[i].localPosition == positions[i] && nodes[i].localRotation == rotations[i] && nodes[i].localScale == scales[i], "Projection moved geometry: " + nodes[i].name);
            for (int i = 0; i < filters.Length; ++i) Assert(filters[i].sharedMesh == meshes[i], "Projection replaced mesh");
            for (int i = 0; i < colliders.Length; ++i) Assert(EditorJsonUtility.ToJson(colliders[i]) == collision[i], "Projection changed collider: " + colliders[i].name);
            for (int n = 101; n <= 106; ++n)
                Assert((Find(f.Root.transform, "Bed " + n).position - HotelLayout.RoomTarget("bed", n)).sqrMagnitude < .00001f, "Authoritative bed target moved");
            HotelSaveStore.Validate(state);
        }

        static void Audit(GameObject root, bool uvOnly, HashSet<Mesh> meshes)
        {
            int count = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponent<TextMesh>() != null) continue; // Font atlas has its own shader.
                Assert(renderer.sharedMaterials.Length > 0, "Renderer has no material: " + renderer.name);
                foreach (Material m in renderer.sharedMaterials)
                {
                    CheckMaterial(m);
                    if (uvOnly) Assert(m.GetFloat("_MappingMode") == (float)HotelMappingMode.UV0, "Movable object uses world projection: " + renderer.name);
                }
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Assert(filter != null && filter.sharedMesh != null, "Missing surface mesh: " + renderer.name);
                Assert(filter.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0), "Surface has no UV0: " + renderer.name);
                meshes.Add(filter.sharedMesh); ++count;
            }
            Assert(count > 0, "Vacuous surface audit: " + root.name);
        }

        static void CheckMaterial(Material m)
        {
            Assert(m != null && m.shader != null && m.shader.name == "WorstHotel/HotelSurface", "Non-hotel surface material");
            foreach (string property in new[] { "_BaseMap", "_BaseColor", "_MappingMode", "_WorldScale", "_Smoothness", "_Emission" })
                Assert(m.HasProperty(property), "Missing shader property: " + property);
            Texture map = m.GetTexture("_BaseMap"); bool known = false;
            for (int i = 0; i < 10; ++i) known |= map == HotelTextureLibrary.GetTexture((HotelTextureId)i);
            Assert(known && map != Texture2D.whiteTexture, "Surface uses fallback/foreign texture: " + m.name);
            foreach (string property in new[] { "_MappingMode", "_WorldScale", "_Smoothness", "_Emission" })
                Assert(Finite(m.GetFloat(property)), "Nonfinite material property: " + m.name);
            Assert(m.GetFloat("_WorldScale") > 0 && (m.GetFloat("_MappingMode") == 0 || m.GetFloat("_MappingMode") == 1), "Invalid mapping values");
            Color color = m.GetColor("_BaseColor");
            Assert(Finite(color.r) && Finite(color.g) && Finite(color.b) && Finite(color.a), "Nonfinite tint");
            Vector2 scale = m.GetTextureScale("_BaseMap"), offset = m.GetTextureOffset("_BaseMap");
            Assert(Finite(scale.x) && Finite(scale.y) && scale.x != 0 && scale.y != 0 && Finite(offset.x) && Finite(offset.y), "Invalid texture transform");
        }

        static float ColorDistance(Material a, Material b)
        {
            Color x = a.GetColor("_BaseColor"), y = b.GetColor("_BaseColor");
            return new Vector3(x.r - y.r, x.g - y.g, x.b - y.b).magnitude;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void Reject(Action action) { try { action(); } catch (ArgumentOutOfRangeException) { return; } throw new Exception("Invalid enum was accepted"); }
        static void Same<T>(T[] a, T[] b) where T : UnityEngine.Object
        { Assert(a.Length == b.Length, "Hierarchy count changed"); for (int i = 0; i < a.Length; ++i) Assert(a[i] == b[i], "Hierarchy identity changed"); }
        static Transform Find(Transform root, string name)
        { foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; throw new Exception("Missing production visual: " + name); }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception("Grim textures: " + message); }

        sealed class WorldFixture : IDisposable
        {
            internal readonly GameObject Root = new GameObject("HotelTextureTests / isolated world");
            internal readonly HotelWorld World;
            readonly Scene scene;
            internal WorldFixture()
            {
                scene = EditorSceneManager.NewPreviewScene();
                try { SceneManager.MoveGameObjectToScene(Root, scene); World = Root.AddComponent<HotelWorld>(); World.Build(); }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
