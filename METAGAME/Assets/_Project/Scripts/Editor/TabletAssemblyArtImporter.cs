using UnityEditor;
using UnityEngine;

namespace Metagame.UI.EditorTools
{
    /// <summary>World-space tablet line art needs mipmaps and uncompressed edges.</summary>
    public static class TabletAssemblyArtImporter
    {
        [MenuItem("Tools/Not This Planet/Tablet/Configure Assembly Artwork")]
        public static void Configure()
        {
            ConfigureTexture("Ship");
            ConfigureTexture("Actions");
            ConfigureTexture("Page5", "TabletGuide");
            ConfigureTexture("Page6", "TabletGuide");
            AssetDatabase.SaveAssets();
        }

        static void ConfigureTexture(string name, string folder = "TabletAssembly")
        {
            var importer = AssetImporter.GetAtPath($"Assets/_Project/UI/Resources/{folder}/{name}.png") as TextureImporter;
            if (importer == null) throw new System.InvalidOperationException($"Missing tablet art: {name}");
            importer.textureType = TextureImporterType.Default;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }
    }
}
