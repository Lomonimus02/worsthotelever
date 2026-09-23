using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    public enum HotelTextureId { Plaster, Wood, Carpet, Cloth, Metal, Ceramic, Rubber, Paper, Skin, Water, TabletCasing, TabletScreen }
    public enum HotelSurfaceFamily { Plaster, Wood, Carpet, Cloth, Metal, Ceramic, Rubber, Paper, Skin, Water }
    public enum HotelMappingMode { UV0, WorldTriplanar }
    public enum HotelInk { Cream, Plaster, Wood, Oak, Burgundy, Red, Brass, White, Linen, Dirty,
        Dark, Metal, Ceramic, Water, Foam, Teal, Green, Leaf, Gold, Blue, Skin, SkinDark, Hair, Glow, Paper }

    /// <summary>
    /// Main-thread, application-lifetime library. Returned textures and materials are shared:
    /// callers must not mutate or destroy them. Import settings belong to the source PNG assets.
    /// </summary>
    public static class HotelTextureLibrary
    {
        const string ResourceRoot = "Hotel/Textures/v1/";
        const int TextureCount = (int)HotelTextureId.TabletScreen + 1;
        const int FamilyCount = (int)HotelSurfaceFamily.Water + 1;
        const int InkCount = (int)HotelInk.Paper + 1;
        const int MappingCount = (int)HotelMappingMode.WorldTriplanar + 1;
        static readonly Texture2D[] Textures = new Texture2D[TextureCount];
        // Finite enum indices: at most 10 * 25 * 2 materials, allocated only when used.
        static readonly Material[,,] Materials = new Material[FamilyCount, InkCount, MappingCount];
        static readonly Dictionary<Material, MaterialKey> Keys = new Dictionary<Material, MaterialKey>();
        static Shader surfaceShader;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int Emission = Shader.PropertyToID("_Emission");
        static readonly int Mapping = Shader.PropertyToID("_MappingMode");
        static readonly int WorldScale = Shader.PropertyToID("_WorldScale");

        readonly struct MaterialKey
        {
            public readonly HotelSurfaceFamily family;
            public readonly HotelInk ink;
            public readonly HotelMappingMode mapping;
            public MaterialKey(HotelSurfaceFamily family, HotelInk ink, HotelMappingMode mapping)
            { this.family = family; this.ink = ink; this.mapping = mapping; }
        }

        /// <summary>Source filename including .png; Resources paths omit that extension.</summary>
        public static string SourceFileName(HotelTextureId id)
        {
            switch (id)
            {
                case HotelTextureId.Plaster: return "wall-plaster.png";
                case HotelTextureId.Wood: return "wood.png";
                case HotelTextureId.Carpet: return "carpet.png";
                case HotelTextureId.Cloth: return "cloth.png";
                case HotelTextureId.Metal: return "metal.png";
                case HotelTextureId.Ceramic: return "ceramic.png";
                case HotelTextureId.Rubber: return "rubber.png";
                case HotelTextureId.Paper: return "paper.png";
                case HotelTextureId.Skin: return "skin.png";
                case HotelTextureId.Water: return "water.png";
                case HotelTextureId.TabletCasing: return "tablet-casing.png";
                case HotelTextureId.TabletScreen: return "tablet-screen.png";
                default: throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown hotel texture");
            }
        }

        public static Texture2D GetTexture(HotelTextureId id)
        {
            string filename = SourceFileName(id); // Validate before indexing; never cache a failed load.
            Texture2D texture = Textures[(int)id];
            if (texture != null) return texture;
            string path = ResourceRoot + filename.Substring(0, filename.Length - 4);
            texture = Resources.Load<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("Missing hotel texture Resources/" + path + ".png");
            Textures[(int)id] = texture;
            return texture;
        }

        public static Material GetMaterial(HotelSurfaceFamily family, HotelInk ink, HotelMappingMode mapping)
        {
            if ((uint)family >= FamilyCount) throw new ArgumentOutOfRangeException(nameof(family));
            if ((uint)ink >= InkCount) throw new ArgumentOutOfRangeException(nameof(ink));
            if ((uint)mapping >= MappingCount) throw new ArgumentOutOfRangeException(nameof(mapping));
            Material material = Materials[(int)family, (int)ink, (int)mapping];
            if (material != null) return material;
            // Remove a stale Unity object if an editor/domain lifecycle destroyed it externally.
            if (!ReferenceEquals(material, null)) Keys.Remove(material);
            // The first ten texture IDs intentionally match the ten surface families.
            Texture2D texture = GetTexture((HotelTextureId)family);
            if (surfaceShader == null) surfaceShader = Resources.Load<Shader>("Hotel/HotelSurface");
            if (surfaceShader == null) throw new InvalidOperationException("Missing Resources/Hotel/HotelSurface.shader");
            Profile(family, out float polish, out float repeatsPerMetre);
            material = new Material(surfaceShader) {
                name = "Hotel / " + family + " / " + mapping + " / " + ink,
                enableInstancing = true
            };
            material.SetTexture(BaseMap, texture);
            material.SetColor(BaseColor, ColorOf(ink));
            material.SetFloat(Smoothness, polish);
            material.SetFloat(Emission, ink == HotelInk.Glow ? .8f : ink == HotelInk.Foam ? .23f : 0);
            material.SetFloat(Mapping, (float)mapping);
            material.SetFloat(WorldScale, repeatsPerMetre);
            // Two mirrored tiles close the 0/1 angular seam on lathes and built-in hand meshes.
            // UV0 stays attached even when a limb or carried item moves/scales. World density is
            // already specified in repeatsPerMetre and must not inherit this UV repeat factor.
            material.SetTextureScale("_BaseMap", mapping == HotelMappingMode.UV0 ? Vector2.one * 2 : Vector2.one);
            Materials[(int)family, (int)ink, (int)mapping] = material;
            Keys.Add(material, new MaterialKey(family, ink, mapping));
            return material;
        }

        // State changes alter tint only; the physical family and projection cannot drift with Ink.
        internal static Material WithInk(Material source, HotelInk ink)
        {
            MaterialKey key = KeyOf(source);
            return GetMaterial(key.family, ink, key.mapping);
        }

        // Used once for immutable architecture, before StaticBatchingUtility.Combine.
        internal static Material WithMapping(Material source, HotelMappingMode mapping)
        {
            MaterialKey key = KeyOf(source);
            return GetMaterial(key.family, key.ink, mapping);
        }

        static MaterialKey KeyOf(Material source)
        {
            if (source == null || !Keys.TryGetValue(source, out MaterialKey key))
                throw new InvalidOperationException("Hotel surface is not owned by HotelTextureLibrary");
            return key;
        }

        static void Profile(HotelSurfaceFamily family, out float polish, out float repeatsPerMetre)
        {
            switch (family)
            {
                case HotelSurfaceFamily.Plaster: polish = .025f; repeatsPerMetre = .65f; break;
                case HotelSurfaceFamily.Wood: polish = .12f; repeatsPerMetre = .8f; break;
                case HotelSurfaceFamily.Carpet: polish = .015f; repeatsPerMetre = 1.25f; break;
                case HotelSurfaceFamily.Cloth: polish = .025f; repeatsPerMetre = 1.5f; break;
                case HotelSurfaceFamily.Metal: polish = .34f; repeatsPerMetre = 1f; break;
                case HotelSurfaceFamily.Ceramic: polish = .42f; repeatsPerMetre = 1f; break;
                case HotelSurfaceFamily.Rubber: polish = .07f; repeatsPerMetre = 1.25f; break;
                case HotelSurfaceFamily.Paper: polish = .015f; repeatsPerMetre = 1f; break;
                case HotelSurfaceFamily.Skin: polish = .09f; repeatsPerMetre = 1f; break;
                case HotelSurfaceFamily.Water: polish = .72f; repeatsPerMetre = 1f; break;
                default: throw new ArgumentOutOfRangeException(nameof(family));
            }
        }

        // Preserve the established tint/label palette. The shader controls exposure and surface wear.
        public static Color ColorOf(HotelInk ink)
        {
            switch (ink)
            {
                case HotelInk.Cream: return new Color(.68f, .70f, .62f);
                case HotelInk.Plaster: return new Color(.55f, .59f, .55f);
                case HotelInk.Wood: return new Color(.22f, .23f, .20f);
                case HotelInk.Oak: return new Color(.39f, .36f, .29f);
                case HotelInk.Burgundy: return new Color(.31f, .095f, .085f);
                case HotelInk.Red: return new Color(.72f, .23f, .16f);
                case HotelInk.Brass: return new Color(.57f, .50f, .32f);
                case HotelInk.White: return new Color(.98f, .96f, .86f);
                case HotelInk.Linen: return new Color(.83f, .78f, .62f);
                case HotelInk.Dirty: return new Color(.56f, .47f, .32f);
                case HotelInk.Dark: return new Color(.12f, .15f, .17f);
                case HotelInk.Metal: return new Color(.38f, .47f, .47f);
                case HotelInk.Ceramic: return new Color(.8f, .86f, .79f);
                case HotelInk.Water: return new Color(.19f, .66f, .78f);
                case HotelInk.Foam: return new Color(.73f, .95f, .98f);
                case HotelInk.Teal: return new Color(.16f, .31f, .29f);
                case HotelInk.Green: return new Color(.29f, .49f, .27f);
                case HotelInk.Leaf: return new Color(.49f, .65f, .3f);
                case HotelInk.Gold: return new Color(.94f, .65f, .22f);
                case HotelInk.Blue: return new Color(.43f, .64f, .69f);
                case HotelInk.Skin: return new Color(.86f, .61f, .4f);
                case HotelInk.SkinDark: return new Color(.47f, .29f, .19f);
                case HotelInk.Hair: return new Color(.19f, .12f, .1f);
                case HotelInk.Glow: return new Color(1, .85f, .55f);
                case HotelInk.Paper: return new Color(.98f, .91f, .73f);
                default: throw new ArgumentOutOfRangeException(nameof(ink));
            }
        }
    }
}
