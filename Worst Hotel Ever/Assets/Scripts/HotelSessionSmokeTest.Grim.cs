using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorstHotel
{
    public sealed partial class HotelSessionSmokeTest
    {
        // Parent dispatch: grim-textures -> ReviewGrimTextures(), then existing Finish().
        // Camera poses, detached projections and factory subjects are labelled fixtures.
        // No native-input, subjective visual-quality or FPS acceptance is implied.
        IEnumerator ReviewGrimTextures()
        {
            if (!GrimStep(() =>
            {
                var args = Environment.GetCommandLineArgs();
                string isolated = Path.GetFullPath(Path.Combine(directory,
                    "smoke-save-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".json"));
                GrimRequire(scenario == "grim-textures" && game.Automated && Array.IndexOf(args, "-whe-session-tests") >= 0 &&
                    string.Equals(Path.GetFullPath(game.Session.SavePath), isolated, StringComparison.OrdinalIgnoreCase),
                    "Requires explicit session-test flag and per-process smoke save");
                GrimRequire(game.Session.IsHost && !game.Session.UseLegacyFixture && !game.Session.UseMvpFixture &&
                    game.Session.State.version == 3 && game.Session.State.contentVersion == 3 &&
                    HotelDangerRules.Enabled(game.Session.State) && game.Session.Manager.ConnectedClientsIds.Count == 1,
                    "Requires isolated production-v3 solo host");
                GrimRequire(game.Session.State.phase == "preparation" && game.Held == null &&
                    game.LocalPlayer.workTarget == "" && game.View != null && game.UI.TeachingHints != null,
                    "Requires idle preparation, camera and teaching scheduler");
                HotelSaveStore.Validate(game.Session.State);
                checks.Add("GRIM_PRODUCTION_V3_ISOLATED_HOST_VALIDATED");
            })) yield break;

            HotelState state = game.Session.State;
            string original = JsonUtility.ToJson(state);
            bool gameEnabled = game.enabled, sessionEnabled = game.Session.enabled, hintsEnabled = game.UI.TeachingHints.Enabled;
            bool uiEnabled = game.UI.enabled;
            string panel = game.Panel;
            Vector3 playerPosition = game.Controller.transform.position;
            Quaternion playerRotation = game.Controller.transform.rotation;
            Vector3 cameraPosition = game.View.transform.localPosition;
            Quaternion cameraRotation = game.View.transform.localRotation;
            float fieldOfView = game.View.fieldOfView;
            Transform hands = game.View.transform.Find("Carry anchor");
            bool handsActive = hands != null && hands.gameObject.activeSelf;
            GameObject stage = null;
            try
            {
                // Both update loops use unscaled time, so timeScale alone would not freeze state.
                game.enabled = false; game.Session.enabled = false;
                game.UI.TeachingHints.Enabled = false;
                game.World.Apply(state, game.Session.LocalId);
                string geometry = GrimGeometrySignature();
                checks.Add("FIXTURE_GRIM_UPDATES_FROZEN_CAMERA_ONLY_NO_AUTHORITATIVE_POSE_COMMANDS");
                if (!GrimStep(() =>
                {
                    GrimAssets();
                    GrimAudit(game.World.gameObject, false);
                    GrimAudit(game.View.gameObject, true);
                    int sleeves = 0, gloves = 0;
                    foreach (Renderer r in game.View.GetComponentsInChildren<Renderer>(true))
                    { if (r.name == "Employee sleeve") ++sleeves; if (r.name == "Work glove") ++gloves; }
                    GrimRequire(sleeves == 2 && gloves == 2, "First-person hand audit was incomplete");
                    checks.Add("GRIM_ALL_WORLD_AND_HAND_SURFACES_USE_CUSTOM_BASEMAP");
                })) yield break;

                yield return TabletResolution(1280); if (finished) yield break;
                game.UI.enabled = false;
                if (hands != null) hands.gameObject.SetActive(false);
                GrimPose("lobby", new Vector3(3.1f, .1f, -5.7f), new Vector3(-1.5f, 1.25f, -.3f));
                yield return GrimCapture("lobby", false); if (errors.Count != 0) yield break;
                GrimPose("corridor", new Vector3(0, .1f, 2.4f), new Vector3(0, 1.4f, 19));
                yield return GrimCapture("corridor", false); if (errors.Count != 0) yield break;
                GrimPose("services", new Vector3(2.3f, .1f, -2.8f), HotelLayout.Target("coffee") + Vector3.up * .5f);
                yield return GrimCapture("services", false); if (errors.Count != 0) yield break;

                // Alter only a detached presentation snapshot. The live hotel/save is unchanged.
                foreach (bool dirty in new[] { false, true })
                {
                    if (!GrimStep(() =>
                    {
                        var projection = JsonUtility.FromJson<HotelState>(original);
                        var room = projection.rooms.Find(r => r.number == 102);
                        room.bed = dirty ? 1 : 2; room.mvp.dirt = dirty ? .8f : 0;
                        room.mvp.dirtyTowels = dirty ? 2 : 0; room.mvp.binFill = dirty ? .9f : 0;
                        room.water = dirty ? .35f : 0;
                        HotelSaveStore.Validate(projection);
                        string before = JsonUtility.ToJson(projection);
                        game.World.Apply(projection, game.Session.LocalId);
                        GrimRequire(before == JsonUtility.ToJson(projection), "World.Apply mutated detached bedroom state");
                    })) yield break;
                    string label = dirty ? "bedroom-dirty" : "bedroom-clean";
                    GrimPose(label, new Vector3(3.3f, .1f, 4.65f), HotelLayout.RoomTarget("bed", 102));
                    yield return GrimCapture(label, false); if (errors.Count != 0) yield break;
                }
                if (!GrimStep(() =>
                {
                    var projection = JsonUtility.FromJson<HotelState>(original);
                    projection.mvp.utilities.powerFault = true;
                    projection.mvp.utilities.powerEpisode++;
                    HotelSaveStore.Validate(projection);
                    game.World.Apply(projection, game.Session.LocalId);
                })) yield break;
                GrimPose("bedroom-power-off", new Vector3(3.3f, .1f, 4.65f), HotelLayout.RoomTarget("bed", 102));
                yield return GrimCapture("bedroom-power-off", false); if (errors.Count != 0) yield break;
                game.World.Apply(state, game.Session.LocalId);
                checks.Add("GRIM_REPRESENTATIVE_WORLD_CAPTURES_COMPLETE");

                stage = new GameObject("FIXTURE grim texture subjects / outside playable hotel");
                stage.transform.position = new Vector3(0, 60, 0);
                foreach (bool employee in new[] { true, false })
                {
                    GameObject subject = null;
                    if (!GrimStep(() =>
                    {
                        subject = HotelWorld.MakePerson(employee, 3);
                        subject.transform.SetParent(stage.transform, false);
                        foreach (Collider c in subject.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                        // Freeze factory animation for reproducible framing; this does not alter geometry.
                        foreach (MonoBehaviour behaviour in subject.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                        GrimAudit(subject, true);
                        GrimFrameSubject(subject, employee ? "staff" : "guest", true);
                    })) yield break;
                    yield return GrimCapture(employee ? "staff" : "guest", false);
                    subject.SetActive(false); Destroy(subject);
                    if (errors.Count != 0) yield break;
                }
                checks.Add("GRIM_STAFF_GUEST_FACTORY_CAPTURES_COMPLETE");

                string[] kinds = { "bag", "toolbox", "mop", "linen", "towel", "dirtylinen", "dirtytowel",
                    "trashbag", "cart", "plunger", "coffee", "coffeecup", "bag-large" };
                foreach (string kind in kinds)
                {
                    GameObject subject = null;
                    if (!GrimStep(() =>
                    {
                        subject = HotelWorld.MakeItem(kind == "bag-large" ? "bag" : kind, kind == "bag-large" ? "large" : "small", "clean");
                        subject.transform.SetParent(stage.transform, false);
                        foreach (Collider c in subject.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                        GrimAudit(subject, true);
                        GrimFrameSubject(subject, "item-" + kind, false);
                    })) yield break;
                    yield return GrimCapture("item-" + kind, false);
                    subject.SetActive(false); Destroy(subject);
                    if (errors.Count != 0) yield break;
                }
                checks.Add("GRIM_ALL_13_PORTABLE_FACTORY_CAPTURES_COMPLETE");

                if (!GrimStep(() =>
                {
                    for (int i = 0; i < 10; ++i)
                    {
                        var sample = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        sample.name = "FIXTURE material " + (HotelSurfaceFamily)i;
                        sample.transform.SetParent(stage.transform, false);
                        sample.transform.localPosition = new Vector3((i % 5 - 2) * 1.3f, (1 - i / 5) * 1.3f, 0);
                        sample.transform.localScale = new Vector3(1.05f, 1.05f, .18f);
                        sample.GetComponent<Collider>().enabled = false;
                        sample.GetComponent<Renderer>().sharedMaterial = HotelTextureLibrary.GetMaterial((HotelSurfaceFamily)i, HotelInk.White, HotelMappingMode.UV0);
                        checks.Add("FIXTURE_GRIM_PANORAMA_SLOT " + i + " " + (HotelSurfaceFamily)i + " source=" + HotelTextureLibrary.SourceFileName((HotelTextureId)i));
                    }
                    GrimAudit(stage, true);
                    GrimPose("material-panorama", new Vector3(0, 59, -5.2f), new Vector3(0, 60.65f, 0));
                })) yield break;
                yield return GrimCapture("material-panorama", false); if (errors.Count != 0) yield break;
                stage.SetActive(false);
                checks.Add("GRIM_TEN_FAMILY_MATERIAL_PANORAMA_CAPTURED");

                game.UI.enabled = true;
                if (hands != null) hands.gameObject.SetActive(handsActive);
                GrimPose("hud-tablet", new Vector3(0, .1f, -4.8f), new Vector3(0, 1.6f, 8));
                foreach (int width in new[] { 1280, 960, 1920 })
                {
                    yield return TabletResolution(width); if (finished) yield break;
                    game.OpenPanel("");
                    yield return GrimCapture("hud", true); if (errors.Count != 0) yield break;
                    game.OpenPanel("tasks");
                    yield return GrimCapture("tablet", true); if (errors.Count != 0) yield break;
                }
                if (!GrimStep(() =>
                {
                    // Inspect actual UI references after real OnGUI Repaints initialized the theme.
                    foreach (var pair in new[] {
                        new KeyValuePair<string, HotelTextureId>("casingTexture", HotelTextureId.TabletCasing),
                        new KeyValuePair<string, HotelTextureId>("screenTexture", HotelTextureId.TabletScreen),
                        new KeyValuePair<string, HotelTextureId>("metalTexture", HotelTextureId.Metal),
                        new KeyValuePair<string, HotelTextureId>("rubberTexture", HotelTextureId.Rubber) })
                    {
                        FieldInfo field = typeof(HotelUI).GetField(pair.Key, BindingFlags.Instance | BindingFlags.NonPublic);
                        GrimRequire(field != null && ReferenceEquals(field.GetValue(game.UI), HotelTextureLibrary.GetTexture(pair.Value)), "Wrong tablet asset reference: " + pair.Key);
                    }
                    checks.Add("GRIM_TABLET_RESOURCE_REFERENCES_AND_REPAINTS_VERIFIED");
                    checks.Add("GRIM_HUD_TABLET_1280x800_960x600_1920x1080_CAPTURED");
                    GrimTiming();
                    game.World.Apply(state, game.Session.LocalId);
                    GrimAudit(game.World.gameObject, false); GrimAudit(game.View.gameObject, true);
                    GrimRequire(geometry == GrimGeometrySignature(), "Capture/projection changed world mesh, target or collider identity/geometry");
                    GrimRequire(original == JsonUtility.ToJson(state), "Texture review changed authoritative hotel state");
                    HotelSaveStore.Validate(state);
                    checks.Add("GRIM_FINAL_STATE_VALID_UNCHANGED_GEOMETRY_REFERENCES_STABLE");
                    GrimRequire(errors.Count == 0, "Unity/IMGUI errors occurred during capture");
                    checks.Add("GRIM_NO_UNITY_OR_IMGUI_ERRORS");
                    checks.Add("LIMITATION: labelled camera, detached-state and factory fixtures; no native mouse/input acceptance.");
                    checks.Add("LIMITATION: real PNG/source references and nonblank frames do not certify contrast, clipping, aesthetics, seam quality or performance; inspect captures.");
                })) yield break;
            }
            finally
            {
                if (stage != null) { stage.SetActive(false); Destroy(stage); }
                if (game.World != null) game.World.Apply(state, game.Session.LocalId);
                if (game.Controller != null) { game.Teleport(playerPosition); game.Controller.transform.rotation = playerRotation; }
                game.View.transform.localPosition = cameraPosition; game.View.transform.localRotation = cameraRotation; game.View.fieldOfView = fieldOfView;
                if (hands != null) hands.gameObject.SetActive(handsActive);
                game.UI.TeachingHints.Enabled = hintsEnabled; game.UI.enabled = uiEnabled;
                game.Panel = panel;
                game.Session.enabled = sessionEnabled; game.enabled = gameEnabled;
            }
        }

        bool GrimStep(Action action)
        {
            try { action(); return true; }
            catch (Exception exception) { errors.Add("Grim textures: " + exception); return false; }
        }
        static void GrimRequire(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        void GrimAssets()
        {
            var textures = new HashSet<Texture2D>();
            foreach (HotelTextureId id in Enum.GetValues(typeof(HotelTextureId)))
            {
                Texture2D texture = HotelTextureLibrary.GetTexture(id);
                string filename = HotelTextureLibrary.SourceFileName(id);
                GrimRequire(filename.EndsWith(".png", StringComparison.Ordinal), "Source filename must include .png");
                GrimRequire(texture != null && texture != Texture2D.whiteTexture && texture != Texture2D.blackTexture &&
                    texture.width >= 64 && texture.height >= 64 && !texture.isReadable && textures.Add(texture), "Missing/fallback/aliased resource: " + id);
                GrimRequire(texture == Resources.Load<Texture2D>("Hotel/Textures/v1/" + filename.Substring(0, filename.Length - 4)), "Wrong Resources identity: " + id);
            }
            GrimRequire(textures.Count == 12, "Expected twelve source textures");
            checks.Add("GRIM_12_DISTINCT_NONFALLBACK_RESOURCES_VERIFIED");
        }

        void GrimAudit(GameObject root, bool moving)
        {
            var materials = new HashSet<Material>(); var meshes = new HashSet<Mesh>();
            int renderers = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponent<TextMesh>() != null) continue;
                GrimRequire(renderer.sharedMaterials.Length > 0, "No material: " + renderer.name);
                foreach (Material material in renderer.sharedMaterials)
                {
                    GrimRequire(material != null && material.shader != null && material.shader.name == "WorstHotel/HotelSurface" && material.HasProperty("_BaseMap"), "Non-hotel/fallback material: " + renderer.name);
                    Texture map = material.GetTexture("_BaseMap"); bool known = false;
                    for (int i = 0; i < 10; ++i) known |= map == HotelTextureLibrary.GetTexture((HotelTextureId)i);
                    GrimRequire(known && map != Texture2D.whiteTexture, "Unknown/white BaseMap: " + renderer.name);
                    float mapping = material.GetFloat("_MappingMode"), scale = material.GetFloat("_WorldScale");
                    GrimRequire((mapping == 0 || mapping == 1) && !float.IsNaN(scale) && !float.IsInfinity(scale) && scale > 0, "Invalid mapping: " + renderer.name);
                    GrimRequire(!moving || mapping == (float)HotelMappingMode.UV0, "Movable surface uses world projection: " + renderer.name);
                    materials.Add(material);
                }
                var filter = renderer.GetComponent<MeshFilter>();
                GrimRequire(filter != null && filter.sharedMesh != null && filter.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0), "Missing UV0 stream: " + renderer.name);
                if (meshes.Add(filter.sharedMesh) && filter.sharedMesh.isReadable)
                {
                    Vector2[] uv = filter.sharedMesh.uv; bool varied = false;
                    GrimRequire(uv.Length == filter.sharedMesh.vertexCount && uv.Length > 0, "UV0 count mismatch");
                    foreach (Vector2 p in uv)
                    {
                        GrimRequire(!float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y), "Nonfinite UV0");
                        varied |= (p - uv[0]).sqrMagnitude > .000001f;
                    }
                    GrimRequire(varied, "Collapsed UV0: " + filter.sharedMesh.name);
                }
                ++renderers;
            }
            GrimRequire(renderers > 0 && materials.Count <= 500, "Empty or unbounded surface audit: " + root.name);
            checks.Add("GRIM_RENDERER_AUDIT root=" + root.name + " renderers=" + renderers + " materials=" + materials.Count + " meshes=" + meshes.Count);
        }

        void GrimPose(string label, Vector3 feet, Vector3 look)
        {
            game.Teleport(feet); game.View.transform.localPosition = new Vector3(0, 1.65f, 0);
            game.LookAtForTest(look);
            checks.Add("FIXTURE_GRIM_CAMERA " + label + " feet=" + feet.ToString("F3", CultureInfo.InvariantCulture) + " look=" + look.ToString("F3", CultureInfo.InvariantCulture));
        }

        void GrimFrameSubject(GameObject subject, string label, bool person)
        {
            Renderer[] renderers = subject.GetComponentsInChildren<Renderer>();
            GrimRequire(renderers.Length > 0, "Empty subject: " + label);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            // Bounding sphere fits even the narrower horizontal FOV; +Z shows people's faces.
            float halfAngle = Mathf.Min(game.View.fieldOfView * .5f * Mathf.Deg2Rad,
                Mathf.Atan(Mathf.Tan(game.View.fieldOfView * .5f * Mathf.Deg2Rad) * game.View.aspect));
            float distance = Mathf.Max(.75f, bounds.extents.magnitude / Mathf.Sin(halfAngle) * 1.2f);
            Vector3 direction = person ? new Vector3(.2f, .12f, 1).normalized : new Vector3(.25f, .3f, -1).normalized;
            Vector3 eye = bounds.center + direction * distance;
            GrimPose(label, eye - Vector3.up * 1.65f, bounds.center);
            checks.Add("FIXTURE_GRIM_SUBJECT " + label + " bounds=" + bounds.size.ToString("F3", CultureInfo.InvariantCulture));
        }

        IEnumerator GrimCapture(string label, bool ui)
        {
            yield return null; yield return new WaitForEndOfFrame();
            Texture2D capture = null;
            try
            {
                GrimStep(() =>
                {
                    if (ui)
                    {
                        bool tablet = label == "tablet";
                        GrimRequire(game.Panel == (tablet ? "tasks" : "") && game.UI.TabletDrawnForTest == tablet &&
                            !game.UI.TeachingHintDrawnForTest, "Unexpected tablet/HUD Repaint: " + label);
                        if (!tablet) GrimRequire(game.UI.HudElementCountForTest == 4, "HUD whitelist changed");
                    }
                    capture = ScreenCapture.CaptureScreenshotAsTexture();
                    GrimRequire(capture != null && capture.width == Screen.width && capture.height == Screen.height, "Bad screenshot dimensions: " + label);
                    int lit = 0, low = 765, high = 0;
                    Color32[] pixels = capture.GetPixels32();
                    for (int p = 0; p < pixels.Length; p += 64)
                    {
                        int sum = pixels[p].r + pixels[p].g + pixels[p].b;
                        if (sum > 70) ++lit; low = Math.Min(low, sum); high = Math.Max(high, sum);
                    }
                    GrimRequire(lit > 100 && high - low > 20, "Blank/flat frame: " + label);
                    string file = "grim-" + Screen.width + "x" + Screen.height + "-" + label + ".png";
                    File.WriteAllBytes(Path.Combine(directory, file), capture.EncodeToPNG());
                    checks.Add("GRIM_CAPTURE " + file);
                });
            }
            finally { if (capture != null) Destroy(capture); }
        }

        string GrimGeometrySignature()
        {
            var text = new StringBuilder();
            foreach (MeshFilter filter in game.World.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                text.Append(filter.GetInstanceID()).Append(':').Append(mesh == null ? 0 : mesh.GetInstanceID()).Append(':').Append(mesh == null ? 0 : mesh.vertexCount).Append(';');
            }
            foreach (HotelTarget target in game.World.GetComponentsInChildren<HotelTarget>(true))
                text.Append(target.GetInstanceID()).Append(':').Append(target.id).Append(':').Append(target.transform.position.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            foreach (Collider c in game.World.GetComponentsInChildren<Collider>(true))
            {
                text.Append(c.GetInstanceID()).Append(':').Append(c.enabled).Append(':').Append(c.isTrigger).Append(':')
                    .Append(c.transform.localPosition.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(c.transform.localRotation.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(c.transform.localScale.ToString("R", CultureInfo.InvariantCulture));
                if (c is BoxCollider box) text.Append(box.center.ToString("R", CultureInfo.InvariantCulture)).Append(box.size.ToString("R", CultureInfo.InvariantCulture));
                if (c is CapsuleCollider capsule) text.Append(capsule.center.ToString("R", CultureInfo.InvariantCulture)).Append(capsule.radius.ToString("R", CultureInfo.InvariantCulture)).Append(capsule.height.ToString("R", CultureInfo.InvariantCulture));
                if (c is SphereCollider sphere) text.Append(sphere.center.ToString("R", CultureInfo.InvariantCulture)).Append(sphere.radius.ToString("R", CultureInfo.InvariantCulture));
                text.Append(';');
            }
            return text.ToString();
        }

        void GrimTiming()
        {
            // CPU-only warm lookup diagnostic. Excludes rendering, GPU, capture and disk I/O.
            // There is no pass threshold or inferred FPS, and no pre-texture baseline claim.
            const int requests = 10000;
            Material expected = HotelTextureLibrary.GetMaterial(HotelSurfaceFamily.Cloth, HotelInk.White, HotelMappingMode.UV0);
            var watch = new System.Diagnostics.Stopwatch(); var milliseconds = new double[2];
            for (int pass = 0; pass < 2; ++pass)
            {
                watch.Restart();
                for (int i = 0; i < requests; ++i)
                    GrimRequire(ReferenceEquals(expected, HotelTextureLibrary.GetMaterial(HotelSurfaceFamily.Cloth, HotelInk.White, HotelMappingMode.UV0)), "Warm lookup changed material identity");
                watch.Stop(); milliseconds[pass] = watch.Elapsed.TotalMilliseconds;
            }
            checks.Add("GRIM_WARM_CACHE_TIMING_DIAGNOSTIC requests_per_pass=" + requests + " first_ms=" + milliseconds[0].ToString("F3", CultureInfo.InvariantCulture) +
                " repeat_ms=" + milliseconds[1].ToString("F3", CultureInfo.InvariantCulture) + " repeat_over_first=" +
                (milliseconds[1] / Math.Max(.000001, milliseconds[0])).ToString("F3", CultureInfo.InvariantCulture) + " cpu_only_not_fps");
        }
    }
}
