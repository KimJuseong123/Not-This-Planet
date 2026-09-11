using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>두 점 사이를 오가는 자기부상 화물판. 위에 탄 인형은 마찰로 같이 간다.</summary>
    public class MovingPlatform : MonoBehaviour
    {
        public static readonly List<MovingPlatform> All = new List<MovingPlatform>();

        public Vector3 A, B;
        public float Period = 7f;
        public float Phase;

        Rigidbody body;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static MovingPlatform Create(Transform parent, Vector3 a, Vector3 b, Vector3 size,
                                            Color color, float period = 7f, float phase = 0f)
        {
            var go = Prim.Box("화물판", a, size, color, 0.2f, parent);
            go.GetComponent<Collider>().material = Prim.GroundPhysics;

            var d = Prim.DecoRoot(go.transform);
            Prim.FitBox(d, "부상 코일", new Vector3(0f, -size.y * 0.55f, 0f),
                        new Vector3(size.x * 0.8f, 0.10f, size.z * 0.8f), Color.white, 0.3f)
                .GetComponent<Renderer>().sharedMaterial = Prim.Ghost(new Color(0.40f, 0.80f, 1.00f, 0.55f), 1.8f, 1f);

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var mp = go.AddComponent<MovingPlatform>();
            mp.body = rb;
            mp.A = a; mp.B = b; mp.Period = period; mp.Phase = phase;
            return mp;
        }

        void FixedUpdate()
        {
            float t = (Mathf.Sin((Time.time / Period + Phase) * Mathf.PI * 2f) + 1f) * 0.5f;
            body.MovePosition(Vector3.Lerp(A, B, t));
        }
    }
}
