using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay
{
    // Static, scene-authored water geometry is sampled identically by server and prediction.
    // Surface ripples are cosmetic and never change the movement boundary.
    public sealed class DrainageWater : MonoBehaviour
    {
        static readonly List<DrainageWater> Active = new();
        [SerializeField] Vector2 _size = new(9, 64);
        [SerializeField] float _bottom = -3.2f;
        [SerializeField] Vector3 _current = new(0, 0, -.3f);
        public float Surface => transform.position.y;
        public Vector2 Size => _size;
        public Vector3 Current => _current;
        public const float SwimSpeed = 1.65f;
        public const float VerticalSpeed = 1.15f;
        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() => Active.Remove(this);
        public bool Contains(Vector3 hips)
        {
            var p = hips - transform.position;
            return Mathf.Abs(p.x) <= _size.x * .5f && Mathf.Abs(p.z) <= _size.y * .5f
                && p.y <= RagdollBuilder.HipsY && hips.y >= _bottom;
        }
        public static DrainageWater Sample(Vector3 hips)
        {
            foreach (var water in Active) if (water != null && water.Contains(hips)) return water;
            return null;
        }
        public float MovementScale(Vector3 hips) => Mathf.Lerp(1, .5f,
            Mathf.Clamp01((Surface - hips.y + RagdollBuilder.HipsY) / .22f));
        public bool CanSwim(Vector3 hips, bool wasSwimming) => Contains(hips)
            && hips.y < Surface + (wasSwimming ? .14f : -.04f);
        public float VerticalTarget(float hipsY, float input)
        {
            // Soft surface boundary keeps Space from repeatedly launching out of the water.
            float target = input * VerticalSpeed;
            if (hipsY > Surface - .14f) target = Mathf.Min(target, (Surface - .08f - hipsY) * 5);
            return target;
        }
    }
}
