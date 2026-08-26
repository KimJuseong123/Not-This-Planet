using UnityEngine;

namespace SpaceFlop
{
    /// <summary>
    /// 손 하나. 잡기는 FixedJoint 를 붙였다 떼는 것이 전부다 —
    /// 고정물을 잡으면 connectedBody 가 없는 조인트가 되어 세계에 매달리고,
    /// 물체를 잡으면 그 물체와 하나가 된다.
    ///
    /// 이 게임에서 잡기는 아주 관대하다:
    ///  · 장갑 반지름(0.09m)의 여섯 배인 0.50m 반경 안이면 붙는다
    ///  · 잡은 물체는 connectedMassScale 로 가볍게 느껴진다
    ///  · 끊어지는 힘이 몸무게의 40배라 사실상 놓치지 않는다
    /// </summary>
    public class Hand : MonoBehaviour
    {
        public Rigidbody Body;
        public bool IsLeft;

        public Joint Grip { get; private set; }
        public Grabbable Held { get; private set; }
        public Rigidbody HeldBody { get; private set; }

        public bool Gripping { get { return Grip != null; } }
        /// <summary>고정물을 잡고 매달려 있는 상태 (기어오르기가 성립한다).</summary>
        public bool Hanging { get { return Grip != null && HeldBody == null; } }
        /// <summary>움직일 수 있는 물체를 들고 있는 상태 (나르기가 성립한다).</summary>
        public bool Carrying { get { return Grip != null && HeldBody != null && !HeldBody.isKinematic; } }

        /// <summary>자석 손이 지금 노리고 있는 것 — 손이 이쪽으로 휘어 간다. HUD 도 이걸 표시한다.</summary>
        public Grabbable Aimed { get; set; }
        public Vector3 AimedPoint { get; set; }

        static readonly Collider[] Probe = new Collider[24];

        /// <summary>
        /// <paramref name="center"/> 주위 <paramref name="radius"/> 안에서 잡을 것을 찾는다.
        ///
        /// 각도 판정은 <b>어깨</b>(<paramref name="coneOrigin"/>) 기준으로 한다 — 뻗은 손끝 기준으로 재면
        /// 손끝 바로 옆에 있는 것이 90° 로 계산돼 전부 걸러진다. 자석이 안 먹는 이유가 대개 이것이다.
        /// </summary>
        public static Grabbable Search(Vector3 center, float radius,
                                       Vector3 coneOrigin, Vector3 coneDir, float maxAngle,
                                       out Vector3 point, out Rigidbody body)
        {
            point = center;
            body = null;

            int n = Physics.OverlapSphereNonAlloc(center, radius, Probe,
                                                  RagdollBuilder.WorldMask, QueryTriggerInteraction.Ignore);
            Grabbable best = null;
            float bestScore = float.MaxValue;
            float cos = Mathf.Cos(maxAngle * Mathf.Deg2Rad);
            bool useAngle = maxAngle < 179f && coneDir.sqrMagnitude > 1e-4f;
            if (useAngle) coneDir = coneDir.normalized;

            for (int i = 0; i < n; i++)
            {
                var col = Probe[i];
                if (col == null) continue;
                var g = col.GetComponentInParent<Grabbable>();
                if (g == null || !g.CanGrab) continue;

                Vector3 p = col.ClosestPoint(center);

                if (useAngle)
                {
                    Vector3 fromShoulder = p - coneOrigin;
                    float len = fromShoulder.magnitude;
                    if (len > 0.05f && Vector3.Dot(fromShoulder / len, coneDir) < cos) continue;
                }

                // 가까운 것 우선, 같은 거리면 "잡아 달라고 만든 것"(우선순위 높은 것) 우선
                float score = (p - center).magnitude - g.Priority * 0.25f;
                if (score >= bestScore) continue;

                bestScore = score;
                best = g;
                point = p;
                body = col.attachedRigidbody;
            }
            return best;
        }

        public bool TryGrip()
        {
            if (Grip != null) return true;
            if (Body == null) return false;

            Vector3 point;
            Rigidbody bestBody;
            var best = Search(Body.position, Tuning.GrabRadius, Vector3.zero, Vector3.zero, 180f, out point, out bestBody);
            if (best == null) return false;

            return GripTo(best, bestBody);
        }

        public bool GripTo(Grabbable target, Rigidbody targetBody)
        {
            if (Grip != null || target == null) return false;

            var j = gameObject.AddComponent<FixedJoint>();
            j.connectedBody = targetBody;                     // null 이면 세계에 고정된다
            j.enableCollision = false;
            j.enablePreprocessing = false;
            j.breakForce = Tuning.GripBreakForce * target.GripStrength;
            j.breakTorque = Tuning.GripBreakTorque * target.GripStrength;
            j.massScale = 1f;
            // 잡은 물체를 가볍게 느끼게 한다 — 궤짝을 들어도 팔이 뜯겨 나가지 않는다
            j.connectedMassScale = targetBody != null && !targetBody.isKinematic ? Tuning.CarryMassScale : 1f;

            Grip = j;
            Held = target;
            HeldBody = targetBody;
            target.NotifyGrabbed(this);
            return true;
        }

        public void Release()
        {
            if (Held != null) Held.NotifyReleased(this);
            if (Grip != null) Destroy(Grip);
            Grip = null;
            Held = null;
            HeldBody = null;
        }

        void OnJointBreak(float breakForce)
        {
            // 손목(ConfigurableJoint)은 끊어지지 않게 해뒀으니 끊긴 건 잡은 손이다
            if (Held != null) Held.NotifyReleased(this);
            Grip = null;
            Held = null;
            HeldBody = null;
        }
    }
}
