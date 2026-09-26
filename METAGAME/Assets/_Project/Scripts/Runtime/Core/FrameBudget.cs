using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Metagame.Core
{
    /// <summary>
    /// 프레임이 어디에 쓰이는지 10초마다 로그 한 줄로 남긴다 (S15P21B202-746).
    ///
    /// <para>2026-09-26 QA: 그래픽 옵션을 낮춰도 렉이 비슷했다 — GPU 가 아니라 CPU 쪽이라는 뜻이다. 60fps 를 목표로 무엇을
    /// 줄일지 고르려면 물리 · 스크립트 · 렌더가 각각 몇 ms 를 쓰는지 알아야 한다.</para>
    ///
    /// <para><b>배포용 빌드에서도 잰다.</b> 처음에는 유니티의 내부 표시(ProfilerRecorder)를 읽었는데, CI 의 개발 빌드가
    /// <c>BuildOptions.Development</c> 가 아니라서 한 칸도 안 찍혔다(2026-09-26 QA). 그래서 <b>가장 먼저 도는 스크립트와
    /// 가장 늦게 도는 스크립트 사이의 시각</b>으로 나눈다 — <see cref="FrameBudgetEdge"/> 둘이 실행 순서 맨 앞과 맨 뒤에 선다.</para>
    ///
    /// <list type="bullet">
    /// <item>Fixed스크립트 — 물리 단계마다 FixedUpdate 맨 앞 → 맨 뒤</item>
    /// <item>물리 — FixedUpdate 맨 뒤 → 다음 물리 단계 맨 앞(또는 Update 맨 앞). 사이에 도는 것은 물리 시뮬레이션이 거의 전부다</item>
    /// <item>Update · Late — 각 맨 앞 → 맨 뒤</item>
    /// <item>렌더+대기 — LateUpdate 맨 뒤 → 다음 프레임 맨 앞. 그리기 · 화면 표시 대기(VSync) · OnGUI 가 여기 든다</item>
    /// </list>
    /// </summary>
    public sealed class FrameBudget : MonoBehaviour
    {
        public const float Window = 10f;

        static FrameBudget instance;
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static double Now => Clock.Elapsed.TotalMilliseconds;

        // 이번 창의 합(ms)
        double fixedScripts, physics, update, late, render, ai;
        double fixedStart = -1, fixedEnd = -1, updateStart = -1, lateEnd = -1;
        readonly List<float> frames = new List<float>(2048);
        ProfilerRecorder gc;
        double gcBytes;
        int fixedSteps, maxSteps;
        long stepSum;
        float windowStart;

        /// <summary>AI 가 한 틱에 쓴 시간(ms). <c>AiCompanionController</c> 가 더한다.</summary>
        public static void AddAi(double ms) { if (instance != null) instance.ai += ms; }
        public static double Stamp => Now;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isBatchMode || FindFirstObjectByType<FrameBudget>() != null) return;
            var go = new GameObject("Frame budget");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameBudget>();
        }

        void OnEnable()
        {
            instance = this;
            gameObject.AddComponent<FrameBudgetEdge>().First = true;
            gameObject.AddComponent<FrameBudgetLast>();
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            windowStart = Time.realtimeSinceStartup;
        }

        void OnDisable()
        {
            if (instance == this) instance = null;
            gc.Dispose();
        }

        // ── 가장자리에서 부른다 ──
        internal void FixedFirst()
        {
            double now = Now;
            if (fixedEnd >= 0) physics += now - fixedEnd;          // 앞 물리 단계의 시뮬레이션
            else if (lateEnd >= 0) render += now - lateEnd;        // 앞 프레임의 그리기 · 대기
            fixedEnd = -1; lateEnd = -1;
            fixedStart = now; fixedSteps++;
        }
        internal void FixedLast() { double now = Now; if (fixedStart >= 0) fixedScripts += now - fixedStart; fixedEnd = now; }
        internal void UpdateFirst()
        {
            double now = Now;
            if (fixedEnd >= 0) physics += now - fixedEnd;          // 마지막 물리 단계의 시뮬레이션
            else if (lateEnd >= 0) render += now - lateEnd;        // 물리 단계가 없던 프레임
            fixedEnd = -1; lateEnd = -1; updateStart = now;
        }
        internal void UpdateLast() { if (updateStart >= 0) update += Now - updateStart; }
        internal double lateStart = -1;
        internal void LateFirst() => lateStart = Now;
        internal void LateLast()
        {
            double now = Now;
            if (lateStart >= 0) late += now - lateStart;
            lateEnd = now;
            FrameDone();
        }

        void FrameDone()
        {
            frames.Add(Time.unscaledDeltaTime * 1000f);
            if (gc.Valid) gcBytes += gc.LastValue;
            if (fixedSteps > maxSteps) maxSteps = fixedSteps;
            stepSum += fixedSteps; fixedSteps = 0;
            if (Time.realtimeSinceStartup - windowStart >= Window) Flush();
        }

        /// <summary>지금 창을 바로 적는다 — 검사가 쓴다.</summary>
        public void FlushNow() => Flush();

        void Flush()
        {
            float seconds = Time.realtimeSinceStartup - windowStart;
            int n = frames.Count;
            if (n > 0)
            {
                frames.Sort();
                double total = 0; foreach (var f in frames) total += f;
                double mean = total / n;
                int over = 0; foreach (var f in frames) if (f > 17.5f) over++;   // VSync 의 16.7 흔들림은 넘김으로 치지 않는다
                var line = new StringBuilder(256);
                line.Append("[FRAME] ").Append(seconds.ToString("0")).Append("초 · 평균 ").Append(mean.ToString("0.0"))
                    .Append("ms(").Append((1000.0 / mean).ToString("0")).Append("fps) · p95 ")
                    .Append(frames[Mathf.Min(n - 1, (int)(n * .95f))].ToString("0")).Append(" · 최대 ")
                    .Append(frames[n - 1].ToString("0")).Append(" · 17.5ms 초과 ")
                    .Append((100f * over / n).ToString("0")).Append("% · 물리단계 ")
                    .Append(((double)stepSum / n).ToString("0.0")).Append("(최대 ").Append(maxSteps).Append(')')
                    .Append(" · 물리 ").Append((physics / n).ToString("0.0"))
                    .Append(" · Fixed스크립트 ").Append((fixedScripts / n).ToString("0.0"))
                    .Append(" · Update ").Append((update / n).ToString("0.0"))
                    .Append(" · Late ").Append((late / n).ToString("0.0"))
                    .Append(" · 렌더+대기 ").Append((render / n).ToString("0.0"))
                    .Append(" · AI ").Append((ai / n).ToString("0.00"));
                // FixedUpdate 스크립트의 세부 (프레임당 ms) — 0.05 이상만
                line.Append(" · 세부[");
                bool first = true;
                for (int i = 0; i < FrameSections.Count; i++)
                {
                    double per = FrameSections.Take(i) / n;
                    if (per < .05) continue;
                    if (!first) line.Append(' ');
                    line.Append(FrameSections.Names[i]).Append(' ').Append(per.ToString("0.0")); first = false;
                }
                line.Append(']');
                if (gc.Valid) line.Append(" · GC ").Append((gcBytes / seconds / 1048576.0).ToString("0.0")).Append("MB/s");
                line.Append(" · VSync ").Append(QualitySettings.vSyncCount).Append(" · 품질 ").Append(QualitySettings.GetQualityLevel());
                Debug.Log(line.ToString());
            }
            if (n == 0) for (int i = 0; i < FrameSections.Count; i++) FrameSections.Take(i);
            frames.Clear(); gcBytes = 0; stepSum = 0; maxSteps = 0;
            fixedScripts = physics = update = late = render = ai = 0;
            windowStart = Time.realtimeSinceStartup;
        }
    }

    /// <summary>실행 순서 맨 앞 — 각 단계의 시작을 적는다.</summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class FrameBudgetEdge : MonoBehaviour
    {
        internal bool First;
        FrameBudget budget;
        void Awake() => budget = GetComponent<FrameBudget>();
        void FixedUpdate() => budget?.FixedFirst();
        void Update() => budget?.UpdateFirst();
        void LateUpdate() => budget?.LateFirst();
    }

    /// <summary>실행 순서 맨 뒤 — 각 단계의 끝을 적는다.</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class FrameBudgetLast : MonoBehaviour
    {
        FrameBudget budget;
        void Awake() => budget = GetComponent<FrameBudget>();
        void FixedUpdate() => budget?.FixedLast();
        void Update() => budget?.UpdateLast();
        void LateUpdate() => budget?.LateLast();
    }
}
