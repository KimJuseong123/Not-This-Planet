using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class AssemblyDockTests
    {
        private readonly List<GameObject> _objects = new();
        private SimulationMode _simulation;

        [SetUp]
        public void SetUp()
        {
            _simulation = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
            WorldEvents.RemoteDecides = false;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
            UnityEngine.Physics.simulationMode = _simulation;
            WorldEvents.RemoteDecides = false;
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        private SpaceshipPart Part(string name, Vector3 position, float height = 2f)
        {
            var go = Make(name);
            go.transform.position = position;
            go.AddComponent<BoxCollider>().size = new Vector3(1f, height, 1f);
            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            part.TryTransitionTo(PartState.Free);
            UnityEngine.Physics.SyncTransforms();
            return part;
        }

        private AssemblyDock Pad()
        {
            var go = Make("Pad");
            var assembly = go.AddComponent<ShipAssembly>();
            assembly.Configure(go.transform);
            var dock = go.AddComponent<AssemblyDock>();
            dock.Configure(assembly);
            return dock;
        }

        [Test]
        public void FirstPartSnapsAndRemainsKinematicAfterMassAndAuthorityRefresh()
        {
            var part = Part("Part_1", new Vector3(0.4f, PartSpec.All[0].Height / 2f, 0f), PartSpec.All[0].Height);
            var dock = Pad();
            Assert.That(dock.CanCapture(part), Is.True);
            dock.Tick(0.02f);
            for (var i = 0; i < 25; i++) { dock.Tick(0.02f); UnityEngine.Physics.Simulate(0.02f); }
            Assert.That(dock.DockedPart, Is.EqualTo(part));
            Assert.That(part.transform.position.x, Is.EqualTo(0f).Within(0.02f));
            Assert.That(part.State, Is.EqualTo(PartState.Placed));
            part.SetExtraMass(5f);
            part.RefreshRepresentation();
            Assert.That(part.GetComponent<Rigidbody>().isKinematic, Is.True);
            Assert.That(part.TryTransitionTo(PartState.Carried), Is.False);
        }

        [Test]
        public void SnapUsesActualHullHeightAndPivotInsteadOfNominalPartSpec()
        {
            var part = Part("Part_1", new Vector3(0.3f, 0.8f, 0f), 1.2f);
            part.GetComponent<BoxCollider>().center = Vector3.up * -0.2f;
            UnityEngine.Physics.SyncTransforms();
            var dock = Pad();
            Assert.That(dock.CanCapture(part), Is.True);
            dock.Tick(0.02f);
            dock.Tick(1f);
            UnityEngine.Physics.SyncTransforms();
            Assert.That(part.TryGetHullBounds(out var bounds), Is.True);
            Assert.That(bounds.min.y, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void LegacyTractorPartIsNotPermanentlyDockedEvenWhenUnoccupied()
        {
            var part = Part("Part_1", Vector3.up);
            part.gameObject.AddComponent<TransportVehicle>();
            Assert.That(Pad().CanCapture(part), Is.False);
        }

        [Test]
        public void DockRejectsWrongPartAirborneTiltedAndExplicitlyCarriedParts()
        {
            var part = Part("Part_1", Vector3.up);
            var dock = Pad();
            var wrong = Part("Part_2", Vector3.up);
            Assert.That(dock.CanCapture(wrong), Is.False);
            part.transform.position = Vector3.up * 3;
            UnityEngine.Physics.SyncTransforms();
            Assert.That(dock.CanCapture(part), Is.False);
            part.transform.SetPositionAndRotation(Vector3.up, Quaternion.Euler(45, 0, 0));
            UnityEngine.Physics.SyncTransforms();
            Assert.That(dock.CanCapture(part), Is.False);
            part.transform.rotation = Quaternion.identity;
            part.TryTransitionTo(PartState.Carried);
            UnityEngine.Physics.SyncTransforms();
            Assert.That(dock.CanCapture(part), Is.False);
        }

        [Test]
        public void ClientNeverCapturesAndRestoredDockSurvivesAuthorityHandover()
        {
            var part = Part("Part_1", Vector3.up);
            var dock = Pad();
            WorldEvents.RemoteDecides = true;
            dock.Tick(1f);
            Assert.That(part.IsDockedToPad, Is.False);
            part.ApplyDockedToPad(true);
            WorldEvents.RemoteDecides = false;
            part.RefreshRepresentation();
            Assert.That(part.GetComponent<Rigidbody>().isKinematic, Is.True);
        }

        [Test]
        public void DockDoesNotChangeUpperPartStateOrMotion()
        {
            var bottom = Part("Part_1", Vector3.up);
            bottom.ApplyDockedToPad(true);
            var middle = Part("Part_2", Vector3.up * 3f);
            var top = Part("Part_3", Vector3.up * 5f);
            var dock = Pad();
            dock.Tick(1f);
            Assert.That(middle.IsDockedToPad, Is.False);
            Assert.That(top.IsDockedToPad, Is.False);
            Assert.That(middle.State, Is.EqualTo(PartState.Free));
            Assert.That(top.State, Is.EqualTo(PartState.Free));
            Assert.That(middle.GetComponent<Rigidbody>().isKinematic, Is.False);
            Assert.That(top.GetComponent<Rigidbody>().isKinematic, Is.False);
            Assert.That(middle.transform.position, Is.EqualTo(Vector3.up * 3f));
            Assert.That(top.transform.position, Is.EqualTo(Vector3.up * 5f));
        }

        [Test]
        public void BeamApiCannotGrabDockedBaseButLooseCargoRemainsGrabbable()
        {
            var bottom = Part("Part_1", Vector3.up);
            bottom.ApplyDockedToPad(true);
            var top = Part("Part_2", Vector3.up * 3f);
            var beam = Make("Beam").AddComponent<TractorBeam>();
            beam.transform.SetPositionAndRotation(Vector3.up * 8f, Quaternion.LookRotation(Vector3.down));
            beam.Configure(beam.transform, 12f, 3f);
            UnityEngine.Physics.SyncTransforms();
            Assert.That(beam.TryGrab(bottom), Is.EqualTo(BeamGrabResult.NoTarget));
            Assert.That(beam.TryGrab(top), Is.EqualTo(BeamGrabResult.Grabbed));
            beam.Release();
            Assert.That(top.IsDockedToPad, Is.False);
        }

        [Test]
        public void DockDoesNotFilterWeldedUpperPartsOutOfExistingField()
        {
            var bottom = Part("Part_1", Vector3.up);
            bottom.ApplyDockedToPad(true);
            var top = Part("Part_2", Vector3.up * 3f);
            var cargo = Part("Loose", new Vector3(1.8f, 1f, 0f));
            var field = Make("Field").AddComponent<LowGravityField>();
            field.transform.position = Vector3.up * 8f;
            field.Configure(field.transform);
            field.SetPhysicsAuthority(true);
            field.SetActive(true);
            UnityEngine.Physics.SyncTransforms();
            field.Tick(0.02f);
            Assert.That(field.AffectedCount, Is.EqualTo(2), "얹어둔 상부와 별도 화물은 모두 저중력장 대상이다.");
            Assert.That(bottom.GetComponent<Rigidbody>().isKinematic, Is.True);

            var joint = top.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = bottom.GetComponent<Rigidbody>();
            var link = top.gameObject.AddComponent<PartAssemblyLink>();
            link.BindBeadWeld(top, bottom, joint);
            link.SetBeadWeldRatio(1f);
            field.Tick(0.02f);
            Assert.That(field.AffectedCount, Is.EqualTo(2), "용접된 상부도 기존 저중력 계산을 그대로 받는다.");
            Assert.That(top.IsDockedToPad, Is.False);
            Assert.That(top.GetComponent<Rigidbody>().isKinematic, Is.False,
                "상부를 강제로 동결하지 않고 기존 용접 조인트가 잡아준다.");

            link.Release();
            field.Tick(0.02f);
            Assert.That(field.AffectedCount, Is.EqualTo(2), "절단 전후 대상 판정에 새 예외 규칙이 없다.");
        }
    }
}
