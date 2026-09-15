using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public class PartGuidanceVfxTests
    {
        GameObject target, root, beaconObject;
        Renderer beacon;
        Material route;
        PartGuidanceVfx marker;
        SpaceshipPart part;

        [SetUp]
        public void SetUp()
        {
            target = new GameObject(PartSpec.All[0].Name);
            part = target.AddComponent<SpaceshipPart>();
            part.Initialize();
            route = new Material(Resources.Load<Shader>("GuidanceNoFog"));
            route.SetColor("_BaseColor", Color.cyan);
            beaconObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(beaconObject.GetComponent<Collider>());
            beaconObject.transform.position = new Vector3(12, 8, -9);
            beaconObject.transform.localScale = new Vector3(.09f, 13, .09f);
            beacon = beaconObject.GetComponent<Renderer>();
            beacon.sharedMaterial = route;
            root = new GameObject("Guidance");
            marker = root.AddComponent<PartGuidanceVfx>();
            marker.Configure(beacon);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(beaconObject);
            Object.DestroyImmediate(route);
        }

        [Test]
        public void UsesExistingBeaconExtentWithoutChangingPartPhysicsOrSharedMaterial()
        {
            var body = target.GetComponent<Rigidbody>();
            bool kinematic = body.isKinematic;
            float mass = body.mass;
            int layer = target.layer;
            target.transform.position = new Vector3(12, 3, -8);
            marker.Refresh(1);
            var halo = root.transform.Find("Plasma glow").GetComponent<LineRenderer>();
            Assert.That(halo.GetPosition(0), Is.EqualTo(new Vector3(12, 1.5f, -9)));
            Assert.That(halo.GetPosition(1), Is.EqualTo(new Vector3(12, 14.5f, -9)));
            Assert.That(beacon.sharedMaterial.GetFloat("_MaxBrightness"), Is.GreaterThan(1));
            Assert.That(route.GetFloat("_MaxBrightness"), Is.EqualTo(1));
            Assert.That(body.isKinematic, Is.EqualTo(kinematic));
            Assert.That(body.mass, Is.EqualTo(mass));
            Assert.That(target.layer, Is.EqualTo(layer));
            Assert.That(part.State, Is.EqualTo(PartState.Locked));
            Assert.That(target.GetComponentsInChildren<Renderer>(), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Collider>(), Is.Empty);
        }

        [Test]
        public void PartMovementDockingAndDestructionLeaveBeaconVisibleAndStationary()
        {
            var halo = root.transform.Find("Plasma glow").GetComponent<LineRenderer>();
            var origin = halo.GetPosition(0);
            target.transform.position = new Vector3(50, 20, 70);
            part.ApplyDockedToPad(true);
            marker.Refresh(0);
            Assert.That(halo.GetPosition(0), Is.EqualTo(origin));
            foreach (var line in root.GetComponentsInChildren<LineRenderer>()) Assert.IsTrue(line.enabled);
            Object.DestroyImmediate(target);
            marker.Refresh(0);
            Assert.That(halo.GetPosition(0), Is.EqualTo(origin));
            foreach (var line in root.GetComponentsInChildren<LineRenderer>()) Assert.IsTrue(line.enabled);
        }

        [Test]
        public void DisabledOrDestroyedBeaconHidesAdditionalOptics()
        {
            beaconObject.SetActive(false);
            marker.Refresh(0);
            foreach (var line in root.GetComponentsInChildren<LineRenderer>()) Assert.IsFalse(line.enabled);
            beaconObject.SetActive(true);
            marker.Refresh(0);
            foreach (var line in root.GetComponentsInChildren<LineRenderer>()) Assert.IsTrue(line.enabled);
            Object.DestroyImmediate(beaconObject);
            marker.Refresh(0);
            foreach (var line in root.GetComponentsInChildren<LineRenderer>()) Assert.IsFalse(line.enabled);
        }

        [Test]
        public void RepeatedConfigureDoesNotDuplicateEffectsAndDisableHidesThem()
        {
            marker.Configure(beacon);
            Assert.That(root.GetComponentsInChildren<LineRenderer>().Length, Is.EqualTo(2));
            marker.enabled = false;
            // Non-ExecuteAlways components do not receive lifecycle callbacks in EditMode.
            typeof(PartGuidanceVfx).GetMethod("OnDisable",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(marker, null);
            foreach (var line in root.GetComponentsInChildren<LineRenderer>()) Assert.IsFalse(line.enabled);
            Assert.That(beacon.sharedMaterial, Is.SameAs(route));
            marker.enabled = true;
            typeof(PartGuidanceVfx).GetMethod("OnEnable",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(marker, null);
            marker.Refresh(0);
            Assert.That(beacon.sharedMaterial, Is.Not.SameAs(route));
            foreach (var line in root.GetComponentsInChildren<LineRenderer>()) Assert.IsTrue(line.enabled);
        }
    }
}
