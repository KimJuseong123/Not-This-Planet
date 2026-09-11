using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 흔적이 식은 뒤 옅어져 사라지는 것을 검증한다(S15P21B202-438).
    ///
    /// <b>증상은 색 하나였다.</b> 식는 단계만 있어서 5초 뒤
    /// <see cref="MeteorScarLook.ColdColor"/> = (0.12, 0.10, 0.10) 으로 굳고 알파가
    /// 0.5 로 남았다 — 밝기 0.106 의 검은 원이 수명이 끝날 때까지 지면에 박혀 있었다.
    ///
    /// <see cref="MeteorScarLook"/> 은 상태 없는 순수 계산이라 시간을 직접 넣어
    /// 잠글 수 있다. <see cref="MeteorWarningLook"/> 을 그렇게 잠근 것과 같은 방식이다.
    /// </summary>
    public sealed class MeteorScarFadeTests
    {
        const float Cool = 5f;
        const float Fade = 5f;

        // --- 옅어지는 곡선 ---------------------------------------------------

        [Test]
        public void WhileCooling_ItStaysFullyOpaque()
        {
            Assert.AreEqual(1f, MeteorScarLook.Opacity01(0f, Cool, Fade), 1e-4f);
            Assert.AreEqual(1f, MeteorScarLook.Opacity01(2.5f, Cool, Fade), 1e-4f);
            Assert.AreEqual(1f, MeteorScarLook.Opacity01(Cool, Cool, Fade), 1e-4f,
                "식는 순간까지는 옅어지지 않는다 — 두 단계가 겹치면 5초 내내 흐려 보인다.");
        }

        [Test]
        public void AfterCooling_ItFadesLinearlyToNothing()
        {
            Assert.AreEqual(0.8f, MeteorScarLook.Opacity01(Cool + 1f, Cool, Fade), 1e-3f);
            Assert.AreEqual(0.5f, MeteorScarLook.Opacity01(Cool + 2.5f, Cool, Fade), 1e-3f);
            Assert.AreEqual(0.2f, MeteorScarLook.Opacity01(Cool + 4f, Cool, Fade), 1e-3f);
            Assert.AreEqual(0f, MeteorScarLook.Opacity01(Cool + Fade, Cool, Fade), 1e-4f);
        }

        [Test]
        public void PastTheFade_ItStaysInvisible()
        {
            Assert.AreEqual(0f, MeteorScarLook.Opacity01(Cool + Fade + 10f, Cool, Fade), 1e-4f);
            Assert.IsTrue(MeteorScarLook.Faded(Cool + Fade, Cool, Fade));
            Assert.IsFalse(MeteorScarLook.Faded(Cool + Fade - 0.1f, Cool, Fade));
        }

        /// <summary>
        /// <b>이 검증이 증상을 직접 잠근다.</b> 예전에는 5초 뒤 알파가 0.5 로 남아
        /// 검은 원이 보였다.
        /// </summary>
        [Test]
        public void TheBlackDiscDoesNotLinger()
        {
            var cooled = MeteorScarLook.ColorOf(Cool, Cool, Fade);
            var brightness = cooled.r * 0.299f + cooled.g * 0.587f + cooled.b * 0.114f;

            Assert.Less(brightness, 0.15f, "다 식으면 어두운 것은 맞다 — 그을린 자리다.");
            Assert.Greater(cooled.a, 0.4f, "식는 순간에는 아직 보인다.");

            // 그 어두운 색이 그대로 남지 않는다.
            var gone = MeteorScarLook.ColorOf(Cool + Fade, Cool, Fade);
            Assert.AreEqual(0f, gone.a, 1e-4f, "옅어진 뒤에는 투명하다 — 검은 원이 남지 않는다.");
        }

        /// <summary>선형이라 툭 끊기지 않는다. 중간 지점이 실제로 중간이다.</summary>
        [Test]
        public void TheFadeIsGradual_NotAStep()
        {
            var quarter = MeteorScarLook.Opacity01(Cool + Fade * 0.25f, Cool, Fade);
            var half = MeteorScarLook.Opacity01(Cool + Fade * 0.5f, Cool, Fade);
            var threeQuarter = MeteorScarLook.Opacity01(Cool + Fade * 0.75f, Cool, Fade);

            Assert.Greater(quarter, half);
            Assert.Greater(half, threeQuarter);
            Assert.AreEqual(0.5f, half, 1e-3f, "절반 시점에 절반이다.");
        }

        [Test]
        public void WithNoFadeTime_ItDisappearsTheMomentItCools()
        {
            Assert.AreEqual(1f, MeteorScarLook.Opacity01(Cool - 0.1f, Cool, 0f), 1e-4f);
            Assert.AreEqual(0f, MeteorScarLook.Opacity01(Cool, Cool, 0f), 1e-4f,
                "페이드 시간이 0 이면 식는 순간 사라진다 — 예전 동작이 아니라 즉시 소멸이다.");
        }

        /// <summary>색은 건드리지 않고 알파만 뺀다.</summary>
        [Test]
        public void TheFadeChangesOnlyAlpha_NotTheHue()
        {
            var cooled = MeteorScarLook.ColorOf(Cool, Cool, Fade);
            var fading = MeteorScarLook.ColorOf(Cool + 2.5f, Cool, Fade);

            Assert.AreEqual(cooled.r, fading.r, 1e-4f);
            Assert.AreEqual(cooled.g, fading.g, 1e-4f);
            Assert.AreEqual(cooled.b, fading.b, 1e-4f);
            Assert.Less(fading.a, cooled.a, "알파만 줄어든다.");
        }

        // --- 상수가 서로 맞는가 ----------------------------------------------

        /// <summary>
        /// <b>수명은 식는 시간 + 옅어지는 시간이어야 한다.</b>
        ///
        /// 더 길면 보이지 않는 원반이 계속 살아 있어 전송·상한 계산에 들어가고,
        /// 더 짧으면 옅어지는 중에 세계에서 사라져 툭 끊긴다.
        /// </summary>
        [Test]
        public void TheLifetimeMatchesCoolPlusFade()
        {
            Assert.AreEqual(PhysicsScale.MeteorScarCoolSeconds + PhysicsScale.MeteorScarFadeSeconds,
                            PhysicsScale.MeteorScarDespawnSeconds, 1e-4f);
        }

        /// <summary>
        /// 후반 파도에서 동시에 깔리는 흔적이 너무 많지 않은가.
        ///
        /// 파도 표(§17.4)의 마지막 구간은 15초마다 5개다. 수명이 그보다 길면 쌓인다 —
        /// 30초였을 때 실제 플레이에서 36개가 살아 있었다.
        /// </summary>
        [Test]
        public void TheLifetimeKeepsTheGroundClear()
        {
            var last = MeteorWaveTable.Rows[MeteorWaveTable.Rows.Length - 1];
            var perWave = last.MaxMeteors;
            var waveSeconds = last.WaveIntervalSeconds;

            var alive = PhysicsScale.MeteorScarDespawnSeconds / waveSeconds * perWave;

            Assert.Less(alive, 10f,
                $"후반 파도({waveSeconds}초마다 {perWave}개)에서 동시에 {alive:F1}개가 된다 — "
                + "10개 안쪽이어야 지면이 검게 덮이지 않는다.");
        }

        // --- 씬에 저장된 값이 실제로 읽히는가 ---------------------------------

        /// <summary>
        /// <b>코드 상수만 고쳐서는 게임에 적용되지 않는다.</b>
        ///
        /// <see cref="MeteorTuning"/> 은 <c>[Serializable]</c> 로
        /// <see cref="MeteorSpawner"/> 에 직렬화되고, 씬에 값이 저장되어 있으면 그것이
        /// 코드 기본값을 덮는다. 실제로 <c>Main.unity</c> 와 <c>Stage1_Astra.unity</c> 에
        /// <c>_scarDespawnSeconds: 30</c> 이 저장돼 있었다 — 상수만 10 으로 줄이면
        /// 게임에서는 아무것도 바뀌지 않았을 것이다.
        ///
        /// 여기서는 <b>새로 만든 인스턴스</b>가 코드 기본값을 받는지 확인한다.
        /// 씬 값 자체는 텍스트로 고쳤고 그 확인은 인게임 몫이다.
        /// </summary>
        [Test]
        public void AFreshTuningTakesTheCodeDefaults()
        {
            var tuning = new MeteorTuning();

            Assert.AreEqual(PhysicsScale.MeteorScarCoolSeconds, tuning.ScarCoolSeconds, 1e-4f);
            Assert.AreEqual(PhysicsScale.MeteorScarFadeSeconds, tuning.ScarFadeSeconds, 1e-4f);
            Assert.AreEqual(PhysicsScale.MeteorScarDespawnSeconds, tuning.ScarDespawnSeconds, 1e-4f);
        }

        /// <summary>
        /// 페이드 시간이 0 인 조정값(예전 씬을 그대로 읽은 경우)에서도 검은 원이
        /// 남지 않는다 — 식는 순간 사라진다.
        ///
        /// 씬에 <c>_scarFadeSeconds</c> 를 명시했지만, 다른 씬이나 프리팹이 그 필드
        /// 없이 남아 있을 수 있다. 그때 0 으로 읽히면 <b>덜 부드럽지만 증상은 없다</b>
        /// 는 것을 잠근다.
        /// </summary>
        [Test]
        public void EvenWithoutAFadeTime_NoBlackDiscRemains()
        {
            var gone = MeteorScarLook.ColorOf(Cool + 1f, Cool, 0f);

            Assert.AreEqual(0f, gone.a, 1e-4f);
            Assert.IsTrue(MeteorScarLook.Faded(Cool, Cool, 0f));
        }

        // --- 표시가 옅어짐을 따라가는가 (배선) -------------------------------

        /// <summary>
        /// <b>이 검증이 없으면 증상을 되살려도 초록불이다.</b>
        ///
        /// 실제로 <see cref="MeteorScarView"/> 의 기준을 예전(<c>Cooled</c>)으로 되돌리는
        /// 대조 실험에서 나머지 검증 12개가 전부 통과했다 — 색 계산만 보고 있었고
        /// <b>그 색을 언제 넣는지</b>는 아무도 보지 않았기 때문이다.
        /// </summary>
        [Test]
        public void TheViewKeepsRepainting_WhileTheScarIsFading()
        {
            var scar = MakeScar(age: Cool + 1f);

            Assert.IsTrue(scar.Cooled, "이미 식었다 — 예전 기준이라면 여기서 갱신이 끊긴다.");
            Assert.IsFalse(scar.Faded, "아직 옅어지는 중이다.");
            Assert.IsTrue(MeteorScarView.ShouldRepaint(scar),
                "옅어지는 동안은 색을 계속 넣어야 한다.");
        }

        [Test]
        public void TheViewStopsRepainting_OnceInvisible()
        {
            var scar = MakeScar(age: Cool + Fade);

            Assert.IsTrue(scar.Faded);
            Assert.IsFalse(MeteorScarView.ShouldRepaint(scar),
                "다 옅어지면 건드릴 이유가 없다 — 200개에서 비용이 0 이 되어야 한다.");
        }

        /// <summary>뷰가 넣는 색이 옅어지는 단계를 반영한다.</summary>
        [Test]
        public void TheViewColorFollowsTheFade()
        {
            var cooled = MeteorScarView.ColorFor(MakeScar(Cool));
            var halfway = MeteorScarView.ColorFor(MakeScar(Cool + Fade * 0.5f));
            var gone = MeteorScarView.ColorFor(MakeScar(Cool + Fade));

            Assert.Greater(cooled.a, halfway.a);
            Assert.Greater(halfway.a, gone.a);
            Assert.AreEqual(0f, gone.a, 1e-4f);
        }

        static MeteorSpawner.Scar MakeScar(float age) => new()
        {
            Id = 1,
            Point = Vector3.zero,
            Radius = 1f,
            Age = age,
            CoolSeconds = Cool,
            FadeSeconds = Fade,
        };

        // --- 예고 원과 섞이지 않았는가 ----------------------------------------

        /// <summary>
        /// 조사 중에 후보가 둘이었다 — 낙하 예고 원과 분화구 흔적. 색으로 갈랐다.
        /// 이 검증이 그 구분을 남긴다.
        /// </summary>
        [Test]
        public void TheWarningRingIsNotTheBlackDisc()
        {
            var warnEnd = MeteorWarningLook.EndColor;
            var scarCold = MeteorScarLook.ColdColor;

            Assert.Greater(warnEnd.r, 0.9f, "예고는 끝까지 붉다.");
            Assert.Less(scarCold.r, 0.2f, "흔적은 다 식으면 어둡다.");
        }
    }
}
