using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay.Tests
{
    public sealed class MagneticStormVisualTests
    {
        [UnityTearDown]
        public IEnumerator LeavePlayModeAfterFailure()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [TestCase(0d, 0f)]
        [TestCase(1.5d, .25f)]
        [TestCase(6d, 0f)]
        [TestCase(123456.75d, .125f)]
        [TestCase(-1.5d, .75f)]
        public void PresentationClockWrapsWithoutDrift(double time, float expected)
        {
            Assert.That(MagneticStormVisual.PhaseAt(time), Is.EqualTo(expected).Within(.00001f));
        }

        [Test]
        public void DirectionalBillowingAndWakeVisibilityUseIndependentShaderValues()
        {
            var parent = new GameObject("Storm visual test owner");
            var root = Object.Instantiate(Resources.Load<GameObject>(MagneticStormVisual.ResourcePath), parent.transform);
            try
            {
                var visual = root.GetComponent<MagneticStormVisual>();
                visual.SetExternallyDriven();
                visual.SetExtents(new Vector3(12, 2.5f, 2.5f));
                var so = new SerializedObject(visual);
                so.FindProperty("frontBillowStrength").floatValue = .4f;
                so.FindProperty("rearBillowStrength").floatValue = 2.2f;
                so.ApplyModifiedPropertiesWithoutUndo();
                visual.Evaluate(16);
                var block = new MaterialPropertyBlock();
                root.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
                Assert.That(block.GetFloat("_FrontBillowStrength"), Is.EqualTo(.4f).Within(.0001));
                Assert.That(block.GetFloat("_RearBillowStrength"), Is.EqualTo(2.2f).Within(.0001));
                var settings = new AtmosphereProfile.DustStormSettings { enabled = true };
                var wake = new DustStormSchedule(settings).EvaluateWake(16,
                    new Bounds(Vector3.zero, new Vector3(80, 10, 80)));
                visual.EvaluateWake(0, wake, 16, .3f);
                var renderer = parent.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Magnetic Storm Wake 0");
                Assert.That(Vector3.Distance(renderer.bounds.center, wake.Pose.Center), Is.LessThan(.001));
                renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_StormOpacity"), Is.EqualTo(.3f).Within(.0001));
                Assert.That(block.GetVector("_WakeAges").w, Is.EqualTo(1));
                visual.SetBodyVisible(false);
                Assert.That(root.GetComponentsInChildren<Renderer>().All(r => !r.enabled), Is.True);
                Assert.That(renderer.enabled, Is.True, "Wake persists independently of the hidden main body.");
                visual.EvaluateWake(0, default, 20, .3f);
                Assert.That(renderer.enabled, Is.False);
                visual.EvaluateWake(0, wake, 16, .3f);
                Assert.That(parent.GetComponentsInChildren<MeshRenderer>().Count(r => r.name == "Magnetic Storm Wake 0"), Is.EqualTo(1));
                visual.HideWakes();
                Assert.That(renderer.enabled, Is.False);
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(173f)]
        public void BillowDisplacementFollowsWorldTravelDespiteImportedMeshRotation(float meshYaw)
        {
            var root = Object.Instantiate(Resources.Load<GameObject>(MagneticStormVisual.ResourcePath));
            try
            {
                var visual = root.GetComponent<MagneticStormVisual>();
                root.transform.rotation = Quaternion.Euler(0, 37, 0);
                visual.SetExtents(new Vector3(12, 2.5f, 2.5f));
                var smoke = root.GetComponentsInChildren<MeshRenderer>()
                    .Single(r => r.sharedMaterial.HasProperty("_WakeAges"));
                smoke.transform.localRotation = Quaternion.Euler(0, meshYaw, 0) * smoke.transform.localRotation;
                var direction = new Vector3(-.8f, 0, .6f);
                visual.Evaluate(1.5, 1, direction);
                var block = new MaterialPropertyBlock();
                smoke.GetPropertyBlock(block);
                Vector3 offset = block.GetVector("_TravelOffsetUV");
                Vector3 worldDisplacement = smoke.transform.TransformVector(
                    Vector3.Scale(offset, new Vector3(24, 5, 10)));
                Assert.That(Vector3.Distance(worldDisplacement, direction), Is.LessThan(.0001));
                Assert.That(Mathf.Abs(Vector3.Dot(worldDisplacement, Vector3.Cross(Vector3.up, direction))), Is.LessThan(.0001));
                Vector3 axis = block.GetVector("_TravelAxisUV");
                Assert.That(Vector3.Dot(axis, offset), Is.GreaterThan(0), "Front mask must increase along actual travel.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator ExistingStormPresenterControlsVisibilitySeedReuseAndCleanup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var profile = ScriptableObject.CreateInstance<AtmosphereProfile>();
            profile.dustStorm.enabled = true;
            profile.dustStorm.minimumInterval = 8;
            profile.dustStorm.maximumInterval = 14;
            profile.dustStorm.speed = 6;
            profile.Validate();
            var stationarySystems = new GameObject("Stationary stage systems");
            stationarySystems.transform.position = new Vector3(4, 0, 8);
            var host = new GameObject("Storm integration test");
            host.transform.SetParent(stationarySystems.transform, false);
            var presenter = host.AddComponent<AtmosphereDustStorm>();
            presenter.BindMapBounds(new Bounds(Vector3.zero,new Vector3(80,10,80)));
            presenter.Apply(profile);
            var visual = host.GetComponentInChildren<MagneticStormVisual>(true);
            Assert.That(visual, Is.Not.Null, "Resources prefab must be present in the content pack.");
            Assert.That(visual.gameObject.activeSelf, Is.False);
            presenter.Tick(12d);
            Assert.That(visual.gameObject.activeSelf, Is.True);
            Assert.That(host.GetComponentsInChildren<MagneticStormVisual>(true).Length, Is.EqualTo(1));
            Vector3 before = host.transform.position;
            presenter.Tick(13.5d);
            Assert.That(Vector2.Distance(new Vector2(before.x, before.z),
                new Vector2(host.transform.position.x, host.transform.position.z)), Is.EqualTo(9f).Within(.001f));
            Assert.That(stationarySystems.transform.position, Is.EqualTo(new Vector3(4, 0, 8)));
            var cameraObject = new GameObject("Storm depth test camera");
            var camera = cameraObject.AddComponent<Camera>();
            var data = camera.GetUniversalAdditionalCameraData();
            data.requiresDepthOption = CameraOverrideOption.Off;
            var rt = new RenderTexture(32,32,24);
            rt.Create();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
            Assert.That(data.requiresDepthOption, Is.EqualTo(CameraOverrideOption.On));
            presenter.Tick(10d);
            Assert.That(visual.gameObject.activeSelf, Is.False);
            Assert.That(data.requiresDepthOption, Is.EqualTo(CameraOverrideOption.Off));
            rt.Release(); Object.Destroy(rt); Object.Destroy(cameraObject);
            presenter.SetMatchSeed(87654);
            presenter.Tick(13.5d);
            Assert.That(visual.gameObject.activeSelf, Is.True);
            Assert.That(host.GetComponentsInChildren<MagneticStormVisual>(true).Length, Is.EqualTo(1));
            var renderers=visual.GetComponentsInChildren<Renderer>();
            var block=new MaterialPropertyBlock();
            renderers[0].GetPropertyBlock(block);
            Assert.That(block.GetFloat("_StormPhase"), Is.EqualTo(.25f).Within(.00001f));
            Assert.That(host.GetComponentsInChildren<ParticleSystem>(true), Is.Empty,
                "The magnetic presentation replaces the old sand and decorative debris emitters.");
            presenter.enabled=false;
            Assert.That(visual.gameObject.activeSelf, Is.False);
            Assert.That(host.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.name.StartsWith("Magnetic Storm Wake")).All(r => !r.enabled), Is.True);
            presenter.enabled=true;
            presenter.Apply(profile);
            presenter.Tick(12d);
            Assert.That(visual.gameObject.activeSelf, Is.True);
            Assert.That(host.GetComponentsInChildren<MagneticStormVisual>(true).Length, Is.EqualTo(1));
            Object.Destroy(host);
            Object.Destroy(stationarySystems);
            Object.Destroy(profile);
            yield return null;
            Assert.That(Object.FindObjectsByType<MagneticStormVisual>().Length, Is.Zero);
            yield return new ExitPlayMode();
        }
    }
}
