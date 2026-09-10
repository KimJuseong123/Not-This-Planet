using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Metagame.Gameplay;

namespace Metagame.EditorTools
{
    /// <summary>Opt-in batch smoke check; inert during normal editing.</summary>
    [InitializeOnLoad]
    public static class Stage1ConceptSmokeCheck
    {
        private const string ActiveKey = "Stage1Concept.Smoke.Active";
        private const string ErrorsKey = "Stage1Concept.Smoke.Errors";
        private static int _frames;
        private static double _started;

        static Stage1ConceptSmokeCheck()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            _started = EditorApplication.timeSinceStartup;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnStateChanged;
        }

        // Invoke without -quit: exits itself after entering and leaving Play Mode.
        public static void Run()
        {
            // The existing URP auto-migrator is a project-authoring operation, not scene runtime.
            // -executeMethod runs before its delayed callback; don't let that callback save production
            // scenes inside this isolated Play Mode check. Normal interactive startup is unchanged.
            SessionState.SetBool("Stage1Concept.Smoke.PreviousUrpSetup", SessionState.GetBool("Metagame.URPSetupApplied", false));
            SessionState.SetBool("Metagame.URPSetupApplied", true);
            EditorSceneManager.OpenScene(Stage1ConceptMapBuilder.ScenePath);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(ErrorsKey, 0);
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.EnterPlaymode();
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                SessionState.SetInt(ErrorsKey, SessionState.GetInt(ErrorsKey, 0) + 1);
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup - _started > 90)
            {
                Debug.LogError("[ConceptSmoke] Timed out.");
                Finish(1);
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            _frames++;
            if (_frames != 120) return;
            Stage1ConceptMapBuilder.Validate();
            var viewer = Object.FindAnyObjectByType<Stage1ConceptExplorer>();
            if (viewer == null || Camera.main == null)
            {
                Debug.LogError("[ConceptSmoke] Missing viewer/camera.");
            }
            else
            {
                var pipeline = QualitySettings.renderPipeline;
                viewer.enabled = false;
                if (QualitySettings.renderPipeline == pipeline)
                    Debug.LogError("[ConceptSmoke] Pipeline override was not restored on disable.");
                viewer.enabled = true;
                if (QualitySettings.renderPipeline != pipeline)
                    Debug.LogError("[ConceptSmoke] Pipeline override was not reapplied on enable.");
                Debug.Log("[ConceptSmoke] 120 Play Mode frames; viewer disable/re-enable and pipeline restoration exercised.");
            }
            EditorApplication.ExitPlaymode();
        }

        private static void OnStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            int errors = SessionState.GetInt(ErrorsKey, 0);
            Debug.Log("[ConceptSmoke] " + (errors == 0 ? "PASS" : "FAIL") + " — runtime errors: " + errors);
            Finish(errors == 0 ? 0 : 1);
        }

        private static void Finish(int exitCode)
        {
            SessionState.SetBool("Metagame.URPSetupApplied", SessionState.GetBool("Stage1Concept.Smoke.PreviousUrpSetup", false));
            SessionState.EraseBool("Stage1Concept.Smoke.PreviousUrpSetup");
            SessionState.EraseBool(ActiveKey);
            SessionState.EraseInt(ErrorsKey);
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnStateChanged;
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }
    }
}
