using System.IO;
using Metagame.Network;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metagame.EditorTools
{
    public static class Stage2Validation
    {
        public static void RefreshAndRender()
        { Stage2SceneBuilder.RefreshDraft(); RenderOverview(); }
        public static void RefreshAndBuildNetworkSmoke()
        { Stage2SceneBuilder.RefreshDraft(); BuildNetworkSmoke(); }
        public static void BuildNetworkSmoke()
        {
            const string temporary = "Assets/Scenes/Stage2SmokeLauncher.unity";
            if (File.Exists(temporary)) throw new System.InvalidOperationException("Temporary smoke scene already exists.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var harness=new GameObject("Development-only Stage2 network test").AddComponent<Stage2NetworkSmoke>();
            harness.Configure(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Network/PF_NetPlayer.prefab"));
            if (!EditorSceneManager.SaveScene(scene,temporary)) throw new System.InvalidOperationException("Could not save smoke launcher.");
            AssetDatabase.ImportAsset(temporary,ImportAssetOptions.ForceSynchronousImport);
            try
            {
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes=new[]{temporary,Stage2SceneBuilder.ScenePath},
                    locationPathName="../artifacts/stage2-smoke/Stage2Smoke.exe",
                    target=BuildTarget.StandaloneWindows64,
                    options=BuildOptions.Development
                });
                if(report.summary.result!=BuildResult.Succeeded) throw new System.InvalidOperationException("Stage2 smoke player build failed.");
                Debug.Log("[Stage2] Network smoke build succeeded");
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single); AssetDatabase.DeleteAsset(temporary); }
        }

        public static void RenderOverview()
        {
            EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            var camera=GameObject.Find("Editor overview camera").GetComponent<Camera>();
            var target=new RenderTexture(1600,1000,24);
            var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1600,1000),0,0); image.Apply();
            Directory.CreateDirectory("../artifacts/stage2-draft");
            File.WriteAllBytes("../artifacts/stage2-draft/overview.png",image.EncodeToPNG());
            camera.targetTexture=null; RenderTexture.active=null;
            Object.DestroyImmediate(image); Object.DestroyImmediate(target);
            Debug.Log("[Stage2] Overview captured");
        }

        public static void DiagnosePreview()
        {
            Stage2SceneBuilder.RefreshDraft();
            UnityEditor.ShaderUtil.allowAsyncCompilation=false;
            RenderOverview();
            var camera=GameObject.Find("Editor overview camera").GetComponent<Camera>();
            var rt=new RenderTexture(1600,1000,24); camera.targetTexture=rt;
            var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            bool batching=UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching;
            try
            {
                UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching=false;
                camera.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,1600,1000),0,0); image.Apply();
                File.WriteAllBytes("../artifacts/stage2-draft/overview-no-batcher.png",image.EncodeToPNG());
                UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching=batching;
                foreach(var renderer in Object.FindObjectsByType<MeshRenderer>())
                {
                    var material=renderer.sharedMaterial;
                    if(material==null || !material.HasProperty("_BaseColor"))continue;
                    var block=new MaterialPropertyBlock(); block.SetColor("_BaseColor",material.GetColor("_BaseColor")); renderer.SetPropertyBlock(block);
                }
                camera.Render(); image.ReadPixels(new Rect(0,0,1600,1000),0,0); image.Apply();
                File.WriteAllBytes("../artifacts/stage2-draft/overview-explicit-color.png",image.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching=batching;
                camera.targetTexture=null; RenderTexture.active=null; Object.DestroyImmediate(image); Object.DestroyImmediate(rt);
            }
        }
    }
}

