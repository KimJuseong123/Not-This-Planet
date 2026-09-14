using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Linq;
using Metagame.Core;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 윈도우 빌드 한 방.
    /// CI:  Unity.exe -batchmode -quit -projectPath . -executeMethod Metagame.EditorTools.SpaceBuild.BuildWindows
    ///      (개발 빌드는 BuildWindowsDev. 네임스페이스가 `NotThisPlanet.EditorTools` 로 적혀 있었는데
    ///       실제는 `Metagame.EditorTools` 다 — 그대로 복사하면 `class could not be found` 로 죽는다,
    ///       2026-09-08 실측. `.gitlab-ci.yml` 의 release-windows 잡이 옳은 문자열을 쓰고 있었다.)
    ///
    /// 환경변수
    ///   SPACE_BUILD_DIR : 출력 폴더 (기본 Build/Windows)
    ///   SPACE_DEV       : 1 이면 개발 빌드
    /// </summary>
    public static class SpaceBuild
    {
        static readonly string[] RequiredScenes = BuildRequiredScenes();

        /// <summary>
        /// 빌드에 반드시 들어가야 하는 씬.
        ///
        /// 로비·커스터마이저·메인은 맵과 상관없이 늘 필요하고, <b>맵 씬과 장 완료 연출 씬은
        /// <see cref="StageCatalog"/> 에서 읽는다</b>(2맵 준비). 여기에 손으로 적어 두면 맵을
        /// 더할 때 이 줄을 잊게 되는데, 빌드 목록에서 빠진 씬은 <b>에디터에서는 멀쩡하다가
        /// 빌드에서만</b> 열리지 않는다 — 컴파일이 통과하므로 아무도 미리 알려주지 않는다.
        ///
        /// 순서가 빌드 설정의 씬 인덱스가 되므로 기존 순서를 그대로 지킨다.
        /// </summary>
        static string[] BuildRequiredScenes()
        {
            var scenes = new List<string>
            {
                "Assets/Scenes/Lobby.unity",
                "Assets/Scenes/Customizer.unity",
                "Assets/Scenes/Main.unity",
            };

            foreach (var stage in StageCatalog.All)
            {
                scenes.Add(stage.ScenePath);
                if (!string.IsNullOrEmpty(stage.EndingScenePath))
                    scenes.Add(stage.EndingScenePath);
            }

            return scenes.ToArray();
        }

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
                    Debug.LogError("[NotThisPlanet] 빌드할 씬이 없다 → " + scenePath);
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

        /// <summary>
        /// dev 결정을 환경변수로 박아 두고 빌드한 뒤, **원래 값으로 되돌린다**
        /// (S15P21B202-284).
        ///
        /// <para><b>왜 환경변수로 박는가.</b> <c>BuildStamp</c> 는
        /// <c>BuildPlayerProcessor</c> 라 이 메서드의 지역변수를 볼 수 없고 환경변수만
        /// 읽는다. <c>BuildWindowsDev()</c> 는 인자로 dev 를 켜므로 그 사실이 스탬프에
        /// 전달되지 않았다 — 그러면 개발 빌드가 <c>백엔드=prod</c> 로 찍혀
        /// <b>실유저 서버를 가리킨다.</b></para>
        ///
        /// <para><b>왜 되돌리는가.</b> 박아 놓기만 하면 그 값이 에디터 프로세스에
        /// 남는다. 같은 세션에서 이어서 Build Settings 버튼처럼 <c>SpaceBuild</c> 를
        /// 거치지 않는 빌드를 하면 그 값이 그대로 쓰여 <b>배포용 빌드가
        /// <c>백엔드=dev</c> 로 찍힌다</b>(반대 방향도 성립한다). 배치모드는 프로세스가
        /// 곧 끝나 뜻이 없지만 사람이 쓰는 에디터 세션은 하루를 넘긴다.
        /// <c>!358</c> 의 AI 리뷰가 잡았다.</para>
        ///
        /// <para>본문을 <c>try</c> 로 감싸지 않고 메서드를 가른 이유: 본문에는 중간
        /// <c>return</c> 과 <c>EditorApplication.Exit</c> 경로가 여럿이라, 감싸면 50줄을
        /// 재들여쓰기하면서 그 경로들을 건드릴 위험이 생긴다.</para>
        /// </summary>
        static void Build(bool forceDev)
        {
            bool dev = forceDev || System.Environment.GetEnvironmentVariable("SPACE_DEV") == "1";
            string saved = System.Environment.GetEnvironmentVariable("SPACE_DEV");
            System.Environment.SetEnvironmentVariable("SPACE_DEV", dev ? "1" : "0");
            try
            {
                BuildWith(dev);
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("SPACE_DEV", saved);
            }
        }

        static void BuildWith(bool dev)
        {
            string outDir = System.Environment.GetEnvironmentVariable("SPACE_BUILD_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = "Build/Windows";

            if (Stage1AtmosphereSetup.PrepareProfileForBuild() == null)
            {
                Debug.LogError("[NotThisPlanet] Stage1 분위기 프로필을 빌드용 에셋과 연결하지 못했습니다.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

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
                Debug.LogError("[NotThisPlanet] 활성화된 빌드 씬이 없습니다.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = System.IO.Path.Combine(outDir, "NotThisPlanet.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = dev ? BuildOptions.Development : BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log("[NotThisPlanet] 빌드 " + s.result + " (" + (dev ? "개발용" : "배포용") + ") — "
                      + (s.totalSize / 1048576f).ToString("0.0") + " MB, 오류 " + s.totalErrors
                      + "개 → " + s.outputPath);

            if (Application.isBatchMode && s.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Not This Planet";
            PlayerSettings.productName = "Not This Planet";

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
                "NotThisPlanet/Dough",
                "NotThisPlanet/Ghost",
                "NotThisPlanet/Flat",
                "NotThisPlanet/PlanetNoFog",
            "Metagame/Guidance/NoFog",
                "Metagame/Atmosphere/SkyGradient",
                "Hidden/Metagame/Atmosphere/Haze",
                "Metagame/Atmosphere/Meteor",
                "Metagame/Atmosphere/Dust",
                "Metagame/Atmosphere/DustStormBoundary");
        }
    }
}
