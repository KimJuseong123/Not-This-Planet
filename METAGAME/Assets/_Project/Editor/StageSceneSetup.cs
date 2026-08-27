using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    public static class StageSceneSetup
    {
        private const string Stage1ScenePath = "Assets/_Project/Scenes/Stage1.unity";

        [MenuItem("Tools/Metagame/Stage1 기본 씬 구성 또는 열기", false, 1)]
        public static void CreateOrOpenStage1()
        {
            EnsureStage1Base();
            PutStage1FirstInBuildSettings();
            EditorSceneManager.OpenScene(Stage1ScenePath, OpenSceneMode.Single);
        }

        private static void EnsureStage1Base()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EnsureStage1Exists();

            var scene = SceneManager.GetSceneByPath(Stage1ScenePath);
            bool closeWhenDone = !scene.isLoaded;
            if (closeWhenDone)
                scene = EditorSceneManager.OpenScene(Stage1ScenePath, OpenSceneMode.Additive);

            bool changed = false;
            var stageRoot = FindRoot(scene, "Stage1") ?? CreateRoot(scene, "Stage1", ref changed);
            var environment = FindOrCreateChild(stageRoot.transform, "Environment", ref changed);
            var gameplay = FindOrCreateChild(stageRoot.transform, "Gameplay", ref changed);
            var lighting = FindOrCreateChild(stageRoot.transform, "Lighting", ref changed);
            var cameras = FindOrCreateChild(stageRoot.transform, "Cameras", ref changed);

            var groundGroup = FindOrCreateChild(environment.transform, "Ground", ref changed);
            FindOrCreateChild(environment.transform, "Boundaries", ref changed);
            FindOrCreateChild(environment.transform, "Nature", ref changed);
            FindOrCreateChild(environment.transform, "DistantBackground", ref changed);

            var spawnPoints = FindOrCreateChild(gameplay.transform, "SpawnPoints", ref changed);
            FindOrCreateChild(gameplay.transform, "Checkpoints", ref changed);

            if (!FindDirectChild(groundGroup.transform, "ENV_Ground_Arena_50m"))
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "ENV_Ground_Arena_50m";
                ground.transform.SetParent(groundGroup.transform, false);
                ground.transform.localPosition = Vector3.zero;
                ground.transform.localScale = new Vector3(5f, 1f, 5f);
                changed = true;
            }

            if (!FindDirectChild(spawnPoints.transform, "PlayerSpawn"))
            {
                var playerSpawn = new GameObject("PlayerSpawn");
                playerSpawn.transform.SetParent(spawnPoints.transform, false);
                playerSpawn.transform.localPosition = new Vector3(0f, 1f, 0f);
                changed = true;
            }

            changed |= ParentRootIfPresent(scene, "Main Camera", cameras.transform);
            changed |= ParentRootIfPresent(scene, "Directional Light", lighting.transform);
            changed |= ParentRootIfPresent(scene, "Global Volume", lighting.transform);

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Metagame] Stage1 기본 플레이 영역 구성 완료");
            }

            PutStage1FirstInBuildSettings();
            if (closeWhenDone)
                EditorSceneManager.CloseScene(scene, true);
        }

        private static void EnsureStage1Exists()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Stage1ScenePath))
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, Stage1ScenePath))
                throw new System.InvalidOperationException("Stage1 씬을 생성하지 못했습니다.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == name)
                    return root;
            return null;
        }

        private static GameObject CreateRoot(Scene scene, string name, ref bool changed)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            changed = true;
            return go;
        }

        private static GameObject FindOrCreateChild(Transform parent, string name, ref bool changed)
        {
            var child = FindDirectChild(parent, name);
            if (child)
                return child.gameObject;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            changed = true;
            return go;
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name)
                    return parent.GetChild(i);
            return null;
        }

        private static bool ParentRootIfPresent(Scene scene, string name, Transform parent)
        {
            var go = FindRoot(scene, name);
            if (!go)
                return false;

            go.transform.SetParent(parent, true);
            return true;
        }

        private static void PutStage1FirstInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(Stage1ScenePath, true)
            };

            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.path != Stage1ScenePath)
                    scenes.Add(scene);

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
