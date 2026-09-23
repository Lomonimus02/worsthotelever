using System;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.BuildTools
{
    // Asset-specific policy: no global import changes to third-party or font textures.
    public sealed class HotelTextureImporter : AssetPostprocessor
    {
        public const string Root = "Assets/Art/Resources/Hotel/Textures/v1/";
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root, StringComparison.Ordinal) || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
            var importer = (TextureImporter)assetImporter;
            bool screen = assetPath.EndsWith("/tablet-screen.png", StringComparison.Ordinal);
            bool casing = assetPath.EndsWith("/tablet-casing.png", StringComparison.Ordinal);
            bool ui = screen || casing;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.isReadable = false;
            importer.mipmapEnabled = !ui;
            importer.streamingMipmaps = false;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.maxTextureSize = ui ? 1024 : 512;
            importer.wrapMode = screen ? TextureWrapMode.Clamp : casing ? TextureWrapMode.Repeat : TextureWrapMode.Mirror;
            importer.filterMode = ui ? FilterMode.Bilinear : FilterMode.Trilinear;
            importer.anisoLevel = ui ? 1 : 4;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = 100;
        }
    }
}
