using System;
using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 실행 인자로 Mirror 를 서버·클라이언트로 띄운다.
    ///
    /// 헤드리스 자동 시작(NetworkManager.headlessStartMode)만으로는 한 PC 에서
    /// 서버 하나와 클라이언트 여럿을 동시에 띄울 수 없다 — 창 없이 실행하면 전부 서버가 된다.
    /// 검증과 서버 운영에 같은 실행 파일을 쓰기 위해 인자로 역할을 정한다.
    ///
    ///   MetaGame.exe -batchmode -nographics -server -roomId=K7M2QX
    ///   MetaGame.exe -host -roomId=K7M2QX          (방장 = 서버 겸 클라이언트)
    ///   MetaGame.exe -client -address=127.0.0.1
    ///   MetaGame.exe -server -port=27777 -verifyUrl=https://j15b202.p.ssafy.io/auth/verify
    ///
    /// 인자가 없으면 아무것도 하지 않는다. 평소 실행(로비에서 시작)에는 영향이 없다.
    /// </summary>
    public static class MirrorLaunchArgs
    {
        private const string ServerFlag = "-server";
        private const string HostFlag = "-host";
        private const string ClientFlag = "-client";
        private const string AddressPrefix = "-address=";
        private const string PortPrefix = "-port=";
        private const string RoomIdPrefix = "-roomId=";
        private const string VerifyUrlPrefix = "-verifyUrl=";

        /// 로비 없이 클라이언트를 띄워 볼 때만 쓴다. 토큰은 실행 인자에 두면
        /// 프로세스 목록에 그대로 보이므로 환경변수로 받는다.
        private const string TokenEnv = "METAGAME_MATCH_TOKEN";
        private const string RoomEnv = "METAGAME_MATCH_ROOM";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Apply()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool host = Has(args, HostFlag);
            bool server = Has(args, ServerFlag) || host;
            bool client = Has(args, ClientFlag);
            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                if (server || client)
                    Debug.LogError("[MirrorLaunchArgs] NetworkManager 가 없다. 로비 씬으로 시작했는지 확인할 것.");
                return;
            }

            // 이 콜백은 NetworkManager.Start() 보다 먼저 돈다. 자동 시작을 항상 꺼 두고
            // 여기서만 정한다.
            //
            // 끄지 않으면 인자 없이 헤드리스로 띄웠을 때도 Mirror 가 혼자 서버를 켜고
            // 7777 포트를 연다. 그러면 게임 씬이 "전용 서버" 로 판단해 내 캐릭터를 세우지
            // 않아, -space-selftest 같은 혼자 돌리는 점검이 조용히 아무것도 하지 않는다.
            manager.headlessStartMode = HeadlessStartOptions.DoNothing;

            if (!server && !client) return;

            if (NetworkServer.active || NetworkClient.active)
            {
                Debug.Log("[MirrorLaunchArgs] 이미 시작되어 있어 인자를 무시한다.");
                return;
            }

            ushort port;
            if (TryReadPort(args, out port))
                ApplyPort(manager, port);

            if (server)
            {
                // 서버 하나가 매치 하나를 맡는다. 방을 지정하면 그 방 사람만 받는다.
                var auth = manager.authenticator as MatchAuthenticator;
                if (auth != null)
                {
                    string roomId = Read(args, RoomIdPrefix);
                    if (!string.IsNullOrWhiteSpace(roomId)) auth.RoomId = roomId.Trim();

                    string verifyUrl = Read(args, VerifyUrlPrefix);
                    if (!string.IsNullOrWhiteSpace(verifyUrl)) auth.VerifyUrl = verifyUrl.Trim();

                    Debug.Log("[MirrorLaunchArgs] 담당 방=" +
                              (string.IsNullOrEmpty(auth.RoomId) ? "(제한 없음)" : auth.RoomId));
                }

                if (host)
                {
                    // 방장 모드 — 서버이면서 클라이언트다. 평소에는 로비에서
                    // 방장이 "게임 시작" 을 누를 때 이 경로로 들어간다(-263).
                    // 이 인자는 사람 없이 확인할 때 쓴다.
                    //
                    // 방장도 자기 자신에게 붙는 클라이언트다 — 접속 확인을 통과해야
                    // 자기 몸이 생긴다. 그래서 토큰을 여기서도 채운다.
                    ReadClientCredentials(args);

                    manager.StartHost();
                    Debug.Log("[MirrorLaunchArgs] 방장(호스트)으로 시작");
                    return;
                }

                manager.StartServer();
                Debug.Log("[MirrorLaunchArgs] 서버로 시작");
                return;
            }

            string address = Read(args, AddressPrefix);
            if (!string.IsNullOrWhiteSpace(address))
                manager.networkAddress = address.Trim();

            ReadClientCredentials(args);

            manager.StartClient();
            Debug.Log("[MirrorLaunchArgs] 클라이언트로 시작 · 주소=" + manager.networkAddress
                      + " · 방=" + (MatchAuthenticator.ClientRoomId ?? "(없음)")
                      + " · 토큰=" + (string.IsNullOrEmpty(MatchAuthenticator.ClientToken) ? "없음" : "있음"));
        }

        /// <summary>
        /// 접속 확인에 쓸 토큰과 방 번호를 채운다. 평소에는 로비가 채워 주고,
        /// 로비 없이 띄우는 검증용 실행에서만 여기서 읽는다.
        /// 토큰은 실행 인자에 두면 프로세스 목록에 그대로 보이므로 환경변수로 받는다.
        /// </summary>
        private static void ReadClientCredentials(string[] args)
        {
            string token = Environment.GetEnvironmentVariable(TokenEnv);
            if (!string.IsNullOrWhiteSpace(token))
                MatchAuthenticator.ClientToken = token;

            string room = Environment.GetEnvironmentVariable(RoomEnv);
            if (string.IsNullOrWhiteSpace(room)) room = Read(args, RoomIdPrefix);
            if (!string.IsNullOrWhiteSpace(room))
                MatchAuthenticator.ClientRoomId = room.Trim();
        }

        private static void ApplyPort(NetworkManager manager, ushort port)
        {
            var kcp = manager.transport as kcp2k.KcpTransport;
            if (kcp == null)
            {
                Debug.LogWarning("[MirrorLaunchArgs] KCP 전송이 아니라 -port 를 적용하지 못했다.");
                return;
            }

            kcp.port = port;
        }

        private static bool TryReadPort(string[] args, out ushort port)
        {
            port = 0;
            string text = Read(args, PortPrefix);
            return !string.IsNullOrWhiteSpace(text) && ushort.TryParse(text.Trim(), out port) && port != 0;
        }

        private static bool Has(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string Read(string[] args, string prefix)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(prefix.Length);
            return null;
        }
    }
}
