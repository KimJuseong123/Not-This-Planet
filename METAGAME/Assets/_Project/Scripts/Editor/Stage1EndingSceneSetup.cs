using System;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Gameplay.Parts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    public static class Stage1EndingSceneSetup
    {
        [MenuItem("Not This Planet/Scenes/Create Stage1 Ending")]
        public static void Create()
        {
            if (File.Exists(EndingSceneFlow.ScenePath))
                throw new InvalidOperationException("Stage1_Ending already exists; edit the existing scene.");
            if (Application.isBatchMode) EditorSceneManager.OpenScene(EndingSceneFlow.LobbyPath);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.fog = false;
                RenderSettings.skybox = null;
                RenderSettings.ambientLight = new Color(0.15f, 0.18f, 0.24f);
                var bootstrap = new GameObject("Stage1 Ending").AddComponent<Stage1EndingBootstrap>();
                var serialized = new SerializedObject(bootstrap);
                var visuals = serialized.FindProperty("partVisuals");
                visuals.arraySize = PartSpec.All.Length;
                for (var i = 0; i < PartSpec.All.Length; i++)
                {
                    var path = "Assets/_Project/Prefabs/Spaceships/Stage1/" + PartSpec.All[i].SkinPrefab + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) throw new InvalidOperationException("Missing ending visual: " + path);
                    visuals.GetArrayElementAtIndex(i).objectReferenceValue = prefab;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var camera = new GameObject("Loading Camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 0;
                camera.gameObject.AddComponent<AudioListener>();
                if (!EditorSceneManager.SaveScene(scene, EndingSceneFlow.ScenePath))
                    throw new InvalidOperationException("Could not save ending scene.");
                var list = EditorBuildSettings.scenes.ToList();
                if (list.All(s => s.path != EndingSceneFlow.ScenePath))
                    list.Add(new EditorBuildSettingsScene(EndingSceneFlow.ScenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
