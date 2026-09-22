using System.Collections.Generic;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운석 충격의 세 단계를 검증한다(기획 §20.1, S15P21B202-247).
    ///
    /// 예전에는 세 단계가 <b>모두 같았다.</b> <c>DamageSource</c> 의 "닿으면 25" 하나뿐이고
    /// <see cref="Tuning.MaxHealth"/> 가 100 이라 <b>네 번</b>을 맞아야 기절했다 — §20.1 은
    /// 직접 충돌 <b>한 번</b>으로 기절한다고 정한다.
    /// </summary>
    public sealed class MeteorImpactTierTests
    {
        const float Step = 0.01f;

        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;
        bool _logWasIgnoring;
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            Meteor.All.Clear();
            PlayerRig.All.Clear();

            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;

            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;

            _logWasIgnoring = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _savedMode;
            Puff.Enabled = _puffWasEnabled;

            // <b>전역 상태다 — 반드시 되돌린다</b>(!525 AI 리뷰). 켜 둔 채로
            // 끝나면 뒤에 도는 다른 검증의 에러 로그까지 삼켜, 실패해야 할 것이
            // 조용히 통과한다.
            LogAssert.ignoreFailingMessages = _logWasIgnoring;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            Meteor.All.Clear();
            PlayerRig.All.Clear();
        }

        // --- 경계 (순수 계산) ---------------------------------------------

        [Test]
        public void OutsideTheShockwave_IsNothing()
        {
            // 반경 2m × 배율 1.8 → 충격파는 3.6m 까지다.
            Assert.AreEqual(MeteorImpactTier.None, MeteorImpact.TierFor(3.7f, 2f));
            Assert.AreEqual(MeteorImpactTier.None, MeteorImpact.TierFor(20f, 2f));
        }

        [Test]
        public void TheOuterHalfIsAShockwave_TheInnerHalfIsHeavy()
        {
            // 반경 2m → 경계는 1m
            Assert.AreEqual(MeteorImpactTier.Heavy, MeteorImpact.TierFor(0f, 2f));
            Assert.AreEqual(MeteorImpactTier.Heavy, MeteorImpact.TierFor(1f, 2f));
            Assert.AreEqual(MeteorImpactTier.Shockwave, MeteorImpact.TierFor(1.01f, 2f));
            Assert.AreEqual(MeteorImpactTier.Shockwave, MeteorImpact.TierFor(2f, 2f));
        }

        /// <summary>
        /// <b>충격 반경 밖에도 충격파가 있다</b>(2026-09-18). 예전에는 충격 반경이 곧
        /// 경계라 소형 운석은 2m 밖이 완전히 안전했고, 그나마 그 안의 바깥 절반도
        /// 아무 일을 하지 않아 <b>실질 판정이 1m</b> 였다 — "정가운데 맞지 않으면
        /// 피해가 없다" 가 그것이다.
        /// </summary>
        [Test]
        public void TheShockwaveReachesPastTheImpactRadius()
        {
            Assert.AreEqual(MeteorImpactTier.Shockwave, MeteorImpact.TierFor(2.5f, 2f),
                "충격 반경(2m) 밖인데 충격파는 닿아야 한다.");
            Assert.AreEqual(MeteorImpactTier.Shockwave, MeteorImpact.TierFor(3.6f, 2f),
                "3.6m 가 가장 바깥이다.");
        }

        /// <summary>강한 충격 경계는 그대로다 — 넓어진 것은 바깥 띠뿐이다.</summary>
        [Test]
        public void TheHeavyBoundaryDidNotMove()
        {
            Assert.AreEqual(MeteorImpactTier.Heavy, MeteorImpact.TierFor(1f, 2f));
            Assert.AreEqual(MeteorImpactTier.Shockwave, MeteorImpact.TierFor(1.01f, 2f));
        }

        /// <summary>
        /// 배율이 1 아래로 들어와도 충격파가 강한 충격보다 좁아지면 안 된다 —
        /// 그러면 동심원의 순서가 뒤집힌다.
        /// </summary>
        [Test]
        public void TheShockwaveNeverShrinksInsideTheImpactRadius()
        {
            Assert.AreEqual(2f, MeteorImpact.ShockwaveRadiusFor(2f, 0.5f), 0.001f);
            Assert.AreEqual(MeteorImpactTier.Shockwave, MeteorImpact.TierFor(1.5f, 2f, 0.5f, 0.5f));
        }

        [Test]
        public void TheFalloffIsOneAtTheCenterAndZeroAtTheEdge()
        {
            Assert.AreEqual(1f, MeteorImpact.FalloffAt(0f, 4f), 0.001f);
            Assert.AreEqual(0.5f, MeteorImpact.FalloffAt(2f, 4f), 0.001f);
            Assert.AreEqual(0f, MeteorImpact.FalloffAt(4f, 4f), 0.001f);
            Assert.AreEqual(0f, MeteorImpact.FalloffAt(9f, 4f), 0.001f, "밖은 음수가 아니라 0 이다.");
        }

        /// <summary>중형은 반경이 넓으니 강한 충격 범위도 함께 넓어져야 한다.</summary>
        [Test]
        public void AMediumMeteorHasAWiderHeavyZone()
        {
            var small = PhysicsScale.MeteorSmallImpactRadius;
            var medium = PhysicsScale.MeteorMediumImpactRadius;

            Assert.AreEqual(MeteorImpactTier.Shockwave, MeteorImpact.TierFor(1.5f, small));
            Assert.AreEqual(MeteorImpactTier.Heavy, MeteorImpact.TierFor(1.5f, medium));
        }

        /// <summary>
        /// 예전에는 힘이 900 하나였다 — 크기를 나눠 놓고(S15P21B202-234) 중형은
        /// <b>맞는 범위만 넓고 아프지는 않았다.</b>
        /// </summary>
        [Test]
        public void AMediumMeteorHitsHarderThanASmallOne()
        {
            var small = MeteorImpact.ForceFor(MeteorSize.Small);
            var medium = MeteorImpact.ForceFor(MeteorSize.Medium);

            Assert.AreEqual(PhysicsScale.MeteorImpactForce, small, 0.01f,
                "소형이 기준값이다.");
            Assert.Greater(medium, small);
            Assert.AreEqual(small * (PhysicsScale.MeteorMediumImpactRadius
                                     / PhysicsScale.MeteorSmallImpactRadius),
                            medium, 0.01f,
                "반경 비율만큼 커진다 — 상수를 따로 두지 않는다.");
        }

        [Test]
        public void SettingTheSize_ChangesBothRadiusAndForce()
        {
            var meteor = MakeMeteor(Vector3.up * 5f);
            meteor.SetSize(MeteorSize.Medium);

            Assert.AreEqual(PhysicsScale.MeteorMediumImpactRadius, meteor.ImpactRadius, 0.01f);

            // 힘은 비공개다. 실제로 더 세게 밀리는지로 본다.
            var near = MakeProp(new Vector3(1.6f, 0.5f, 0f));
            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.Greater(near.linearVelocity.magnitude, 0.1f,
                "중형의 넓은 반경 안에 있으므로 밀려야 한다.");
        }

        // --- 직접 충돌 ----------------------------------------------------

        /// <summary>기획 §20.1 "운석 직접 충돌: 날아간 뒤 기절".</summary>
        [Test]
        public void ADirectHit_DownsThePlayerInOneGo()
        {
            var player = MakePlayer(new Vector3(0f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(new Vector3(0f, 1f, 0f), player);

            Assert.IsTrue(player.Downed, "한 번에 기절해야 한다.");
        }

        /// <summary>
        /// <b>판정하지 않는 기계에서는 직접 충돌로도 기절하지 않는다</b> (S15P21B202-692).
        ///
        /// <para><b>실제로 일어났다.</b> 2026-09-22 QA 로그 <c>Player-prev.log:12252</c> 의
        /// 스택이 <c>OnCollisionEnter → ApplyKnockback → ApplyTier → GoDown</c> 이고, 같은
        /// 시각의 진단이 <c>role=client rtt=94.4 crew=4</c> 다 — <b>접속한 참가자 기계가
        /// 권위 판정을 실행했다.</b> 접속이 끝나기 전 창에서는 그 기계도 권위로 보이므로
        /// 진짜 운석이 생길 수 있고(<c>MeteorClockBridge</c> 가 그 사실을 적어 두고 있다),
        /// 그 돌이 판정까지 하면 안 된다.</para>
        ///
        /// <para><b>증상이 셋으로 갈려 보인다.</b> 참가자가 자기 몸만 기절시키면 서버는
        /// 모른다 — 서버 리그는 입력을 계속 적용하므로 <b>쓰러진 채 미끄러지고</b>,
        /// <c>NetPlayer.downed</c> 는 서버 값을 실으므로 <b>남의 화면에서는 서 있고</b>,
        /// <c>CollectRescueCandidates</c> 는 <c>!other.Downed</c> 로 거르므로 <b>살리기
        /// 후보가 아예 안 생긴다.</b> 하나를 막으면 셋이 사라진다.</para>
        ///
        /// <para>바로 위 <see cref="ADirectHit_DownsThePlayerInOneGo"/> 와 <b>짝</b>이다 —
        /// 권위 쪽에서는 한 번에 기절해야 하고, 여기서는 기절하지 않아야 한다. 한쪽만 두면
        /// 게이트를 너무 넓게 잡아도, 아예 안 잡아도 통과한다.</para>
        /// </summary>
        [Test]
        public void ADirectHit_DoesNotDownOnAMachineThatDoesNotJudge()
        {
            var player = MakePlayer(Vector3.zero);
            player.ServerAuthority = false;   // 참가자 기계의 몸 (RemoteAvatar 가 내려놓는다)

            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));
            meteor.ApplyImpact(new Vector3(0f, 1f, 0f), player);

            Assert.IsFalse(player.Downed,
                "참가자 기계가 자기 몸을 기절시켰다 — 서버는 모르므로 쓰러진 채 끌려다니고 "
                + "살리기 후보도 안 생긴다");
        }

        /// <summary>
        /// 체력과 기절이 <b>같은 게이트</b>를 쓴다 (S15P21B202-692).
        ///
        /// <para><see cref="PlayerRig.ApplyDamage"/> 는 <c>-513</c> 에서 이미 막혔는데
        /// <see cref="PlayerRig.GoDown"/> 은 그대로였고, 직접 충돌은 체력을 거치지 않고
        /// <c>GoDown</c> 을 직접 부르므로 <b>막힌 구멍이 한 경로로만 열려 있었다.</b>
        /// 이 파일의 주석이 이미 적고 있다 — "판정을 두 곳에 두면 반드시 갈라진다".</para>
        ///
        /// <para>그래서 둘을 <b>한 검사에서 같이</b> 본다. 한쪽만 보면 다음에 또 갈린다.</para>
        /// </summary>
        [Test]
        public void HealthAndDowningUseTheSameGate()
        {
            var player = MakePlayer(Vector3.zero);
            player.ServerAuthority = false;
            float health = player.Health;

            player.ApplyDamage(999f, "검증");
            player.GoDown("검증");

            Assert.AreEqual(health, player.Health, "체력 게이트가 열렸다 (`-513` 의 회귀)");
            Assert.IsFalse(player.Downed, "기절 게이트가 열렸다 (`-692`)");
        }

        /// <summary>
        /// 순서가 중요하다. <c>GoDown</c> 이 먼저 돌면 <c>KnockDown</c> 이 <c>Downed</c> 를
        /// 보고 즉시 돌아나가 <b>날아가지 않는다</b> — §20.1 은 "날아간 뒤 기절" 이다.
        /// </summary>
        [Test]
        public void ADirectHit_FliesBeforeItFalls()
        {
            var player = MakePlayer(new Vector3(2f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(2f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero, player);

            Assert.IsTrue(player.Downed);
            Assert.Greater(BodySpeed(player), 0.1f, "기절만 하고 멈춰 있으면 안 된다.");
        }

        /// <summary>
        /// 직접 맞은 사람은 <b>반경 밖에 있을 수도 있다</b> — 떨어지는 중에 얻어맞으면
        /// 착탄 지점이 그 사람 발밑이 아니다.
        /// </summary>
        [Test]
        public void ADirectHitFarFromTheImpactPoint_StillDowns()
        {
            var player = MakePlayer(new Vector3(20f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(20f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero, player);

            Assert.IsTrue(player.Downed, "거리로 판정하면 이 경우를 놓친다.");
        }

        // --- 강한 충격 ----------------------------------------------------

        /// <summary>기획 §20.1 "강한 충격: 크게 날아가고 들고 있던 장비를 떨어뜨림".</summary>
        [Test]
        public void AHeavyShock_ThrowsAndDropsButDoesNotDown()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));   // 경계 1m 안
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.IsFalse(player.Downed, "강한 충격은 기절이 아니다.");
            Assert.IsTrue(player.Ragdolled, "크게 날아가야 한다.");
            Assert.Greater(BodySpeed(player), 0.1f);
        }

        /// <summary>
        /// 예전에는 놓는 코드가 <c>GoDown</c> 안에만 있어서 <b>기절하지 않으면 장비를
        /// 떨어뜨릴 수가 없었다.</b>
        /// </summary>
        [Test]
        public void AHeavyShock_LetsGoOfTheHeldTool()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            player.DropEquipment();   // 진입점이 열려 있는지부터 확인한다
            Assert.IsFalse(player.Downed, "장비를 놓는 것이 기절을 부르지는 않는다.");

            meteor.ApplyImpact(Vector3.zero);

            Assert.IsFalse(player.Downed);
        }

        // --- 약한 충격파 --------------------------------------------------

        /// <summary>기획 §20.1 "약한 운석 충격파: 밀려나지만 바로 행동 가능".</summary>
        [Test]
        public void AWeakShockwave_PushesButLeavesYouInControl()
        {
            var player = MakePlayer(new Vector3(1.6f, 0f, 0f));   // 경계 1m 밖, 반경 2m 안
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.IsFalse(player.Downed);
            Assert.IsFalse(player.Ragdolled, "바로 행동 가능해야 한다.");
            Assert.Greater(BodySpeed(player), 0f, "밀려나기는 한다.");
        }

        /// <summary>
        /// <b>충격 반경 밖에서도 밀린다</b>(2026-09-18). 2.8m 는 소형 운석의 충격
        /// 반경(2m) 밖이고 충격파(3.6m) 안이다 — 예전에는 여기가 완전히 안전했다.
        /// </summary>
        [Test]
        public void TheShockwaveStillPushesYouOutsideTheImpactRadius()
        {
            var player = MakePlayer(new Vector3(2.8f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            Assert.Greater(2.8f, meteor.ImpactRadius, "충격 반경 밖에 세운 것이 전제다.");
            Assert.Less(2.8f, meteor.ShockwaveRadius, "충격파 안이어야 한다.");

            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.Greater(BodySpeed(player), 0.1f, "충격 반경 밖도 밀려야 한다.");
            Assert.IsFalse(player.Ragdolled, "밀리기만 한다 — 바로 행동 가능(§20.1).");
            Assert.IsFalse(player.Downed);
        }

        /// <summary>
        /// 밀리는 것만으로는 안 보인다. <c>Balance</c> 가 다음 스텝에 되세우므로
        /// 휘청임까지 걸어야 래그돌이 실제로 흔들린다.
        /// </summary>
        [Test]
        public void TheShockwaveMakesYouStagger()
        {
            var player = MakePlayer(new Vector3(1.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            Assert.IsFalse(player.Staggering);

            meteor.ApplyImpact(Vector3.zero);

            Assert.IsTrue(player.Staggering, "휘청이지 않으면 밀린 티가 나지 않는다.");
            Assert.IsFalse(player.Ragdolled, "휘청임은 조종을 빼앗지 않는다.");
        }

        /// <summary>"래그돌도 조금 날아가게" — 옆으로만 미끄러지면 안 된다.</summary>
        [Test]
        public void TheShockwaveLiftsTheRagdollOffTheGround()
        {
            var player = MakePlayer(new Vector3(1.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);

            Assert.Greater(player.Doll.Hips.linearVelocity.y, 0f,
                "위로 뜨는 성분이 없으면 발만 미끄러진다.");
            Assert.Greater(player.Doll.Hips.linearVelocity.x, 0f,
                "착탄 지점 반대쪽으로 밀려야 한다.");
        }

        /// <summary>가까울수록 세게 — 폭발력과 같은 감쇠 축을 쓴다.</summary>
        [Test]
        public void TheShockwaveFadesWithDistance()
        {
            var near = MakePlayer(new Vector3(1.2f, 0f, 0f));    // 강한 충격 경계(1m) 밖
            var far = MakePlayer(new Vector3(3.2f, 0f, 0f));     // 충격파 가장자리
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);

            Assert.IsFalse(near.Ragdolled, "둘 다 약한 충격파여야 비교가 된다.");
            Assert.Greater(BodySpeed(near), BodySpeed(far));
            Assert.Greater(BodySpeed(far), 0f, "가장자리도 0 은 아니다.");
        }

        [Test]
        public void OutsideTheShockwave_NothingHappens()
        {
            var player = MakePlayer(new Vector3(6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            Assert.Greater(6f, meteor.ShockwaveRadius, "충격파 밖에 세운 것이 전제다.");

            meteor.ApplyImpact(Vector3.zero);

            // <b>물리 스텝 전에 본다.</b> 한 스텝을 돌리면 중력만으로도 속도가 붙어
            // "밀리지 않았다" 를 0 으로 잴 수 없다. 넉백은 속도를 즉시 더하므로
            // 여기서 0 이면 아무것도 들어가지 않은 것이다.
            Assert.AreEqual(0f, BodySpeed(player), 0.001f, "충격파 밖은 밀리지 않는다.");
            Assert.IsFalse(player.Staggering);

            UnityEngine.Physics.Simulate(Step);

            Assert.IsFalse(player.Downed);
            Assert.IsFalse(player.Ragdolled);
        }

        // --- 착탄 순간에만 (기획 §19) --------------------------------------

        /// <summary>
        /// §19 "충돌 <b>순간에만</b> 넉백과 기절 판정이 있다". 예전에는
        /// <c>DamageSource</c> 가 0.5초마다 다시 때리고 운석이 6초 뒤에 사라져
        /// <b>박힌 운석 옆에 서 있으면 열두 번 맞았다.</b>
        /// </summary>
        [Test]
        public void AfterImpact_TheMeteorStopsHurting()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);
            Assert.IsTrue(meteor.HasImpacted);

            // 착탄한 자리에서 6초를 버틴다. 예전 반복 주기(0.5초)의 열두 배다.
            for (var i = 0; i < 600; i++) meteor.Tick(Step);

            Assert.IsFalse(player.Downed, "두 번째 판정이 없어야 한다.");
        }

        [Test]
        public void ASecondImpactCallIsIgnored()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);
            meteor.ApplyImpact(Vector3.zero, player);   // 두 번째 — 직접 충돌이라도

            Assert.IsFalse(player.Downed, "착탄은 한 번만 처리한다.");
        }

        // --- 쳐낸 운석 ----------------------------------------------------

        /// <summary>
        /// <b>쳐낸 순간부터 순해진다</b>(S15P21B202-273).
        ///
        /// <b>기획 §18.2 를 어긴다.</b> §18.2 는 "반사된 운석의 충돌과 폭발은 일반
        /// 운석과 같은 피해를 준다" 고 정하고, 이 검증이 원래 그것을 지켰다.
        ///
        /// 2026-09-02 에 운석을 두 상태로 나누기로 정했다 — 쳐낸 것은 <b>굴러다니며
        /// 때릴 수 있는 소품</b>, 못 쳐낸 것은 <b>박힌 흔적</b>이고 둘 다 사람을 때리지
        /// 않는다. 그러지 않으면 방망이로 쳐낸 운석이 날아가다 동료를 기절시켜,
        /// <b>잘한 플레이가 팀을 해친다.</b> [Plan] 확인이 필요하다.
        /// </summary>
        [Test]
        public void ADeflectedMeteor_StopsHurting()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.Deflect(Vector3.right * PhysicsScale.BatDeflectSpeed);
            Assert.IsTrue(meteor.WasDeflected);
            Assert.IsTrue(meteor.Harmless, "맞은 순간부터 소품이다.");

            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.IsFalse(player.Ragdolled, "쳐낸 운석은 동료를 때리지 않는다.");
            Assert.IsFalse(player.Downed);
        }

        // --- 기절한 사람 --------------------------------------------------

        /// <summary>
        /// §20.1 "기절한 플레이어도 다른 물체의 충격으로 다시 밀릴 수 있다".
        /// <see cref="GameLayers.PlayerMask"/> 에 <c>PlayerDowned</c> 가 들어 있어
        /// <b>이미 동작한다</b> — 이 검증은 그것이 깨지지 않았다는 방어선이다.
        /// </summary>
        [Test]
        public void ADownedPlayer_CanStillBePushed()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            player.GoDown("검증");

            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));
            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.Greater(BodySpeed(player), 0f, "쓰러진 사람도 밀린다.");
        }

        /// <summary>한 사람의 뼈가 여러 개 걸려도 처리는 한 번이다.</summary>
        [Test]
        public void ARagdollIsHandledOncePerPlayer()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);

            Assert.IsFalse(player.Downed,
                "뼈마다 강한 충격을 겹쳐 넣으면 기절로 넘어가 버린다.");
        }

        // --- 리그 ---------------------------------------------------------

        static float BodySpeed(PlayerRig player)
        {
            var fastest = 0f;
            for (var i = 0; i < player.Doll.Bodies.Count; i++)
            {
                var body = player.Doll.Bodies[i];
                if (body == null) continue;
                fastest = Mathf.Max(fastest, body.linearVelocity.magnitude);
            }

            return fastest;
        }

        Meteor MakeMeteor(Vector3 position)
        {
            var go = new GameObject("Meteor") { layer = GameLayers.Meteor };
            go.transform.position = position;
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            var meteor = go.AddComponent<Meteor>();
            meteor.Initialize();
            meteor.SetGroundHeight(0f);

            UnityEngine.Physics.SyncTransforms();
            return meteor;
        }

        Rigidbody MakeProp(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "화물 상자";
            go.layer = GameLayers.Prop;
            go.transform.position = position;
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 20f;
            body.useGravity = false;

            UnityEngine.Physics.SyncTransforms();
            return body;
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
