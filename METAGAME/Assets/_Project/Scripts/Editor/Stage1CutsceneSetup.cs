using System;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    public static class Stage1CutsceneSetup
    {
        [MenuItem("Not This Planet/Scenes/Create Stage1 Entry and Assault Cutscenes")]
        public static void Create()
        {
            if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity");
            else if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path)
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!AssetDatabase.IsValidFolder("Assets/Scene")) AssetDatabase.CreateFolder("Assets", "Scene");
            var previous = SceneManager.GetActiveScene();
            var visual = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Environment/Stage1/Meteors/PF_Stage1_MeteorMain.prefab");
            if (visual == null) throw new InvalidOperationException("Stage1 meteor visual missing");
            CreateScene(Stage1CutsceneFlow.EntryPath, false, visual, new[]
            {
                Shot("01 탐사대와 현재 커마", 0, 4, Stage1CutsceneScene.Anchor.Crew,
                    new(5, 2.5f, -6), new(3.5f, 1.8f, -4), new(0, 0.5f, 0), 52),
                Shot("02 올려다본 유성우", 4, 2.8f, Stage1CutsceneScene.Anchor.Crew,
                    new(2, 1.6f, -3), new(2, 2.2f, -3), new(-12, 30, 12), 66),
                Shot("03 기지 옆 작은 운석 충돌", 6.8f, 3.2f, Stage1CutsceneScene.Anchor.Impact,
                    new(7, 4, -9), new(6, 3, -7), new(0, 2, 0), 58),
                Shot("04 소등과 엔진 고정못", 10, 2.5f, Stage1CutsceneScene.Anchor.Engine,
                    new(4, 2.4f, -5), new(3, 1.5f, -4), new(0, 0.5f, 0), 52),
                Shot("05 중앙 조립대 점등", 12.5f, 2.5f, Stage1CutsceneScene.Anchor.Assembly,
                    new(9, 7, -11), new(7, 5, -9), new(0, 1, 0), 62),
                Shot("06 실제 1인칭 태블릿", 15, 3, Stage1CutsceneScene.Anchor.Crew,
                    new(0, 1, 0), new(0, 1, 0), new(1, 1, 0), 64),
            });
            CreateScene(Stage1CutsceneFlow.AssaultPath, true, visual, new[]
            {
                Shot("01 경고와 탐사대", 0, 2, Stage1CutsceneScene.Anchor.Crew,
                    new(4, 2, -5), new(3, 2.5f, -4), new(0, 0.5f, 0), 55),
                Shot("02 하늘을 덮는 유성우", 2, 3, Stage1CutsceneScene.Anchor.Assembly,
                    new(7, 4, -9), new(9, 6, -10), new(-8, 25, 10), 72),
                Shot("03 실제 운석 공세와 기지", 5, 3, Stage1CutsceneScene.Anchor.Assembly,
                    new(16, 13, -18), new(12, 9, -14), new(0, 3, 0), 76),
            });
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            var stage = SceneManager.GetSceneByPath(Stage1CutsceneFlow.StagePath);
            bool opened = !stage.isLoaded;
            if (opened) stage = EditorSceneManager.OpenScene(Stage1CutsceneFlow.StagePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(stage);
            var bootstrap = stage.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SpaceBootstrap>(true)).Single();
            var dev = bootstrap.GetComponent<DevManager>();
            if (dev == null) dev = bootstrap.gameObject.AddComponent<DevManager>();
            var settings = new SerializedObject(dev);
            settings.FindProperty("enableF10Entry").boolValue = true;
            settings.FindProperty("enableF9Assault").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var trigger = stage.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Stage1AssaultTrigger>(true)).FirstOrDefault();
            if (trigger == null)
            {
                var dock = stage.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(t => t.name == "Tractor dock · center");
                if (dock == null) throw new InvalidOperationException("Tractor dock center not found");
                var go = new GameObject("Stage1 Assault Trigger - Tractor Dock");
                go.transform.position = dock.position + Vector3.up * 1.5f;
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true; box.size = new Vector3(6f, 4f, 6f);
                go.AddComponent<Stage1AssaultTrigger>();
            }
            EditorSceneManager.SaveScene(stage);
            if (opened) EditorSceneManager.CloseScene(stage, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (var path in new[] { Stage1CutsceneFlow.EntryPath, Stage1CutsceneFlow.AssaultPath })
            {
                var existing = scenes.FirstOrDefault(s => s.path == path);
                if (existing != null) existing.enabled = true;
                else scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
        }

        static Stage1CutsceneScene.Shot Shot(string label, float start, float duration,
            Stage1CutsceneScene.Anchor anchor, Vector3 from, Vector3 to, Vector3 look, float fov) => new()
        { label = label, start = start, duration = duration, anchor = anchor, from = from, to = to, look = look, fieldOfView = fov };

        static void CreateScene(string path, bool assault, GameObject visual, Stage1CutsceneScene.Shot[] shots)
        {
            if (File.Exists(path)) return; // 아티스트가 수정한 샷을 재실행으로 덮어쓰지 않는다.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject(assault ? "Stage1 Assault Cutscene" : "Stage1 Entry Cutscene");
            root.AddComponent<Stage1CutsceneScene>().Author(assault, visual, shots);
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException(path);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
