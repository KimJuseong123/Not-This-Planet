using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Metagame.Gameplay.Audio;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage1SoundTests
    {
        [Test]
        public void MaterialImpactPairsMapToSixDistinctEvents()
        {
            Assert.AreEqual(SoundEventId.ImpactMetalMetal,
                SoundSurface.ImpactEvent(SoundSurfaceKind.Metal, SoundSurfaceKind.Metal));
            Assert.AreEqual(SoundEventId.ImpactMetalHard,
                SoundSurface.ImpactEvent(SoundSurfaceKind.HardNonMetal, SoundSurfaceKind.Metal));
            Assert.AreEqual(SoundEventId.ImpactMetalSoft,
                SoundSurface.ImpactEvent(SoundSurfaceKind.SoftNonMetal, SoundSurfaceKind.Metal));
            Assert.AreEqual(SoundEventId.ImpactHardHard,
                SoundSurface.ImpactEvent(SoundSurfaceKind.HardNonMetal, SoundSurfaceKind.HardNonMetal));
            Assert.AreEqual(SoundEventId.ImpactHardSoft,
                SoundSurface.ImpactEvent(SoundSurfaceKind.SoftNonMetal, SoundSurfaceKind.HardNonMetal));
            Assert.AreEqual(SoundEventId.ImpactSoftSoft,
                SoundSurface.ImpactEvent(SoundSurfaceKind.SoftNonMetal, SoundSurfaceKind.SoftNonMetal));
        }

        [Test]
        public void Stage1SoundBankContainsEveryEventAndConfiguredClips()
        {
            var bank = AssetDatabase.LoadAssetAtPath<Stage1SoundBank>(
                "Assets/_Project/Resources/Audio/Stage1SoundBank.asset");
            Assert.IsNotNull(bank, "Stage1SoundBank asset is missing or not imported.");
            Assert.IsTrue(bank.HasAllEventSlots(), "At least one logical sound slot is missing.");

            for (var i = 0; i < (int)SoundEventId.Count; i++)
            {
                var slot = bank.GetSlot((SoundEventId)i);
                Assert.IsNotNull(slot, "Sound slot missing for " + (SoundEventId)i);
                Assert.IsTrue(slot.maxDistance > 0f, "Spatial max distance missing for " + (SoundEventId)i);

                // 아직 제작 전인 슬롯은 의도적으로 비워 둔다. 클립이 연결되기 전에는
                // SoundRuntime.Resolve 가 null을 반환해 해당 상호작용이 무음으로 남는다.
                if (i > (int)SoundEventId.Footstep)
                {
                    Assert.IsNull(slot.stylized, "Future stylized clip should be empty for " + (SoundEventId)i);
                    Assert.IsNull(slot.realistic, "Future realistic clip should be empty for " + (SoundEventId)i);
                    continue;
                }

                Assert.IsNotNull(slot.stylized, "Stylized clip missing for " + (SoundEventId)i);
                Assert.IsNotNull(slot.realistic, "Realistic clip missing for " + (SoundEventId)i);
                Assert.AreSame(slot.stylized, slot.realistic,
                    "Only the Stylized physical clip set should be referenced for " + (SoundEventId)i);
            }
        }

        [Test]
        public void FootstepUsesExistingRabbitWalkClip()
        {
            var bank = AssetDatabase.LoadAssetAtPath<Stage1SoundBank>(
                "Assets/_Project/Resources/Audio/Stage1SoundBank.asset");
            var source = AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/_Project/Audio/SFX_Rabbit_Walk_Space_Light.wav");
            var slot = bank != null ? bank.GetSlot(SoundEventId.Footstep) : null;

            Assert.IsNotNull(bank, "Stage1SoundBank asset is missing or not imported.");
            Assert.IsNotNull(source, "Existing rabbit walk clip is missing or not imported.");
            Assert.IsNotNull(slot, "Footstep sound slot is missing.");
            Assert.AreSame(source, slot.stylized);
            Assert.AreSame(source, slot.realistic);
        }

        [Test]
        public void AudioManagerProvidesCentralStage1Settings()
        {
            var manager = AssetDatabase.LoadAssetAtPath<AudioManager>(
                "Assets/_Project/Resources/Audio/Stage1AudioManager.asset");
            var bank = AssetDatabase.LoadAssetAtPath<Stage1SoundBank>(
                "Assets/_Project/Resources/Audio/Stage1SoundBank.asset");

            Assert.IsNotNull(manager, "Stage1AudioManager asset is missing or not imported.");
            Assert.AreSame(bank, manager.SoundBank);
            Assert.IsTrue(manager.HasAllEventSlots());
            Assert.IsTrue(manager.HasAllEventOverrides());
            Assert.AreEqual((int)SoundEventId.Count, manager.EventOverrides.Count);
            Assert.GreaterOrEqual(manager.MasterVolume, 0f);
            Assert.Greater(manager.SpatialMinDistance, 0f);
            Assert.GreaterOrEqual(manager.FadeInSeconds(SoundEventId.Footstep), 0f);
            Assert.GreaterOrEqual(manager.FadeOutSeconds(SoundEventId.Footstep), 0f);
            Assert.GreaterOrEqual(manager.NoiseVolumeScale(SoundEventId.Footstep), 0f);
            Assert.Greater(manager.NoisePitchScale(SoundEventId.Footstep), 0f);
            for (var i = 0; i < manager.EventOverrides.Count; i++)
            {
                var settings = manager.EventOverrides[i];
                Assert.IsNotNull(settings, "AudioManager override is null at index " + i);
                Assert.GreaterOrEqual(settings.fadeInScale, 0f);
                Assert.GreaterOrEqual(settings.fadeOutScale, 0f);
                Assert.GreaterOrEqual(settings.maxInstances, 1);
                Assert.GreaterOrEqual(settings.minRetriggerInterval, 0f);
            }

            Assert.IsNull(manager.Resolve(SoundEventId.PlayerJump, out _),
                "An empty future slot should remain silent.");

            var globalDiscovery = bank.GetSlot(SoundEventId.ExplorationFoundGlobal);
            var globalWeld = bank.GetSlot(SoundEventId.WeldAllCompleteGlobal);
            Assert.AreEqual(SoundRouting.Global2D, globalDiscovery.routing);
            Assert.AreEqual(SoundRouting.Global2D, globalWeld.routing);
        }
    }
}
