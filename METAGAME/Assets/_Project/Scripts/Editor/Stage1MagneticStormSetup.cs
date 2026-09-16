using System;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Gameplay;
using Metagame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static class Stage1MagneticStormSetup
    {
        const string ScenePath = "Assets/Scenes/Stage1_Astra.unity";
        const string ValidationKey = "Stage1MagneticStorm.Validation";
        static string Output => Path.GetFullPath("../artifacts/stage1-magnetic-storm/scene-integration");

        [MenuItem("Metagame/Art/Apply Magnetic Storm to Stage1 Astra")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before editing the stage.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isDirty)
                throw new InvalidOperationException("Stage1_Astra has unsaved edits. Save them before applying the storm.");
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var bootstrap = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SpaceBootstrap>(true)).Single();
                var bootstrapData = new SerializedObject(bootstrap);
                var reference = bootstrapData.FindProperty("stormPresentation");
                var presenter = reference.objectReferenceValue as AtmosphereDustStorm;
                if (presenter == null)
                {
                    var root = new GameObject("Stage1 Magnetic Storm");
                    SceneManager.MoveGameObjectToScene(root, scene);
                    presenter = root.AddComponent<AtmosphereDustStorm>();
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MagneticStormAssetBuilder.PrefabPath);
                    if (prefab == null) throw new InvalidOperationException("Magnetic storm prefab is missing.");
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    var visual = instance.GetComponent<MagneticStormVisual>();
                    var presenterData = new SerializedObject(presenter);
                    presenterData.FindProperty("magneticVisual").objectReferenceValue = visual;
                    presenterData.ApplyModifiedPropertiesWithoutUndo();
                    var profile = (AtmosphereProfile)bootstrapData.FindProperty("atmosphereProfile").objectReferenceValue;
                    var map = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AuthoredMapSettings>(true)).Single();
                    root.transform.position = map.Bounds.center;
                    var position = root.transform.position;
                    position.y = map.Bounds.min.y + profile.dustStorm.halfExtents.y + profile.dustStorm.groundClearance;
                    root.transform.position = position;
                    visual.SetExtents(profile.dustStorm.halfExtents);
                    reference.objectReferenceValue = presenter;
                    bootstrapData.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Stage save failed.");
                Debug.Log("[MagneticStorm] Stage1_Astra now references its own moving magnetic storm presenter.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        [MenuItem("Metagame/Art/Validate Stage1 Magnetic Storm in Play Mode")]
        public static void ValidateInPlayMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already in Play Mode.");
            if (SceneManager.GetActiveScene().path != ScenePath || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Open the saved Stage1_Astra scene first.");
            Directory.CreateDirectory(Output);
            SessionState.SetBool(ValidationKey, true);
            SessionState.SetFloat(ValidationKey + ".start", (float)EditorApplication.timeSinceStartup);
            ResumeValidation();
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void ResumeValidation()
        {
            EditorApplication.update -= PollValidation;
            if (SessionState.GetBool(ValidationKey, false)) EditorApplication.update += PollValidation;
        }

        static void PollValidation()
        {
            if (!SessionState.GetBool(ValidationKey, false)) return;
            if (!EditorApplication.isPlaying) return;
            if (Time.timeSinceLevelLoad < 18f) return;
            SessionState.SetBool(ValidationKey, false);
            try
            {
                var bootstrap = Object.FindAnyObjectByType<SpaceBootstrap>();
                var presenter = Object.FindAnyObjectByType<AtmosphereDustStorm>();
                var visuals = Object.FindObjectsByType<MagneticStormVisual>(FindObjectsInactive.Include);
                if (bootstrap == null || presenter == null || visuals.Length != 1)
                    throw new Exception("Expected one bootstrap, presenter and magnetic volume.");
                var visual = visuals[0];
                var sys = bootstrap.transform.Find("~시스템");
                if (sys == null || sys.localPosition.sqrMagnitude > .00001f || presenter.transform == sys)
                    throw new Exception("The storm must not move the shared systems.");
                if (presenter.GetComponentsInChildren<ParticleSystem>(true).Length != 0)
                    throw new Exception("Legacy sand emitters are still present.");
                var data = new SerializedObject(bootstrap);
                var profile = (AtmosphereProfile)data.FindProperty("atmosphereProfile").objectReferenceValue;
                if (!DustStormMatchSeed.TryGet(out int seed)) throw new Exception("Match seed is unavailable.");
                var schedule = new DustStormSchedule(profile.dustStorm.WithSeed(seed));
                var bounds = DustStormBounds.FindOrFallback();
                var clock = Object.FindAnyObjectByType<DustStormClockBridge>();
                clock.enabled = false;
                double gap = (profile.dustStorm.minimumInterval + profile.dustStorm.maximumInterval) * .5;
                double middle = gap + (bounds.size.magnitude * .5 + profile.dustStorm.halfExtents.x + profile.dustStorm.spawnMargin) / profile.dustStorm.speed;
                Vector3 first = default;
                for (int i = 0; i < 2; i++)
                {
                    double t = middle + i * 1.5;
                    presenter.Tick(t);
                    var expected = schedule.Evaluate(t, bounds);
                    if (!visual.gameObject.activeInHierarchy || Vector3.Distance(presenter.transform.position, expected.Center) > .001f)
                        throw new Exception("Visual position does not match the physics schedule.");
                    if (i == 0) first = presenter.transform.position;
                }
                float distance = Vector3.Distance(first, presenter.transform.position);
                if (Mathf.Abs(new Vector2(presenter.transform.position.x-first.x,presenter.transform.position.z-first.z).magnitude - 9f) > .01f)
                    throw new Exception("Storm travel should cover 9 metres in 1.5 seconds.");
                CapturePass(presenter, visual, middle);
                presenter.Tick(0);
                if (visual.gameObject.activeSelf) throw new Exception("Storm stays visible between passes.");
                presenter.Tick(middle);
                if (!visual.gameObject.activeSelf || Object.FindObjectsByType<MagneticStormVisual>().Length != 1)
                    throw new Exception("Storm is not reused on reactivation.");
                File.WriteAllText(Path.Combine(Output,"validation.txt"),
                    "PASS: actual Stage1_Astra Play Mode; one scene-wired volume; no legacy particles; stationary shared systems; shared seeded physics path; 9m horizontal travel in 1.5s; hidden between passes; instance reused; animated pass rendered.\n" +
                    "Bounds: " + bounds + "\nSeed: " + seed + "\nTravel distance including height: " + distance);
                Debug.Log("[MagneticStorm] Stage1 Play Mode validation PASS.");
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(Output,"validation.txt"), "FAIL: " + e);
                Debug.LogException(e);
            }
            finally { EditorApplication.ExitPlaymode(); }
        }

        static void CapturePass(AtmosphereDustStorm presenter, MagneticStormVisual visual, double time)
        {
            var camera = new GameObject("Temporary storm validation camera").AddComponent<Camera>();
            presenter.Tick(time);
            camera.transform.position = presenter.transform.position + new Vector3(20,12,-28);
            camera.transform.LookAt(presenter.transform.position);
            camera.farClipPlane = 400;
            camera.fieldOfView = 55;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            var rt = new RenderTexture(960,540,24);
            rt.Create();
            var previous = RenderTexture.active;
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                for (int i=0;i<3;i++)
                {
                    presenter.Tick(time + i * 1.5);
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    for(int warm=0;warm<3;warm++) RenderPipeline.SubmitRenderRequest(camera,request);
                    RenderTexture.active=rt;
                    var image=new Texture2D(960,540,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,960,540),0,0); image.Apply();
                    File.WriteAllBytes(Path.Combine(Output,"pass-"+i+".png"),image.EncodeToPNG());
                    Object.DestroyImmediate(image);
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=async;
                RenderTexture.active=previous; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(camera.gameObject);
            }
        }
    }
}
