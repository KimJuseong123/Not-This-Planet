using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metagame.Core;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 1인 운반이 실제로 되는가, 그리고 파츠를 넘어뜨려 옮기는 것이 막히는가
    /// (S15P21B202-667).
    ///
    /// <para><b>관측:</b> "손으로 잡고 밀면 잘 안 밀리고, 안 잡은 채로 모듈 윗부분에
    /// 몸통박치기를 하면 모듈이 굴러가면서 밀린다."</para>
    ///
    /// <para><b>재서 알아낸 것.</b> 미는 속도는 손이든 몸이든 같았다 — 표의 상한
    /// (1인 0.6m/s)이 지배하기 때문이다. 다른 것은 <b>회전</b>이었다. 윗부분을 밀면
    /// 73.8도까지 기울어 굴러갔고, 그래서 더 잘 밀리는 것처럼 보였다.</para>
    ///
    /// <para>그리고 "잘 안 밀린다" 의 원인은 밀기가 아니라 <b>걷기</b>였다. 파츠는
    /// 0.6m/s 인데 미는 사람은 2.9m/s 로 걸어 <b>파츠를 두고 앞서 간다.</b>
    /// <c>Hand.BraceTo</c> 는 조인트를 만들지 않아 붙잡아 주지도 않는다.</para>
    /// </summary>
    public sealed class PartHaulFeelTests
    {
        const float Step = 0.01f;

        readonly List<GameObject> _spawned = new();
        UnityEngine.SimulationMode _wasMode;
        Vector3 _wasGravity;
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();
            PartPush.All.Clear();
            PartSocket.All.Clear();
            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;
            _wasGravity = UnityEngine.Physics.gravity;
            UnityEngine.Physics.gravity = new Vector3(0f, Tuning.Gravity, 0f);
            _wasMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = UnityEngine.SimulationMode.Script;
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            // 콜라이더를 지운 뒤 물리 씬을 맞춰야 뒤이어 도는 검증이 유령을 안 만난다.
            UnityEngine.Physics.SyncTransforms();
            UnityEngine.Physics.simulationMode = _wasMode;
            UnityEngine.Physics.gravity = _wasGravity;
            Puff.Enabled = _puffWasEnabled;
            PlayerRig.All.Clear();
            PartPush.All.Clear();
            PartSocket.All.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // --- 끙끙 끌린다: 미는 동안 사람이 느려진다 ------------------------------------

        [Test]
        public void ThePusherKeepsUpWithThePartAtEveryCrewSize()
        {
            // 파츠는 1명 0.6 · 4명 1.5m/s 다(기획 §13.2). 사람이 2.9m/s 로 걸으면
            // **파츠를 두고 앞서 가고**, 반대로 고정 배율로 눌러 버리면 넷이 밀 때
            // **전원이 뒤처져 접촉이 끊긴다.** 그래서 파츠 속도를 따라간다.
            for (var people = 1; people <= 4; people++)
            {
                var part = PartPushTable.SpeedFor(people);
                var walker = part * PhysicsScale.PartPushKeepUpMargin;
                Assert.That(walker, Is.GreaterThan(part),
                    $"{people}명일 때 미는 사람이 파츠({part:0.00})보다 느리다 — 접촉이 끊긴다.");
                Assert.That(walker, Is.LessThan(part * 1.4f),
                    $"{people}명일 때 미는 사람이 너무 앞선다 — 끙끙 끄는 느낌이 사라진다.");
            }
        }

        [Test]
        public void RunningWhilePushingIsNotAShortcut()
        {
            // 상한이라 달리기로 넘길 수 없다. 넘기면 미는 감각이 사라진다.
            var cap = PartPushTable.SpeedFor(1) * PhysicsScale.PartPushKeepUpMargin;
            Assert.That(cap, Is.LessThan(Tuning.RunSpeed),
                "뛰는 속도보다 상한이 높으면 상한이 아무 일도 안 한다.");
            Assert.That(cap, Is.LessThan(Tuning.WalkSpeed),
                "걷는 속도보다 상한이 높으면 혼자 밀 때 느려지지 않는다.");
        }

        [Test]
        public void TheSpeedCapFollowsHowManyArePushing()
        {
            var player = PlayerRig.Create("속도", Vector3.zero, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);

            // 참가자 화면에는 인원수가 안 온다 — 1인 값을 바닥으로 쓴다.
            player.SetHaulState(true, Vector3.right, Vector3.zero);
            Assert.That(player.HaulTopSpeed, Is.EqualTo(PartPushTable.SpeedFor(1)).Within(0.001f),
                "인원수를 모를 때 바닥값이 1인 속도가 아니다.");
        }

        [Test]
        public void TheSlowdownOnlyAppliesWhileActuallyHauling()
        {
            var player = PlayerRig.Create("테스트", Vector3.zero, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);

            Assert.That(player.Hauling, Is.False, "가만히 있는데 미는 중이다.");
            player.SetHaulState(true, Vector3.right, Vector3.zero);
            Assert.That(player.Hauling, Is.True);
            player.SetHaulState(false, Vector3.zero, Vector3.zero);
            Assert.That(player.Hauling, Is.False, "놓았는데 계속 느려진다.");
        }

        // --- 넘어뜨려 옮기지 못한다 -----------------------------------------------------

        PartPush MakeBoxOnGround()
        {
            var ground = new GameObject("지면");
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.AddComponent<BoxCollider>().size = new Vector3(120f, 1f, 120f);
            ground.GetComponent<Collider>().material = Prim.GroundPhysics;
            _spawned.Add(ground);

            var go = new GameObject("Part_Box") { layer = GameLayers.Part };
            go.transform.position = new Vector3(0f, 0.62f, 0f);
            _spawned.Add(go);
            go.AddComponent<BoxCollider>().size = new Vector3(2f, 1.2f, 2f);
            go.AddComponent<Rigidbody>().useGravity = true;

            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            part.TryTransitionTo(PartState.Free);   // Locked 는 kinematic 이라 안 밀린다

            var push = go.AddComponent<PartPush>();
            push.Initialize();
            UnityEngine.Physics.SyncTransforms();
            for (var i = 0; i < 80; i++) UnityEngine.Physics.Simulate(Step);
            return push;
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            var player = PlayerRig.Create("미는 사람", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            typeof(PlayerRig).GetProperty("MoveWish")
                .SetValue(player, Vector3.right);
            foreach (var body in player.Doll.Bodies) body.isKinematic = true;
            UnityEngine.Physics.SyncTransforms();
            return player;
        }

        /// <summary>
        /// 골반을 그 높이에 두고 <b>파츠 표면에서 살짝 띄워</b> 한 틱 민 뒤, 밀기가
        /// 집계됐는지 본다.
        ///
        /// <para><b>겹쳐 놓으면 안 된다.</b> 키네매틱 인형을 파츠 안에 밀어 넣으면
        /// 물리가 밀어내는 힘이 생겨 <b>밀기가 아닌 것을 재게 된다</b> — 처음에 그렇게
        /// 했더니 서서 미는데도 파츠가 20도 기울었다.</para>
        /// </summary>
        bool CountsAsPushing(float hipHeight)
        {
            var push = MakeBoxOnGround();
            var player = MakePlayer(new Vector3(-1.4f, 0f, 0f));
            var surface = push.transform.position.x - 1.0f;   // 상자 반폭
            var hips = new Vector3(surface - 0.22f, hipHeight, 0f);
            player.Teleport(hips - Vector3.up * Tuning.RideHeight, Quaternion.identity);
            UnityEngine.Physics.SyncTransforms();

            // 0.2초 이상 밀어야 시작한다(기획 §13.1).
            for (var i = 0; i < 40; i++) push.Tick(Step);
            return push.HaulerCount > 0;
        }

        [Test]
        public void ShovingAboveTheCentreOfMassIsNotPushing()
        {
            // 윗부분을 몸으로 밀면 토크가 생겨 파츠가 굴러갔다 — 기존 하네스로 재니
            // 1.52m 이동에 73.8도 기울었다. 파츠를 넘어뜨리는 것은 §13 의 밀기가
            // 아니라 §22.4 의 붕괴다.
            Assert.That(CountsAsPushing(1.15f), Is.False,
                "질량중심 위를 밀었는데 밀기로 집계됐다 — 넘어뜨려 옮길 수 있다.");
        }

        [Test]
        public void PushingAtNormalHipHeightStillCounts()
        {
            // **정상적으로 서서 미는 것은 그대로다.** 골반은 지면에서 0.30m 라
            // 파츠 질량중심(0.62m)보다 늘 아래다 — 막히는 것은 올라탔을 때뿐이다.
            Assert.That(CountsAsPushing(Tuning.RideHeight), Is.True,
                "서서 미는 것까지 막혔다 — 문턱이 너무 높다.");
        }

        // --- 고정못을 잡은 채로 몸을 비비는 것 ------------------------------------------

        /// <summary>
        /// 고정못을 잡으면 손이 <b>파츠 강체에 FixedJoint</b> 로 묶이고 박힌 파츠는
        /// kinematic 이라, 잡은 사람은 <b>밀리지 않는 벽</b>이 된다. 그 상태로 자유
        /// 파츠에 몸을 비비면 힘이 한쪽으로만 가서 아주 쉽게 밀려 나간다.
        /// </summary>
        [Test]
        public void AnchoredByAClampYouCannotShovePartsWithYourBody()
        {
            var push = MakeBoxOnGround();
            var player = MakePlayer(new Vector3(-1.4f, 0f, 0f));
            var surface = push.transform.position.x - 1.0f;
            var hips = new Vector3(surface - 0.22f, Tuning.RideHeight, 0f);
            player.Teleport(hips - Vector3.up * Tuning.RideHeight, Quaternion.identity);
            UnityEngine.Physics.SyncTransforms();

            for (var i = 0; i < 40; i++) push.Tick(Step);
            Assert.That(push.HaulerCount, Is.GreaterThan(0),
                "빈손으로는 밀 수 있어야 비교가 뜻을 갖는다.");

            // 고정못을 잡은 상태를 만든다.
            player.ClampGrabbedForTests = true;
            push.Tick(Step);
            for (var i = 0; i < 80; i++) push.Tick(Step);
            Assert.That(push.HaulerCount, Is.Zero,
                "고정못을 잡은 채로 몸을 비비는데 밀기로 집계된다.");
        }

        [Test]
        public void LettingGoOfTheClampMakesYouAPusherAgain()
        {
            var push = MakeBoxOnGround();
            var player = MakePlayer(new Vector3(-1.4f, 0f, 0f));
            var surface = push.transform.position.x - 1.0f;
            player.Teleport(new Vector3(surface - 0.22f, 0f, 0f), Quaternion.identity);
            UnityEngine.Physics.SyncTransforms();

            player.ClampGrabbedForTests = true;
            for (var i = 0; i < 40; i++) push.Tick(Step);
            Assert.That(push.HaulerCount, Is.Zero);

            player.ClampGrabbedForTests = false;
            for (var i = 0; i < 40; i++) push.Tick(Step);
            Assert.That(push.HaulerCount, Is.GreaterThan(0), "고정못을 놓았는데 못 민다.");
        }

    }
}
