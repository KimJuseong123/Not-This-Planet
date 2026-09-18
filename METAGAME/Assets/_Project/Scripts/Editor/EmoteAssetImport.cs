using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>외부 렌더 PNG를 에디터 안에서 최초 임포트하고 빌드용 설정을 고정한다.</summary>
    public static class EmoteAssetImport
    {
        public static void ImportFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-emoteSource");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing -emoteSource");
            string source = args[index + 1];
            const string target = "Assets/_Project/UI/Resources/Emotes";
            Directory.CreateDirectory(target);
            string[] names = { "01-question", "02-exclamation", "03-thumbsup", "04-heart", "05-crying", "06-angry" };
            string[] originals = { "01-question", "02-exclamation", "03-thumbsup", "04-heart", "05-astronaut", "06-space-character" };
            for (int i = 0; i < names.Length; i++)
            {
                string path = target + "/" + names[i] + ".png";
                if (!File.Exists(path)) File.Copy(Path.Combine(source, originals[i] + ".png"), path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.maxTextureSize = 512;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Emotes] Imported six transparent 512px billboard textures.");
        }
    }
}
