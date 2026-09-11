using Metagame.Core;
using System;
using System.Collections;
using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.Networking;

namespace Metagame.Network
{
    /// <summary>
    /// 게임 서버에 붙을 때 "로비에서 로그인한 그 사람이 맞는지" 를 확인한다.
    ///
    /// 게임 서버는 로비를 모른다. 누가 어느 방 사람인지 아는 곳은 Spring 뿐이라,
    /// 클라이언트가 보낸 토큰을 서버가 Spring 에 물어보고 그 답으로 신원을 정한다.
    /// 이 과정을 거치지 않으면 방 코드만 알면 아무나 남의 매치에 들어올 수 있다.
    /// </summary>
    public sealed class MatchAuthenticator : NetworkAuthenticator
    {
        /// <summary>
        /// 확인을 요청할 Spring 주소. 게임 서버에서만 쓴다.
        ///
        /// <para><b>기본값을 손으로 적어 두면 안 된다</b> (S15P21B202-284). 예전에는 prod
        /// 주소가 여기 박혀 있었고 <c>-verifyUrl=</c> 실행 인자로만 덮였다. 그 인자는 옛
        /// 데디케이티드 서버가 쓰던 것이라 <b>리슨 서버 호스트에는 오지 않는다</b> — 즉
        /// 개발 빌드로 호스트를 하면 참가자 인증을 <b>배포 백엔드에 물었다.</b> 로그인은
        /// dev 로 하고 인증은 prod 로 가므로 토큰이 맞지 않아 아무도 못 들어오거나, 더 나쁘게
        /// 개발 중인 판이 실유저 계정을 건드린다.</para>
        /// </summary>
        public string VerifyUrl { get; set; } = BackendEndpoints.VerifyUrl;

        /// <summary>이 게임 서버가 맡은 방. 이 방 사람만 받는다 (서버 하나 = 매치 하나).</summary>
        public string RoomId { get; set; } = string.Empty;

        /// <summary>클라이언트가 보낼 값. 로비에서 채워 둔다.</summary>
        public static string ClientToken;
        public static string ClientRoomId;

        public struct AuthRequest : NetworkMessage
        {
            public string token;
            public string roomId;
        }

        public struct AuthResponse : NetworkMessage
        {
            public bool ok;
            public string message;
        }

        // ── 클라이언트 ────────────────────────────────────────────────────────

        public override void OnStartClient()
        {
            NetworkClient.RegisterHandler<AuthResponse>(OnAuthResponse, false);
        }

        public override void OnClientAuthenticate()
        {
            NetworkClient.Send(new AuthRequest
            {
                token = ClientToken ?? string.Empty,
                roomId = ClientRoomId ?? string.Empty
            });
        }

        private void OnAuthResponse(AuthResponse response)
        {
            if (response.ok)
            {
                ClientAccept();
                return;
            }

            Debug.LogWarning("[MatchAuthenticator] 게임 서버가 접속을 거절했다: " + response.message);
            ClientReject();
        }

        // ── 서버 ─────────────────────────────────────────────────────────────

        public override void OnStartServer()
        {
            NetworkServer.RegisterHandler<AuthRequest>(OnAuthRequest, false);
        }

        private void OnAuthRequest(NetworkConnectionToClient conn, AuthRequest request)
        {
            StartCoroutine(VerifyRoutine(conn, request));
        }

        private IEnumerator VerifyRoutine(NetworkConnectionToClient conn, AuthRequest request)
        {
            // 이 서버가 맡은 방인지 먼저 본다. Spring 에 물어볼 것도 없다.
            if (!string.IsNullOrEmpty(RoomId) &&
                !string.Equals(RoomId, request.roomId, StringComparison.Ordinal))
            {
                Deny(conn, "이 서버의 방이 아닙니다.");
                yield break;
            }

            string payload = JsonUtility.ToJson(new VerifyBody
            {
                token = request.token ?? string.Empty,
                roomId = request.roomId ?? string.Empty
            });

            using (var web = new UnityWebRequest(VerifyUrl, UnityWebRequest.kHttpVerbPOST))
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                web.downloadHandler = new DownloadHandlerBuffer();
                web.SetRequestHeader("Content-Type", "application/json");
                web.timeout = 10;
                yield return web.SendWebRequest();

                if (web.result != UnityWebRequest.Result.Success)
                {
                    // 확인할 수 없으면 들여보내지 않는다. 로비 서버가 죽었을 때
                    // 아무나 들어오는 것보다 아무도 못 들어오는 편이 낫다.
                    Deny(conn, "로그인 서버에 확인할 수 없습니다.");
                    yield break;
                }

                VerifyResult result;
                try
                {
                    result = JsonUtility.FromJson<VerifyResult>(web.downloadHandler.text);
                }
                catch (Exception)
                {
                    Deny(conn, "확인 응답을 읽을 수 없습니다.");
                    yield break;
                }

                if (result == null || result.userId <= 0)
                {
                    Deny(conn, "확인되지 않은 사용자입니다.");
                    yield break;
                }

                // 이 연결의 신원. 뒤 이슈에서 플레이어를 스폰할 때 쓴다.
                conn.authenticationData = new MatchIdentity(result.userId, result.nickname);
                conn.Send(new AuthResponse { ok = true, message = string.Empty });
                ServerAccept(conn);
            }
        }

        private void Deny(NetworkConnectionToClient conn, string message)
        {
            conn.Send(new AuthResponse { ok = false, message = message });
            ServerReject(conn);
        }

        [Serializable]
        private sealed class VerifyBody
        {
            public string token;
            public string roomId;
        }

        [Serializable]
        private sealed class VerifyResult
        {
            public long userId;
            public string nickname;
        }
    }

    /// <summary>확인을 통과한 연결의 신원. 게임 서버가 들고 있는다.</summary>
    public sealed class MatchIdentity
    {
        public long UserId { get; }
        public string Nickname { get; }

        public MatchIdentity(long userId, string nickname)
        {
            UserId = userId;
            Nickname = string.IsNullOrWhiteSpace(nickname) ? userId.ToString() : nickname;
        }
    }
}
