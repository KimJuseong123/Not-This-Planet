using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;

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

            // **물리 시간과 로직 시간을 맞춰 쥔다**(!460 AI 리뷰). `Tick` 안은
            // `Time.fixedDeltaTime` 으로 세는데 여기서는 `Physics.Simulate(Step)` 으로
            // 돌린다 — 둘이 어긋나면 "300스텝 = 3초" 가 거짓이 되고 `TossedMinSeconds`
            // 검증이 다른 시간축을 재게 된다. 지금 프로젝트 값이 마침 0.01 이라 바뀌는
            // 것은 없지만, 프로젝트 설정이 움직여도 이 검증이 옳게 남도록 여기서 쥔다.
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

        /// <summary>
        /// <b>오류 로그를 삼키지 않는다.</b> 기존 던지기 검증들은 여기서
        /// <c>LogAssert.ignoreFailingMessages = true</c> 로 <c>Create</c> 의 로그를 통째로
        /// 넘겼는데, 재 보니 <c>Create</c> 는 <b>오류를 0건 낸다</b>. 삼킬 것도 없이
        /// 방어선만 없애고 있었다 — 생성 경로에 새 오류가 생기면 조용히 통과한다
        /// (!460 AI 리뷰).
        /// </summary>
        PlayerRig MakePlayer(Vector3 at)
        {
            var player = PlayerRig.Create("검증", at, Quaternion.identity,
                                          Color.white, Color.gray, false);

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

            // **상태만 켜고 속도는 셋에 똑같이 준다.** 처음에는 `LaunchTossed(up * 8)` 로
            // 켠 뒤 아래 Push 를 또 줘서 던져진 쪽만 초기 속도가 두 배였다 — 통과는 했지만
            // "같은 조건에서 비교" 라는 전제가 깨져 있었다(!460 AI 리뷰가 짚었다).
            tossed.LaunchTossed(Vector3.zero);
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
                foreach (var rig in new[] { tossed, downed, airborne })
                {
                    rig.SetInput(default);      // 앞선 검증이 남긴 입력에 기대지 않는다
                    rig.Tick();
                }
                UnityEngine.Physics.Simulate(Step);
                if (i % 15 != 14) continue;
                tossedTilts.Add(Tilt(tossed));
                downedTilts.Add(Tilt(downed));
                airborneTilts.Add(Tilt(airborne));
            }

            Assert.IsTrue(tossed.Ragdolled, "던져진 몸이 늘어지지 않았다");
            Assert.IsFalse(airborne.Ragdolled, "그냥 뛰어오른 몸까지 늘어졌다");

            // **절대 허용치로 재지 않는다.** 처음에는 0.3도로 쟀다가 관절 솔버 흔들림이
            // 걱정돼 2.0도로 넓혔는데, 그러자 실측값(던져짐 2.0~2.8 · 그냥 띄움 1.8~2.5)이
            // 전부 그 안에 들어와 **회귀를 못 잡는 테스트**가 됐다 — 던져짐이 '아무 일
            // 없는 몸' 으로 되돌아가도 통과한다(!460 AI 리뷰가 짚었다).
            //
            // 대신 **거리를 견준다**: 던져짐은 기절 쪽에 훨씬 가까워야 한다. 눈금이 아니라
            // 비율이라 솔버가 조금 흔들려도 결론이 안 바뀐다.
            float toDowned = 0f, toAirborne = 0f;
            for (var i = 0; i < tossedTilts.Count; i++)
            {
                toDowned += Mathf.Abs(tossedTilts[i] - downedTilts[i]);
                toAirborne += Mathf.Abs(tossedTilts[i] - airborneTilts[i]);
            }

            var seen = $"던져짐 {string.Join(" ", tossedTilts.ConvertAll(t => t.ToString("F1")))}"
                       + $" · 기절 {string.Join(" ", downedTilts.ConvertAll(t => t.ToString("F1")))}"
                       + $" · 그냥 {string.Join(" ", airborneTilts.ConvertAll(t => t.ToString("F1")))}";

            // 셋이 전부 똑같으면 비교가 성립하지 않는다 — 통과해도 아무 뜻이 없다.
            Assert.Greater(toAirborne, 1.0f,
                "던져진 몸과 아무 일 없는 몸이 구분되지 않는다 — 대조가 성립하지 않는다. " + seen);

            Assert.Less(toDowned, toAirborne * 0.4f,
                $"던져진 몸이 기절한 몸(거리 {toDowned:F2})보다 아무 일 없는 몸"
                + $"(거리 {toAirborne:F2})에 가깝다. " + seen);

            Assert.AreNotEqual(tossed.Ragdolled, airborne.Ragdolled,
                "던져진 몸과 아무 일 없는 몸이 같은 상태다");
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

        /// <summary>
        /// <b>잠정 기준이다.</b> "착지 직후 바로 일어나는가, 잠깐 누워 있는가" 는 기획에
        /// 정해진 것이 없다(<c>-550</c> 의 '확인할 것'). 지금 동작이 즉시 기립이라 그것을
        /// 적어 둘 뿐이고, 기획이 "잠깐 누워 있기" 로 정해지면 <b>이 검증이 먼저 바뀐다.</b>
        /// </summary>
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
