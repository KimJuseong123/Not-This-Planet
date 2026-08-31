using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 윈도우 빌드 한 방.
    /// CI:  Unity.exe -batchmode -quit -projectPath . -executeMethod SpaceFlop.EditorTools.SpaceBuild.BuildWindows
    ///
    /// 환경변수
    ///   SPACE_BUILD_DIR : 출력 폴더 (기본 Build/Windows)
    ///   SPACE_DEV       : 1 이면 개발 빌드
    /// </summary>
    public static class SpaceBuild
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Tools/둥실둥실 우주 대탈출/윈도우 빌드 (배포용)", false, 40)]
        public static void BuildWindows() { Build(false); }

        [MenuItem("Tools/둥실둥실 우주 대탈출/윈도우 빌드 (개발용)", false, 41)]
        public static void BuildWindowsDev() { Build(true); }

        /// <summary>
        /// 병합된 단일 씬이 있고 빌드 목록에 올라 있는지 확인한다.
        ///
        /// 목록을 덮어쓰지 않고 없을 때만 더한다. 예전에는 씬을 다시 만들고 목록을
        /// 통째로 교체했는데, 손으로 구성한 씬을 그렇게 다루면 레벨이 사라진다.
        /// </summary>
        static bool EnsureMainSceneListed()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                Debug.LogError("[SpaceFlop] 빌드할 씬이 없다 → " + ScenePath);
                return false;
            }

            var list = EditorBuildSettings.scenes;
            foreach (var entry in list)
                if (entry.path == ScenePath)
                    return true;

            var grown = new EditorBuildSettingsScene[list.Length + 1];
            System.Array.Copy(list, grown, list.Length);
            grown[list.Length] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = grown;
            return true;
        }

        static void Build(bool forceDev)
        {
            string outDir = System.Environment.GetEnvironmentVariable("SPACE_BUILD_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = "Build/Windows";

            bool dev = forceDev || System.Environment.GetEnvironmentVariable("SPACE_DEV") == "1";

            ArtLink.BuildPrefabs();            // 블렌더 FBX → Resources 프리팹 (런타임이 이걸 집는다)
            if (!EnsureMainSceneListed()) return;
            ApplyPlayerSettings();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = System.IO.Path.Combine(outDir, "SpaceFlop.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = dev ? BuildOptions.Development : BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log("[SpaceFlop] 빌드 " + s.result + " (" + (dev ? "개발용" : "배포용") + ") — "
                      + (s.totalSize / 1048576f).ToString("0.0") + " MB, 오류 " + s.totalErrors
                      + "개 → " + s.outputPath);

            if (Application.isBatchMode && s.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "SpaceFlop";
            PlayerSettings.productName = "둥실둥실 우주 대탈출";

            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.Direct3D11
            });

            // 런타임에 Resources.Load / Shader.Find 로 찾는 셰이더가 빌드에 반드시 들어가게
            EnsureAlwaysIncludedShaders("SpaceFlop/Dough", "SpaceFlop/Ghost", "SpaceFlop/Flat");
        }

        static void EnsureAlwaysIncludedShaders(params string[] shaderNames)
        {
            // AssetDatabase.LoadAllAssetsAtPath 는 Assets/ 아래 자산용이라 ProjectSettings 경로로는
            // 대개 빈 배열을 돌려주고 조용히 return 했다 — 셰이더 포함 보장이 사실상 동작하지 않았다
            // (2026-08-27 전수 AI 리뷰 지적, S15P21B202-136). GraphicsSettings 를 직접 받는다.
            var settingsObject = GraphicsSettings.GetGraphicsSettings();
            if (settingsObject == null)
            {
                Debug.LogWarning("[SpaceFlop] GraphicsSettings 를 열지 못해 AlwaysIncludedShaders 를 확인하지 못했습니다.");
                return;
            }

            var so = new SerializedObject(settingsObject);
            var arrayProp = so.FindProperty("m_AlwaysIncludedShaders");
            if (arrayProp == null)
            {
                Debug.LogWarning("[SpaceFlop] m_AlwaysIncludedShaders 속성을 찾지 못했습니다(Unity 버전 변경?).");
                return;
            }

            foreach (var name in shaderNames)
            {
                var shader = Shader.Find(name);
                if (shader == null) { Debug.LogWarning("[SpaceFlop] 셰이더를 찾지 못함: " + name); continue; }

                bool already = false;
                for (int i = 0; i < arrayProp.arraySize; i++)
                    if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == shader) { already = true; break; }
                if (already) continue;

                arrayProp.InsertArrayElementAtIndex(arrayProp.arraySize);
                arrayProp.GetArrayElementAtIndex(arrayProp.arraySize - 1).objectReferenceValue = shader;
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
    }
}
