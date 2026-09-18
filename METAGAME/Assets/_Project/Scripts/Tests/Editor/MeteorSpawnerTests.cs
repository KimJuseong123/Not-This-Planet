using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 예고·낙하 타이밍, 난이도 곡선, 시드 재현성을 검증한다.
    ///
    /// MeteorSpawner 가 Tick(deltaTime) 을 받기 때문에 3분치 난이도 곡선을
    /// PlayMode 없이 몇 밀리초 만에 확인할 수 있다.
    /// </summary>
    public sealed class MeteorSpawnerTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            // 스포너가 만든 운석까지 정리한다.
            foreach (var meteor in Object.FindObjectsByType<Meteor>(FindObjectsSortMode.None))
                if (meteor != null)
                    Object.DestroyImmediate(meteor.gameObject);

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        // --- 예고와 낙하 -----------------------------------------------------

        /// <summary>
        /// <b>예고와 운석이 함께 나온다</b>(기획 §17.2 "착탄 3초 전에 표시").
        ///
        /// 예전에는 반대를 검증했다 — "예고 중에는 운석이 아직 없어야 한다". 그런데 그
        /// 구조에서는 예고가 <b>끝난 뒤</b> 운석이 30m 위에 생겨 4.63초를 더 떨어졌고,
        /// 결과적으로 표시가 착탄 4.63초 전에 사라졌다. S15P21B202-234 에서 뒤집었다.
        /// </summary>
        [Test]
        public void Warning_AndMeteor_AppearTogether()
        {
            var spawner = CreateSpawner();

            AdvanceUntilPending(spawner);

            Assert.AreEqual(1, spawner.Pending.Count, "예고가 떠야 한다.");
            Assert.AreEqual(1, spawner.Live.Count,
                "예고가 켜져 있는 동안 운석이 실제로 떨어지고 있어야 한다.");
        }

        /// <summary>
        /// 예고가 끝나는 순간이 <b>착탄 시각</b>이어야 한다.
        ///
        /// S15P21B202-233 의 <c>Meteor.TryPredictImpact</c> 로 실제 착탄 시각을 재어
        /// 예고 시간과 맞는지 본다 — 이 둘이 어긋난 것이 S15P21B202-234 의 시작이었다.
        /// </summary>
        [Test]
        public void TheWarningEndsWhenTheMeteorLands()
        {
            var spawner = CreateSpawner();

            AdvanceUntilPending(spawner);

            var meteor = spawner.Live[0];
            var remaining = spawner.Pending[0].RemainingSeconds;

            Assert.IsTrue(meteor.TryPredictImpact(out var seconds, out _));
            Assert.AreEqual(remaining, seconds, 0.15f,
                $"예고 {remaining:0.00}초와 실제 착탄 {seconds:0.00}초가 맞아야 한다.");
        }

        /// <summary>
        /// <b>더 이상 착지 지점 바로 위가 아니다</b>(S15P21B202-274). 대각선으로
        /// 오므로 수평으로 떨어진 곳에서 시작한다 — 자세한 검증은
        /// <see cref="MeteorDiagonalFallTests"/> 에 있고, 여기서는 "하늘에서 낙하한다"
        /// (§17.1)와 수직 속도만 지킨다.
        /// </summary>
        [Test]
        public void TheMeteorFallsFromAboveWithAnInitialPush()
        {
            var spawner = CreateSpawner();
            AdvanceUntilPending(spawner);

            var point = spawner.Pending[0].ImpactPoint;
            var meteor = spawner.Live[0];

            Assert.Greater(meteor.transform.position.y, point.y + 1f,
                "하늘에서 떨어져야 한다(기획 §17.1 \"하늘에서 낙하한다\").");

            var velocity = meteor.GetComponent<Rigidbody>().linearVelocity;
            Assert.Less(velocity.y, 0f, "예고 시간에 맞추려면 아래로 밀려 시작해야 한다.");
            Assert.AreEqual(PhysicsScale.MeteorDropSpeed, -velocity.y, 0.2f,
                "수직 속도는 대각선이 되어도 그대로다 — 착지 시각이 예고와 맞아야 한다.");
        }

        [Test]
        public void SpawnedMeteor_UsesMeteorLayerAndMass()
        {
            var spawner = CreateSpawner();
            AdvanceUntilPending(spawner);

            var meteor = spawner.Live[0];

            Assert.AreEqual(GameLayers.Meteor, meteor.gameObject.layer,
                "Meteor 레이어여야 운석끼리 충돌하지 않는다.");
            Assert.AreEqual(PhysicsScale.MeteorMass, meteor.GetComponent<Rigidbody>().mass, 0.01f);
        }

        /// <summary>
        /// 예전에는 이 검증이 <c>DamageSource</c> 가 <b>있는지</b>를 잠갔다. 그것이
        /// 문제였다 — "닿으면 25" 하나뿐이라 기획 §20.1 의 세 단계가 모두 같아지고,
        /// <c>MaxHealth</c> 100 에서 네 번을 맞아야 기절했다. 착탄 후에도 0.5초마다
        /// 다시 때려 §19 의 "충돌 순간에만" 도 어겼다. S15P21B202-247 에서 뒤집었다.
        /// </summary>
        [Test]
        public void SpawnedMeteor_DoesNotCarryDamageSource()
        {
            var spawner = CreateSpawner();
            AdvanceUntilPending(spawner);

            Assert.IsNull(spawner.Live[0].GetComponent<DamageSource>(),
                "Meteor 가 §20.1 의 단계를 직접 정한다. 균일 피해가 섞이면 안 된다.");
        }

        // --- 공세 구조 (기획 §17.4) ------------------------------------------

        /// <summary>
        /// 시간대 표(§17.4)가 바탕이고, 거기에 경과 시간이 곱해진다(2026-09-18).
        ///
        /// <b>바탕값은 그대로 잠근다.</b> 0분에서는 조임이 ×1 이므로 표의 30초가 그대로
        /// 나와야 한다 — 여기가 어긋나면 표를 잘못 읽고 있는 것이다.
        /// </summary>
        [Test]
        public void WaveInterval_StartsFromTheDesignTable()
        {
            var spawner = CreateSpawner();

            Assert.AreEqual(30f, spawner.IntervalAt(0f), 0.01f, "0분에는 조임이 없다 — 표 그대로.");

            // 이후는 표 ÷ 조임배율. 15분 램프이므로 6분이면 ×1.4 다.
            Assert.AreEqual(25f / MeteorEscalation.FactorAt(6f * 60f),
                            spawner.IntervalAt(6f * 60f), 0.01f);
            Assert.AreEqual(20f / MeteorEscalation.FactorAt(11f * 60f),
                            spawner.IntervalAt(11f * 60f), 0.01f);
        }

        /// <summary>
        /// <b>20분에서 멈추지 않는다</b>(2026-09-18).
        ///
        /// 예전에는 §17.4 의 마지막 칸("20분~ · 15초")이 영원히 이어져,
        /// <c>Interval_StopsShrinkingAtTheLastRow</c> 가 바로 그것을 잠그고 있었다.
        /// 플레이 확인에서 "진행될수록 점점 더 강하게" 가 나온 자리다 — 끝이 평평하면
        /// 오래 끌어도 불리하지 않아 발사를 서두를 이유가 없다.
        /// </summary>
        [Test]
        public void WaveInterval_KeepsTighteningPastTheLastRow()
        {
            var spawner = CreateSpawner();

            var twenty = spawner.IntervalAt(20f * 60f);
            var forty = spawner.IntervalAt(40f * 60f);

            Assert.Less(forty, twenty, "마지막 시간대 뒤로도 계속 조여야 한다.");
        }

        /// <summary>
        /// 아무리 조여도 §17.3 의 신호 시간보다 짧아지지는 않는다.
        ///
        /// 더 짧으면 5초 전 신호가 <b>이전 공세 안에서</b> 울려 무엇을 예고하는지 알 수
        /// 없다 — 신호를 못 믿게 되면 예고 자체가 무의미해진다.
        /// </summary>
        [Test]
        public void WaveInterval_NeverGoesBelowTheWarningLead()
        {
            var spawner = CreateSpawner();

            Assert.GreaterOrEqual(spawner.IntervalAt(600f * 60f),
                                  PhysicsScale.MeteorEscalationIntervalFloor - 0.01f,
                                  "열 시간이 지나도 바닥은 지켜야 한다.");
        }

        /// <summary>
        /// 공세가 알린 개수만큼 <b>1~2초 간격으로</b> 떨어진다(기획 §17.4).
        ///
        /// 개수를 상수로 쓰지 않는다. 0~5분 구간은 1~2개이고 난수가 정하므로, 신호가
        /// 말한 수를 받아 그만큼 오는지를 본다.
        /// </summary>
        [Test]
        public void AWave_DropsWhatItAnnounced_SpacedApart()
        {
            var spawner = CreateSpawner();

            var planned = 0;
            var dropped = 0;
            void OnIncoming(int size) => planned = size;
            void OnDropped(PendingMeteorDrop _) => dropped++;

            spawner.WaveIncoming += OnIncoming;
            spawner.DropScheduled += OnDropped;

            AdvanceUntilPending(spawner);
            Assert.Greater(planned, 0, "신호가 먼저 와야 한다.");

            // 공세가 다 나올 만큼 전진한다. 하나당 최대 2초다.
            Advance(spawner, PhysicsScale.MeteorWithinWaveMaxSeconds * (planned + 1));

            spawner.WaveIncoming -= OnIncoming;
            spawner.DropScheduled -= OnDropped;

            // <b>떨어진 수를 센다.</b> Live 는 착탄 6초 뒤 사라지므로 시간이 지난 뒤
            // 세면 0 이 된다 — 처음에 그렇게 썼다가 알았다.
            Assert.AreEqual(planned, dropped, $"알린 {planned}개가 모두 떨어져야 한다.");
            Assert.IsFalse(spawner.WaveActive, "공세가 끝나야 한다.");
        }

        /// <summary>공세가 끝나면 다음 공세까지 <b>간격만큼</b> 쉰다.</summary>
        [Test]
        public void BetweenWaves_NothingFalls()
        {
            var spawner = CreateSpawner();

            var planned = 0;
            var dropped = 0;
            void OnIncoming(int size) => planned = size;
            void OnDropped(PendingMeteorDrop _) => dropped++;

            spawner.WaveIncoming += OnIncoming;
            spawner.DropScheduled += OnDropped;

            AdvanceUntilPending(spawner);
            Advance(spawner, PhysicsScale.MeteorWithinWaveMaxSeconds * (planned + 1));

            var afterWave = dropped;
            Assert.IsFalse(spawner.WaveActive);

            // 다음 신호가 오기 전까지는 아무것도 늘지 않는다.
            Advance(spawner, spawner.IntervalAt(spawner.Elapsed)
                             - PhysicsScale.MeteorWaveLeadSeconds - 1f);
            spawner.WaveIncoming -= OnIncoming;
            spawner.DropScheduled -= OnDropped;

            Assert.AreEqual(afterWave, dropped, "공세 사이에는 떨어지지 않는다.");
        }

        /// <summary>기획 §17.3 — 공세 약 5초 전에 신호가 나간다.</summary>
        [Test]
        public void AWaveIsAnnouncedInAdvance()
        {
            var spawner = CreateSpawner();

            var announced = 0;
            var sizeAtSignal = 0;
            void OnIncoming(int size) { announced++; sizeAtSignal = size; }

            spawner.WaveIncoming += OnIncoming;
            Advance(spawner, spawner.IntervalAt(0f) - PhysicsScale.MeteorWaveLeadSeconds + 0.05f);

            Assert.AreEqual(1, announced, "공세 5초 전에 한 번만 알려야 한다.");
            Assert.AreEqual(0, spawner.Live.Count, "신호 시점에는 아직 떨어지지 않는다.");

            AdvanceUntilPending(spawner);
            spawner.WaveIncoming -= OnIncoming;

            Assert.AreEqual(sizeAtSignal, spawner.WaveRemaining + 1,
                "신호가 말한 개수와 실제 공세 개수가 같아야 한다.");
        }

        // --- 인원수 보정 (기획 §17.6) ----------------------------------------

        [Test]
        public void FewerPlayers_MeanFewerMeteors_ButTheSameInterval()
        {
            var solo = CreateSpawner();
            solo.SetCrew(1);

            var full = CreateSpawner();
            full.SetCrew(4);

            Assert.AreEqual(full.IntervalAt(0f), solo.IntervalAt(0f), 0.01f,
                "간격은 인원수와 무관하다(기획 §17.6).");

            // 20분 이후는 4명 기준 5개다. 1명이면 45% 로 줄어야 한다.
            var late = 25f * 60f;
            var fourCrew = MeteorWaveTable.WaveSizeAt(late, 4, 0.5);
            var oneCrew = MeteorWaveTable.WaveSizeAt(late, 1, 0.5);

            Assert.Less(oneCrew, fourCrew, "혼자면 운석이 줄어야 한다.");
            Assert.GreaterOrEqual(oneCrew, 1, "0 개가 되면 신호만 울리고 아무것도 안 온다.");
        }

        // --- 소형과 중형 (기획 §17.7) ----------------------------------------

        [Test]
        public void MediumMeteors_DoNotAppearBeforeTenMinutes()
        {
            var spawner = CreateSpawner();

            Assert.AreEqual(0f, spawner.MediumChanceAt(0f), 0.001f);
            Assert.AreEqual(0f, spawner.MediumChanceAt(9f * 60f), 0.001f);
            Assert.Greater(spawner.MediumChanceAt(11f * 60f), 0f, "10분 이후에는 나온다.");
        }

        [Test]
        public void MediumChance_RisesWithTime()
        {
            var spawner = CreateSpawner();

            var ten = spawner.MediumChanceAt(11f * 60f);
            var fifteen = spawner.MediumChanceAt(16f * 60f);
            var twenty = spawner.MediumChanceAt(25f * 60f);
            var late = spawner.MediumChanceAt(45f * 60f);

            Assert.Greater(fifteen, ten);
            Assert.Greater(twenty, fifteen);
            Assert.Greater(late, twenty, "20분 뒤로도 계속 올라야 한다(2026-09-18).");
            Assert.LessOrEqual(late, PhysicsScale.MeteorEscalationMediumCap + 0.001f,
                "상한은 있다 — 소형이 다수로 남아야 방망이가 쓸모 있다.");
        }

        /// <summary>
        /// <b>표가 0 인 동안은 조임도 0 이다</b>(2026-09-18).
        ///
        /// 경과 시간만 보고 확률을 더하면 §17.7 의 "중형 · 등장 10분 이후" 가 깨져
        /// <b>1분에도 중형이 나온다.</b> 등장 시점을 정하는 것은 여전히 표다.
        /// </summary>
        [Test]
        public void Escalation_DoesNotOpenTheMediumDoorEarly()
        {
            var spawner = CreateSpawner();

            Assert.AreEqual(0f, spawner.MediumChanceAt(1f * 60f), 0.001f);
            Assert.AreEqual(0f, spawner.MediumChanceAt(9.9f * 60f), 0.001f);
        }

        [Test]
        public void SizeDecidesTheKnockbackRadius()
        {
            var spawner = CreateSpawner();
            AdvanceUntilPending(spawner);

            var meteor = spawner.Live[0];

            Assert.AreEqual(MeteorSize.Small, meteor.Size, "초반에는 소형만 나온다.");

            // <b>스포너의 배율을 거쳐 본다</b>(2026-09-18). 경과 시간이 반경을 조이므로
            // (<see cref="MeteorEscalation.RadiusScaleFor"/>) §17.7 의 생값 2m·3.5m 을
            // 그대로 기대하면 몇 초만 지나도 어긋난다. 여기서 재려는 것은 "크기가 반경을
            // 정한다" 이지 "반경이 정확히 2m 다" 가 아니다 — 후자는 MeteorImpactTierTests 몫이다.
            Assert.AreEqual(spawner.ScaledImpactRadiusFor(MeteorSize.Small),
                            meteor.ImpactRadius, 0.01f);

            meteor.SetSize(MeteorSize.Medium);
            Assert.AreEqual(spawner.ScaledImpactRadiusFor(MeteorSize.Medium),
                            meteor.ImpactRadius, 0.01f);

            Assert.Greater(spawner.ScaledImpactRadiusFor(MeteorSize.Medium),
                           spawner.ScaledImpactRadiusFor(MeteorSize.Small),
                           "중형이 더 넓다 — 배율은 둘에 같이 곱해진다.");
        }

        [Test]
        public void Warning_ExposesImpactPointAndProgress()
        {
            var spawner = CreateSpawner();

            // 낙하 간격이 시간에 따라 줄어들므로 예고 시점은 초기 간격보다 이르다.
            // 고정 시간을 전진하지 않고 예고가 뜨는 순간을 잡는다.
            AdvanceUntilPending(spawner);
            var drop = spawner.Pending[0];

            Assert.AreEqual(0f, drop.Progress01, 0.05f, "예고 직후 진행도는 0 에 가깝다.");

            Advance(spawner, PhysicsScale.MeteorWarningSeconds * 0.5f);
            Assert.AreEqual(0.5f, spawner.Pending[0].Progress01, 0.05f);

            Assert.AreEqual(drop.ImpactPoint, spawner.Pending[0].ImpactPoint,
                "예고 중 착탄 지점이 바뀌면 플레이어가 피할 수 없다.");
        }

        // --- 난이도 곡선 -----------------------------------------------------

        /// <summary>
        /// 간격이 <b>계단으로</b> 줄어든다. 예전에는 8초에서 2초까지 선형이었고,
        /// S15P21B202-234 에서 기획 §17.4 의 표(30 → 25 → 20 → 15)로 바꿨다.
        /// </summary>
        [Test]
        public void Interval_ShrinksInSteps()
        {
            var spawner = CreateSpawner();

            var early = spawner.IntervalAt(0f);
            var mid = spawner.IntervalAt(11f * 60f);
            var late = spawner.IntervalAt(21f * 60f);

            Assert.Greater(early, mid, "시간이 지나면 짧아져야 한다.");
            Assert.Greater(mid, late);
        }

        /// <summary>
        /// <b>공세는 커지기도 한다</b>(2026-09-18). 간격만 줄면 "같은 것이 자주 온다" 고,
        /// 개수까지 늘어야 "점점 더 강해진다" 가 된다.
        ///
        /// 스포너를 굴리지 않고 순수 계산을 본다 — 개수는 난수로 뽑히므로 한 번 굴려서는
        /// 곡선이 올라갔는지 알 수 없고, 여러 번 굴리면 예고가 쌓여 세기가 어긋난다.
        /// </summary>
        [Test]
        public void LateWaves_CarryMoreMeteorsThanEarlyOnes()
        {
            // §17.4 마지막 칸의 5개가 바탕이다.
            var early = MeteorEscalation.CountFor(5, 0f);
            var late = MeteorEscalation.CountFor(5, 30f * 60f);

            Assert.AreEqual(5, early, "0분에는 조임이 없다 — 표 그대로.");
            Assert.Greater(late, early, $"초반 {early}개 · 후반 {late}개");
            Assert.LessOrEqual(late, PhysicsScale.MeteorEscalationCountCap,
                "상한 위로는 올라가지 않는다.");
        }

        [Test]
        public void LateGame_DropsMoreOftenThanEarlyGame()
        {
            var spawner = CreateSpawner();

            var early = CountScheduled(spawner, 0f, 40f);
            var late = CountScheduled(spawner, PhysicsScale.MeteorRampSeconds, 40f);

            Assert.Greater(late, early,
                $"후반이 더 자주 떨어져야 한다. 초반 {early}회 / 후반 {late}회");
        }

        // --- 시드 재현성 -----------------------------------------------------

        [Test]
        public void SameSeed_ProducesSameImpactPoints()
        {
            var first = PointsFrom(CreateSpawner(seed: 4242), 6);
            var second = PointsFrom(CreateSpawner(seed: 4242), 6);

            CollectionAssert.AreEqual(first, second,
                "같은 시드면 같은 순서로 떨어져야 한다. 4인이 같은 장면을 봐야 하기 때문이다.");
        }

        [Test]
        public void DifferentSeed_ProducesDifferentImpactPoints()
        {
            var first = PointsFrom(CreateSpawner(seed: 1), 6);
            var second = PointsFrom(CreateSpawner(seed: 2), 6);

            CollectionAssert.AreNotEqual(first, second);
        }

        [Test]
        public void ResetRun_RewindsToTheSameSequence()
        {
            var spawner = CreateSpawner(seed: 777);

            var first = PointsFrom(spawner, 4);
            spawner.ResetRun();
            var second = PointsFrom(spawner, 4);

            CollectionAssert.AreEqual(first, second, "재시도하면 같은 순서로 다시 떨어져야 한다.");
        }

        [Test]
        public void ImpactPoints_StayInsideArea()
        {
            var spawner = CreateSpawner();
            spawner.SetArea(spawner.transform, new Vector2(20f, 10f));
            spawner.ResetRun();

            foreach (var point in PointsFrom(spawner, 20))
            {
                Assert.LessOrEqual(Mathf.Abs(point.x), 10.001f, $"x 가 영역을 벗어났다: {point.x}");
                Assert.LessOrEqual(Mathf.Abs(point.z), 5.001f, $"z 가 영역을 벗어났다: {point.z}");
            }
        }

        // --- 헬퍼 ------------------------------------------------------------

        MeteorSpawner CreateSpawner(int seed = 12345)
        {
            var go = new GameObject("MeteorSpawner");
            _spawned.Add(go);

            var spawner = go.AddComponent<MeteorSpawner>();
            spawner.Configure(seed);
            return spawner;
        }

        /// <summary>예고가 하나 뜰 때까지 전진한다.</summary>
        static void AdvanceUntilPending(MeteorSpawner spawner)
        {
            var guard = 0;
            while (spawner.Pending.Count == 0 && guard++ < 200000) spawner.Tick(Tuning.FixedStep);
        }

        static void Advance(MeteorSpawner spawner, float seconds)
        {
            var steps = Mathf.RoundToInt(seconds / Tuning.FixedStep);
            for (var i = 0; i < steps; i++) spawner.Tick(Tuning.FixedStep);
        }

        /// <summary>예고된 착탄 지점을 원하는 개수만큼 모은다.</summary>
        static List<Vector3> PointsFrom(MeteorSpawner spawner, int count)
        {
            var points = new List<Vector3>();
            void OnScheduled(PendingMeteorDrop drop) => points.Add(drop.ImpactPoint);

            spawner.DropScheduled += OnScheduled;

            var guard = 0;
            while (points.Count < count && guard++ < 200000) spawner.Tick(Tuning.FixedStep);

            spawner.DropScheduled -= OnScheduled;
            return points.GetRange(0, Mathf.Min(count, points.Count));
        }

        /// <summary>지정 시각부터 일정 구간 동안 몇 번 예고되는지 센다.</summary>
        static int CountScheduled(MeteorSpawner spawner, float startAt, float window)
        {
            spawner.ResetRun();
            Advance(spawner, startAt);

            var count = 0;
            void OnScheduled(PendingMeteorDrop _) => count++;

            spawner.DropScheduled += OnScheduled;
            Advance(spawner, window);
            spawner.DropScheduled -= OnScheduled;

            return count;
        }
    }
}
