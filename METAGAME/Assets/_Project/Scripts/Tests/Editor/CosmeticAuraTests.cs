using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.Gameplay.Tests
{
    public class CosmeticAuraTests
    {
        [Test]
        public void AuraSurvivesSaveAndReload()
        {
            const string key = "appearance.v2";
            bool existed = PlayerPrefs.HasKey(key);
            int original = PlayerPrefs.GetInt(key);
            try
            {
                for (int aura = 0; aura < 4; aura++)
                {
                    var selected = new CustomizationData { Tint = 4, Helmet = 6, Badge = 5, Aura = aura };
                    selected.Save();
                    Assert.That(CustomizationData.Load().Packed, Is.EqualTo(selected.Packed));
                }
            }
            finally
            {
                if (existed) PlayerPrefs.SetInt(key, original); else PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void ExistingSelectionsDefaultToNoAura()
        {
            for (int helmet = 0; helmet < 7; helmet++)
            for (int badge = 0; badge < 7; badge++)
            {
                var selection = new CustomizationData { Helmet = helmet, Badge = badge };
                Assert.That(CustomizationData.Decode(selection.Packed).Aura, Is.Zero);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void AuraIsBoundedAndHasNoGameplayComponents(int index)
        {
            var prefab = Resources.Load<GameObject>($"Cosmetics609/Aura{index:00}");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Collider>(), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Light>(), Is.Empty);
            int budget = 0;
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>())
            {
                budget += ps.main.maxParticles;
                Assert.That(ps.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.Local));
                Assert.That(ps.main.loop, Is.True);
                Assert.That(ps.collision.enabled, Is.False);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                Assert.That(r.sharedMaterial.shader.name, Is.EqualTo("NotThisPlanet/CharacterAura"));
                Assert.That(r.sharedMaterial.GetFloat("_Intensity"), Is.GreaterThan(1));
            }
            Assert.That(budget, Is.InRange(1, 64));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AuraHidesEvenWhenBodyKeepsShadowsAndCanBeRemoved(bool keepShadow)
        {
            var doll = RagdollBuilder.Build("aura test", Vector3.zero, Quaternion.identity, Color.white, Color.cyan, false);
            try
            {
                int colliderCount = doll.Root.GetComponentsInChildren<Collider>(true).Length;
                float mass = doll.TotalMass;
                for (int aura = 1; aura <= 3; aura++)
                {
                    doll.SetVisible(false, keepShadow);
                    var selection = new CustomizationData { Aura = aura, Helmet = 1, Badge = 1 };
                    PlayerAppearance.Apply(doll, selection.Packed);
                    var systems = doll.Root.GetComponentsInChildren<ParticleSystem>();
                    Assert.That(systems, Is.Not.Empty);
                    foreach (var ps in systems)
                    {
                        var r = ps.GetComponent<ParticleSystemRenderer>();
                        Assert.That(r.enabled, Is.False);
                        Assert.That(r.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                        Assert.That(ps.gameObject.layer, Is.EqualTo(doll.Chest.gameObject.layer));
                        ps.Simulate(3f, true, true);
                        Assert.That(ps.particleCount, Is.GreaterThan(0));
                    }
                    PlayerAppearance.Apply(doll, selection.Packed);
                    Assert.That(doll.Root.GetComponentsInChildren<ParticleSystem>().Length, Is.EqualTo(systems.Length));
                    doll.SetVisible(true);
                    foreach (var ps in systems) Assert.That(ps.GetComponent<Renderer>().enabled, Is.True);
                }
                PlayerAppearance.Apply(doll, 0);
                Assert.That(doll.Root.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
                Assert.That(doll.Root.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(colliderCount));
                Assert.That(doll.TotalMass, Is.EqualTo(mass));
            }
            finally { Object.DestroyImmediate(doll.Root.gameObject); }
        }
    }
}
