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
    ///   MetaGame.exe -batchmode -nographics -server
    ///   MetaGame.exe -client -address=127.0.0.1
    ///   MetaGame.exe -server -port=27777
    ///
    /// 인자가 없으면 아무것도 하지 않는다. 평소 실행(로비에서 시작)에는 영향이 없다.
    /// </summary>
    public static class MirrorLaunchArgs
    {
        private const string ServerFlag = "-server";
        private const string ClientFlag = "-client";
        private const string AddressPrefix = "-address=";
        private const string PortPrefix = "-port=";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Apply()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool server = Has(args, ServerFlag);
            bool client = Has(args, ClientFlag);
            if (!server && !client) return;

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[MirrorLaunchArgs] NetworkManager 가 없다. 로비 씬으로 시작했는지 확인할 것.");
                return;
            }

            // 이 콜백은 NetworkManager.Start() 보다 먼저 돈다. 그대로 두면 여기서 클라이언트로 띄운 뒤
            // NetworkManager 가 헤드리스 자동 시작으로 서버까지 켜 버린다. 인자를 준 실행에서는
            // 자동 시작을 끄고 여기서만 정한다 — 인자가 없으면 자동 시작이 그대로 동작한다.
            manager.headlessStartMode = HeadlessStartOptions.DoNothing;

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
                manager.StartServer();
                Debug.Log("[MirrorLaunchArgs] 서버로 시작");
                return;
            }

            string address = Read(args, AddressPrefix);
            if (!string.IsNullOrWhiteSpace(address))
                manager.networkAddress = address.Trim();

            manager.StartClient();
            Debug.Log("[MirrorLaunchArgs] 클라이언트로 시작 · 주소=" + manager.networkAddress);
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
