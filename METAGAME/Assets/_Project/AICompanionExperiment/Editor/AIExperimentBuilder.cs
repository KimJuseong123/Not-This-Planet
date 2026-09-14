using System;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
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

        static void BuildAt(string output)
        {
            string path = Path.GetFullPath(output);
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
