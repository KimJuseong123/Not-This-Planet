using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 시간이 갈수록 공세가 조여지는 곡선(2026-09-18).
    ///
    /// 플레이 확인에서 "진행될수록 점점 더 강하게" 가 나왔다. §17.4 의 표는 다섯 칸짜리
    /// 계단이고 마지막 칸이 "20분~ · 15초 · 5개" 하나라, <b>20분 뒤로는 영원히 같았다.</b>
    /// </summary>
    public sealed class MeteorEscalationTests
    {
        const float Ramp = PhysicsScale.MeteorEscalationRampMinutes;

        // --- 배율 ----------------------------------------------------------

        [Test]
        public void AtTheStart_NothingIsTightened()
        {
            Assert.AreEqual(1f, MeteorEscalation.FactorAt(0f), 0.001f);
        }

        [Test]
        public void TheFactorDoublesOverTheRamp()
        {
            Assert.AreEqual(2f, MeteorEscalation.FactorAt(Ramp * 60f), 0.001f);
            Assert.AreEqual(3f, MeteorEscalation.FactorAt(Ramp * 2f * 60f), 0.001f);
        }

        /// <summary>끝이 없다 — 오래 끌수록 불리해야 발사를 서두른다.</summary>
        [Test]
        public void TheFactorNeverStopsRising()
        {
            Assert.Greater(MeteorEscalation.FactorAt(120f * 60f),
                           MeteorEscalation.FactorAt(60f * 60f));
        }

        [Test]
        public void NegativeTimeIsTreatedAsZero()
        {
            Assert.AreEqual(1f, MeteorEscalation.FactorAt(-500f), 0.001f);
        }

        // --- 간격 ----------------------------------------------------------

        [Test]
        public void TheIntervalShrinksButNeverBelowTheFloor()
        {
            var floor = PhysicsScale.MeteorEscalationIntervalFloor;

            Assert.AreEqual(15f, MeteorEscalation.IntervalFor(15f, 0f), 0.001f);
            Assert.AreEqual(7.5f, MeteorEscalation.IntervalFor(15f, Ramp * 60f), 0.001f);
            Assert.AreEqual(floor, MeteorEscalation.IntervalFor(15f, 600f * 60f), 0.001f);
        }

        /// <summary>
        /// <b>바닥이 시간대보다 길면 시간대를 쓴다.</b> "과부하" 프리셋처럼 이미 바닥보다
        /// 짧은 값이 들어오면, 바닥이 그것을 <b>늘려서는</b> 안 된다.
        /// </summary>
        [Test]
        public void TheFloorNeverStretchesAnAlreadyShortInterval()
        {
            var band = PhysicsScale.MeteorEscalationIntervalFloor * 0.5f;

            Assert.AreEqual(band, MeteorEscalation.IntervalFor(band, 600f * 60f), 0.001f);
        }

        // --- 개수 ----------------------------------------------------------

        [Test]
        public void TheCountGrowsAndStopsAtTheCap()
        {
            var cap = PhysicsScale.MeteorEscalationCountCap;

            Assert.AreEqual(2, MeteorEscalation.CountFor(2, 0f));
            Assert.AreEqual(4, MeteorEscalation.CountFor(2, Ramp * 60f));
            Assert.AreEqual(cap, MeteorEscalation.CountFor(5, 600f * 60f));
        }

        /// <summary>바탕이 상한보다 크면 그대로 둔다 — 조임이 개수를 <b>깎지는</b> 않는다.</summary>
        [Test]
        public void ACountAboveTheCapIsLeftAlone()
        {
            var over = PhysicsScale.MeteorEscalationCountCap + 3;

            Assert.AreEqual(over, MeteorEscalation.CountFor(over, 0f));
        }

        // --- 중형 확률 ------------------------------------------------------

        /// <summary>
        /// §17.7 "중형 · 등장 10분 이후". 등장 시점을 정하는 것은 여전히 시간대 표다 —
        /// 경과 시간만 보고 더하면 <b>1분에도 중형이 나온다.</b>
        /// </summary>
        [Test]
        public void AClosedMediumDoorStaysClosed()
        {
            Assert.AreEqual(0f, MeteorEscalation.MediumChanceFor(0f, 0f), 0.001f);
            Assert.AreEqual(0f, MeteorEscalation.MediumChanceFor(0f, 60f * 60f), 0.001f);
        }

        [Test]
        public void AnOpenMediumDoorOpensFurther()
        {
            var opened = MeteorEscalation.MediumChanceFor(0.30f, 20f * 60f);

            Assert.Greater(opened, 0.30f);
            Assert.LessOrEqual(MeteorEscalation.MediumChanceFor(0.30f, 600f * 60f),
                               PhysicsScale.MeteorEscalationMediumCap + 0.001f);
        }

        // --- 반경 ----------------------------------------------------------

        [Test]
        public void TheRadiusGrowsAndStopsAtTheCap()
        {
            Assert.AreEqual(1f, MeteorEscalation.RadiusScaleFor(1f, 0f), 0.001f);
            Assert.Greater(MeteorEscalation.RadiusScaleFor(1f, 20f * 60f), 1f);
            Assert.AreEqual(PhysicsScale.MeteorEscalationRadiusCap,
                            MeteorEscalation.RadiusScaleFor(1f, 600f * 60f), 0.001f);
        }

        /// <summary>시간대가 이미 배율을 갖고 있으면 거기에 곱한다.</summary>
        [Test]
        public void TheBandScaleIsMultipliedNotReplaced()
        {
            Assert.AreEqual(1.2f, MeteorEscalation.RadiusScaleFor(1.2f, 0f), 0.001f);
            Assert.AreEqual(1.2f * PhysicsScale.MeteorEscalationRadiusCap,
                            MeteorEscalation.RadiusScaleFor(1.2f, 600f * 60f), 0.001f);
        }

        // --- 모든 축이 같은 방향 ---------------------------------------------

        /// <summary>
        /// 네 축이 <b>함께</b> 조여야 "점점 더 강하게" 가 된다. 하나만 움직이면
        /// "같은 것이 자주 온다" 거나 "가끔 센 것이 온다" 로 읽힌다.
        /// </summary>
        [Test]
        public void EveryAxisTightensTogether()
        {
            const float Late = 25f * 60f;

            Assert.Less(MeteorEscalation.IntervalFor(15f, Late), 15f, "더 자주");
            Assert.Greater(MeteorEscalation.CountFor(5, Late), 5, "더 많이");
            Assert.Greater(MeteorEscalation.MediumChanceFor(0.3f, Late), 0.3f, "더 크게");
            Assert.Greater(MeteorEscalation.RadiusScaleFor(1f, Late), 1f, "더 넓게");
        }
    }
}
