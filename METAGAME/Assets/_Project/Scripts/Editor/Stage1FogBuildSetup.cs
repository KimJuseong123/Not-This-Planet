using System;
using System.Linq;
using Metagame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace Metagame.EditorTools
{
    public static class Stage1FogBuildSetup
    {
        public static void Install()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch setup only.");
            var scene = EditorSceneManager.OpenScene(Stage1AstraIntegration.ScenePath);
            var controller = UnityEngine.Object.FindFirstObjectByType<Stage1FogController>();
            if (controller == null) controller = new GameObject("Stage1 Distance Fog").AddComponent<Stage1FogController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("fogShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/Atmosphere/Stage1DistanceFog.shader");
            var beacons = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Renderer>(true))
                .Where(r=>r.name.StartsWith("Module beacon",StringComparison.Ordinal)||r.name=="02 Tractor sky beacon").ToArray();
            if (beacons.Length != 4) throw new InvalidOperationException("Expected four sky beacons.");
            var refs=serialized.FindProperty("skyBeacons"); refs.arraySize=beacons.Length;
            for(int i=0;i<beacons.Length;i++) refs.GetArrayElementAtIndex(i).objectReferenceValue=beacons[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
            foreach(var path in new[]{"Assets/Settings/PC_Renderer.asset","Assets/Settings/Mobile_Renderer.asset"})
            {
                var renderer=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if(renderer==null) throw new InvalidOperationException("Renderer missing: "+path);
                if(!renderer.rendererFeatures.OfType<Stage1FogRendererFeature>().Any())
                {
                    var feature=ScriptableObject.CreateInstance<Stage1FogRendererFeature>();
                    feature.name="Stage1 Distance Fog";
                    AssetDatabase.AddObjectToAsset(feature,renderer);
                    renderer.rendererFeatures.Add(feature);
                }
                    var rendererState = new SerializedObject(renderer);
                    var map = rendererState.FindProperty("m_RendererFeatureMap");
                    map.arraySize = renderer.rendererFeatures.Count;
                    for (int i = 0; i < map.arraySize; i++)
                    {
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string guid, out long localId);
                        map.GetArrayElementAtIndex(i).longValue = localId;
                    }
                    rendererState.ApplyModifiedPropertiesWithoutUndo();
                    renderer.SetDirty(); EditorUtility.SetDirty(renderer);
                    AssetDatabase.SaveAssetIfDirty(renderer);
            }
            Debug.Log("[Fog411] BUILD SETUP PASS: scene controller, shader reference, four beacons, PC/Mobile renderer features.");
        }
        public static void BuildSmoke()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch build only.");
            Install();
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{Stage1AstraIntegration.ScenePath}, locationPathName="../fog-build-smoke/NotThisPlanet.exe",
                target=BuildTarget.StandaloneWindows64, options=BuildOptions.Development
            });
            if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Fog player build failed: "+report.summary.result);
            Debug.Log("[Fog411] PLAYER BUILD PASS");
        }
    }
}
