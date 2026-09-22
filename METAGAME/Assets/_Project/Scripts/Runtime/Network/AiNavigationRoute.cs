using UnityEngine;
using UnityEngine.AI;

namespace Metagame.Network
{
    /// <summary>Reusable route steering for follow and future interaction approach points.</summary>
    public sealed class AiNavigationRoute
    {
        // NetPlayer owns this through a field initializer; Unity native objects must be lazy.
        NavMeshPath path;
        readonly Vector3[] corners = new Vector3[128];
        int count, corner;
        float nextPath;
        Vector3 goal;
        public float Remaining { get; private set; }
        public bool Arrived { get; private set; }

        public void Reset() { count = 0; nextPath = 0; Arrived = false; Remaining = 0; }

        public bool Steer(AiNavigationWorld world, Vector3 from, Vector3 target, float stopDistance, out Vector3 direction)
        {
            direction = default; Arrived = false;
            if (world == null || !world.Ready) return false;
            path ??= new NavMeshPath();
            if (Time.time >= nextPath || (goal - target).sqrMagnitude > 1f)
            {
                nextPath = Time.time + .75f;
                goal = target;
                count = 0;
                if (!world.CalculatePath(from, target, path)) return false;
                count = path.GetCornersNonAlloc(corners);
                corner = 1;
            }
            if (count < 2 || count >= corners.Length)
            {
                Arrived = count == 1 && Vector3.Distance(from, target) <= stopDistance;
                return Arrived;
            }
            while (corner < count - 1 && Vector3.Distance(from, corners[corner]) < .2f) corner++;
            Remaining = Vector3.Distance(from, corners[corner]);
            for (int i = corner; i < count - 1; i++) Remaining += Vector3.Distance(corners[i], corners[i + 1]);
            if (Remaining <= stopDistance) { Arrived = true; return true; }
            direction = corners[corner] - from;
            direction.y = 0;
            direction.Normalize();
            return direction.sqrMagnitude > .01f;
        }
    }
}
