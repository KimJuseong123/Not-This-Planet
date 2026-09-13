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

        [Test]
        public void FirstPersonStopsBeforeWorldSurface()
        {
            var target = new GameObject("camera target");
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var camera = OrbitCamera.Create(target.transform, Color.black, true);
            try
            {
                camera.TargetHeight = 0f;
                camera.FirstPersonForward = 0.20f;
                camera.FirstPersonCollisionRadius = 0.08f;
                camera.FirstPersonCollisionPadding = 0.015f;

                blocker.name = "spaceship part surface";
                blocker.layer = GameLayers.Part;
                blocker.transform.position = new Vector3(0f, 0f, 0.17f);
                blocker.transform.localScale = new Vector3(2f, 2f, 0.10f);
                Physics.SyncTransforms();

                camera.Tick();

                Assert.That(camera.transform.position.z, Is.LessThan(0.12f),
                    "1인칭 카메라가 파츠의 가까운 표면을 넘어가면 내부가 보인다.");
                Assert.That(camera.transform.position.z, Is.GreaterThanOrEqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(blocker);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void FirstPersonKeepsForwardOffsetWhenPathIsClear()
        {
            var target = new GameObject("camera target");
            var camera = OrbitCamera.Create(target.transform, Color.black, true);
            try
            {
                camera.TargetHeight = 0f;
                camera.FirstPersonForward = 0.20f;

                camera.Tick();

                Assert.That(
                    Vector3.Distance(camera.transform.position, new Vector3(0f, 0f, 0.20f)),
                    Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(target);
            }
        }
    }
}
