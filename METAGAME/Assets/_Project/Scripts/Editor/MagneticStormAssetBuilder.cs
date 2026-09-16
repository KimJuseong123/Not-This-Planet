using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Metagame.Gameplay;

namespace Metagame.EditorTools
{
    public static class MagneticStormAssetBuilder
    {
        public const string Root = "Assets/_Project/Models/Stage1MagneticStorm";
        public const string PrefabPath = Root + "/Resources/Stage1MagneticStorm/Stage1MagneticStorm.prefab";
        const string Generated = Root + "/Unity";
        const string Fbm = Root + "/AstraMagneticStorm.fbm";
        public static string Artifacts => Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/stage1-magnetic-storm"));

        [MenuItem("Metagame/Art/Rebuild Stage1 Magnetic Storm")]
        public static void Build()
        {
            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            Directory.CreateDirectory(Artifacts);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Root + "/AstraMagneticStorm.fbx");
            if (importer == null) throw new InvalidOperationException("Missing magnetic storm FBX.");
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();

            var density = MakeVolume("SmokeDensity_128", 128, 16, TextureWrapMode.Clamp);
            var noise = MakeVolume("SmokeNoise_64", 64, 8, TextureWrapMode.Repeat);
            var smoke = MaterialAt("Smoke", "Metagame/Atmosphere/MagneticStormVolume");
            smoke.SetTexture("_DensityTex", density);
            smoke.SetTexture("_NoiseTex", noise);
            smoke.SetFloat("_Steps", 64);
            smoke.SetFloat("_Density", 2.4f);
            smoke.SetFloat("_BillowStrength", 1f);
            smoke.SetFloat("_Erosion", .22f);
            smoke.SetColor("_ShadowColor", new Color(.28f,.12f,.48f));
            smoke.SetColor("_LightColor", new Color(.90f,.63f,1f));
            smoke.SetFloat("_StormOpacity", 1f);
            var flux = MaterialAt("Flux", "Metagame/Atmosphere/MagneticStormFlux");
            flux.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Fbm + "/Flux_Emission.png"));
            var debrisMaterial = MaterialAt("Debris", "Universal Render Pipeline/Lit");
            debrisMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Fbm + "/StormDebris_BaseColor.png"));
            debrisMaterial.SetColor("_BaseColor", new Color(.65f, .62f, .8f));
            debrisMaterial.SetFloat("_Metallic", .45f);
            debrisMaterial.SetFloat("_Smoothness", .35f);
            foreach (var mat in new[] { smoke, flux, debrisMaterial }) EditorUtility.SetDirty(mat);

            var prefabRoot = new GameObject("Stage1 Magnetic Storm");
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/AstraMagneticStorm.fbx");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                model.transform.SetParent(prefabRoot.transform, false);
                var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != 17) throw new InvalidOperationException("Expected 1 volume, 7 flux and 9 debris meshes, got " + renderers.Length);
                var volume = renderers.Single(r => r.name.StartsWith("SmokeVolume", StringComparison.Ordinal));
                var bounds = volume.GetComponent<MeshFilter>().sharedMesh.bounds;
                Debug.Log("[MagneticStorm] Volume mesh local bounds " + bounds + ", transform " + volume.transform.localToWorldMatrix);
                // The ray marcher samples in the FBX volume mesh's local space.
                if (Vector3.Distance(bounds.size, new Vector3(24, 5, 10)) > .05f || bounds.center.sqrMagnitude > .001f)
                    throw new InvalidOperationException("FBX volume axes do not match Unity density space: " + bounds);
                var debris = renderers.Where(r => r.name.StartsWith("Debris_", StringComparison.Ordinal)).Select(r => r.transform).ToArray();
                foreach (var renderer in renderers)
                {
                    renderer.sharedMaterial = renderer == volume ? smoke
                        : renderer.name.StartsWith("Flux_", StringComparison.Ordinal) ? flux : debrisMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.enabled = true;
                }
                var visual = prefabRoot.AddComponent<MagneticStormVisual>();
                var so = new SerializedObject(visual);
                SetArray(so.FindProperty("animatedRenderers"), renderers);
                SetArray(so.FindProperty("debris"), debris);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(prefabRoot); }
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("[MagneticStorm] Assets and ready-to-use prefab generated.");
        }

        static void SetArray(SerializedProperty array, UnityEngine.Object[] items)
        {
            array.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        static Material MaterialAt(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Shader unavailable: " + shaderName);
            var path = Generated + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader) { name = name }; AssetDatabase.CreateAsset(mat, path); }
            else mat.shader = shader;
            return mat;
        }

        static Texture3D MakeVolume(string name, int size, int columns, TextureWrapMode wrap)
        {
            var imagePath = Fbm + "/" + name + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(imagePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
            if (atlas.width != size * columns || atlas.height != size * (size / columns))
                throw new InvalidOperationException("Density atlas dimensions mismatch: " + name);
            var pixels = atlas.GetPixels32();
            var values = new byte[size * size * size];
            for (int z = 0; z < size; z++)
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        values[(z * size + y) * size + x] = pixels[((z / columns) * size + y) * atlas.width + (z % columns) * size + x].r;
            var texture = new Texture3D(size, size, size, TextureFormat.R8, false)
                { name = name, wrapMode = wrap, filterMode = FilterMode.Bilinear };
            texture.SetPixelData(values, 0);
            texture.Apply(false, true);
            var assetPath = Generated + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(assetPath);
            if (existing != null) { EditorUtility.CopySerialized(texture, existing); UnityEngine.Object.DestroyImmediate(texture); texture = existing; }
            else AssetDatabase.CreateAsset(texture, assetPath);
            EditorUtility.SetDirty(texture);
            importer.isReadable = false;
            importer.SaveAndReimport();
            return texture;
        }

        public static void Validate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || prefab.GetComponent<MagneticStormVisual>() == null)
                throw new InvalidOperationException("Prefab or visual component missing.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var visual = instance.GetComponent<MagneticStormVisual>();
                visual.SetExternallyDriven();
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                    if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null || !renderer.sharedMaterial.shader.isSupported)
                        throw new InvalidOperationException("Invalid renderer material: " + renderer.name);
                foreach (var shader in new[] { "Metagame/Atmosphere/MagneticStormVolume", "Metagame/Atmosphere/MagneticStormFlux" })
                {
                    var errors = ShaderUtil.GetShaderMessages(Shader.Find(shader)).Where(m => m.severity.ToString() == "Error").ToArray();
                    if (errors.Length != 0) throw new InvalidOperationException(string.Join("\n", errors.Select(m => m.message)));
                }
                var debris = instance.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Debris_")).ToArray();
                visual.Evaluate(0);
                var initial = debris.Select(d => d.localPosition).ToArray();
                visual.Evaluate(1.5);
                if (!debris.Where((d, i) => Vector3.Distance(d.localPosition, initial[i]) > .01f).Any())
                    throw new InvalidOperationException("Debris does not animate.");
                visual.Evaluate(6);
                for (int i = 0; i < debris.Length; i++)
                    if (Vector3.Distance(debris[i].localPosition, initial[i]) > .0001f)
                        throw new InvalidOperationException("Loop seam on debris.");
                instance.SetActive(false); instance.SetActive(true); visual.Evaluate(12);
                var report = new {
                    unity = Application.unityVersion, prefab = PrefabPath, renderers = renderers.Length,
                    volumeCount = 1, fluxCount = 7, debrisCount = debris.Length,
                    loopSeconds = MagneticStormVisual.LoopSeconds, density = "128^3 R8", noise = "64^3 R8",
                    shaderErrors = 0, loopAndReactivation = "passed",
                    playerBuild = "not run", multiplayer = "not run", devicePerformance = "not measured"
                };
                File.WriteAllText(Path.Combine(Artifacts, "unity-validation.json"), Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
    }
}
