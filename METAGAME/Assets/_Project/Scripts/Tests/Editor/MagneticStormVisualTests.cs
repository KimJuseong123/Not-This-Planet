using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
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
