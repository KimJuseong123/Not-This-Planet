using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage1PlayAreaTests
    {
        readonly List<GameObject> created = new();

        GameObject Make(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in created) if (go != null) Object.DestroyImmediate(go);
            created.Clear();
        }

        [Test]
        public void FlightBoundsStopOutwardVelocityButPreserveAltitude()
        {
            var area = Make("area").AddComponent<Stage1PlayArea>();
            area.transform.position = new Vector3(1000, 0, 1000);
            var velocity = new Vector3(3, 2, -4);
            var p = area.ClampFlight(new Vector3(1060, 12, 940), ref velocity);
            Assert.AreEqual(new Vector3(1051, 12, 953), p);
            Assert.AreEqual(new Vector3(0, 2, 0), velocity);
        }

        [Test]
        public void FlightBoundsAllowMovementBackInside()
        {
            var area = Make("area").AddComponent<Stage1PlayArea>();
            var velocity = new Vector3(-3, 0, 4);
            area.ClampFlight(new Vector3(60, 12, -60), ref velocity);
            Assert.AreEqual(new Vector3(-3, 0, 4), velocity);
            Assert.AreEqual(20, area.MaximumAltitude);
        }

        [Test]
        public void VehicleFloorUsesElevatedGroundNotRoofAbove()
        {
            var area = Make("area").AddComponent<Stage1PlayArea>();
            var floor = Make("elevated floor");
            floor.layer = GameLayers.Ground;
            floor.transform.position = new Vector3(1000, 7.5f, 1000);
            floor.AddComponent<BoxCollider>().size = new Vector3(10, 1, 10);
            var roof = Make("roof");
            roof.layer = GameLayers.Ground;
            roof.transform.position = new Vector3(1000, 12, 1000);
            roof.AddComponent<BoxCollider>().size = new Vector3(10, 1, 10);
            Physics.SyncTransforms();
            Assert.AreEqual(8 + TransportVehicle.GroundY,
                area.VehicleFloor(new Vector3(1000, 8.2f, 1000)), .001f);
        }
    }
}
