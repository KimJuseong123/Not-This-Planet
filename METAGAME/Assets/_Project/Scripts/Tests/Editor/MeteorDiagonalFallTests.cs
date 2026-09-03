using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운석이 대각선으로 빠르게 떨어지는 것을 검증한다(S15P21B202-274).
    ///
    /// <b>예전에는 착지 지점 바로 위에서 수직으로 떨어졌다.</b> 위에서 곧장 내려오면
    /// 화면에서 거의 움직이지 않아 속도감이 없고, "하늘에서 뭔가 내려온다" 로만 보여
    /// 운석으로 읽히지 않았다.
    ///
    /// 이 스위트가 지키는 <b>계약</b>은 하나다 — 경로가 어떻게 바뀌든
    /// <b>예고가 끝나는 순간 예고한 지점에 닿는다.</b> 방망이의 "착탄 몇 초 전"
    /// (§18.1)과 바닥 원(§17.2)이 둘 다 그것에 의존한다.
    /// </summary>
    public sealed class MeteorDiagonalFallTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var meteor in Object.FindObjectsByType<Meteor>(FindObjectsSortMode.None))
                if (meteor != null)
                    Object.DestroyImmediate(meteor.gameObject);

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        // --- 대각선으로 온다 -------------------------------------------------

        [Test]
        public void TheMeteorStartsAwayFromTheImpactPoint()
        {
            var (spawner, meteor, point) = SpawnOne();

            var offset = Horizontal(meteor.transform.position - point);

            Assert.Greater(offset.magnitude, 1f,
                "착지 지점 바로 위에서 시작하면 수직 낙하다.");
            Assert.AreEqual(ExpectedReach(spawner), offset.magnitude, 0.5f);
        }

        /// <summary>입사각이 상수와 맞는가. 90도면 수직이다.</summary>
        [Test]
        public void TheEntryAngleMatchesTheConstant()
        {
            var (spawner, meteor, point) = SpawnOne();

            var delta = meteor.transform.position - point;
            var angle = Mathf.Atan2(delta.y, Horizontal(delta).magnitude) * Mathf.Rad2Deg;

            Assert.AreEqual(PhysicsScale.MeteorEntryAngleDegrees, angle, 1f);
        }

        [Test]
        public void TheHorizontalVelocityAimsAtTheImpactPoint()
        {
            var (_, meteor, point) = SpawnOne();

            var toPoint = Horizontal(point - meteor.transform.position).normalized;
            var moving = Horizontal(meteor.GetComponent<Rigidbody>().linearVelocity).normalized;

            Assert.AreEqual(1f, Vector3.Dot(toPoint, moving), 0.01f,
                "수평 속도가 착지 지점을 향해야 예고한 곳에 닿는다.");
        }

        // --- 그래도 예고와 맞는다 (이 이슈의 계약) ---------------------------

        [Test]
        public void ItStillLandsWhenTheWarningEnds()
        {
            var (spawner, meteor, _) = SpawnOne();

            var remaining = spawner.Pending[0].RemainingSeconds;

            Assert.IsTrue(meteor.TryPredictImpact(out var seconds, out _));
            Assert.AreEqual(remaining, seconds, 0.15f,
                $"예고 {remaining:0.00}초와 실제 착탄 {seconds:0.00}초가 맞아야 한다.");
        }

        [Test]
        public void ItStillLandsOnTheWarnedPoint()
        {
            var (_, meteor, point) = SpawnOne();

            Assert.IsTrue(meteor.TryPredictImpact(out _, out var predicted));

            Assert.AreEqual(0f, Horizontal(predicted - point).magnitude, 0.35f,
                "대각선으로 와도 예고한 원 안에 닿아야 한다(§17.2).");
        }

        /// <summary>
        /// 수직 속도는 <b>건드리지 않는다.</b> 중력은 y 만 바꾸므로, 수평 속도를 얼마로
        /// 주든 착탄 시각은 수직 속도만으로 정해진다.
        /// </summary>
        [Test]
        public void TheVerticalSpeedIsUnchanged()
        {
            var (_, meteor, _) = SpawnOne();

            var velocity = meteor.GetComponent<Rigidbody>().linearVelocity;

            Assert.AreEqual(PhysicsScale.MeteorDropSpeed, -velocity.y, 0.2f);
        }

        // --- 더 빠르다 -------------------------------------------------------

        /// <summary>
        /// <b>속도는 거리로 올린다.</b> 예고 3초를 줄이면 반응 시간이 줄어 바닥 원으로
        /// 타이밍을 잡는다는 기믹의 핵심이 사라진다 — 그래서 예고는 그대로 두고 경로를
        /// 길게 만든다.
        /// </summary>
        [Test]
        public void ItMovesFasterThanAVerticalDrop()
        {
            var (_, meteor, _) = SpawnOne();

            var speed = meteor.GetComponent<Rigidbody>().linearVelocity.magnitude;

            Assert.Greater(speed, PhysicsScale.MeteorDropSpeed * 1.3f,
                "대각선이면 같은 시간에 더 긴 거리를 지나므로 빨라야 한다.");
        }

        [Test]
        public void TheReactionTimeIsUnchanged()
        {
            var (spawner, _, _) = SpawnOne();

            Assert.AreEqual(PhysicsScale.MeteorWarningSeconds,
                            spawner.Pending[0].WarningSeconds, 0.01f,
                "예고 시간을 줄여 속도를 만들면 안 된다(§17.2 착탄 3초 전).");
        }

        // --- 예고 원은 착지 지점에 있다 ---------------------------------------

        /// <summary>
        /// <b>원이 운석을 따라가면 안 된다.</b> 대각선이면 운석의 수평 위치와 착지
        /// 지점이 다르므로, 운석 아래에 그리면 §17.2 의 "실제 충격 범위와 같은 크기의
        /// 원형 표시" 가 엉뚱한 곳을 가리킨다.
        /// </summary>
        [Test]
        public void TheWarningStaysAtTheImpactPointNotUnderTheMeteor()
        {
            var (spawner, meteor, point) = SpawnOne();

            Assert.AreEqual(point, spawner.Pending[0].ImpactPoint);
            Assert.Greater(Horizontal(meteor.transform.position - point).magnitude, 1f,
                "이 검증이 의미를 가지려면 둘이 실제로 떨어져 있어야 한다.");
        }

        // --- 재현성 ---------------------------------------------------------

        [Test]
        public void SameSeed_ProducesTheSameEntryPath()
        {
            var (_, first, _) = SpawnOne(seed: 4242);
            var a = first.transform.position;

            TearDown();

            var (_, second, _) = SpawnOne(seed: 4242);

            Assert.AreEqual(a, second.transform.position);
        }

        [Test]
        public void DifferentSeed_ProducesADifferentEntryDirection()
        {
            var (_, first, pointA) = SpawnOne(seed: 1);
            var a = Horizontal(first.transform.position - pointA).normalized;

            TearDown();

            var (_, second, pointB) = SpawnOne(seed: 99);
            var b = Horizontal(second.transform.position - pointB).normalized;

            Assert.Less(Vector3.Dot(a, b), 0.999f, "방향이 시드에 따라 달라야 한다.");
        }

        // --- 막힌 경로 (대각선이 만든 새 위험) -------------------------------

        /// <summary>
        /// <b>수직 낙하에는 없던 위험이다.</b> 비스듬히 오면 우주선·발사대·타워에
        /// <b>먼저</b> 부딪혀 예고한 원이 아닌 곳에서 터진다 — 플레이어는 이유를 알 수
        /// 없다. 그때는 수직으로 되돌린다.
        /// </summary>
        [Test]
        public void ABlockedPathFallsBackToVertical()
        {
            var wall = StaticBlocker();

            var (_, meteor, point) = SpawnOne();

            Assert.AreEqual(0f, Horizontal(meteor.transform.position - point).magnitude, 0.01f,
                "다 막혔으면 예고한 지점에 닿는 쪽을 고른다.");
            Assert.IsNotNull(wall);
        }

        [Test]
        public void EvenWhenBlocked_ItStillLandsOnTheWarnedPoint()
        {
            StaticBlocker();

            var (spawner, meteor, point) = SpawnOne();

            Assert.IsTrue(meteor.TryPredictImpact(out var seconds, out var predicted));
            Assert.AreEqual(spawner.Pending[0].RemainingSeconds, seconds, 0.15f);
            Assert.AreEqual(0f, Horizontal(predicted - point).magnitude, 0.35f);
        }

        /// <summary>
        /// <b>움직이는 것은 막힘으로 세지 않는다.</b> 플레이어나 굴러다니는 파츠가 잠깐
        /// 경로에 들어와 방향이 바뀌면 같은 시드에서 다른 경로가 나와 재현이 깨진다 —
        /// 게다가 날아오는 운석에 맞는 것은 막을 일이 아니다.
        /// </summary>
        [Test]
        public void AMovingObstacleDoesNotBlockThePath()
        {
            var wall = StaticBlocker();
            var body = wall.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = false;

            var (spawner, meteor, point) = SpawnOne();

            Assert.Greater(Horizontal(meteor.transform.position - point).magnitude, 1f,
                "움직이는 물체 때문에 대각선을 포기하면 안 된다.");
            Assert.IsNotNull(spawner);
        }

        // --- 리그 ------------------------------------------------------------

        static Vector3 Horizontal(Vector3 v) => new(v.x, 0f, v.z);

        /// <summary>입사각과 낙하 높이가 정하는 수평 거리.</summary>
        static float ExpectedReach(MeteorSpawner spawner)
        {
            var angle = Mathf.Clamp(PhysicsScale.MeteorEntryAngleDegrees, 1f, 89.9f);
            return PhysicsScale.MeteorDropHeight / Mathf.Tan(angle * Mathf.Deg2Rad);
        }

        /// <summary>
        /// 어느 방향으로 뽑아도 걸리는 정적 덩어리. 낙하 구간 전체를 감싼다.
        ///
        /// 착지 지점 근처는 검사에서 빠지므로(경로 끝에는 늘 지면이 있다) 지면보다
        /// 충분히 위에 둔다.
        /// </summary>
        GameObject StaticBlocker()
        {
            var go = new GameObject("경로 차단");
            _spawned.Add(go);

            go.transform.position = new Vector3(0f, PhysicsScale.MeteorDropHeight * 0.5f, 0f);
            go.layer = GameLayers.Ground;

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(400f, PhysicsScale.MeteorDropHeight, 400f);

            UnityEngine.Physics.SyncTransforms();
            return go;
        }

        (MeteorSpawner Spawner, Meteor Meteor, Vector3 Point) SpawnOne(int seed = 12345)
        {
            var go = new GameObject("MeteorSpawner");
            _spawned.Add(go);

            var spawner = go.AddComponent<MeteorSpawner>();
            spawner.Configure(seed);

            var guard = 0;
            while (spawner.Pending.Count == 0 && guard++ < 200000)
                spawner.Tick(Tuning.FixedStep);

            Assert.AreEqual(1, spawner.Pending.Count, "예고가 떠야 한다.");
            Assert.AreEqual(1, spawner.Live.Count, "운석이 떨어지고 있어야 한다.");

            return (spawner, spawner.Live[0], spawner.Pending[0].ImpactPoint);
        }
    }
}
