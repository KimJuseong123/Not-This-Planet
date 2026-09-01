using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    /// <summary>
    /// Stage1의 기존 50m Plane을 Unity Terrain으로 교체한다.
    ///
    /// 자연물과 주요 오브젝트 배치가 끝나기 전까지 전체 지형을 평평하게 유지한다.
    /// 최종 높낮이는 배치 결과를 기준으로 후속 작업에서 조정한다.
    ///
    /// 기존 Plane은 삭제하지 않고 비활성화한다. 되돌리거나 위치를 비교할 수 있는 안전장치다.
    /// 이 도구는 같은 이름의 Terrain만 다시 만들기 때문에 반복 실행해도 중복되지 않는다.
    ///
    /// CI: Unity.exe -batchmode -quit -projectPath .
    ///     -executeMethod Metagame.EditorTools.Stage1TerrainBuilder.Build
    /// </summary>
    public static class Stage1TerrainBuilder
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string TerrainRoot = "Assets/_Project/Terrain/Stage1";
        const string TerrainDataPath = TerrainRoot + "/TerrainData/TD_Stage1_Arena.asset";
        const string TerrainPrefabPath =
            "Assets/_Project/Prefabs/Environment/Stage1/PF_Stage1_Terrain.prefab";

        const string TerrainName = "ENV_Terrain_Arena_50m";
        const string LegacyGroundName = "ENV_Ground_Arena_50m";

        const int HeightmapResolution = 129;
        const int AlphamapResolution = 256;
        const float TerrainWidth = 50f;
        const float TerrainHeight = 6f;
        const float GroundY = 0.06f;

        public static void Apply()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Build();
        }

        /// <summary>배치 모드와 메뉴에서 함께 사용하는 진입점.</summary>
        public static void Build()
        {
            EnsureFolders();

            var data = EnsureTerrainData();
            EnsureTerrainPrefab(data);
            ApplyToMainScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Stage1Terrain] 평평한 기본 Terrain 생성 완료 — 크기 "
                      + TerrainWidth + "m → " + ScenePath);
        }

        static void EnsureFolders()
        {
            Directory.CreateDirectory(TerrainRoot + "/TerrainData");
            Directory.CreateDirectory(Path.GetDirectoryName(TerrainPrefabPath));
            AssetDatabase.Refresh();
        }

        static TerrainData EnsureTerrainData()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null)
            {
                data = new TerrainData { name = "TD_Stage1_Arena" };
                AssetDatabase.CreateAsset(data, TerrainDataPath);
            }

            data.heightmapResolution = HeightmapResolution;
            data.alphamapResolution = AlphamapResolution;
            data.baseMapResolution = 512;
            data.size = new Vector3(TerrainWidth, TerrainHeight, TerrainWidth);
            // S15P21B202-74에서 연결한 Terrain Layer가 있다면 Terrain 재생성 시에도 유지한다.
            data.SetHeights(0, 0, GenerateHeights());

            EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>자연물 배치 전 기준면으로 사용할 높이 0의 평평한 지형을 만든다.</summary>
        static float[,] GenerateHeights()
        {
            return new float[HeightmapResolution, HeightmapResolution];
        }

        static void EnsureTerrainPrefab(TerrainData data)
        {
            var temporary = Terrain.CreateTerrainGameObject(data);
            temporary.name = TerrainName;
            ConfigureTerrain(temporary);
            PrefabUtility.SaveAsPrefabAsset(temporary, TerrainPrefabPath);
            Object.DestroyImmediate(temporary);
        }

        static void ApplyToMainScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var ground = FindByPath(scene, "Stage1/Environment/Ground");
            if (ground == null)
            {
                Debug.LogError("[Stage1Terrain] Stage1/Environment/Ground를 찾지 못했다.");
                return;
            }

            // 프리팹을 저장하면 루트 이름이 파일명(PF_Stage1_Terrain)으로 정규화된다.
            // 이름만 보고 지우면 이전 실행의 인스턴스를 놓쳐 Terrain이 겹치므로,
            // 이 Ground 그룹 아래의 Terrain 컴포넌트를 기준으로 전부 정리한다.
            for (var i = ground.childCount - 1; i >= 0; i--)
            {
                var child = ground.GetChild(i);
                if (child.GetComponent<Terrain>() != null)
                    Object.DestroyImmediate(child.gameObject);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainPrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = TerrainName;
            instance.transform.SetParent(ground, false);
            instance.transform.localPosition = new Vector3(-TerrainWidth * 0.5f, GroundY, -TerrainWidth * 0.5f);
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            ConfigureTerrain(instance);

            var legacy = ground.Find(LegacyGroundName);
            if (legacy != null) legacy.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void ConfigureTerrain(GameObject gameObject)
        {
            var groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0) gameObject.layer = groundLayer;

            var terrain = gameObject.GetComponent<Terrain>();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 5f;
            terrain.basemapDistance = 1000f;
            terrain.allowAutoConnect = false;

            var collider = gameObject.GetComponent<TerrainCollider>();
            if (collider != null) collider.enabled = true;
        }

        static Transform FindByPath(Scene scene, string path)
        {
            var pieces = path.Split('/');
            Transform current = null;

            foreach (var root in scene.GetRootGameObjects())
                if (root.name == pieces[0]) { current = root.transform; break; }

            for (var i = 1; current != null && i < pieces.Length; i++)
                current = current.Find(pieces[i]);

            return current;
        }
    }
}
