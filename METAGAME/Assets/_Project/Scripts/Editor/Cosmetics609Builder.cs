using System.IO;
using UnityEditor;
using UnityEngine;
using Metagame.Core;

namespace Metagame.EditorTools
{
    public static class Cosmetics609Builder
    {
        const string Models = "Assets/_Project/Models/Characters/Cosmetics609";
        const string Prefabs = "Assets/_Project/Prefabs/Resources/Cosmetics609";

        // Batch-only visual verification; never saves or alters a project scene.
        public static void CaptureLineup() => Capture(false);
        public static void CaptureAuras() => Capture(true);

        public static void CaptureAurasAndExit()
        {
            Capture(true);
            // Let asynchronous shader/asset workers settle before closing a batch editor.
            double deadline = EditorApplication.timeSinceStartup + 10;
            EditorApplication.CallbackFunction finish = null;
            finish = () =>
            {
                if (EditorApplication.timeSinceStartup < deadline || EditorApplication.isUpdating) return;
                EditorApplication.update -= finish;
                EditorApplication.Exit(0);
            };
            EditorApplication.update += finish;
        }

        static void Capture(bool auras)
        {
            if (!Application.isBatchMode) throw new System.InvalidOperationException("Use an isolated batch editor for this capture.");
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var rt = new RenderTexture(1500, 1000, 24);
            var image = new Texture2D(1500, 1000, TextureFormat.RGB24, false);
            var previousRt = RenderTexture.active;
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    var doll = Metagame.Gameplay.RagdollBuilder.Build("Cosmetic " + i,
                        new Vector3((i % 3) * 1.5f, -(i / 3) * 1.6f, 0),
                        Quaternion.identity, Color.white, Color.cyan, primitiveLook: false);
                    foreach (var body in doll.Bodies) body.isKinematic = true;
                    Metagame.Gameplay.PlayerAppearance.Apply(doll,
                        new Metagame.Gameplay.CustomizationData { Helmet = auras ? 0 : i + 1, Badge = auras ? 0 : i + 1, Aura = auras ? i % 3 + 1 : 0 }.Packed);
                    foreach (var particles in doll.Root.GetComponentsInChildren<ParticleSystem>())
                    {
                        particles.useAutoRandomSeed = false;
                        particles.randomSeed = (uint)(100 + i);
                        particles.Simulate(i < 3 ? 3f : 4.5f, true, true);
                    }
                    if (doll.Suit != null) doll.Suit.Sync();
                }
                var light = new GameObject("Preview light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.0f;
                light.transform.rotation = Quaternion.Euler(35, -150, 0);
                var camera = new GameObject("Preview camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(1.5f, .4f, 8f);
                camera.transform.LookAt(new Vector3(1.5f, -.15f, 0));
                camera.orthographic = true;
                camera.orthographicSize = 1.8f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.04f, .06f, .10f);
                camera.targetTexture = rt;
                for (int i = 0; i < 3; i++)
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                        new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1500, 1000), 0, 0);
                image.Apply();
                File.WriteAllBytes("../docs/assets/concepts/cosmetics609/" + (auras ? "unity-auras.png" : "unity-lineup.png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousRt;
                Object.DestroyImmediate(image);
                rt.Release(); Object.DestroyImmediate(rt);
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);
            }
        }

        [MenuItem("Metagame/Art/Rebuild 609 Cosmetics")]
        public static void Build()
        {
            Directory.CreateDirectory(Prefabs);
            AssetDatabase.Refresh();
            var shader = Resources.Load<Shader>("CharacterSuitTint");
            if (shader == null) throw new System.InvalidOperationException("CharacterSuitTint missing");
            foreach (var file in Directory.GetFiles(Models, "*.fbx"))
            {
                string path = file.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.importAnimation = false;
                importer.addCollider = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
                string texturePath = $"{Models}/{name}.fbm/Palette.png";
                var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                textureImporter.filterMode = FilterMode.Point;
                textureImporter.mipmapEnabled = false;
                textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                textureImporter.SaveAndReimport();
                string materialPath = $"{Models}/{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
                material.SetColor("_WhiteTint", Color.white);
                material.SetFloat(Prim.FogStrengthProperty, 0f);
                material.SetFloat(Prim.FogExemptMaskProperty, Prim.FogExemptMaskValue);
                EditorUtility.SetDirty(material);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var root = Object.Instantiate(model);
                root.name = name;
                try
                {
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                        renderer.sharedMaterial = material;
                    string key = name.Substring(0, name.IndexOf('_'));
                    PrefabUtility.SaveAsPrefabAsset(root, $"{Prefabs}/{key}.prefab");
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Cosmetics609] Built 12 resource prefabs.");
        }
    }
}
