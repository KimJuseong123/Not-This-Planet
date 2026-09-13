using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 백엔드 주소를 <b>한곳에서</b> 정한다 (S15P21B202-284).
    ///
    /// <para><b>왜 모으나.</b> 갈려야 하는 주소가 셋인데 서로 다른 파일에 흩어져 있었다 —
    /// <c>LobbyBootstrap.httpBaseUrl</c>(로그인·로비) · <c>LobbyBootstrap.webSocketUrl</c>(방) ·
    /// <c>MatchAuthenticator.VerifyUrl</c>(호스트가 참가자 토큰을 되물어보는 곳). 앞의 둘은
    /// <c>[SerializeField]</c> 라 눈에 보이는데 <b>셋째는 코드 기본값이 prod 주소로 박혀
    /// 있고</b> <c>-verifyUrl=</c> 실행 인자로만 덮인다. 그 인자는 옛 데디케이티드 서버가
    /// 쓰던 것이라 <b>리슨 서버 호스트에는 오지 않는다</b> — 즉 개발 빌드로 호스트를 하면
    /// 참가자 인증을 <b>prod 백엔드에 묻는다.</b> `-284` 가 경고한 자리가 그것이다.</para>
    ///
    /// <para>그래서 셋을 하나의 <see cref="Base"/> 에서 파생시킨다. 한 군데를 고치면 셋이
    /// 같이 움직이고, 하나만 어긋나는 상태가 만들어지지 않는다.</para>
    ///
    /// <para><b>어디서 정해지나.</b> 빌드할 때 <c>BuildStamp</c> 가 <c>build-stamp.txt</c> 의
    /// 둘째 줄부터에 <c>백엔드=dev</c> 또는 <c>백엔드=prod</c> 를 써 넣고, 런타임에
    /// <see cref="BuildInfo.BackendEnvironment"/> 가 그것을 읽는다. 실험 라벨(<c>실험=</c>)이
    /// 쓰는 것과 같은 통로다 — 새 장치를 만들지 않았다.</para>
    /// </summary>
    public static class BackendEndpoints
    {
        /// <summary>배포 서버. nginx 가 <c>/</c> 를 8080 으로 넘긴다.</summary>
        public const string ProdBase = "https://j15b202.p.ssafy.io:443";

        /// <summary>
        /// 개발 서버. nginx 가 <c>/dev/</c> 를 8081 로 넘긴다 (S15P21B202-284).
        ///
        /// <para><b>경로 접두다.</b> 새 포트는 SSAFY 보안그룹이 열어 줘야 하고 우리가 못
        /// 정한다. 클라이언트가 base 에 경로를 이어 붙이므로 접두로 나눌 수 있다.</para>
        /// </summary>
        public const string DevBase = "https://j15b202.p.ssafy.io:443/dev";

        /// <summary>
        /// 실행 인자로 덮어쓸 수 있다 — <c>NotThisPlanet.exe -backend=https://…</c>.
        ///
        /// <para>QA 에서 임시로 다른 서버를 가리켜야 할 때 빌드를 다시 만들지 않아도 된다.
        /// <c>MirrorLaunchArgs</c> 가 <c>-verifyUrl=</c> 로 같은 일을 하고 있고, 그 결을 따른다.</para>
        /// </summary>
        public const string ArgPrefix = "-backend=";

        static string resolved;

        /// <summary>
        /// 이 실행이 쓸 백엔드 base. 끝에 <c>/</c> 를 두지 않는다 — 붙이는 쪽이
        /// <c>base + "/auth/guest"</c> 로 조립한다.
        /// </summary>
        public static string Base
        {
            get
            {
                if (resolved != null) return resolved;
                resolved = Resolve();
                Debug.Log("[백엔드] " + resolved + (IsDev ? "  (개발)" : "  (배포)"));
                return resolved;
            }
        }

        /// <summary>개발 서버를 가리키고 있는가. 화면에 표시하거나 로그를 가를 때 쓴다.</summary>
        public static bool IsDev => Base.EndsWith("/dev");

        public static string HttpBaseUrl => Base;

        /// <summary>
        /// 방 WebSocket. <c>https</c> → <c>wss</c> 로 바꾸고 경로를 잇는다.
        ///
        /// <para>손으로 적어 두면 base 를 바꿀 때 이쪽을 잊는다 — 그것이 이 파일이 생긴
        /// 이유이므로 여기서도 파생시킨다.</para>
        /// </summary>
        public static string WebSocketUrl =>
            Base.Replace("https://", "wss://").Replace("http://", "ws://") + "/ws/game";

        /// <summary>호스트가 참가자 토큰을 되물어보는 곳.</summary>
        public static string VerifyUrl => Base + "/auth/verify";

        static string Resolve()
        {
            // ① 실행 인자가 가장 세다. QA 가 빌드를 다시 만들지 않아도 되게.
            string fromArgs = ReadArg();
            if (!string.IsNullOrEmpty(fromArgs)) return fromArgs.TrimEnd('/');

            // ② 에디터는 **개발**을 가리킨다. 개발자가 로컬에서 돌리는 것이 실유저
            //    데이터를 건드리면 안 된다 — 나누는 목적이 그것이다.
            if (Application.isEditor) return DevBase;

            // ③ 빌드는 스탬프가 정한다.
            string env = (BuildInfo.BackendEnvironment ?? string.Empty).Trim();
            if (env == "dev") return DevBase;
            if (env == "prod") return ProdBase;

            // ④ 스탬프에 값이 없다 — 이 표시가 없던 시절 빌드다. 어느 쪽으로 볼지를
            //    **빌드 종류로 가른다**(`!358` AI 리뷰).
            //
            //    개발 빌드(`BuildOptions.Development`)에서 이 자리에 오는 것은
            //    `BuildStamp` 가 값을 쓰지 못한 경우뿐이고, 그때 prod 로 폴백하면
            //    **이 파일이 막으려던 실패 방식이 그대로 재현된다** — 개발 빌드가
            //    실유저 서버에 붙는 것. 그래서 개발 빌드는 개발로 본다.
            //
            //    배포 빌드는 prod 로 본다. 표시가 없던 시절 빌드는 전부 prod 를
            //    가리켰으므로 그것이 사실에 맞다.
            //
            //    어느 쪽이든 **조용히 넘기지 않는다** — 스탬프가 비는 것 자체가 고칠
            //    거리이고, 로그에 남아야 나중에 가릴 수 있다.
            bool devBuild = Debug.isDebugBuild;
            Debug.LogWarning("[백엔드] 스탬프에 `백엔드=` 가 없다 — "
                             + (devBuild ? "개발 빌드라 개발 서버로 본다" : "배포 서버로 본다")
                             + ". BuildStamp 가 값을 쓰지 못한 것이다");
            return devBuild ? DevBase : ProdBase;
        }

        static string ReadArg()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (args == null) return null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a != null && a.StartsWith(ArgPrefix))
                    return a.Substring(ArgPrefix.Length).Trim();
            }

            return null;
        }
    }
}
