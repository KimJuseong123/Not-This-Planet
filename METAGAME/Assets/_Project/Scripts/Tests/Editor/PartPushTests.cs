using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 파츠 수동 밀기를 검증한다(기획 §13, S15P21B202-250).
    ///
    /// <b>가장 미묘한 부분은 운석과의 간섭이다.</b> §13.2 의 속도표를 맞추려면 속도를
    /// 몰아야 하는데, 그대로 몰면 S15P21B202-247 이 만든 운석 충격이 지워지고 §13.2 의
    /// 1초 감쇠가 날아간 파츠를 붙잡는다. 검증의 절반이 그것을 잠근다.
    /// </summary>
    public sealed class PartPushTests
    {
        const float Step = 0.01f;   // 프로젝트 고정 시간 간격

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

        // --- 표 (§13.2) ---------------------------------------------------

        /// <summary>
        /// 표가 <b>선형이 아니다</b> — 1·2·3명은 0.4씩 오르는데 4명은 0.3만 오른다.
        /// 식으로 바꾸면 기획이 정한 값이 아니라 내가 정한 값이 된다.
        /// </summary>
        [Test]
        public void TheSpeedTableMatchesTheDesign()
        {
            Assert.AreEqual(0f, PartPushTable.SpeedFor(0), 1e-3f);
            Assert.AreEqual(0.4f, PartPushTable.SpeedFor(1), 1e-3f);
            Assert.AreEqual(0.8f, PartPushTable.SpeedFor(2), 1e-3f);
            Assert.AreEqual(1.2f, PartPushTable.SpeedFor(3), 1e-3f);
            Assert.AreEqual(1.5f, PartPushTable.SpeedFor(4), 1e-3f);
        }

        [Test]
        public void MorePushersThanTheTable_ClampToTheLastRow()
        {
            Assert.AreEqual(1.5f, PartPushTable.SpeedFor(7), 1e-3f,
                "표에 없는 값을 만들어내지 않는다.");
        }

        // --- 시작과 종료 (§13.1) -------------------------------------------

        [Test]
        public void WalkingIntoThePartForTwoTenths_StartsPushing()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, PhysicsScale.PartPushIntentSeconds + Step);

            Assert.AreEqual(1, push.PusherCount);
            Assert.IsTrue(push.IsPushed);
            Assert.Greater(Vector3.Dot(push.PushDirection, Vector3.right), 0.9f);
            Assert.IsNotNull(player);
        }

        /// <summary>§13.1 "스쳐 지나가는 접촉으로 발동하지 않도록".</summary>
        [Test]
        public void BrushingPast_DoesNotStart()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, PhysicsScale.PartPushIntentSeconds - 0.05f);

            Assert.AreEqual(0, push.PusherCount);
        }

        /// <summary>§13.1 "당기기는 제공하지 않으며 반대편으로 이동해 반대 방향으로 민다".</summary>
        [Test]
        public void WalkingAwayIsNotPulling()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.left);

            Push(push, 1f);

            Assert.AreEqual(0, push.PusherCount, "멀어지는 입력은 밀기가 아니다.");
        }

        [Test]
        public void StandingTooFarAway_DoesNotStart()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-6f, 0f, 0f), Vector3.right);

            Push(push, 1f);

            Assert.AreEqual(0, push.PusherCount);
        }

        [Test]
        public void StoppingTheInput_EndsAfterHalfASecond()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, PhysicsScale.PartPushIntentSeconds + Step);
            Assert.AreEqual(1, push.PusherCount);

            SetWish(player, Vector3.zero);

            Push(push, PhysicsScale.PartPushReleaseSeconds - 0.1f);
            Assert.AreEqual(1, push.PusherCount, "0.5초 안에는 유지된다(§13.1).");

            Push(push, 0.2f);
            Assert.AreEqual(0, push.PusherCount);
        }

        [Test]
        public void LosingContact_EndsAfterHalfASecond()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, PhysicsScale.PartPushIntentSeconds + Step);
            Assert.AreEqual(1, push.PusherCount);

            Teleport(player, new Vector3(-8f, 0f, 0f));

            Push(push, PhysicsScale.PartPushReleaseSeconds + 0.1f);
            Assert.AreEqual(0, push.PusherCount);
        }

        // --- 속도 (§13.2) -------------------------------------------------

        [Test]
        public void OnePusherReachesFourTenths()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, 3f);

            Assert.AreEqual(0.4f, Horizontal(push).magnitude, 0.05f);
        }

        [Test]
        public void TwoPushersReachEightTenths()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0.4f), Vector3.right);
            MakePlayer(new Vector3(-1f, 0f, -0.4f), Vector3.right);

            Push(push, 3f);

            Assert.AreEqual(2, push.PusherCount);
            Assert.AreEqual(0.8f, Horizontal(push).magnitude, 0.06f);
        }

        /// <summary>§13.2 "서로 다른 방향으로 밀면 힘 벡터를 합산한다".</summary>
        [Test]
        public void PushingFromTwoSides_SumsTheVectors()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);
            MakePlayer(new Vector3(0f, 0f, -1f), Vector3.forward);

            Push(push, PhysicsScale.PartPushIntentSeconds + Step);

            Assert.AreEqual(2, push.PusherCount);

            var direction = push.PushDirection;
            Assert.Greater(direction.x, 0.5f, "두 방향이 섞여야 한다.");
            Assert.Greater(direction.z, 0.5f);
        }

        /// <summary>§13.2 "아무도 밀지 않으면 약 1초 동안 관성이 줄어든 뒤 정지".</summary>
        [Test]
        public void WithNobodyPushing_ItCoastsToAStop()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, 3f);
            var moving = Horizontal(push).magnitude;
            Assert.Greater(moving, 0.3f);

            Teleport(player, new Vector3(-20f, 0f, 0f));
            Push(push, PhysicsScale.PartPushReleaseSeconds + 0.1f);

            Push(push, 1f);
            Assert.Less(Horizontal(push).magnitude, moving * 0.5f, "1초쯤에 많이 줄어야 한다.");

            Push(push, 4f);
            Assert.AreEqual(0f, Horizontal(push).magnitude, 0.02f, "정지한다.");
        }

        // --- 운석과의 간섭 (§13.2 마지막 줄) --------------------------------

        /// <summary>
        /// §13.2 "운석 충격은 플레이어의 밀기보다 <b>훨씬 강한 힘</b>을 적용한다".
        /// 속도를 대입하면 그 충격이 지워진다.
        /// </summary>
        [Test]
        public void PushingDoesNotHoldBackAPartTheMeteorSentFlying()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, 1f);   // 밀기 시작

            var body = push.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(9f, 0f, 0f);   // 운석에 맞았다

            Push(push, 0.1f);

            Assert.AreEqual(9f, Horizontal(push).magnitude, 0.1f,
                "미는 손이 날아가는 파츠를 붙잡으면 안 된다.");
        }

        /// <summary>
        /// 감쇠를 무조건 걸면 운석에 날아간 파츠도 1초 만에 멈춘다. §13.2 가 말하는
        /// 관성은 <b>밀어서 생긴 것</b>이다.
        /// </summary>
        [Test]
        public void TheOneSecondDampingDoesNotCatchAMeteorHit()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, 1f);
            Teleport(player, new Vector3(-20f, 0f, 0f));
            Push(push, PhysicsScale.PartPushReleaseSeconds + 0.1f);

            var body = push.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(9f, 0f, 0f);   // 운석에 맞았다

            Push(push, 1f);

            Assert.AreEqual(9f, Horizontal(push).magnitude, 0.1f,
                "밀기 감쇠가 운석 충격을 삼키면 안 된다.");
        }

        /// <summary>밀기가 파츠를 <b>위로</b> 띄우지 않는다. 미는 것은 수평이다.</summary>
        [Test]
        public void PushingLeavesTheVerticalSpeedAlone()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            var body = push.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(0f, -3f, 0f);   // 떨어지는 중

            Push(push, 1f);

            Assert.AreEqual(-3f, body.linearVelocity.y, 0.01f);
        }

        // --- 밀 수 있는 상태 -----------------------------------------------

        [Test]
        public void ALockedPartDoesNotMove()
        {
            var push = MakePart(PartState.Locked);
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, 2f);

            Assert.IsFalse(push.CanPush, "고정쇠로 박혀 있다.");
            Assert.AreEqual(0, push.PusherCount);
        }

        [Test]
        public void ACarriedPartDoesNotMove()
        {
            var push = MakePart(PartState.Carried);
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, 2f);

            Assert.IsFalse(push.CanPush, "견인 빔이 잡고 있다.");
        }

        /// <summary>
        /// 접합된 파츠를 미는 것은 <b>탑 전체를 미는 것</b>이다. 기획 판단이 필요해
        /// 이 이슈에서 제외했다 — 이 검증이 그 결정을 잠근다.
        /// </summary>
        [Test]
        public void AWeldedPartIsOutOfScopeForNow()
        {
            var push = MakePart(PartState.Welded);
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, 2f);

            Assert.IsFalse(push.CanPush);
            Assert.AreEqual(0, push.PusherCount);
        }

        [Test]
        public void APlacedPartMoves()
        {
            var push = MakePart(PartState.Placed);
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Push(push, 1f);

            Assert.IsTrue(push.CanPush, "§13 이 노리는 상태다.");
            Assert.AreEqual(1, push.PusherCount);
        }

        [Test]
        public void OneOrTwoGrabbersHoldThePartWithoutMovingIt()
        {
            var push = MakePart();
            var first = MakePlayer(new Vector3(1.2f, 0f, 0f), Vector3.zero);
            var second = MakePlayer(new Vector3(1.2f, 0f, 0.8f), Vector3.zero);

            Assert.IsTrue(push.TryGrab(first, first.RightHand));
            Assert.IsTrue(push.TryGrab(second, second.RightHand));

            Push(push, 1f);

            Assert.AreEqual(2, push.PullerCount);
            Assert.IsFalse(push.IsPulled);
            Assert.AreEqual(0f, Horizontal(push).magnitude, 0.02f);
        }

        [Test]
        public void BothHandsOfOnePlayerCountAsOneGrabber()
        {
            var push = MakePart();
            var first = MakePlayer(new Vector3(1.2f, 0f, -0.4f), Vector3.zero);
            var second = MakePlayer(new Vector3(1.2f, 0f, 0.4f), Vector3.zero);

            Assert.IsTrue(push.TryGrab(first, first.LeftHand));
            Assert.IsTrue(push.TryGrab(first, first.RightHand));
            Assert.IsTrue(push.TryGrab(second, second.RightHand));

            Push(push, 1f);

            Assert.AreEqual(2, push.PullerCount);
            Assert.IsFalse(push.IsPulled);
        }

        [Test]
        public void LockedPartCannotBeGrabbedForPulling()
        {
            var push = MakePart(PartState.Locked);
            var player = MakePlayer(new Vector3(1.2f, 0f, 0f), Vector3.zero);

            Assert.IsFalse(push.TryGrab(player, player.RightHand));
            Push(push, 1f);

            Assert.AreEqual(0, push.PullerCount);
            Assert.IsFalse(push.IsPulled);
        }

        [Test]
        public void ThreeGrabbersPullThePartTowardTheirSide()
        {
            var push = MakePart();
            var first = MakePlayer(new Vector3(1.2f, 0f, -0.8f), Vector3.zero);
            var second = MakePlayer(new Vector3(1.2f, 0f, 0f), Vector3.zero);
            var third = MakePlayer(new Vector3(1.2f, 0f, 0.8f), Vector3.zero);

            Assert.IsTrue(push.TryGrab(first, first.RightHand));
            Assert.IsTrue(push.TryGrab(second, second.RightHand));
            Assert.IsTrue(push.TryGrab(third, third.RightHand));

            Push(push, 1f);

            Assert.AreEqual(3, push.PullerCount);
            Assert.IsTrue(push.IsPulled);
            Assert.Greater(Horizontal(push).x, 0.3f);
            Assert.Greater(Vector3.Dot(push.PullDirection, Vector3.right), 0.9f);
        }

        [Test]
        public void PullersOnOppositeSidesCancelOut()
        {
            var push = MakePart();
            var rightA = MakePlayer(new Vector3(1.2f, 0f, -0.8f), Vector3.zero);
            var rightB = MakePlayer(new Vector3(1.2f, 0f, 0.8f), Vector3.zero);
            var leftA = MakePlayer(new Vector3(-1.2f, 0f, -0.8f), Vector3.zero);
            var leftB = MakePlayer(new Vector3(-1.2f, 0f, 0.8f), Vector3.zero);

            Assert.IsTrue(push.TryGrab(rightA, rightA.RightHand));
            Assert.IsTrue(push.TryGrab(rightB, rightB.RightHand));
            Assert.IsTrue(push.TryGrab(leftA, leftA.RightHand));
            Assert.IsTrue(push.TryGrab(leftB, leftB.RightHand));

            Push(push, 1f);

            Assert.AreEqual(4, push.PullerCount);
            Assert.IsFalse(push.IsPulled);
            Assert.AreEqual(0f, Horizontal(push).magnitude, 0.02f);
        }

        // --- 밀 수 없는 사람 -----------------------------------------------

        [Test]
        public void ADownedPlayerDoesNotPush()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);
            player.GoDown("검증");

            Push(push, 2f);

            Assert.AreEqual(0, push.PusherCount, "쓰러진 사람은 밀지 않는다.");
        }

        // --- 리그 ---------------------------------------------------------

        static Vector3 Horizontal(PartPush push)
        {
            var v = push.GetComponent<Rigidbody>().linearVelocity;
            return new Vector3(v.x, 0f, v.z);
        }

        static void Push(PartPush push, float seconds)
        {
            var steps = Mathf.CeilToInt(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                UnityEngine.Physics.SyncTransforms();
                push.Tick(Step);
            }
        }

        /// <summary>
        /// 이동 의도를 넣는다. <c>MoveWish</c> 는 입력 처리에서만 채워지므로 검증은
        /// 리플렉션으로 직접 넣는다 — 조작 전체를 흉내내지 않는다.
        /// </summary>
        static void SetWish(PlayerRig player, Vector3 wish)
        {
            var property = typeof(PlayerRig).GetProperty("MoveWish");
            property.SetValue(player, wish);
        }

        static void Teleport(PlayerRig player, Vector3 foot)
        {
            player.Teleport(foot, Quaternion.identity);
            UnityEngine.Physics.SyncTransforms();
        }

        PartPush MakePart(PartState state = PartState.Placed)
        {
            var go = new GameObject("Part_1") { layer = GameLayers.Part };
            go.transform.position = new Vector3(0f, 0.5f, 0f);
            _spawned.Add(go);

            go.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 1f);

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;   // 떨어지는 것과 밀리는 것을 섞지 않는다

            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();

            if (state != PartState.Locked)
            {
                part.TryTransitionTo(PartState.Free);
                if (state != PartState.Free)
                {
                    part.TryTransitionTo(PartState.Carried);
                    if (state != PartState.Carried)
                    {
                        part.TryTransitionTo(PartState.Placed);
                        if (state == PartState.Welded) part.TryTransitionTo(PartState.Welded);
                    }
                }
            }

            // 상태 전이가 질량과 kinematic 을 바꾼다. 밀기는 그 뒤에 붙인다.
            var push = go.AddComponent<PartPush>();
            push.Initialize();

            UnityEngine.Physics.SyncTransforms();
            return push;
        }

        PlayerRig MakePlayer(Vector3 foot, Vector3 wish)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            SetWish(player, wish.normalized);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
