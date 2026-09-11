using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Interaction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 살아있는 플레이어끼리 잡아서 밀고 당기는 것을 검증한다(S15P21B202-442).
    ///
    /// <b>협력 요소가 아니다.</b> 근거는 둘이다 — 세계의 다른 물건은 전부 잡아서 밀고
    /// 당길 수 있는데 사람만 안 되는 <b>일관성</b> 문제이고, 서로 붙들고 끄는 것 자체가
    /// <b>장난 요소</b>다.
    ///
    /// <para><b>막고 있던 것이 셋이었다.</b> ① 살아있는 사람에게는 잡을 지점이 아예
    /// 없었고 ② 후보를 찾는 유일한 함수가 <c>!other.Downed</c> 로 걸렀고 ③ 잡는 데
    /// 성공해도 <b>끌려오지 않았다</b> — 이동 코드가 매 스텝 속도를 직접 써 넣어
    /// 조인트가 끌어온 결과를 지웠기 때문이다. 셋째가 가장 비쌌다: 앞의 둘만 열면
    /// "잡히기는 하는데 안 움직인다" 가 되고, 그 상태로도 잡기 검증은 초록불이다.</para>
    /// </summary>
    public sealed class LivePlayerGrabTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;
        bool _savedIgnore;
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();
            Grabbable.All.Clear();

            _savedMode = UnityEngine.Physics.simulationMode;
            _savedIgnore = LogAssert.ignoreFailingMessages;
            _puffWasEnabled = Puff.Enabled;

            UnityEngine.Physics.simulationMode = SimulationMode.Script;
            LogAssert.ignoreFailingMessages = true;
            Puff.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _savedMode;
            LogAssert.ignoreFailingMessages = _savedIgnore;
            Puff.Enabled = _puffWasEnabled;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PlayerRig.All.Clear();
            Grabbable.All.Clear();
        }

        // ── 잡을 지점 ────────────────────────────────────────────

        [Test]
        public void ALivingPlayerHasAGrabPoint_FromBirth()
        {
            var player = MakePlayer(Vector3.zero);

            Assert.IsFalse(player.Downed, "한 번도 기절하지 않았다.");
            Assert.IsNotNull(player.CarryTarget,
                "예전에는 GoDown·Revive 안에서만 만들어져 여기서 null 이었다.");
            Assert.IsTrue(player.CarryTarget.gameObject.activeSelf);
        }

        /// <summary>
        /// 태어날 때 만드는 것이 <b>참가자 화면에서도</b> 후보가 뜨게 하는 조건이다.
        /// 참가자 기계에서는 <c>GoDown</c>·<c>Revive</c> 가 돌지 않는다 — 물리가 서버
        /// 것이라서다. 그래서 <c>SetRemoteDowned</c> 가 기절한 동료에 대해서만 지점을
        /// 만들어 주고 있었고(S15P21B202-390), 살아있는 동료에게는 그 경로조차 없었다.
        /// </summary>
        [Test]
        public void ThePointIsMadeWithoutEverGoingDown()
        {
            var player = MakePlayer(Vector3.zero);

            Assert.IsTrue(player.CarryTarget.PhysicsGrip,
                "사람이 도구처럼 몸 앞에 붙으면 안 된다(§20.4 · -249).");
            Assert.AreEqual("동료", player.CarryTarget.Label);
        }

        // ── 후보 탐색 ────────────────────────────────────────────

        [Test]
        public void ALivingTeammateInFront_IsAnInteractionTarget()
        {
            var me = MakePlayer(Vector3.zero);
            MakePlayer(new Vector3(0f, 0f, 1.0f));
            UnityEngine.Physics.SyncTransforms();

            me.RefreshInteractionFocus();

            Assert.AreEqual(PlayerInteractionKind.Grabbable,
                me.CurrentInteractionTarget.Kind,
                "앞에 선 살아있는 동료가 후보가 되어야 한다.");
        }

        /// <summary>
        /// <b>물리 질의를 열어서 푼 것이 아니다.</b> 조준 탐색 마스크는 그대로 Player 를
        /// 뺀다 — 열면 고정못(파츠의 자식이다)이 파츠의 잡기 지점으로 딸려 들어온다
        /// (S15P21B202-249). 후보는 <c>PlayerRig.All</c> 을 훑어서 찾는다.
        ///
        /// 이 검증이 없으면 다음 사람이 "마스크를 열면 간단한데" 로 되돌린다.
        /// </summary>
        [Test]
        public void TheAimingMaskStillExcludesPlayers()
        {
            var mask = RagdollBuilder.WorldMask;

            Assert.Zero(mask & (1 << GameLayers.Player),
                "Player 를 마스크에 넣어 푼 것이 아니다.");
            Assert.Zero(mask & (1 << GameLayers.Interactable),
                "Interactable 도 마찬가지다 — 고정못이 딸려 온다.");
        }

        [Test]
        public void YouCannotGrabYourself()
        {
            var me = MakePlayer(Vector3.zero);
            UnityEngine.Physics.SyncTransforms();

            me.RefreshInteractionFocus();

            Assert.AreNotEqual(me.CarryTarget, me.CurrentInteractionTarget.Grabbable,
                "혼자 있으면 자기 잡을 지점이 후보가 되면 안 된다.");
        }

        // ── 끌기 (§13 "밀거나 당긴다" 와 같은 뜻) ─────────────────

        /// <summary>
        /// <b>이 검증이 이 이슈의 핵심이다.</b>
        ///
        /// 앞의 두 관문만 열면 "잡히기는 하는데 안 움직인다" 가 된다. 그 상태에서도
        /// 잡기 검증은 전부 초록불이다 — 실제로 그랬다. 실측으로 2초에 <b>0.0785m</b>
        /// 였고, 잡은 쪽이 오히려 끌려갔다.
        ///
        /// <para>원인은 <c>DriveMovementVelocity</c> 가 인형의 모든 몸에
        /// <c>linearVelocity = ...</c> 를 <b>직접 써 넣는</b> 것이었다. 입력이 없으면
        /// 목표 속도가 0 이므로 제자리에 못을 박는 것과 같고, 조인트가 끌어온 속도가
        /// 매 스텝 지워진다. 기절한 동료가 끌려오던 이유는 래그돌이라 이 코드가 돌지
        /// 않아서였다.</para>
        /// </summary>
        [Test]
        public void GrabbingAndWalkingAway_ActuallyDragsThem()
        {
            MakeGround();
            var puller = MakePlayer(Vector3.zero);
            var target = MakePlayer(new Vector3(0.7f, 0f, 0f));

            Assert.IsTrue(puller.LeftHand.GripTo(target.CarryTarget, target.Doll.Hips),
                "잡을 수 있어야 한다.");

            var from = target.Doll.Hips.position;
            Drive(puller, WalkAwayHolding(), target, Idle(), 2f);
            var moved = Vector3.Distance(target.Doll.Hips.position, from);

            Assert.IsNotNull(puller.LeftHand.Held, "2초 동안 놓지 않는다.");
            Assert.IsFalse(target.Doll.Hips.isKinematic,
                "물리 오브젝트로 남아야 한다 — 도구처럼 붙으면 안 된다(§20.4).");
            Assert.Greater(moved, 0.8f,
                $"끌려와야 한다. 실측 1.27m. 이동={moved:F4}m");
        }

        /// <summary>대조군. 잡지 않으면 같은 2초에 상대가 움직이지 않는다.</summary>
        [Test]
        public void WithoutAGrip_WalkingAwayMovesNobody()
        {
            MakeGround();
            var puller = MakePlayer(Vector3.zero);
            var target = MakePlayer(new Vector3(0.7f, 0f, 0f));

            var from = target.Doll.Hips.position;
            Drive(puller, WalkAwayHolding(), target, Idle(), 2f);
            var moved = Vector3.Distance(target.Doll.Hips.position, from);

            Assert.Less(moved, 0.3f,
                $"손만 뻗고 걸어가면 상대는 제자리다. 실측 0.01m. 이동={moved:F4}m");
        }

        /// <summary>
        /// 버티면 덜 끌려온다. 이것이 요구된 "밀고 당기기" 이고, 잡힌 사람이 빠져나갈
        /// 방법이기도 하다.
        ///
        /// <para><b>거리가 아니라 끌려간 방향으로 잰다.</b> 처음에는 이동 거리의 크기를
        /// 쟀는데 버티는 쪽이 <b>더 큰</b> 값이 나왔다(가만히 0.816m · 버팀 2.528m) —
        /// 버티는 사람은 반대쪽으로 걸어가므로 크기만 보면 많이 움직인 것이 된다.
        /// 재야 하는 것은 "끌려간 만큼" 이고 그것은 부호가 있는 양이다.</para>
        /// </summary>
        [Test]
        public void ResistingMakesYouHarderToDrag()
        {
            var dragged = DraggedAlongPull(resist: false);
            var resisted = DraggedAlongPull(resist: true);

            Assert.Greater(dragged, 0.5f, $"가만히 있으면 끌려간다. {dragged:F3}m");
            Assert.Less(resisted, dragged,
                $"반대로 걸으면 덜 끌려야 한다. 가만히={dragged:F3}m 버팀={resisted:F3}m");
        }

        /// <summary>
        /// 버텨도 조인트는 끊어지지 않는다. 끊어지면 줄다리기가 아니라 그냥 놓치는 것이다.
        /// </summary>
        [Test]
        public void TugOfWar_DoesNotBreakTheGrip()
        {
            MakeGround();
            var puller = MakePlayer(Vector3.zero);
            var target = MakePlayer(new Vector3(0.7f, 0f, 0f));
            puller.LeftHand.GripTo(target.CarryTarget, target.Doll.Hips);

            Drive(puller, WalkAwayHolding(), target, WalkToward(), 2f);

            var gap = Vector3.Distance(target.Doll.Hips.position,
                                       puller.Doll.Hips.position);

            Assert.IsNotNull(puller.LeftHand.Held, "붙들고 있어야 한다.");
            Assert.Less(gap, 2.2f, $"팔 길이 안에 남아야 한다. 실측 1.31m. 거리={gap:F3}m");
        }

        [Test]
        public void TheGrabbedPlayerStaysOnTheirFeet()
        {
            MakeGround();
            var puller = MakePlayer(Vector3.zero);
            var target = MakePlayer(new Vector3(0.7f, 0f, 0f));

            var beforeY = target.Doll.Hips.position.y;
            puller.LeftHand.GripTo(target.CarryTarget, target.Doll.Hips);
            Drive(puller, WalkAwayHolding(), target, Idle(), 2f);

            Assert.Greater(target.Doll.Hips.position.y, beforeY - 0.25f,
                "끌린다고 바닥에 처박히면 안 된다.");
        }

        // ── 무엇을 끌고 있는지 ───────────────────────────────────

        [Test]
        public void CarryingALivingTeammate_IsReportedButNotAsDowned()
        {
            var carrier = MakePlayer(Vector3.zero);
            var mate = MakePlayer(new Vector3(1f, 0f, 0f));

            carrier.LeftHand.GripTo(mate.CarryTarget, mate.Doll.Hips);

            Assert.IsTrue(carrier.CarryingTeammate);
            Assert.IsFalse(carrier.CarryingDownedTeammate,
                "살아있는 사람이다 — 두 가지를 갈라 두지 않으면 이름이 거짓이 된다.");
        }

        [Test]
        public void CarryingADownedTeammate_IsStillReportedAsDowned()
        {
            var carrier = MakePlayer(Vector3.zero);
            var mate = MakePlayer(new Vector3(1f, 0f, 0f));
            mate.GoDown("검증");

            carrier.LeftHand.GripTo(mate.CarryTarget, mate.Doll.Hips);

            Assert.IsTrue(carrier.CarryingTeammate);
            Assert.IsTrue(carrier.CarryingDownedTeammate, "§20.4 는 그대로다.");
        }

        [Test]
        public void BeingGrabbed_IsVisibleOnTheOneWhoIsHeld()
        {
            var carrier = MakePlayer(Vector3.zero);
            var mate = MakePlayer(new Vector3(1f, 0f, 0f));

            Assert.IsFalse(mate.GrabbedByOthers);

            carrier.LeftHand.GripTo(mate.CarryTarget, mate.Doll.Hips);
            Assert.IsTrue(mate.GrabbedByOthers, "잡힌 쪽이 그 사실을 알아야 이동이 바뀐다.");

            carrier.LeftHand.Release();
            Assert.IsFalse(mate.GrabbedByOthers);
        }

        // ── 놓아야 하는 자리 ─────────────────────────────────────

        /// <summary>
        /// 순간이동(부활 · <c>R</c> 끼임 복구, §21)하면 나를 잡고 있던 손도 놓는다.
        ///
        /// <b>이것은 잡을 지점을 항상 켜면서 새로 생긴 자리다.</b> 예전에는 지점이
        /// 기절 중에만 켜져 있어 이 경로가 기절 해제와 겹쳐 가려져 있었다. 놓지 않으면
        /// 남은 <c>FixedJoint</c> 가 순간이동한 몸을 맵 저쪽에서 도로 끌어당긴다.
        /// </summary>
        [Test]
        public void TeleportingAway_MakesTheCarrierLetGo()
        {
            var carrier = MakePlayer(Vector3.zero);
            var mate = MakePlayer(new Vector3(1f, 0f, 0f));

            carrier.LeftHand.GripTo(mate.CarryTarget, mate.Doll.Hips);
            Assert.IsTrue(carrier.CarryingTeammate);

            mate.Teleport(new Vector3(30f, 0f, 30f), Quaternion.identity);

            Assert.IsNull(carrier.LeftHand.Held, "맵 저쪽까지 붙들고 있을 수는 없다.");
            Assert.IsFalse(mate.GrabbedByOthers);
        }

        [Test]
        public void LettingGo_LeavesThemWhereTheyAre()
        {
            MakeGround();
            var carrier = MakePlayer(Vector3.zero);
            var mate = MakePlayer(new Vector3(1f, 0f, 0f));

            carrier.LeftHand.GripTo(mate.CarryTarget, mate.Doll.Hips);
            var where = mate.Doll.Hips.position;

            carrier.LeftHand.Release();

            Assert.IsFalse(carrier.CarryingTeammate);
            Assert.AreEqual(where, mate.Doll.Hips.position, "그 자리에 남는다.");
        }

        // ── 도구 ─────────────────────────────────────────────────

        /// <summary>끌려간 방향으로 얼마나 갔는가. 반대로 가면 음수다.</summary>
        float DraggedAlongPull(bool resist)
        {
            MakeGround();
            var puller = MakePlayer(Vector3.zero);
            var target = MakePlayer(new Vector3(0.7f, 0f, 0f));
            puller.LeftHand.GripTo(target.CarryTarget, target.Doll.Hips);

            var from = target.Doll.Hips.position;
            var pullDir = (puller.Doll.Hips.position - from);
            pullDir.y = 0f;
            pullDir.Normalize();

            Drive(puller, WalkAwayHolding(), target, resist ? WalkToward() : Idle(), 2f);

            var moved = target.Doll.Hips.position - from;
            moved.y = 0f;
            return Vector3.Dot(moved, pullDir);
        }

        /// <summary>
        /// <b><c>GrabLeft</c> 를 계속 켜 둔다.</b> <c>PlayerRig.DriveArm</c> 은
        /// <c>reaching</c> 이 거짓이면 그 스텝에 바로 손을 놓는다 — 버튼을 놓은 것으로
        /// 읽기 때문이다. 이것을 빠뜨리면 "조인트가 안 붙든다" 로 잘못 읽게 된다.
        /// </summary>
        static InputFrame WalkAwayHolding() =>
            new InputFrame { Move = new Vector2(-1f, 0f), GrabLeft = true };

        static InputFrame WalkToward() => new InputFrame { Move = new Vector2(1f, 0f) };
        static InputFrame Idle() => new InputFrame();

        void Drive(PlayerRig a, InputFrame inputA, PlayerRig b, InputFrame inputB, float seconds)
        {
            var steps = Mathf.RoundToInt(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                a.SetInput(inputA);
                b.SetInput(inputB);
                a.Tick();
                b.Tick();
                UnityEngine.Physics.Simulate(Step);
            }
        }

        GameObject MakeGround()
        {
            var go = new GameObject("Ground") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(80f, 1f, 80f);
            _spawned.Add(go);
            return go;
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            var player = PlayerRig.Create("검증", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
