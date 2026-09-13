using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public class DebugPerspectiveTests
    {
        [Test]
        public void ToggleRoundTripOnlyChangesThisCamera()
        {
            var target = new GameObject("camera target");
            var mine = OrbitCamera.Create(target.transform, Color.black, true);
            var other = OrbitCamera.Create(target.transform, Color.black, true);
            try
            {
                mine.Tick();
                var firstPersonPosition = mine.transform.position;
                mine.ToggleDebugPerspective();
                Assert.That(mine.FirstPerson, Is.False);
                Assert.That(other.FirstPerson, Is.True);
                Assert.That(Vector3.Distance(mine.transform.position, firstPersonPosition), Is.GreaterThan(1f));
                mine.ToggleDebugPerspective();
                Assert.That(mine.FirstPerson, Is.True);
                Assert.That(Vector3.Distance(mine.transform.position, firstPersonPosition), Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(mine.gameObject);
                Object.DestroyImmediate(other.gameObject);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ForcedCameraRestoresSelectedPerspective()
        {
            var target = new GameObject("camera target");
            var camera = OrbitCamera.Create(target.transform, Color.black, true);
            try
            {
                camera.ToggleDebugPerspective();
                camera.SetVehicleView(true);
                camera.SetVehicleView(false);
                Assert.That(camera.FirstPerson, Is.False);
                camera.SetDownedView(true, target.transform);
                camera.ToggleDebugPerspective();
                camera.SetDownedView(false);
                Assert.That(camera.FirstPerson, Is.True);
            }
            finally { Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(target); }
        }
    }
}
