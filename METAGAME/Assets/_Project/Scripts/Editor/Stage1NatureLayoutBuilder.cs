using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Metagame.Gameplay;

namespace Metagame.EditorTools
{
    /// <summary>
    /// S15P21B202-236: 백업한 Terrain 프리팹으로 Stage1 맵을 두 타일로 확장하고,
    /// 중앙 동선을 비운 채 자연물 군집과 디딤돌을 재현 가능하게 배치한다.
    /// </summary>
    public static class Stage1NatureLayoutBuilder
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string EnvironmentPath = "Stage1/Environment";
        private const string NaturePath = "Stage1/Environment/Nature";
        private const string LayoutName = "NatureLayout_S15P21B202_236";
        private const string ImportedTerrainPrefabPath =
            "Assets/__CodexTerrainBackup/PF_Stage1_Terrain_25_0_06_-25.prefab";
        private const string FlatTerrainPrefabPath =
            "Assets/_Project/Prefabs/Environment/Stage1/PF_Stage1_Terrain.prefab";
        private const string CliffTerrainPrefabPath =
            "Assets/_Project/Prefabs/Environment/Stage1/PF_Stage1_Terrain_Expanded.prefab";
        private const string FlatRockModelPath =
            "Assets/_Project/Models/Environment/Stage1/alien_rock_flat_cracked/alien_rock_flat_cracked.fbx";
        private const string FlatRockPrefabPath =
            "Assets/_Project/Prefabs/Environment/Stage1/PF_Stage1_FlatRockCracked.prefab";
        private const string FlatRockMaterialPath =
            "Assets/_Project/Materials/Environment/Stage1/M_Stage1_FlatRockCracked.mat";
        private const string FlatRockBaseColorPath =
            "Assets/_Project/Models/Environment/Stage1/alien_rock_flat_cracked/" +
            "tripo_convert_a6904291-be99-4e71-a4fd-69566933138e.fbm/Color.jpg";

        private enum ColliderKind
        {
            None,
            Box,
            Tree,
            PlantTrigger
        }

        private readonly struct Placement
        {
            public readonly string PrefabName;
            public readonly string GroupName;
            public readonly Vector3 Position;
            public readonly float RotationY;
            public readonly Vector3 Scale;
            public readonly ColliderKind Collider;

            public Placement(string prefabName, string groupName, Vector3 position,
                float rotationY, Vector3 scale, ColliderKind collider)
            {
                PrefabName = prefabName;
                GroupName = groupName;
                Position = position;
                RotationY = rotationY;
                Scale = scale;
                Collider = collider;
            }
        }

        [MenuItem("Tools/둥실둥실 우주 대탈출/Stage1 지형 확장 및 자연물 배치", false, 9)]
        public static void Apply()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Build();
        }

        public static void Build()
        {
            PrepareTerrainPrefab();
            Stage1TerrainBuilder.EnsureTerrainAssets();
            EnsureFlatRockPrefab();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var environment = FindByPath(scene, EnvironmentPath);
            var nature = FindByPath(scene, NaturePath);
            if (environment == null || nature == null)
                throw new InvalidOperationException("Stage1/Environment 또는 Nature를 찾지 못했다.");

            var ground = environment.Find("Ground");
            if (ground == null)
                throw new InvalidOperationException("Stage1/Environment/Ground를 찾지 못했다.");

            BuildExpandedTerrain(scene, environment);
            RemoveExistingBoundaries(environment);
            ClearChildren(nature);

            var layout = new GameObject(LayoutName).transform;
            layout.SetParent(nature, false);

            var groups = new Dictionary<string, Transform>();
            var cliffs = new List<GameObject>();
            var made = 0;

            foreach (var placement in CreatePlacements())
            {
                var prefabPath = placement.PrefabName == "FlatRockCracked"
                    ? FlatRockPrefabPath
                    : $"Assets/_Project/Prefabs/Environment/Stage1/PF_Stage1_{placement.PrefabName}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                    throw new InvalidOperationException("자연물 프리팹을 찾지 못했다: " + prefabPath);

                var group = EnsureGroup(layout, groups, placement.GroupName);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = placement.PrefabName + "_" + group.childCount.ToString("00");
                instance.transform.SetParent(group, false);
                instance.transform.position = placement.Position;
                instance.transform.rotation = Quaternion.Euler(0f, placement.RotationY, 0f);
                instance.transform.localScale = placement.Scale;

                SnapToTerrain(instance.transform);
                AddSimpleCollider(instance, placement.Collider);
                if (placement.PrefabName == "CliffWall") cliffs.Add(instance);
                made++;
            }

            made += AddCliffTopFlowers(scene, layout, groups, cliffs);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            // Main.unity also contains terrain objects from other stage/layout data.
            // Validate only the two tiles owned by this builder, not every Terrain in
            // the open scene.
            var terrains = ground.GetComponentsInChildren<Terrain>(true);
            ValidateResult(scene, layout, terrains);
            Debug.Log($"STAGE1_LAYOUT_OK|jira=S15P21B202-236|terrains={terrains.Length}|nature={made}|scene={ScenePath}");
        }

        private static void ValidateResult(Scene scene, Transform layout, IReadOnlyCollection<Terrain> terrains)
        {
            if (terrains.Count != 2)
                throw new InvalidOperationException($"Terrain 개수가 2가 아니다: {terrains.Count}");

            var ordered = terrains.OrderBy(terrain => terrain.transform.position.x).ToArray();
            if (!Mathf.Approximately(ordered[0].transform.position.x, -25f) ||
                !Mathf.Approximately(ordered[1].transform.position.x, 25f))
                throw new InvalidOperationException("Terrain 타일 위치가 -25m, 25m가 아니다.");
            if (ReferenceEquals(ordered[0].terrainData, ordered[1].terrainData))
                throw new InvalidOperationException("두 Terrain이 같은 TerrainData를 공유한다.");

            var leftSource = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ordered[0].gameObject);
            var rightSource = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ordered[1].gameObject);
            if (leftSource != FlatTerrainPrefabPath || rightSource != CliffTerrainPrefabPath)
                throw new InvalidOperationException(
                    $"Terrain 프리팹 순서가 잘못됐다: left={leftSource}, right={rightSource}");

            var floating = 0;
            var checkedObjects = 0;
            foreach (Transform group in layout)
            foreach (Transform item in group)
            {
                if (group.name == "CliffTopFlowers" || !TryRendererBounds(item.gameObject, out var bounds))
                    continue;
                if (Mathf.Abs(bounds.min.y - SampleGroundY(bounds.center, .06f)) > .08f) floating++;
                checkedObjects++;
            }

            var missingScripts = scene.GetRootGameObjects()
                .Sum(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount);
            if (floating > 0 || missingScripts > 0)
                throw new InvalidOperationException(
                    $"배치 검증 실패: floating={floating}, missingScripts={missingScripts}");

            Debug.Log("STAGE1_LAYOUT_VALIDATION_OK|" +
                      $"grounded={checkedObjects}|floating=0|missingScripts=0|" +
                      "left=flat|right=cliff|independentTerrainData=true|positions=-25.00,25.00");
        }

        private static void PrepareTerrainPrefab()
        {
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>(ImportedTerrainPrefabPath);
            var installed = AssetDatabase.LoadAssetAtPath<GameObject>(CliffTerrainPrefabPath);

            if (imported != null && installed == null)
            {
                var error = AssetDatabase.MoveAsset(ImportedTerrainPrefabPath, CliffTerrainPrefabPath);
                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException("Terrain 프리팹 이동 실패: " + error);
                installed = AssetDatabase.LoadAssetAtPath<GameObject>(CliffTerrainPrefabPath);
            }

            if (installed == null)
                throw new InvalidOperationException("백업 Terrain 프리팹을 찾지 못했다: " + CliffTerrainPrefabPath);

            var contents = PrefabUtility.LoadPrefabContents(CliffTerrainPrefabPath);
            contents.name = "PF_Stage1_Terrain_Expanded";
            PrefabUtility.SaveAsPrefabAsset(contents, CliffTerrainPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            if (AssetDatabase.IsValidFolder("Assets/__CodexTerrainBackup"))
                AssetDatabase.DeleteAsset("Assets/__CodexTerrainBackup");

            AssetDatabase.SaveAssets();
        }

        private static void BuildExpandedTerrain(Scene scene, Transform environment)
        {
            var ground = environment.Find("Ground");
            if (ground == null)
            {
                ground = new GameObject("Ground").transform;
                ground.SetParent(environment, false);
            }
            ClearChildren(ground);

            var flatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlatTerrainPrefabPath);
            var cliffPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CliffTerrainPrefabPath);
            var flatTerrain = flatPrefab != null ? flatPrefab.GetComponent<Terrain>() : null;
            var cliffTerrain = cliffPrefab != null ? cliffPrefab.GetComponent<Terrain>() : null;
            if (flatTerrain == null || flatTerrain.terrainData == null)
                throw new InvalidOperationException(
                    $"기존 평지 Terrain 연결 오류: prefab={(flatPrefab == null ? "null" : flatPrefab.name)}, " +
                    $"terrain={(flatTerrain == null ? "null" : "ok")}, " +
                    $"data={(flatTerrain == null || flatTerrain.terrainData == null ? "null" : AssetDatabase.GetAssetPath(flatTerrain.terrainData))}, " +
                    $"mainType={AssetDatabase.GetMainAssetTypeAtPath(FlatTerrainPrefabPath)}");
            if (cliffTerrain == null || cliffTerrain.terrainData == null)
                throw new InvalidOperationException("설치한 절벽 Terrain 프리팹 또는 TerrainData가 없다.");

            var tileWidth = flatTerrain.terrainData.size.x;
            var baseX = -25f;

            var left = CreateTerrainTile(scene, ground, flatPrefab,
                "Terrain_Flat_(-25.00, 0.06, -25.00)", new Vector3(baseX, 0.06f, -25f));
            var right = CreateTerrainTile(scene, ground, cliffPrefab,
                "Terrain_Cliff_(25.00, 0.06, -25.00)", new Vector3(baseX + tileWidth, 0.06f, -25f));

            left.SetNeighbors(null, null, right, null);
            right.SetNeighbors(left, null, null, null);
        }

        private static Terrain CreateTerrainTile(Scene scene, Transform parent, GameObject prefab,
            string name, Vector3 position)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            return instance.GetComponent<Terrain>();
        }

        private static List<Placement> CreatePlacements()
        {
            var result = new List<Placement>(128);

            // 연속 벽 대신, 모서리와 외곽에 분리된 절벽 랜드마크를 둔다.
            Add(result, "CliffWall", "Cliffs", -18f, 18f, 8f, 2.55f, ColliderKind.Box);
            Add(result, "CliffWall", "Cliffs", -10f, 21f, 78f, 2.05f, ColliderKind.Box);
            Add(result, "CliffWall", "Cliffs", 30f, 21f, -4f, 2.20f, ColliderKind.Box);
            Add(result, "CliffWall", "Cliffs", 70f, 9f, 84f, 2.25f, ColliderKind.Box);
            Add(result, "CliffWall", "Cliffs", 69f, -6f, 92f, 1.90f, ColliderKind.Box);

            // 대표 실루엣: 큰 나무와 돌기둥.
            Add(result, "MushroomTree", "MushroomTrees", -20f, 4f, -22f, 2.15f, ColliderKind.Tree);
            Add(result, "MushroomTree", "MushroomTrees", 58f, 17f, 18f, 3.00f, ColliderKind.Tree);
            Add(result, "MushroomTree", "MushroomTrees", 69f, -18f, -31f, 2.35f, ColliderKind.Tree);
            Add(result, "RockPillars", "RockPillars", -20f, -8f, -8f, 1.80f, ColliderKind.Box);
            Add(result, "RockPillars", "RockPillars", 20f, -21f, 15f, 1.90f, ColliderKind.Box);
            Add(result, "RockPillars", "RockPillars", 43f, 20f, -13f, 1.45f, ColliderKind.Box);
            Add(result, "RockPillars", "RockPillars", 68f, 1f, 11f, 2.20f, ColliderKind.Box);

            // 큰 바위는 2~3개를 겹쳐 하나의 군집처럼 보이게 한다.
            AddBoulderGroup(result, -17f, -18f, 2.50f, 12f);
            AddBoulderGroup(result, 65f, -18f, 2.60f, -18f);
            AddBoulderGroup(result, 48f, 20f, 1.70f, 28f);
            AddBoulderGroup(result, 20f, 21f, 1.45f, -22f);

            var smallBoulders = new[]
            {
                new Vector4(-22f, 14f, 32f, .78f), new Vector4(-21f, -1f, -18f, .68f),
                new Vector4(-21f, -13f, 24f, .82f), new Vector4(-7f, 20f, -31f, .68f),
                new Vector4(5f, 22f, 17f, .72f), new Vector4(12f, -21f, -14f, .74f),
                new Vector4(29f, -21f, 37f, .65f), new Vector4(38f, 21f, -27f, .70f),
                new Vector4(52f, 21f, 41f, .72f), new Vector4(63f, 13f, -34f, .82f),
                new Vector4(71f, 16f, 16f, .76f), new Vector4(71f, -11f, 48f, .72f),
                new Vector4(54f, -21f, -42f, .70f), new Vector4(40f, -22f, 23f, .66f),
                new Vector4(4f, -21f, 52f, .70f), new Vector4(-8f, -20f, -38f, .68f)
            };
            foreach (var item in smallBoulders)
                Add(result, "BoulderCluster", "Boulders", item.x, item.y, item.z, item.w, ColliderKind.Box);

            // 발광 식물은 외곽의 포인트로 제한한다.
            Add(result, "SpiralPlant", "SpiralPlants", -21f, 10f, 18f, 7.6f, ColliderKind.PlantTrigger);
            Add(result, "SpiralPlant", "SpiralPlants", -10f, -20f, 56f, 7.2f, ColliderKind.PlantTrigger);
            Add(result, "SpiralPlant", "SpiralPlants", 8f, 21f, -13f, 7.4f, ColliderKind.PlantTrigger);
            Add(result, "SpiralPlant", "SpiralPlants", 29f, -21f, 34f, 7.6f, ColliderKind.PlantTrigger);
            Add(result, "SpiralPlant", "SpiralPlants", 45f, 21f, -26f, 7.2f, ColliderKind.PlantTrigger);
            Add(result, "SpiralPlant", "SpiralPlants", 61f, 15f, 51f, 7.5f, ColliderKind.PlantTrigger);
            Add(result, "SpiralPlant", "SpiralPlants", 70f, -10f, -14f, 7.4f, ColliderKind.PlantTrigger);
            Add(result, "SpiralPlant", "SpiralPlants", 52f, -21f, 22f, 7.1f, ColliderKind.PlantTrigger);

            // 꽃은 절벽·나무·바위 발치에 군집으로 둔다. 빈 공간에 균일하게 뿌리지 않는다.
            var flowers = new[]
            {
                new Vector4(-21f, 17f, 15f, 3.2f), new Vector4(-15f, 20f, 52f, 3.0f),
                new Vector4(-21f, 7f, -18f, 3.4f), new Vector4(-18f, 1f, 24f, 2.8f),
                new Vector4(-20f, -15f, 67f, 3.3f), new Vector4(-13f, -20f, -28f, 3.0f),
                new Vector4(17f, 20f, 22f, 2.8f), new Vector4(23f, 21f, -16f, 3.1f),
                new Vector4(28f, 19f, 43f, 3.0f), new Vector4(34f, 21f, -24f, 2.7f),
                new Vector4(19f, -20f, 31f, 3.2f), new Vector4(23f, -21f, -19f, 2.9f),
                new Vector4(45f, 19f, 16f, 3.1f), new Vector4(51f, 20f, -34f, 2.8f),
                new Vector4(56f, 16f, 27f, 3.4f), new Vector4(61f, 18f, -22f, 3.1f),
                new Vector4(68f, 12f, 38f, 3.0f), new Vector4(70f, 5f, -12f, 2.9f),
                new Vector4(69f, -9f, 19f, 3.2f), new Vector4(67f, -16f, -28f, 3.4f),
                new Vector4(61f, -20f, 51f, 3.0f), new Vector4(52f, -21f, -17f, 2.8f),
                new Vector4(42f, -21f, 26f, 2.9f), new Vector4(32f, -21f, -33f, 2.7f)
            };
            foreach (var item in flowers)
                Add(result, "FlowerCluster", "Flowers", item.x, item.y, item.z, item.w, ColliderKind.PlantTrigger);

            // 좌측 상단에서 우측 하단으로 흐르는 S자 디딤돌 길.
            var stones = new[]
            {
                new Vector4(-4f, 18f, -9f, .72f), new Vector4(0f, 16f, 18f, .58f),
                new Vector4(4f, 13f, -21f, .76f), new Vector4(8f, 10f, 12f, .62f),
                new Vector4(12f, 7f, -14f, .72f), new Vector4(16f, 4f, 23f, .58f),
                new Vector4(20f, 1f, -18f, .76f), new Vector4(24f, -2f, 11f, .60f),
                new Vector4(28f, -5f, -24f, .74f), new Vector4(32f, -8f, 17f, .58f),
                new Vector4(36f, -10f, -9f, .78f), new Vector4(40f, -12f, 21f, .60f),
                new Vector4(44f, -13f, -16f, .74f), new Vector4(48f, -14f, 13f, .58f),
                new Vector4(52f, -14f, -11f, .76f), new Vector4(56f, -13f, 19f, .60f),
                new Vector4(60f, -11f, -17f, .72f)
            };
            foreach (var item in stones) AddStone(result, item.x, item.y, item.z, item.w);
            for (var i = 0; i < stones.Length - 1; i++)
            {
                var a = stones[i];
                var b = stones[i + 1];
                AddStone(result, (a.x + b.x) * .5f, (a.y + b.y) * .5f,
                    (a.z + b.z) * .5f + 17f, .22f + .03f * (i % 3));
            }

            return result;
        }

        private static void AddBoulderGroup(List<Placement> result, float x, float z,
            float scale, float rotation)
        {
            Add(result, "BoulderCluster", "Boulders", x, z, rotation, scale, ColliderKind.Box);
            Add(result, "BoulderCluster", "Boulders", x + 3.4f, z - 1.4f,
                rotation + 43f, scale * .66f, ColliderKind.Box);
            Add(result, "BoulderCluster", "Boulders", x - 2.8f, z + 2.1f,
                rotation - 37f, scale * .53f, ColliderKind.Box);
        }

        private static void Add(List<Placement> result, string prefabName, string groupName,
            float x, float z, float rotationY, float scale, ColliderKind collider)
        {
            result.Add(new Placement(prefabName, groupName, new Vector3(x, 0.06f, z),
                rotationY, Vector3.one * scale, collider));
        }

        private static void AddStone(List<Placement> result, float x, float z,
            float rotationY, float scale)
        {
            var planar = scale * 2.6f;
            result.Add(new Placement("FlatRockCracked", "SteppingStones",
                new Vector3(x, 0.06f, z), rotationY,
                new Vector3(planar, scale * .11f, planar), ColliderKind.Box));
        }

        private static int AddCliffTopFlowers(Scene scene, Transform layout,
            IDictionary<string, Transform> groups, IReadOnlyList<GameObject> cliffs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Environment/Stage1/PF_Stage1_FlowerCluster.prefab");
            if (prefab == null) return 0;

            var group = EnsureGroup(layout, groups, "CliffTopFlowers");
            var made = 0;
            for (var cliffIndex = 0; cliffIndex < cliffs.Count; cliffIndex++)
            {
                if (!TryRendererBounds(cliffs[cliffIndex], out var support)) continue;
                var count = cliffIndex % 2 == 0 ? 2 : 1;
                for (var i = 0; i < count; i++)
                {
                    var flower = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    flower.name = $"CliffTopFlower_{cliffIndex}_{i}";
                    flower.transform.SetParent(group, false);
                    flower.transform.rotation = Quaternion.Euler(0f, 37f * i + cliffIndex * 19f, 0f);
                    flower.transform.localScale = Vector3.one * (2.7f + .25f * i);
                    flower.transform.position = new Vector3(
                        support.center.x + support.extents.x * (i == 0 ? -.18f : .18f),
                        support.max.y,
                        support.center.z + support.extents.z * (i == 0 ? -.08f : .12f));

                    if (TryRendererBounds(flower, out var flowerBounds))
                    {
                        var position = flower.transform.position;
                        position.y += support.max.y - flowerBounds.min.y + .02f;
                        flower.transform.position = position;
                    }
                    AddSimpleCollider(flower, ColliderKind.PlantTrigger);
                    made++;
                }
            }
            return made;
        }

        private static void EnsureFlatRockPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FlatRockModelPath);
            if (model == null)
                throw new InvalidOperationException("디딤돌 FBX를 찾지 못했다: " + FlatRockModelPath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(FlatRockMaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                material = new Material(shader) { name = "M_Stage1_FlatRockCracked" };
                var baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(FlatRockBaseColorPath);
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", baseColor);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", baseColor);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .2f);
                AssetDatabase.CreateAsset(material, FlatRockMaterialPath);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlatRockPrefabPath);
            if (prefab == null)
            {
                var root = new GameObject("PF_Stage1_FlatRockCracked");
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                visual.name = "Visual";
                visual.transform.SetParent(root.transform, false);
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(root, FlatRockPrefabPath);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void SnapToTerrain(Transform instance)
        {
            if (!TryRendererBounds(instance.gameObject, out var bounds)) return;

            var samplePosition = new Vector3(bounds.center.x, 0f, bounds.center.z);
            var groundY = SampleGroundY(samplePosition, .06f);
            var position = instance.position;
            position.y += groundY - bounds.min.y + .01f;
            instance.position = position;
        }

        private static float SampleGroundY(Vector3 worldPosition, float fallback)
        {
            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var data = terrain.terrainData;
                if (data == null) continue;
                var min = terrain.transform.position;
                var max = min + new Vector3(data.size.x, 0f, data.size.z);
                if (worldPosition.x < min.x || worldPosition.x > max.x ||
                    worldPosition.z < min.z || worldPosition.z > max.z) continue;
                return terrain.SampleHeight(worldPosition) + terrain.transform.position.y;
            }
            return fallback;
        }

        private static void AddSimpleCollider(GameObject instance, ColliderKind kind)
        {
            if (kind == ColliderKind.None) return;

            RemoveGeneratedColliders(instance);
            if (kind == ColliderKind.PlantTrigger)
            {
                AddPlantTrigger(instance);
                return;
            }

            // Static nature follows the render mesh. A Rigidbody-bearing exception is
            // deliberately routed to convex compound boxes for PhysX stability.
            if (instance.GetComponentInChildren<Rigidbody>(true) == null
                && AddSurfaceMeshColliders(instance)) return;

            AddCompoundFallback(instance, kind);
        }

        private static bool AddSurfaceMeshColliders(GameObject instance)
        {
            return MeshColliderUtility.EnsureMeshColliders(instance, false, true) > 0;
        }

        private static void AddCompoundFallback(GameObject instance, ColliderKind kind)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || HasAuthoredCollider(renderer.gameObject)) continue;

                if (!TryWorldBoundsInLocal(instance.transform, renderer.bounds, out var bounds))
                    continue;

                // Keep each generated marker next to exactly one collider. A marker on
                // the root cannot identify which of several root BoxColliders it owns,
                // so repeated builds would leave stale shapes behind.
                var colliderObject = new GameObject($"GeneratedCollider_{i}");
                colliderObject.transform.SetParent(instance.transform, false);
                colliderObject.transform.localPosition = bounds.center;
                colliderObject.transform.localRotation = Quaternion.identity;

                var box = colliderObject.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = bounds.size;
                var marker = colliderObject.AddComponent<Stage1GeneratedCollider>();
                marker.RemoveOwnerOnCleanup = true;
            }
        }

        private static void AddPlantTrigger(GameObject instance)
        {
            if (!TryLocalRendererBounds(instance, out var bounds)) return;
            instance.layer = GameLayers.Prop;

            var authored = instance.GetComponentsInChildren<Collider>(true);
            if (authored.Length == 0)
            {
                var trigger = instance.AddComponent<BoxCollider>();
                trigger.center = bounds.center;
                trigger.size = bounds.size;
                trigger.isTrigger = true;
                instance.AddComponent<Stage1GeneratedCollider>();
            }
            else
            {
                for (var i = 0; i < authored.Length; i++)
                {
                    if (authored[i] == null) continue;
                    authored[i].isTrigger = true;
                    authored[i].gameObject.layer = GameLayers.Prop;
                }
            }

            var rustle = instance.GetComponent<PlantRustle>();
            if (rustle == null) rustle = instance.AddComponent<PlantRustle>();
        }

        private static void RemoveGeneratedColliders(GameObject instance)
        {
            var markers = instance.GetComponentsInChildren<Stage1GeneratedCollider>(true);
            for (var i = markers.Length - 1; i >= 0; i--)
            {
                var marker = markers[i];
                if (marker == null) continue;
                var collider = marker.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
                if (marker.RemoveOwnerOnCleanup && marker.gameObject != instance)
                    UnityEngine.Object.DestroyImmediate(marker.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(marker);
            }
        }

        private static bool HasAuthoredCollider(GameObject objectWithCollider)
        {
            var colliders = objectWithCollider.GetComponents<Collider>();
            for (var i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && colliders[i].GetComponent<Stage1GeneratedCollider>() == null)
                    return true;
            return false;
        }

        private static bool TryWorldBoundsInLocal(Transform root, Bounds worldBounds, out Bounds local)
        {
            local = default;
            var initialized = false;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var corner = worldBounds.center + Vector3.Scale(worldBounds.extents, new Vector3(x, y, z));
                var point = root.InverseTransformPoint(corner);
                if (!initialized)
                {
                    local = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else local.Encapsulate(point);
            }
            return initialized;
        }

        private static Transform EnsureGroup(Transform layout,
            IDictionary<string, Transform> groups, string groupName)
        {
            if (groups.TryGetValue(groupName, out var existing)) return existing;
            var group = new GameObject(groupName).transform;
            group.SetParent(layout, false);
            groups[groupName] = group;
            return group;
        }

        private static void RemoveExistingBoundaries(Transform environment)
        {
            var boundaries = environment.Find("Boundaries");
            if (boundaries != null) UnityEngine.Object.DestroyImmediate(boundaries.gameObject);
        }

        private static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        private static bool TryRendererBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }
            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        private static bool TryLocalRendererBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            var initialized = false;
            bounds = default;
            foreach (var renderer in renderers)
            {
                var world = renderer.bounds;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = world.center + Vector3.Scale(world.extents, new Vector3(x, y, z));
                    var local = root.transform.InverseTransformPoint(corner);
                    if (!initialized)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        initialized = true;
                    }
                    else bounds.Encapsulate(local);
                }
            }
            return initialized;
        }

        private static Transform FindByPath(Scene scene, string path)
        {
            var parts = path.Split('/');
            var current = scene.GetRootGameObjects().FirstOrDefault(root => root.name == parts[0])?.transform;
            for (var i = 1; current != null && i < parts.Length; i++) current = current.Find(parts[i]);
            return current;
        }
    }
}
