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
        public void ValidateClampsEffectLimits()
        {
            profile.stars.radius = 0f;
            profile.fog.startDistance = 30f;
            profile.fog.endDistance = 1f;
            profile.haze.startDistance = 40f;
            profile.haze.endDistance = 1f;
            profile.meteorShower.maxParticles = 100;
            profile.meteorShower.alertEmissionMultiplier = 0.5f;
            profile.meteorShower.lifetime = 0f;
            profile.mobileEffectScale = 0f;

            profile.Validate();

            Assert.That(profile.stars.radius, Is.EqualTo(1f));
            Assert.That(profile.fog.endDistance, Is.GreaterThan(profile.fog.startDistance));
            Assert.That(profile.haze.endDistance, Is.GreaterThan(profile.haze.startDistance));
            Assert.That(profile.meteorShower.maxParticles, Is.EqualTo(64));
            Assert.That(profile.meteorShower.alertEmissionMultiplier, Is.EqualTo(1f));
            Assert.That(profile.meteorShower.lifetime, Is.EqualTo(0.05f));
            Assert.That(profile.mobileEffectScale, Is.EqualTo(0.1f));
        }

        [Test]
        public void ProfileDefaultsKeepAtmosphereEffectsOptIn()
        {
            Assert.That(profile.fog.enabled, Is.False);
            Assert.That(profile.haze.enabled, Is.False);
            Assert.That(profile.meteorShower.enabled, Is.False);
            Assert.That(profile.postProcessing.bloomEnabled, Is.False);
            Assert.That(profile.postProcessing.colorAdjustmentsEnabled, Is.False);
            Assert.That(profile.postProcessing.vignetteEnabled, Is.False);
            Assert.That(profile.enableOnMobile, Is.False);
        }
    }
}
