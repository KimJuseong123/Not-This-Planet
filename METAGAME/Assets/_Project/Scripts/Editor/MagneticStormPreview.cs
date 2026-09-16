using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Metagame.Gameplay;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static class MagneticStormPreview
    {
        public static void BuildAndRender()
        {
            MagneticStormAssetBuilder.Build();
            RenderPreviews();
        }
        [MenuItem("Metagame/Art/Open Magnetic Storm Preview")]
        public static void OpenPreview()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(MagneticStormAssetBuilder.Root + "/Preview/MagneticStormPreview.unity");
        }

        public static void RenderPreviews()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("RenderPreviews is an isolated batch validation command.");
            Directory.CreateDirectory(MagneticStormAssetBuilder.Artifacts);
            var priorPipeline = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            bool priorAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.ambientLight = new Color(.18f, .15f, .24f);
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MagneticStormAssetBuilder.PrefabPath));
                var visual = root.GetComponent<MagneticStormVisual>();
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Preview Ground";
                ground.transform.position = new Vector3(0, -2.65f, 0);
                ground.transform.localScale = new Vector3(90, .2f, 90);
                var groundPath = MagneticStormAssetBuilder.Root + "/Unity/PreviewGround.mat";
                var groundMat = AssetDatabase.LoadAssetAtPath<Material>(groundPath);
                if (groundMat == null)
                {
                    groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    groundMat.SetColor("_BaseColor", new Color(.085f, .085f, .14f));
                    groundMat.SetFloat("_Smoothness", .15f);
                    AssetDatabase.CreateAsset(groundMat, groundPath);
                }
                ground.GetComponent<Renderer>().sharedMaterial = groundMat;
                var light = new GameObject("Preview Soft Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.color = new Color(.75f, .78f, 1f);
                light.transform.rotation = Quaternion.Euler(45,-20,0);
                var volumeProfilePath = MagneticStormAssetBuilder.Root + "/Unity/PreviewBloom.asset";
                var volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(volumeProfilePath);
                if (volumeProfile == null)
                {
                    volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    AssetDatabase.CreateAsset(volumeProfile, volumeProfilePath);
                    var bloom = volumeProfile.Add<Bloom>(true);
                    bloom.intensity.Override(.6f);
                    bloom.threshold.Override(.9f);
                    bloom.scatter.Override(.65f);
                    AssetDatabase.AddObjectToAsset(bloom, volumeProfile);
                    EditorUtility.SetDirty(volumeProfile);
                    AssetDatabase.SaveAssets();
                }
                var bloomVolume = new GameObject("Preview Bloom").AddComponent<Volume>();
                bloomVolume.isGlobal = true;
                bloomVolume.sharedProfile = volumeProfile;
                var camera = MakeCamera();
                SetCamera(camera, new Vector3(12,5,-21), Vector3.zero);
                string previewFolder = MagneticStormAssetBuilder.Root + "/Preview";
                Directory.CreateDirectory(previewFolder);
                AssetDatabase.Refresh();
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), previewFolder + "/MagneticStormPreview.unity");
                Capture(camera, "unity-perspective", visual, 0);
                SetCamera(camera, new Vector3(0,1,-24), Vector3.zero);
                Capture(camera, "unity-front-t0", visual, 0);
                Capture(camera, "unity-front-t1_5", visual, 1.5);
                Capture(camera, "unity-front-t6", visual, 6);
                SetCamera(camera, new Vector3(28,3,0), Vector3.zero);
                Capture(camera, "unity-side", visual, 0);
                camera.orthographic = true; camera.orthographicSize = 10;
                camera.transform.SetPositionAndRotation(new Vector3(0,28,0),Quaternion.Euler(90,0,0));
                Capture(camera, "unity-top", visual, 0);
                camera.orthographic = false;
                SetCamera(camera, Vector3.zero, Vector3.forward * 10);
                Capture(camera, "unity-inside", visual, 0);
                MagneticStormAssetBuilder.Validate();

                // Inspect the authored scene without saving any changes into it.
                EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity");
                var shelter = Object.FindObjectsByType<Transform>().FirstOrDefault(t =>
                    t.name.IndexOf("AstraShelter", StringComparison.OrdinalIgnoreCase) >= 0);
                var position = shelter != null ? shelter.position + new Vector3(15,0,4) : Vector3.zero;
                var bounds = DustStormBounds.FindOrFallback();
                position.y = DustStormBounds.SampleGroundHeight(position, bounds) + 2.58f;
                root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MagneticStormAssetBuilder.PrefabPath));
                root.transform.position = position;
                visual = root.GetComponent<MagneticStormVisual>();
                camera = MakeCamera();
                SetCamera(camera, position + new Vector3(18,9,-27), position);
                Capture(camera, "unity-stage1-astra", visual, 1.5);
                Debug.Log("[MagneticStorm] Studio, inside-volume, orthographic and Stage1_Astra captures complete.");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = priorAsync;
                QualitySettings.renderPipeline = priorPipeline;
            }
        }

        static Camera MakeCamera()
        {
            var cam = new GameObject("Magnetic Storm Preview Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.015f,.012f,.035f);
            cam.fieldOfView = 50;
            cam.nearClipPlane = .05f;
            cam.farClipPlane = 200;
            cam.allowHDR = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.requiresDepthTexture = true;
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            return cam;
        }

        static void SetCamera(Camera camera, Vector3 position, Vector3 target)
        {
            camera.transform.position = position;
            camera.transform.LookAt(target);
        }

        static void Capture(Camera camera, string name, MagneticStormVisual visual, double time)
        {
            visual.Evaluate(time);
            var rt = new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
            rt.Create();
            var previous = RenderTexture.active;
            var image = new Texture2D(1280,720,TextureFormat.RGB24,false);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                for (int i=0;i<3;i++) RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
                File.WriteAllBytes(Path.Combine(MagneticStormAssetBuilder.Artifacts,name+".png"),image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
            }
        }
    }
}
