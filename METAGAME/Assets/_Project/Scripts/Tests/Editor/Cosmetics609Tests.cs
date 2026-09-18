using NUnit.Framework;
using UnityEngine;
using Metagame.Gameplay;

namespace Metagame.Gameplay.Tests
{
    public class Cosmetics609Tests
    {
        [Test]
        public void OldPackedSelectionsKeepTheirMeaning()
        {
            for (int tint = 0; tint < 5; tint++)
            for (int helmet = 0; helmet < 3; helmet++)
            for (int badge = 0; badge < 3; badge++)
            {
                int packed = tint | (helmet << 3) | (badge << 5);
                var data = CustomizationData.Decode(packed);
                Assert.That(data.Tint, Is.EqualTo(tint));
                Assert.That(data.Helmet, Is.EqualTo(helmet));
                Assert.That(data.Badge, Is.EqualTo(badge));
                Assert.That(data.Packed, Is.EqualTo(packed));
            }
        }

        [TestCase("Helmet", 7)]
        [TestCase("Badge", 7)]
        public void EveryCosmeticLoadsWithTextureAndCorrectScale(string slot, int count)
        {
            for (int i = 1; i < count; i++)
            {
                var prefab = Resources.Load<GameObject>($"Cosmetics609/{slot}{i:00}");
                Assert.That(prefab, Is.Not.Null, $"{slot}{i}");
                var instance = Object.Instantiate(prefab);
                try
                {
                    Assert.That(instance.GetComponentsInChildren<Collider>(), Is.Empty);
                    Assert.That(instance.GetComponentsInChildren<Rigidbody>(), Is.Empty);
                    var renderers = instance.GetComponentsInChildren<Renderer>();
                    Assert.That(renderers.Length, Is.EqualTo(1));
                    var r = renderers[0];
                    Assert.That(r.sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null);
                    Assert.That(r.bounds.size.magnitude, Is.InRange(.08f, 1f));
                    if (slot == "Badge") Assert.That(r.bounds.center.z, Is.GreaterThan(.20f));
                    else Assert.That(r.bounds.center.z, Is.InRange(-.2f, .3f));
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void EveryNewSelectionKeepsPhysicsAndVisibility(bool primitiveLook)
        {
            var doll = RagdollBuilder.Build("cosmetics609", Vector3.zero, Quaternion.identity, Color.white, Color.cyan, primitiveLook);
            try
            {
                int colliders = doll.Root.GetComponentsInChildren<Collider>(true).Length;
                float mass = doll.TotalMass;
                for (int i = 1; i < 7; i++)
                {
                    doll.SetVisible(false);
                    PlayerAppearance.Apply(doll, new CustomizationData { Helmet = i, Badge = i }.Packed);
                    Assert.That(doll.Root.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(colliders));
                    Assert.That(doll.TotalMass, Is.EqualTo(mass));
                    foreach (var renderer in doll.Root.GetComponentsInChildren<Renderer>())
                        Assert.That(renderer.enabled, Is.False);
                }
            }
            finally { Object.DestroyImmediate(doll.Root.gameObject); }
        }
    }
}
