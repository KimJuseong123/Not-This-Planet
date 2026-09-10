using UnityEngine;
using Metagame.Gameplay;

namespace Metagame.Network
{
    /// <summary>
    /// 다른 사람의 몸을 화면에 어떻게 보여 줄지 담당한다.
    ///
    /// 네트워크로 오는 것은 골반의 위치·회전 하나뿐이다(NetworkTransform). 관절 11개를
    /// 전부 보내면 사람 수 × 관절 수만큼 트래픽이 늘고, 그럴 필요도 없다 — 팔다리가
    /// 정확히 어디 있는지는 게임 결과에 영향을 주지 않는다.
    ///
    /// 그래서 골반만 서버가 보낸 자리로 옮기고, 팔다리는 조인트에 맡긴다. 조인트에
    /// 약한 용수철을 걸어 두면 기본 자세를 유지하면서 골반이 움직일 때 조금씩 흔들린다.
    /// 용수철이 없으면 축 늘어진 시체처럼 보이고, 너무 세면 마네킹처럼 뻣뻣해진다.
    ///
    /// 물리는 서버가 돈다(S15P21B202-145). 여기 있는 몸은 판정에 관여하지 않는
    /// 그림일 뿐이라 충돌을 모두 끊고 RemotePlayer 레이어로 옮긴다.
    ///
    /// <para><b>내 몸은 예외다</b>(S15P21B202-363). 참가자도 자기 캐릭터는 로컬 물리로
    /// 즉시 돌린다(정책 P1) — 그래서 <c>mine</c> 이면 위의 개조를 <b>하나도 하지 않고</b>
    /// 서버 자세와의 오차만 <see cref="AddCorrection"/> 으로 조금씩 갚는다. 남의 몸에만
    /// 위의 설명이 그대로 적용된다. <c>docs/network-sync-policy.md</c></para>
    /// </summary>
    public sealed class RemoteAvatar : MonoBehaviour
    {
        /// 조인트가 기본 자세를 붙드는 힘. 약하면 늘어지고 세면 뻣뻣하다.
        const float JointSpring = 120f;
        const float JointDamper = 12f;
        const float ReachShoulderSpring = 220f;
        const float ReachShoulderDamper = 18f;
        const float ReachElbowSpring = 90f;
        const float ReachElbowDamper = 9f;

        /// <summary>
        /// 이만큼 넘게 한 번에 벌어지면 보간하지 않고 그 자리로 옮긴다.
        /// 리스폰·재접속처럼 진짜 순간이동일 때, 사이를 이어 그리면 몸이
        /// 맵을 가로질러 날아가는 그림이 된다.
        /// </summary>
        const float SnapDistance = 3f;

        /// <summary>
        /// 서버가 몰아 주는 내 몸을 좇을 때 카메라가 쓰는 추종 속도 (S15P21B202-328).
        /// 시정수 25ms — 기본값 16(62ms)보다 빠르다.
        /// </summary>
        const float NetworkFocusSmoothing = 40f;

        PlayerRig rig;
        Doll doll;
        Transform source;

        bool mine;
        bool leftArmIntent;
        bool rightArmIntent;

        /// <summary>
        /// 서버가 보낸 자리를 그리는 몸으로 바꾼다. <paramref name="source"/> 는 서버가
        /// 위치를 넣어 주는 네트워크 오브젝트(NetPlayer)의 Transform 이다.
        /// </summary>
        /// <param name="mine">
        /// 내 캐릭터인가. <b>내 것이면 개조하지 않는다</b> — 로컬 물리가 계속 돌아 입력이
        /// 즉시 반영되고(P1), 서버 자세와의 오차만 조금씩 갚는다(S15P21B202-363).
        /// 남의 것이면 서버 자세를 끌고 다니는 그림으로 바꾼다.
        /// </param>
        public static RemoteAvatar Attach(PlayerRig rig, Transform source, bool mine = false)
        {
            var avatar = rig.gameObject.AddComponent<RemoteAvatar>();
            avatar.rig = rig;
            avatar.doll = rig.Doll;
            avatar.source = source;
            avatar.mine = mine;
            avatar.Configure();
            return avatar;
        }

        /// <summary>
        /// 원격 플레이어의 표시용 팔 상태를 갱신한다. 입력 자체나 잡기 결과를
        /// 네트워크로 새로 보내지 않고, NetPlayer가 이미 복제하는 locomotion 상태만
        /// 사용한다. 실제 판정은 계속 서버의 PlayerRig가 담당한다.
        /// </summary>
        public void SetArmIntent(bool left, bool right)
        {
            leftArmIntent = left;
            rightArmIntent = right;
        }

        void Configure()
        {
            // ── 내 몸은 개조하지 않는다 (S15P21B202-363, P1) ──────────────
            //
            // 아래의 개조는 **남의 몸을 서버 자세로 끌고 다니기 위한 것**이다. 내 몸에
            // 그것을 하면 로컬 물리가 돌 수 없다 — `Balance` 조인트가 없어 인형이 서지
            // 못하고, 충돌이 꺼져 땅에 닿지 않고, 골반이 kinematic 이라 힘을 받지 않는다.
            //
            // 그래서 내 몸은 **그대로 둔다.** `PlayerRig` 가 계속 돌아 내 입력이 즉시
            // 반영되고, 서버 자세와의 오차는 `AddCorrection` 으로 부드럽게 얹는다.
            // 참가자 쪽 입력 지연 ~166ms 의 대부분이 여기서 없어진다.
            if (mine)
            {
                // **물리만 살린다. 판정은 그대로 서버 몫이다** (`!275` AI 리뷰).
                //
                // 이 MR 이 선언한 것은 "위치만 예측" 이다. 아래 둘을 함께 건너뛰면 그
                // 경계를 넘는다 —
                //   · ClampInteractor: 고정쇠 잡기가 클라에서도 돌아 서버와 이중 판정이 된다.
                //     게이지 표시를 P1 으로 올리는 것은 같은 이슈의 **다음 몫**이다
                //   · NetworkDriven: `GameSession` 이 이 표시를 보고 낙사·전원 기절의
                //     **부활 판정에서 물러난다**. 끄면 클라가 로컬로 부활시키고 서버가
                //     보낸 자세가 그것을 되돌려, 스폰 지점과 원래 자리 사이에서 튄다
                if (rig.ClampInteractor != null) rig.ClampInteractor.enabled = false;
                rig.NetworkDriven = true;

                // 부활 판정도 내려놓는다 (S15P21B202-390). 클라이언트에서 홀드가
                // 완료되면 DownedRescue 가 되살리는데, 그러면 **이 화면에서만**
                // 일어나고 되돌아오지도 않는다 — downed 훅은 값이 바뀔 때만 불려서
                // 서버가 여전히 true 인 동안 다시 내려오는 것이 없다(-378).
                rig.ServerAuthority = false;

                // 카메라 스무딩만 낮춘다. 예측이 들어와 이제 표시가 한 겹 덜 다듬어지므로
                // 기본값(62ms)을 그대로 두면 더해지는 것은 지연뿐이다.
                if (rig.Cam != null) rig.Cam.FocusSmoothing = NetworkFocusSmoothing;
                return;
            }

            // 조종 로직을 멈춘다. PlayerRig 는 매 물리 스텝마다 힘을 넣는데,
            // 그대로 두면 서버가 보낸 위치와 계속 싸운다.
            rig.enabled = false;
            if (rig.ClampInteractor != null) rig.ClampInteractor.enabled = false;
            rig.NetworkDriven = true;
            rig.IsLocal = false;

            // 남의 몸에서도 내려놓는다 (S15P21B202-390) — 위와 같은 이유다.
            rig.ServerAuthority = false;

            // 골반을 세계에 붙들어 인형을 세우는 조인트다. 골반을 우리가 직접
            // 옮길 것이므로 남겨 두면 서로 잡아당긴다.
            RemoveNow(doll.Balance);
            doll.Balance = null;

            for (int i = 0; i < doll.Bodies.Count; i++)
            {
                var b = doll.Bodies[i];
                if (b == null) continue;

                b.detectCollisions = false;
                b.gameObject.layer = GameLayers.RemotePlayer;

                if (b == doll.Hips)
                {
                    // 골반만 우리가 옮긴다. 물리 스텝 사이를 부드럽게 잇도록 보간을 켠다.
                    b.isKinematic = true;
                    b.interpolation = RigidbodyInterpolation.Interpolate;
                }
                else
                {
                    // 팔다리는 계속 돌리되 중력은 끈다. 골반에 매달린 채 흔들리기만
                    // 하면 되고, 중력까지 받으면 아래로 처져 자세가 무너진다.
                    b.isKinematic = false;
                    b.useGravity = false;
                }
            }

            SetDrives();
        }

        /// <summary>
        /// 지금 당장 없앤다. <c>Destroy</c> 는 프레임이 끝날 때 처리되므로, 그때까지
        /// 조인트가 살아 남아 첫 물리 스텝에서 골반을 붙든다. EditMode 테스트에서는
        /// 아예 오류가 된다.
        /// </summary>
        static void RemoveNow(Object target)
        {
            if (target != null) DestroyImmediate(target);
        }

        /// <summary>모든 관절에 같은 세기의 용수철을 건다. 기본 자세를 붙들어 준다.</summary>
        void SetDrives()
        {
            Drive(doll.ChestJoint);
            Drive(doll.HeadJoint);
            DriveArm(doll.Left);
            DriveArm(doll.Right);
            DriveLeg(doll.LeftLeg);
            DriveLeg(doll.RightLeg);
        }

        void DriveArm(Arm arm)
        {
            if (arm == null) return;
            Drive(arm.Shoulder);
            Drive(arm.Elbow);
        }

        void DriveLeg(Leg leg)
        {
            if (leg == null) return;
            Drive(leg.Hip);
            Drive(leg.Knee);
            Drive(leg.Ankle);
        }

        static void Drive(ConfigurableJoint joint)
        {
            if (joint == null) return;
            JointTools.SetDrive(joint, JointSpring, JointDamper);
        }

        // ── 내 몸의 오차 갚기 (S15P21B202-363) ──────────────────────────

        Vector3 correction;

        /// <summary>서버가 알려 준 오차를 쌓는다. 갚는 것은 물리 스텝에서 조금씩 한다.</summary>
        public void AddCorrection(Vector3 error)
        {
            // 덧붙이지 않고 **덮어쓴다.** 오차는 누적량이 아니라 "지금 얼마나 벌어져
            // 있나" 이고, 새 소식이 옛 소식을 대체한다. 더하면 같은 오차를 여러 번 갚아
            // 지나쳐 버린다.
            correction = error;
        }

        /// <summary>스냅했으니 갚을 것이 없다.</summary>
        public void ClearCorrection() => correction = Vector3.zero;

        /// <summary>남은 오차의 일부를 갚는다. 힘이 아니라 위치로 옮긴다 — 오차는 물리가 아니다.</summary>
        void Converge()
        {
            Vector3 step = PredictionCorrection.Step(correction, out correction);
            if (step != Vector3.zero) doll.Hips.position += step;
        }

        void FixedUpdate() => Follow();

        /// <summary>
        /// 골반을 서버가 알려 준 자리로 옮긴다. 물리 스텝에서 옮겨야 매달린
        /// 팔다리가 그 움직임을 힘으로 느낀다 — Transform 을 직접 대입하면
        /// 조인트가 순간이동으로 받아들여 몸이 튄다.
        ///
        /// 런타임에서는 <c>FixedUpdate</c> 가 부르고, EditMode 테스트는
        /// <c>Physics.Simulate</c> 와 짝지어 직접 부른다(PlayerRig.Tick 과 같은 방식).
        /// </summary>
        public void Follow()
        {
            if (source == null || doll == null || doll.Hips == null) return;

            // 내 몸은 로컬 물리가 돌린다. 여기서는 서버와의 **오차만** 조금씩 갚는다.
            if (mine)
            {
                Converge();
                return;
            }

            ApplyArmPose(doll.Left, leftArmIntent);
            ApplyArmPose(doll.Right, rightArmIntent);

            Vector3 target = source.position;
            Quaternion facing = source.rotation;

            if ((doll.Hips.position - target).sqrMagnitude > SnapDistance * SnapDistance)
            {
                doll.Teleport(target, facing);
                return;
            }

            doll.Hips.MovePosition(target);
            doll.Hips.MoveRotation(facing);
        }

        void ApplyArmPose(Arm arm, bool reaching)
        {
            if (arm == null || arm.Shoulder == null || arm.Elbow == null) return;

            if (!reaching)
            {
                JointTools.SetDrive(arm.Shoulder, JointSpring, JointDamper);
                JointTools.SetDrive(arm.Elbow, JointSpring, JointDamper);
                JointTools.SetTargetLocalRotation(arm.Shoulder, Quaternion.identity, arm.ShoulderStart);
                JointTools.SetTargetLocalRotation(arm.Elbow, Quaternion.identity, arm.ElbowStart);
                return;
            }

            JointTools.SetDrive(arm.Shoulder, ReachShoulderSpring, ReachShoulderDamper);
            JointTools.SetDrive(arm.Elbow, ReachElbowSpring, ReachElbowDamper);

            Vector3 direction = source != null ? source.forward : Vector3.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = doll.Chest != null ? doll.Chest.transform.forward : Vector3.forward;
                direction.y = 0f;
            }
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            direction.Normalize();

            Quaternion aimWorld = Quaternion.FromToRotation(Vector3.down, direction);
            Quaternion aimLocal = doll.Chest != null
                ? Quaternion.Inverse(doll.Chest.rotation) * aimWorld
                : aimWorld;
            JointTools.SetTargetLocalRotation(arm.Shoulder, aimLocal, arm.ShoulderStart);
            JointTools.SetTargetLocalRotation(arm.Elbow, Quaternion.identity, arm.ElbowStart);
        }
    }
}
