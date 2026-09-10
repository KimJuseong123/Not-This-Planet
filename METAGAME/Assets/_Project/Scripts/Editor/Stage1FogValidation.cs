using System;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    /// <summary>Isolated batch-only visual regression check; never saves a scene or shared settings.</summary>
    public static class Stage1FogValidation
    {
        private const string Pending = "Stage1FogValidation.Pending";
        private const string Started = "Stage1FogValidation.Started";

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (!Application.isBatchMode || !Environment.GetCommandLineArgs().Contains("-stage1-fog-validation")) return;
            // Avoid the unrelated automatic URP migration while collecting an isolated baseline.
            SessionState.SetBool("Metagame.URPSetupApplied", true);
            EditorApplication.update -= Poll;
            if (SessionState.GetBool(Pending, false)) EditorApplication.update += Poll;
        }

        public static void Run()
        {
            if (!Application.isBatchMode || !Environment.GetCommandLineArgs().Contains("-stage1-fog-validation"))
                throw new InvalidOperationException("Run only in an isolated batch editor with -stage1-fog-validation.");
            Stage1AstraIntegration.Validate();
            AssertFog();
            var camera = Object.FindFirstObjectByType<Camera>();
            camera.transform.position = new Vector3(-22, 2, -29);
            camera.transform.LookAt(new Vector3(0, 3, 0));
            camera.farClipPlane = 400;
            camera.clearFlags = CameraClearFlags.Skybox;
            CapturePair(camera, "ground");
            camera.transform.position = new Vector3(45, 40, -55);
            camera.transform.LookAt(new Vector3(0, 1, 0));
            CapturePair(camera, "overview");
            // Reload settings from disk, discarding all preview camera changes.
            EditorSceneManager.OpenScene(Stage1AstraIntegration.ScenePath);
            SessionState.SetBool(Pending, true);
            SessionState.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorApplication.EnterPlaymode();
        }

        public static void TestShader()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch validation only.");
            Stage1AstraIntegration.Validate();
            var camera = Object.FindFirstObjectByType<Camera>();
            camera.transform.position = new Vector3(-22, 2, -29);
            camera.transform.LookAt(new Vector3(0, 3, 0));
            camera.farClipPlane = 400;
            camera.clearFlags = CameraClearFlags.Skybox;
            bool originalFog = RenderSettings.fog;
            var originalPipeline = QualitySettings.renderPipeline;
            try
            {
                for (int mode = 0; mode < 3; mode++)
                {
                    Stage1FogTestSession.SetMode(mode);
                    Capture(camera, "shader-test-" + mode);
                }
                Stage1FogTestSession.ConfigureMist(0, 0, 5, .08f);
                Capture(camera, "volume-off");
                Stage1FogTestSession.ConfigureMist(.35f, 0, 5, .08f);
                Capture(camera, "volume-on");
                camera.transform.position += camera.transform.forward * 12;
                Capture(camera, "volume-close");
            }
            finally { Stage1FogTestSession.Cleanup(); }
            if (RenderSettings.fog != originalFog || QualitySettings.renderPipeline != originalPipeline)
                throw new InvalidOperationException("Comparison cleanup did not restore original settings.");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/Atmosphere/Stage1DistanceFog.shader");
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Fog shader compile errors.");
            Debug.Log("[Fog411] SHADER TEST PASS: three modes rendered; pipeline/fog restored.");
        }

        private static void AssertFog()
        {
            if (!RenderSettings.fog || RenderSettings.fogMode != FogMode.Linear
                || !Mathf.Approximately(RenderSettings.fogStartDistance, 15)
                || !Mathf.Approximately(RenderSettings.fogEndDistance, 90))
                throw new InvalidOperationException("Stage1 distance fog settings were lost.");
        }

        private static void Poll()
        {
            float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(Started, 0);
            if (!EditorApplication.isPlaying && elapsed < 120) return;
            if (EditorApplication.isPlaying && elapsed < 15) return;
            EditorApplication.update -= Poll;
            SessionState.SetBool(Pending, false);
            try
            {
                if (!EditorApplication.isPlaying) throw new TimeoutException("Play Mode did not start.");
                var runtimeFog = Metagame.Gameplay.Stage1FogController.Active;
                if (runtimeFog == null || !runtimeFog.ShaderEnabled || runtimeFog.Material == null || RenderSettings.fog)
                    throw new InvalidOperationException("Stage1 runtime fog did not activate automatically or basic fog is doubled.");
                var boot = Object.FindFirstObjectByType<SpaceBootstrap>();
                if (boot == null || boot.Player == null) throw new InvalidOperationException("Player was not spawned.");
                var camera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c => c.enabled);
                Capture(camera, "runtime-auto-play");
                Debug.Log("[Fog411] AUTO PLAY PASS: runtime fog enabled without comparison window.");
                try
                {
                    for (int mode = 0; mode < 3; mode++)
                    {
                        Stage1FogTestSession.SetMode(mode);
                        Capture(camera, "shader-play-" + mode);
                    }
                }
                finally { Stage1FogTestSession.Cleanup(); }
                AssertFog();
                Debug.Log("[Fog411] SHADER PLAY PASS: three comparison modes rendered and cleaned up.");
                Debug.Log("[Fog411] PLAY PASS: fog survives bootstrap; actual player camera captured.");
                EditorApplication.playModeStateChanged += AfterPlay;
                EditorApplication.ExitPlaymode();
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(2); }
        }

        private static void AfterPlay(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= AfterPlay;
            try
            {
                foreach (string path in new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/Lobby.unity" })
                {
                    EditorSceneManager.OpenScene(path);
                    if (Metagame.Gameplay.Stage1FogController.Active != null)
                        throw new InvalidOperationException("Fog leaked into another scene.");
                    Debug.Log("[Fog411] Scene settings restored: " + path + " mode=" + RenderSettings.fogMode
                        + " enabled=" + RenderSettings.fog + " start=" + RenderSettings.fogStartDistance
                        + " end=" + RenderSettings.fogEndDistance);
                }
                EditorSceneManager.OpenScene(Stage1AstraIntegration.ScenePath);
                AssertFog();
                Debug.Log("[Fog411] VALIDATION COMPLETE");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(2); }
        }

        private static void CapturePair(Camera camera, string label)
        {
            Color color = RenderSettings.fogColor;
            FogMode mode = RenderSettings.fogMode;
            try
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = new Color(.085f, .078f, .17f);
                Capture(camera, label + "-before");
                RenderSettings.fogMode = mode;
                RenderSettings.fogColor = color;
                Capture(camera, label + "-after");
            }
            finally { RenderSettings.fogMode = mode; RenderSettings.fogColor = color; }
        }

        private static void Capture(Camera camera, string label)
        {
            var target = new RenderTexture(1280, 720, 24);
            var previous = RenderTexture.active;
            bool async = ShaderUtil.allowAsyncCompilation;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                target.Create();
                ShaderUtil.allowAsyncCompilation = false;
                var request = new RenderPipeline.StandardRequest { destination = target };
                for (int i = 0; i < 4; i++) RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                string directory = Path.GetFullPath("../fog-validation");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, label + ".png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                ShaderUtil.allowAsyncCompilation = async;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
        }
    }
}
