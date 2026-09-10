using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 파츠 밀기 3인칭 카메라를 검증한다(기획 §10.2, S15P21B202-400).
    ///
    /// 카메라는 <c>LateUpdate</c> 로 도는 표현이라 EditMode 로 화면을 볼 수 없다.
    /// <b>상태와 전환을</b> 잠근다 — <see cref="DownedCameraTests"/> 와 같은 방식이다.
    ///
    /// <b>여기서 가장 중요한 것은 전환 조건이 <c>IsHauling</c> 이 아니라는 점이다.</b>
    /// 한두 명이 붙어 파츠가 꿈쩍하지 않을 때 3인칭으로 빠지면 "시점은 바뀌었는데
    /// 아무 일도 안 일어나는" 화면이 된다. S15P21B202-394 가 그 두 값을 가른 이유가
    /// 이것이고, 이 파일이 그 결정을 잠근다.
    /// </summary>
    public sealed class PartPushCameraTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();
            PartPush.All.Clear();
            PartSocket.All.Clear();

            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;

            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            Puff.Enabled = _puffWasEnabled;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PlayerRig.All.Clear();
            PartPush.All.Clear();
            PartSocket.All.Clear();
        }

        // --- 전환 조건 (§10.2) ----------------------------------------------

        [Test]
        public void WalkingAround_ThereIsNoPushView()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            Assert.IsFalse(player.Cam.PushView);
            Assert.IsFalse(player.Hauling);
        }

        [Test]
        public void HaulingStarts_TheCameraPullsBackToThirdPerson()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.Cam.FirstPerson = true;

            Haul(player, Vector3.right);

            Assert.IsTrue(player.Cam.PushView);
            Assert.IsFalse(player.Cam.FirstPerson, "§10.2 3인칭으로 전환한다.");
            Assert.AreEqual(PhysicsScale.PartPushCamDistance, player.Cam.Distance, 0.01f);
        }

        /// <summary>
        /// <b>이 검증이 이 이슈의 핵심이다.</b> 한두 명이 붙어 있을 때는 파츠가 움직이지
        /// 않으므로 시점이 바뀌면 안 된다. 그 판정은 <c>PartPush.IsMoving</c> 이고,
        /// 그것이 <see cref="PlayerRig.Hauling"/> 으로 온다.
        /// </summary>
        [Test]
        public void BracingWithoutMoving_LeavesTheCameraAlone()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.Cam.FirstPerson = true;

            // 밀려고 하지만 파츠가 움직이지 않는 상태 — 인원이 모자란다.
            player.SetHaulState(false, Vector3.right, new Vector3(1.5f, 0.5f, 0f));
            Frame(player, 0.5f);

            Assert.IsFalse(player.Cam.PushView);
            Assert.IsTrue(player.Cam.FirstPerson, "1인칭에 머문다.");
        }

        [Test]
        public void HaulingWithNoDirection_DoesNotSwitch()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            // 반대편에서 서로 밀어 방향이 상쇄된 순간.
            player.SetHaulState(true, Vector3.zero, new Vector3(1.5f, 0.5f, 0f));
            Frame(player, 0.2f);

            Assert.IsFalse(player.Hauling, "갈 곳이 없으면 미는 것이 아니다.");
            Assert.IsFalse(player.Cam.PushView);
        }

        [Test]
        public void HaulingEnds_ItGoesBackToWhatItWas()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.Cam.FirstPerson = true;
            var wasDistance = player.Cam.Distance;

            Haul(player, Vector3.right);
            Assert.IsTrue(player.Cam.PushView);

            player.SetHaulState(false, Vector3.zero, Vector3.zero);
            Frame(player, 0.1f);

            Assert.IsFalse(player.Cam.PushView);
            Assert.IsTrue(player.Cam.FirstPerson, "원래 1인칭이었다.");
            Assert.AreEqual(wasDistance, player.Cam.Distance, 0.01f, "거리도 되돌린다.");
        }

        /// <summary>
        /// §10.2 "밀기를 멈추거나 접촉이 끊기면 0.5초 뒤 1인칭으로 복귀한다".
        ///
        /// <b>카메라가 그 0.5초를 세지 않는다.</b> <c>PartPush</c> 가 이미
        /// <see cref="PhysicsScale.PartPushReleaseSeconds"/> 동안 인원을 유지하므로,
        /// 걸음을 멈춰도 <c>IsMoving</c> 이 0.5초간 참이다. 그 유예가 실제로 그 값인지
        /// 여기서 잰다 — 카메라에 타이머를 하나 더 두면 두 값이 어긋난다.
        /// </summary>
        [Test]
        public void TheHalfSecondGraceComesFromPartPush_NotTheCamera()
        {
            var push = MakePart();
            var haulers = ThreeHaulers(push);

            // 셋이 밀기 시작한다.
            for (var i = 0; i < 60; i++) push.Tick(0.01f);
            Assert.IsTrue(push.IsMoving, "셋이 밀고 있다.");

            // 걸음을 멈춘다. 0.5초 동안은 유지되어야 한다.
            for (var i = 0; i < haulers.Count; i++) SetWish(haulers[i], Vector3.zero);

            for (var i = 0; i < 40; i++) push.Tick(0.01f);   // 0.4초
            Assert.IsTrue(push.IsMoving, "0.5초 안에는 유지된다 — 카메라도 3인칭에 머문다.");

            for (var i = 0; i < 20; i++) push.Tick(0.01f);   // 합계 0.6초
            Assert.IsFalse(push.IsMoving, "0.5초를 넘기면 끝난다 — 그때 1인칭으로 돌아간다.");
        }

        // --- 카메라 중심 (§10.2) --------------------------------------------

        /// <summary>
        /// §10.2 "카메라 중심은 플레이어와 파츠 접촉 지점 사이이다".
        ///
        /// 초점이 사람 쪽에 더 가깝다는 것까지 잠근다 — 정확히 가운데로 두면 파츠가
        /// 화면 가운데를 차지해 미는 사람이 보이지 않는다.
        /// </summary>
        [Test]
        public void TheFocusMovesTowardTheGraspPoint_ButStaysNearerThePlayer()
        {
            Assert.Greater(PhysicsScale.PartPushCamFocusBias, 0f,
                "접촉 지점 쪽으로 조금이라도 옮겨야 §10.2 다.");
            Assert.Less(PhysicsScale.PartPushCamFocusBias, 0.5f,
                "가운데보다 사람 쪽이어야 미는 사람이 화면에 남는다.");

            var player = MakePlayerWithCamera(Vector3.zero);
            var grasp = new Vector3(4f, 0.5f, 0f);

            Haul(player, Vector3.right, grasp);

            // 카메라는 초점 뒤에 선다. 초점이 파츠 쪽으로 갔으면 카메라도 +x 로 밀린다.
            var withGrasp = player.Cam.transform.position.x;

            var far = MakePlayerWithCamera(new Vector3(40f, 0f, 0f));
            Haul(far, Vector3.right, new Vector3(40f, 0.5f, 0f));   // 접촉 지점 = 제자리
            var withoutGrasp = far.Cam.transform.position.x - 40f;

            Assert.Greater(withGrasp, withoutGrasp,
                "접촉 지점이 앞에 있으면 초점도 그쪽으로 간다.");
        }

        // --- 자유 시점과 밀기 방향 (§10.2) ----------------------------------

        /// <summary>
        /// §10.2 "마우스로 제한적인 자유 시점이 가능하되 <b>캐릭터는 기존 밀기 방향을
        /// 유지한다</b>".
        ///
        /// 카메라를 돌려도 이동 기준이 따라가지 않아야 한다. 매 틱 밀기 방향을 다시
        /// 읽게 만들면 순환이 된다 — 밀기 방향은 사람들의 이동 입력 합이고 그 입력이
        /// 이 기준으로 만들어지므로, A 만 눌러도 기준이 계속 왼쪽으로 돌아 제자리에서
        /// 빙빙 도는 조작이 된다. 그래서 <b>시작 순간에 한 번</b> 고정한다.
        /// </summary>
        [Test]
        public void TurningTheCamera_DoesNotTurnTheHaulBasis()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            Haul(player, Vector3.right);
            var basis = player.HaulBasisYaw;

            player.Cam.Steer(new Vector2(12f, 0f));   // 마우스로 옆을 본다
            Frame(player, 0.1f);

            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(basis, player.Cam.ViewYaw)), 0.5f,
                "카메라는 돌았다.");
            Assert.AreEqual(basis, player.HaulBasisYaw, 0.01f,
                "그런데 이동 기준은 그대로다 — §10.2.");
        }

        /// <summary>밀기 방향이 매 틱 조금 흔들려도 기준은 처음 값을 붙든다.</summary>
        [Test]
        public void TheHaulBasisIsFixedAtTheStart_NotFollowedEveryTick()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            Haul(player, Vector3.right);
            var basis = player.HaulBasisYaw;

            // 합산 방향이 옆으로 돌아간다(다른 사람들이 방향을 틀었다).
            player.SetHaulState(true, Vector3.forward, new Vector3(0f, 0.5f, 1.5f));
            Frame(player, 0.2f);

            Assert.AreEqual(basis, player.HaulBasisYaw, 0.01f,
                "밀기가 이어지는 동안 기준은 바뀌지 않는다.");
        }

        [Test]
        public void StartingToHaulAgain_PicksUpTheNewDirection()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            Haul(player, Vector3.right);
            var first = player.HaulBasisYaw;

            player.SetHaulState(false, Vector3.zero, Vector3.zero);
            Frame(player, 0.1f);
            Haul(player, Vector3.forward);

            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(first, player.HaulBasisYaw)), 1f,
                "다시 시작하면 그때의 방향을 기준으로 잡는다.");
        }

        /// <summary>§10.2 "<b>제한적인</b> 자유 시점".</summary>
        [Test]
        public void TheFreeLookIsClampedAroundTheHaulDirection()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            Haul(player, Vector3.right);

            // 뒤를 보려고 크게 돌린다.
            for (var i = 0; i < 40; i++)
            {
                player.Cam.Steer(new Vector2(20f, 0f));
                player.SyncPushCamera(Step);
            }

            var offset = Mathf.Abs(Mathf.DeltaAngle(player.Cam.PushYawCenter,
                                                    player.Cam.ViewYaw));

            Assert.LessOrEqual(offset, PhysicsScale.PartPushCamYawRangeDegrees + 0.5f,
                "한계를 넘어 뒤를 보면 무엇을 어디로 밀고 있는지 화면에서 사라진다.");
        }

        // --- 다른 시야와의 우선순위 ------------------------------------------

        /// <summary>
        /// 기절하면 밀기 시야를 <b>먼저 끈다.</b> 저장 슬롯(<c>savedDistance</c> 등)을
        /// 세 모드가 공유하므로, 겹쳐 켜면 구조된 뒤 밀기 거리로 돌아간다.
        /// </summary>
        [Test]
        public void GoingDownWhileHauling_RestoresTheWalkingCamera()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.Cam.FirstPerson = true;
            var wasDistance = player.Cam.Distance;

            Haul(player, Vector3.right);
            Assert.IsTrue(player.Cam.PushView);

            player.Cam.SetDownedView(true, player.Doll.Head.transform);
            Assert.IsFalse(player.Cam.PushView, "밀기 시야가 먼저 꺼진다.");

            player.Cam.SetDownedView(false);

            Assert.IsTrue(player.Cam.FirstPerson, "걷던 시점으로 돌아온다.");
            Assert.AreEqual(wasDistance, player.Cam.Distance, 0.01f,
                "밀기 거리(5.6)가 아니라 걷던 거리다.");
        }

        [Test]
        public void MountingWhileHauling_RestoresTheWalkingCamera()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.Cam.FirstPerson = true;
            var wasDistance = player.Cam.Distance;

            Haul(player, Vector3.right);

            player.Cam.SetVehicleView(true);
            Assert.IsFalse(player.Cam.PushView);

            player.Cam.SetVehicleView(false);

            Assert.AreEqual(wasDistance, player.Cam.Distance, 0.01f);
        }

        /// <summary>기절·탑승 중에는 밀기 시야로 들어가지 않는다.</summary>
        [Test]
        public void WhileDowned_ThePushViewDoesNotTakeOver()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            player.Cam.SetDownedView(true, player.Doll.Head.transform);
            player.Cam.SetPushView(true, Vector3.right);

            Assert.IsFalse(player.Cam.PushView);
            Assert.IsTrue(player.Cam.DownedView);
        }

        [Test]
        public void WhileMounted_ThePushViewDoesNotTakeOver()
        {
            var player = MakePlayerWithCamera(Vector3.zero);

            player.Cam.SetVehicleView(true);
            player.Cam.SetPushView(true, Vector3.right);

            Assert.IsFalse(player.Cam.PushView);
            Assert.IsTrue(player.Cam.VehicleView);
        }

        // --- 서버가 참가자에게 보내는 경로 ------------------------------------

        /// <summary>
        /// 참가자 화면은 파츠를 스스로 볼 수 없다 — 그쪽 파츠는 kinematic 이고
        /// <c>PartPush.CanPush</c> 가 false 다. 서버가 보낸 값이 그대로 카메라를
        /// 움직이는지 잠근다. <see cref="PlayerRig.SetHaulState"/> 가 그 입구다.
        /// </summary>
        [Test]
        public void AParticipantSwitchesFromTheServerState()
        {
            var player = MakePlayerWithCamera(Vector3.zero);
            player.Cam.FirstPerson = true;

            // NetPlayer.LateUpdate 가 하는 일과 같다.
            player.SetHaulState(true, Vector3.right, new Vector3(2f, 0.5f, 0f));
            Frame(player, 0.1f);

            Assert.IsTrue(player.Hauling);
            Assert.IsTrue(player.Cam.PushView, "서버가 보낸 값만으로 전환된다.");
            // Atan2(x, z) 이므로 +x 를 미는 기준은 yaw 90도다.
            Assert.AreEqual(90f, player.HaulBasisYaw, 1f, "방향도 함께 왔다.");
        }

        /// <summary>
        /// 참가자 화면에서 <c>PlayerRig.Tick</c> 이 서버 값을 덮지 않는다.
        /// 덮으면 파츠가 kinematic 이라 늘 "밀고 있지 않다" 가 되어 전환이 풀린다.
        /// </summary>
        [Test]
        public void OnAParticipant_TheLocalTickDoesNotOverwriteTheServerState()
        {
            var wasRemote = WorldEvents.RemoteDecides;
            try
            {
                WorldEvents.RemoteDecides = true;

                var player = MakePlayerWithCamera(Vector3.zero);
                player.SetHaulState(true, Vector3.right, new Vector3(2f, 0.5f, 0f));

                player.Tick();   // 참가자 화면의 물리 스텝

                Assert.IsTrue(player.Hauling, "서버 값이 살아 있어야 한다.");
            }
            finally
            {
                WorldEvents.RemoteDecides = wasRemote;
            }
        }

        // --- 배선 (유형 A 방지) ----------------------------------------------

        /// <summary>
        /// <b>S15P21B202-394 가 노출한 값이 실제로 쓰이는지</b> 본다. 그 이슈는
        /// <c>IsMoving</c>·<c>HaulDirection</c> 을 만들었지만 읽는 런타임 코드가
        /// 0곳이었다 — QA 기록의 유형 A(만들었는데 아무도 안 씀)다.
        ///
        /// 여기서는 <c>PlayerRig.Tick</c> 이 <c>PartPush</c> 를 조회해
        /// <see cref="PlayerRig.Hauling"/> 을 채우는지 통합으로 확인한다.
        /// </summary>
        [Test]
        public void PlayerRigTick_ReadsThePartPushState()
        {
            var push = MakePart();
            var haulers = ThreeHaulers(push);

            for (var i = 0; i < 60; i++) push.Tick(0.01f);
            Assert.IsTrue(push.IsMoving, "먼저 셋이 밀고 있어야 한다.");

            var me = haulers[0];
            Assert.IsFalse(me.Hauling, "아직 Tick 이 읽지 않았다.");

            me.Tick();

            Assert.IsTrue(me.Hauling, "PlayerRig.Tick 이 PartPush 를 읽어야 한다.");
            Assert.Greater(Vector3.Dot(me.HaulDirection, Vector3.right), 0.9f);
            Assert.Greater(me.HaulGrasp.sqrMagnitude, 0f, "접촉 지점도 왔다.");
        }

        /// <summary>파츠에 참여하지 않은 사람은 밀고 있는 것이 아니다.</summary>
        [Test]
        public void SomeoneElsesHauling_DoesNotMoveMyCamera()
        {
            var push = MakePart();
            ThreeHaulers(push);

            var bystander = MakePlayerWithCamera(new Vector3(30f, 0f, 0f));

            for (var i = 0; i < 60; i++) push.Tick(0.01f);
            bystander.Tick();

            Assert.IsTrue(push.IsMoving, "저쪽에서는 밀고 있다.");
            Assert.IsFalse(bystander.Hauling, "나는 참여하지 않았다.");
            Assert.IsFalse(bystander.Cam.PushView);
        }

        // --- PartPush 이음새 -------------------------------------------------

        [Test]
        public void MovingPartFor_FindsOnlyThePartIAmActuallyMoving()
        {
            var push = MakePart();
            var haulers = ThreeHaulers(push);

            for (var i = 0; i < 60; i++) push.Tick(0.01f);

            Assert.AreSame(push, PartPush.MovingPartFor(haulers[0]));
            Assert.IsTrue(push.Participates(haulers[0]));

            var stranger = MakePlayer(new Vector3(30f, 0f, 0f), Vector3.zero);
            Assert.IsNull(PartPush.MovingPartFor(stranger));
            Assert.IsFalse(push.Participates(stranger));
        }

        /// <summary>
        /// 접촉 지점은 파츠 표면이다 — 파츠 중심이 아니다. 중심을 쓰면 카메라 초점이
        /// 파츠 안쪽으로 들어가 화면이 파츠에 파묻힌다.
        /// </summary>
        [Test]
        public void TheGraspPointIsOnThePartSurface()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1.2f, 0f, 0f), Vector3.right);

            for (var i = 0; i < 60; i++) push.Tick(0.01f);

            var grasp = push.GraspPointFor(player);
            var center = push.transform.position;

            Assert.Less(grasp.x, center.x, "사람이 -x 쪽에 있으니 표면도 그쪽이다.");
            Assert.Greater(grasp.x, center.x - 1.5f, "그렇다고 파츠 밖으로 나가지 않는다.");
        }

        [Test]
        public void MovingPartFor_IgnoresAPartThatIsOnlyBracedByTwo()
        {
            var push = MakePart();
            var first = MakePlayer(new Vector3(-1.2f, 0f, 0.4f), Vector3.right);
            var second = MakePlayer(new Vector3(-1.2f, 0f, -0.4f), Vector3.right);

            // 손으로 잡는다. 몸밀기로 세우면 접촉 거리(0.6m) 안에 들어가야 하는데
            // 여기 위치는 잡기 기준이라 멀다 — 재려는 것은 인원 문턱이다.
            push.TryGrab(first, first.RightHand);
            push.TryGrab(second, second.RightHand);

            for (var i = 0; i < 60; i++) push.Tick(0.01f);

            Assert.AreEqual(2, push.HaulerCount);
            Assert.IsFalse(push.IsMoving);
            Assert.IsNull(PartPush.MovingPartFor(first), "둘이면 카메라도 바뀌지 않는다.");
        }

        // --- 리그 ------------------------------------------------------------

        /// <summary>밀기 상태를 넣고 카메라가 반응할 시간을 준다.</summary>
        void Haul(PlayerRig player, Vector3 direction, Vector3 grasp = default)
        {
            if (grasp == default)
                grasp = player.Hips.position + direction * 1.5f;

            player.SetHaulState(true, direction, grasp);
            Frame(player, 0.1f);
        }

        /// <summary>
        /// 프레임 단위 갱신.
        ///
        /// <b>실제 배선을 지나야 한다.</b> 예전에는 여기서 <c>SetPushView</c> 와
        /// <c>TickPushView</c> 를 직접 불렀는데, 그러면 하네스가 GameSession 을 흉내내는
        /// 꼴이 되어 <b>전환 조건을 true 로 바꿔도 22개가 전부 통과했다.</b>
        /// 지금은 소유자가 부르는 것과 같은 <see cref="PlayerRig.SyncPushCamera"/> 를
        /// 부른다 — GameSession 은 이 한 줄만 부른다.
        /// </summary>
        static void Frame(PlayerRig player, float seconds)
        {
            var steps = Mathf.CeilToInt(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                player.SyncPushCamera(Step);
                player.Cam.Tick();
            }
        }

        PlayerRig MakePlayerWithCamera(Vector3 foot)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);

            player.Cam = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            _spawned.Add(player.Cam.gameObject);

            UnityEngine.Physics.SyncTransforms();
            return player;
        }

        PlayerRig MakePlayer(Vector3 foot, Vector3 wish)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            SetWish(player, wish);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }

        static void SetWish(PlayerRig player, Vector3 wish)
        {
            typeof(PlayerRig).GetProperty("MoveWish")
                .SetValue(player, wish.sqrMagnitude > 1e-6f ? wish.normalized : Vector3.zero);
        }

        PartPush MakePart()
        {
            var go = new GameObject("Part_1") { layer = GameLayers.Part };
            go.transform.position = new Vector3(0f, 0.5f, 0f);
            _spawned.Add(go);

            go.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 1f);
            go.AddComponent<Rigidbody>().useGravity = false;

            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            part.TryTransitionTo(PartState.Free);
            part.TryTransitionTo(PartState.Carried);
            part.TryTransitionTo(PartState.Placed);

            var push = go.AddComponent<PartPush>();
            push.Initialize();

            UnityEngine.Physics.SyncTransforms();
            return push;
        }

        /// <summary>파츠를 손으로 잡고 +x 로 미는 세 사람.</summary>
        List<PlayerRig> ThreeHaulers(PartPush push)
        {
            var people = new List<PlayerRig>
            {
                MakePlayerWithCamera(new Vector3(-1.2f, 0f, 0.8f)),
                MakePlayerWithCamera(new Vector3(-1.2f, 0f, 0f)),
                MakePlayerWithCamera(new Vector3(-1.2f, 0f, -0.8f)),
            };

            for (var i = 0; i < people.Count; i++)
            {
                SetWish(people[i], Vector3.right);
                push.TryGrab(people[i], people[i].RightHand);
            }

            return people;
        }
    }
}
