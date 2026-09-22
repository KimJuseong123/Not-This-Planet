using System;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>Bounded ascent / cruise / descent corridors. Every segment must be clear.</summary>
    public static class AiFlightCorridor
    {
        public static Vector3[] Find(Vector3 start, Vector3 end, float clearance,
            Func<Vector3, Vector3, bool> clear)
        {
            var delta = end - start; delta.y = 0;
            var side = Vector3.Cross(Vector3.up, delta.normalized);
            if (side.sqrMagnitude < .1f) side = Vector3.right;
            Vector3[] best = null; float bestLength = float.PositiveInfinity;
            for (int level = 0; level < 4; level++)
            for (int lane = -2; lane <= 2; lane++)
            {
                float height = Mathf.Max(start.y, end.y) + level * Mathf.Max(4f, clearance * 2f);
                var a = new Vector3(start.x, height, start.z);
                var b = new Vector3(end.x, height, end.z);
                var offset = side * (lane * Mathf.Max(6f, clearance * 2f));
                var points = new[] { a, a + offset, b + offset, b, end };
                var previous = start; float length = 0; bool valid = true;
                foreach (var point in points)
                {
                    if (!clear(previous, point)) { valid = false; break; }
                    length += Vector3.Distance(previous, point); previous = point;
                }
                if (valid && length < bestLength) { best = points; bestLength = length; }
            }
            return best;
        }

        public static bool Settled(float gap, float horizontalError, float speed, float spin)
            => gap >= -.2f && gap <= .3f && horizontalError <= .7f && speed <= .25f && spin <= .35f;
    }
}
