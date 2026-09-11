using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class AtmosphereProfileTests
    {
        AtmosphereProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<AtmosphereProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            if (profile != null)
                Object.DestroyImmediate(profile);
        }

        [Test]
        public void FogOnlyPreservesAuthoredLightingAndRestoresFog()
        {
            var root = new GameObject("FogOnlyTest");
            var controller = root.AddComponent<AtmosphereController>();
            var camera = root.AddComponent<Camera>();
            var originalSky = RenderSettings.skybox;
            var originalAmbient = RenderSettings.ambientSkyColor;
            var originalSun = RenderSettings.sun;
            bool originalFog = RenderSettings.fog;
            float originalDensity = RenderSettings.fogDensity;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.magenta;
            profile.fog.enabled = true;
            profile.fog.density = 0.025f;
            profile.haze.enabled = true;
            profile.haze.requiresDepth = true;
            profile.groundFog.enabled = true;
            try
            {
                controller.BindCamera(camera);
                controller.ApplyFogOnly(profile);
                controller.SetAlertIntensity(1f);
                Assert.That(RenderSettings.fog, Is.True);
                Assert.That(RenderSettings.fogDensity, Is.EqualTo(0.025f));
                Assert.That(Shader.GetGlobalFloat(AtmosphereController.HazeEnabledId), Is.EqualTo(1f));
                Assert.That(Shader.GetGlobalFloat(AtmosphereController.GroundFogEnabledId), Is.EqualTo(1f));
                Assert.That(RenderSettings.skybox, Is.EqualTo(originalSky));
                Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(originalAmbient));
                Assert.That(RenderSettings.sun, Is.EqualTo(originalSun));
                Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
                Assert.That(camera.backgroundColor, Is.EqualTo(Color.magenta));
                Assert.That(root.GetComponentsInChildren<Light>(), Is.Empty);
                Assert.That(root.transform.childCount, Is.Zero, "Fog-only mode must not create a post-processing volume or backdrop.");
                controller.ResetToDefaults();
                Assert.That(RenderSettings.fog, Is.EqualTo(originalFog));
                Assert.That(RenderSettings.fogDensity, Is.EqualTo(originalDensity));
                Assert.That(Shader.GetGlobalFloat(AtmosphereController.HazeEnabledId), Is.Zero);
                Assert.That(Shader.GetGlobalFloat(AtmosphereController.GroundFogEnabledId), Is.Zero);
            }
            finally
            {
                controller.ResetToDefaults();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ValidateClampsEffectLimits()
        {
            profile.stars.radius = 0f;
            profile.fog.startDistance = 30f;
            profile.fog.endDistance = 1f;
            profile.haze.startDistance = 40f;
            profile.haze.endDistance = 1f;
            profile.groundFog.startDistance = 30f;
            profile.groundFog.endDistance = 1f;
            profile.groundFog.heightFalloff = 0f;
            profile.groundFog.noiseScale = 0f;
            profile.groundFog.noiseStrength = 2f;
            profile.groundFog.density = 2f;
            profile.meteorShower.maxParticles = 100;
            profile.meteorShower.alertEmissionMultiplier = 0.5f;
            profile.meteorShower.lifetime = 0f;
            profile.mobileEffectScale = 0f;

            profile.Validate();

            Assert.That(profile.stars.radius, Is.EqualTo(1f));
            Assert.That(profile.fog.endDistance, Is.GreaterThan(profile.fog.startDistance));
            Assert.That(profile.haze.endDistance, Is.GreaterThan(profile.haze.startDistance));
            Assert.That(profile.groundFog.endDistance, Is.GreaterThan(profile.groundFog.startDistance));
            Assert.That(profile.groundFog.heightFalloff, Is.EqualTo(0.001f));
            Assert.That(profile.groundFog.noiseScale, Is.EqualTo(0.001f));
            Assert.That(profile.groundFog.noiseStrength, Is.EqualTo(1f));
            Assert.That(profile.groundFog.density, Is.EqualTo(1f));
            Assert.That(profile.meteorShower.maxParticles, Is.EqualTo(64));
            Assert.That(profile.meteorShower.alertEmissionMultiplier, Is.EqualTo(1f));
            Assert.That(profile.meteorShower.lifetime, Is.EqualTo(0.05f));
            Assert.That(profile.mobileEffectScale, Is.EqualTo(0.1f));
        }

        [Test]
        public void ValidateClampsDustStormWindTuning()
        {
            profile.dustStorm.windForce = -1f;
            profile.dustStorm.playerWindForceMultiplier = -1f;
            profile.dustStorm.objectWindForceMultiplier = -1f;
            profile.dustStorm.maximumAffectedMass = -1f;

            profile.Validate();

            Assert.That(profile.dustStorm.windForce, Is.EqualTo(0f));
            Assert.That(profile.dustStorm.playerWindForceMultiplier, Is.EqualTo(0f));
            Assert.That(profile.dustStorm.objectWindForceMultiplier, Is.EqualTo(0f));
            Assert.That(profile.dustStorm.maximumAffectedMass, Is.EqualTo(0f));
        }

        [Test]
        public void ProfileDefaultsKeepAtmosphereEffectsOptIn()
        {
            Assert.That(profile.fog.enabled, Is.False);
            Assert.That(profile.haze.enabled, Is.False);
            Assert.That(profile.groundFog.enabled, Is.False);
            Assert.That(profile.meteorShower.enabled, Is.False);
            Assert.That(profile.postProcessing.bloomEnabled, Is.False);
            Assert.That(profile.postProcessing.colorAdjustmentsEnabled, Is.False);
            Assert.That(profile.postProcessing.vignetteEnabled, Is.False);
            Assert.That(profile.enableOnMobile, Is.False);
        }

        [Test]
        public void ValidateNormalizesDustDirectionAndCapsParticleBudget()
        {
            profile.dustGust.direction = Vector3.zero;
            profile.dustGust.minimumInterval = 12f;
            profile.dustGust.maximumInterval = 1f;
            profile.dustGust.maxParticles = 999;
            profile.dustGust.burstCount = 999;
            profile.dustGust.startSize = 0f;
            profile.dustGust.endSize = 0f;

            profile.Validate();

            Assert.That(profile.dustGust.direction, Is.EqualTo(Vector3.right));
            Assert.That(profile.dustGust.maximumInterval,
                Is.GreaterThanOrEqualTo(profile.dustGust.minimumInterval));
            Assert.That(profile.dustGust.maxParticles, Is.EqualTo(128));
            Assert.That(profile.dustGust.burstCount, Is.EqualTo(128));
            Assert.That(profile.dustGust.startSize, Is.GreaterThan(0f));
            Assert.That(profile.dustGust.endSize, Is.GreaterThan(0f));
        }

        [Test]
        public void PlantRustle_PulsesVisualWithoutAddingPhysicsBody()
        {
            var plant = new GameObject("Plant");
            var visual = new GameObject("Visual");
            visual.transform.SetParent(plant.transform, false);
            var rustle = plant.AddComponent<PlantRustle>();

            try
            {
                rustle.Pulse(Vector3.right, 4f);

                Assert.Greater(rustle.CurrentSway.sqrMagnitude, 0.001f,
                    "식물 이벤트는 시각 자식에 흔들림을 줘야 한다.");
                Assert.IsNull(plant.GetComponent<Rigidbody>(),
                    "식물 흔들림 때문에 물리 Rigidbody를 추가하면 안 된다.");
            }
            finally
            {
                Object.DestroyImmediate(plant);
            }
        }

        [Test]
        public void AlertIntensityKeepsFogDensityStable()
        {
            profile.fog.enabled = true;
            profile.fog.density = 0.15f;

            var atmosphereObject = new GameObject("Atmosphere Controller Test");
            var controller = atmosphereObject.AddComponent<AtmosphereController>();

            try
            {
                controller.Apply(profile);
                float baselineDensity = RenderSettings.fogDensity;

                controller.SetAlertIntensity(1f);

                Assert.That(RenderSettings.fogDensity, Is.EqualTo(baselineDensity));
            }
            finally
            {
                controller.ResetToDefaults();
                Object.DestroyImmediate(atmosphereObject);
            }
        }
    }
}
