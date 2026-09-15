using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 던져진 몸이 <b>실제로 던져진 상태</b>가 되는지 지킨다 (S15P21B202-550).
    ///
    /// <para><b>무엇이 문제였나.</b> <c>Tossed</c> 는 카메라 플래그 하나였고 몸에는 아무
    /// 일도 하지 않았다. 실측으로 던져진 몸과 그냥 같은 속도로 띄운 몸의 기울기 계열이
    /// 완전히 같았고(0.7 1.3 1.5 1.6 1.6 1.5), 공중에서 옆으로 민 거리도 1.804m 로
    /// 똑같았다 — 던져진 사람이 선 자세로 날아가며 평소처럼 조종했다.</para>
    ///
    /// <para><b>그래서 여기서는 결과를 잰다.</b> "플래그가 켜졌는가" 가 아니라 "몸이
    /// 달라졌는가" 다. 예전 코드도 플래그는 멀쩡히 켰다.</para>
    /// </summary>
    public sealed class TossedBodyTests
    {
        const float Step = 0.01f;

        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();
            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;
            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _savedMode;
            Puff.Enabled = _puffWasEnabled;
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            PlayerRig.All.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        PlayerRig MakePlayer(Vector3 at)
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerRig player;
            try
            {
                player = PlayerRig.Create("검증", at, Quaternion.identity,
                                          Color.white, Color.gray, false);
            }
            finally { LogAssert.ignoreFailingMessages = false; }

            player.ServerAuthority = true;
            _spawned.Add(player.gameObject);
            return player;
        }

        /// <summary>바닥. 던져진 것이 <b>끝나는지</b> 보려면 닿을 곳이 있어야 한다.</summary>
        void MakeGround()
        {
            // DownedRescueTests 와 같은 방식이다 — 프리미티브는 레이어가 기본값이라
            // ProbeGround 의 마스크에 안 걸린다.
            var go = new GameObject("바닥") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(80f, 1f, 80f);
            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
        }

        static void Push(PlayerRig rig, Vector3 velocity)
        {
            foreach (var body in rig.Doll.Bodies)
                if (body != null && !body.isKinematic)
                    body.linearVelocity += velocity;
        }

        static float Tilt(PlayerRig rig) =>
            Vector3.Angle(rig.Doll.Hips.transform.up, Vector3.up);

        static void Run(PlayerRig rig, int steps, InputFrame input)
        {
            for (var i = 0; i < steps; i++)
            {
                rig.SetInput(input);
                rig.Tick();
                UnityEngine.Physics.Simulate(Step);
            }
        }

        // ── 몸이 실제로 달라지는가 ────────────────────────────────

        /// <summary>
        /// <b>던져진 몸은 기절한 몸과 똑같이 굴어야 한다.</b> 둘 다 "내 몸이 내 것이 아닌"
        /// 상태이고, 기절 쪽은 이미 그렇게 돌고 있으므로 그것이 기준이다.
        ///
        /// <para><b>절대 각도로 재지 않는 이유.</b> 처음에는 "던져지면 기울어질 것" 으로
        /// 쟀는데 0.0도가 나왔다 — 자유낙하에는 토크가 없어서 세우는 힘만 꺼서는 몸이
        /// 선 자세 그대로 떨어진다. 회전을 억지로 넣어 봐도 관절과 각저항(0.70)이 먹어
        /// <b>25 rad/s 를 줘도 최대 15.7도</b>밖에 안 구른다. 구르는 연출은 이 티켓이
        /// 내놓는 것이 아니다.</para>
        /// </summary>
        [Test]
        public void ATossedBodyBehavesExactlyLikeADownedOne()
        {
            var tossed = MakePlayer(new Vector3(0f, 1f, 0f));
            var downed = MakePlayer(new Vector3(20f, 1f, 0f));
            var airborne = MakePlayer(new Vector3(40f, 1f, 0f));
            UnityEngine.Physics.SyncTransforms();

            tossed.LaunchTossed(Vector3.up * 8f);
            downed.GoDown();

            foreach (var rig in new[] { tossed, downed, airborne })
            {
                Push(rig, Vector3.up * 8f);
                Spin(rig, new Vector3(4f, 0f, 0f));
            }

            var tossedTilts = new List<float>();
            var downedTilts = new List<float>();
            var airborneTilts = new List<float>();
            for (var i = 0; i < 90; i++)
            {
                foreach (var rig in new[] { tossed, downed, airborne }) rig.Tick();
                UnityEngine.Physics.Simulate(Step);
                if (i % 15 != 14) continue;
                tossedTilts.Add(Tilt(tossed));
                downedTilts.Add(Tilt(downed));
                airborneTilts.Add(Tilt(airborne));
            }

            Assert.IsTrue(tossed.Ragdolled, "던져진 몸이 늘어지지 않았다");
            Assert.IsFalse(airborne.Ragdolled, "그냥 뛰어오른 몸까지 늘어졌다");

            for (var i = 0; i < tossedTilts.Count; i++)
                Assert.AreEqual(downedTilts[i], tossedTilts[i], 0.3f,
                    $"던져진 몸과 기절한 몸이 다르게 군다 — 던져짐 "
                    + string.Join(" ", tossedTilts.ConvertAll(t => t.ToString("F1")))
                    + " · 기절 "
                    + string.Join(" ", downedTilts.ConvertAll(t => t.ToString("F1"))));

            // 그리고 둘 다 "아무 일 없는 몸" 과는 달라야 한다. 예전에는 던져짐이
            // 이쪽과 같았다 — 그것이 이 티켓의 증상이다.
            Assert.Greater(tossedTilts[^1], airborneTilts[^1] + 0.3f,
                $"던져진 몸 {tossedTilts[^1]:F2}도 · 아무 일 없는 몸 {airborneTilts[^1]:F2}도");
        }

        static void Spin(PlayerRig rig, Vector3 angularVelocity)
        {
            foreach (var body in rig.Doll.Bodies)
                if (body != null && !body.isKinematic)
                    body.angularVelocity = angularVelocity;
        }

        [Test]
        public void ATossedPlayerCannotSteerInTheAir()
        {
            var tossed = MakePlayer(new Vector3(0f, 1f, 0f));
            var airborne = MakePlayer(new Vector3(20f, 1f, 0f));
            UnityEngine.Physics.SyncTransforms();

            var velocity = Vector3.up * 7f;
            tossed.LaunchTossed(velocity);
            Push(airborne, velocity);

            var tossedStart = tossed.Doll.Hips.position;
            var airborneStart = airborne.Doll.Hips.position;

            var pushRight = new InputFrame { Move = new Vector2(1f, 0f) };
            Run(tossed, 80, pushRight);
            Run(airborne, 80, pushRight);

            float tossedMoved = Sideways(tossed.Doll.Hips.position - tossedStart);
            float airborneMoved = Sideways(airborne.Doll.Hips.position - airborneStart);

            // 예전에는 둘 다 1.804m 로 **같았다**. 그것이 이 티켓의 증상이다.
            Assert.Less(tossedMoved, airborneMoved * 0.5f,
                $"던져진 사람이 {tossedMoved:F3}m 움직였다 — 그냥 뛴 사람은 {airborneMoved:F3}m");
        }

        static float Sideways(Vector3 delta)
        {
            delta.y = 0f;
            return delta.magnitude;
        }

        [Test]
        public void ATossedPlayerCannotJump()
        {
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));
            MakeGround();

            // 땅에 선 채로 던져짐만 켠다 — 점프가 막히는지만 본다.
            Run(rig, 60, default);
            Assert.IsTrue(rig.Grounded, "준비가 안 됐다 — 바닥에 서 있지 않다");

            rig.LaunchTossed(Vector3.zero);
            var before = rig.Doll.Hips.position.y;
            Run(rig, 30, new InputFrame { Jump = true });

            Assert.Less(rig.Doll.Hips.position.y, before + 0.25f,
                "던져진 채로 점프했다");
        }

        // ── 끝나는가 (가장 위험한 자리) ────────────────────────────

        /// <summary>
        /// <b>이 검증이 이 티켓에서 제일 중요하다.</b> 몸을 늘어지게 해 놓으면 발이 아니라
        /// 등으로 떨어질 수 있다. <c>Grounded</c> 가 발 기준이었다면 던져짐이 영영 끝나지
        /// 않아 그 사람은 다시는 움직이지 못한다.
        ///
        /// <para>실제로는 골반에서 아래로 쏘는 레이캐스트다(<c>ProbeGround</c>) — 늘어져도
        /// 골반이 땅 가까우면 참이다. 읽어서 그렇게 보였지만, 영영 못 움직이는 결함은
        /// 읽고 넘길 수 있는 종류가 아니라 재서 지킨다.</para>
        /// </summary>
        [Test]
        public void ATossedPlayerLandsAndGetsControlBack()
        {
            MakeGround();
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));

            rig.LaunchTossed(new Vector3(0f, 6f, 4f));
            Assert.IsTrue(rig.Tossed, "던져지지 않았다");

            // 3초. 6m/s 로 올라가면 1.2초면 떨어지고도 남는다.
            Run(rig, 300, default);

            Assert.IsFalse(rig.Tossed,
                $"3초가 지나도 던져진 상태다 — Grounded={rig.Grounded} "
                + $"높이={rig.Doll.Hips.position.y:F2} 기울기={Tilt(rig):F1}도. "
                + "착지 판정이 늘어진 몸을 못 읽으면 그 사람은 다시는 못 움직인다");
        }

        [Test]
        public void AfterLandingTheBodyStandsUpAgain()
        {
            MakeGround();
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));

            rig.LaunchTossed(new Vector3(0f, 6f, 4f));
            Run(rig, 300, default);
            Assert.IsFalse(rig.Tossed, "먼저 착지부터 돼야 한다");

            Run(rig, 300, default);

            Assert.Less(Tilt(rig), 25f,
                $"착지하고 3초가 지나도 {Tilt(rig):F1}도로 누워 있다");
            Assert.IsFalse(rig.Ragdolled, "일어나지 못했다");
        }

        [Test]
        public void ControlComesBackAfterLanding()
        {
            MakeGround();
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));

            rig.LaunchTossed(new Vector3(0f, 6f, 4f));
            Run(rig, 600, default);       // 던져짐이 끝나고 일어설 때까지
            Assert.IsFalse(rig.Tossed);

            var start = rig.Doll.Hips.position;
            Run(rig, 120, new InputFrame { Move = new Vector2(1f, 0f) });

            Assert.Greater(Sideways(rig.Doll.Hips.position - start), 0.3f,
                "착지한 뒤에도 조종이 돌아오지 않았다");
        }

        // ── 같은 조건을 두 곳에 따로 적지 않는다 ───────────────────

        [Test]
        public void TheViewRuleAndTheControlRuleAreTheSameCondition()
        {
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));

            Assert.IsFalse(rig.BodyOutOfControl);
            Assert.AreEqual(rig.BodyOutOfControl, rig.BodyViewActive);

            rig.LaunchTossed(Vector3.up * 5f);
            Assert.IsTrue(rig.BodyOutOfControl, "던져졌는데 조종을 잃지 않았다");
            Assert.AreEqual(rig.BodyOutOfControl, rig.BodyViewActive,
                "시야 규칙과 조종 규칙이 갈라졌다 — 한쪽만 고치는 날이 온다");
        }

        [Test]
        public void BeingDownedStillTakesControlAway()
        {
            // 던져짐을 얹으면서 기절을 부수지 않았는지 본다.
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));
            rig.GoDown();

            Assert.IsTrue(rig.BodyOutOfControl);
            Assert.IsTrue(rig.BodyViewActive);

            MakeGround();
            var start = rig.Doll.Hips.position;
            Run(rig, 120, new InputFrame { Move = new Vector2(1f, 0f) });

            Assert.Less(Sideways(rig.Doll.Hips.position - start), 0.3f,
                "기절한 사람이 걸어 다닌다");
        }

        // ── 참가자 쪽 ─────────────────────────────────────────────

        [Test]
        public void AParticipantGetsTheSameLimpBodyFromTheSyncVar()
        {
            // 참가자 기계에서는 LaunchTossed 가 조기 반환한다. 몸을 늘어뜨리는 것은
            // SetRemoteTossed 로 들어온 값이어야 한다 — 아니면 참가자만 선 채로 난다.
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));
            rig.ServerAuthority = false;

            rig.LaunchTossed(Vector3.up * 8f);
            Assert.IsFalse(rig.Tossed, "참가자가 스스로 던져진 상태를 만들었다");
            Assert.IsFalse(rig.BodyOutOfControl);

            rig.SetRemoteTossed(true);
            Assert.IsTrue(rig.BodyOutOfControl,
                "참가자가 서버가 보낸 던져짐을 몸에 반영하지 않았다");

            Push(rig, Vector3.up * 8f);
            var start = rig.Doll.Hips.position;
            Run(rig, 90, new InputFrame { Move = new Vector2(1f, 0f) });

            Assert.IsTrue(rig.Ragdolled,
                "참가자 화면의 몸이 늘어지지 않았다 — 호스트만 제대로 보인다");
            Assert.Less(Sideways(rig.Doll.Hips.position - start), 0.35f,
                "참가자가 던져진 채로 공중에서 움직였다");
        }

    }
}
