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
        ///
        /// <para><b>이 초기화식에 기대지 않는다</b> (S15P21B202-588). 값이 비어 있는 채로
        /// 쓰인 실측이 있다 — 2026-09-17 개발 빌드에서 방장의 로그가
        /// <c>확인 실패 · &lt;빈칸&gt; · HTTP 0 · ConnectionError</c> 를 찍었고, 빈 주소라
        /// 요청이 <b>나가지도 못해</b> 참가자가 전원 거절됐다(nginx 에 기록 없음).
        /// 같은 빌드가 다른 PC 에서는 멀쩡했으니 <b>언제 채워질지가 보장되지 않는다.</b>
        /// 그래서 <see cref="ResolveVerifyUrl"/> 이 <b>쓸 때</b> 다시 정한다.</para>
        /// </summary>
        public string VerifyUrl { get; set; } = BackendEndpoints.VerifyUrl;

        /// <summary>
        /// 이번 확인에 쓸 주소. <b>담아 둔 값을 믿지 않는다</b> (S15P21B202-588).
        ///
        /// <para>덮어쓴 값(<c>-verifyUrl=</c>)이 있으면 그것을 쓰고, 없으면
        /// <see cref="BackendEndpoints"/> 에 <b>그 자리에서</b> 묻는다 — 그쪽은 실행 인자와
        /// 빌드 채널을 이미 반영한 값이라 한 실행이 한 환경만 본다.</para>
        /// </summary>
        public string ResolveVerifyUrl()
        {
            return string.IsNullOrWhiteSpace(VerifyUrl) ? BackendEndpoints.VerifyUrl : VerifyUrl.Trim();
        }

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

            // **무엇을 들고 있는지 먼저 말한다** (S15P21B202-588). 참가자가 거절될 때
            // 원인을 찾느라 서버·방화벽·릴레이를 차례로 뒤졌는데, 정작 "이 서버가 어느
            // 주소로 물어보려 하는가" 가 로그에 없었다. 한 줄이면 다음엔 바로 보인다.
            string url = ResolveVerifyUrl();
            if (string.IsNullOrWhiteSpace(url))
                Debug.LogError("[MatchAuthenticator] 확인 주소가 비어 있다 — 참가자를 아무도 못 받는다");
            else
                Debug.Log("[MatchAuthenticator] 확인 주소 " + url);
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

            // **쓸 때 정한다** (S15P21B202-588). 담아 둔 값이 비어 있으면 빈 주소로
            // 요청하게 되고, 그것은 `ConnectionError` 라 **서버 장애와 구분이 안 된다.**
            string verifyUrl = ResolveVerifyUrl();
            if (string.IsNullOrWhiteSpace(verifyUrl))
            {
                // 여기서도 거절하는 것은 맞다 — 확인 못 하면 안 들여보낸다. 다만
                // **왜인지를 말한다.** "로그인 서버에 확인할 수 없습니다" 로 뭉개면
                // 사람이 멀쩡한 서버를 뒤진다.
                Deny(conn, "이 게임 서버에 확인 주소가 없습니다. 방장에게 알려 주세요.");
                Debug.LogError("[MatchAuthenticator] 확인 주소가 비어 있다 — 요청을 보내지 못했다");
                yield break;
            }

            using (var web = new UnityWebRequest(verifyUrl, UnityWebRequest.kHttpVerbPOST))
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
                    //
                    // **거절과 장애를 가른다** (S15P21B202-573). 401 은 서버가 멀쩡히
                    // 답한 것이고 "확인할 수 없다" 가 아니다. 둘을 한 문구로 뭉개면
                    // 사람이 서버 장애를 찾는다 — 실제로 그렇게 시간을 썼다.
                    Deny(conn, web.responseCode == 401
                        ? "로그인 서버가 이 접속을 거절했습니다. 방과 로그인이 같은 서버인지 확인하세요."
                        : "로그인 서버에 확인할 수 없습니다.");
                    // `web.error` 를 같이 찍는다 (S15P21B202-588) — `responseCode`·`result`
                    // 만으로는 "주소가 빈 것" 과 "서버가 안 뜬 것" 이 똑같이
                    // `HTTP 0 · ConnectionError` 로 보인다.
                    Debug.LogWarning($"[MatchAuthenticator] 확인 실패 · {verifyUrl} · "
                                     + $"HTTP {web.responseCode} · {web.result} · {web.error}");
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
