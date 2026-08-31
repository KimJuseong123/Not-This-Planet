using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    /// <summary>
    /// S15P21B202-74의 Stage1 맵 타일 원본을 기본 Terrain에 연결한다.
    /// 50m 지면에 5m 단위로 반복하므로 화면에는 10 × 10 타일처럼 보이지만,
    /// 실제 충돌과 지형 편집은 하나의 Terrain으로 유지한다.
    /// </summary>
    public static class Stage1TileMapBuilder
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string TexturePath =
            "Assets/_Project/Textures/Environment/Stage1/Renewal/Ground/" +
            "T_Stage1_AlienGroundTile_BaseColor.png";
        const string TerrainDataPath =
            "Assets/_Project/Terrain/Stage1/TerrainData/TD_Stage1_Arena.asset";
        const string TerrainLayerFolder = "Assets/_Project/Terrain/Stage1/TerrainLayers";
        const string TerrainLayerPath = TerrainLayerFolder + "/TL_Stage1_AlienGround.asset";

        const float TileSize = 5f;

        [MenuItem("Tools/둥실둥실 우주 대탈출/Stage1 맵 타일 적용", false, 8)]
        public static void Apply()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Build();
        }

        public static void Build()
        {
            EnsureTextureImport();
            var layer = EnsureTerrainLayer();
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);

            if (layer == null || data == null)
            {
                Debug.LogError("[Stage1TileMap] 맵 타일 원본 또는 TerrainData를 찾지 못했다. " +
                               "S15P21B202-177 Terrain을 먼저 적용한다.");
                return;
            }

            data.terrainLayers = new[] { layer };
            PaintWholeTerrain(data);
            EditorUtility.SetDirty(data);

            ApplyToMainScene(data);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Stage1TileMap] 맵 타일 적용 완료 — 50m Terrain에 5m 단위 10 × 10 반복 → "
                      + ScenePath);
        }

        static void EnsureTextureImport()
        {
            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null) return;

            var changed = importer.wrapMode != TextureWrapMode.Repeat
                          || !importer.mipmapEnabled
                          || importer.anisoLevel < 4;
            if (!changed) return;

            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }

        static TerrainLayer EnsureTerrainLayer()
        {
            Directory.CreateDirectory(TerrainLayerFolder);
            AssetDatabase.Refresh();

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture == null)
            {
                Debug.LogError("[Stage1TileMap] Base Color를 찾지 못했다: " + TexturePath);
                return null;
            }

            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainLayerPath);
            if (layer == null)
            {
                layer = new TerrainLayer { name = "TL_Stage1_AlienGround" };
                AssetDatabase.CreateAsset(layer, TerrainLayerPath);
            }

            layer.diffuseTexture = texture;
            layer.tileSize = new Vector2(TileSize, TileSize);
            layer.tileOffset = Vector2.zero;
            layer.metallic = 0f;
            layer.smoothness = 0.08f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        static void PaintWholeTerrain(TerrainData data)
        {
            var resolution = data.alphamapResolution;
            var weights = new float[resolution, resolution, 1];

            for (var z = 0; z < resolution; z++)
                for (var x = 0; x < resolution; x++)
                    weights[z, x, 0] = 1f;

            data.SetAlphamaps(0, 0, weights);
        }

        static void ApplyToMainScene(TerrainData data)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrainObject = FindByPath(scene,
                "Stage1/Environment/Ground/ENV_Terrain_Arena_50m");

            if (terrainObject == null)
            {
                Debug.LogError("[Stage1TileMap] Main 씬의 Stage1 Terrain을 찾지 못했다.");
                return;
            }

            var terrain = terrainObject.GetComponent<Terrain>();
            var collider = terrainObject.GetComponent<TerrainCollider>();
            if (terrain == null || collider == null)
            {
                Debug.LogError("[Stage1TileMap] Terrain 또는 TerrainCollider가 없다.");
                return;
            }

            var changed = terrain.terrainData != data || collider.terrainData != data;
            if (!changed) return;

            terrain.terrainData = data;
            collider.terrainData = data;
            EditorUtility.SetDirty(terrain);
            EditorUtility.SetDirty(collider);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
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
