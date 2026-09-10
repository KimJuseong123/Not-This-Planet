using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 실험용 빌드가 <b>스스로</b> 프레임 수치를 남긴다 (S15P21B202-376).
    ///
    /// <para><b>왜 필요한가.</b> 2026-09-10 4인 QA 에서 "여러 명이 같이 뭔가 하면 프레임이
    /// 떨어진다"(S15P21B202-374)가 나왔는데 <b>재는 수단이 없었다.</b> 그래서 같은 QA 의
    /// 다른 항목(남의 캐릭터가 부드러운가)까지 판정할 수 없었다.</para>
    ///
    /// <para><b>인자가 필요 없다.</b> 기존 네트워크 진단(<c>NetDiagnostics</c>)은
    /// <c>-netdiag</c> 를 붙여 실행해야 켜지는데, 그러면 QA 하는 사람이 PowerShell 로
    /// 무엇을 해야 한다. 이것은 <b>빌드에 박힌 실험 라벨</b>
    /// (<see cref="BuildInfo.IsExperiment"/>)만 보고 켜진다 — 실험용 빌드를 받아 두 번
    /// 눌러 실행하면 로그가 남는다.</para>
    ///
    /// <para><b>씬·프리팹을 건드리지 않는다.</b> <c>RuntimeInitializeOnLoadMethod</c> 로
    /// 자기 오브젝트를 만든다. 배치모드 빌드가 프리팹을 지운 사고(S15P21B202-351) 이후
    /// 씬·프리팹 변경은 사전 선언이 필요하고, 계측을 넣자고 그 비용을 치를 이유가 없다.</para>
    ///
    /// <para><b>평균만 남기지 않는다.</b> "여러 명이 같이 할 때 떨어진다" 는 평균으로는
    /// 잘 보이지 않는다 — 끊기는 것은 <b>최저 프레임과 프레임 시간 95백분위</b>에
    /// 나타난다. 평균이 60 인데 95백분위가 100ms 면 그것이 체감되는 그 현상이다.</para>
    ///
    /// <para><b>계측이 재려는 것을 흔들지 않는다.</b> 처음에는 매 보고마다
    /// <c>FindObjectsByType</c> 로 사람 수와 움직이는 강체를 셌는데 되돌렸다 — 씬의 모든
    /// <c>Transform</c> 을 훑는 것은 프레임을 재는 도구가 해서는 안 되는 일이다. 그 수치는
    /// 이미 알고 있는 쪽이 <see cref="Players"/>·<see cref="MovingProps"/> 로
    /// <b>밀어 넣는다.</b> 매 프레임 하는 일은 값 하나 더하기다.</para>
    ///
    /// <para>로그 형식 (<c>scripts/ci/perflog.py</c> 가 읽는다):</para>
    /// <code>
    /// [PERF] t=30.0 fps=52.3 min=14.1 p95=71.2 frames=157 players=4 props=23
    /// </code>
    /// </summary>
    public sealed class PerfLog : MonoBehaviour
    {
        /// <summary>한 줄을 남기는 간격(초). 짧으면 로그가 비대해지고 길면 순간 저하를 놓친다.</summary>
        public const float Interval = 5f;

        /// <summary>이 창에서 담아 둘 프레임 시간의 상한. 이 값을 넘으면 뒤는 버린다.</summary>
        public const int Capacity = 2048;

        /// <summary>
        /// 지금 화면에 있는 사람 수. <b>네트워크 쪽이 갱신한다</b>(<c>NetPlayer</c>).
        ///
        /// <para>"여러 명이 같이 할 때" 라는 조건을 재려면 이 값이 프레임 수치와 같은 줄에
        /// 있어야 한다 — 없으면 인원을 짐작하게 된다. <c>Metagame.Core</c> 는
        /// <c>Metagame.Network</c> 를 참조하지 않으므로(의존 방향이 반대다) 여기서 세지 않고
        /// 이미 아는 쪽이 넣는다.</para>
        /// </summary>
        public static int Players;

        /// <summary>
        /// 지금 <b>움직이고 있는</b> 소품 수 — 잠든 것은 세지 않는다.
        ///
        /// <para>S15P21B202-355(착지한 운석을 쳐 날린다)가 재야 하는 값과 같다. 200개를
        /// 복제 대상으로 두면 비용을 정하는 것이 총 개수가 아니라 <b>동시 이동 개수</b>다.
        /// 두 이슈가 같은 계측을 기다리므로 자리를 여기 둔다 — 채우는 것은 그쪽 작업이고,
        /// 그때까지 0 이다.</para>
        /// </summary>
        public static int MovingProps;

        static PerfLog instance;

        readonly List<float> frameMs = new List<float>(Capacity);
        float elapsed;

        /// <summary>
        /// 실험용 빌드에서만 자기 오브젝트를 만든다.
        ///
        /// <para><c>AfterSceneLoad</c> 인 이유: <see cref="BuildInfo"/> 가
        /// <c>BeforeSceneLoad</c> 에서 라벨을 읽으므로 그 뒤여야 값을 볼 수 있다.</para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // 평소 빌드는 아무 일도 하지 않는다 — 계측이 게임에 값을 물리지 않아야 한다.
            if (!BuildInfo.IsExperiment) return;
            if (instance != null) return;

            var go = new GameObject("[PerfLog]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PerfLog>();

            Debug.Log($"[PERF] 시작 · 실험 {BuildInfo.Experiment} · {Interval}초마다 한 줄");
            Debug.Log("[PERF] 형식: t fps min p95 frames players props");
            Debug.Log("[PERF] 값 " + Parameters());
        }

        /// <summary>
        /// 실험이 건드리는 <b>실제 값</b>을 찍는다.
        ///
        /// <para>라벨은 사람이 적는 것이라 틀릴 수 있다. 라벨과 이 줄이 어긋나면
        /// <b>이 줄이 옳다</b> — 로그를 읽는 사람이 무엇을 믿을지 알아야 한다.</para>
        ///
        /// <para>네트워크 쪽 상수(예측 수렴률 등)는 여기서 읽지 않는다. <c>Core</c> 는 그
        /// 어셈블리를 참조하지 않고, 참조를 더하면 계측 하나 때문에 어셈블리 경계가
        /// 무너진다. 그 값들은 <c>NetDiagnostics</c> 가 자기 <c>[NETDIAG]</c> 줄에 싣는다.</para>
        /// </summary>
        static string Parameters()
        {
            return $"fixedDeltaTime={Time.fixedDeltaTime:0.####} "
                   + $"vSync={QualitySettings.vSyncCount} "
                   + $"targetFrameRate={Application.targetFrameRate} "
                   + $"quality={QualitySettings.GetQualityLevel()}";
        }

        void Update()
        {
            // unscaledDeltaTime — 일시정지(ESC 메뉴)로 timeScale 이 0 이 되어도 계속 잰다.
            float ms = Time.unscaledDeltaTime * 1000f;
            if (frameMs.Count < Capacity) frameMs.Add(ms);

            elapsed += Time.unscaledDeltaTime;
            if (elapsed < Interval) return;

            Debug.Log(Line(frameMs, Time.unscaledTime, Players, MovingProps));
            frameMs.Clear();
            elapsed = 0f;
        }

        /// <summary>
        /// 한 줄을 만든다. <b>순수 함수라 테스트가 형식과 계산을 지킨다</b> —
        /// 파서(<c>scripts/ci/perflog.py</c>)가 이 형식에 맞춰져 있어 조용히 바뀌면
        /// 그 도구가 아무것도 못 읽는다.
        /// </summary>
        public static string Line(List<float> frameMs, float now, int players, int props)
        {
            if (frameMs == null || frameMs.Count == 0)
                return $"[PERF] t={N(now)} fps=0.0 min=0.0 p95=0.0 frames=0 players={players} props={props}";

            float total = 0f;
            float worst = 0f;
            for (int i = 0; i < frameMs.Count; i++)
            {
                total += frameMs[i];
                if (frameMs[i] > worst) worst = frameMs[i];
            }

            float meanMs = total / frameMs.Count;

            return $"[PERF] t={N(now)} "
                   + $"fps={N(1000f / Mathf.Max(meanMs, 0.001f))} "
                   + $"min={N(1000f / Mathf.Max(worst, 0.001f))} "
                   + $"p95={N(Percentile(frameMs, 0.95f))} "
                   + $"frames={frameMs.Count} "
                   + $"players={players} "
                   + $"props={props}";
        }

        /// <summary>
        /// 기계가 읽는 숫자. <b>로케일을 고정한다</b> — 소수점이 쉼표인 환경에서
        /// 문자열 보간을 쓰면 <c>fps=52,3</c> 이 되어 파서가 조용히 못 읽는다.
        /// 이 줄은 사람이 아니라 <c>scripts/ci/perflog.py</c> 가 읽는 것이 목적이다.
        /// </summary>
        static string N(float value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 프레임 시간의 백분위(ms). <b>정렬은 복사본에 한다</b> — 원본을 정렬하면
        /// 최저값 계산이 이미 끝났더라도 부르는 쪽의 목록을 몰래 바꿔 놓는다.
        /// </summary>
        public static float Percentile(List<float> values, float fraction)
        {
            var sorted = new List<float>(values);
            sorted.Sort();

            int index = Mathf.Clamp(Mathf.CeilToInt(fraction * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[index];
        }
    }
}
