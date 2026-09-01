using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 손 하나. 고정물을 잡을 때는 FixedJoint로 세계에 매달리고,
    /// 가벼운 운반 물체는 논리적인 운반 상태만 유지한다.
    ///
    /// 이 게임에서 잡기는 아주 관대하다:
    ///  · 장갑 반지름(0.09m)의 여섯 배인 0.50m 반경 안이면 붙는다
    ///  · 고정형 물체는 connectedMassScale 로 가볍게 느껴진다
    ///  · 끊어지는 힘이 몸무게의 40배라 사실상 놓치지 않는다
    /// </summary>
    public class Hand : MonoBehaviour
    {
        public Rigidbody Body;
        public bool IsLeft;

        public Joint Grip { get; private set; }
        public Grabbable Held { get; private set; }
        public Rigidbody HeldBody { get; private set; }
        bool carrying;
        bool braced;

        public bool Gripping { get { return Grip != null || braced; } }
        /// <summary>고정물을 잡고 매달려 있는 상태 (기어오르기가 성립한다).</summary>
        public bool Hanging { get { return Grip != null && HeldBody == null; } }
        /// <summary>움직일 수 있는 물체를 들고 있는 상태 (나르기가 성립한다).</summary>
        public bool Carrying { get { return carrying && HeldBody != null; } }

        /// <summary>자석 손이 지금 노리고 있는 것 — 손이 이쪽으로 휘어 간다.</summary>
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
            if (Grip != null || braced) return true;
            if (Body == null) return false;

            Vector3 point;
            Rigidbody bestBody;
            var best = Search(Body.position, Tuning.GrabRadius, Vector3.zero, Vector3.zero, 180f, out point, out bestBody);
            if (best == null) return false;

            // 너무 무거운 동적 물체는 들어 올리지 않는다. 손은 고정물처럼 세계에
            // 짚고(HeldBody == null), 물체는 원래 자리에 남는다.
            if (bestBody != null && !bestBody.isKinematic && bestBody.mass >= Tuning.FixedGripMass)
                bestBody = null;

            return GripTo(best, bestBody);
        }

        public bool GripTo(Grabbable target, Rigidbody targetBody)
        {
            if (Grip != null || Held != null || target == null) return false;

            // 가벼운 동적 물체는 손과 물체 사이에 물리 Joint를 만들지 않는다.
            // PlayerRig가 운반 상태를 Kinematic으로 전환하고 크로스헤드 위치를 직접 추적한다.
            // 무거운 물체와 세계 고정물만 아래 FixedJoint 경로를 사용한다.
            if (targetBody != null && !targetBody.isKinematic && targetBody.mass < Tuning.FixedGripMass)
                return CarryTo(target, targetBody);

            return AttachFixed(target, targetBody);
        }

        /// <summary>
        /// 지정한 월드 지점에 손을 고정한다.
        ///
        /// 고정쇠처럼 Collider의 중심과 실제 잡는 지점이 분리된 대상은 일반
        /// <see cref="GripTo"/>의 자동 앵커만으로는 손이 대상에 결합되지 않는다.
        /// 이 경로는 양쪽 앵커를 같은 월드 지점으로 명시해 실제 팔 결합을 만든다.
        /// </summary>
        public bool GripToFixedAtPoint(Grabbable target, Rigidbody targetBody, Vector3 worldPoint)
        {
            if (Grip != null || Held != null || target == null || Body == null)
                return false;
            if (!IsFinite(worldPoint)) return false;

            return AttachFixed(target, targetBody, worldPoint);
        }

        /// <summary>
        /// 도구처럼 물체를 손에 고정한다. 운반체와 달리 논리상 고정 그립으로 표시하지만,
        /// 물체의 위치·회전은 PlayerRig가 직접 제어하므로 물리 FixedJoint는 만들지 않는다.
        /// 이렇게 해야 키네마틱 도구의 포즈 제어와 두 손의 물리 solver가 서로 당기지 않는다.
        /// </summary>
        public bool BraceTo(Grabbable target, Rigidbody targetBody)
        {
            if (Grip != null || braced || Held != null || target == null || targetBody == null) return false;

            Held = target;
            HeldBody = targetBody;
            carrying = false;
            braced = true;
            target.NotifyGrabbed(this);
            return true;
        }

        bool AttachFixed(Grabbable target, Rigidbody targetBody)
        {
            var j = CreateFixedJoint(target, targetBody);
            j.autoConfigureConnectedAnchor = true;
            return CompleteFixedGrip(target, targetBody, j);
        }

        bool AttachFixed(Grabbable target, Rigidbody targetBody, Vector3 worldPoint)
        {
            var j = CreateFixedJoint(target, targetBody);
            j.autoConfigureConnectedAnchor = false;
            j.anchor = transform.InverseTransformPoint(worldPoint);
            j.connectedAnchor = targetBody != null
                ? targetBody.transform.InverseTransformPoint(worldPoint)
                : worldPoint;
            return CompleteFixedGrip(target, targetBody, j);
        }

        FixedJoint CreateFixedJoint(Grabbable target, Rigidbody targetBody)
        {
            var j = gameObject.AddComponent<FixedJoint>();
            j.connectedBody = targetBody;                     // null 이면 세계에 고정된다
            j.enableCollision = false;
            j.enablePreprocessing = false;
            j.breakForce = Tuning.GripBreakForce * target.GripStrength;
            j.breakTorque = Tuning.GripBreakTorque * target.GripStrength;
            j.massScale = 1f;
            // 잡은 물체를 가볍게 느끼게 한다 — 궤짝을 들어도 팔이 뜯겨 나가지 않는다
            j.connectedMassScale = targetBody != null && !targetBody.isKinematic ? Tuning.CarryMassScale : 1f;

            return j;
        }

        bool CompleteFixedGrip(Grabbable target, Rigidbody targetBody, FixedJoint j)
        {
            Grip = j;
            Held = target;
            HeldBody = targetBody;
            carrying = false;
            target.NotifyGrabbed(this);
            return true;
        }

        static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        /// <summary>
        /// 물리 Joint 없이 운반 상태만 설정한다. 실제 Rigidbody 상태 전환과
        /// 플레이어 충돌 무시는 PlayerRig가 소유한다.
        /// </summary>
        public bool CarryTo(Grabbable target, Rigidbody targetBody)
        {
            if (Grip != null || Held != null || target == null || targetBody == null || targetBody.isKinematic)
                return false;

            Grip = null;
            Held = target;
            HeldBody = targetBody;
            carrying = true;
            target.NotifyGrabbed(this);
            return true;
        }

        public void Release()
        {
            if (Held != null) Held.NotifyReleased(this);

            // 에디터에서는 Destroy 가 동작하지 않는다. EditMode 검증에서 손을 놓으면
            // 에러 로그만 남고 <b>조인트가 그대로 남아</b> 몸이 물체에 계속 묶인다.
            // 저장소의 다른 곳(Prim.Decor · PartAssemblyLink.DestroyObject ·
            // PlayerRig.DisconnectTransportJoint)은 모두 재생 여부로 가린다.
            if (Grip != null)
            {
                if (Application.isPlaying) Destroy(Grip);
                else DestroyImmediate(Grip);
            }
            Grip = null;
            Held = null;
            HeldBody = null;
            carrying = false;
            braced = false;
        }

        void OnJointBreak(float breakForce)
        {
            // 손목(ConfigurableJoint)은 끊어지지 않게 해뒀으니 끊긴 건 잡은 손이다
            if (Held != null) Held.NotifyReleased(this);
            Grip = null;
            Held = null;
            HeldBody = null;
            carrying = false;
            braced = false;
        }
    }
}
