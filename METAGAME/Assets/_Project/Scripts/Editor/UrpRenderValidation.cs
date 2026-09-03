using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Metagame.Gameplay;

namespace Metagame.EditorTools
{
    /// <summary>
    /// Prevents a build from silently producing magenta materials because the active
    /// pipeline, material shader, renderer feature, or runtime shader dependency is invalid.
    /// </summary>
    public static class UrpRenderValidation
    {
        const string PcAssetPath = "Assets/Settings/PC_RPAsset.asset";
        const string MobileAssetPath = "Assets/Settings/Mobile_RPAsset.asset";
        const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";
        const string MobileRendererPath = "Assets/Settings/Mobile_Renderer.asset";
        const string BaselineVolumePath = "Assets/_Project/Data/Atmosphere/URPBaselineVolumeProfile.asset";
        const string Stage1ProfilePath = "Assets/_Project/Data/Atmosphere/Stage1AtmosphereProfile.asset";
        const string MainScenePath = "Assets/Scenes/Main.unity";
        const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        const string MaterialRoot = "Assets/_Project/Materials";
        const string ProjectRoot = "Assets/_Project";
        const string RuntimeScriptRoot = "Assets/_Project/Scripts/Runtime";

        static readonly string[] RuntimeResourceShaders =
        {
            "DoughSurface",
            "GhostSurface",
            "FlatUnlit"
        };

        static readonly string[] ProtectedShaderNames =
        {
            "SpaceFlop/Dough",
            "SpaceFlop/Ghost",
            "SpaceFlop/Flat",
            "SpaceFlop/PlanetNoFog",
            "Metagame/Atmosphere/SkyGradient",
            "Hidden/Metagame/Atmosphere/Haze",
            "Metagame/Atmosphere/Meteor"
        };

        [MenuItem("Metagame/Rendering/Validate URP Build Safety")]
        public static void ValidateFromMenu()
        {
            bool valid = ValidateProject(true);
            if (valid) Debug.Log("[URP] 빌드 안전성 검증 통과.");
        }

        public static bool ValidateProject(bool logErrors)
        {
            var errors = new List<string>();
            ValidatePipelineAssets(errors);
            ValidateStage1Profile(errors);
            ValidateQualityLevels(errors);
            ValidateMaterials(errors);
            ValidateShaders(errors);
            ValidateRuntimeShaderReferences(errors);
            ValidateAlwaysIncludedShaders(errors);
            ValidateRendererFeatures(errors);

            if (errors.Count == 0) return true;
            if (logErrors)
            {
                foreach (string error in errors) Debug.LogError("[URP] " + error);
            }
            return false;
        }

        static void ValidatePipelineAssets(List<string> errors)
        {
            if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset))
                errors.Add("GraphicsSettings.defaultRenderPipeline이 UniversalRenderPipelineAsset가 아닙니다.");

            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcAssetPath) == null)
                errors.Add("PC_RPAsset.asset를 찾지 못했습니다.");
            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileAssetPath) == null)
                errors.Add("Mobile_RPAsset.asset를 찾지 못했습니다.");
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(BaselineVolumePath) == null)
                errors.Add("공용 URP 기본 Volume Profile을 찾지 못했습니다.");

            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled || string.IsNullOrEmpty(scene.path)) continue;
                if (scene.path != "Assets/Scenes/Main.unity" && scene.path != "Assets/Scenes/Lobby.unity") continue;
                var dependencies = AssetDatabase.GetDependencies(scene.path, false);
                if (Array.IndexOf(dependencies, BaselineVolumePath) < 0)
                    errors.Add(scene.path + "가 공용 URP 기본 Volume Profile을 참조하지 않습니다.");
            }
        }

        static void ValidateStage1Profile(List<string> errors)
        {
            var profile = AssetDatabase.LoadAssetAtPath<AtmosphereProfile>(Stage1ProfilePath);
            if (profile == null)
            {
                errors.Add("Stage1 분위기 프로필을 찾지 못했습니다: " + Stage1ProfilePath);
                return;
            }

            if (!File.Exists(MainScenePath))
            {
                errors.Add("Stage1 Main 씬을 찾지 못했습니다: " + MainScenePath);
                return;
            }

            var mainDependencies = AssetDatabase.GetDependencies(MainScenePath, true);
            if (Array.IndexOf(mainDependencies, Stage1ProfilePath) < 0)
                errors.Add("Main 씬이 Stage1 분위기 프로필을 참조하지 않습니다.");

            if (File.Exists(LobbyScenePath))
            {
                var lobbyDependencies = AssetDatabase.GetDependencies(LobbyScenePath, true);
                if (Array.IndexOf(lobbyDependencies, Stage1ProfilePath) >= 0)
                    errors.Add("Lobby 씬이 Stage1 전용 분위기 프로필을 참조합니다.");
            }

            if (profile.meteorShower == null)
            {
                errors.Add("Stage1 분위기 프로필의 MeteorShowerSettings가 비어 있습니다.");
                return;
            }

            if (profile.meteorShower.maxParticles < 0 || profile.meteorShower.maxParticles > 64)
                errors.Add("Stage1 시각 유성우 파티클 수가 0~64 범위를 벗어납니다.");
            if (profile.meteorShower.alertEmissionMultiplier < 1f)
                errors.Add("Stage1 시각 유성우 경고 배율이 1보다 작습니다.");
        }

        static void ValidateQualityLevels(List<string> errors)
        {
            if (QualitySettings.names == null || QualitySettings.names.Length == 0)
            {
                errors.Add("Quality Level이 하나도 없습니다.");
                return;
            }

            int current = Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, QualitySettings.names.Length - 1);
            var pcAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcAssetPath);
            var mobileAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileAssetPath);
            for (int index = 0; index < QualitySettings.names.Length; index++)
            {
                QualitySettings.SetQualityLevel(index, false);
                var pipeline = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
                if (pipeline == null)
                    errors.Add("Quality Level '" + QualitySettings.names[index] + "'이 Built-in으로 fallback됩니다.");
                else if (index < 3 && pipeline != mobileAsset)
                    errors.Add("Quality Level '" + QualitySettings.names[index] + "'이 Mobile_RPAsset를 사용하지 않습니다.");
                else if (index >= 3 && pipeline != pcAsset)
                    errors.Add("Quality Level '" + QualitySettings.names[index] + "'이 PC_RPAsset를 사용하지 않습니다.");
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        static void ValidateMaterials(List<string> errors)
        {
            var materialPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // All first-party material assets are checked directly.
            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { MaterialRoot });
            foreach (string guid in materialGuids)
                materialPaths.Add(AssetDatabase.GUIDToAssetPath(guid));

            // Scene/prefab dependencies are checked as well. This catches a Built-in
            // material hidden inside an imported model or a prefab, without making the
            // immutable source files of unused third-party packages build blockers.
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled || string.IsNullOrEmpty(scene.path)) continue;
                AddMaterialDependencies(scene.path, materialPaths);
            }

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { ProjectRoot });
            foreach (string guid in prefabGuids)
                AddMaterialDependencies(AssetDatabase.GUIDToAssetPath(guid), materialPaths);

            foreach (string path in materialPaths)
                ValidateMaterial(path, errors);
        }

        static void AddMaterialDependencies(string assetPath, HashSet<string> materialPaths)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath)) return;
            foreach (string dependency in AssetDatabase.GetDependencies(assetPath, true))
            {
                if (dependency.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    materialPaths.Add(dependency);
            }
        }

        static void ValidateMaterial(string path, List<string> errors)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null)
            {
                errors.Add("셰이더가 없는 머티리얼: " + path);
                return;
            }

            if (IsBuiltInShader(material.shader))
                errors.Add("Built-in 셰이더를 사용하는 머티리얼: " + path + " (" + material.shader.name + ")");
        }

        static void ValidateRuntimeShaderReferences(List<string> errors)
        {
            // Prim resolves these by Resources.Load first so they survive shader
            // stripping even when no serialized Material references them.
            foreach (string resourceName in RuntimeResourceShaders)
            {
                if (Resources.Load<Shader>(resourceName) == null)
                    errors.Add("Resources 셰이더를 찾지 못했습니다: " + resourceName);
            }

            // Also catch literal Shader.Find calls added to runtime code. Dynamic
            // names should be added to the explicit required-shader list below.
            string[] scriptGuids = AssetDatabase.FindAssets("t:Script", new[] { RuntimeScriptRoot });
            foreach (string guid in scriptGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!File.Exists(path)) continue;
                string source = File.ReadAllText(path);
                foreach (Match match in Regex.Matches(source, "Shader\\.Find\\s*\\(\\s*\\\"([^\\\"]+)\\\""))
                {
                    string shaderName = match.Groups[1].Value;
                    if (IsBuiltInShaderName(shaderName))
                    {
                        errors.Add("런타임 코드가 Built-in 셰이더를 조회합니다: " + path + " (" + shaderName + ")");
                        continue;
                    }
                    if (Shader.Find(shaderName) == null)
                        errors.Add("런타임 Shader.Find 대상이 없습니다: " + path + " (" + shaderName + ")");
                }
            }
        }

        static void ValidateAlwaysIncludedShaders(List<string> errors)
        {
            var settingsObject = GraphicsSettings.GetGraphicsSettings();
            if (settingsObject == null)
            {
                errors.Add("Graphics Settings 오브젝트를 찾지 못했습니다.");
                return;
            }

            var serialized = new SerializedObject(settingsObject);
            var array = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (array == null)
            {
                errors.Add("Always Included Shaders 목록을 읽지 못했습니다.");
                return;
            }

            foreach (string shaderName in ProtectedShaderNames)
            {
                var shader = Shader.Find(shaderName);
                bool included = false;
                for (int index = 0; shader != null && index < array.arraySize; index++)
                {
                    if (array.GetArrayElementAtIndex(index).objectReferenceValue == shader)
                    {
                        included = true;
                        break;
                    }
                }
                if (!included)
                    errors.Add("런타임 셰이더가 Always Included Shaders에 없습니다: " + shaderName);
            }
        }

        static bool IsBuiltInShader(Shader shader)
        {
            return shader != null && IsBuiltInShaderName(shader.name);
        }

        static bool IsBuiltInShaderName(string shaderName)
        {
            return shaderName == "Standard" ||
                   shaderName == "Diffuse" ||
                   shaderName == "Legacy Shaders/Diffuse" ||
                   shaderName == "Legacy Shaders/Specular";
        }

        static void ValidateShaders(List<string> errors)
        {
            string[] shaderNames =
            {
                "SpaceFlop/Dough",
                "SpaceFlop/Ghost",
                "SpaceFlop/Flat",
                "SpaceFlop/PlanetNoFog",
                "Universal Render Pipeline/Lit",
                "Metagame/Atmosphere/SkyGradient",
                "Hidden/Metagame/Atmosphere/Haze",
                "Metagame/Atmosphere/Meteor"
            };

            foreach (string shaderName in shaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    errors.Add("필수 셰이더를 찾지 못했습니다: " + shaderName);
                    continue;
                }

                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                {
                    if (message.severity == ShaderCompilerMessageSeverity.Error)
                        errors.Add(shaderName + " 컴파일 오류: " + message.message);
                }
            }
        }

        static void ValidateRendererFeatures(List<string> errors)
        {
            ValidateRenderer(PcRendererPath, errors, false, true);
            ValidateRenderer(MobileRendererPath, errors, true, false);
        }

        static void ValidateRenderer(string path, List<string> errors, bool mobile, bool expectedActive)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (renderer == null)
            {
                errors.Add("Renderer Data를 찾지 못했습니다: " + path);
                return;
            }

            if (!renderer.TryGetRendererFeature<AtmosphereHazeRendererFeature>(out var feature))
            {
                errors.Add("AtmosphereHazeRendererFeature가 등록되지 않았습니다: " + path);
                return;
            }
            if (feature.PassMaterial == null || feature.PassMaterial.shader == null)
                errors.Add("Atmosphere Haze Feature의 Pass Material이 비어 있습니다: " + path);
            if (feature.isActive != expectedActive)
                errors.Add((mobile ? "Mobile" : "PC") + " Renderer의 Haze Feature 활성 상태가 잘못되었습니다: " + path);
            if (mobile && feature.EnableOnMobile)
                errors.Add("Mobile Renderer의 Haze Feature가 모바일 플랫폼에서 허용되어 있습니다: " + path);
        }
    }

    public sealed class UrpBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return -1000; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!UrpRenderValidation.ValidateProject(true))
                throw new BuildFailedException("URP 빌드 안전성 검증에 실패했습니다. Console의 [URP] 오류를 해결한 뒤 다시 빌드하세요.");
        }
    }
}
