using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 만들어진 인형 한 구. 부위별 강체와 조인트를 들고 있고,
    /// 부활할 때 통째로 원위치시키는 일도 여기서 한다.
    /// </summary>
    public class Doll
    {
        public Transform Root;
        public Rigidbody Hips, Chest, Head;
        public ConfigurableJoint ChestJoint, HeadJoint;
        public ConfigurableJoint Balance;      // 골반 ↔ 세계. 이것이 인형을 세운다
        public Arm Left, Right;
        public Leg LeftLeg, RightLeg;

        /// <summary>씌운 블렌더 메시 (없으면 프리미티브 룩으로 돈 것이다).</summary>
        public SpaceSuit Suit;

        public readonly List<Rigidbody> Bodies = new List<Rigidbody>();
        public readonly List<Collider> Colliders = new List<Collider>();

        /// <summary>지금 화면에 보이는지. <see cref="SetVisible"/> 가 바꾼다.</summary>
        public bool Visible { get; private set; } = true;

        /// <summary>골반 기준 각 부위의 처음 상대 위치/회전 — 부활할 때 이 자세로 되돌린다.</summary>
        public readonly List<Vector3> RestOffsets = new List<Vector3>();
        public readonly List<Quaternion> RestRotations = new List<Quaternion>();

        public float TotalMass
        {
            get { float m = 0f; for (int i = 0; i < Bodies.Count; i++) if (Bodies[i] != null) m += Bodies[i].mass; return m; }
        }

        public Vector3 Center { get { return Hips != null ? Hips.worldCenterOfMass : Vector3.zero; } }

        public void CaptureRestPose()
        {
            RestOffsets.Clear();
            RestRotations.Clear();
            Vector3 origin = Hips.position;
            for (int i = 0; i < Bodies.Count; i++)
            {
                RestOffsets.Add(Bodies[i].position - origin);
                RestRotations.Add(Bodies[i].rotation);
            }
        }

        /// <summary>인형 전체를 처음 자세 그대로 옮긴다 (부활/순간이동).</summary>
        public void Teleport(Vector3 hipsPosition, Quaternion facing)
        {
            for (int i = 0; i < Bodies.Count; i++)
            {
                var b = Bodies[i];
                if (b == null) continue;
                b.linearVelocity = Vector3.zero;
                b.angularVelocity = Vector3.zero;
                b.position = hipsPosition + facing * RestOffsets[i];
                b.rotation = facing * RestRotations[i];
                b.transform.SetPositionAndRotation(b.position, b.rotation);
            }
        }

        /// <summary>
        /// 화면에서만 감춘다. 물리는 그대로 둔다 — 콜라이더와 조인트가 살아 있어야
        /// 탑승 중 좌석 조인트가 유지되고 하차할 때 자세가 돌아온다.
        ///
        /// 렌더러를 캐시하지 않고 매번 훑는 이유는 모델(<see cref="Suit"/>)이 런타임에
        /// 붙거나 떨어질 수 있어서다. <see cref="Root"/> 하위를 훑으면 프리미티브 몸과
        /// 씌운 메시가 함께 잡힌다.
        /// </summary>
        public void SetVisible(bool visible)
        {
            Visible = visible;
            if (Root == null) return;

            var renderers = Root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
                renderers[i].enabled = visible;
        }

        public void SetSleepAllowed(bool allowed)
        {
            for (int i = 0; i < Bodies.Count; i++)
                if (Bodies[i] != null) { if (allowed) Bodies[i].WakeUp(); else Bodies[i].WakeUp(); }
        }
    }
}
