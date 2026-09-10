using System;
using System.Collections.Generic;
using System.IO;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Metagame.Gameplay.Tests
{
    public sealed class ConceptTractorRegressionTests
    {
        const float Step = .02f;
        readonly List<GameObject> objects = new();
        SimulationMode savedMode;

        [SetUp] public void Setup()
        {
            savedMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown] public void Cleanup()
        {
            foreach (var go in objects) if (go != null) Object.DestroyImmediate(go);
            objects.Clear();
            Physics.simulationMode = savedMode;
        }

        GameObject Cube(string name, Vector3 position, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.layer = layer; go.transform.position = position;
            objects.Add(go);
            return go;
        }

        LowGravityField Field(Vector3 position, out Rigidbody carrier)
        {
            var go = new GameObject("Tractor"); objects.Add(go);
            go.transform.position = position;
            carrier = go.AddComponent<Rigidbody>(); carrier.useGravity = false;
            var field = go.AddComponent<LowGravityField>();
            field.Configure(go.transform, carrier); field.SetActive(true);
            return field;
        }

        [Test]
        public void LinkedBottomAndUpperPart_AreNotLifted()
        {
            var bottom = SocketTestRig.Placed(objects, new Vector3(0, 2, 0));
            var upper = SocketTestRig.Placed(objects, new Vector3(0, 6, 0));
            SocketTestRig.TopSocketOf(bottom).Connect(SocketTestRig.BaseSocketOf(upper));
            bottom.GetComponent<Rigidbody>().useGravity = false;
            upper.GetComponent<Rigidbody>().useGravity = false;
            var field = Field(new Vector3(0, 10, 0), out _);
            Physics.SyncTransforms();
            for (int i = 0; i < 50; i++) { field.Tick(Step); Physics.Simulate(Step); }
            Assert.That(field.AffectedCount, Is.Zero);
            Assert.That(bottom.transform.position.y, Is.EqualTo(2).Within(.01f));
            Assert.That(upper.transform.position.y, Is.EqualTo(6).Within(.01f));
        }

        [TestCase(6.4f)]
        [TestCase(9.6f)]
        public void CargoStaysInsideCone_ThroughAccelerationTurnsAndStop(float speed)
        {
            var field = Field(new Vector3(0, 10, 0), out var carrier);
            var go = Cube("Cargo", new Vector3(-.8f, 6, 0), GameLayers.Part);
            var cargo = go.AddComponent<Rigidbody>(); cargo.mass = 700;
            Physics.SyncTransforms();
            float worst = 0;
            for (int i = 0; i < 600; i++)
            {
                var heading = i < 150 ? Vector3.right : i < 300 ? Vector3.forward
                    : i < 450 ? Vector3.left : Vector3.zero;
                carrier.linearVelocity = Vector3.MoveTowards(carrier.linearVelocity, heading * speed, 12 * Step);
                field.Tick(Step); Physics.Simulate(Step);
                var lag = Vector3.ProjectOnPlane(carrier.position - cargo.position, Vector3.up).magnitude;
                worst = Mathf.Max(worst, lag);
                Assert.That(field.Contains(cargo.worldCenterOfMass), Is.True, $"step={i} lag={lag:F3} speed={speed}");
            }
            TestContext.WriteLine($"speed={speed:F1}, max cargo lag={worst:F3}m");
            field.SetActive(false);
            float y = cargo.position.y;
            for (int i = 0; i < 50; i++) Physics.Simulate(Step);
            Assert.That(cargo.position.y, Is.LessThan(y - .5f), "Switching off must really release the cargo.");
        }

        [Test]
        public void AssemblyPad_ParksEngineButDoesNotFreezeManualTransport()
        {
            var ground = Cube("Pad", new Vector3(0, -.5f, 0), GameLayers.Ground);
            ground.transform.localScale = new Vector3(20, 1, 20);
            var part = SocketTestRig.Freed(objects, new Vector3(0, 1.88f, 0));
            var body = part.GetComponent<Rigidbody>();
            var hover = part.gameObject.AddComponent<PartGroundHover>();
            hover.ConfigureAssemblyPad(ground.transform);
            Physics.SyncTransforms();
            for (int i = 0; i < 200; i++) { hover.Tick(); Physics.Simulate(Step); }
            Assert.That(hover.IsParked, Is.True);
            Assert.That(part.State, Is.EqualTo(PartState.Placed));
            var field = Field(new Vector3(0, 8, 0), out _);
            float y = body.position.y;
            for (int i = 0; i < 100; i++) { hover.Tick(); field.Tick(Step); Physics.Simulate(Step); }
            Assert.That(field.AffectedCount, Is.Zero);
            Assert.That(body.position.y, Is.EqualTo(y).Within(.06f));
            Assert.That(body.isKinematic, Is.False);
            field.SetActive(false);
            body.position += Vector3.right * 6;
            Physics.SyncTransforms(); hover.Tick();
            Assert.That(hover.IsParked, Is.False, "Dragging the part away must make it transportable again.");
        }

        SpaceshipPart MakeFinal(out PartTractorActivator activator)
        {
            var part = SocketTestRig.CreatePart(objects, Vector3.zero);
            // Unit root makes the vehicle platform and the two-metre art share one scale.
            part.transform.localScale = Vector3.one;
            part.GetComponent<BoxCollider>().size = new Vector3(2, 2, 2);
            SocketTestRig.BaseSocketOf(part).transform.localPosition = Vector3.down;
            SocketTestRig.TopSocketOf(part).transform.localPosition = Vector3.up;
            var visual = Cube("Visual", Vector3.zero, GameLayers.Part);
            visual.transform.SetParent(part.transform, false);
            visual.transform.localScale = Vector3.one * 2;
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            activator = part.gameObject.AddComponent<PartTractorActivator>();
            activator.ConfigureFinalModule(true);
            activator.Initialize();
            part.TryTransitionTo(PartState.Free);
            return part;
        }

        [Test]
        public void SameTractorBecomesFinalPart_WithoutMovingItsVisualOrAutoWelding()
        {
            var target = SocketTestRig.Placed(objects, new Vector3(0, 4, 0));
            var part = MakeFinal(out var activator);
            Assert.That(activator.IsActivated, Is.True);
            var visual = part.transform.Find("Visual");
            float desiredVisualY = SocketTestRig.TopSocketOf(target).transform.position.y + 1 + .05f;
            var body = part.GetComponent<Rigidbody>();
            part.transform.position += Vector3.up * (desiredVisualY - visual.position.y);
            body.position = part.transform.position;
            Physics.SyncTransforms();
            var artPosition = visual.position;
            Assert.That(activator.TryDock(), Is.True);
            Assert.That(Vector3.Distance(artPosition, visual.position), Is.LessThan(.001f));
            Assert.That(part.State, Is.EqualTo(PartState.Placed));
            Assert.That(part.enabled, Is.True);
            Assert.That(part.GetComponent<BoxCollider>().enabled, Is.True);
            Assert.That(SocketTestRig.BaseSocketOf(part).ConnectedTo, Is.SameAs(SocketTestRig.TopSocketOf(target)));
            Assert.That(activator.Vehicle.enabled, Is.False);
            var placedPosition = part.transform.position;
            activator.Vehicle.ForceReset();
            Assert.That(part.transform.position, Is.EqualTo(placedPosition));
            Assert.That(activator.TryDock(), Is.False);
        }

        [Test]
        public void DockingAwayFromTower_DoesNotAlterVehiclePose()
        {
            var part = MakeFinal(out var activator);
            var visual = part.transform.Find("Visual");
            var rootPose = part.transform.position;
            var artPose = visual.position;
            var rotation = part.transform.rotation;
            Assert.That(activator.TryDock(), Is.False);
            Assert.That(part.transform.position, Is.EqualTo(rootPose));
            Assert.That(visual.position, Is.EqualTo(artPose));
            Assert.That(Quaternion.Angle(rotation, part.transform.rotation), Is.LessThan(.001f));
            Assert.That(activator.Vehicle.enabled, Is.True);
        }
    }

    public sealed class ConceptTractorTestRunner : ICallbacks
    {
        static TestRunnerApi api;
        [MenuItem("Tools/NOT THIS PLANET/컨셉 맵/견인 회귀 테스트")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new ConceptTractorTestRunner());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, testNames = new[] {
                "Metagame.Gameplay.Tests.ConceptTractorRegressionTests",
                "Metagame.Gameplay.Tests.LowGravityFieldTests",
                "Metagame.Gameplay.Tests.PartTractorActivatorTests",
                "Metagame.Gameplay.Tests.PartPushTests",
                "Metagame.Gameplay.Tests.PartAssemblerTests" } }));
        }
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            TestRunnerApi.SaveResultToFile(result, Path.GetFullPath("../concept-playtest/tractor-unit.xml"));
            File.WriteAllText(Path.GetFullPath("../concept-playtest/tractor-unit-summary.txt"),
                $"{DateTime.Now:s}\n{result.TestStatus}: {result.PassCount} passed, {result.FailCount} failed\n{result.Message}");
            api.UnregisterCallbacks(this);
            Object.DestroyImmediate(api);
        }
    }
}
