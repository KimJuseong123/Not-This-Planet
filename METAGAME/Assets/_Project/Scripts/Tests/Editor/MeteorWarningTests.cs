using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 낙하 예고 지면 표시를 검증한다(S15P21B202-241, 기획 §17.2 · §18.1).
    ///
    /// 지키려는 것은 넷이다.
    /// <list type="number">
    /// <item>표시 크기가 <b>실제 충격 반경</b>과 같다 — 소형 2m · 중형 3.5m.</item>
    /// <item>주황에서 붉게 가고 안쪽이 차오른다.</item>
    /// <item>점멸이 <b>방망이가 맞는 순간</b>과 같은 값에서 시작한다.</item>
    /// <item>동시에 여럿 떠도 원판을 새로 만들지 않는다.</item>
    /// </list>
    ///
    /// 계산은 <see cref="MeteorWarningLook"/> 이 값만 내므로 렌더러 없이 잠글 수 있다.
    /// </summary>
    public sealed class MeteorWarningTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp() => Meteor.All.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            Meteor.All.Clear();
        }

        // --- 예고가 반경을 갖는다 --------------------------------------------

        /// <summary>
        /// 기획 §17.2 "실제 충격 범위와 <b>같은 크기</b>".
        ///
        /// S15P21B202-234 가 소형·중형을 나눴는데 그 값이 예고로 전달되지 않아 표시를
        /// 그릴 근거가 없었다. 없으면 중형에서 2m 원을 보고 피했는데 3.5m 로 맞는다.
        /// </summary>
        [Test]
        public void AWarning_CarriesTheImpactRadius()
        {
            var spawner = MakeSpawner();
            AdvanceUntilPending(spawner);

            var drop = spawner.Pending[0];
            var meteor = spawner.Live[0];

            Assert.Greater(drop.ImpactRadius, 0f, "예고가 반경을 가져야 한다.");
            Assert.AreEqual(meteor.ImpactRadius, drop.ImpactRadius, 0.001f,
                "예고의 반경과 실제 운석의 반경이 같아야 한다.");
        }

        /// <summary>크기 → 반경 매핑이 한 곳에서 나와야 표시와 실제가 어긋나지 않는다.</summary>
        [Test]
        public void RadiusForSize_MatchesTheDesign()
        {
            Assert.AreEqual(PhysicsScale.MeteorSmallImpactRadius,
                            Meteor.RadiusFor(MeteorSize.Small), 0.001f);
            Assert.AreEqual(PhysicsScale.MeteorMediumImpactRadius,
                            Meteor.RadiusFor(MeteorSize.Medium), 0.001f);

            Assert.AreEqual(2f, PhysicsScale.MeteorSmallImpactRadius, 0.001f, "§17.7 소형 2m");
            Assert.AreEqual(3.5f, PhysicsScale.MeteorMediumImpactRadius, 0.001f, "§17.7 중형 3.5m");
        }

        [Test]
        public void TheWarningKeepsItsRadiusWhileCountingDown()
        {
            var drop = Drop(remaining: 3f, radius: 3.5f);
            var advanced = drop.Advanced(1f);

            Assert.AreEqual(3.5f, advanced.ImpactRadius, 0.001f,
                "시간이 흐른다고 반경이 바뀌면 안 된다.");
        }

        // --- 크기 ---------------------------------------------------------

        [Test]
        public void TheDisplayDiameter_IsTwiceTheRadius()
        {
            Assert.AreEqual(4f, MeteorWarningLook.DiameterOf(Drop(3f, 2f)), 0.001f);
            Assert.AreEqual(7f, MeteorWarningLook.DiameterOf(Drop(3f, 3.5f)), 0.001f);
        }

        [Test]
        public void AMediumWarning_IsBiggerThanASmallOne()
        {
            var small = MeteorWarningLook.DiameterOf(
                Drop(3f, Meteor.RadiusFor(MeteorSize.Small)));
            var medium = MeteorWarningLook.DiameterOf(
                Drop(3f, Meteor.RadiusFor(MeteorSize.Medium)));

            Assert.Greater(medium, small, "중형이 눈에 뚜렷하게 커야 피할 수 있다.");
        }

        // --- 색과 채움 -----------------------------------------------------

        /// <summary>기획 §17.2 "주황색으로 시작해 운석이 가까워질수록 붉게 변하고".</summary>
        [Test]
        public void TheColorGoesFromOrangeToRed()
        {
            var start = MeteorWarningLook.RingColorOf(Drop(remaining: 3f, radius: 2f));
            var end = MeteorWarningLook.RingColorOf(Drop(remaining: 0f, radius: 2f));

            Assert.AreEqual(MeteorWarningLook.StartColor, start);
            Assert.AreEqual(MeteorWarningLook.EndColor, end);

            Assert.Less(end.g, start.g, "붉어지려면 초록 성분이 줄어야 한다.");
            Assert.Greater(end.a, start.a, "가까워지면 진해져야 한다.");
        }

        /// <summary>
        /// <b>이 검증이 반대였다.</b> §17.2 는 "안쪽이 채워진다" 고 정하고
        /// S15P21B202-241 이 그대로 만들었는데, 인게임에서 보고 <b>줄어드는 원</b>으로
        /// 뒤집었다(S15P21B202-268). 기획과 어긋나므로 [Plan] 확인이 필요하다.
        /// </summary>
        [Test]
        public void TheInsideShrinksTowardTheMeteor()
        {
            var start = MeteorWarningLook.CountdownScaleOf(Drop(3f, 2f));
            var middle = MeteorWarningLook.CountdownScaleOf(Drop(1.5f, 2f));
            var end = MeteorWarningLook.CountdownScaleOf(Drop(0f, 2f));

            Assert.AreEqual(1f, start, 0.01f, "예고 직후에는 테두리와 같은 크기다.");
            Assert.Less(middle, start, "줄어들어야 한다.");
            Assert.Less(end, middle);
        }

        /// <summary>
        /// <b>0 까지 줄이지 않는다.</b> 줄어들다 멈춘 그 자리에 실제 운석이 박힌다 —
        /// "워닝사인의 크기가 운석과 같다" 가 그 뜻이다.
        /// </summary>
        [Test]
        public void ItStopsAtTheMeteorSize()
        {
            var drop = Drop(0f, SMALL_RADIUS);

            var ring = MeteorWarningLook.DiameterOf(drop);
            var inner = ring * MeteorWarningLook.CountdownScaleOf(drop);

            Assert.AreEqual(MeteorWarningLook.MeteorDiameter, inner, 0.05f,
                "착탄 순간의 안쪽 원이 운석 겉보기 크기여야 한다.");
            Assert.Greater(inner, 0f, "0 이면 무엇이 올지 알려주지 못한다.");
        }

        [Test]
        public void ItNeverGrowsBack()
        {
            var previous = float.MaxValue;

            for (var remaining = 3f; remaining >= 0f; remaining -= 0.1f)
            {
                var scale = MeteorWarningLook.CountdownScaleOf(Drop(remaining, SMALL_RADIUS));

                Assert.LessOrEqual(scale, previous + 1e-4f,
                    $"남은 {remaining:0.0}초에서 다시 커졌다.");
                previous = scale;
            }
        }

        /// <summary>중형 운석에서도 같은 규칙이다 — 테두리 7m 에서 운석 0.9m 까지.</summary>
        [Test]
        public void AMediumMeteorShrinksToTheSameMeteorSize()
        {
            var small = Drop(0f, SMALL_RADIUS);
            var medium = Drop(0f, MEDIUM_RADIUS);

            var smallInner = MeteorWarningLook.DiameterOf(small)
                             * MeteorWarningLook.CountdownScaleOf(small);
            var mediumInner = MeteorWarningLook.DiameterOf(medium)
                              * MeteorWarningLook.CountdownScaleOf(medium);

            Assert.AreEqual(smallInner, mediumInner, 0.05f,
                "떨어지는 운석의 크기는 같으므로 안쪽 원도 같아야 한다.");
        }

        /// <summary>테두리는 건드리지 않았다 — §17.2 의 "실제 충격 범위" 다.</summary>
        [Test]
        public void TheRingIsStillTheImpactRange()
        {
            Assert.AreEqual(SMALL_RADIUS * 2f,
                            MeteorWarningLook.DiameterOf(Drop(1f, SMALL_RADIUS)), 0.01f);

            Assert.AreEqual(MEDIUM_RADIUS * 2f,
                            MeteorWarningLook.DiameterOf(Drop(1f, MEDIUM_RADIUS)), 0.01f);
        }

        // --- 점멸 ---------------------------------------------------------

        /// <summary>
        /// <b>이 검증이 §18.1 과 §17.2 를 잇는다.</b> 점멸이 시작되는 순간이 방망이가
        /// 맞는 순간과 <b>같아야</b> 한다. 따로 적으면 "점멸하는데 안 맞는" 구간이 생긴다.
        /// </summary>
        [Test]
        public void BlinkingStartsExactlyWhenTheBatCanHit()
        {
            var justBefore = Drop(PhysicsScale.BatSwingWindowSeconds + 0.01f, 2f);
            var justAfter = Drop(PhysicsScale.BatSwingWindowSeconds - 0.01f, 2f);

            Assert.AreEqual(MeteorWarningStage.Approaching, MeteorWarningLook.StageOf(justBefore),
                "맞을 수 없는 동안에는 점멸하지 않는다.");
            Assert.AreEqual(MeteorWarningStage.Strikable, MeteorWarningLook.StageOf(justAfter),
                "맞을 수 있게 되는 순간 점멸해야 한다(기획 §18.1).");
        }

        /// <summary>기획 §17.2 "마지막 0.5초에는 빠르게 점멸한다".</summary>
        [Test]
        public void TheLastHalfSecond_BlinksFaster()
        {
            var strikable = Drop(PhysicsScale.BatSwingWindowSeconds - 0.01f, 2f);
            var imminent = Drop(PhysicsScale.MeteorWarningImminentSeconds - 0.01f, 2f);

            Assert.AreEqual(MeteorWarningStage.Strikable, MeteorWarningLook.StageOf(strikable));
            Assert.AreEqual(MeteorWarningStage.Imminent, MeteorWarningLook.StageOf(imminent));

            Assert.Greater(PhysicsScale.MeteorWarningImminentBlinkHz,
                           PhysicsScale.MeteorWarningStrikableBlinkHz,
                           "임박 구간이 더 빨라야 구분된다.");
        }

        [Test]
        public void WhileApproaching_ThereIsNoBlink()
        {
            var drop = Drop(3f, 2f);

            for (var t = 0f; t < 2f; t += 0.1f)
                Assert.AreEqual(0f, MeteorWarningLook.BlinkOf(drop, t), 0.001f,
                    "예고 내내 깜빡이면 마지막 0.5초가 눈에 띄지 않는다.");
        }

        [Test]
        public void BlinkStaysWithinRange()
        {
            var drop = Drop(0.2f, 2f);

            for (var t = 0f; t < 3f; t += 0.017f)
            {
                var blink = MeteorWarningLook.BlinkOf(drop, t);
                Assert.GreaterOrEqual(blink, -0.001f);
                Assert.LessOrEqual(blink, 1.001f);
            }
        }

        // --- 표시와 풀 -----------------------------------------------------

        [Test]
        public void OneMarkerPerWarning()
        {
            var spawner = MakeSpawner();
            var view = MakeView(spawner);

            view.Tick(0f);
            Assert.AreEqual(0, view.VisibleCount, "예고가 없으면 표시도 없다.");

            AdvanceUntilPending(spawner);
            view.Tick(0f);

            Assert.AreEqual(spawner.Pending.Count, view.VisibleCount);
            Assert.AreEqual(1, view.VisibleCount);
        }

        /// <summary>
        /// 공세 후반에는 동시에 다섯 개가 뜨고 사이사이 0 이 된다. 매번 만들고 지우면
        /// GC 가 돌므로 <b>숨겨서 재사용</b>한다.
        /// </summary>
        [Test]
        public void MarkersAreReused_NotRecreated()
        {
            var spawner = MakeSpawner();
            var view = MakeView(spawner);

            AdvanceUntilPending(spawner);
            view.Tick(0f);
            var pooledAfterFirst = view.PooledCount;
            Assert.GreaterOrEqual(pooledAfterFirst, 1);

            // 예고가 끝나 0 이 되었다가 다시 뜨는 것을 흉내낸다.
            Advance(spawner, PhysicsScale.MeteorWarningSeconds + 0.1f);
            view.Tick(0f);
            Assert.AreEqual(0, view.VisibleCount, "예고가 끝나면 숨겨야 한다.");
            Assert.AreEqual(pooledAfterFirst, view.PooledCount, "지우지 않고 남겨야 한다.");

            AdvanceUntilPending(spawner);
            view.Tick(0f);
            Assert.AreEqual(pooledAfterFirst, view.PooledCount,
                "다시 떠도 새로 만들지 않아야 한다.");
        }

        [Test]
        public void TheMarkerSitsAtTheImpactPointJustAboveGround()
        {
            var spawner = MakeSpawner();
            var view = MakeView(spawner);

            AdvanceUntilPending(spawner);
            view.Tick(0f);

            var drop = spawner.Pending[0];
            var marker = FirstMarker(view);

            Assert.AreEqual(drop.ImpactPoint.x, marker.position.x, 0.001f);
            Assert.AreEqual(drop.ImpactPoint.z, marker.position.z, 0.001f);
            Assert.AreEqual(drop.ImpactPoint.y + PhysicsScale.MeteorWarningLift,
                            marker.position.y, 0.001f,
                            "지면과 같은 높이면 서로를 가리며 깜빡인다.");
        }

        [Test]
        public void TheRingMatchesTheImpactDiameter()
        {
            var spawner = MakeSpawner();
            var view = MakeView(spawner);

            AdvanceUntilPending(spawner);
            view.Tick(0f);

            var drop = spawner.Pending[0];
            var ring = FirstMarker(view).Find($"Ring_0");

            Assert.IsNotNull(ring);
            Assert.AreEqual(MeteorWarningLook.DiameterOf(drop), ring.localScale.x, 0.01f);
            Assert.AreEqual(MeteorWarningLook.DiameterOf(drop), ring.localScale.z, 0.01f);
        }

        [Test]
        public void TheMarkerHasNoCollider()
        {
            var spawner = MakeSpawner();
            var view = MakeView(spawner);

            AdvanceUntilPending(spawner);
            view.Tick(0f);

            Assert.IsNull(FirstMarker(view).GetComponentInChildren<Collider>(true),
                "표시가 부딪히면 플레이어가 원 위에서 걸린다.");
        }

        // --- 리그 ---------------------------------------------------------

        const float SMALL_RADIUS = PhysicsScale.MeteorSmallImpactRadius;
        const float MEDIUM_RADIUS = PhysicsScale.MeteorMediumImpactRadius;

        static PendingMeteorDrop Drop(float remaining, float radius) =>
            new(Vector3.zero, remaining, PhysicsScale.MeteorWarningSeconds, radius);

        MeteorSpawner MakeSpawner(int seed = 20260901)
        {
            var go = new GameObject("MeteorSpawner");
            _spawned.Add(go);

            var spawner = go.AddComponent<MeteorSpawner>();
            spawner.Configure(seed);
            spawner.SetArea(go.transform, new Vector2(40f, 40f));
            return spawner;
        }

        MeteorWarningView MakeView(MeteorSpawner spawner)
        {
            var go = new GameObject("MeteorWarningView");
            _spawned.Add(go);

            var view = go.AddComponent<MeteorWarningView>();
            view.Configure(spawner);
            return view;
        }

        static Transform FirstMarker(MeteorWarningView view)
        {
            var container = view.transform.Find("MeteorWarnings");
            Assert.IsNotNull(container, "표시를 모아 두는 자식이 있어야 한다.");
            Assert.Greater(container.childCount, 0);
            return container.GetChild(0);
        }

        static void AdvanceUntilPending(MeteorSpawner spawner)
        {
            var guard = 0;
            while (spawner.Pending.Count == 0 && guard++ < 200000) spawner.Tick(Tuning.FixedStep);

            Assert.Greater(spawner.Pending.Count, 0, "예고가 떠야 한다.");
        }

        static void Advance(MeteorSpawner spawner, float seconds)
        {
            var steps = Mathf.RoundToInt(seconds / Tuning.FixedStep);
            for (var i = 0; i < steps; i++) spawner.Tick(Tuning.FixedStep);
        }
    }
}
