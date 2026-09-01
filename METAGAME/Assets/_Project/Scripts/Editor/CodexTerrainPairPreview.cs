using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CodexTerrainPairPreview
{
    public static void Capture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
        {
            Debug.Log($"TERRAIN_PAIR|{terrain.name}|position={terrain.transform.position}|" +
                      $"data={AssetDatabase.GetAssetPath(terrain.terrainData)}|" +
                      $"prefab={PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(terrain.gameObject)}");
        }

        var cameraObject = new GameObject("CodexTerrainPairCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(25f, 120f, 0f);
        camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        camera.orthographic = true;
        camera.orthographicSize = 31f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.025f, .02f, .05f, 1f);
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 250f;
        camera.allowHDR = false;

        var renderTexture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = renderTexture;
        camera.Render();

        RenderTexture.active = renderTexture;
        var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0f, 0f, 1600f, 900f), 0, 0);
        texture.Apply();
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../stage1-flat-cliff-preview.png"));
        File.WriteAllBytes(output, texture.EncodeToPNG());
        Debug.Log("TERRAIN_PAIR_PREVIEW|" + output);

        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(texture);
        Object.DestroyImmediate(renderTexture);
        Object.DestroyImmediate(cameraObject);
    }
}
