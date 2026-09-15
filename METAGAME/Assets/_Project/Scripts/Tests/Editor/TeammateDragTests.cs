using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 잡은 동료를 <b>시선 방향으로</b> 끌고, <b>고개 높이로</b> 끌기·들기를 가르는 것을
    /// 지킨다 (S15P21B202-555).
    ///
    /// <para><b>위험이 셋 있고 그 셋을 각각 지킨다</b>(티켓의 '위험' 절).
    /// ① 줄다리기가 사라지는 것 ② 경계에서 높이가 떨리는 것 ③ 끌리는 쪽 시야가
    /// 땅에 처박히는 것.</para>
    /// </summary>
    public sealed class TeammateDragTests
    {
        const float Step = 0.01f;

        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;
        float _savedFixedDelta;
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();
            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;
            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
            _savedFixedDelta = Time.fixedDeltaTime;
            Time.fixedDeltaTime = Step;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _savedMode;
            Time.fixedDeltaTime = _savedFixedDelta;
            Puff.Enabled = _puffWasEnabled;
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            PlayerRig.All.Clear();
        }

        static Vector3 Aim(float pitchDegrees) =>
            Quaternion.Euler(-pitchDegrees, 0f, 0f) * Vector3.forward;

        // ── 높이 규칙 (순수 계산) ─────────────────────────────────

        [Test]
        public void LookingDownDragsAndLookingUpLifts()
        {
            var down = Aim(-30f).normalized.y;
            var up = Aim(40f).normalized.y;

            Assert.IsFalse(PlayerRig.TeammateLifted(down, false), "내려다보는데 들렸다");
            Assert.IsTrue(PlayerRig.TeammateLifted(up, false), "올려다보는데 안 들렸다");

            Assert.Less(PlayerRig.TeammateHoldHeight(down, false),
                        PlayerRig.TeammateHoldHeight(up, true),
                        "내려다볼 때가 올려다볼 때보다 높다");
        }

        [Test]
        public void LookingHigherLiftsHigher()
        {
            var low = Aim(20f).normalized.y;
            var high = Aim(80f).normalized.y;

            Assert.Less(PlayerRig.TeammateHoldHeight(low, true),
                        PlayerRig.TeammateHoldHeight(high, true),
                        "더 올려다봐도 더 안 올라간다");
        }

        [Test]
        public void TheDragHeightIsAboveTheGroundNotInIt()
        {
            Assert.Greater(PlayerRig.TeammateHoldHeight(Aim(-60f).normalized.y, false), 0.2f,
                "끌리는 높이가 땅에 박혀 있다 — 몸이 지면을 파고든다");
        }

        /// <summary>
        /// <b>위험 ②.</b> 문턱이 하나면 경계에서 고개가 조금만 흔들려도 몸이 떴다 내렸다 한다.
        /// </summary>
        [Test]
        public void TheBoundaryDoesNotFlutterWhenTheHeadWobbles()
        {
            // **들어가는 문턱을 가로지르며 흔든다.** 처음에는 두 문턱 사이(0.145~0.205)를
            // 흔들었는데, 그 범위는 문턱이 하나인 구현(0.26)에서도 한 번을 안 넘어
            // **히스테리시스를 없애도 통과했다**(대조 실험이 잡아냈다). 흔들림의 폭은
            // 히스테리시스 띠(Enter − Exit = 0.17)보다 작아야 의미가 있다.
            var wobbleSize = 0.05f;
            Assert.Less(wobbleSize * 2f,
                PhysicsScale.TeammateLiftEnter - PhysicsScale.TeammateLiftExit,
                "흔드는 폭이 히스테리시스 띠보다 넓다 — 이 검증이 아무것도 안 지킨다");

            var lifted = false;
            var flips = 0;
            for (var i = 0; i < 40; i++)
            {
                var wobble = PhysicsScale.TeammateLiftEnter
                             + (i % 2 == 0 ? wobbleSize : -wobbleSize);
                var next = PlayerRig.TeammateLifted(wobble, lifted);
                if (next != lifted) flips++;
                lifted = next;
            }

            // 처음 한 번 올라가는 것은 맞다. 그 뒤로는 안 흔들려야 한다.
            Assert.LessOrEqual(flips, 1,
                $"경계에서 {flips}번 뒤집혔다 — 고개가 조금 흔들릴 때마다 몸이 떴다 내렸다 한다");
            Assert.IsTrue(lifted, "문턱 위에서 흔들었는데 안 들렸다");
        }

        [Test]
        public void TheBoundaryStillMovesWhenTheHeadActuallyMoves()
        {
            // 히스테리시스가 "영영 안 바뀜" 이 되면 안 된다.
            Assert.IsTrue(PlayerRig.TeammateLifted(Aim(45f).normalized.y, false),
                "확실히 올려다봐도 안 들린다");
            Assert.IsFalse(PlayerRig.TeammateLifted(Aim(-20f).normalized.y, true),
                "확실히 내려다봐도 안 내려온다");
        }

        // ── 끌어올 자리 ───────────────────────────────────────────

        [Test]
        public void TheHoldPointSitsInFrontOfTheHolderAlongTheLook()
        {
            var foot = new Vector3(3f, 0f, -2f);
            var point = PlayerRig.TeammateHoldPoint(foot, Vector3.right, 1.0f);

            var flat = point - foot;
            flat.y = 0f;

            Assert.Greater(Vector3.Dot(flat.normalized, Vector3.right), 0.99f,
                "끌어올 자리가 보는 방향에 있지 않다");
            Assert.AreEqual(PhysicsScale.TeammateHoldDistance, flat.magnitude, 0.01f);
            Assert.AreEqual(foot.y + 1.0f, point.y, 0.01f, "높이가 발 기준이 아니다");
        }

        [Test]
        public void LookingStraightUpStillPicksAForwardDirection()
        {
            // 수평 성분이 0 이면 방향을 못 고른다. 그래도 자리는 있어야 한다.
            var point = PlayerRig.TeammateHoldPoint(Vector3.zero, Vector3.up, 1.5f);

            Assert.IsFalse(float.IsNaN(point.x), "끌어올 자리가 NaN 이다");
            Assert.AreEqual(1.5f, point.y, 0.01f);
        }

        [Test]
        public void TheHeightIsMeasuredFromTheFootSoJumpingDoesNotMoveIt()
        {
            var standing = PlayerRig.TeammateHoldPoint(new Vector3(0f, 0f, 0f), Vector3.forward, 1.0f);
            var jumping = PlayerRig.TeammateHoldPoint(new Vector3(0f, 2f, 0f), Vector3.forward, 1.0f);

            // 발이 올라갔으면 자리도 올라간다 — 발 기준이라는 뜻이다.
            Assert.AreEqual(2f, jumping.y - standing.y, 0.01f);
        }

        // ── 끌어오는 힘 ───────────────────────────────────────────

        [Test]
        public void ThePullPointsAtTheTarget()
        {
            var pull = PlayerRig.TeammatePull(Vector3.zero, new Vector3(2f, 0f, 0f), Vector3.zero, 1f);

            Assert.Greater(pull.x, 0f, "목표 쪽으로 안 당긴다");
            Assert.AreEqual(0f, pull.z, 0.01f);
        }

        [Test]
        public void ThePullIsCappedSoTheTwoBodiesDoNotFlingEachOther()
        {
            var far = PlayerRig.TeammatePull(Vector3.zero, new Vector3(100f, 0f, 0f), Vector3.zero, 1f);

            Assert.LessOrEqual(far.magnitude, PhysicsScale.TeammatePullMaxAccel + 0.01f,
                "멀리 있으면 무한정 세게 당긴다 — 두 몸이 서로 밀며 튄다");
        }

        [Test]
        public void ThePullSlowsDownWhenAlreadyMovingThere()
        {
            var still = PlayerRig.TeammatePull(Vector3.zero, new Vector3(1f, 0f, 0f), Vector3.zero, 1f);
            var rushing = PlayerRig.TeammatePull(Vector3.zero, new Vector3(1f, 0f, 0f),
                                                 new Vector3(3f, 0f, 0f), 1f);

            Assert.Less(rushing.x, still.x, "감쇠가 없다 — 목표를 지나쳐 출렁인다");
        }

        // ── 위험 ① 줄다리기 ──────────────────────────────────────

        [Test]
        public void PullingAgainstItCountsAsResisting()
        {
            var pull = Vector3.right;

            Assert.IsTrue(PlayerRig.TeammateResisting(-Vector3.right, pull),
                "정반대로 미는데 저항이 아니다");
            Assert.IsFalse(PlayerRig.TeammateResisting(Vector3.right, pull),
                "끌리는 쪽으로 가는데 저항이다");
            Assert.IsFalse(PlayerRig.TeammateResisting(Vector3.forward, pull),
                "옆으로 걷는 것이 저항이 됐다");
            Assert.IsFalse(PlayerRig.TeammateResisting(Vector3.zero, pull),
                "가만히 있는 것이 저항이 됐다");
        }

        [Test]
        public void ResistingMakesItSlowerButNotFree()
        {
            var full = PlayerRig.TeammatePull(Vector3.zero, new Vector3(1f, 0f, 0f), Vector3.zero, 1f);
            var resisted = PlayerRig.TeammatePull(Vector3.zero, new Vector3(1f, 0f, 0f), Vector3.zero,
                                                  PhysicsScale.TeammateResistPullScale);

            Assert.Less(resisted.magnitude, full.magnitude,
                "버텨도 끌리는 힘이 안 줄어든다 — 저항이 무의미하다");
            Assert.Greater(resisted.magnitude, 0f,
                "버티면 완전히 멈춘다 — 잡은 쪽이 아무것도 못 한다");
        }

        [Test]
        public void ThereIsAWayOutOfBeingHeld()
        {
            // 이것이 없으면 이 기능은 트롤 도구가 된다.
            Assert.Greater(PhysicsScale.TeammateResistSeconds, 0f,
                "버텨도 영영 안 풀린다 — 잡히면 끝이다");
            Assert.Less(PhysicsScale.TeammateResistSeconds, 5f,
                "빠져나가는 데 너무 오래 걸린다");
        }

        // -- 실제로 잡아서 끌어 본다 -------------------------------
        //
        // 위의 것들은 전부 순수 계산이다. 계산이 맞아도 <b>부르는 곳이 없으면</b> 화면은
        // 그대로다 — `-546`(고친 파일이 아예 안 돌았다)과 `!460`(함수만 재고 호출부를
        // 안 봤다)에서 두 번 겪었다. 여기서는 실제로 손으로 잡고 물리를 돌린다.

        PlayerRig MakePlayer(Vector3 at)
        {
            var player = PlayerRig.Create("검증", at, Quaternion.identity,
                                          Color.white, Color.gray, false);
            player.ServerAuthority = true;
            _spawned.Add(player.gameObject);
            return player;
        }

        void MakeGround()
        {
            var go = new GameObject("바닥") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(120f, 1f, 120f);
            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
        }

        /// <summary>양손으로 상대의 잡을 지점을 쥔다. 쥐지 못하면 그 자리에서 알린다.</summary>
        static void GrabBothHands(PlayerRig holder, PlayerRig victim)
        {
            Assert.NotNull(victim.CarryTarget, "잡을 지점이 없다");

            var left = holder.LeftHand.GripTo(victim.CarryTarget, victim.Doll.Hips);
            var right = holder.RightHand.GripTo(victim.CarryTarget, victim.Doll.Hips);

            Assert.IsTrue(left && right, $"손이 안 잡혔다 — 왼손 {left} 오른손 {right}");
            Assert.AreSame(victim, holder.TossVictim, "양손으로 잡았는데 대상이 안 잡힌다");
        }

        void Run(PlayerRig holder, PlayerRig victim, int steps,
                 float aimYaw, float aimPitch, Vector2 victimMove = default)
        {
            var holderInput = new InputFrame { AimYaw = aimYaw, AimPitch = aimPitch };
            var victimInput = new InputFrame { Move = victimMove, AimYaw = aimYaw };

            for (var i = 0; i < steps; i++)
            {
                holder.SetInput(holderInput);
                victim.SetInput(victimInput);
                holder.Tick();
                victim.Tick();
                UnityEngine.Physics.Simulate(Step);
            }
        }

        [Test]
        public void AHeldTeammateFollowsWhereTheHolderLooks()
        {
            MakeGround();
            var holder = MakePlayer(new Vector3(0f, 1f, 0f));
            var victim = MakePlayer(new Vector3(0.9f, 1f, 0f));
            UnityEngine.Physics.SyncTransforms();
            Run(holder, victim, 20, 0f, 0f);

            GrabBothHands(holder, victim);

            // 조준 yaw 90도는 +X 다.
            Run(holder, victim, 120, 90f, 0f);
            var toVictim = victim.Doll.Hips.position - holder.Doll.Hips.position;

            Assert.Greater(Vector3.Dot(toVictim.normalized, Vector3.right), 0.6f,
                $"오른쪽을 보는데 동료가 {toVictim} 에 있다 — 시선을 안 따라온다");
        }

        [Test]
        public void LookingTheOtherWayBringsThemRoundToTheOtherSide()
        {
            MakeGround();
            var holder = MakePlayer(new Vector3(0f, 1f, 0f));
            var victim = MakePlayer(new Vector3(0.9f, 1f, 0f));
            UnityEngine.Physics.SyncTransforms();
            Run(holder, victim, 20, 0f, 0f);

            GrabBothHands(holder, victim);

            Run(holder, victim, 150, 90f, 0f);
            var right = (victim.Doll.Hips.position - holder.Doll.Hips.position).x;

            Run(holder, victim, 150, 270f, 0f);
            var left = (victim.Doll.Hips.position - holder.Doll.Hips.position).x;

            Assert.Greater(right, left + 0.8f,
                $"반대를 봐도 동료가 안 넘어온다 — 오른쪽 {right:F2} · 왼쪽 {left:F2}");
        }

        /// <summary><b>이 티켓의 본론.</b> 고개를 올리면 들리고 내리면 끌린다.</summary>
        [Test]
        public void LookingUpLiftsThemAndLookingDownDragsThem()
        {
            MakeGround();
            var holder = MakePlayer(new Vector3(0f, 1f, 0f));
            var victim = MakePlayer(new Vector3(0.9f, 1f, 0f));
            UnityEngine.Physics.SyncTransforms();
            Run(holder, victim, 20, 0f, 0f);

            GrabBothHands(holder, victim);

            // AimPitch 는 유니티 관례대로 아래가 양수다.
            Run(holder, victim, 160, 0f, 30f);
            var dragged = victim.Doll.Hips.position.y;
            var draggedLifted = holder.CarryLifted;

            Run(holder, victim, 160, 0f, -45f);
            var lifted = victim.Doll.Hips.position.y;

            Assert.IsFalse(draggedLifted, "내려다보는데 들림 상태다");
            Assert.IsTrue(holder.CarryLifted, "올려다보는데 들림 상태가 아니다");
            Assert.Greater(lifted, dragged + 0.3f,
                $"고개를 올려도 안 들린다 — 끌 때 {dragged:F2}m · 들 때 {lifted:F2}m");
        }

        /// <summary><b>위험 3.</b> 끌리는 동안 머리가 땅에 처박히면 그 사람 화면이 지면 속이 된다.</summary>
        [Test]
        public void ADraggedTeammatesHeadStaysAboveTheGround()
        {
            MakeGround();
            var holder = MakePlayer(new Vector3(0f, 1f, 0f));
            var victim = MakePlayer(new Vector3(0.9f, 1f, 0f));
            UnityEngine.Physics.SyncTransforms();
            Run(holder, victim, 20, 0f, 0f);

            GrabBothHands(holder, victim);

            var lowest = float.MaxValue;
            for (var i = 0; i < 200; i++)
            {
                Run(holder, victim, 1, 0f, 40f);
                lowest = Mathf.Min(lowest, victim.Doll.Head.position.y);
            }

            Assert.Greater(lowest, 0.25f,
                $"끌리는 동안 머리가 {lowest:F2}m 까지 내려간다 — 그 사람 화면이 지면 속이 된다");
        }

        /// <summary><b>위험 1.</b> 버티면 빠져나갈 수 있어야 한다. 없으면 트롤 도구가 된다.</summary>
        [Test]
        public void ResistingLongEnoughBreaksTheGrip()
        {
            MakeGround();
            var holder = MakePlayer(new Vector3(0f, 1f, 0f));
            var victim = MakePlayer(new Vector3(0.9f, 1f, 0f));
            UnityEngine.Physics.SyncTransforms();
            Run(holder, victim, 20, 0f, 0f);

            GrabBothHands(holder, victim);

            // **옆걸음이 아니라 뒷걸음이어야 한다.** 처음에는 Move.x 로 밀었는데 그것은
            // 끌리는 방향과 직각이라 저항이 아니다 — TeammateResisting 이 그렇게 정해
            // 두었고(옆으로 걷는 정도로는 저항이 아니다) 그 규칙이 여기서 확인됐다.
            var steps = Mathf.CeilToInt(PhysicsScale.TeammateResistSeconds / Step) + 60;
            Run(holder, victim, steps, 90f, 0f, new Vector2(0f, -1f));

            Assert.IsNull(holder.TossVictim,
                $"{PhysicsScale.TeammateResistSeconds}초를 버텼는데 안 풀린다 — 잡히면 끝이다");
        }

        [Test]
        public void GoingAlongWithItDoesNotBreakTheGrip()
        {
            // 저항의 반대쪽도 지킨다 — 순순히 끌려오는데 풀리면 잡을 수가 없다.
            MakeGround();
            var holder = MakePlayer(new Vector3(0f, 1f, 0f));
            var victim = MakePlayer(new Vector3(0.9f, 1f, 0f));
            UnityEngine.Physics.SyncTransforms();
            Run(holder, victim, 20, 0f, 0f);

            GrabBothHands(holder, victim);

            var steps = Mathf.CeilToInt(PhysicsScale.TeammateResistSeconds / Step) + 120;
            Run(holder, victim, steps, 90f, 0f);

            Assert.AreSame(victim, holder.TossVictim, "가만히 있었는데 손이 풀렸다");
            Assert.Zero(holder.CarryResistSeconds, "저항한 적이 없는데 시간이 쌓였다");
        }

        [Test]
        public void TheExitThresholdIsLowerThanTheEnterThreshold()
        {
            Assert.Less(PhysicsScale.TeammateLiftExit, PhysicsScale.TeammateLiftEnter,
                "문턱이 하나다 — 히스테리시스가 없다");
        }
    }
}
