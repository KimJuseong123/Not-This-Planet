using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 파츠 협동 밀기를 검증한다(기획 §13, S15P21B202-250 · -394).
    ///
    /// <b>이 파일은 하네스가 두 개다. 그 이유가 이 검증의 핵심이다.</b>
    ///
    /// <list type="number">
    /// <item><see cref="MakePart"/> — 물리는 돌리지만 <b>지면이 없고 파츠의 중력도
    /// 꺼져 있다.</b> 마찰이 개입하지 않으므로 <b>집계 규칙</b>(인원수·방향·유예 시간)과
    /// <b>운석 간섭 규칙</b>(밀기가 남의 속도를 덮지 않는가)만 드러난다.
    /// 이 하네스로 "무거운 파츠가 밀린다" 를 단정하면 안 된다.</item>
    /// <item><see cref="MakeGroundedPart"/> — 실제 지면 재질
    /// (<see cref="Prim.GroundPhysics"/>)과 중력을 켜고 매 스텝
    /// <c>Physics.Simulate</c> 를 돌린다. <b>이동 거리</b>로 단정한다.</item>
    /// </list>
    ///
    /// 둘로 나눈 것은 S15P21B202-394 의 결과다. 그전까지 이 파일의 검증 27개는 전부
    /// 첫 번째 하네스만 썼고, <c>Physics.Simulate</c> 를 한 번도 부르지 않았다.
    /// 그래서 실제로 확인한 것은 "<c>Drive()</c> 가 <c>linearVelocity</c> 에 써 넣은
    /// 값을 같은 필드에서 다시 읽으면 같은 값이 나온다" 뿐이었다. 마찰이 그 속도를
    /// 매 스텝 전부 지우는 것을 아무도 몰랐고, <b>협동 밀기는 한 번도 작동한 적이
    /// 없었다.</b>
    /// </summary>
    public sealed class PartPushTests
    {
        const float Step = 0.01f;   // 프로젝트 고정 시간 간격

        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;
        UnityEngine.SimulationMode _wasMode;
        Vector3 _wasGravity;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();
            PartPush.All.Clear();
            PartSocket.All.Clear();

            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;

            // 지면 하네스가 쓴다. 물리를 안 돌리는 검증에는 영향이 없다.
            _wasGravity = UnityEngine.Physics.gravity;
            UnityEngine.Physics.gravity = new Vector3(0f, Tuning.Gravity, 0f);
            _wasMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = UnityEngine.SimulationMode.Script;

            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _wasMode;
            UnityEngine.Physics.gravity = _wasGravity;
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

        /// <summary>
        /// 한두 명은 <b>힘이 0</b> 이다. 문턱 아래의 작은 힘을 주면 마찰이 지우는
        /// 헛일이 되고, 파츠 형태에 따라 우연히 밀리기도 한다.
        /// </summary>
        [Test]
        public void OneOrTwoPeopleGetNoForce()
        {
            const float mass = 500f;

            Assert.AreEqual(0f, PartPushTable.ForceFor(0, mass), 1e-3f);
            Assert.AreEqual(0f, PartPushTable.ForceFor(1, mass), 1e-3f);
            Assert.AreEqual(0f, PartPushTable.ForceFor(2, mass), 1e-3f);
            Assert.Greater(PartPushTable.ForceFor(3, mass), 0f);
            Assert.Greater(PartPushTable.ForceFor(4, mass),
                           PartPushTable.ForceFor(3, mass), "넷이면 셋보다 세다.");
        }

        /// <summary>무거운 파츠에는 더 큰 힘이 들어간다 — 질량이 의미를 갖는다.</summary>
        [Test]
        public void TheForceScalesWithMass()
        {
            var light = PartPushTable.ForceFor(3, 400f);
            var heavy = PartPushTable.ForceFor(3, 800f);

            Assert.AreEqual(light * 2f, heavy, 1f, "질량에 비례한다.");
        }

        [Test]
        public void MinPeopleToMoveIsThree()
        {
            Assert.AreEqual(3, PartPushTable.MinPeopleToMove);
            Assert.AreEqual(3, PartPush.MinPeopleToMove, "HUD 가 보는 값과 같아야 한다.");
        }

        /// <summary>
        /// <b>이 검증이 -394 의 핵심이다.</b> 세 명의 가속은 지면 마찰이 요구하는
        /// 값보다 커야 한다. 예전 값(인원 무관 1.2)은 그 4분의 1이었다.
        ///
        /// 마찰 문턱은 <c>μ·g</c> 이고 질량과 무관하다 — 그래서 745kg 파츠와 406kg
        /// 파츠가 같은 표를 쓸 수 있다.
        /// </summary>
        [Test]
        public void TheForceForThreeBeatsGroundFriction()
        {
            var needed = Prim.GroundPhysics.staticFriction * Mathf.Abs(Tuning.Gravity);

            Assert.AreEqual(4.75f, needed, 0.01f, "0.95 x 5. 이 값이 바뀌면 여유를 다시 재야 한다.");
            Assert.AreEqual(needed, PartPushTable.GroundFrictionAccel, 0.01f,
                "표가 지면 재질에서 값을 얻어야 재질을 고쳤을 때 따라온다.");

            // 셋이 내는 힘은 정지 마찰이 요구하는 힘의 FrictionMargin 배다.
            const float mass = 500f;
            Assert.AreEqual(mass * needed * PartPushTable.FrictionMargin,
                            PartPushTable.ForceFor(3, mass), 1f);

            Assert.Greater(PartPushTable.FrictionMargin, 2.05f,
                "PhysX 의 정지 마찰은 접촉 구성에 따라 이론값의 최대 2.05배를 요구했다.");
        }

        // --- 시작과 종료 (§13.1) — 집계 규칙만 본다 -------------------------

        [Test]
        public void WalkingIntoThePartForTwoTenths_StartsPushing()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Tick(push, PhysicsScale.PartPushIntentSeconds - Step * 2f);
            Assert.AreEqual(0, push.HaulerCount, "0.2초를 채우기 전이다.");

            Tick(push, Step * 4f);

            Assert.AreEqual(1, push.HaulerCount);
            Assert.AreEqual(1, push.PusherCount, "몸으로 밀었다.");
            Assert.AreEqual(0, push.PullerCount);
            Assert.IsTrue(push.IsHauling);
            Assert.Greater(Vector3.Dot(push.HaulDirection, Vector3.right), 0.9f);
            Assert.IsNotNull(player);
        }

        [Test]
        public void BrushingPast_DoesNotStart()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Tick(push, PhysicsScale.PartPushIntentSeconds * 0.5f);

            Assert.AreEqual(0, push.HaulerCount);
        }

        /// <summary>
        /// §13.1 "당기기는 제공하지 않으며 반대편으로 이동해 반대 방향으로 민다".
        /// <b>손이 비었으면</b> 파츠에서 멀어지는 입력은 밀기가 아니다.
        /// </summary>
        [Test]
        public void WalkingAwayWithEmptyHands_IsNotHauling()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.left);

            Tick(push, 1f);

            Assert.AreEqual(0, push.HaulerCount);
        }

        [Test]
        public void StandingTooFarAway_DoesNotStart()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-4f, 0f, 0f), Vector3.right);

            Tick(push, 1f);

            Assert.AreEqual(0, push.HaulerCount);
        }

        [Test]
        public void StoppingTheInput_EndsAfterHalfASecond()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Tick(push, 1f);
            Assert.AreEqual(1, push.HaulerCount);

            SetWish(player, Vector3.zero);

            Tick(push, PhysicsScale.PartPushReleaseSeconds * 0.5f);
            Assert.AreEqual(1, push.HaulerCount, "0.5초 안에는 유지된다(§13.1).");

            Tick(push, PhysicsScale.PartPushReleaseSeconds);
            Assert.AreEqual(0, push.HaulerCount);
        }

        [Test]
        public void LosingContact_EndsAfterHalfASecond()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);

            Tick(push, 1f);
            Assert.AreEqual(1, push.HaulerCount);

            Teleport(player, new Vector3(-20f, 0f, 0f));

            Tick(push, PhysicsScale.PartPushReleaseSeconds + 0.1f);
            Assert.AreEqual(0, push.HaulerCount);
        }

        [Test]
        public void ADownedPlayerDoesNotPush()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);
            player.GoDown("검증");

            Tick(push, 2f);

            Assert.AreEqual(0, push.HaulerCount, "쓰러진 사람은 밀지 않는다.");
        }

        /// <summary>§13.2 "서로 다른 방향으로 밀면 힘 벡터를 합산한다".</summary>
        [Test]
        public void PushingFromTwoSides_SumsTheVectors()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);
            MakePlayer(new Vector3(0f, 0f, -1f), Vector3.forward);

            Tick(push, PhysicsScale.PartPushIntentSeconds + Step);

            Assert.AreEqual(2, push.HaulerCount);

            var direction = push.HaulDirection;
            Assert.Greater(direction.x, 0.5f, "두 방향이 섞여야 한다.");
            Assert.Greater(direction.z, 0.5f);
        }

        /// <summary>
        /// 몸으로 밀 때 <b>비스듬히 서도</b> 세어진다.
        ///
        /// <see cref="PhysicsScale.PartPushAimDot"/> 를 0.5(60도)에서 0(직각)으로
        /// 완화했다. 셋이 정확히 정렬해야만 세어지던 것이 "셋이 모여도 안 된다" 는
        /// 보고의 한 원인이었다. 70도로 비스듬히 밀면 dot 0.342 로, 예전에는 빠졌다.
        /// </summary>
        [Test]
        public void PushingAtASteepAngle_StillCounts()
        {
            var push = MakePart();
            var player = MakePlayer(new Vector3(-1f, 0f, 0f),
                                    Quaternion.Euler(0f, 70f, 0f) * Vector3.right);

            Tick(push, 1f);

            Assert.AreEqual(1, push.HaulerCount, "70도로 비스듬해도 미는 것이다.");
        }

        /// <summary>
        /// 그래도 <b>멀어지는</b> 입력은 밀기가 아니다 — §13.1 "당기기는 제공하지 않으며".
        /// 이 검증이 관용의 한계를 잠근다. 0 보다 더 낮추면 이것이 깨진다.
        /// </summary>
        [Test]
        public void PushingPastARightAngle_DoesNotCount()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0f),
                       Quaternion.Euler(0f, 110f, 0f) * Vector3.right);

            Tick(push, 1f);

            Assert.AreEqual(0, push.HaulerCount, "직각을 넘으면 멀어지는 쪽이다.");
        }

        // --- 잡고 미는 것과 잡고 당기는 것 (S15P21B202-394) ------------------

        /// <summary>
        /// <b>이 검증이 -394 의 두 번째 핵심이다.</b>
        ///
        /// 예전에는 잡은 사람의 방향을 "파츠 중심에서 그 사람 쪽" 으로 정했다.
        /// 그러면 잡은 채로 <b>앞으로 밀면 파츠가 자기 쪽으로 왔다.</b>
        /// 방향을 이동 입력에서 얻으면 민 쪽으로 간다.
        /// </summary>
        [Test]
        public void GrabbingAndWalkingForward_HaulsThePartForward()
        {
            var push = MakePart();

            // 셋이 파츠의 -x 쪽에 서서 +x 로 민다. 예전 규칙이라면 방향이 -x 였다.
            var a = MakePlayer(new Vector3(-1.2f, 0f, -0.8f), Vector3.right);
            var b = MakePlayer(new Vector3(-1.2f, 0f, 0f), Vector3.right);
            var c = MakePlayer(new Vector3(-1.2f, 0f, 0.8f), Vector3.right);

            Assert.IsTrue(push.TryGrab(a, a.RightHand));
            Assert.IsTrue(push.TryGrab(b, b.RightHand));
            Assert.IsTrue(push.TryGrab(c, c.RightHand));

            Tick(push, 1f);

            Assert.AreEqual(3, push.HaulerCount);
            Assert.AreEqual(3, push.PullerCount, "손으로 잡아서 참여했다.");
            Assert.Greater(Vector3.Dot(push.HaulDirection, Vector3.right), 0.9f,
                "민 방향으로 가야 한다 — 사람이 서 있는 쪽이 아니다.");
        }

        /// <summary>같은 자리에서 뒤로 걸으면 파츠가 따라온다.</summary>
        [Test]
        public void GrabbingAndWalkingBackward_HaulsThePartBack()
        {
            var push = MakePart();

            var a = MakePlayer(new Vector3(-1.2f, 0f, -0.8f), Vector3.left);
            var b = MakePlayer(new Vector3(-1.2f, 0f, 0f), Vector3.left);
            var c = MakePlayer(new Vector3(-1.2f, 0f, 0.8f), Vector3.left);

            Assert.IsTrue(push.TryGrab(a, a.RightHand));
            Assert.IsTrue(push.TryGrab(b, b.RightHand));
            Assert.IsTrue(push.TryGrab(c, c.RightHand));

            Tick(push, 1f);

            Assert.AreEqual(3, push.HaulerCount,
                "손으로 잡았으면 멀어지는 입력도 밀기다 — 당기는 것이다.");
            Assert.Greater(Vector3.Dot(push.HaulDirection, Vector3.left), 0.9f);
        }

        /// <summary>잡고 가만히 서 있는 것은 힘을 쓰는 것이 아니다.</summary>
        [Test]
        public void GrabbingWithoutMoving_DoesNothing()
        {
            var push = MakePart();

            var a = MakePlayer(new Vector3(-1.2f, 0f, -0.8f), Vector3.zero);
            var b = MakePlayer(new Vector3(-1.2f, 0f, 0f), Vector3.zero);
            var c = MakePlayer(new Vector3(-1.2f, 0f, 0.8f), Vector3.zero);

            Assert.IsTrue(push.TryGrab(a, a.RightHand));
            Assert.IsTrue(push.TryGrab(b, b.RightHand));
            Assert.IsTrue(push.TryGrab(c, c.RightHand));

            Tick(push, 1f);

            Assert.AreEqual(0, push.HaulerCount);
            Assert.IsFalse(push.IsHauling);
            Assert.IsFalse(push.IsMoving);
        }

        /// <summary>손으로 잡은 사람과 몸으로 미는 사람이 같은 인원으로 합산된다.</summary>
        [Test]
        public void GrabbersAndBodyPushersAddUp()
        {
            var push = MakePart();

            var a = MakePlayer(new Vector3(-1.2f, 0f, -0.8f), Vector3.right);
            var b = MakePlayer(new Vector3(-1.2f, 0f, 0.8f), Vector3.right);
            var c = MakePlayer(new Vector3(-1f, 0f, 0f), Vector3.right);   // 몸으로만

            Assert.IsTrue(push.TryGrab(a, a.RightHand));
            Assert.IsTrue(push.TryGrab(b, b.RightHand));

            // 짧게 잰다. 힘이 들어가면 파츠가 밀려 나가고, 몸으로 미는 사람은 매 틱
            // 접촉을 다시 보므로 뒤에 남아 판정이 끊긴다 — 재려는 것은 합산 규칙이다.
            Tick(push, PhysicsScale.PartPushIntentSeconds + Step * 2f);

            Assert.AreEqual(3, push.HaulerCount, "둘이 잡고 하나가 몸으로 밀면 셋이다.");
            Assert.AreEqual(2, push.PullerCount);
            Assert.AreEqual(1, push.PusherCount);
            Assert.IsTrue(push.IsMoving);
            Assert.IsNotNull(c);
        }

        [Test]
        public void BothHandsOfOnePlayerCountAsOneHauler()
        {
            var push = MakePart();
            var first = MakePlayer(new Vector3(-1.2f, 0f, -0.4f), Vector3.right);
            var second = MakePlayer(new Vector3(-1.2f, 0f, 0.4f), Vector3.right);

            Assert.IsTrue(push.TryGrab(first, first.LeftHand));
            Assert.IsTrue(push.TryGrab(first, first.RightHand));
            Assert.IsTrue(push.TryGrab(second, second.RightHand));

            Tick(push, 1f);

            Assert.AreEqual(2, push.HaulerCount, "양손은 한 명이다.");
            Assert.AreEqual(2, push.PullerCount);
        }

        /// <summary>마주보고 밀면 힘이 상쇄된다.</summary>
        [Test]
        public void HaulersPushingAgainstEachOther_CancelOut()
        {
            var push = MakePart();

            var rightA = MakePlayer(new Vector3(1.2f, 0f, -0.8f), Vector3.left);
            var rightB = MakePlayer(new Vector3(1.2f, 0f, 0.8f), Vector3.left);
            var leftA = MakePlayer(new Vector3(-1.2f, 0f, -0.8f), Vector3.right);
            var leftB = MakePlayer(new Vector3(-1.2f, 0f, 0.8f), Vector3.right);

            Assert.IsTrue(push.TryGrab(rightA, rightA.RightHand));
            Assert.IsTrue(push.TryGrab(rightB, rightB.RightHand));
            Assert.IsTrue(push.TryGrab(leftA, leftA.RightHand));
            Assert.IsTrue(push.TryGrab(leftB, leftB.RightHand));

            Tick(push, 1f);

            Assert.AreEqual(4, push.HaulerCount, "네 명 다 힘을 쓰고 있다.");
            Assert.IsFalse(push.IsHauling, "방향이 상쇄되어 갈 곳이 없다.");
            Assert.IsFalse(push.IsMoving);
        }

        [Test]
        public void LockedPartCannotBeGrabbed()
        {
            var push = MakePart(PartState.Locked);
            var player = MakePlayer(new Vector3(-1.2f, 0f, 0f), Vector3.right);

            Assert.IsFalse(push.TryGrab(player, player.RightHand));

            Tick(push, 1f);

            Assert.AreEqual(0, push.PullerCount);
            Assert.AreEqual(0, push.HaulerCount);
        }

        // --- 밀려고 하는 것과 실제로 움직이는 것 ------------------------------
        //
        // 두 값을 가르는 이유는 기획 §10.2 의 밀기 카메라다. 3인칭 전환은 파츠가
        // **실제로 밀릴 때**만 걸려야 하므로 IsHauling 이 아니라 IsMoving 을 봐야 한다.
        //
        // 한두 명이 붙었을 때의 "버티는 중" 연출은 만들지 않기로 했다(2026-09-10
        // 기획 판단). 그래서 그 상태를 위한 별도 값은 두지 않는다 — 필요하면
        // IsHauling && !IsMoving 으로 언제든 물을 수 있다.

        [Test]
        public void OneOrTwoHaulers_TryButDoNotMoveIt()
        {
            var push = MakePart();
            MakePlayer(new Vector3(-1f, 0f, 0.4f), Vector3.right);
            MakePlayer(new Vector3(-1f, 0f, -0.4f), Vector3.right);

            Tick(push, 1f);

            Assert.AreEqual(2, push.HaulerCount);
            Assert.IsTrue(push.IsHauling, "밀려고 하고 있다.");
            Assert.IsFalse(push.IsMoving, "인원이 마찰을 못 이긴다.");
        }

        [Test]
        public void ThreeHaulers_ActuallyMoveIt_ByCount()
        {
            var push = MakePart();
            ThreeHaulers(push);

            Tick(push, 1f);

            Assert.AreEqual(3, push.HaulerCount);
            Assert.IsTrue(push.IsHauling);
            Assert.IsTrue(push.IsMoving, "밀기 카메라가 이 값을 본다.");
        }

        [Test]
        public void NobodyHauling_IsNeitherHaulingNorMoving()
        {
            var push = MakePart();

            Tick(push, 1f);

            Assert.IsFalse(push.IsHauling);
            Assert.IsFalse(push.IsMoving, "아무도 없으면 움직이지 않는다.");
        }

        // --- 실제로 움직이는가 — 지면·중력·Physics.Simulate ------------------

        /// <summary>
        /// 이 아래 넷이 -394 가 고친 것을 잠근다. <b>속도가 아니라 이동 거리로</b>
        /// 단정한다 — 마찰이 속도를 지우면 거리는 0 이 된다.
        /// </summary>
        [Test]
        public void OneHauler_DoesNotBudgeAHeavyPart()
        {
            Assert.Less(HaulOnGround(1), 0.1f, "혼자서는 무게를 못 이긴다.");
        }

        [Test]
        public void TwoHaulers_StillDoNotBudgeIt()
        {
            Assert.Less(HaulOnGround(2), 0.1f, "둘도 못 이긴다.");
        }

        [Test]
        public void ThreeHaulers_ActuallyMoveIt()
        {
            Assert.Greater(HaulOnGround(3), 1.5f,
                "셋이 모이면 마찰을 이긴다. 이것이 -394 의 목적이다.");
        }

        [Test]
        public void FourHaulers_MoveItFurtherThanThree()
        {
            var three = HaulOnGround(3);
            Wipe();
            var four = HaulOnGround(4);

            // 둘 다 0 에 가까우면 대소 비교만으로는 통과해 버린다 — 먼저 움직였는지 본다.
            Assert.Greater(three, 1.5f, "셋이 실제로 움직여야 비교가 뜻을 갖는다.");
            Assert.Greater(four, three * 1.1f, "표의 상한이 1.2 에서 1.5 로 오른다.");
        }

        /// <summary>
        /// 가장 무거운 파츠(승무원 거주 모듈, 745kg)를 <b>실제 콜라이더 구성으로</b>
        /// 세 명이 민다. 형태가 접촉면을 바꾸어 마찰 문턱을 흔들기 때문에 상자 하나로
        /// 재는 것으로는 부족하다 — Part_1 은 최하단이 좁아 문턱이 훨씬 낮다.
        /// </summary>
        [Test]
        public void TheHeaviestPart_MovesWithThree()
        {
            var spec = HeaviestSpec();
            Assert.AreEqual("Part_3", spec.Name, "가장 무거운 파츠가 바뀌었으면 표를 다시 재야 한다.");
            Assert.Greater(spec.Mass, 700f);

            Assert.Greater(HaulOnGround(3, spec), 1.5f,
                $"{spec.DisplayName} {spec.Mass:F0}kg 을 셋이 밀 수 있어야 한다.");
        }

        /// <summary>가장 무거운 파츠도 둘로는 안 된다 — 형태와 무관하게 문턱이 같다.</summary>
        [Test]
        public void TheHeaviestPart_DoesNotMoveWithTwo()
        {
            Assert.Less(HaulOnGround(2, HeaviestSpec()), 0.1f);
        }

        /// <summary>
        /// 접촉면이 가장 좁은 파츠도 둘로는 안 움직인다.
        ///
        /// Part_1 은 최하단 상자가 0.94 x 0.94 뿐이라 정지 마찰 문턱이 가장 낮다 —
        /// 가속 2.4 만 주어도 1.16m 밀렸다. 1·2명 가속을 0 으로 둔 이유가 이것이다.
        /// </summary>
        [Test]
        public void TheNarrowestFootedPart_DoesNotMoveWithTwo()
        {
            var spec = PartSpec.Find("Part_1");
            Assert.IsNotNull(spec);

            Assert.Less(HaulOnGround(2, spec), 0.1f,
                "이 파츠가 둘에 밀리면 1·2명 가속에 값을 준 것이다.");
        }

        // --- 실제 씬 구성 (콜라이더가 자식에 있다) ---------------------------

        /// <summary>
        /// <b>이 검증이 없어서 몸으로 미는 것이 죽어 있는 줄 몰랐다.</b>
        ///
        /// 실제 파츠는 루트에 콜라이더가 <b>하나도 없다</b> — `Stage1PuzzleBuilder` 가
        /// `PartSpec` 의 상자마다 자식 `Body_*` 를 만들어 붙인다. 그래서 예전의
        /// <c>GetComponent&lt;Collider&gt;()</c> 는 항상 null 이었고 <c>IsTouching</c> 이
        /// 늘 false 였다. 예전 검증은 콜라이더를 <b>루트에</b> 붙였기 때문에 통과했다.
        /// </summary>
        [Test]
        public void APartWithCollidersOnChildren_CanStillBeBodyPushed()
        {
            var push = MakeSceneShapedPart();
            MakePlayer(new Vector3(-1.4f, 0f, 0f), Vector3.right);

            Tick(push, PhysicsScale.PartPushIntentSeconds + Step * 4f);

            Assert.AreEqual(1, push.HaulerCount,
                "루트에 콜라이더가 없는 실제 구성에서도 접촉이 잡혀야 한다.");
            Assert.AreEqual(1, push.PusherCount, "몸으로 밀었다.");
        }

        /// <summary>
        /// 고정못 콜라이더는 접촉 판정에서 빠진다. 고정못은 파츠 밑면보다 아래로
        /// 내려가 있어 함께 세면 접촉 범위가 발밑까지 늘어난다.
        /// </summary>
        [Test]
        public void ClampCollidersDoNotCountAsThePartSurface()
        {
            var push = MakeSceneShapedPart(withClampFarAway: true);

            // 몸통에서는 멀고 고정못에서는 가까운 자리에 세운다.
            MakePlayer(new Vector3(-4.2f, 0f, 0f), Vector3.right);

            Tick(push, 1f);

            Assert.AreEqual(0, push.HaulerCount,
                "고정못이 파츠 표면으로 세어지면 4m 밖에서도 밀 수 있게 된다.");
        }

        /// <summary>
        /// 실제 씬처럼 <b>자식</b>에 몸통 콜라이더를 붙인 파츠. 루트에는 없다.
        /// </summary>
        PartPush MakeSceneShapedPart(bool withClampFarAway = false)
        {
            var spec = PartSpec.Find("Part_1");

            var root = new GameObject(spec.Name) { layer = GameLayers.Part };
            root.transform.position = new Vector3(0f, spec.Height * 0.5f, 0f);
            _spawned.Add(root);

            for (var i = 0; i < spec.Boxes.Length; i++)
            {
                var box = spec.Boxes[i];
                var body = new GameObject($"Body_{i}") { layer = GameLayers.Part };
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = box.CenterFromPartCenter(spec.Height);
                body.AddComponent<BoxCollider>().size = box.Size;
            }

            if (withClampFarAway)
            {
                // 고정못을 일부러 멀리 둔다 — 이것이 표면으로 세어지면 검증이 잡는다.
                var clamp = new GameObject("Clamp_0") { layer = GameLayers.Interactable };
                clamp.transform.SetParent(root.transform, false);
                clamp.transform.localPosition = new Vector3(-3.5f, -spec.Height * 0.5f, 0f);
                clamp.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
            }

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;

            var part = root.AddComponent<SpaceshipPart>();
            part.SetPlacedMass(spec.Mass);
            part.Initialize();
            TransitionTo(part, PartState.Free);   // Placed 는 조립 조인트가 붙잡는다

            var push = root.AddComponent<PartPush>();
            push.Initialize();

            UnityEngine.Physics.SyncTransforms();
            return push;
        }

        // --- 관성과 운석 (§13.2) — 속도 규칙만 본다 --------------------------

        /// <summary>§13.2 "아무도 밀지 않으면 약 1초 동안 관성이 줄어든 뒤 정지".</summary>
        [Test]
        public void WithNobodyPushing_ItCoastsToAStop()
        {
            var push = MakePart();
            var players = ThreeHaulers(push);

            Tick(push, 3f);
            var moving = Horizontal(push).magnitude;
            Assert.Greater(moving, 0.3f);

            // 손은 놓지 않고 걸음만 멈춘다 — 잡기는 거리로 끊기지 않는다.
            for (var i = 0; i < players.Count; i++)
                SetWish(players[i], Vector3.zero);

            Tick(push, PhysicsScale.PartPushReleaseSeconds + 0.1f);

            Tick(push, 1f);
            Assert.Less(Horizontal(push).magnitude, moving * 0.5f, "1초쯤에 많이 줄어야 한다.");

            Tick(push, 4f);
            Assert.AreEqual(0f, Horizontal(push).magnitude, 0.02f, "정지한다.");
        }

        /// <summary>
        /// §13.2 "운석 충격은 플레이어의 밀기보다 <b>훨씬 강한 힘</b>을 적용한다".
        /// 속도를 대입하면 그 충격이 지워진다.
        /// </summary>
        [Test]
        public void PushingDoesNotHoldBackAPartTheMeteorSentFlying()
        {
            var push = MakePart();
            ThreeHaulers(push);

            Tick(push, 1f);   // 밀기 시작

            var body = push.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(9f, 0f, 0f);   // 운석에 맞았다

            Tick(push, 0.1f);

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
            var players = ThreeHaulers(push);

            Tick(push, 1f);
            for (var i = 0; i < players.Count; i++)
                SetWish(players[i], Vector3.zero);
            Tick(push, PhysicsScale.PartPushReleaseSeconds + 0.1f);

            var body = push.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(9f, 0f, 0f);   // 운석에 맞았다

            Tick(push, 1f);

            Assert.AreEqual(9f, Horizontal(push).magnitude, 0.1f,
                "밀기 감쇠가 운석 충격을 삼키면 안 된다.");
        }

        /// <summary>밀기가 파츠를 <b>위로</b> 띄우지 않는다. 미는 것은 수평이다.</summary>
        [Test]
        public void PushingLeavesTheVerticalSpeedAlone()
        {
            var push = MakePart();
            ThreeHaulers(push);

            var body = push.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(0f, -3f, 0f);   // 떨어지는 중

            Tick(push, 1f);

            Assert.AreEqual(-3f, body.linearVelocity.y, 0.01f);
        }

        /// <summary>세 명이 밀어도 표의 상한을 넘지 않는다.</summary>
        [Test]
        public void ThreeHaulersStopAtTheTableSpeed()
        {
            var push = MakePart();
            ThreeHaulers(push);

            Tick(push, 4f);

            Assert.AreEqual(PartPushTable.SpeedFor(3), Horizontal(push).magnitude, 0.05f);
        }

        // --- 밀 수 있는 상태 -----------------------------------------------

        [Test]
        public void ALockedPartDoesNotMove()
        {
            var push = MakePart(PartState.Locked);
            ThreeHaulers(push);

            Tick(push, 2f);

            Assert.IsFalse(push.CanPush, "고정쇠로 박혀 있다.");
            Assert.AreEqual(0, push.HaulerCount);
        }

        [Test]
        public void ACarriedPartDoesNotMove()
        {
            var push = MakePart(PartState.Carried);
            ThreeHaulers(push);

            Tick(push, 2f);

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
            ThreeHaulers(push);

            Tick(push, 2f);

            Assert.IsFalse(push.CanPush);
            Assert.AreEqual(0, push.HaulerCount);
        }

        [Test]
        public void AFreePartCanBeHauled()
        {
            var push = MakePart(PartState.Free);
            ThreeHaulers(push);

            Tick(push, 1f);

            Assert.IsTrue(push.CanPush, "§13 이 노리는 상태다 — 고정못을 다 뽑은 파츠.");
            Assert.AreEqual(3, push.HaulerCount);
            Assert.IsTrue(push.IsMoving);
        }

        /// <summary>
        /// <b>조립대에 올린 파츠는 밀 수 없다.</b>
        ///
        /// 예전에는 <c>CanPush</c> 가 <c>Placed</c> 를 포함해 "밀 수 있다" 고 답했지만,
        /// <c>PartAssembler.Assemble</c> 이 그 상태로 올리면서 FixedJoint 로 아래 파츠에
        /// 묶으므로 <b>실제로는 절대 움직이지 않았다</b> — 셋이 붙어 8843N 이 들어가는데
        /// 2초에 0.0000m 였다. 답과 결과가 어긋나면 플레이어는 고장으로 읽는다.
        /// </summary>
        [Test]
        public void APlacedPartIsOutOfScope()
        {
            var push = MakePart(PartState.Placed);
            ThreeHaulers(push);

            Tick(push, 1f);

            Assert.IsFalse(push.CanPush, "조립 조인트가 붙잡으므로 처음부터 거절한다.");
            Assert.AreEqual(0, push.HaulerCount);
            Assert.IsFalse(push.IsMoving);
        }

        // --- 하네스 1: 집계·속도 규칙 (물리를 돌리지 않는다) -------------------

        static Vector3 Horizontal(PartPush push)
        {
            var v = push.GetComponent<Rigidbody>().linearVelocity;
            return new Vector3(v.x, 0f, v.z);
        }

        /// <summary>
        /// 물리를 돌린다. <b>지면이 없고 파츠의 중력도 꺼져 있다</b> — 마찰이 개입하지
        /// 않으므로 집계 규칙과 속도 규칙(표의 상한, 운석 충격 보존)만 드러난다.
        /// 이동 거리를 보려면 <see cref="HaulOnGround"/> 를 쓴다.
        ///
        /// <c>Physics.Simulate</c> 가 필요한 이유는 <c>Drive()</c> 가 <c>AddForce</c> 로
        /// 힘을 넣기 때문이다 — 힘은 다음 물리 스텝에야 속도가 된다.
        /// </summary>
        static void Tick(PartPush push, float seconds)
        {
            var steps = Mathf.CeilToInt(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                UnityEngine.Physics.SyncTransforms();
                push.Tick(Step);
                UnityEngine.Physics.Simulate(Step);
            }
        }

        /// <summary>
        /// 이동 의도를 넣는다. <c>MoveWish</c> 는 입력 처리에서만 채워지므로 검증은
        /// 리플렉션으로 직접 넣는다 — 조작 전체를 흉내내지 않는다.
        /// </summary>
        static void SetWish(PlayerRig player, Vector3 wish)
        {
            var property = typeof(PlayerRig).GetProperty("MoveWish");
            property.SetValue(player, wish.sqrMagnitude > 1e-6f ? wish.normalized : Vector3.zero);
        }

        static void Teleport(PlayerRig player, Vector3 foot)
        {
            player.Teleport(foot, Quaternion.identity);
            UnityEngine.Physics.SyncTransforms();
        }

        PartPush MakePart(PartState state = PartState.Free)
        {
            var go = new GameObject("Part_1") { layer = GameLayers.Part };
            go.transform.position = new Vector3(0f, 0.5f, 0f);
            _spawned.Add(go);

            go.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 1f);

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;   // 떨어지는 것과 밀리는 것을 섞지 않는다

            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            TransitionTo(part, state);

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
            SetWish(player, wish);
            Freeze(player);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }

        /// <summary>
        /// 파츠를 <b>손으로 잡고</b> +x 로 미는 세 사람. 힘이 붙는 최소 인원이 셋이므로,
        /// 파츠가 움직이는 것을 전제하는 검증은 셋을 세워야 한다 — 예전에는 한 사람으로
        /// 충분했다.
        ///
        /// <b>몸밀기가 아니라 잡기로 만드는 이유.</b> 몸밀기는 매 틱 접촉을 다시 보므로
        /// 파츠가 밀려 나가면 사람이 뒤에 남아 판정이 끊긴다. 그러면 밀기가 아니라
        /// "사람이 파츠를 따라 걷는가" 를 재게 된다. 잡기는 접촉을 다시 보지 않는다.
        /// 합산 규칙은 <see cref="GrabbersAndBodyPushersAddUp"/> 가 따로 잠근다.
        /// </summary>
        List<PlayerRig> ThreeHaulers(PartPush push)
        {
            var people = new List<PlayerRig>
            {
                MakePlayer(new Vector3(-1.2f, 0f, 0.8f), Vector3.right),
                MakePlayer(new Vector3(-1.2f, 0f, 0f), Vector3.right),
                MakePlayer(new Vector3(-1.2f, 0f, -0.8f), Vector3.right),
            };

            for (var i = 0; i < people.Count; i++)
                push.TryGrab(people[i], people[i].RightHand);

            return people;
        }

        // --- 하네스 2: 실제 이동 거리 (지면·중력·Physics.Simulate) ------------

        static PartSpec HeaviestSpec()
        {
            var best = PartSpec.All[0];
            for (var i = 1; i < PartSpec.All.Length; i++)
                if (PartSpec.All[i].Mass > best.Mass)
                    best = PartSpec.All[i];

            return best;
        }

        /// <summary>
        /// 지면 위의 파츠를 <paramref name="people"/> 명이 3초 밀고 <b>이동 거리</b>를
        /// 돌려준다.
        ///
        /// <b>사람은 손으로 잡는다.</b> 몸밀기로 세우면 파츠가 밀려 나가는 순간 사람이
        /// 뒤에 남아 접촉 판정이 끊기고, 그러면 밀기가 아니라 "사람이 파츠를 따라
        /// 걷는가" 를 재게 된다. 잡기는 접촉을 다시 보지 않으므로 3초 내내 유지된다.
        ///
        /// 인형은 <see cref="Freeze"/> 로 물리에서 떼어 낸다 — <c>PlayerRig.Tick</c> 을
        /// 부르지 않는 채로 물리에 맡기면 3초 동안 주저앉고, kinematic 인형의 팔이
        /// 파츠에 얹히면 그 접촉이 파츠를 눌러 지면 마찰이 아닌 것을 재게 된다.
        /// </summary>
        float HaulOnGround(int people, PartSpec spec = null)
        {
            var ground = new GameObject("지면");
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.AddComponent<BoxCollider>().size = new Vector3(120f, 1f, 120f);
            ground.GetComponent<Collider>().material = Prim.GroundPhysics;
            _spawned.Add(ground);

            var push = spec != null ? MakeGroundedPart(spec) : MakeGroundedBox();

            // 파츠를 지면에 얹는다.
            for (var i = 0; i < 80; i++) UnityEngine.Physics.Simulate(Step);

            var halfWidth = spec != null ? spec.Footprint.x * 0.5f : 1f;
            var line = -(halfWidth + 0.4f);
            var offsets = new[] { 0f, 0.5f, -0.5f, 0.75f };

            for (var i = 0; i < people; i++)
            {
                var player = MakePlayer(new Vector3(line, 0f, offsets[i % offsets.Length]),
                                        Vector3.right);
                Assert.IsTrue(push.TryGrab(player, player.RightHand),
                    $"{i + 1}번째 사람이 파츠를 못 짚었다 — 하네스 문제다.");
            }

            var start = push.transform.position;

            for (var i = 0; i < 300; i++)
            {
                push.Tick(Step);
                UnityEngine.Physics.Simulate(Step);
            }

            Assert.AreEqual(people, push.HaulerCount,
                $"{people}명이 밀고 있어야 한다 — 잡기나 의도가 끊겼다면 하네스 문제다.");

            var moved = push.transform.position - start;
            moved.y = 0f;
            return moved.magnitude;
        }

        PartPush MakeGroundedBox()
        {
            var go = new GameObject("Part_Box") { layer = GameLayers.Part };
            go.transform.position = new Vector3(0f, 0.62f, 0f);
            _spawned.Add(go);

            go.AddComponent<BoxCollider>().size = new Vector3(2f, 1.2f, 2f);

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = true;

            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            TransitionTo(part, PartState.Free);   // Placed 는 조립 조인트가 붙잡는다

            var push = go.AddComponent<PartPush>();
            push.Initialize();

            UnityEngine.Physics.SyncTransforms();
            return push;
        }

        /// <summary>실제 파츠의 상자 구성과 질량으로 만든다.</summary>
        PartPush MakeGroundedPart(PartSpec spec)
        {
            var go = new GameObject(spec.Name) { layer = GameLayers.Part };
            go.transform.position = new Vector3(0f, spec.Height * 0.5f + 0.02f, 0f);
            _spawned.Add(go);

            for (var i = 0; i < spec.Boxes.Length; i++)
            {
                var box = spec.Boxes[i];
                var collider = go.AddComponent<BoxCollider>();
                collider.size = box.Size;
                collider.center = box.CenterFromPartCenter(spec.Height);
            }

            go.AddComponent<Rigidbody>().useGravity = true;

            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            part.SetPlacedMass(spec.Mass);
            TransitionTo(part, PartState.Free);   // Placed 는 조립 조인트가 붙잡는다

            var push = go.AddComponent<PartPush>();
            push.Initialize();

            UnityEngine.Physics.SyncTransforms();
            return push;
        }

        /// <summary>
        /// 인형을 물리에서 떼어 낸다 — kinematic 으로 만들고 <b>콜라이더도 끈다.</b>
        ///
        /// 콜라이더를 끄는 이유는 kinematic 인형이 사실상 무한 질량이라서다. 팔이나
        /// 손이 파츠에 얹히면 그 접촉이 파츠를 누르거나 막아, 재려던 지면 마찰이 아니라
        /// <b>인형에 걸린 파츠</b>를 재게 된다. 실제로 그래서 500kg 상자가 세 명에게도
        /// 0.0002m 만 움직였다.
        ///
        /// 판정은 영향을 받지 않는다 — <c>PartPush.IsTouching</c> 은 파츠 콜라이더의
        /// <c>ClosestPoint(hips)</c> 까지의 거리로만 보고 인형 콜라이더를 쓰지 않는다.
        /// </summary>
        static void Freeze(PlayerRig player)
        {
            var bodies = player.GetComponentsInChildren<Rigidbody>();
            for (var i = 0; i < bodies.Length; i++)
                bodies[i].isKinematic = true;

            var colliders = player.GetComponentsInChildren<Collider>();
            for (var i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;

            UnityEngine.Physics.SyncTransforms();
        }

        static void TransitionTo(SpaceshipPart part, PartState state)
        {
            if (state == PartState.Locked) return;   // 처음 상태다

            part.TryTransitionTo(PartState.Free);
            if (state == PartState.Free) return;

            part.TryTransitionTo(PartState.Carried);
            if (state == PartState.Carried) return;

            part.TryTransitionTo(PartState.Placed);
            if (state == PartState.Welded) part.TryTransitionTo(PartState.Welded);
        }

        void Wipe()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PlayerRig.All.Clear();
            PartPush.All.Clear();
        }
    }
}
