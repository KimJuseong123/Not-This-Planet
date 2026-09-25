using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 프레임이 어디에 쓰이는지 10초마다 로그 한 줄로 남긴다 (S15P21B202-746).
    ///
    /// <para>2026-09-26 QA: 그래픽 옵션을 낮춰도 렉이 비슷했다 — GPU 가 아니라 CPU 쪽이라는 뜻이다. 60fps 를 목표로 무엇을
    /// 줄일지 고르려면 물리 · 스크립트 · 렌더 대기 · AI 가 각각 몇 ms 를 쓰는지 알아야 한다. 개발 빌드 QA 에서 그대로 쌓이게
    /// <c>Player.log</c> 에 적는다. 프로파일러를 붙일 필요가 없다.</para>
    ///
    /// <para><b>가볍게.</b> 기록기는 유니티가 이미 재는 표시(marker)의 마지막 값을 읽을 뿐이다. 문자열은 10초에 한 번만 만든다.</para>
    /// </summary>
    public sealed class FrameBudget : MonoBehaviour
    {
        public const float Window = 10f;

        /// <summary>(이름, 로그에 쓸 짧은 이름). 없는 표시는 조용히 0 이다 — 판마다·빌드마다 있는 것이 다르다.</summary>
        static readonly (string marker, string label)[] Markers =
        {
            ("FixedUpdate.PhysicsFixedUpdate", "물리"),
            ("FixedUpdate.ScriptRunBehaviourFixedUpdate", "Fixed스크립트"),
            ("Update.ScriptRunBehaviourUpdate", "Update"),
            ("PreLateUpdate.ScriptRunBehaviourLateUpdate", "Late"),
            ("PreLateUpdate.DirectorUpdateAnimationBegin", "애니"),
            ("PostLateUpdate.PlayerUpdateCanvases", "UI"),
            ("PostLateUpdate.FinishFrameRendering", "렌더"),
            ("Gfx.WaitForPresentOnGfxThread", "GPU대기"),
            ("AI.Companion.Tick", "AI"),
            ("AI.Navigation.CollectSources", "길수집"),
            ("AI.Navigation.ScheduleBuild", "길굽기"),
            ("Parts.Outline.Collect", "테두리"),
        };

        readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
        readonly double[] sums = new double[Markers.Length];
        readonly List<float> frames = new List<float>(2048);
        ProfilerRecorder gc;
        double gcBytes;
        int fixedSteps, frameSteps, maxSteps;
        float windowStart;

        /// <summary>지금 창을 바로 적는다 — 검사가 쓴다.</summary>
        public void FlushNow() => Flush();

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
            foreach (var (marker, _) in Markers) recorders.Add(new ProfilerRecorder(marker, 1));
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            windowStart = Time.realtimeSinceStartup;
        }

        void OnDisable()
        {
            foreach (var r in recorders) r.Dispose();
            recorders.Clear();
            gc.Dispose();
        }

        void FixedUpdate() => fixedSteps++;

        void Update()
        {
            // 앞 프레임의 값이다 — 표시는 프레임이 끝날 때 닫힌다.
            frames.Add(Time.unscaledDeltaTime * 1000f);
            for (int i = 0; i < recorders.Count; i++)
                if (recorders[i].Valid) sums[i] += recorders[i].LastValue / 1e6;
            if (gc.Valid) gcBytes += gc.LastValue;
            frameSteps = fixedSteps; fixedSteps = 0;
            if (frameSteps > maxSteps) maxSteps = frameSteps;
            stepSum += frameSteps;
            if (Time.realtimeSinceStartup - windowStart >= Window) Flush();
        }

        long stepSum;

        void Flush()
        {
            float seconds = Time.realtimeSinceStartup - windowStart;
            int n = frames.Count;
            if (n > 0)
            {
                frames.Sort();
                double total = 0; foreach (var f in frames) total += f;
                double mean = total / n;
                var line = new StringBuilder(256);
                line.Append("[FRAME] ").Append(seconds.ToString("0")).Append("초 · 평균 ").Append(mean.ToString("0.0"))
                    .Append("ms(").Append((1000.0 / mean).ToString("0")).Append("fps) · p95 ")
                    .Append(frames[Mathf.Min(n - 1, (int)(n * .95f))].ToString("0")).Append(" · 최대 ")
                    .Append(frames[n - 1].ToString("0")).Append(" · 16.7ms 초과 ")
                    .Append((100f * frames.FindAll(f => f > 16.7f).Count / n).ToString("0")).Append("% · 물리단계 ")
                    .Append(((double)stepSum / n).ToString("0.0")).Append("(최대 ").Append(maxSteps).Append(')');
                for (int i = 0; i < Markers.Length; i++)
                    if (recorders[i].Valid && sums[i] > 0)
                        line.Append(" · ").Append(Markers[i].label).Append(' ').Append((sums[i] / n).ToString("0.0"));
                if (gc.Valid) line.Append(" · GC ").Append((gcBytes / seconds / 1048576.0).ToString("0.0")).Append("MB/s");
                Debug.Log(line.ToString());
            }
            frames.Clear(); System.Array.Clear(sums, 0, sums.Length); gcBytes = 0; stepSum = 0; maxSteps = 0;
            windowStart = Time.realtimeSinceStartup;
        }
    }
}
