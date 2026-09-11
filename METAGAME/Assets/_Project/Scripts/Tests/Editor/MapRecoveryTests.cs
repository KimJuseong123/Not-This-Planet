using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class MapRecoveryTests
    {
        readonly List<GameObject> objects = new();
        static readonly Bounds Map = new Bounds(new Vector3(10f, 3f, -10f), new Vector3(50f, 6f, 50f));

        GameObject Create(string name)
        {
            var go = new GameObject(name);
            objects.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [TestCase(-18f, 0f, 0f, false)]
        [TestCase(-18.01f, 0f, 0f, true)]
        [TestCase(38.01f, 0f, 0f, true)]
        [TestCase(10f, 0f, -38.01f, true)]
        [TestCase(10f, 0f, 18.01f, true)]
        [TestCase(10f, -5f, 0f, false)]
        [TestCase(10f, -5.01f, 0f, true)]
        [TestCase(10f, 1000f, 0f, false)]
        [TestCase(float.NaN, 0f, 0f, true)]
        [TestCase(0f, float.NaN, 0f, true)]
        [TestCase(0f, 0f, float.PositiveInfinity, true)]
        public void BoundaryUsesWorldCoordinatesAndHasNoCeiling(float x, float y, float z, bool outside)
        {
            Assert.That(MapRecovery.IsOutside(new Vector3(x, y, z), Map, 3f, 5f), Is.EqualTo(outside));
        }

        [Test]
        public void ToolReturnsToOriginalPoseAndStopsWithoutCreatingDuplicate()
        {
            var toolObject = Create("tool");
            var body = toolObject.AddComponent<Rigidbody>();
            var tool = toolObject.AddComponent<PlasmaWeldingTool>();
            var initialPosition = new Vector3(3f, 2f, -2f);
            var initialRotation = Quaternion.Euler(10f, 40f, 15f);
            body.position = initialPosition;
            body.rotation = initialRotation;
            var recovery = Create("recovery").AddComponent<MapRecovery>();
            recovery.Configure(null, Map, null, new ToolBase[] { tool }, null);

            for (int i = 0; i < 2; i++)
            {
                body.position = new Vector3(100f, -20f, 0f);
                body.linearVelocity = Vector3.down * 30f;
                body.angularVelocity = Vector3.one;
                recovery.Tick();
                Assert.That(Vector3.Distance(body.position, initialPosition), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(body.rotation, initialRotation), Is.LessThan(0.01f));
                Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
                Assert.That(body.angularVelocity, Is.EqualTo(Vector3.zero));
                Assert.That(toolObject.GetComponent<PlasmaWeldingTool>(), Is.SameAs(tool));
            }
        }

        [Test]
        public void LostCoreRequestsOneSceneResetBeforeRecoveringTools()
        {
            var part = Create("core").AddComponent<SpaceshipPart>();
            part.transform.position = new Vector3(100f, 0f, 0f);
            var toolObject = Create("tool");
            var body = toolObject.AddComponent<Rigidbody>();
            var tool = toolObject.AddComponent<PlasmaWeldingTool>();
            var recovery = Create("recovery").AddComponent<MapRecovery>();
            recovery.Configure(null, Map, new[] { part }, new ToolBase[] { tool }, null);
            body.position = new Vector3(200f, 0f, 0f);
            int resets = 0;
            recovery.SceneResetRequested += () => resets++;

            recovery.Tick();
            recovery.Tick();

            Assert.That(resets, Is.EqualTo(1));
            Assert.That(recovery.ResetPending, Is.True);
            Assert.That(body.position.x, Is.EqualTo(200f));
        }
    }
}
