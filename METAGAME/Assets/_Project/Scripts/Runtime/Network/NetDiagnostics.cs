using System.Text;
using Mirror;
using kcp2k;
using UnityEngine;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;

namespace Metagame.Network
{
    /// <summary>
    /// 멀티플레이 검증에 필요한 값을 <b>로그에 한 줄로</b> 남긴다 (S15P21B202-352).
    ///
    /// <para><b>왜 GUI 가 아니라 로그인가.</b> 2026-09-08 1차 실측을 화면의 오버레이를 읽어
    /// 손으로 적었더니 느리고 빠뜨렸다 — <c>KcpTransport.statisticsGUI</c> 박스는 HUD 에
    /// 가려 아예 못 봤고(<c>Rect(5, 110, ...)</c>), RTT 는 외부 <c>ping</c> 으로 어림했고,
    /// FPS 는 측정 도구가 꺼져 빈칸이었다. 무엇보다 <b>두 화면을 동시에 볼 수 없어</b>
    /// 호스트와 참가자가 같은 상태인지 확인할 방법이 없었다.</para>
    ///
    /// <para>그래서 양쪽이 <b>같은 형식으로</b> 로그에 남기고, 나중에 두 로그를 기계로
    /// 대조한다(<c>scripts/ci/netdiag.py</c>). 특히 <c>poses</c> 체크섬을 견주면
    /// <b>세계가 실제로 같은지 증명</b>된다 — 눈으로 좌표를 비교할 필요가 없다.</para>
    ///
    /// <para><b>켜는 방법</b>: 실행 인자 <c>-netdiag</c> 또는 환경변수
    /// <c>METAGAME_NETDIAG=1</c>. 평소 실행에는 아무 영향이 없다.</para>
    ///
    /// <para><b>로그 위치</b>:
    /// <c>%USERPROFILE%\AppData\LocalLow\NotThisPlanet\Not This Planet\Player.log</c></para>
    ///
    /// <para>한 줄 형식(1초마다):
    /// <code>
    /// [NETDIAG] t=12.0 role=host rtt=75.2 fps=118 btm=2.48 buftime=82.6 buf=6
    ///           tracked=6 clamps=16 tools=2 drops=3 pending=3 live=2 scars=5 crew=2
    ///           seq=360 poses=a1b2c3d4
    /// </code>
    /// <c>KcpTransport.statisticsLog</c> 도 함께 켜므로 <c>SendQueue</c>·<c>MaxSendRate</c> 는
    /// kcp 자신이 1초마다 남긴다 — 그 값들은 <c>client</c> 필드가 protected 라 밖에서
    /// 못 읽는다. 바퀴를 다시 만들지 않는다.</para>
    /// </summary>
    public sealed class NetDiagnostics : MonoBehaviour
    {
        const float Interval = 1f;
        const string Flag = "-netdiag";
        const string EnvVar = "METAGAME_NETDIAG";

        WorldSync worldSync;
        MeteorSpawner spawner;
        float timer;
        float frames;
        bool namesLogged;

        /// <summary>마지막으로 찍은 공유 시계의 초. <see cref="Update"/> 의 주석 참고.</summary>
        long lastBucket;
        bool hasBucket;

        /// <summary>이미 통계를 켜 둔 전송. 갈리면 새로 켠다.</summary>
        Transport loggedTransport;
        readonly StringBuilder line = new StringBuilder(256);

        /// <summary>인자나 환경변수로 켜졌는가. 평소 실행에서는 이 컴포넌트를 붙이지 않는다.</summary>
        public static bool Requested
        {
            get
            {
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                    if (string.Equals(args[i], Flag, System.StringComparison.OrdinalIgnoreCase))
                        return true;
                return System.Environment.GetEnvironmentVariable(EnvVar) == "1";
            }
        }

        void Start()
        {
            EnableKcpLog();

            Debug.Log("[NETDIAG] 시작. 형식: t role rtt fps btm buftime buf tracked clamps "
                      + "tools drops pending live scars crew seq poses");
        }

        void Update()
        {
            frames += 1f;
            timer += Time.unscaledDeltaTime;

            // **공유 시계의 정수 초에 맞춰 찍는다** (AI 리뷰 지적, 2026-09-08).
            //
            // 예전에는 각자 자기 프로세스 시작부터 1초마다 찍고 t 에
            // `Time.timeAsDouble` 을 넣었다. 그러면 두 창을 30초 차이로 띄웠을 때
            // 호스트는 12.3·13.3·… 참가자는 12.8·13.8·… 을 찍어 **겹치는 t 가 하나도
            // 없다** — netdiag.py 는 t 가 같은 줄끼리 짝지으므로 "겹치는 구간이 없다"
            // 로 끝나거나, 더 나쁘게는 **서로 다른 실제 순간을 같은 시각으로 비교**한다.
            // 검증 도구가 조용히 거짓 판정을 내는 것이 이 이슈에서 가장 피하려던 것이다.
            //
            // 그래서 표본 시각을 공유 시계(NetworkTime.time)의 초 경계로 삼는다. 양쪽이
            // 같은 t 를 찍고, 그 t 는 **실제로 같은 순간**이다.
            var bucket = (long)System.Math.Floor(SimulationTime());
            if (bucket == lastBucket) return;

            bool firstBucket = !hasBucket;
            hasBucket = true;
            lastBucket = bucket;

            // 첫 구간은 길이를 모른다(붙은 시점이 초 경계와 무관하다) — FPS 를 왜곡하므로
            // 값을 버리고 다음 초부터 찍는다.
            if (firstBucket)
            {
                timer = 0f;
                frames = 0f;
                return;
            }

            float fps = timer > 0f ? frames / timer : 0f;
            timer = 0f;
            frames = 0f;

            // 전송이 늦게 뜨거나 갈려도 통계를 놓치지 않는다 — EnableKcpLog 의 주석 참고.
            EnableKcpLog();

            if (worldSync == null) worldSync = FindAnyObjectByType<WorldSync>();
            if (spawner == null) spawner = FindAnyObjectByType<MeteorSpawner>();

            // 개수가 갈렸을 때 **어느 물체가 다른지** 가르려면 이름이 필요하다.
            // 매 초 찍으면 로그가 덮이므로 목록이 생긴 뒤 한 번만 남긴다.
            if (!namesLogged && worldSync != null && worldSync.TrackedCount > 0)
            {
                namesLogged = true;
                Debug.Log($"[NETDIAG] tracked({worldSync.TrackedCount}) = {worldSync.TrackedNames()}");
            }

            line.Length = 0;
            line.Append("[NETDIAG]");
            Append("t", ((double)bucket).ToString("F1"));
            Append("role", Role());
            Append("rtt", NetworkClient.active ? (NetworkTime.rtt * 1000.0).ToString("F1") : "-");
            Append("fps", fps.ToString("F0"));

            // 가설 ② 를 직접 답하는 두 값. buftime 은 ms 다 — 예산표의 항과 바로 견줄 수 있다.
            Append("btm", NetworkClient.active ? NetworkClient.bufferTimeMultiplier.ToString("F2") : "-");
            Append("buftime", NetworkClient.active ? (NetworkClient.bufferTime * 1000.0).ToString("F1") : "-");
            Append("buf", NetworkClient.active ? NetworkClient.snapshots.Count.ToString() : "-");

            // 세계 상태 — 이 값들이 양쪽에서 다르면 그 자리가 갈린 지점이다.
            Append("tracked", worldSync != null ? worldSync.TrackedCount.ToString() : "-");
            Append("clamps", worldSync != null ? worldSync.ClampCount.ToString() : "-");
            Append("tools", worldSync != null ? worldSync.ToolCount.ToString() : "-");
            Append("drops", worldSync != null ? worldSync.DropCount.ToString() : "-");
            Append("pending", spawner != null ? spawner.Pending.Count.ToString() : "-");

            // **양쪽을 견줄 수 있는 값으로 만든다.** 참가자에게는 진짜 운석이 없고
            // 그림만 있으므로(MeteorSpawner.Ghosts) Live 만 찍으면 호스트 3 대 참가자 0 이
            // 되어 "다르다" 가 늘 참이 된다. 뜻은 하나다 — **지금 하늘에 떠 있는 운석이
            // 몇 개인가**. 그러니 한 항으로 합쳐 찍는다.
            Append("live", spawner != null
                ? (spawner.Live.Count + spawner.Ghosts.Count).ToString()
                : "-");
            Append("scars", spawner != null ? spawner.Scars.Count.ToString() : "-");
            Append("crew", spawner != null ? spawner.Crew.ToString() : "-");

            // 세계가 실제로 같은지 증명하는 값. **같은 seq 의 poses 를** 견준다 —
            // 시각으로 짝지으면 보간 지연 때문에 움직이는 것이 늘 갈려 보인다
            // (WorldSync.BundleSeq 의 주석에 그 계산이 있다).
            Append("seq", worldSync != null ? worldSync.BundleSeq.ToString() : "-");
            Append("poses", worldSync != null ? worldSync.BundleHash.ToString("x8") : "-");

            Debug.Log(line.ToString());
        }

        /// <summary>
        /// kcp 가 자기 통계를 로그에 남기게 한다(1초마다 <c>SendQueue</c>·<c>MaxSendRate</c> 등).
        /// 그쪽 내부 카운터는 <c>protected</c> 라 밖에서 못 읽으므로 그 기능을 그대로 쓴다.
        /// 씬을 고치지 않고 코드에서 켠다 — 씬 편집은 배치모드 빌드가 재직렬화한다.
        ///
        /// <para><b>될 때까지 다시 시도한다.</b> 한 번만 켜면 두 가지에 진다: 붙는 순간
        /// <c>Transport.active</c> 가 아직 없을 수 있고, <c>MatchTransport</c> 가 나중에
        /// <b>다른 전송으로 갈아 끼울</b> 수도 있다(직결 → 릴레이). 그러면
        /// <c>SendQueue</c>·<c>MaxSendRate</c> 가 로그에 하나도 남지 않는다 — 1차 실측에서
        /// 가설 ③④ 가 빈칸이던 바로 그 결과다.</para>
        /// </summary>
        void EnableKcpLog()
        {
            var kcp = Transport.active as KcpTransport;
            if (kcp == null) return;
            if (ReferenceEquals(kcp, loggedTransport)) return;

            kcp.statisticsLog = true;
            loggedTransport = kcp;
            Debug.Log("[NETDIAG] kcp statisticsLog 를 켰다 — SendQueue·MaxSendRate 는 kcp 가 남긴다");
        }

        void Append(string key, string value)
        {
            line.Append(' ').Append(key).Append('=').Append(value);
        }

        /// <summary>
        /// 두 로그를 <b>짝지을 수 있는</b> 시각.
        ///
        /// <para>접속해 있으면 <c>NetworkTime.time</c> — Mirror 가 맞춰 주는 공유 시계라
        /// 호스트와 참가자가 같은 값을 본다. 혼자 실행일 때만 로컬 시간으로 물러난다.
        /// <see cref="DustStormClockBridge"/> 가 시뮬레이션 시각을 고르는 방식과 같다.</para>
        /// </summary>
        static double SimulationTime()
        {
            return NetworkClient.active || NetworkServer.active
                ? NetworkTime.time
                : Time.unscaledTimeAsDouble;
        }

        /// <summary>
        /// 이 기계의 역할. <c>MeteorClockBridge.HasAuthority</c> 와 <b>같은 식</b>이어야
        /// 로그를 읽을 때 헷갈리지 않는다.
        /// </summary>
        static string Role()
        {
            if (MatchRole.DedicatedServer) return "server";
            if (NetworkServer.active) return NetworkClient.active ? "host" : "server";
            if (NetworkClient.active) return "client";
            return "solo";
        }
    }
}
