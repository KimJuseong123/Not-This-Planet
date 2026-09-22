using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metagame.Core;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 손으로 잡은 파츠와 손이 <b>실제로 이어져 있는가</b> (S15P21B202-686).
    ///
    /// <para><b>재서 알아낸 것.</b> 잡은 채 <b>73m 떨어져서도 파츠가 0.62m/s 로
    /// 끌렸다.</b> <c>PartPush.Gather</c> 가 "손으로 잡았으면 접촉·조준을 다시 보지
    /// 않는다" 였고, 손을 거리로 놓는 경로가 어디에도 없었다.</para>
    ///
    /// <para><b>처음 잰 것은 틀렸다.</b> 사람을 <c>Teleport</c> 로 옮겼는데 그것이
    /// 스스로 <c>ReleaseHand</c> 를 부른다 — <c>Gripping=False</c> 가 나와서 하마터면
    /// "거리로 놓고 있다" 로 읽을 뻔했다. <b>계측이 조건을 지우고 있었다.</b> 인형을
    /// 통째로 옮기는 방식으로 바꾸고서야 73m 가 드러났다.</para>
    /// </summary>
    public sealed class PartHaulGripTests
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

        PartPush MakeBoxOnGround(PartState state = PartState.Free)
        {
            var ground = new GameObject("지면");
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.AddComponent<BoxCollider>().size = new Vector3(400f, 1f, 400f);
            ground.GetComponent<Collider>().material = Prim.GroundPhysics;
            _spawned.Add(ground);

            var go = new GameObject("Part_Box") { layer = GameLayers.Part };
            go.transform.position = new Vector3(0f, 0.62f, 0f);
            _spawned.Add(go);
            go.AddComponent<BoxCollider>().size = new Vector3(2f, 1.2f, 2f);
            go.AddComponent<Rigidbody>().useGravity = true;
            go.AddComponent<Grabbable>();

            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            part.TryTransitionTo(state);

            var push = go.AddComponent<PartPush>();
            push.Initialize();
            UnityEngine.Physics.SyncTransforms();
            for (var i = 0; i < 80; i++) UnityEngine.Physics.Simulate(Step);
            return push;
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            var player = PlayerRig.Create("잡는 사람", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            typeof(PlayerRig).GetProperty("MoveWish").SetValue(player, Vector3.right);
            foreach (var body in player.Doll.Bodies) body.isKinematic = true;
            UnityEngine.Physics.SyncTransforms();
            return player;
        }

        /// <summary>
        /// <b><see cref="PlayerRig.Teleport"/> 를 쓰면 안 된다</b> — 그것이 스스로
        /// 두 손을 놓는다. 인형을 통째로 옮겨 "저기 가 있다" 만 만든다.
        /// </summary>
        static void MoveDoll(PlayerRig player, Vector3 delta)
        {
            foreach (var body in player.Doll.Bodies)
                body.transform.position += delta;
            UnityEngine.Physics.SyncTransforms();
        }

        static float HandGap(PlayerRig player, PartPush push)
        {
            var hand = player.RightHand.transform.position;
            return Vector3.Distance(hand, push.GetComponent<Collider>().ClosestPoint(hand));
        }

        (PartPush Push, PlayerRig Player) Hauling()
        {
            var push = MakeBoxOnGround();
            var player = MakePlayer(new Vector3(-1.4f, 0f, 0f));
            Assert.That(push.TryGrab(player, player.RightHand), Is.True, "잡지를 못했다.");
            // 0.2초 이상 밀어야 시작한다(기획 §13.1).
            for (var i = 0; i < 40; i++) push.Tick(Step);
            Assert.That(push.HaulerCount, Is.EqualTo(1), "잡았는데 미는 사람으로 안 세어진다.");
            return (push, player);
        }

        // --- 멀어지면 놓는다 -----------------------------------------------------------

        [Test]
        public void WalkingOutOfReachLetsGoOfThePart()
        {
            var (push, player) = Hauling();

            MoveDoll(player, Vector3.left * 3f);
            push.Tick(Step);
            Assert.That(player.RightHand.Gripping, Is.False,
                "손이 파츠에서 3m 넘게 떨어졌는데 아직 잡고 있다.");

            // 놓은 뒤 밀기가 끝나기까지는 §13.1 의 0.5초가 있다 — 그것이 없으면
            // 걸음이 한 틱 끊길 때마다 파츠가 덜컥거린다.
            for (var i = 0; i < Mathf.CeilToInt(PhysicsScale.PartPushReleaseSeconds / Step) + 10; i++)
                push.Tick(Step);
            Assert.That(push.HaulerCount, Is.Zero, "놓았는데 미는 사람으로 계속 세어진다.");
        }

        [Test]
        public void AFarAwayPlayerCannotDragThePart()
        {
            // 이것이 관측된 결함이다 — 73m 떨어져서도 0.62m/s 로 끌렸다.
            var (push, player) = Hauling();
            MoveDoll(player, Vector3.left * 70f);

            // §13.1 의 0.5초 유예와 관성이 지나가기를 기다린다. 재려는 것은
            // "계속 끌리는가" 이지 "즉시 멈추는가" 가 아니다.
            for (var i = 0; i < 400; i++) { push.Tick(Step); UnityEngine.Physics.Simulate(Step); }

            var before = push.transform.position;
            for (var i = 0; i < 100; i++) { push.Tick(Step); UnityEngine.Physics.Simulate(Step); }

            Assert.That(Vector3.Distance(push.transform.position, before), Is.LessThan(0.02f),
                "70m 떨어진 사람이 파츠를 끌고 있다.");
            Assert.That(push.HaulerCount, Is.Zero);
        }

        [Test]
        public void StayingWithinReachKeepsTheGrip()
        {
            // **정상적인 운반을 끊으면 안 된다.** 실제로 3초를 끄는 동안 손과 표면은
            // 0.16 ~ 0.83m 사이에 있었다(실측) — 그 범위에서는 절대 안 놓아야 한다.
            var (push, player) = Hauling();

            MoveDoll(player, Vector3.left * 0.5f);
            for (var i = 0; i < 60; i++) { push.Tick(Step); UnityEngine.Physics.Simulate(Step); }

            Assert.That(HandGap(player, push),
                Is.LessThan(PhysicsScale.PartPushHoldReach),
                "하네스가 이미 문턱을 넘었다 — 이 검사가 아무것도 안 지킨다.");
            Assert.That(player.RightHand.Gripping, Is.True, "닿는 거리인데 놓아 버렸다.");
            Assert.That(push.HaulerCount, Is.EqualTo(1));
        }

        [Test]
        public void TheReachIsGenerousEnoughForANormalHaul()
        {
            // 실측 최대치 0.83m 보다 넉넉해야 한다. EditMode 에서는 실제로 끄는
            // 동안의 간격을 못 재므로(인형이 파츠를 통과해 지나간다) 값은 일부러
            // 여유를 뒀다.
            Assert.That(PhysicsScale.PartPushHoldReach, Is.GreaterThan(0.9f),
                "정상 운반 중에 손을 놓칠 수 있다.");
            // 그래도 팔 길이의 몇 배 안이어야 "잡고 있다" 는 말이 된다.
            Assert.That(PhysicsScale.PartPushHoldReach, Is.LessThan(Tuning.ArmReach * 8f),
                "이 거리에서는 잡고 있다고 할 수 없다.");
        }

        [Test]
        public void LettingGoLeavesTheHandFreeToGrabAgain()
        {
            // **세지 않기만 하면 안 된다.** 손이 붙어 있는 것으로 남으면 다시 잡을
            // 수도, 다른 것을 잡을 수도 없다.
            var (push, player) = Hauling();
            MoveDoll(player, Vector3.left * 3f);
            push.Tick(Step);
            Assert.That(player.RightHand.Held, Is.Null, "손이 아직 파츠를 들고 있다.");

            MoveDoll(player, Vector3.right * 3f);
            Assert.That(push.TryGrab(player, player.RightHand), Is.True, "돌아왔는데 다시 못 잡는다.");
        }

        // --- 팔이 잡은 지점을 향한다 ----------------------------------------------------

        [Test]
        public void TheArmReachesForWhereItIsHoldingNotWhereYouLook()
        {
            // 예전에는 조준 방향(카메라 앞)으로만 뻗었다. 옆으로 걸으면 몸이 카메라를
            // 보므로 손이 파츠를 떠난다.
            var shoulder = new Vector3(0f, 1.2f, 0f);
            var grasp = new Vector3(0.3f, 1.2f, 0f);     // 오른쪽 옆에 파츠
            var aim = Vector3.forward;                    // 앞을 보고 있다

            var target = PlayerRig.HaulArmTarget(shoulder, grasp, aim);
            var toGrasp = (grasp - shoulder).normalized;
            Assert.That(Vector3.Dot((target - shoulder).normalized, toGrasp),
                Is.GreaterThan(0.99f), "팔이 잡은 지점이 아니라 다른 곳을 향한다.");
        }

        [Test]
        public void TheArmNeverStretchesPastItsLength()
        {
            // 팔 길이를 넘기면 뻗다 못해 뜯긴다 — 자석 손이 같은 이유로 같은 것을 한다.
            var shoulder = Vector3.zero;
            foreach (var far in new[] { 0.5f, 1.2f, 5f })
            {
                var target = PlayerRig.HaulArmTarget(shoulder, Vector3.right * far, Vector3.forward);
                Assert.That(Vector3.Distance(target, shoulder),
                    Is.LessThanOrEqualTo(Tuning.ArmReach + 1e-4f),
                    far + "m 떨어진 지점을 향해 팔이 뻗어 나갔다.");
            }
        }

        [Test]
        public void ANearbyGraspIsNotStretchedOutward()
        {
            // 팔 길이보다 가까우면 그 자리까지만 간다. 늘리면 손이 파츠를 파고든다.
            var shoulder = Vector3.zero;
            var grasp = Vector3.right * 0.1f;
            var target = PlayerRig.HaulArmTarget(shoulder, grasp, Vector3.forward);
            Assert.That(Vector3.Distance(target, grasp), Is.LessThan(1e-4f));
        }

        [Test]
        public void AGraspOnTopOfTheShoulderFallsBackToTheAim()
        {
            // 방향이 0 이면 FromToRotation 이 쓰레기 회전을 내놓는다.
            var shoulder = new Vector3(1f, 1f, 1f);
            var target = PlayerRig.HaulArmTarget(shoulder, shoulder, Vector3.forward);
            Assert.That(Vector3.Distance(target, shoulder + Vector3.forward * Tuning.ArmReach),
                Is.LessThan(1e-4f));
        }

        // --- 잡을 수 없는 파츠 ----------------------------------------------------------

        [Test]
        public void PlacedAndLockedPartsStillRefuseTheGrab()
        {
            // -667 에서 **고정못을 잡은 사람이 밀리지 않는 벽이 되는** 문제를 겪었다.
            // 잡을 수 있는 것은 Free 뿐이어야 한다.
            foreach (var state in new[] { PartState.Locked, PartState.Placed })
            {
                var push = MakeBoxOnGround(state);
                var player = MakePlayer(new Vector3(-1.4f, 0f, 0f));
                Assert.That(push.TryGrab(player, player.RightHand), Is.False,
                    state + " 파츠를 손으로 잡을 수 있다.");
                TearDown();
                SetUp();
            }
        }
    }
}
