using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 윈도우 빌드 한 방.
    /// CI:  Unity.exe -batchmode -quit -projectPath . -executeMethod Metagame.EditorTools.SpaceBuild.BuildWindows
    ///      (개발 빌드는 BuildWindowsDev. 네임스페이스가 `SpaceFlop.EditorTools` 로 적혀 있었는데
    ///       실제는 `Metagame.EditorTools` 다 — 그대로 복사하면 `class could not be found` 로 죽는다,
    ///       2026-09-08 실측. `.gitlab-ci.yml` 의 release-windows 잡이 옳은 문자열을 쓰고 있었다.)
    ///
    /// 환경변수
    ///   SPACE_BUILD_DIR : 출력 폴더 (기본 Build/Windows)
    ///   SPACE_DEV       : 1 이면 개발 빌드
    /// </summary>
    public static class SpaceBuild
    {
        static readonly string[] RequiredScenes = { "Assets/Scenes/Lobby.unity", "Assets/Scenes/Main.unity" };

        public static void BuildWindows() { Build(false); }

        public static void BuildWindowsDev() { Build(true); }

        /// <summary>
        /// 병합된 단일 씬이 있고 빌드 목록에 올라 있는지 확인한다.
        ///
        /// 목록을 덮어쓰지 않고 없을 때만 더한다. 예전에는 씬을 다시 만들고 목록을
        /// 통째로 교체했는데, 손으로 구성한 씬을 그렇게 다루면 레벨이 사라진다.
        /// </summary>
        static bool EnsureRequiredScenesListed()
        {
            foreach (var scenePath in RequiredScenes)
            {
                if (!System.IO.File.Exists(scenePath))
                {
                    Debug.LogError("[SpaceFlop] 빌드할 씬이 없다 → " + scenePath);
                    return false;
                }
            }

            var list = EditorBuildSettings.scenes;
            var entries = list.ToList();
            foreach (var scenePath in RequiredScenes)
            {
                var existing = entries.FirstOrDefault(entry => entry.path == scenePath);
                if (existing != null)
                    existing.enabled = true;
                else
                    entries.Add(new EditorBuildSettingsScene(scenePath, true));
            }
            EditorBuildSettings.scenes = entries.ToArray();
            return true;
        }

        static string[] EnabledBuildScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrEmpty(scene.path))
                .Select(scene => scene.path)
                .ToArray();
        }

        static void Build(bool forceDev)
        {
            string outDir = System.Environment.GetEnvironmentVariable("SPACE_BUILD_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = "Build/Windows";

            bool dev = forceDev || System.Environment.GetEnvironmentVariable("SPACE_DEV") == "1";

            ArtLink.BuildPrefabs();            // 블렌더 FBX → Resources 프리팹 (런타임이 이걸 집는다)
            if (!EnsureRequiredScenesListed()) return;
            ApplyPlayerSettings();
            if (!UrpRenderValidation.ValidateProject(true))
            {
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var scenes = EnabledBuildScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[SpaceFlop] 활성화된 빌드 씬이 없습니다.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
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
            UrpProjectSetup.EnsureAlwaysIncludedShaders(
                "SpaceFlop/Dough",
                "SpaceFlop/Ghost",
                "SpaceFlop/Flat",
                "SpaceFlop/PlanetNoFog",
                "Metagame/Atmosphere/SkyGradient",
                "Hidden/Metagame/Atmosphere/Haze",
                "Metagame/Atmosphere/Meteor");
        }
    }
}
