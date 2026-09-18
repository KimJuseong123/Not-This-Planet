using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 공세 강도는 컷신 전후 둘뿐이다(2026-09-18).
    ///
    /// 처음에는 경과 시간으로 서서히 조이는 곡선이었다. 이 스테이지의 난이도 전환점이
    /// 이미 기획 §17.8 의 공세 트리거로 정해져 있는데 시계가 따로 돌면,
    /// <b>컷신을 일찍 본 판과 늦게 본 판의 난이도가 달라진다.</b>
    /// </summary>
    public sealed class MeteorIntensityTests
    {
        [Test]
        public void BeforeTheCutscene_ItIsAThinDrizzle()
        {
            var level = MeteorIntensity.For(assaultBegun: false);

            Assert.AreEqual(PhysicsScale.MeteorDrizzleIntervalSeconds,
                            level.WaveIntervalSeconds, 0.001f);
            Assert.AreEqual(PhysicsScale.MeteorDrizzleMeteors, level.Meteors);
            Assert.AreEqual(0f, level.MediumChance, 0.001f, "컷신 전에는 중형이 없다.");
            Assert.AreEqual(1f, level.ImpactRadiusScale, 0.001f, "반경도 기획 §17.7 생값이다.");
        }

        /// <summary>
        /// <b>0 발이 아니다.</b> 예전에는 <c>Stage1Sequence</c> 가 스포너를 통째로 막아
        /// 비행선까지 걸어가는 내내 한 발도 안 떨어졌다 — 그러면 운석이 위협이라는 것을
        /// 공세 컷신에서 처음 배운다.
        /// </summary>
        [Test]
        public void TheDrizzleStillDropsSomething()
        {
            var level = MeteorIntensity.For(assaultBegun: false);

            Assert.Greater(level.Meteors, 0);
            Assert.Greater(level.WaveIntervalSeconds, 0f);
        }

        [Test]
        public void AfterTheCutscene_EverythingIsOn()
        {
            var level = MeteorIntensity.For(assaultBegun: true);

            Assert.AreEqual(PhysicsScale.MeteorFullBlastIntervalSeconds,
                            level.WaveIntervalSeconds, 0.001f);
            Assert.AreEqual(PhysicsScale.MeteorFullBlastMeteors, level.Meteors);
            Assert.AreEqual(PhysicsScale.MeteorFullBlastMediumChance,
                            level.MediumChance, 0.001f);
            Assert.AreEqual(PhysicsScale.MeteorFullBlastRadiusScale,
                            level.ImpactRadiusScale, 0.001f);
        }

        /// <summary>네 축이 모두 한쪽으로 세져야 "컷신 뒤가 다르다" 가 된다.</summary>
        [Test]
        public void TheCutsceneMakesEveryAxisWorse()
        {
            var before = MeteorIntensity.For(false);
            var after = MeteorIntensity.For(true);

            Assert.Less(after.WaveIntervalSeconds, before.WaveIntervalSeconds, "더 자주");
            Assert.Greater(after.Meteors, before.Meteors, "더 많이");
            Assert.Greater(after.MediumChance, before.MediumChance, "더 크게");
            Assert.Greater(after.ImpactRadiusScale, before.ImpactRadiusScale, "더 넓게");
        }

        /// <summary>
        /// 컷신 뒤 간격이 §17.3 의 신호 시간보다 짧으면, 5초 전 신호가 <b>이전 공세
        /// 안에서</b> 울려 무엇을 예고하는지 알 수 없다.
        /// </summary>
        [Test]
        public void TheFullBlastStillLeavesRoomForTheWarning()
        {
            Assert.GreaterOrEqual(MeteorIntensity.FullBlast.WaveIntervalSeconds,
                                  PhysicsScale.MeteorWaveLeadSeconds - 0.001f);
        }

        /// <summary>
        /// 전부 중형이면 방망이(§18)로 쳐낼 수 있는 것이 사라진다 — 소형이 다수로 남아야 한다.
        /// </summary>
        [Test]
        public void SmallMeteorsStayTheMajorityEvenAtFullBlast()
        {
            Assert.Less(MeteorIntensity.FullBlast.MediumChance, 1f);
            Assert.Less(MeteorIntensity.FullBlast.MediumChance, 0.8f);
        }

        [Test]
        public void TheBandCarriesTheSameNumbers()
        {
            var level = MeteorIntensity.FullBlast;
            var band = level.ToBand();

            Assert.AreEqual(level.WaveIntervalSeconds, band.WaveIntervalSeconds, 0.001f);
            Assert.AreEqual(level.Meteors, band.MinMeteors);
            Assert.AreEqual(level.Meteors, band.MaxMeteors);
            Assert.AreEqual(level.MediumChance, band.MediumChance, 0.001f);
            Assert.AreEqual(level.ImpactRadiusScale, band.ImpactRadiusScale, 0.001f);
            Assert.AreEqual(0f, band.FromMinutes, 0.001f, "시간대가 아니라 상태다.");
        }
    }
}
