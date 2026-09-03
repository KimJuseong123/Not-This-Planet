using System;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// A deterministic snapshot of the moving storm volume.
    /// Local X is the travel axis, Y is vertical, and Z is lateral.
    /// </summary>
    public readonly struct StormPose
    {
        public readonly bool IsActive;
        public readonly Vector3 Center;
        public readonly Vector3 Direction;
        public readonly Vector3 HalfExtents;
        public readonly int CycleIndex;

        public StormPose(bool isActive, Vector3 center, Vector3 direction,
                          Vector3 halfExtents, int cycleIndex)
        {
            IsActive = isActive;
            Center = center;
            Direction = direction;
            HalfExtents = halfExtents;
            CycleIndex = cycleIndex;
        }

        public Quaternion Rotation
        {
            get { return Direction.sqrMagnitude < 0.0001f
                ? Quaternion.identity
                : Quaternion.LookRotation(Direction, Vector3.up) * Quaternion.Euler(0f, 90f, 0f); }
        }

        public bool Contains(Vector3 point)
        {
            if (!IsActive) return false;
            Vector3 local = Quaternion.Inverse(Rotation) * (point - Center);
            return Mathf.Abs(local.x) <= HalfExtents.x
                && Mathf.Abs(local.y) <= HalfExtents.y
                && Mathf.Abs(local.z) <= HalfExtents.z;
        }

        public static StormPose Inactive(Vector3 halfExtents, int cycleIndex = -1)
        {
            return new StormPose(false, Vector3.zero, Vector3.forward, halfExtents, cycleIndex);
        }
    }

    /// <summary>
    /// O(1) deterministic storm schedule. The midpoint of the configured interval is
    /// used as the fixed inter-storm gap so late-joining clients can reconstruct the
    /// current cycle directly from shared server time without replaying past events.
    /// </summary>
    public sealed class DustStormSchedule
    {
        readonly AtmosphereProfile.DustStormSettings settings;

        public DustStormSchedule(AtmosphereProfile.DustStormSettings settings)
        {
            this.settings = settings ?? new AtmosphereProfile.DustStormSettings();
        }

        public StormPose Evaluate(double simulationTime, Bounds mapBounds)
        {
            Vector3 halfExtents = new Vector3(
                Mathf.Max(0.1f, Mathf.Abs(settings.halfExtents.x)),
                Mathf.Max(0.1f, Mathf.Abs(settings.halfExtents.y)),
                Mathf.Max(0.1f, Mathf.Abs(settings.halfExtents.z)));

            if (!settings.enabled || simulationTime < 0d || double.IsNaN(simulationTime)
                || double.IsInfinity(simulationTime) || settings.speed <= 0.001f)
                return StormPose.Inactive(halfExtents);

            Vector3 size = mapBounds.size;
            if (size.x <= 0.01f || size.z <= 0.01f)
                return StormPose.Inactive(halfExtents);

            float gap = Mathf.Max(0.1f, (settings.minimumInterval + settings.maximumInterval) * 0.5f);
            float travelDistance = Mathf.Sqrt(size.x * size.x + size.z * size.z)
                                 + 2f * (halfExtents.x + settings.spawnMargin);
            float travelDuration = travelDistance / Mathf.Max(0.01f, settings.speed);
            double cycleDuration = travelDuration + gap;
            int cycle = (int)Math.Floor(simulationTime / cycleDuration);
            double cycleStart = cycle * cycleDuration;
            double activeAt = cycleStart + gap;
            double elapsed = simulationTime - activeAt;

            if (elapsed < 0d || elapsed > travelDuration)
                return StormPose.Inactive(halfExtents, cycle);

            Vector3 perimeter = SelectPerimeterPoint(mapBounds, settings.seed, cycle);
            Vector3 toCenter = mapBounds.center - perimeter;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.0001f) toCenter = Vector3.forward;
            toCenter.Normalize();

            float angle = Mathf.Lerp(-settings.directionConeDegrees * 0.5f,
                                      settings.directionConeDegrees * 0.5f,
                                      Sample01(settings.seed, cycle, 3));
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * toCenter;
            direction.y = 0f;
            direction.Normalize();

            Vector3 start = perimeter - direction * (halfExtents.x + settings.spawnMargin);
            Vector3 center = start + direction * (float)elapsed * settings.speed;
            center.y = mapBounds.center.y;
            return new StormPose(true, center, direction, halfExtents, cycle);
        }

        public static float Sample01(int seed, int cycleIndex, int channel)
        {
            unchecked
            {
                uint value = (uint)seed;
                value += 0x9E3779B9u * (uint)(cycleIndex + 1);
                value ^= 0x85EBCA6Bu * (uint)(channel + 1);
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777216f;
            }
        }

        static Vector3 SelectPerimeterPoint(Bounds bounds, int seed, int cycle)
        {
            float sideSample = Sample01(seed, cycle, 0);
            int side = Mathf.Min(3, Mathf.FloorToInt(sideSample * 4f));
            float along = Mathf.Lerp(-0.35f, 0.35f, Sample01(seed, cycle, 1));
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            switch (side)
            {
                case 0: return new Vector3(bounds.min.x, center.y, center.z + extents.z * along * 2f);
                case 1: return new Vector3(bounds.max.x, center.y, center.z + extents.z * along * 2f);
                case 2: return new Vector3(center.x + extents.x * along * 2f, center.y, bounds.min.z);
                default: return new Vector3(center.x + extents.x * along * 2f, center.y, bounds.max.z);
            }
        }
    }
}
