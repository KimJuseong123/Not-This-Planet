using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public class AuthoredMapSettingsTests
    {
        GameObject root;
        AuthoredMapSettings map;
        [SetUp] public void Setup() { root = new GameObject("MapTest"); map = root.AddComponent<AuthoredMapSettings>(); }
        [TearDown] public void Cleanup() => Object.DestroyImmediate(root);

        [Test] public void BoundsContainHillAndHangar()
        {
            Assert.IsTrue(map.Bounds.Contains(new Vector3(28,8.1f,19)));
            Assert.IsTrue(map.Bounds.Contains(new Vector3(-29,5.4f,25)));
        }
        [Test] public void RuntimeCameraGetsPostProcessingAndHdrWithoutDuplicateComponents()
        {
            var camera = root.AddComponent<Camera>();
            camera.allowHDR = false;
            map.ConfigureCamera(camera);
            map.ConfigureCamera(camera);
            var data = root.GetComponents<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            Assert.That(data.Length, Is.EqualTo(1));
            Assert.That(data[0].renderPostProcessing, Is.True);
            Assert.That(camera.allowHDR, Is.True);
        }
        [Test] public void BoundaryStopsOnlyOutwardMovement()
        {
            var velocity = new Vector3(3,2,-1);
            var position = map.ClampFlight(new Vector3(60,9,4), ref velocity);
            Assert.AreEqual(new Vector3(51,9,4), position);
            Assert.AreEqual(new Vector3(0,2,-1), velocity);
        }
        [Test] public void InteriorDoesNotChangeVelocity()
        {
            var velocity = new Vector3(2,3,4);
            Assert.AreEqual(new Vector3(28,9,19), map.ClampFlight(new Vector3(28,9,19), ref velocity));
            Assert.AreEqual(new Vector3(2,3,4), velocity);
        }
        [Test] public void OffsetMapUsesWorldBounds()
        {
            root.transform.position = new Vector3(100,0,100);
            var velocity = new Vector3(-1,0,0);
            Assert.AreEqual(49, map.ClampFlight(new Vector3(40,0,100), ref velocity).x);
        }
        [Test] public void LightingReadsDevelopAssemblyOrder()
        {
            var floors = new[] { ShipAssembly.FloorState.Welded, ShipAssembly.FloorState.Empty };
            Assert.AreEqual(1, AssemblyGuidanceLight.RequiredFloor(floors));
        }
        [Test] public void FinishedAssemblyUsesReadySignal()
        {
            Assert.AreEqual(-1, AssemblyGuidanceLight.RequiredFloor(new[] { ShipAssembly.FloorState.Welded }));
        }
    }
}
