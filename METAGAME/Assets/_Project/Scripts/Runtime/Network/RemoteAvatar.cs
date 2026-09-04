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
        /// 내 캐릭터인가. 내 것이어도 물리는 서버가 돌리므로 하는 일은 같지만,
        /// 카메라가 붙어 있어 1인칭 표시(<see cref="PlayerRig.IsLocal"/>)는 남겨 둔다.
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
            // 조종 로직을 멈춘다. PlayerRig 는 매 물리 스텝마다 힘을 넣는데,
            // 그대로 두면 서버가 보낸 위치와 계속 싸운다.
            rig.enabled = false;
            if (rig.ClampInteractor != null) rig.ClampInteractor.enabled = false;
            rig.NetworkDriven = true;
            if (!mine) rig.IsLocal = false;

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
