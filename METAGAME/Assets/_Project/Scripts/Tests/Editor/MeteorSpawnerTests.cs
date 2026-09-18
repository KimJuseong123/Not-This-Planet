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
        /// 컷신 전에는 가랑비다(2026-09-18). 경과 시간을 아무리 옮겨도 그대로다 —
        /// 강도를 정하는 것은 시계가 아니라 공세 컷신이다
        /// (<see cref="MeteorIntensity"/>).
        /// </summary>
        [Test]
        public void BeforeTheCutscene_TheIntervalIsTheDrizzle()
        {
            var spawner = CreateSpawner();

            Assert.AreEqual(PhysicsScale.MeteorDrizzleIntervalSeconds,
                            spawner.IntervalAt(0f), 0.01f);
            Assert.AreEqual(PhysicsScale.MeteorDrizzleIntervalSeconds,
                            spawner.IntervalAt(30f * 60f), 0.01f,
                            "30분이 지나도 컷신 전이면 그대로다.");
        }

        /// <summary>
        /// 컷신을 지나면 바로 최대다. <b>서서히 오르지 않는다</b> —
        /// 예전 시간 램프를 대체한 자리다.
        /// </summary>
        [Test]
        public void AfterTheCutscene_TheIntervalJumpsToFullBlast()
        {
            var spawner = CreateSpawner();
            spawner.SetAssault(true);

            Assert.AreEqual(PhysicsScale.MeteorFullBlastIntervalSeconds,
                            spawner.IntervalAt(0f), 0.01f,
                            "컷신 직후부터 최대여야 한다.");
            Assert.AreEqual(PhysicsScale.MeteorFullBlastIntervalSeconds,
                            spawner.IntervalAt(30f * 60f), 0.01f,
                            "그 뒤로 더 조여지지도 않는다.");
        }

        /// <summary>
        /// <b>공세 배율을 두 번 곱하지 않는다.</b> 예전에는 <c>AssaultEnabled</c> 가
        /// 간격에 0.35 를 또 곱해서, 컷신 뒤 5초가 1.75초가 됐을 것이다 — §17.3 의
        /// 5초 전 신호가 이전 공세 안에서 울린다.
        /// </summary>
        [Test]
        public void TheFullBlastIsNotMultipliedTwice()
        {
            var spawner = CreateSpawner();
            spawner.SetAssault(true);

            Assert.GreaterOrEqual(spawner.IntervalAt(0f),
                                  PhysicsScale.MeteorWaveLeadSeconds - 0.01f,
                                  "신호가 붙을 자리가 남아야 한다.");
        }

        /// <summary>컷신 전으로 되돌리면 가랑비로 돌아간다 — 시험장이 두 단계를 오간다.</summary>
        [Test]
        public void TheCutsceneStateCanBeToggledBack()
        {
            var spawner = CreateSpawner();

            spawner.SetAssault(true);
            Assert.IsTrue(spawner.AssaultEnabled);

            spawner.SetAssault(false);
            Assert.IsFalse(spawner.AssaultEnabled);
            Assert.AreEqual(PhysicsScale.MeteorDrizzleIntervalSeconds,
                            spawner.IntervalAt(0f), 0.01f);
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

        /// <summary>
        /// 컷신 전에는 중형이 없다. §17.7 은 "중형 · 등장 10분 이후" 로 정했는데,
        /// 그 시계를 컷신이 대신한다(2026-09-18) — 비행선 구역에 들어서기 전에
        /// 중형을 맞으면 배우기도 전에 기절한다.
        /// </summary>
        [Test]
        public void MediumMeteors_DoNotAppearBeforeTheCutscene()
        {
            var spawner = CreateSpawner();

            Assert.AreEqual(0f, spawner.MediumChanceAt(0f), 0.001f);
            Assert.AreEqual(0f, spawner.MediumChanceAt(30f * 60f), 0.001f,
                            "시간이 아무리 흘러도 컷신 전이면 0 이다.");
        }

        [Test]
        public void MediumMeteors_ArriveWithTheCutscene()
        {
            var spawner = CreateSpawner();
            spawner.SetAssault(true);

            Assert.AreEqual(PhysicsScale.MeteorFullBlastMediumChance,
                            spawner.MediumChanceAt(0f), 0.001f);
            Assert.Less(spawner.MediumChanceAt(0f), 0.8f,
                        "소형이 다수로 남아야 방망이가 쓸모 있다(§18).");
        }

        [Test]
        public void SizeDecidesTheKnockbackRadius()
        {
            var spawner = CreateSpawner();
            AdvanceUntilPending(spawner);

            var meteor = spawner.Live[0];

            Assert.AreEqual(MeteorSize.Small, meteor.Size, "초반에는 소형만 나온다.");

            // <b>스포너의 배율을 거쳐 본다</b>(2026-09-18). 컷신 단계가 반경에 배율을
            // 곱하므로(<see cref="MeteorIntensity.ImpactRadiusScale"/>) §17.7 의 생값
            // 2m·3.5m 을 그대로 기대하면 단계가 바뀌는 순간 어긋난다. 여기서 재려는 것은
            // "크기가 반경을 정한다" 이지 "반경이 정확히 2m 다" 가 아니다 —
            // 후자는 MeteorImpactTierTests 몫이다.
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
        /// 컷신이 개수도 바꾼다. 간격만 줄면 "같은 것이 자주 온다" 고,
        /// 개수까지 늘어야 "달라졌다" 가 된다.
        ///
        /// 스포너를 굴리지 않고 강도값을 본다 — 개수는 인원 보정과 난수를 거치므로
        /// 한 번 굴려서는 판정이 안 되고, 여러 번 굴리면 예고가 쌓여 세기가 어긋난다.
        /// </summary>
        [Test]
        public void TheCutsceneMakesWavesBigger()
        {
            var before = MeteorIntensity.For(false).Meteors;
            var after = MeteorIntensity.For(true).Meteors;

            Assert.Greater(after, before, $"컷신 전 {before}발 · 후 {after}발");
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
