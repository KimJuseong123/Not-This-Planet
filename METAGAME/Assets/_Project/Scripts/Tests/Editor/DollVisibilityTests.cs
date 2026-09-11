using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.Gameplay.Tests
{
    public sealed class DollVisibilityTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ViewTransitionsPreserveSelectedAppearanceAndPhysics(bool primitiveLook)
        {
            if (!primitiveLook)
                Assert.That(SpaceSuit.Available, Is.True, "The player model must be available.");

            var doll = RagdollBuilder.Build("Visibility test", Vector3.zero,
                Quaternion.identity, Color.white, Color.gray, primitiveLook);
            try
            {
                var renderers = doll.Root.GetComponentsInChildren<Renderer>(true);
                var presentation = renderers.Where(renderer => renderer.enabled).ToArray();
                var hidden = renderers.Where(renderer => !renderer.enabled).ToArray();
                Assert.That(presentation, Is.Not.Empty);
                if (!primitiveLook) Assert.That(hidden, Is.Not.Empty);

                for (var cycle = 0; cycle < 3; cycle++)
                {
                    // First person -> downed/dead third person -> riding -> restored.
                    CheckVisibility(false, true);
                    CheckVisibility(true, false);
                    CheckVisibility(false, false);
                    CheckVisibility(true, false);
                }

                void CheckVisibility(bool visible, bool keepShadow)
                {
                    doll.SetVisible(visible, keepShadow);
                    Assert.That(doll.Visible, Is.EqualTo(visible));
                    foreach (var renderer in hidden)
                        Assert.That(renderer.enabled, Is.False, renderer.name);
                    foreach (var renderer in presentation)
                    {
                        Assert.That(renderer.enabled, Is.EqualTo(visible || keepShadow), renderer.name);
                        Assert.That(renderer.shadowCastingMode,
                            Is.EqualTo(visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly));
                    }
                    foreach (var collider in doll.Colliders)
                        Assert.That(collider.enabled, Is.True, collider.name);
                    Assert.That(doll.Balance, Is.Not.Null);
                    Assert.That(doll.Root.gameObject.activeSelf, Is.True);
                }
            }
            finally
            {
                Object.DestroyImmediate(doll.Root.gameObject);
            }
        }
    }
}
