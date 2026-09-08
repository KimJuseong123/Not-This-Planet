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
    /// <c>%USERPROFILE%\AppData\LocalLow\SpaceFlop\둥실둥실 우주 대탈출\Player.log</c></para>
    ///
    /// <para>한 줄 형식(1초마다):
    /// <code>
    /// [NETDIAG] t=12.3 role=host rtt=75.2 fps=118 btm=2.48 buftime=82.6 buf=6
    ///           tracked=6 clamps=16 drops=3 pending=3 live=2 scars=5 crew=2 poses=a1b2c3d4
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
            // kcp 는 자기 통계를 스스로 로그에 남긴다(1초마다 SendQueue·MaxSendRate 등).
            // 그쪽 내부 카운터는 protected 라 밖에서 못 읽으므로 그 기능을 그대로 쓴다.
            // 씬을 고치지 않고 여기서 켠다 — 씬 편집은 배치모드 빌드가 재직렬화한다.
            if (Transport.active is KcpTransport kcp)
            {
                kcp.statisticsLog = true;
                Debug.Log("[NETDIAG] kcp statisticsLog 를 켰다 — SendQueue·MaxSendRate 는 kcp 가 남긴다");
            }
            else
            {
                Debug.Log("[NETDIAG] 활성 전송이 kcp 가 아니다 — 전송 통계는 남지 않는다");
            }

            Debug.Log("[NETDIAG] 시작. 형식: t role rtt fps btm buftime buf tracked clamps "
                      + "drops pending live scars crew poses");
        }

        void Update()
        {
            frames += 1f;
            timer += Time.unscaledDeltaTime;
            if (timer < Interval) return;

            float fps = frames / timer;
            timer = 0f;
            frames = 0f;

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
            Append("t", Time.timeAsDouble.ToString("F1"));
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
            Append("drops", worldSync != null ? worldSync.DropCount.ToString() : "-");
            Append("pending", spawner != null ? spawner.Pending.Count.ToString() : "-");
            Append("live", spawner != null ? spawner.Live.Count.ToString() : "-");
            Append("scars", spawner != null ? spawner.Scars.Count.ToString() : "-");
            Append("crew", spawner != null ? spawner.Crew.ToString() : "-");

            // 세계가 실제로 같은지 증명하는 값. 양쪽 로그에서 같은 t 의 이것을 견준다.
            Append("poses", worldSync != null ? worldSync.PoseHash().ToString("x8") : "-");

            Debug.Log(line.ToString());
        }

        void Append(string key, string value)
        {
            line.Append(' ').Append(key).Append('=').Append(value);
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
