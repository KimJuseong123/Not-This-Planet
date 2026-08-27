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

        [Test]
        public void Spawner_SchedulesWarningBeforeDropping()
        {
            var spawner = CreateSpawner();

            Advance(spawner, PhysicsScale.MeteorIntervalStart + 0.05f);

            Assert.AreEqual(1, spawner.Pending.Count, "먼저 예고가 떠야 한다.");
            Assert.AreEqual(0, spawner.Live.Count, "예고 중에는 운석이 아직 없어야 한다.");
        }

        [Test]
        public void Spawner_DropsAfterWarningElapses()
        {
            var spawner = CreateSpawner();

            Advance(spawner, PhysicsScale.MeteorIntervalStart + 0.05f);
            Advance(spawner, PhysicsScale.MeteorWarningSeconds + 0.05f);

            Assert.AreEqual(0, spawner.Pending.Count);
            Assert.AreEqual(1, spawner.Live.Count, "예고 시간이 지나면 떨어져야 한다.");
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

        [Test]
        public void Meteor_SpawnsAboveItsWarnedPoint()
        {
            var spawner = CreateSpawner();

            Advance(spawner, PhysicsScale.MeteorIntervalStart + 0.05f);
            var point = spawner.Pending[0].ImpactPoint;

            Advance(spawner, PhysicsScale.MeteorWarningSeconds + 0.05f);
            var meteor = spawner.Live[0];

            Assert.AreEqual(point.x, meteor.transform.position.x, 0.001f);
            Assert.AreEqual(point.z, meteor.transform.position.z, 0.001f);
            Assert.Greater(meteor.transform.position.y, point.y + 1f,
                "예고 지점 위에서 떨어져야 한다.");
        }

        [Test]
        public void SpawnedMeteor_UsesMeteorLayerAndMass()
        {
            var spawner = CreateSpawner();
            Advance(spawner, PhysicsScale.MeteorIntervalStart + PhysicsScale.MeteorWarningSeconds + 0.1f);

            var meteor = spawner.Live[0];

            Assert.AreEqual(GameLayers.Meteor, meteor.gameObject.layer,
                "Meteor 레이어여야 운석끼리 충돌하지 않는다.");
            Assert.AreEqual(PhysicsScale.MeteorMass, meteor.GetComponent<Rigidbody>().mass, 0.01f);
        }

        [Test]
        public void SpawnedMeteor_CarriesDamageSource()
        {
            var spawner = CreateSpawner();
            Advance(spawner, PhysicsScale.MeteorIntervalStart + PhysicsScale.MeteorWarningSeconds + 0.1f);

            Assert.IsNotNull(spawner.Live[0].GetComponent<DamageSource>(),
                "플레이어 피해는 새 시스템 없이 기존 DamageSource 로 전달한다.");
        }

        // --- 난이도 곡선 -----------------------------------------------------

        [Test]
        public void Interval_ShrinksOverTime()
        {
            var spawner = CreateSpawner();

            var start = spawner.IntervalAt(0f);
            var mid = spawner.IntervalAt(PhysicsScale.MeteorRampSeconds * 0.5f);
            var end = spawner.IntervalAt(PhysicsScale.MeteorRampSeconds);

            Assert.AreEqual(PhysicsScale.MeteorIntervalStart, start, 0.01f);
            Assert.Less(mid, start, "시간이 지나면 간격이 줄어야 한다.");
            Assert.AreEqual(PhysicsScale.MeteorIntervalMin, end, 0.01f);
        }

        [Test]
        public void Interval_StopsShrinkingAtMinimum()
        {
            var spawner = CreateSpawner();

            Assert.AreEqual(PhysicsScale.MeteorIntervalMin,
                spawner.IntervalAt(PhysicsScale.MeteorRampSeconds * 10f), 0.01f,
                "최소 간격 아래로는 내려가지 않아야 한다.");
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
            spawner.Configure(seed,
                PhysicsScale.MeteorIntervalStart,
                PhysicsScale.MeteorIntervalMin,
                PhysicsScale.MeteorRampSeconds);
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
