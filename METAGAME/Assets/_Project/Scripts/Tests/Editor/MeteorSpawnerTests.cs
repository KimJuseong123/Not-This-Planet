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

            Assert.GreaterOrEqual(spawner.Pending.Count, 1, "예고가 떠야 한다.");
            Assert.AreEqual(spawner.Pending.Count, spawner.Live.Count,
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

        [TestCase(false, 2f, 4f)]
        [TestCase(true, 0.5f, 1.5f)]
        public void BatchIntervals_StayWithinOffsetAndVary(bool assault, float min, float max)
        {
            var spawner = CreateSpawner();
            if (assault) spawner.BeginAssault();
            var starts = new List<float>();
            spawner.DropScheduled += _ =>
            {
                if (starts.Count == 0 || starts[starts.Count - 1] != spawner.Elapsed)
                    starts.Add(spawner.Elapsed);
            };
            Advance(spawner, 45f);
            Assert.Greater(starts.Count, 8);
            var intervals = new HashSet<int>();
            for (var i = 1; i < starts.Count; i++)
            {
                var gap = starts[i] - starts[i - 1];
                Assert.That(gap, Is.InRange(min - 0.001f, max + Tuning.FixedStep + 0.001f));
                intervals.Add(Mathf.RoundToInt(gap * 100f));
            }
            Assert.Greater(intervals.Count, 3, "매 묶음마다 간격을 다시 뽑아야 한다.");
        }

        [Test]
        public void AWave_DropsExactlyWhatItAnnouncedTogether()
        {
            var spawner = CreateSpawner();
            var planned = 0;
            var dropped = 0;
            spawner.WaveIncoming += count => planned = count;
            spawner.DropScheduled += _ => dropped++;
            AdvanceUntilPending(spawner);
            Assert.Greater(planned, 0);
            Assert.AreEqual(planned, dropped);
            Assert.IsFalse(spawner.WaveActive);
        }

        [Test]
        public void BetweenBatches_NothingFalls()
        {
            var spawner = CreateSpawner();
            var dropped = 0;
            spawner.DropScheduled += _ => dropped++;
            AdvanceUntilPending(spawner);
            var afterBatch = dropped;
            Advance(spawner, spawner.CurrentInterval - 2f * Tuning.FixedStep);
            Assert.AreEqual(afterBatch, dropped);
        }

        [Test]
        public void AssaultImmediatelyDropsMultiple_AndDoesNotRestartOnRepeatedCalls()
        {
            var spawner = CreateSpawner();
            spawner.SetCrew(1);
            spawner.Tick(0.2f);
            spawner.BeginAssault();
            spawner.Tick(Tuning.FixedStep);
            Assert.GreaterOrEqual(spawner.Pending.Count, 3);
            var count = spawner.Live.Count;
            var elapsed = spawner.Elapsed;
            spawner.BeginAssault();
            spawner.Tick(Tuning.FixedStep);
            Assert.AreEqual(count, spawner.Live.Count);
            Assert.Greater(spawner.Elapsed, elapsed);
        }

        [Test]
        public void SoloExplorationCanDropTwoTogether()
        {
            var spawner = CreateSpawner();
            spawner.SetCrew(1);
            var maxBatch = 0;
            var batch = 0;
            var at = -1f;
            spawner.DropScheduled += _ =>
            {
                if (at != spawner.Elapsed) { at = spawner.Elapsed; batch = 0; }
                maxBatch = Mathf.Max(maxBatch, ++batch);
            };
            Advance(spawner, 45f);
            Assert.AreEqual(2, maxBatch);
        }

        [Test]
        public void DefaultRandomAreaIgnoresWeightedLaunchPadOutsideArea()
        {
            var spawner = CreateSpawner();
            var pad = new GameObject("outside launch pad");
            _spawned.Add(pad);
            pad.transform.position = new Vector3(1000f, 0f, 1000f);
            spawner.SetLaunchPad(pad.transform);
            var settings = new UnityEditor.SerializedObject(spawner);
            settings.FindProperty("_environmentWeight").floatValue = 0f;
            settings.FindProperty("_nearPlayerWeight").floatValue = 0f;
            settings.FindProperty("_workAreaWeight").floatValue = 0f;
            settings.FindProperty("_launchPadWeight").floatValue = 1f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            foreach (var point in PointsFrom(spawner, 20))
            {
                Assert.That(point.x, Is.InRange(-20f, 20f));
                Assert.That(point.z, Is.InRange(-20f, 20f));
            }
        }

        [Test]
        public void AssaultOpeningWaitsForPauseAndSequenceHold()
        {
            var spawner = CreateSpawner();
            spawner.SetPaused(true);
            spawner.SetSequenceHold(true);
            spawner.BeginAssault();
            spawner.Tick(10f);
            Assert.IsEmpty(spawner.Live);
            spawner.SetPaused(false);
            spawner.Tick(10f);
            Assert.IsEmpty(spawner.Live);
            spawner.SetSequenceHold(false);
            spawner.Tick(Tuning.FixedStep);
            Assert.GreaterOrEqual(spawner.Live.Count, 3);
        }

        [Test]
        public void AssaultProducesMoreMeteorsInTheSameTime()
        {
            var normal = CreateSpawner();
            normal.SetCrew(1);
            var assault = CreateSpawner();
            assault.SetCrew(1);
            assault.BeginAssault();
            var normalCount = 0;
            var assaultCount = 0;
            normal.Spawned += _ => normalCount++;
            assault.Spawned += _ => assaultCount++;
            Advance(normal, 30f);
            Advance(assault, 30f);
            Assert.Greater(assaultCount, normalCount * 2);
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

            Assert.AreEqual(0.15f, spawner.MediumChanceAt(11f * 60f), 0.001f);
            Assert.AreEqual(0.25f, spawner.MediumChanceAt(16f * 60f), 0.001f);
            Assert.AreEqual(0.30f, spawner.MediumChanceAt(25f * 60f), 0.001f);
        }

        [Test]
        public void SizeDecidesTheKnockbackRadius()
        {
            var spawner = CreateSpawner();
            AdvanceUntilPending(spawner);

            var meteor = spawner.Live[0];

            Assert.AreEqual(MeteorSize.Small, meteor.Size, "초반에는 소형만 나온다.");
            Assert.AreEqual(PhysicsScale.MeteorSmallImpactRadius, meteor.ImpactRadius, 0.01f);

            meteor.SetSize(MeteorSize.Medium);
            Assert.AreEqual(PhysicsScale.MeteorMediumImpactRadius, meteor.ImpactRadius, 0.01f);
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
        public void BaseIntervalDependsOnAssaultRatherThanElapsedTime()
        {
            var spawner = CreateSpawner();
            Assert.AreEqual(3f, spawner.IntervalAt(0f));
            Assert.AreEqual(3f, spawner.IntervalAt(21f * 60f));
            spawner.BeginAssault();
            Assert.AreEqual(1f, spawner.IntervalAt(0f));
            Assert.AreEqual(1f, spawner.IntervalAt(21f * 60f));
        }

        [Test]
        public void Interval_StopsShrinkingAtTheLastRow()
        {
            var spawner = CreateSpawner();

            var last = spawner.IntervalAt(20f * 60f);

            Assert.AreEqual(last, spawner.IntervalAt(60f * 60f), 0.01f,
                "마지막 시간대 뒤로는 더 짧아지지 않아야 한다.");
        }

        [Test]
        public void LateGame_DropsMoreOftenThanEarlyGame()
        {
            var spawner = CreateSpawner();

            var early = CountScheduled(spawner, 0f, 40f);
            var late = CountScheduled(spawner, 21f * 60f, 40f);

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
            typeof(MeteorSpawner).GetField("_elapsed", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic).SetValue(spawner, startAt);

            var count = 0;
            void OnScheduled(PendingMeteorDrop _) => count++;

            spawner.DropScheduled += OnScheduled;
            Advance(spawner, window);
            spawner.DropScheduled -= OnScheduled;

            return count;
        }
    }
}
