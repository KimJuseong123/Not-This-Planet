using Metagame.Network;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class AiFlightCorridorTests
    {
        [Test] public void OpenRouteEndsAtRequestedDestination()
        {
            var end = new Vector3(20, 3, 0);
            var route = AiFlightCorridor.Find(Vector3.zero, end, 2, (a, b) => true);
            Assert.That(route, Is.Not.Null);
            Assert.That(route[route.Length - 1], Is.EqualTo(end));
        }
        [Test] public void CompletelyBlockedRouteFailsClosed()
            => Assert.That(AiFlightCorridor.Find(Vector3.zero, Vector3.right * 20, 2, (a, b) => false), Is.Null);
        [Test] public void WallRequiresSideCorridor()
        {
            bool Clear(Vector3 a, Vector3 b)
                => !((a.x < 10 && b.x >= 10 || b.x < 10 && a.x >= 10)
                    && Mathf.Abs(a.z) < 5 && Mathf.Abs(b.z) < 5);
            var route = AiFlightCorridor.Find(Vector3.zero, Vector3.right * 20, 2, Clear);
            Assert.That(route, Is.Not.Null);
            var previous = Vector3.zero;
            foreach (var point in route) { Assert.That(Clear(previous, point), Is.True); previous = point; }
            Assert.That(System.Array.Exists(route, p => Mathf.Abs(p.z) >= 5), Is.True);
        }
        [Test] public void LowObstacleCanUseHigherCorridor()
        {
            bool Clear(Vector3 a, Vector3 b) => Mathf.Abs(a.x - b.x) < .1f || Mathf.Min(a.y, b.y) >= 4;
            var route = AiFlightCorridor.Find(Vector3.zero, Vector3.right * 20, 2, Clear);
            Assert.That(route, Is.Not.Null);
            Assert.That(System.Array.Exists(route, p => p.y >= 4), Is.True);
        }
        [TestCase(0f, 0f, 0f, 0f, true)]
        [TestCase(1f, 0f, 0f, 0f, false)]
        [TestCase(-1f, 0f, 0f, 0f, false)]
        [TestCase(0f, 2f, 0f, 0f, false)]
        [TestCase(0f, 0f, 2f, 0f, false)]
        [TestCase(0f, 0f, 0f, 2f, false)]
        public void LandingNeedsPositionAndMotionToSettle(float gap, float offset, float speed, float spin, bool expected)
            => Assert.That(AiFlightCorridor.Settled(gap, offset, speed, spin), Is.EqualTo(expected));
    }
}
