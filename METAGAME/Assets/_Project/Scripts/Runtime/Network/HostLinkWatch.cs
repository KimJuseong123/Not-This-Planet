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

            if (quietFor >= GiveUpAfter)
            {
                GiveUp();
                return;
            }

            Notice = quietFor >= WarnAfter
                ? "방장과 연결이 불안정합니다…"
                : string.Empty;
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
