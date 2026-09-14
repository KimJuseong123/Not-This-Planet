using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Metagame.Bootstrap;
using Metagame.Gameplay;
using Metagame.UI;

namespace Metagame.Gameplay.Tests
{
    public class CustomizationTests
    {
        [Test]
        public void EverySelectionSurvivesNetworkEncoding()
        {
            for (int tint = 0; tint < CustomizationData.TintNames.Length; tint++)
            for (int helmet = 0; helmet < 3; helmet++)
            for (int badge = 0; badge < 3; badge++)
            {
                var choice = new CustomizationData { Tint = tint, Helmet = helmet, Badge = badge };
                var restored = CustomizationData.Decode(choice.Packed);
                Assert.That(restored.Tint, Is.EqualTo(tint));
                Assert.That(restored.Helmet, Is.EqualTo(helmet));
                Assert.That(restored.Badge, Is.EqualTo(badge));
            }
        }

        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        public void InvalidNetworkDataIsBounded(int packed)
        {
            var choice = CustomizationData.Decode(packed);
            Assert.That(choice.Tint, Is.InRange(0, CustomizationData.TintNames.Length - 1));
            Assert.That(choice.Helmet, Is.InRange(0, 2));
            Assert.That(choice.Badge, Is.InRange(0, 2));
        }

        [Test]
        public void SuitTintPaletteCanBeConfiguredAndRemainsOpaque()
        {
            var original = new Color[CustomizationData.TintNames.Length];
            var configured = new Color[CustomizationData.TintNames.Length];
            for (int i = 0; i < original.Length; i++)
            {
                original[i] = CustomizationData.TintColor(i);
                configured[i] = new Color(i * 0.1f, i * 0.15f, i * 0.2f, 0.25f);
            }

            try
            {
                CustomizationData.ConfigureTintColors(configured);
                for (int i = 0; i < configured.Length; i++)
                {
                    configured[i].a = 1f;
                    Assert.That(CustomizationData.TintColor(i), Is.EqualTo(configured[i]));
                }
            }
            finally
            {
                CustomizationData.ConfigureTintColors(original);
            }
        }

        [Test]
        public void AccessoriesPreservePhysicsAndRespectHiddenBody()
        {
            var doll = RagdollBuilder.Build("appearance test", Vector3.zero, Quaternion.identity, Color.white, Color.cyan);
            try
            {
                int colliders = doll.Root.GetComponentsInChildren<Collider>(true).Length;
                int bodies = doll.Bodies.Count;
                float mass = doll.TotalMass;
                doll.SetVisible(false, true);
                var selection = new CustomizationData { Tint = 2, Helmet = 1, Badge = 2 };
                PlayerAppearance.Apply(doll, selection.Packed);
                Assert.That(doll.Root.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(colliders));
                Assert.That(doll.Bodies.Count, Is.EqualTo(bodies));
                Assert.That(doll.TotalMass, Is.EqualTo(mass));
                foreach (var renderer in doll.Root.GetComponentsInChildren<Renderer>())
                    Assert.That(renderer.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly));
                int rendererCount = doll.Root.GetComponentsInChildren<Renderer>().Length;
                PlayerAppearance.Apply(doll, selection.Packed);
                Assert.That(doll.Root.GetComponentsInChildren<Renderer>().Length, Is.EqualTo(rendererCount));
                doll.SetVisible(false);
                PlayerAppearance.Apply(doll, new CustomizationData { Helmet = 2 }.Packed);
                foreach (var renderer in doll.Root.GetComponentsInChildren<Renderer>()) Assert.That(renderer.enabled, Is.False);
                PlayerAppearance.Apply(doll, 0);
                foreach (var child in doll.Root.GetComponentsInChildren<Transform>(true))
                    Assert.That(child.name, Does.Not.StartWith("Customizer "));
            }
            finally { Object.DestroyImmediate(doll.Root.gameObject); }
        }

        [Test]
        public void TintDoesNotModifySharedMaterial()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Metagame.Core.Prim.DoughShader);
            try
            {
                material.SetColor("_Color", Color.white);
                var renderer = go.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                PlayerAppearance.Tint(new[] { renderer }, Color.cyan);
                Assert.That(material.GetColor("_Color"), Is.EqualTo(Color.white));
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, 0);
                Assert.That(block.GetColor("_Color"), Is.EqualTo(Color.cyan));
                PlayerAppearance.Tint(new[] { renderer }, Color.white);
                renderer.GetPropertyBlock(block, 0);
                Assert.That(block.GetColor("_Color"), Is.EqualTo(Color.white));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(material); }
        }

        [Test]
        public void TintLeavesColoredAccentMaterialUnchanged()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Metagame.Core.Prim.DoughShader);
            var accent = new Color(0.42f, 0.18f, 0.82f, 1f);
            try
            {
                material.SetColor("_Color", accent);
                var renderer = go.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                PlayerAppearance.Tint(new[] { renderer }, CustomizationData.TintColor(1));
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, 0);
                Assert.That(block.GetColor("_Color"), Is.EqualTo(accent));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(material); }
        }

        [Test]
        public void CustomizerSceneHasBootstrapAndBuildEntry()
        {
            const string path = "Assets/Scenes/Customizer.unity";
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null);
            Assert.That(System.Array.Exists(EditorBuildSettings.scenes, s => s.enabled && s.path == path), Is.True);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                Assert.That(scene.name, Is.EqualTo(CustomizerFlow.SceneName));
                Assert.That(System.Array.Exists(scene.GetRootGameObjects(), go => go.GetComponent<CustomizerBootstrap>() != null), Is.True);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
