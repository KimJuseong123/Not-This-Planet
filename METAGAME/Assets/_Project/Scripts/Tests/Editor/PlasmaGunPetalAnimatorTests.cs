using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public class PlasmaGunPetalAnimatorTests
    {
        const string PrefabPath =
            "Assets/_Project/Prefabs/Props/Stage1/PF_Stage1_WeldingPlasmaGun.prefab";

        GameObject root;
        PlasmaGunPetalAnimator animator;
        readonly Transform[] hinges = new Transform[5];

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("PetalVisual");
            var positions = new[]
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, 0.31f, 0.95f),
                new Vector3(0f, -0.81f, 0.59f),
                new Vector3(0f, -0.81f, -0.59f),
                new Vector3(0f, 0.31f, -0.95f),
            };

            for (var i = 0; i < hinges.Length; i++)
            {
                hinges[i] = new GameObject("Hinge_" + (i + 1).ToString("00")).transform;
                hinges[i].SetParent(root.transform, false);
                hinges[i].localPosition = positions[i];
            }

            animator = root.AddComponent<PlasmaGunPetalAnimator>();
            animator.Rebind();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void FindsFiveHingesAndCreatesDifferentWeldAndCutPoses()
        {
            animator.SnapToMode(PlasmaBeamMode.Weld);
            var weldRotation = hinges[0].localRotation;

            animator.SnapToMode(PlasmaBeamMode.Cut);
            var cutRotation = hinges[0].localRotation;

            Assert.AreEqual(5, animator.HingeCount);
            Assert.That(Quaternion.Angle(weldRotation, cutRotation), Is.GreaterThan(80f));
        }

        [Test]
        public void WaitsForMechanicalTransitionBeforeReportingReady()
        {
            animator.SnapToMode(PlasmaBeamMode.None);

            Assert.IsFalse(animator.TickAndIsReady(PlasmaBeamMode.Weld, 0.01f));
            Assert.IsTrue(animator.TickAndIsReady(PlasmaBeamMode.Weld, 1f));
            Assert.That(animator.CurrentAngle, Is.EqualTo(50f).Within(0.01f));
        }

        [Test]
        public void ProductionPrefabContainsFiveHingesAndTexturedModel()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);

            var instance = Object.Instantiate(prefab);
            try
            {
                var productionAnimator = instance.GetComponentInChildren<PlasmaGunPetalAnimator>(true);
                Assert.IsNotNull(productionAnimator);
                productionAnimator.Rebind();
                Assert.AreEqual(5, productionAnimator.HingeCount);

                var replacement = instance.transform.Find("AttachPoints/ReplacementModel");
                Assert.IsNotNull(replacement);
                var renderers = replacement.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThanOrEqualTo(31));

                var texturedMaterialCount = 0;
                foreach (var renderer in renderers)
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        Assert.That(material.shader.name,
                            Is.EqualTo("Standard").Or.EqualTo("Universal Render Pipeline/Lit"),
                            material.name + " must use a supported project render-pipeline shader.");
                        if (material.mainTexture != null) texturedMaterialCount++;
                    }
                Assert.That(texturedMaterialCount, Is.GreaterThan(0));

                var modelBounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                    modelBounds.Encapsulate(renderers[i].bounds);

                var hingeCenter = Vector3.zero;
                var hingeCount = 0;
                foreach (var child in replacement.GetComponentsInChildren<Transform>(true))
                {
                    if (!child.name.StartsWith("Hinge_")) continue;
                    hingeCenter += child.position;
                    hingeCount++;
                }
                Assert.AreEqual(5, hingeCount);
                hingeCenter /= hingeCount;
                Assert.That(Vector3.Distance(hingeCenter, modelBounds.center),
                    Is.LessThan(modelBounds.size.magnitude),
                    "The five petal hinges must remain within the imported model bounds.");

                var muzzle = instance.transform.Find("AttachPoints/Muzzle");
                Assert.IsNotNull(muzzle);
                Assert.That(Mathf.Abs(muzzle.position.z - modelBounds.max.z), Is.LessThan(0.15f));

                var placeholder = instance.transform.Find("PlaceholderVisual");
                Assert.IsNotNull(placeholder);
                foreach (var renderer in placeholder.GetComponentsInChildren<Renderer>(true))
                    Assert.IsFalse(renderer.enabled);
                Assert.That(placeholder.GetComponentsInChildren<Collider>(true).Length,
                            Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
