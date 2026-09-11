using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Metagame.Core;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 실험용 로그 한 줄의 <b>형식과 계산</b>을 검증한다 (S15P21B202-376).
    ///
    /// <para><b>왜 형식을 테스트하나.</b> 이 줄은 사람이 아니라
    /// <c>scripts/ci/perflog.py</c> 가 읽는다. 형식이 조용히 바뀌면 그 도구가 아무것도
    /// 못 읽고, 그 사실은 "로그에 아무 문제가 없어 보이는" 모양으로 나타난다 —
    /// CI 쪽에서 같은 종류의 침묵을 여러 번 겪었다(S15P21B202-342·-361).</para>
    /// </summary>
    public sealed class PerfLogTests
    {
        static List<float> Ms(params float[] values) => new List<float>(values);

        /// <summary>파서가 읽는 항목이 모두 있고 이름이 맞는가.</summary>
        [Test]
        public void LineHasEveryFieldTheParserNeeds()
        {
            string line = PerfLog.Line(Ms(16.7f, 16.7f), 30f, 4, 23);

            Assert.That(line, Does.StartWith("[PERF] "));
            foreach (var key in new[] { "t=", "fps=", "min=", "p95=", "frames=", "players=", "props=" })
                Assert.That(line, Does.Contain(key), $"파서가 찾는 {key} 가 없다: {line}");

            Assert.That(line, Does.Contain("players=4"));
            Assert.That(line, Does.Contain("props=23"));
            Assert.That(line, Does.Contain("frames=2"));
        }

        /// <summary>
        /// <b>최저 프레임은 가장 느린 프레임에서 나온다.</b>
        ///
        /// <para>평균만 보면 "여러 명이 같이 할 때 떨어진다"(S15P21B202-374)를 놓친다.
        /// 16.7ms 가 계속되다 한 프레임이 200ms 면 평균은 멀쩡한데 그 한 번이 체감된다.</para>
        /// </summary>
        [Test]
        public void MinComesFromTheWorstFrame()
        {
            // 16.7ms × 9 + 200ms × 1 → 평균 35.03ms(≈28.5fps), 최저는 200ms(=5.0fps)
            var frames = Ms(16.7f, 16.7f, 16.7f, 16.7f, 16.7f, 16.7f, 16.7f, 16.7f, 16.7f, 200f);
            string line = PerfLog.Line(frames, 10f, 4, 0);

            Assert.That(line, Does.Contain("min=5.0"), line);
            Assert.That(line, Does.Not.Contain("min=0.0"), line);
        }

        /// <summary>백분위는 큰 쪽에서 센다 — 95백분위가 가장 느린 쪽이어야 한다.</summary>
        [Test]
        public void PercentileCountsFromTheSlowEnd()
        {
            var frames = new List<float>();
            for (int i = 1; i <= 100; i++) frames.Add(i);

            Assert.That(PerfLog.Percentile(frames, 0.95f), Is.EqualTo(95f).Within(0.001f));
            Assert.That(PerfLog.Percentile(frames, 1f), Is.EqualTo(100f).Within(0.001f));
        }

        /// <summary>
        /// <b>백분위가 부르는 쪽의 목록을 바꾸지 않는다.</b> 원본을 정렬하면 같은 창의
        /// 다른 계산이 뒤섞인 순서를 보게 된다.
        /// </summary>
        [Test]
        public void PercentileDoesNotReorderTheInput()
        {
            var frames = Ms(50f, 10f, 30f);
            PerfLog.Percentile(frames, 0.95f);

            Assert.That(frames, Is.EqualTo(new[] { 50f, 10f, 30f }), "입력 목록이 정렬돼 버렸다");
        }

        /// <summary>표본이 없어도 죽지 않고 형식을 지킨다 — 파서가 빈 창도 읽어야 한다.</summary>
        [Test]
        public void EmptyWindowStillProducesAValidLine()
        {
            foreach (var frames in new[] { null, new List<float>() })
            {
                string line = PerfLog.Line(frames, 5f, 2, 0);
                Assert.That(line, Does.StartWith("[PERF] "), line);
                Assert.That(line, Does.Contain("frames=0"), line);
                Assert.That(line, Does.Contain("players=2"), line);
            }
        }

        /// <summary>
        /// <b>소수점이 쉼표인 환경에서도 형식이 그대로다.</b>
        ///
        /// <para>문자열 보간은 <c>CurrentCulture</c> 를 쓴다. 그대로 두면 그런 환경에서
        /// <c>fps=52,3</c> 이 되어 파서가 조용히 못 읽는다 — 로그는 남아 있는데 도구만
        /// 빈손이 되는, 가장 알아채기 어려운 실패다.</para>
        /// </summary>
        [Test]
        public void NumbersDoNotFollowTheSystemLocale()
        {
            var before = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");   // 소수점이 쉼표
                string line = PerfLog.Line(Ms(20f, 20f), 30.5f, 3, 7);

                Assert.That(line, Does.Not.Contain(","), "쉼표가 섞이면 파서가 못 읽는다: " + line);
                Assert.That(line, Does.Contain("t=30.5"), line);
                Assert.That(line, Does.Contain("fps=50.0"), line);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }
    }
}
