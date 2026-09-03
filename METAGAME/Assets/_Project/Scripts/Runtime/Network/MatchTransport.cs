using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 이 매치를 어떤 길로 보낼지 정한다 — 곧바로 방장에게, 아니면 릴레이를 거쳐서
    /// (S15P21B202-264).
    ///
    /// <b>왜 런타임에 바꾸나.</b> 같은 네트워크에서는 방장에게 직접 붙는 것이 가장 빠르고
    /// 릴레이가 죽어도 굴러간다. 서로 다른 네트워크에서는 공유기가 막아 직접 붙을 수 없으니
    /// 릴레이를 거쳐야 한다. 어느 쪽인지는 방을 시작할 때 서버가 알려 주므로, 씬에 미리
    /// 정해 둘 수 없다.
    ///
    /// <b>왜 자식 오브젝트인가.</b> <c>KcpTransport</c> 에 <c>[DisallowMultipleComponent]</c>
    /// 가 붙어 있고 <c>EdgegapKcpTransport</c> 가 그 자식 클래스라, 둘을 한 오브젝트에
    /// 함께 둘 수 없다. 그래서 릴레이 전송은 따로 만든 오브젝트에 붙인다.
    /// </summary>
    public static class MatchTransport
    {
        /// <summary>릴레이 세션 한 벌. 서버가 만들어 알려 준다.</summary>
        public struct Relay
        {
            public string Address;
            public ushort ServerPort;
            public ushort ClientPort;

            /// <summary>방 전원이 같은 값(세션 단위 authorization_token).</summary>
            public uint SessionToken;

            /// <summary>사람마다 다른 값(session_users[i].authorization_token).</summary>
            public uint UserToken;

            public bool IsUsable =>
                !string.IsNullOrWhiteSpace(Address) && SessionToken != 0 && UserToken != 0;
        }

        const string RelayObjectName = "~릴레이 전송";

        static Transport direct;

        /// <summary>씬이 원래 들고 있던 전송으로 되돌린다(같은 네트워크 직접 접속).</summary>
        public static void UseDirect(NetworkManager manager)
        {
            if (manager == null || direct == null) return;

            manager.transport = direct;
            Transport.active = direct;
        }

        /// <summary>
        /// 릴레이를 거치도록 바꾼다. 실패하면 직접 접속을 그대로 두고 거짓을 돌려준다 —
        /// 릴레이 하나 때문에 매치가 아예 안 열리는 것보다 낫다.
        /// </summary>
        public static bool UseRelay(NetworkManager manager, Relay relay)
        {
            if (manager == null) return false;

            if (!relay.IsUsable)
            {
                Debug.LogWarning("[MatchTransport] 릴레이 값이 모자라 직접 접속으로 간다.");
                return false;
            }

            Remember(manager);

            var holder = GameObject.Find(RelayObjectName);
            if (holder == null)
            {
                holder = new GameObject(RelayObjectName);
                holder.transform.SetParent(manager.transform, false);
            }

            var transport = holder.GetComponent<Edgegap.EdgegapKcpTransport>();
            if (transport == null) transport = holder.AddComponent<Edgegap.EdgegapKcpTransport>();

            transport.relayAddress = relay.Address;
            transport.relayGameServerPort = relay.ServerPort;
            transport.relayGameClientPort = relay.ClientPort;
            transport.sessionId = relay.SessionToken;
            transport.userId = relay.UserToken;

            // 화면에 릴레이 상태 창을 띄우는 기능이다. 게임 화면에 겹치므로 끈다.
            transport.relayGUI = false;

            manager.transport = transport;
            Transport.active = transport;

            // 릴레이가 우리를 알아보는지 지켜본다. 답이 아예 없으면 Edgegap 전송은
            // 아무 로그도 남기지 않아, 그때 무슨 일이 있었는지 알 길이 없다.
            RelayWatch.Attach(transport);

            Debug.Log($"[MatchTransport] 릴레이로 간다 · {relay.Address} " +
                      $"(서버 {relay.ServerPort} · 클라 {relay.ClientPort})");
            return true;
        }

        /// <summary>씬의 원래 전송을 한 번만 기억해 둔다.</summary>
        static void Remember(NetworkManager manager)
        {
            if (direct != null) return;
            if (manager.transport != null && manager.transport.gameObject.name != RelayObjectName)
                direct = manager.transport;
        }
    }
}
