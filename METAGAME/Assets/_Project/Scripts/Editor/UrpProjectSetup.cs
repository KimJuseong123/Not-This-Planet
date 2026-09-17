using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;

namespace Metagame.EditorTools
{
    /// <summary>
    /// Idempotent editor migration for the project's URP baseline.
    /// Run from the menu or with -executeMethod Metagame.EditorTools.UrpProjectSetup.Apply.
    /// </summary>
    public static class UrpProjectSetup
    {
        const string PcAssetPath = "Assets/Settings/PC_RPAsset.asset";
        const string MobileAssetPath = "Assets/Settings/Mobile_RPAsset.asset";
        const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";
        const string MobileRendererPath = "Assets/Settings/Mobile_Renderer.asset";
        const string BaselineVolumePath = "Assets/_Project/Data/Atmosphere/URPBaselineVolumeProfile.asset";
        const string HazeMaterialPath = "Assets/_Project/Materials/Rendering/M_Atmosphere_Haze.mat";
        // 핵심 파츠 테두리 (S15P21B202-562). 마스크와 선 그리기가 각각 재질 하나를 쓴다.
        const string PartOutlineMaskMaterialPath = "Assets/_Project/Materials/Rendering/M_PartOutlineMask.mat";
        const string PartOutlineMaterialPath = "Assets/_Project/Materials/Rendering/M_PartOutline.mat";
        const string MirrorMaterialDirectory = "Assets/_Project/Materials/Rendering/Mirror";

        [MenuItem("Metagame/Rendering/Apply URP Setup")]
        public static void Apply()
        {
            AssetDatabase.Refresh();

            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcAssetPath);
            var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileAssetPath);
            if (pc == null || mobile == null)
                throw new InvalidOperationException("PC_RPAsset 또는 Mobile_RPAsset를 찾지 못했습니다.");

            var baselineVolume = EnsureBaselineVolumeProfile();
            var hazeMaterial = EnsureHazeMaterial();
            var outlineMaskMaterial = EnsureMaterial(
                PartOutlineMaskMaterialPath, "Hidden/Metagame/Rendering/PartOutlineMask", "M_PartOutlineMask");
            var outlineMaterial = EnsureMaterial(
                PartOutlineMaterialPath, "Hidden/Metagame/Rendering/PartOutline", "M_PartOutline");
            ConfigurePipelineAssets(pc, mobile, baselineVolume);
            ConfigureQualityLevels(pc, mobile);
            ConfigureRenderer(PcRendererPath, hazeMaterial, false);
            ConfigureRenderer(MobileRendererPath, hazeMaterial, true);
            ConfigurePartOutline(PcRendererPath, outlineMaskMaterial, outlineMaterial, false);
            ConfigurePartOutline(MobileRendererPath, outlineMaskMaterial, outlineMaterial, true);
            MigrateProjectMaterials();
            CreateMirrorMaterialCopies();
            UpdateSceneVolumes(baselineVolume);
            EnsureAlwaysIncludedShaders(
                "NotThisPlanet/Dough",
                "NotThisPlanet/Ghost",
                "NotThisPlanet/Flat",
                "NotThisPlanet/PlanetNoFog",
                "Metagame/Guidance/NoFog",
                "Metagame/Atmosphere/SkyGradient",
                "Hidden/Metagame/Atmosphere/Haze",
                "Hidden/Metagame/Rendering/PartOutlineMask",
                "Hidden/Metagame/Rendering/PartOutline",
                "Metagame/Atmosphere/Meteor",
                "Metagame/Atmosphere/Dust",
                "Metagame/Atmosphere/DustStormBoundary");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[URP] 프로젝트 URP 설정 및 머티리얼 이관 완료.");
        }

        static VolumeProfile EnsureBaselineVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(BaselineVolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "URP Baseline Volume Profile";
                AssetDatabase.CreateAsset(profile, BaselineVolumePath);
            }

            var tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.Neutral;

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.active = false;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0f;

            var color = GetOrAdd<ColorAdjustments>(profile);
            color.active = false;
            color.postExposure.overrideState = true;
            color.postExposure.value = 0f;
            color.contrast.overrideState = true;
            color.contrast.value = 0f;
            color.colorFilter.overrideState = true;
            color.colorFilter.value = Color.white;

            var vignette = GetOrAdd<Vignette>(profile);
            vignette.active = false;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0f;

            EditorUtility.SetDirty(profile);
            return profile;
        }

        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T component)) return component;
            return profile.Add<T>(true);
        }

        static Material EnsureHazeMaterial()
        {
            var shader = Shader.Find("Hidden/Metagame/Atmosphere/Haze");
            if (shader == null) throw new InvalidOperationException("Haze 셰이더를 찾지 못했습니다.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(HazeMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_Atmosphere_Haze" };
                AssetDatabase.CreateAsset(material, HazeMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        /// <summary>경로에 재질이 없으면 만들고, 셰이더가 다르면 맞춘다. 여러 번 불러도 같다.</summary>
        static Material EnsureMaterial(string path, string shaderName, string materialName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException(shaderName + " 셰이더를 찾지 못했습니다.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        /// <summary>
        /// 핵심 파츠 테두리 피처를 등록한다 (S15P21B202-562).
        ///
        /// <para>Haze 와 같은 규칙이다 — 모바일 렌더러에도 <b>등록은 하되 꺼 둔다.</b>
        /// 나중에 켤 수 있게 자리는 남기고, Mobile/Low/Medium 이 전체화면 패스 값을
        /// 치르지 않게 한다.</para>
        /// </summary>
        static void ConfigurePartOutline(string path, Material maskMaterial,
                                         Material outlineMaterial, bool mobileRenderer)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (renderer == null) throw new InvalidOperationException("Renderer Data를 찾지 못했습니다: " + path);

            var feature = renderer.rendererFeatures.OfType<PartOutlineRendererFeature>().FirstOrDefault();
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<PartOutlineRendererFeature>();
                feature.name = "Part Outline";
                feature.hideFlags = HideFlags.HideInInspector;
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
            }

            feature.MaskMaterial = maskMaterial;
            feature.OutlineMaterial = outlineMaterial;
            feature.SetActive(!mobileRenderer);
            renderer.SetDirty();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
        }

        static void ConfigurePipelineAssets(
            UniversalRenderPipelineAsset pc,
            UniversalRenderPipelineAsset mobile,
            VolumeProfile baselineVolume)
        {
            GraphicsSettings.defaultRenderPipeline = pc;
            pc.volumeProfile = baselineVolume;
            mobile.volumeProfile = baselineVolume;
            pc.useSRPBatcher = true;
            mobile.useSRPBatcher = true;
            pc.supportsHDR = true;
            mobile.supportsHDR = true;
            EditorUtility.SetDirty(pc);
            EditorUtility.SetDirty(mobile);
        }

        static void ConfigureQualityLevels(
            UniversalRenderPipelineAsset pc,
            UniversalRenderPipelineAsset mobile)
        {
            int current = Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, QualitySettings.names.Length - 1);
            for (int index = 0; index < QualitySettings.names.Length; index++)
            {
                QualitySettings.SetQualityLevel(index, false);
                QualitySettings.renderPipeline = index <= 2 ? mobile : pc;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        static void ConfigureRenderer(string path, Material hazeMaterial, bool mobileRenderer)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (renderer == null) throw new InvalidOperationException("Renderer Data를 찾지 못했습니다: " + path);

            var feature = renderer.rendererFeatures.OfType<AtmosphereHazeRendererFeature>().FirstOrDefault();
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<AtmosphereHazeRendererFeature>();
                feature.name = "Atmosphere Haze";
                feature.hideFlags = HideFlags.HideInInspector;
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
            }

            feature.PassMaterial = hazeMaterial;
            feature.EnableOnMobile = false;
            // The mobile renderer keeps the feature registered for future tuning,
            // but its default active state is off so Mobile/Low/Medium never pay
            // for the full-screen depth pass.
            feature.SetActive(!mobileRenderer);
            renderer.SetDirty();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
        }

        static void MigrateProjectMaterials()
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) throw new InvalidOperationException("Universal Render Pipeline/Lit 셰이더를 찾지 못했습니다.");

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project/Materials" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || !IsBuiltInSurface(material.shader)) continue;
                ConvertSurfaceMaterial(material, urpLit);
            }
        }

        static bool IsBuiltInSurface(Shader shader)
        {
            if (shader == null) return true;
            return shader.name == "Standard" ||
                   shader.name == "Legacy Shaders/Specular" ||
                   shader.name == "Legacy Shaders/Diffuse";
        }

        static void ConvertSurfaceMaterial(Material material, Shader urpLit)
        {
            Texture mainTexture = material.GetTexture("_MainTex");
            Vector2 mainScale = material.GetTextureScale("_MainTex");
            Vector2 mainOffset = material.GetTextureOffset("_MainTex");
            Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
            float smoothness = material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : 0.2f;
            Texture metallicTexture = material.GetTexture("_MetallicGlossMap");
            Texture normalTexture = material.GetTexture("_BumpMap");
            float bumpScale = material.HasProperty("_BumpScale") ? material.GetFloat("_BumpScale") : 1f;
            Texture emissionTexture = material.GetTexture("_EmissionMap");
            Color emissionColor = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
            bool emission = material.IsKeywordEnabled("_EMISSION") || emissionTexture != null || emissionColor.maxColorComponent > 0.001f;

            material.shader = urpLit;
            material.SetColor("_BaseColor", color);
            if (mainTexture != null)
            {
                material.SetTexture("_BaseMap", mainTexture);
                material.SetTextureScale("_BaseMap", mainScale);
                material.SetTextureOffset("_BaseMap", mainOffset);
            }
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
            if (metallicTexture != null)
            {
                material.SetTexture("_MetallicGlossMap", metallicTexture);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (normalTexture != null)
            {
                material.SetTexture("_BumpMap", normalTexture);
                material.SetFloat("_BumpScale", bumpScale);
                material.EnableKeyword("_NORMALMAP");
            }
            if (emissionTexture != null) material.SetTexture("_EmissionMap", emissionTexture);
            material.SetColor("_EmissionColor", emissionColor);
            if (emission) material.EnableKeyword("_EMISSION");

            EditorUtility.SetDirty(material);
        }

        static void CreateMirrorMaterialCopies()
        {
            Directory.CreateDirectory(MirrorMaterialDirectory);
            var ghostShader = Shader.Find("NotThisPlanet/Ghost");
            if (ghostShader == null) return;

            CopyGhostMaterial(
                "Assets/_Project/ThirdParty/Mirror/Components/PredictedRigidbody/LocalGhostMaterial.mat",
                MirrorMaterialDirectory + "/LocalGhostMaterial_URP.mat",
                ghostShader);
            CopyGhostMaterial(
                "Assets/_Project/ThirdParty/Mirror/Components/PredictedRigidbody/RemoteGhostMaterial.mat",
                MirrorMaterialDirectory + "/RemoteGhostMaterial_URP.mat",
                ghostShader);
        }

        static void CopyGhostMaterial(string sourcePath, string destinationPath, Shader shader)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source == null) return;

            var copy = AssetDatabase.LoadAssetAtPath<Material>(destinationPath);
            if (copy == null)
            {
                copy = new Material(shader) { name = Path.GetFileNameWithoutExtension(destinationPath) };
                AssetDatabase.CreateAsset(copy, destinationPath);
            }
            else copy.shader = shader;

            Color color = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            copy.SetColor("_Color", color);
            copy.SetFloat("_RimBoost", 1.6f);
            copy.SetFloat("_Pulse", 0f);
            EditorUtility.SetDirty(copy);
        }

        static void UpdateSceneVolumes(VolumeProfile baselineVolume)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string activePath = SceneManager.GetActiveScene().path;
            string[] scenePaths = { "Assets/Scenes/Lobby.unity", "Assets/Scenes/Main.unity" };
            foreach (string path in scenePaths)
            {
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool changed = false;
                foreach (var volume in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
                {
                    if (!volume.isGlobal || volume.name != "Global Volume") continue;
                    if (volume.sharedProfile == baselineVolume) continue;
                    volume.sharedProfile = baselineVolume;
                    EditorUtility.SetDirty(volume);
                    changed = true;
                }
                if (changed) EditorSceneManager.SaveScene(scene);
            }

            if (!string.IsNullOrEmpty(activePath) && File.Exists(activePath))
                EditorSceneManager.OpenScene(activePath, OpenSceneMode.Single);
        }

        public static void EnsureAlwaysIncludedShaders(params string[] shaderNames)
        {
            var settingsObject = GraphicsSettings.GetGraphicsSettings();
            if (settingsObject == null) return;

            var serialized = new SerializedObject(settingsObject);
            var array = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (array == null) return;

            foreach (string shaderName in shaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogError("[URP] Always Included 대상 셰이더를 찾지 못했습니다: " + shaderName);
                    continue;
                }

                bool exists = false;
                for (int index = 0; index < array.arraySize; index++)
                {
                    if (array.GetArrayElementAtIndex(index).objectReferenceValue == shader)
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists) continue;

                array.InsertArrayElementAtIndex(array.arraySize);
                array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = shader;
            }

            serialized.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Applies the migration once after an editor domain reload so the checked-in
    /// project settings and the generated Unity assets cannot drift apart.
    /// The menu entry remains available for a manual re-run after package changes.
    /// </summary>
    [InitializeOnLoad]
    static class UrpProjectSetupAutoRunner
    {
        const string SessionKey = "Metagame.URPSetupApplied";

        static UrpProjectSetupAutoRunner()
        {
            EditorApplication.delayCall += Run;
        }

        static void Run()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset)) return;

            // Mark before AssetDatabase.Refresh so a refresh/domain reload triggered
            // by Apply cannot recursively run the migration a second time.
            SessionState.SetBool(SessionKey, true);
            try
            {
                UrpProjectSetup.Apply();
            }
            catch (Exception exception)
            {
                SessionState.SetBool(SessionKey, false);
                Debug.LogException(exception);
            }
        }
    }
}
