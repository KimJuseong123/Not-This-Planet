using System.Collections.Generic;
using Metagame.Network;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 다른 사람의 몸이 화면에서 어떻게 보이는지 검증한다(S15P21B202-148).
    ///
    /// 네트워크로 오는 것은 골반 하나뿐이라, 팔다리가 스스로 자세를 유지하지 못하면
    /// 축 늘어진 시체가 되고, 순간이동을 보간하면 몸이 맵을 가로질러 날아간다.
    /// 그 두 가지가 이 검증의 전부다.
    /// </summary>
    public sealed class RemoteAvatarTests
    {
        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            // 물리를 우리가 원하는 만큼만 돌린다. EditMode 에서는 자동으로 돌지 않으므로
            // 명시하지 않으면 Physics.Simulate 가 무시된다.
            _savedMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = _savedMode;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        /// <summary>
        /// 원격 아바타 한 구와, 서버가 위치를 넣어 주는 자리를 만든다.
        /// 모델(SpaceSuit)은 쓰지 않는다 — 에셋 로딩 없이 물리만 본다.
        /// </summary>
        (RemoteAvatar Avatar, PlayerRig Rig, Transform Source) Spawn(Vector3 at, bool mine = false)
        {
            var source = new GameObject("서버가 보낸 자리").transform;
            source.position = at;
            _spawned.Add(source.gameObject);

            var rig = PlayerRig.Create("검증용", at, Quaternion.identity,
                                       Color.white, Color.gray, useModel: false);
            _spawned.Add(rig.gameObject);

            return (RemoteAvatar.Attach(rig, source, mine), rig, source);
        }

        [Test]
        public void LocalRiderFollowsServerWithoutWalkingOrColliding()
        {
            var (avatar, rig, source) = Spawn(Vector3.zero, mine: true);
            rig.SetRemoteRiding(true);
            source.position = new Vector3(2f, 5f, 0f);
            avatar.AddCorrection(Vector3.left * 10f);

            for (int i = 0; i < 20; i++)
            {
                source.position += Vector3.forward * 0.1f;
                rig.SetInput(new Metagame.Core.InputFrame { Move = Vector2.up, Jump = true });
                avatar.Follow();
                rig.Tick();
                Physics.Simulate(Time.fixedDeltaTime);
                Assert.Less(Vector3.Distance(rig.Hips.position, source.position), 0.01f);
            }

            Assert.IsTrue(rig.IsRiding, "서버의 탑승 상태가 보행·상호작용 판정에도 적용되어야 한다.");
            foreach (var body in rig.Doll.Bodies)
            {
                Assert.IsTrue(body.isKinematic);
                Assert.IsFalse(body.detectCollisions);
            }
        }

        [Test]
        public void LocalDismountRestoresPhysicsAndClearsSeatCorrection()
        {
            var (avatar, rig, source) = Spawn(Vector3.zero, mine: true);
            var balance = rig.Doll.Balance;
            var gravity = rig.Hips.useGravity;
            for (int i = 0; i < 3; i++)
            {
                rig.SetRemoteRiding(true);
                avatar.Follow();
                avatar.AddCorrection(Vector3.right * 10f);
                rig.SetRemoteRiding(false);
                source.position = new Vector3(3f + i, 1f, 0f);
                avatar.Follow();
                var exit = rig.Hips.position;
                avatar.Follow();

                Assert.IsFalse(rig.IsRiding);
                Assert.IsFalse(rig.Hips.isKinematic);
                Assert.IsTrue(rig.Hips.detectCollisions);
                Assert.AreEqual(gravity, rig.Hips.useGravity);
                Assert.AreSame(balance, rig.Doll.Balance);
                Assert.AreEqual(exit, rig.Hips.position, "탑승 중 쌓인 보정이 하차 후 적용되면 안 된다.");
            }
        }

        [Test]
        public void LocalRidePreservesDrivingInputAndRestoresCameraOnDisable()
        {
            var (avatar, rig, _) = Spawn(Vector3.zero, mine: true);
            rig.Cam = OrbitCamera.Create(rig.Hips.transform, Color.black, firstPerson: false);
            _spawned.Add(rig.Cam.gameObject);
            rig.SetInput(new Metagame.Core.InputFrame { Move = Vector2.up, Jump = true });
            rig.SetRemoteRiding(true);
            avatar.ApplyRidingState();
            rig.Tick();

            // 탑승 시점은 3인칭이다 (S15P21B202-441). 예전에는 조종석 1인칭이었다.
            Assert.IsFalse(rig.Cam.FirstPerson);
            Assert.IsTrue(rig.IsMounted);
            Assert.AreEqual(Vector2.up, rig.CurrentInput.Move);
            Assert.IsTrue(rig.CurrentInput.Jump, "서버로 보낼 상승 입력은 유지해야 한다.");

            avatar.enabled = false;
            // EditMode에서는 일반 MonoBehaviour의 OnDisable을 자동 호출하지 않는다.
            typeof(RemoteAvatar).GetMethod("OnDisable",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(avatar, null);
            Assert.IsFalse(rig.Hips.isKinematic);
            Assert.IsTrue(rig.Hips.detectCollisions);
            Assert.IsFalse(rig.Cam.FirstPerson);
        }

        /// <summary>PlayerRig 내부와 같은 간격으로 돌린다 — 프로젝트 Fixed Timestep 은 0.01초다.</summary>
        static void Run(RemoteAvatar avatar, float seconds)
        {
            var step = Time.fixedDeltaTime;
            for (var s = 0; s < Mathf.Max(1, Mathf.RoundToInt(seconds / step)); s++)
            {
                avatar.Follow();
                Physics.Simulate(step);
            }
        }

        [Test]
        public void 조종로직을멈춘다()
        {
            var (_, rig, _) = Spawn(Vector3.zero);

            // PlayerRig 는 매 물리 스텝마다 힘을 넣는다. 켜 둔 채로 두면
            // 서버가 보낸 위치와 계속 싸운다.
            Assert.IsFalse(rig.enabled, "원격 아바타에서 조종 로직이 그대로 돌고 있다.");
            Assert.IsFalse(rig.IsLocal, "원격 아바타가 내 캐릭터로 표시된다.");
        }

        [Test]
        public void 골반은세계에붙들려있지않다()
        {
            var (_, rig, _) = Spawn(Vector3.zero);

            // 인형을 세우는 조인트다. 골반을 우리가 직접 옮기므로 남아 있으면 서로 잡아당긴다.
            Assert.IsNull(rig.Doll.Balance, "골반을 세계에 묶는 조인트가 남아 있다.");
        }

        [Test]
        public void 팔다리가늘어지지않는다()
        {
            var (avatar, rig, _) = Spawn(Vector3.zero);
            var standing = rig.Doll.Head.position.y - rig.Doll.Hips.position.y;

            Run(avatar, 2f);

            var after = rig.Doll.Head.position.y - rig.Doll.Hips.position.y;
            Assert.Greater(after, standing * 0.7f,
                $"2초 뒤 머리가 골반 쪽으로 무너졌다 (처음 {standing:F2}m → {after:F2}m). " +
                "관절 용수철이 자세를 붙들지 못하고 있다.");
        }

        [Test]
        public void 골반은서버가보낸자리를따라간다()
        {
            var (avatar, rig, source) = Spawn(Vector3.zero);

            // 스냅 거리보다 짧게 옮긴다 — 보간해서 따라가야 하는 구간이다.
            source.position = new Vector3(1f, 0f, 0f);
            Run(avatar, 0.5f);

            Assert.Less((rig.Doll.Hips.position - source.position).magnitude, 0.1f,
                "골반이 서버가 보낸 자리까지 오지 못했다.");
        }

        [Test]
        public void 순간이동은보간하지않고스냅한다()
        {
            var (avatar, rig, source) = Spawn(Vector3.zero);

            // 리스폰·재접속처럼 한 번에 멀리 뛰는 경우. 사이를 이어 그리면
            // 몸이 맵을 가로질러 날아가는 그림이 된다.
            source.position = new Vector3(0f, 0f, 50f);
            avatar.Follow();

            Assert.Less((rig.Doll.Hips.position - source.position).magnitude, 0.01f,
                "먼 거리를 한 번에 옮기지 않고 보간했다.");
        }

        [Test]
        public void 순간이동해도자세가무너지지않는다()
        {
            var (avatar, rig, source) = Spawn(Vector3.zero);
            var standing = rig.Doll.Head.position.y - rig.Doll.Hips.position.y;

            source.position = new Vector3(0f, 0f, 50f);
            avatar.Follow();

            // 골반만 옮기고 팔다리를 두고 오면 몸이 늘어진다. Doll.Teleport 가
            // 처음 자세 그대로 통째로 옮기는지 본다.
            var after = rig.Doll.Head.position.y - rig.Doll.Hips.position.y;
            Assert.Greater(after, standing * 0.9f, "순간이동 뒤 자세가 무너졌다.");
            Assert.Less((rig.Doll.Head.position - source.position).magnitude, 3f,
                "머리가 골반을 따라오지 않고 원래 자리에 남았다.");
        }

        [Test]
        public void 판정에관여하지않는다()
        {
            var (_, rig, _) = Spawn(Vector3.zero);

            // 물리는 서버가 돈다. 여기 있는 몸은 그림일 뿐이라 아무것과도 부딪히면 안 된다.
            foreach (var body in rig.Doll.Bodies)
            {
                if (body == null) continue;
                Assert.IsFalse(body.detectCollisions, $"{body.name} 이 충돌을 잡고 있다.");
                Assert.AreEqual(GameLayers.RemotePlayer, body.gameObject.layer,
                    $"{body.name} 이 RemotePlayer 레이어가 아니다.");
            }
        }

        [Test]
        public void 팔잡기상태를받으면기존조인트가뻗는다()
        {
            var (avatar, rig, source) = Spawn(Vector3.zero);

            avatar.SetArmIntent(false, false);
            avatar.Follow();
            var rest = rig.Doll.Right.Shoulder.targetRotation;

            // 네트워크에는 골반 회전만 오므로, 원격 팔은 그 사람의 수평 몸 방향을 사용한다.
            source.rotation = Quaternion.Euler(0f, 90f, 0f);
            avatar.SetArmIntent(false, true);
            avatar.Follow();
            var reaching = rig.Doll.Right.Shoulder.targetRotation;

            Assert.Greater(Quaternion.Angle(rest, reaching), 5f,
                "오른쪽 잡기 intent가 기존 어깨 조인트 목표를 바꾸지 않았다.");
            Assert.AreEqual(220f, rig.Doll.Right.Shoulder.slerpDrive.positionSpring,
                            "뻗는 동안 어깨 조인트 스프링이 강화되지 않았다.");
            Assert.AreEqual(90f, rig.Doll.Right.Elbow.slerpDrive.positionSpring,
                            "뻗는 동안 팔꿈치 조인트 스프링이 강화되지 않았다.");

            avatar.SetArmIntent(false, false);
            avatar.Follow();

            Assert.Less(Quaternion.Angle(rest, rig.Doll.Right.Shoulder.targetRotation), 0.1f,
                "잡기 intent를 해제했는데 어깨 조인트가 기본 자세로 돌아오지 않았다.");
        }
        /// <summary>
        /// 기절한 남의 몸은 <b>서 있지 않다</b> (S15P21B202-683).
        ///
        /// <para><b>무엇을 막는 검증인가.</b> 남의 몸은 <c>rig.enabled = false</c> 라
        /// <c>PlayerRig.Tick()</c> 이 돌지 않는다 — 기절 자세를 만드는 그쪽 분기
        /// (<c>uprightBlend = 0</c> · <c>Ragdolled = true</c> · <c>Speed = 0</c>)가
        /// <b>한 번도 실행되지 않는다.</b> 그래서 <c>SetRemoteDowned</c> 가 플래그를
        /// 세워도 팔다리는 설정 때 걸어 둔 선 자세 드라이브가 계속 붙들고 있었다.</para>
        ///
        /// <para>그 결과가 2026-09-22 QA 신고다 — <i>기절했을 때 다른 사람한테는
        /// 일어서서 움직이는 것처럼 보임.</i> 골반은 서버 자리로 옮겨지는데 팔다리가
        /// 선 자세를 유지하므로 <b>선 채로 끌려다니는 그림</b>이 된다.</para>
        ///
        /// <para>이 파일의 다른 검증들이 <b>선 자세를 유지하는 것</b>을 고정하고
        /// 있으므로(그게 평소에 맞다), 여기서는 <b>기절했을 때만 반대로 되는지</b>를
        /// 본다. 두 검증이 함께 있어야 한쪽으로 기울지 않는다.</para>
        /// </summary>
        [Test]
        public void RemoteDownedBodyGoesLimpInsteadOfStandingUp()
        {
            var (avatar, rig, _) = Spawn(Vector3.zero);

            // 설정 직후에는 선 자세를 붙든다 — 평소 동작.
            avatar.Follow();
            var joint = rig.Doll.LeftLeg.Knee;
            Assert.Greater(joint.slerpDrive.positionSpring, 0f,
                "기절하지 않은 남의 몸은 선 자세를 붙들어야 한다");

            float standingSpring = joint.slerpDrive.positionSpring;
            var shin = rig.Doll.LeftLeg.Shin;
            bool limbGravityWhileStanding = shin.useGravity;

            // 서버가 기절을 알려 온다.
            rig.SetRemoteDowned(true);
            avatar.Follow();

            Assert.AreEqual(0f, joint.slerpDrive.positionSpring,
                "기절한 몸의 팔다리를 아직 선 자세로 당긴다 — 남의 화면에서 서 있게 된다");
            Assert.Greater(joint.slerpDrive.positionDamper, 0f,
                "감쇠까지 0 이면 골반이 움직일 때 팔다리가 떨린다");

            // 중력도 켜져야 처진다. 설정 때는 일부러 꺼 두었다(선 자세를 지키려고).
            Assert.IsFalse(limbGravityWhileStanding,
                "선 자세에서는 팔다리 중력이 꺼져 있어야 한다(전제 확인)");
            Assert.IsTrue(shin.useGravity,
                "기절했는데 팔다리 중력이 꺼져 있다 — 아래로 처지지 않는다");

            // **골반은 건드리지 않는다.** 네트워크가 옮기는 유일한 몸이다.
            Assert.IsTrue(rig.Hips.isKinematic, "골반이 kinematic 이 아니게 됐다");

            // 되살아나면 선 자세로 돌아온다. 돌아오지 않으면 살린 뒤에도 늘어져 있다.
            rig.SetRemoteDowned(false);
            avatar.Follow();

            Assert.AreEqual(standingSpring, joint.slerpDrive.positionSpring,
                "되살아났는데 팔다리가 늘어진 채다");
            Assert.IsFalse(shin.useGravity,
                "되살아났는데 팔다리 중력이 켜진 채다 — 자세가 무너진다");
        }

        /// <summary>
        /// 기절 중에는 <b>팔 자세를 다시 세우지 않는다</b> (S15P21B202-683).
        ///
        /// <para><c>ApplyArmPose</c> 는 매 틱 어깨·팔꿈치 드라이브를 선 자세 값으로
        /// 되돌린다. 기절 처리를 그 앞에 두기만 하면 <b>같은 틱에 되돌려진다</b> —
        /// 팔만 서 있는 몸이 된다.</para>
        /// </summary>
        [Test]
        public void ArmsStayLimpWhileDownedEvenAcrossManyTicks()
        {
            var (avatar, rig, _) = Spawn(Vector3.zero);
            rig.SetRemoteDowned(true);

            for (int i = 0; i < 5; i++) avatar.Follow();

            Assert.AreEqual(0f, rig.Doll.Left.Shoulder.slerpDrive.positionSpring,
                "어깨가 선 자세로 되돌려졌다 — ApplyArmPose 가 같은 틱에 덮는다");
            Assert.AreEqual(0f, rig.Doll.Left.Elbow.slerpDrive.positionSpring,
                "팔꿈치가 선 자세로 되돌려졌다");
        }
    }
}
