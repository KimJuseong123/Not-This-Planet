using System;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Gameplay.Parts;
using Mirror;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.AIExperiment.Editor
{
    public static class AIExperimentBuilder
    {
        const string PrefabPath = "Assets/_Project/AICompanionExperiment/AICompanion.prefab";

        /// <summary>이미 있는 실험 씬을 이 PC 의 빌드 목록에만 등록한다.
        ///
        /// 씬 파일은 저장소에 있지만 EditorBuildSettings 는 공유하지 않는다. 공유하면
        /// 출시 빌드에도 실험 씬이 따라 들어가기 때문이다. 시험해 볼 사람이 한 번 누른다.
        /// 여러 번 눌러도 중복으로 쌓이지 않는다.</summary>
        [MenuItem("Tools/Not This Planet/AI 실험/실험 씬을 이 PC 빌드 목록에 등록")]
        public static void RegisterScenes()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            int added = 0;
            foreach (var path in new[] { AIExperimentManager.EntryScene, AIExperimentManager.RoomScene })
            {
                if (!File.Exists(path))
                    throw new InvalidOperationException("실험 씬이 없습니다: " + path);
                if (scenes.Any(s => s.path == path)) continue;
                scenes.Add(new EditorBuildSettingsScene(path, true));
                ++added;
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log(added == 0
                ? "[AIExperiment] 실험 씬이 이미 빌드 목록에 있습니다."
                : "[AIExperiment] 실험 씬 " + added + "개를 이 PC 빌드 목록에 등록했습니다."
                  + " 공유 설정은 바뀌지 않습니다.");
        }

        [MenuItem("Tools/Not This Planet/AI 실험/실험 씬 만들기")]
        public static void BuildScenes()
        {
            if (File.Exists(AIExperimentManager.EntryScene) || File.Exists(AIExperimentManager.RoomScene))
                throw new InvalidOperationException("Experimental scenes already exist; refusing to overwrite.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var bot = new GameObject("AI Companion");
            bot.SetActive(false);
            bot.AddComponent<NetworkIdentity>();
            bot.AddComponent<NetworkCompanion>();
            var sync = bot.AddComponent<CompanionTransform>();
            sync.target = bot.transform;
            sync.syncDirection = SyncDirection.ServerToClient;
            bot.SetActive(true);
            var botPrefab = PrefabUtility.SaveAsPrefabAsset(bot, PrefabPath);
            UnityEngine.Object.DestroyImmediate(bot);

            // Save-as through Unity assigns valid scene/network identity metadata.
            var room = EditorSceneManager.OpenScene("Assets/Scenes/PracticeLobby.unity");
            foreach (var practice in UnityEngine.Object.FindObjectsByType<PracticeLobbyBootstrap>(FindObjectsSortMode.None))
                practice.enabled = false; // Test room cannot accidentally depart to Stage1/reload via practice console.
            var trial = new GameObject("AI Companion Trial").AddComponent<AICompanionTrial>();
            trial.Configure(botPrefab.GetComponent<NetworkCompanion>());
            trial.gameObject.AddComponent<AITrialSmoke>();
            EditorSceneManager.SaveScene(room, AIExperimentManager.RoomScene, true);

            var entry = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Local AI Test Network");
            var transport = go.AddComponent<kcp2k.KcpTransport>();
            transport.Port = 7789;
            var manager = go.AddComponent<AIExperimentManager>();
            manager.transport = transport;
            manager.networkAddress = "127.0.0.1";
            manager.onlineScene = AIExperimentManager.RoomScene;
            manager.offlineScene = AIExperimentManager.EntryScene;
            manager.maxConnections = 3;
            manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Network/PF_NetPlayer.prefab");
            if (manager.playerPrefab == null) throw new InvalidOperationException("Production network player prefab missing.");
            manager.spawnPrefabs.Add(botPrefab);
            new GameObject("AI Test Menu").AddComponent<AIExperimentEntry>().Configure(manager);
            var cam = new GameObject("Menu camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.025f, 0.04f, 0.075f);
            EditorSceneManager.SaveScene(entry, AIExperimentManager.EntryScene);
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.Add(new EditorBuildSettingsScene(AIExperimentManager.EntryScene, true));
            scenes.Add(new EditorBuildSettingsScene(AIExperimentManager.RoomScene, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[AIExperiment] Scenes authored. Original PracticeLobby/Stage1 are untouched.");
        }

        [MenuItem("Tools/Not This Planet/AI 실험/Windows 테스트 빌드")]
        public static void BuildPlayer()
            => BuildAt("../AITrialBuild/AICompanion.exe");

        [MenuItem("Tools/Not This Planet/AI 실험/v2 고정못 테스트 빌드")]
        public static void BuildPlayerV2()
            => BuildAt("../AITrialBuild-v2/AICompanion.exe");

        [MenuItem("Tools/Not This Planet/AI 실험/상세 로그 테스트 빌드")]
        public static void BuildPlayerDiagnostics()
            => BuildAt("../AITrialBuild-diagnostics/AICompanion.exe");

        [MenuItem("Tools/Not This Planet/AI 실험/고정못 4개 테스트 빌드")]
        public static void BuildPlayerFourPins()
            => BuildAt("../AITrialBuild-four-pins/AICompanion.exe");

        [MenuItem("Tools/Not This Planet/AI 실험/이모티콘 테스트 빌드")]
        public static void BuildPlayerEmotes()
            => BuildAt("../AITrialBuild-emotes-v2/AICompanion.exe");

        [MenuItem("Tools/Not This Planet/AI 실험/v3 가까운 못과 연속 작업 빌드")]
        public static void BuildPlayerSkillsV3()
            => BuildAt("../AITrialBuild-skills-v3/AICompanion.exe");

        [MenuItem("Tools/Not This Planet/AI 실험/v3 연결 검증 개선 빌드")]
        public static void BuildPlayerSkillsV3Checked()
            => BuildAt("../AITrialBuild-skills-v3-checked-v2/AICompanion.exe");

        [MenuItem("Tools/Not This Planet/AI 실험/v4 도구와 방어 빌드")]
        public static void BuildPlayerToolsV4()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(AIExperimentManager.RoomScene);
            var trial = UnityEngine.Object.FindAnyObjectByType<AICompanionTrial>();
            var serialized = new SerializedObject(trial);
            var meteorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Hazards/Stage1/PF_Stage1_Meteor.prefab")?.GetComponent<Metagame.Gameplay.Hazards.Meteor>();
            if (meteorPrefab == null) throw new InvalidOperationException("Practice meteor prefab missing. Scene not saved.");
            serialized.FindProperty("_practiceMeteorPrefab").objectReferenceValue = meteorPrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
            BuildAt("../AITrialBuild-tools-v4/AICompanion.exe");
        }

        // Batch fallback: build the already-authored scene in a fresh Editor process.
        // Does not save scenes or change the user's Build Settings.
        public static void BuildPreparedToolsV4()
        {
            Debug.Log("[AIExperiment] Building already-prepared v4 scene; target=" + EditorUserBuildSettings.activeBuildTarget);
            BuildAt("../AITrialBuild-tools-v4/AICompanion.exe");
        }

        // Explicit authoring operation, never runs automatically on import or in a player build.
        public static void CreateFourPinLayout()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(AIExperimentManager.RoomScene);
            var pins = UnityEngine.Object.FindObjectsByType<PartClamp>();
            if (pins.Length == 4 && pins.All(p => p.name.StartsWith("PIN ")))
            { Debug.Log("[AIExperiment] Four-pin layout already exists; no changes made."); return; }
            if (pins.Length != 1) throw new InvalidOperationException("Expected the original single practice pin. Scene not saved.");
            var original = pins[0].GetComponentInParent<SpaceshipPart>();
            if (original == null) throw new InvalidOperationException("Practice cargo missing.");
            var labelTemplate = UnityEngine.Object.FindObjectsByType<TextMesh>().First(t => t.text.Contains("PULL & PLAY"));
            var parent = original.transform.parent;
            var originalPosition = original.transform.position;
            for (int i = 0; i < 4; ++i)
            {
                var cargo = i == 0 ? original.gameObject : UnityEngine.Object.Instantiate(original.gameObject, parent);
                cargo.name = $"AI pin cargo {i + 1:00}";
                cargo.transform.position = new Vector3(-8 + i * 2, originalPosition.y, originalPosition.z);
                var pin = cargo.GetComponentInChildren<PartClamp>();
                pin.name = $"PIN {i + 1:00}";
                var so = new SerializedObject(pin);
                so.FindProperty("_part").objectReferenceValue = cargo.GetComponent<SpaceshipPart>();
                so.ApplyModifiedPropertiesWithoutUndo();
                // A non-colliding sign stays at the station after its pin is removed.
                var label = UnityEngine.Object.Instantiate(labelTemplate, parent);
                label.name = $"AI target sign {i + 1:00}";
                label.text = $"PIN {i + 1:00}";
                label.characterSize = 0.075f;
                label.transform.SetPositionAndRotation(new Vector3(cargo.transform.position.x, 1.7f, 6.1f), Quaternion.identity);
                Debug.Log($"[AIExperiment] {pin.name}: position={pin.transform.position:F2}, cargo={cargo.name}");
            }
            var mat = parent.Find("Butter practice mat");
            if (mat != null)
            {
                mat.position = new Vector3(-5, mat.position.y, mat.position.z);
                mat.localScale = new Vector3(8.5f, mat.localScale.y, mat.localScale.z);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[AIExperiment] Saved four independent practice pins. Production lobby and Stage1 untouched.");
        }

        static void BuildAt(string output)
        {
            string path = Path.GetFullPath(output);
            Debug.Log("[AIExperiment] Build output: " + path);
            if (File.Exists(path)) throw new InvalidOperationException("Build already exists; choose a fresh output folder.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { AIExperimentManager.EntryScene, AIExperimentManager.RoomScene },
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("AI test build failed: " + report.summary.result);
            Debug.Log("[AIExperiment] Build: " + path);
        }

        public static void FinalizeTrialAssets()
        {
            var bot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var old = bot.GetComponent<NetworkTransformReliable>();
                if (!(old is CompanionTransform))
                {
                    UnityEngine.Object.DestroyImmediate(old);
                    bot.AddComponent<CompanionTransform>();
                }
                PrefabUtility.SaveAsPrefabAsset(bot, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(bot); }
            var room = EditorSceneManager.OpenScene(AIExperimentManager.RoomScene);
            var trial = UnityEngine.Object.FindAnyObjectByType<AICompanionTrial>();
            var serialized = new SerializedObject(trial);
            serialized.FindProperty("_model").stringValue = "qwen3:1.7b";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (trial.GetComponent<AITrialSmoke>() == null) trial.gameObject.AddComponent<AITrialSmoke>();
            EditorSceneManager.SaveScene(room);
            AssetDatabase.SaveAssets();
            BuildPlayer();
        }
    }
}
