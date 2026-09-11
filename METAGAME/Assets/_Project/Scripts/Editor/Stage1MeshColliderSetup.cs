using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Metagame.Gameplay;

namespace Metagame.EditorTools
{
    /// <summary>
    /// Stage1 정적 환경 메시의 물리 표면을 프리팹과 Main 씬 인스턴스에 함께 연결한다.
    ///
    /// 큰 절벽·바위·버섯나무는 실제 메시 표면을 따라가고, 꽃·나선 식물·원경 행성은
    /// 충돌을 만들지 않는다. 파츠·운반체·고정못·운석은 이동하는 물리 오브젝트이므로
    /// 기존의 단순/복합 콜라이더를 유지한다.
    /// </summary>
    public static class Stage1MeshColliderSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string EnvironmentPath = "Stage1/Environment";

        static readonly string[] StaticPrefabNames =
        {
            "CliffWall",
            "RockPillars",
            "MushroomTree",
            "BoulderCluster",
            "FlatRock",
            "FlatRockCracked",
            "RockClusterA",
            "RockClusterB",
            "RockOval",
            "RockRound",
            "RockRoundCracked",
        };

        static readonly HashSet<string> StaticGroupNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Cliffs",
            "MushroomTrees",
            "RockPillars",
            "Boulders",
            "RockClusters",
        };

        [MenuItem("Tools/Not This Planet/Stage1 정적 메시 콜라이더 연결", false, 10)]
        public static void Apply()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var prefabCount = ApplyKnownPrefabColliders();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var sceneCount = ApplySceneColliders(scene);

            if (sceneCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"STAGE1_MESH_COLLIDERS_OK|prefabAdded={prefabCount}|sceneAdded={sceneCount}|scene={ScenePath}");
        }

        /// <summary>
        /// Environment asset generation calls this after creating wrappers, so newly created
        /// static prefabs are ready before a scene instance is placed.
        /// </summary>
        public static int ApplyKnownPrefabColliders()
        {
            var added = 0;
            foreach (var name in StaticPrefabNames)
            {
                var path = Stage1EnvironmentAssets.PrefabPath(name);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;

                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var changed = MeshColliderUtility.EnsureMeshColliders(contents, false);
                    if (changed > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        added += changed;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            return added;
        }

        static int ApplySceneColliders(Scene scene)
        {
            var environment = FindByPath(scene, EnvironmentPath);
            if (environment == null)
            {
                Debug.LogWarning("[Stage1MeshCollider] Stage1/Environment를 찾지 못했다: " + ScenePath);
                return 0;
            }

            var added = 0;
            var transforms = environment.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var group = transforms[i];
                if (!StaticGroupNames.Contains(group.name)) continue;

                for (var childIndex = 0; childIndex < group.childCount; childIndex++)
                    added += MeshColliderUtility.EnsureMeshColliders(
                        group.GetChild(childIndex).gameObject, false, true);
            }

            return added;
        }

        static Transform FindByPath(Scene scene, string path)
        {
            var parts = path.Split('/');
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;

                var current = root.transform;
                for (var i = 1; i < parts.Length && current != null; i++)
                    current = current.Find(parts[i]);
                return current;
            }

            return null;
        }
    }
}
