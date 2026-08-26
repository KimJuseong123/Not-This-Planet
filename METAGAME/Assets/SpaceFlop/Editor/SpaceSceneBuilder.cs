using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceFlop.EditorTools
{
    /// <summary>
    /// 표면 씬을 다시 만든다. 씬에는 부트스트랩 컴포넌트 하나만 있고
    /// 나머지 세계는 실행할 때 코드가 세운다.
    /// </summary>
    public static class SpaceSceneBuilder
    {
        const string ScenePath = "Assets/SpaceFlop/Scenes/Surface.unity";

        [MenuItem("Tools/둥실둥실 우주 대탈출/표면 씬 다시 만들기", false, 1)]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("SpaceFlop");
            go.AddComponent<SpaceBootstrap>();

            System.IO.Directory.CreateDirectory("Assets/SpaceFlop/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log("[SpaceFlop] 표면 씬을 다시 만들었다 → " + ScenePath);
        }

        [MenuItem("Tools/둥실둥실 우주 대탈출/표면 씬 열기", false, 2)]
        public static void Open()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>빌드 전에 씬이 없으면 만들어 둔다 (처음 클론했을 때).</summary>
        public static void EnsureScene()
        {
            if (System.IO.File.Exists(ScenePath))
            {
                bool listed = false;
                var list = EditorBuildSettings.scenes;
                for (int i = 0; i < list.Length; i++)
                    if (list[i].path == ScenePath) { listed = true; break; }
                if (listed) return;

                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
                return;
            }
            Rebuild();
        }
    }
}
