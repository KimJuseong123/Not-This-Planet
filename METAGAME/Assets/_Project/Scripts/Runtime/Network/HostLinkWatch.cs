using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 방장과의 연결이 조용해지면 알리고, 오래 조용하면 로비로 돌려보낸다
    /// (S15P21B202-385).
    ///
    /// <b>왜 필요한가.</b> 방장이 나가도 화면은 마지막으로 받은 자세에 멈춘 채
    /// <b>10초 동안 아무 말이 없었다</b>. KCP 전송의 <c>Timeout</c> 이 10000ms 라 그때까지
    /// 클라이언트가 끊김을 알아채지 못한다. 사용자에게는 게임이 죽은 것으로 보인다.
    ///
    /// 돌아가는 길 자체는 이미 있다 — <c>Lobby.unity</c> 의 NetworkManager 에
    /// <c>offlineScene</c> 이 로비로 설정돼 있어서, 끊기면 Mirror 가 로비를 다시 연다.
    /// 문제는 <b>그때까지 걸리는 시간과 설명의 부재</b>였다.
    ///
    /// <b>참가자 화면에서만 돈다.</b> 호스트는 자기가 서버라 조용해질 일이 없다.
    /// </summary>
    public sealed class HostLinkWatch : MonoBehaviour
    {
        /// <summary>이만큼 조용하면 "불안정" 을 띄운다(초).</summary>
        const float WarnAfter = 1.5f;

        /// <summary>
        /// 이만큼 조용하면 포기하고 로비로 돌아간다(초).
        ///
        /// <para>KCP 의 10초를 그대로 기다리지 않는다. 릴레이를 거쳐도 왕복이 100ms 안이고
        /// KCP 가 재전송까지 하므로, 5초 내내 아무것도 오지 않는 것은 이미 복구 가능한
        /// 상태가 아니다. 다만 짧게 잡으면 순간적인 끊김에 사람을 내보내므로 여유를 뒀다.</para>
        /// </summary>
        const float GiveUpAfter = 5f;

        /// <summary>지금 화면에 띄울 문구. 비어 있으면 아무 것도 안 띄운다.</summary>
        public static string Notice { get; private set; } = string.Empty;

        float quietFor;
        bool leaving;

        void OnEnable()
        {
            Notice = string.Empty;
            quietFor = 0f;
            leaving = false;
        }

        void OnDisable() => Notice = string.Empty;

        void Update()
        {
            if (Metagame.Gameplay.GameSession.Instance != null
                && Metagame.Gameplay.GameSession.Instance.EndingActive) return;
            // 호스트(서버 겸 클라이언트)와 솔로는 볼 것이 없다.
            if (!NetworkClient.active || NetworkServer.active)
            {
                Clear();
                return;
            }

            var connection = NetworkClient.connection;
            if (connection == null)
            {
                Clear();
                return;
            }

            quietFor = Time.time - connection.lastMessageTime;

            // 이전이 오는 중이면 기다린다 (S15P21B202-389).
            //
            // <b>추측이 아니라 들은 것이다.</b> 방장이 바뀐 것은 로비 웹소켓으로 오는데
            // 그 연결은 Spring 에 붙어 있어 방장의 PC 와 무관하게 살아 있다. 그것을 듣고도
            // 5초에 로비로 내려보내면 <b>이어받기가 시작되기 전에 사람들이 나간다</b>.
            //
            // 그래도 언젠가는 포기한다 — 새 방장까지 함께 죽으면 아무 소식이 오지 않는다.
            // 이전이 올 수 있으면 기다린다 (S15P21B202-389).
            //
            // <b>이전 알림을 기다리는 것으로는 늦다.</b> 예전에는 <c>HostMigration.Pending</c>
            // 만 봤는데, 그 값은 <b>로비 웹소켓으로 ROOM_STATE 가 도착한 뒤에야</b> 참이
            // 된다. 게임 연결(KCP)은 방장이 사라지는 순간 조용해지지만 방 상태는 서버를
            // 거쳐 오므로 몇 초 늦을 수 있고, 그 사이 5초가 지나면 <b>이전이 시작되기도
            // 전에 전원이 로비로 내려간다.</b>
            //
            // 그 다음이 더 나쁘다. 로비에서 다시 들어가면 새 방장이 된 사람이
            // <c>StartHost</c> 로 <b>새 판을 연다</b> — 게임은 되는데 탑·용접·시간이 전부
            // 초기화된다. 돌아온 옛 방장은 릴레이에서 아직 <b>서버 토큰</b>의 주인이라
            // 참가자로 붙지 못하고 로딩창에서 끊긴다.
            //
            // 그래서 <b>알림이 아니라 방 상태</b>를 본다. 로비 연결은 Spring 에 붙어 있어
            // 방장의 PC 와 무관하게 살아 있으므로, 그 연결이 "이 방은 아직 PLAYING" 이라고
            // 말하는 동안은 기다릴 이유가 있다.
            //
            //  · 방이 PLAYING 이 아니게 되면(판 종료·방 사라짐) 곧바로 나간다
            //  · 로비 연결까지 끊겼으면 내 인터넷 문제다 — 기다릴 이유가 없다
            //  · 그래도 상한은 둔다. 새 방장까지 함께 죽으면 아무 소식도 오지 않는다
            float giveUp = MatchMayResume ? HostMigration.WaitForTakeover : GiveUpAfter;

            if (quietFor >= giveUp)
            {
                GiveUp();
                return;
            }

            if (HostMigration.Pending)
            {
                Notice = "방장이 바뀌었습니다 · 다시 연결하는 중…";
                return;
            }

            if (quietFor >= WarnAfter && MatchMayResume)
            {
                Notice = "방장과 연결이 끊겼습니다 · 이어받기를 기다리는 중…";
                return;
            }

            Notice = quietFor >= WarnAfter
                ? "방장과 연결이 불안정합니다…"
                : string.Empty;
        }

        /// <summary>
        /// 이 판이 다시 이어질 수 있는가 (S15P21B202-389).
        ///
        /// <para>이미 이전 알림을 받았거나, 로비 연결이 살아 있고 그 방이 아직
        /// <c>PLAYING</c> 이면 참이다. 후자가 중요하다 — 알림이 오기 <b>전에도</b>
        /// 기다려야 하기 때문이다.</para>
        /// </summary>
        static bool MatchMayResume
        {
            get
            {
                if (HostMigration.Pending) return true;

                var session = NetworkSession.Instance;
                if (session == null || !session.IsConnected) return false;

                var room = session.CurrentRoom;
                return room != null && room.IsPlaying;
            }
        }

        void Clear()
        {
            quietFor = 0f;
            if (!leaving) Notice = string.Empty;
        }

        /// <summary>
        /// 포기하고 로비로 돌아간다. <c>StopClient</c> 가 <c>offlineScene</c> 을 열어 주므로
        /// 씬 전환을 직접 하지 않는다.
        /// </summary>
        void GiveUp()
        {
            if (leaving) return;
            leaving = true;

            Notice = "방장과 연결이 끊겼습니다 · 로비로 돌아갑니다";
            Debug.LogWarning($"[HostLinkWatch] {GiveUpAfter:0.0}초 동안 아무것도 오지 않았다 — 로비로 돌아간다");

            var manager = NetworkManager.singleton;
            if (manager != null) manager.StopClient();
            else NetworkClient.Disconnect();
        }
    }
}
