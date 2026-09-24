using System.Text.RegularExpressions;
using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>준비 진행률과 남은 시간이 <b>거짓말을 하지 않는가</b> (S15P21B202-713).</summary>
    public sealed class AiPrepProgressTests
    {
        const long MB = ByteRate.Megabyte;

        /// <summary>
        /// 속도를 <b>모르면 모른다고 한다</b> — 0 도, 무한대도, NaN 도 아니다 (S15P21B202-713).
        ///
        /// <para>몇 초짜리 표본으로 낸 속도는 남은 시간을 수십 분씩 흔든다. 0 이면 남은 시간이
        /// 무한대가 되고, 무한대면 0초가 된다 — 어느 쪽이든 화면에 거짓이 선다.</para>
        /// </summary>
        [Test] public void AnUnknownRateIsNullNeverZeroInfinityOrNaN()
        {
            var rate = new ByteRate();
            Assert.That(rate.PerSecond, Is.Null, "아무것도 안 받았는데 속도가 있다");

            rate.Add(0, 0);
            rate.Add(1, MB / 2);
            Assert.That(rate.PerSecond, Is.Null, "1초·0.5MB 로 속도를 말한다 — 남은 시간이 요동친다");
            rate.Add(2.9, 10 * MB);
            Assert.That(rate.PerSecond, Is.Null, "3초가 안 됐는데 속도를 말한다");

            rate.Add(4, 0);
            Assert.That(rate.PerSecond, Is.Not.Null);
            Assert.That(rate.PerSecond.Value, Is.GreaterThan(0).And.LessThan(double.PositiveInfinity));
            Assert.That(double.IsNaN(rate.PerSecond.Value), Is.False);

            var still = new ByteRate();
            still.Add(0, 0); still.Add(10, 0);
            Assert.That(still.PerSecond, Is.Null, "10초 동안 한 바이트도 없었는데 속도가 0 으로 나온다 — 남은 시간이 무한대가 된다");
        }

        /// <summary>
        /// 속도는 <b>일한 시간만으로</b> 재고, <b>두 파일에 걸쳐 이어진다</b> (S15P21B202-713).
        ///
        /// <para>runtime.zip 을 푸는 동안에도 시계를 돌리면 속도가 가라앉아, models.zip 을 받기
        /// 시작할 때 남은 시간이 터무니없이 길다. 파일마다 새로 재면 models.zip 첫 3초는
        /// 또 "계산 중" 이다.</para>
        /// </summary>
        [Test] public void TheRateSkipsPausesAndCarriesAcrossFiles()
        {
            var rate = new ByteRate();
            rate.Add(0, 0);
            rate.Add(4, 4 * MB);                 // runtime.zip: 4초에 4MB
            rate.Pause(4);
            rate.Add(100, 0);                    // 96초 동안 풀었다 — 받기가 아니다
            Assert.That(rate.PerSecond.Value, Is.EqualTo(MB).Within(1),
                "푸는 동안의 시간이 받기 속도에 들어갔다 — models.zip 의 남은 시간이 부풀어 오른다");
            rate.Add(104, 4 * MB);               // models.zip: 같은 빠르기
            Assert.That(rate.PerSecond.Value, Is.EqualTo(MB).Within(1));

            // models.zip 을 받자마자 남은 시간을 말할 수 있다 — runtime.zip 에서 잰 속도로.
            var s = Fetching(rate.PerSecond);
            Assert.That(AiPrepProgress.Remaining(s), Is.Not.Null,
                "둘째 파일의 받기 첫머리에 남은 시간이 없다 — 속도가 파일마다 새로 시작한다");
        }

        /// <summary>
        /// 필요한 속도 중 <b>하나라도 모르면</b> 남은 시간은 없다 (S15P21B202-713).
        /// 아는 것만 더하면 짧게 나왔다가 나중에 늘어난다 — 늘어나는 남은 시간은 멈춘 것보다 나쁘다.
        /// </summary>
        [Test] public void RemainingNeedsEveryRateItUses()
        {
            var s = Fetching(4.0 * MB);
            double? known = AiPrepProgress.Remaining(s);
            Assert.That(known, Is.Not.Null);
            Assert.That(known.Value, Is.GreaterThan(0).And.LessThan(double.PositiveInfinity));

            var noDisk = s; noDisk.ExtractRate = null;
            Assert.That(AiPrepProgress.Remaining(noDisk), Is.Null, "풀기 속도를 모르는데 남은 시간을 말한다");

            var noNet = s; noNet.FetchRate = null;
            Assert.That(AiPrepProgress.Remaining(noNet), Is.Null, "받기 속도를 모르는데 남은 시간을 말한다");

            var zero = s; zero.FetchRate = 0;
            Assert.That(AiPrepProgress.Remaining(zero), Is.Null, "속도 0 으로 나눴다");

            var stalled = s; stalled.Idle = AiPackInstaller.StallNotice + 1;
            Assert.That(AiPrepProgress.Remaining(stalled), Is.Null,
                "멈춘 동안에도 남은 시간을 말한다 — 곧 틀릴 숫자다");

            // 받을 것이 없으면 받기 속도는 필요 없다(받아 둔 파일로 다시 시도한 경우).
            var cached = s; cached.FetchTotal = cached.FetchDone = 0; cached.FetchRate = null;
            Assert.That(AiPrepProgress.Remaining(cached), Is.Not.Null,
                "받을 것도 없는데 받기 속도를 기다린다 — 다시 시도하면 끝까지 \"계산 중\" 이다");

            var done = s; done.Phase = AiPrepPhase.Done;
            Assert.That(AiPrepProgress.Remaining(done), Is.EqualTo(0));
        }

        /// <summary>
        /// 막대는 <b>뒤로 가지 않는다</b> (S15P21B202-713). 속도가 떨어져 남은 시간이 늘어도,
        /// 바이트 비율에서 시간 비율로 바뀌어도. 끝나기 전에는 99% 를 넘지 않는다.
        /// </summary>
        [Test] public void TheFractionNeverDecreases()
        {
            var steps = new[]
            {
                Step(AiPrepPhase.Check, 0, null, 0, 1),
                Step(AiPrepPhase.Fetch, 100 * MB, null, 0, 5),        // 속도 모름 → 바이트 비율
                Step(AiPrepPhase.Fetch, 128 * MB, 20.0 * MB, 0, 8),   // 속도 앎 → 시간 비율
                Step(AiPrepPhase.Fetch, 600 * MB, 0.2 * MB, 0, 400),  // 회선이 느려졌다 — 남은 시간이 는다
                Step(AiPrepPhase.Fetch, 700 * MB, 0.2 * MB, 30, 430), // 멈췄다 — 남은 시간을 모른다
                Step(AiPrepPhase.Unpack, 2300 * MB, 2.0 * MB, 0, 900),
                Step(AiPrepPhase.Apply, 2300 * MB, 2.0 * MB, 0, 1500),
                Step(AiPrepPhase.Start, 2300 * MB, 2.0 * MB, 0, 1600, 100),
                Step(AiPrepPhase.Start, 2300 * MB, 2.0 * MB, 0, 1900, 400), // 예상을 넘겼다
                Step(AiPrepPhase.Failed, 2300 * MB, 2.0 * MB, 0, 1950, 450),
            };

            float shown = 0;
            foreach (var step in steps)
            {
                float next = AiPrepProgress.Fraction(step, shown);
                Assert.That(next, Is.GreaterThanOrEqualTo(shown), step.Phase + " 에서 막대가 뒤로 갔다 — 고장으로 보인다");
                Assert.That(next, Is.LessThanOrEqualTo(AiPrepProgress.Cap), step.Phase + " 에서 끝나기 전에 99% 를 넘었다");
                shown = next;
            }
            Assert.That(shown, Is.GreaterThan(.5f), "끝 무렵인데 막대가 절반도 안 찼다");

            var done = steps[0]; done.Phase = AiPrepPhase.Done;
            Assert.That(AiPrepProgress.Fraction(done, shown), Is.EqualTo(1f));
            Assert.That(AiPrepProgress.Fraction(steps[1], float.NaN), Is.GreaterThanOrEqualTo(0f), "NaN 을 그대로 물려받았다");
        }

        /// <summary>
        /// 데우기가 <b>예상을 넘기면 null</b> — 0 을 내지 않는다 (S15P21B202-713).
        ///
        /// <para>워커는 데우는 단계를 알려 주지 않아 시간으로만 어림한다. 어림이 틀렸을 때
        /// "0초 남음" 으로 서 있으면 멈춘 것과 같다.</para>
        /// </summary>
        [Test] public void AWarmupOverrunIsUnknownNeverZero()
        {
            Assert.That(AiPrepProgress.WarmupRemaining(100, 227), Is.EqualTo(127).Within(1e-9));
            Assert.That(AiPrepProgress.WarmupRemaining(227, 227), Is.Null, "예상에 닿았는데 0 을 낸다");
            Assert.That(AiPrepProgress.WarmupRemaining(300, 227), Is.Null, "예상을 넘겼는데 숫자를 낸다");
            Assert.That(AiPrepProgress.WarmupRemaining(10, 0), Is.Null, "예상이 없는데 숫자를 낸다");
            Assert.That(AiPrepProgress.WarmupRemaining(-5, 227), Is.EqualTo(227).Within(1e-9));

            var late = new AiPrepSnapshot { Phase = AiPrepPhase.Start, WarmupElapsed = 300, WarmupExpected = 227 };
            Assert.That(AiPrepProgress.Remaining(late), Is.Null);
            string text = AiPrepProgress.Display(late);
            Assert.That(text, Does.Contain(AiPrepProgress.Overrun), "예상을 넘겼다는 말이 없다");
            Assert.That(text, Does.Not.Contain("초"), "넘긴 뒤에도 초를 센다");
            Assert.That(text, Does.Not.Contain("남음"), "넘긴 뒤에도 남은 시간을 댄다");
        }

        /// <summary>
        /// 남은 시간은 <b>올림</b>한다 (S15P21B202-713). 일이 남았는데 "0분" · "0초" 가 나오면
        /// 방장은 끝났다고 보고 기다리지 않는다.
        /// </summary>
        [Test] public void RemainingRoundsUpSoZeroNeverShows()
        {
            Assert.That(AiPrepProgress.FormatRemaining(0.4), Is.EqualTo("1분 미만 남음"));
            Assert.That(AiPrepProgress.FormatRemaining(59.9), Is.EqualTo("1분 미만 남음"));
            Assert.That(AiPrepProgress.FormatRemaining(60), Is.EqualTo("약 1분 남음"));
            Assert.That(AiPrepProgress.FormatRemaining(61), Is.EqualTo("약 2분 남음"), "내림했다 — 남은 시간이 모자라게 나온다");
            Assert.That(AiPrepProgress.FormatRemaining(3599), Is.EqualTo("약 1시간 남음"));
            Assert.That(AiPrepProgress.FormatRemaining(3601), Is.EqualTo("약 1시간 1분 남음"));
            Assert.That(AiPrepProgress.FormatRemaining(null), Is.EqualTo(AiPrepProgress.Unknown));
            Assert.That(AiPrepProgress.FormatRemaining(double.NaN), Is.EqualTo(AiPrepProgress.Unknown));
            Assert.That(AiPrepProgress.FormatRemaining(double.PositiveInfinity), Is.EqualTo(AiPrepProgress.Unknown));
            Assert.That(AiPrepProgress.FormatRemaining(1e300), Does.Contain("시간"), "아주 큰 값에서 넘쳐 음수가 나온다");

            var zero = new Regex(@"(^|\D)0\s*(분|초|시간)");
            for (double s = 0; s < 20000; s += 0.37)
                Assert.That(zero.IsMatch(AiPrepProgress.FormatRemaining(s)), Is.False,
                    s + "초가 남았는데 0 으로 말한다: " + AiPrepProgress.FormatRemaining(s));
        }

        /// <summary>
        /// 데우기 예상은 <b>이 PC 의 기록</b>에서 온다. 설치 뒤 첫 실행과 그다음은 따로다 (S15P21B202-713).
        ///
        /// <para>측정 둘이 열 배 차이다(첫 실행 227초, 포장 PC 20초). 한 값으로 섞으면 첫 실행에서는
        /// 몇 분 모자라고, 그다음에는 몇 분 넘친다.</para>
        /// </summary>
        [Test] public void TheWarmupEstimateComesFromThisPc()
        {
            Assert.That(AiPrepProgress.FallbackWarmupSeconds, Is.EqualTo(227f), "2026-09-22 QA 첫 실행 실측이 아니다");
            Assert.That(AiPrepProgress.FallbackWarmupSeconds, Is.LessThan(AiLocalRuntime.WarmupLimit),
                "예상이 마감보다 길다 — 남은 시간이 끝나기도 전에 잘린다");

            Assert.That(AiPrepProgress.ExpectedWarmup(true, 0, 0), Is.EqualTo(227f), "기록이 없는데 실측을 안 쓴다");
            Assert.That(AiPrepProgress.ExpectedWarmup(true, 180, 20), Is.EqualTo(180f),
                "설치 뒤 첫 실행에 그다음 실행의 기록을 썼다 — 몇 분 모자라게 말한다");
            Assert.That(AiPrepProgress.ExpectedWarmup(false, 180, 20), Is.EqualTo(20f),
                "그다음 실행에 첫 실행 기록을 썼다 — 몇 분 넘치게 말한다");
            Assert.That(AiPrepProgress.ExpectedWarmup(false, 180, 0), Is.EqualTo(180f),
                "그다음 기록이 없으면 첫 실행 기록이 더 가깝다 — 모자라게 말하지 않는다");
            Assert.That(AiPrepProgress.ExpectedWarmup(false, 0, 0), Is.EqualTo(227f));
            Assert.That(AiPrepProgress.ExpectedWarmup(false, float.PositiveInfinity, float.NaN), Is.EqualTo(227f),
                "망가진 기록을 그대로 믿는다");

            string sha = new string('a', 64);
            Assert.That(AiPrepProgress.WarmupKey(sha, true), Is.Not.EqualTo(AiPrepProgress.WarmupKey(sha, false)),
                "첫 실행과 그다음이 한 자리를 쓴다");
            Assert.That(AiPrepProgress.WarmupKey(sha.ToUpperInvariant(), true), Is.EqualTo(AiPrepProgress.WarmupKey(sha, true)),
                "같은 팩이 대소문자만 다른 해시로 다른 자리를 쓴다 — 설치기와 실행기가 서로 못 만난다");
            Assert.That(AiPrepProgress.WarmupKey(new string('b', 64), true), Is.Not.EqualTo(AiPrepProgress.WarmupKey(sha, true)),
                "팩이 바뀌었는데 옛 팩의 기록을 쓴다");
            Assert.That(AiPrepProgress.InstalledKey(sha), Is.Not.EqualTo(AiPrepProgress.WarmupKey(sha, true)));
            Assert.That(AiPrepProgress.WarmupKey(null, true), Is.EqualTo(AiPrepProgress.WarmupKey("", true)));
        }

        /// <summary>
        /// Ready 에 닿을 때 <b>걸린 시간을 남긴다</b> (S15P21B202-713).
        ///
        /// <para>값으로는 볼 수 없다 — 진짜 워커가 있어야 한다. 이 배선이 빠지면 기록이 영영
        /// 안 생겨 <b>모든 PC 가 227초를 말한다</b> — 20초면 끝나는 PC 에서도.</para>
        /// </summary>
        [Test] public void TheWarmupTimeIsRecordedWhenTheWorkerIsReady()
        {
            var watch = TestSource.Method(
                TestSource.Read("Runtime/Network/AiLocalRuntime.cs"), "IEnumerator Watch(int epoch)");

            Assert.That(watch, Does.Contain("StartWarmupClock(began);"), "데우기 시계를 켜지 않는다 — 걸린 시간을 잴 수 없다");
            Assert.That(watch, Does.Contain("RecordWarmup(Time.realtimeSinceStartup);"),
                "Ready 에 닿아도 기록하지 않는다 — 다음 준비가 늘 고정값으로 어림한다");
        }

        /// <summary>
        /// 받기 전체 시간에 <b>상한이 없다</b> (S15P21B202-713).
        ///
        /// <para>전에는 파일마다 3600초에서 잘랐다. models.zip 을 한 시간 안에 받으려면 초당
        /// 약 0.61MB 가 필요해서, 그보다 느린 회선은 몇 번을 다시 눌러도 못 받았다. 멈춘 받기만
        /// 끊는다.</para>
        /// </summary>
        [Test] public void OnlyAStalledDownloadIsCut()
        {
            var download = TestSource.Method(
                TestSource.Read("Runtime/Network/AiPackInstaller.cs"), "IEnumerator Download(");

            Assert.That(download, Does.Contain("StallAbort"), "멈춘 받기를 끊지 않는다 — 영영 기다린다");
            Assert.That(download, Does.Not.Contain("3600"), "전체 시간 상한이 돌아왔다 — 느린 회선은 영영 못 받는다");
            Assert.That(AiPackInstaller.StallAbort, Is.GreaterThan(AiPackInstaller.StallNotice));
        }

        static AiPrepSnapshot Fetching(double? rate) => new AiPrepSnapshot
        {
            Phase = AiPrepPhase.Fetch,
            FetchTotal = 2105 * MB, FetchDone = 10 * MB, FetchRate = rate,
            HashTotal = 2233 * MB, HashDone = 128 * MB, HashRate = 400.0 * MB,
            ExtractTotal = 2500 * MB, ExtractDone = 400 * MB, ExtractRate = 150.0 * MB,
            WarmupExpected = 227, Elapsed = 60,
        };

        static AiPrepSnapshot Step(AiPrepPhase phase, long done, double? rate, double idle, double elapsed, double warm = 0)
        {
            long total = 2300 * MB;
            bool unpacked = phase >= AiPrepPhase.Apply;
            return new AiPrepSnapshot
            {
                Phase = phase,
                FetchTotal = total, FetchDone = System.Math.Min(done, total), FetchRate = rate, Idle = idle,
                HashTotal = total, HashDone = unpacked ? total : phase == AiPrepPhase.Unpack ? total / 2 : 0,
                HashRate = phase >= AiPrepPhase.Unpack ? 300.0 * MB : (double?)null,
                ExtractTotal = total, ExtractDone = unpacked ? total : 0,
                ExtractRate = phase >= AiPrepPhase.Unpack ? 100.0 * MB : (double?)null,
                WarmupElapsed = warm, WarmupExpected = 227, Elapsed = elapsed,
            };
        }
    }
}
