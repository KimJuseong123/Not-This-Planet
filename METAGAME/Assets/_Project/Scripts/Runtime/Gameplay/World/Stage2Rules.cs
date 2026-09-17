using UnityEngine;

namespace Metagame.Gameplay.Stage2
{
    public enum YardAction { None, Interact, Recover, Unfasten }
    public enum YardTargetKind { Container, Ring, Bolt, Crane, Lift, Ladder, LadderClimb, Wrench, Sprayer, Shower, Recovery, Launch }
    public enum YardTool { None, Wrench, Sprayer }

    /// <summary>Chapter 2 §§7–12. Values are prototype hypotheses, not approved balance.</summary>
    public static class Stage2Rules
    {
        public const float RadiusMin = 5f, RadiusMax = 25f, HookMin = 2f, HookMax = 19f;
        public const float BoltTurns = 1.25f, UseRange = 3.4f, SprayRange = 12f;
        public static Vector3 Hook(Vector3 pivot, float yaw, float radius, float height) =>
            pivot + Quaternion.Euler(0, yaw, 0) * new Vector3(0, height, radius);
        public static bool Aligned(Vector3 position, Quaternion rotation, Vector3 socket) =>
            Vector3.Distance(position, socket) <= .65f && Vector3.Angle(rotation * Vector3.up, Vector3.up) <= 12f;
        public static bool Fastened(float a, float b, float c) => a >= 1f && b >= 1f && c >= 1f;
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }

    /// <summary>Integrates a turning mouse tangent, without depending on screen edges.</summary>
    public sealed class CircularWrenchInput
    {
        Vector2 _previous;
        float _travel;
        public void Reset() { _previous = Vector2.zero; _travel = 0; }
        public float Sample(Vector2 delta)
        {
            if (delta.sqrMagnitude < .0025f) return 0;
            float angle = _previous.sqrMagnitude > .0025f ? Vector2.SignedAngle(_previous, delta) : 0;
            _previous = delta;
            // A reversal/zigzag is not a full turn. Ignore implausible sharp reversals.
            if (Mathf.Abs(angle) > 100f) { _travel = 0; return 0; }
            if (angle * _travel < 0) _travel = 0;
            _travel += angle;
            if (Mathf.Abs(_travel) < 12f) return 0;
            float result = Mathf.Abs(_travel) / 360f;
            _travel = 0;
            return result;
        }
    }
}
