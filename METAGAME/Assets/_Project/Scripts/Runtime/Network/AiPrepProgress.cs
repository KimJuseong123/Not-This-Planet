using System;
using System.Globalization;
using System.Text;

namespace Metagame.Network
{
    /// <summary>AI 동료 준비의 단계. 확인 → 받기 → 풀기 → 적용 → 데우기 → 끝 (S15P21B202-713).</summary>
    public enum AiPrepPhase { Check, Fetch, Unpack, Apply, Start, Done, Failed, Cancelled }

    /// <summary>왜 멈췄나. 화면에는 쉬운 말로, 로그에는 이름 그대로 나간다.</summary>
    public enum AiPrepFailure { None, NoSource, NoSpace, Network, Stalled, Corrupt, Apply, StartTimeout, StartFailed, LostHost }

    /// <summary>
    /// 준비가 지금 어디쯤인가 — <b>화면과 로그가 함께 읽는 한 가지 상태</b> (S15P21B202-713).
    ///
    /// <para><b>왜 문구가 아니라 상태인가.</b> 전에는 <c>Message</c> 한 줄이 화면 글자이자
    /// 로그이자 성공·실패 신호였다(S15P21B202-708). 그래서 화면 글자를 쉬운 말로 고치면
    /// 로그에서 원인이 사라지고, 로그를 자세히 적으면 화면에 파일 이름이 샜다. 숫자와 사유를
    /// 여기 모아 두고 <see cref="AiPrepProgress.Display"/> 와 <see cref="AiPrepProgress.Log"/>
    /// 가 따로 읽는다 — 출처는 여전히 하나다.</para>
    ///
    /// <para>남은 시간은 <b>초</b>로 센다. 받기·확인·풀기는 남은 바이트를 그 일의 속도로
    /// 나누고, 데우기는 이 PC 가 지난번에 걸린 시간을 쓴다.</para>
    /// </summary>
    public struct AiPrepSnapshot
    {
        public AiPrepPhase Phase;
        public AiPrepFailure Failure;

        /// <summary>지금 다루는 파일 이름. <b>로그에만</b> 쓴다.</summary>
        public string Archive;

        /// <summary>실패 사유 원문(예외 이름·HTTP 코드 등). <b>로그에만</b> 쓴다.</summary>
        public string Detail;

        /// <summary>이번 시도에서 받아야 하는 바이트와 받은 바이트. 받아 둔 파일은 처음부터 빠진다.</summary>
        public long FetchTotal, FetchDone;

        /// <summary>받기 속도(바이트/초). 아직 모르면 null.</summary>
        public double? FetchRate;

        /// <summary>마지막 바이트가 온 뒤로 흐른 초.</summary>
        public double Idle;

        /// <summary>무결성 확인(해시)할 바이트 — 배포 정보의 <c>bytes</c> 합. 파일마다 한 번만 센다.</summary>
        public long HashTotal, HashDone;
        public double? HashRate;

        /// <summary>풀어 쓸 바이트 — 배포 정보의 <c>installed_bytes</c> 합.</summary>
        public long ExtractTotal, ExtractDone;
        public double? ExtractRate;

        /// <summary>데우기를 시작한 뒤 흐른 초, 그리고 이 PC 에서 걸릴 것으로 보는 초.</summary>
        public double WarmupElapsed, WarmupExpected;

        /// <summary>이 준비를 시작한 뒤 흐른 초. 진행 막대의 분자다.</summary>
        public double Elapsed;

        /// <summary>지금까지 보여 준 진행률. <see cref="AiPrepProgress.Fraction"/> 이 준 값을 그대로 담는다.</summary>
        public float Progress;

        /// <summary><see cref="Progress"/> 를 정한 때의 <see cref="Elapsed"/>. 앞서 간 막대를 시간에 맞춰 움직이는 데 쓴다.</summary>
        public double ProgressAt;

        /// <summary>공간이 모자랄 때 더 비워야 하는 바이트. 모르면 0.</summary>
        public long NeededBytes;
    }

    /// <summary>
    /// <b>실제로 일한 시간만으로</b> 잰 평균 속도 (S15P21B202-713).
    ///
    /// <para><b>왜 쉬는 시간을 빼나.</b> 받기 사이에 풀기가 끼어 있다. 그 동안에도 시계를 돌리면
    /// runtime.zip 을 푸는 사이 속도가 0 쪽으로 가라앉아, models.zip 을 받기 시작할 때
    /// 남은 시간이 터무니없이 길게 나온다. <see cref="Pause"/> 로 시계를 멈추고 다음
    /// <see cref="Add"/> 부터 다시 센다 — 그래서 <b>두 파일에 걸쳐 이어진다.</b></para>
    ///
    /// <para><b>모를 때는 모른다고 한다.</b> 몇 초, 몇 MB 도 안 되는 표본으로 낸 속도는
    /// 남은 시간을 수십 분씩 흔든다. 문턱을 넘기 전에는 <c>null</c> 이다 — 0 도, 무한대도,
    /// NaN 도 아니다.</para>
    /// </summary>
    public sealed class ByteRate
    {
        public const long Megabyte = 1048576;

        readonly double minSeconds;
        readonly long minBytes;
        double seconds, since;
        long bytes;
        bool running;

        /// <summary>받기용 — 3초, 1MB 를 넘겨야 속도를 말한다.</summary>
        public ByteRate() : this(3, Megabyte) { }

        public ByteRate(double minSeconds, long minBytes)
        {
            this.minSeconds = Math.Max(0, minSeconds);
            this.minBytes = Math.Max(1, minBytes);
        }

        /// <summary>
        /// <paramref name="now"/> 까지 일한 시간을 더하고, 그 사이 새로 처리한 바이트를 더한다.
        /// 멈춰 있었다면 이 호출부터 시계가 다시 간다 — 멈춘 동안은 세지 않는다.
        /// </summary>
        public void Add(double now, long more)
        {
            if (running && now > since) seconds += now - since;
            since = now; running = true;
            if (more > 0) bytes += more;
        }

        /// <summary>여기까지 센다. 다음 <see cref="Add"/> 전까지의 시간은 속도에 넣지 않는다.</summary>
        public void Pause(double now)
        {
            if (running && now > since) seconds += now - since;
            running = false;
        }

        public double Seconds => seconds;
        public long Bytes => bytes;

        /// <summary>바이트/초. 문턱 전에는 null.</summary>
        public double? PerSecond => seconds > 0 && seconds >= minSeconds && bytes >= minBytes
            ? bytes / seconds : (double?)null;
    }

    /// <summary>
    /// 준비 상태를 <b>진행률·남은 시간·글자</b>로 바꾸는 순수 함수들 (S15P21B202-713).
    ///
    /// <para><b>화면에는 세 가지만.</b> 무엇을 하는 중인지, 얼마나 됐는지(%), 얼마나 남았는지.
    /// 파일 이름·MB·예외 이름·내부 용어는 <see cref="Log"/> 로만 간다. 방장이 알아야 하는
    /// 것은 "기다리면 되는가, 다시 눌러야 하는가" 이고, 그 밖의 글자는 겁만 준다.</para>
    ///
    /// <para><b>막대와 남은 시간은 같은 숫자에서 나온다.</b> 막대 = 쓴 시간 ÷ (쓴 시간 +
    /// 남은 시간). 따로 셈하면 "90% · 약 20분 남음" 처럼 서로 어긋난다.</para>
    /// </summary>
    public static class AiPrepProgress
    {
        /// <summary>
        /// 이 PC 의 기록이 없을 때 데우기에 걸린다고 보는 초.
        /// <b>2026-09-22 QA 첫 실행 실측 3분 47초(227초)</b> — <see cref="AiLocalRuntime.WarmupLimit"/>
        /// 주석의 그 측정이다. 2026-09-21 포장 PC 에서는 약 20초였다. 열 배 차이가 나므로
        /// 기록이 생기면 기록을 쓴다.
        /// </summary>
        public const float FallbackWarmupSeconds = 227f;

        /// <summary>끝나기 전에는 막대를 이 이상 채우지 않는다 — "100%인데 안 끝남" 을 막는다.</summary>
        public const float Cap = 0.99f;

        /// <summary>
        /// 디스크 속도(해시·풀기)는 문턱 시간을 짧게 둔다. runtime.zip(128MB) 해시는 빠른 PC 에서
        /// 1초도 안 걸린다 — 받기처럼 3초를 기다리면 <b>재지도 못하고 끝나</b>, models.zip 을
        /// 받는 20분 내내 "계산 중" 이 된다.
        /// </summary>
        public const double DiskMinSeconds = 0.05;

        public const string Unknown = "남은 시간 계산 중";

        /// <summary>데우기가 예상보다 길어졌을 때. <b>숫자를 대지 않는다</b> — "0초 남음" 은 거짓말이다.</summary>
        public const string Overrun = "조금 더 걸리고 있어요";

        /// <summary>
        /// 남은 초. <b>필요한 속도 중 하나라도 모르면 null</b> — 아는 것만 더하면 짧게 나와
        /// 나중에 늘어나고, 늘어나는 남은 시간은 멈춘 것보다 나쁘다.
        /// </summary>
        public static double? Remaining(AiPrepSnapshot s)
        {
            switch (s.Phase)
            {
                case AiPrepPhase.Done: return 0;
                case AiPrepPhase.Check:
                case AiPrepPhase.Failed:
                case AiPrepPhase.Cancelled: return null;
            }
            // 멈춰 있는 동안의 속도는 곧 틀린다. 멈췄다고 따로 말하고 숫자는 거둔다.
            if (s.Phase == AiPrepPhase.Fetch && Stalled(s)) return null;

            double total = 0;
            if (!AddWork(ref total, s.FetchTotal - s.FetchDone, s.FetchRate)) return null;
            if (!AddWork(ref total, s.HashTotal - s.HashDone, s.HashRate)) return null;
            if (!AddWork(ref total, s.ExtractTotal - s.ExtractDone, s.ExtractRate)) return null;

            double expected = ExpectedOrFallback(s.WarmupExpected);
            if (s.Phase == AiPrepPhase.Start)
            {
                double? warm = WarmupRemaining(s.WarmupElapsed, expected);
                if (!warm.HasValue) return null;
                total += warm.Value;
            }
            else total += expected;
            return total;
        }

        static bool AddWork(ref double total, long left, double? rate)
        {
            if (left <= 0) return true;
            if (!rate.HasValue || !(rate.Value > 0) || double.IsInfinity(rate.Value)) return false;
            total += left / rate.Value;
            return true;
        }

        /// <summary>받는 중인데 이만큼 조용하면 멈춘 것으로 본다.</summary>
        public static bool Stalled(AiPrepSnapshot s) => s.Idle > AiPackInstaller.StallNotice;

        /// <summary>
        /// 막대 값. <b>줄지 않고</b>, 끝나기 전에는 <see cref="Cap"/> 을 넘지 않는다.
        ///
        /// <para>남은 시간을 알면 쓴 시간 ÷ (쓴 시간 + 남은 시간) — 남은 시간 글자와 같은 숫자다.
        /// 모르면(받기 첫 몇 초·아직 안 잰 디스크 속도) 처리한 바이트의 비율로 대신한다.
        /// 두 계산이 바뀌는 순간 값이 뒤로 갈 수 있어서 <paramref name="previous"/> 아래로는
        /// 내려가지 않는다 — 뒤로 가는 막대는 고장으로 보인다.</para>
        /// </summary>
        public static float Fraction(AiPrepSnapshot s, float previous)
        {
            float before = float.IsNaN(previous) ? 0f : Math.Max(0f, Math.Min(Cap, previous));
            switch (s.Phase)
            {
                case AiPrepPhase.Done: return 1f;
                case AiPrepPhase.Failed:
                case AiPrepPhase.Cancelled: return before;
            }

            double f;
            double? left = Remaining(s);
            double spent = Math.Max(s.Elapsed, s.Phase == AiPrepPhase.Start ? s.WarmupElapsed : 0);
            if (left.HasValue && spent > 0)
            {
                f = spent / (spent + left.Value);
                // **앞서 간 막대를 세워 두지 않는다** (S15P21B202-713 리뷰). 속도를 모르는 동안은 바이트
                // 비율로 가는데, 받아 둔 파일을 다시 확인하는 재시도에서는 그것이 시간 비율보다 한참 앞선다
                // (두 파일을 다 받아 둔 경우 47% 대 4%). 그대로 "줄지 않는다" 로 붙잡으면 막대가 몇 분 서
                // 있는 동안 남은 시간만 줄어 멈춘 것처럼 보인다. 그때는 **남은 틈을 남은 시간에 맞춰** 줄인다
                // — 지난번부터 흐른 초 ÷ (그 초 + 남은 초) 만큼.
                if (f < before)
                {
                    double since = Math.Max(0, s.Elapsed - s.ProgressAt);
                    f = before + (1 - before) * (since / (since + Math.Max(1e-6, left.Value)));
                }
            }
            else f = WorkFraction(s);
            if (double.IsNaN(f) || double.IsInfinity(f)) f = 0;
            return (float)Math.Min(Cap, Math.Max(before, f));
        }

        static double WorkFraction(AiPrepSnapshot s)
        {
            double total = Math.Max(0, s.FetchTotal) + Math.Max(0, s.HashTotal) + Math.Max(0, s.ExtractTotal);
            if (total <= 0) return 0;
            double done = Within(s.FetchDone, s.FetchTotal) + Within(s.HashDone, s.HashTotal) + Within(s.ExtractDone, s.ExtractTotal);
            return done / total;
        }

        static double Within(long done, long total) => Math.Max(0, Math.Min(done, total));

        /// <summary>
        /// 데우기 남은 초. <b>예상을 넘기면 null</b> — 0 을 내지 않는다.
        ///
        /// <para>데우기는 워커가 단계를 알려 주지 않는다(<c>loading|ready|failed</c> 뿐). 그래서
        /// 시간으로만 어림하는데, 어림이 틀렸을 때 "0초 남음" 으로 서 있으면 멈춘 것과 같다.
        /// 모른다고 하고 <see cref="Overrun"/> 을 보인다. 끊는 것은 여전히
        /// <see cref="AiLocalRuntime.WarmupLimit"/> 다.</para>
        /// </summary>
        public static double? WarmupRemaining(double elapsed, double expected)
        {
            if (!(expected > 0) || double.IsInfinity(expected) || double.IsNaN(elapsed)) return null;
            double spent = Math.Max(0, elapsed);
            if (spent >= expected) return null;
            return expected - spent;
        }

        /// <summary>
        /// 사람이 읽는 남은 시간. <b>올림</b>한다 — 일이 남았는데 "0분" 이 나오면 안 된다.
        /// <c>"약 12분 남음"</c> · <c>"약 1시간 5분 남음"</c> · <c>"1분 미만 남음"</c> ·
        /// 모르면 <see cref="Unknown"/>.
        /// </summary>
        public static string FormatRemaining(double? seconds)
        {
            if (!seconds.HasValue || double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value)) return Unknown;
            if (seconds.Value < 60) return "1분 미만 남음";
            // 100시간 넘는 값은 속도가 사실상 0 이라는 뜻이다. long 이 넘치지 않게 여기서 멈춘다.
            long minutes = (long)Math.Ceiling(Math.Min(seconds.Value, 360000.0) / 60.0);
            if (minutes < 60) return "약 " + minutes.ToString(CultureInfo.InvariantCulture) + "분 남음";
            long hours = minutes / 60, rest = minutes % 60;
            string h = "약 " + hours.ToString(CultureInfo.InvariantCulture) + "시간";
            return rest == 0 ? h + " 남음" : h + " " + rest.ToString(CultureInfo.InvariantCulture) + "분 남음";
        }

        /// <summary>
        /// 화면 글자: <b>무엇을 하는 중 · 몇 % · 남은 시간</b>. 그 밖의 것은 넣지 않는다.
        /// 실패면 쉬운 사유와 다음에 할 일, 끝이면 <c>"완료되었습니다"</c>.
        /// </summary>
        public static string Display(AiPrepSnapshot s)
        {
            switch (s.Phase)
            {
                case AiPrepPhase.Done: return "완료되었습니다";
                case AiPrepPhase.Cancelled: return "취소했습니다";
                case AiPrepPhase.Failed: return FailureText(s);
            }
            return Head(s.Phase) + " · " + Percent(Math.Min(s.Progress, Cap)) + " · " + Tail(s);
        }

        static string Head(AiPrepPhase phase)
        {
            switch (phase)
            {
                case AiPrepPhase.Check: return "설치 정보를 확인하는 중";
                case AiPrepPhase.Fetch: return "내려받는 중";
                case AiPrepPhase.Unpack: return "설치하는 중";
                case AiPrepPhase.Apply: return "설치를 마무리하는 중";
                default: return "AI 동료를 준비하는 중";
            }
        }

        static string Tail(AiPrepSnapshot s)
        {
            if (s.Phase == AiPrepPhase.Fetch && Stalled(s)) return "연결이 잠시 멈췄어요";
            double? left = Remaining(s);
            // 데우기에서 모른다는 것은 예상을 넘겼다는 뜻이다 — 속도를 기다리는 것이 아니다.
            if (!left.HasValue && s.Phase == AiPrepPhase.Start) return Overrun;
            return FormatRemaining(left);
        }

        static string FailureText(AiPrepSnapshot s)
        {
            switch (s.Failure)
            {
                case AiPrepFailure.NoSource: return "이 빌드에서는 설치할 수 없습니다";
                case AiPrepFailure.NoSpace:
                    return s.NeededBytes > 0
                        ? "저장 공간이 부족합니다 · 약 " + Gigabytes(s.NeededBytes) + " GB를 더 비운 뒤 다시 시도해 주세요"
                        : "저장 공간이 부족합니다 · 공간을 비운 뒤 다시 시도해 주세요";
                case AiPrepFailure.Network: return "내려받지 못했습니다 · 인터넷 연결을 확인한 뒤 다시 시도해 주세요";
                case AiPrepFailure.Stalled: return "연결이 너무 오래 멈춰 중단했습니다 · 다시 시도해 주세요";
                case AiPrepFailure.Corrupt: return "받은 파일이 올바르지 않습니다 · 다시 시도해 주세요";
                case AiPrepFailure.Apply: return "설치 파일을 쓰지 못했습니다 · 디스크 권한을 확인한 뒤 다시 시도해 주세요";
                case AiPrepFailure.StartTimeout: return "AI 동료 준비가 너무 오래 걸립니다 · 다시 시도해 주세요";
                case AiPrepFailure.StartFailed: return "AI 동료를 시작하지 못했습니다 · 다시 시도해 주세요";
                case AiPrepFailure.LostHost: return "방장이 바뀌어 준비를 멈췄습니다";
                default: return "준비하지 못했습니다 · 다시 시도해 주세요";
            }
        }

        static string Percent(float p)
        {
            double v = float.IsNaN(p) ? 0 : Math.Max(0, Math.Min(1, (double)p));
            return ((int)Math.Floor(v * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>
        /// 0.1GB 단위로 <b>올림</b> — 모자라게 말하면 비우고도 또 모자란다. 2의 거듭제곱으로
        /// 나누므로 딱 떨어지는 값이 반올림 오차로 한 칸 올라가지 않는다.
        /// </summary>
        static string Gigabytes(long bytes)
            => (Math.Ceiling(bytes * 10.0 / 1073741824.0) / 10).ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>
        /// 로그 글자: <b>전부</b>. 단계·실패 이름·파일 이름·MB·속도·남은 초·사유 원문.
        ///
        /// <para>QA 뒤 <c>Player.log</c> 에서 "어디서 왜 멈췄나" 를 이 한 줄로 가릴 수 있어야
        /// 한다(S15P21B202-708). 숫자만 바뀌는 줄은 <see cref="AiStatusLog"/> 가 뜸하게 적는다.</para>
        /// </summary>
        public static string Log(AiPrepSnapshot s)
        {
            var b = new StringBuilder(192);
            b.Append(s.Phase);
            if (s.Failure != AiPrepFailure.None) b.Append('(').Append(s.Failure).Append(')');
            if (!string.IsNullOrEmpty(s.Archive)) b.Append(" · ").Append(s.Archive);
            if (s.FetchTotal > 0)
                b.Append(" · ").Append(AiPackInstaller.DownloadMessage(
                        (float)(Within(s.FetchDone, s.FetchTotal) / s.FetchTotal), s.FetchDone, s.FetchTotal, (float)s.Idle))
                 .Append(" · ").Append(Rate(s.FetchRate));
            if (s.HashTotal > 0)
                b.Append(" · 확인 ").Append(Megabytes(s.HashDone)).Append('/').Append(Megabytes(s.HashTotal))
                 .Append(" MB ").Append(Rate(s.HashRate));
            if (s.ExtractTotal > 0)
                b.Append(" · 풀기 ").Append(Megabytes(s.ExtractDone)).Append('/').Append(Megabytes(s.ExtractTotal))
                 .Append(" MB ").Append(Rate(s.ExtractRate));
            double expected = ExpectedOrFallback(s.WarmupExpected);
            if (s.Phase == AiPrepPhase.Start)
                b.Append(" · 데우기 ").Append(Seconds(s.WarmupElapsed)).Append('/').Append(Seconds(expected)).Append("초");
            else
                b.Append(" · 데우기 예상 ").Append(Seconds(expected)).Append("초");
            double? left = Remaining(s);
            b.Append(" · 남은 ").Append(left.HasValue ? Seconds(left.Value) + "초" : "모름");
            b.Append(" · 전체 ").Append(Percent(s.Progress));
            if (s.NeededBytes > 0) b.Append(" · 부족 ").Append(Megabytes(s.NeededBytes)).Append(" MB");
            if (!string.IsNullOrEmpty(s.Detail)) b.Append(" · ").Append(s.Detail);
            return b.ToString();
        }

        static string Megabytes(long bytes) => (bytes / Megabyte).ToString(CultureInfo.InvariantCulture);
        const long Megabyte = ByteRate.Megabyte;

        static string Seconds(double seconds)
            => ((long)Math.Ceiling(Math.Max(0, Math.Min(seconds, 360000.0)))).ToString(CultureInfo.InvariantCulture);

        static string Rate(double? perSecond)
            => perSecond.HasValue ? (perSecond.Value / Megabyte).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s" : "속도 모름";

        static double ExpectedOrFallback(double expected)
            => expected > 0 && !double.IsInfinity(expected) ? expected : FallbackWarmupSeconds;

        /// <summary>
        /// 데우기 기록의 자리. <b>팩마다(배포 정보 해시), 설치 뒤 첫 실행과 그다음을 따로</b> 둔다.
        ///
        /// <para>설치 직후 첫 실행은 디스크 캐시가 비어 있고 백신 검사까지 겹쳐 훨씬 길다
        /// (227초 대 20초). 둘을 한 값으로 섞으면 어느 쪽에서도 틀린다. 팩이 바뀌면 모델도
        /// 바뀌므로 옛 기록을 쓰지 않는다.</para>
        /// </summary>
        public static string WarmupKey(string packId, bool firstRun)
            => WarmupPrefix(packId) + (firstRun ? ".first" : ".later");

        /// <summary>
        /// "막 설치됐다" 표시의 자리. 설치기가 남기고, 첫 데우기가 끝나면 지운다 — 그래서
        /// 같은 팩을 다시 깔아도 그다음 데우기는 다시 "설치 뒤 첫 실행" 이다.
        /// </summary>
        public static string InstalledKey(string packId) => WarmupPrefix(packId) + ".installed";

        // 배포 정보가 없는 팩(에디터의 로컬 팩)은 한 자리를 함께 쓴다.
        static string WarmupPrefix(string packId)
            => "metagame.ai-warmup." + (string.IsNullOrEmpty(packId) ? "local" : packId.ToLowerInvariant());

        /// <summary>
        /// 이번 데우기에 걸릴 것으로 보는 초. 기록은 0 이하면 없는 것이다.
        ///
        /// <para>첫 실행이면 첫 실행 기록, 없으면 <see cref="FallbackWarmupSeconds"/>. 그다음
        /// 실행이면 그다음 기록, 없으면 첫 실행 기록 — 설치 직후보다 길 일은 없으므로
        /// 모자라게 말하지 않는다 — 그것도 없으면 <see cref="FallbackWarmupSeconds"/>.</para>
        /// </summary>
        public static float ExpectedWarmup(bool firstRun, float firstRunRecorded, float laterRecorded)
        {
            bool first = Recorded(firstRunRecorded), later = Recorded(laterRecorded);
            if (!firstRun && later) return laterRecorded;
            return first ? firstRunRecorded : FallbackWarmupSeconds;
        }

        static bool Recorded(float seconds) => seconds > 0 && !float.IsInfinity(seconds);
    }
}
