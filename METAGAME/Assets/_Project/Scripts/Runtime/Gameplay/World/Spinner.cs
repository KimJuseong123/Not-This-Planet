using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>제자리에서 도는 통신 안테나 — 맞으면 저중력이라 멀리 날아간다.</summary>
    public class Spinner : MonoBehaviour
    {
        public float DegPerSecond = 60f;
        Rigidbody body;
        float angle;

        public static Spinner Create(Transform parent, Vector3 center, Vector3 size, Color color, float dps)
        {
            var go = Prim.Box("회전 안테나", center, size, color, 0.2f, parent);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var s = go.AddComponent<Spinner>();
            s.body = rb;
            s.DegPerSecond = dps;
            return s;
        }

        void FixedUpdate()
        {
            angle += DegPerSecond * Time.fixedDeltaTime;
            body.MoveRotation(Quaternion.Euler(0f, angle, 0f));
        }
    }
}
