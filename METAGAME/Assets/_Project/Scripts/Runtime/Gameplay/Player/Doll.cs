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

                // 키네마틱 몸에는 속도를 넣을 수 없다 — 넣으면 경고만 쏟아진다.
                // 원격 아바타의 골반이 그렇다(Network.RemoteAvatar).
                if (!b.isKinematic)
                {
                    b.linearVelocity = Vector3.zero;
                    b.angularVelocity = Vector3.zero;
                }
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
        /// <param name="visible">몸을 그릴지.</param>
        /// <param name="keepShadow">
        /// 감출 때 그림자는 남길지. 1인칭에서 제 몸을 감출 때 쓴다 — 발밑을 읽는 단서가
        /// 제 그림자이므로, 메시만 빼고 그림자는 그대로 두는 것이 옳다.
        /// 탑승 중에는 반대로 그림자까지 감춘다. 기체 안에 있는 몸이고 기체 껍데기도
        /// 감춰 둔 상태라, 그림자만 남으면 허공에 혼자 뜬 그림자가 된다.
        /// </param>
        public void SetVisible(bool visible, bool keepShadow = false)
        {
            Visible = visible;
            if (Root == null) return;

            var renderers = Root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = visible || keepShadow;
                renderers[i].shadowCastingMode = visible
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }

        public void SetSleepAllowed(bool allowed)
        {
            for (int i = 0; i < Bodies.Count; i++)
                if (Bodies[i] != null) { if (allowed) Bodies[i].WakeUp(); else Bodies[i].WakeUp(); }
        }
    }
}
