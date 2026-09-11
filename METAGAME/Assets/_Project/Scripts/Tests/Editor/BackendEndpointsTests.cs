using Metagame.Core;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 백엔드 주소가 한곳에서 파생된다 (S15P21B202-284).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> 갈려야 하는 주소가 셋인데 서로 다른 파일에
    /// 흩어져 있었고, 그중 <c>MatchAuthenticator.VerifyUrl</c> 은 <c>[SerializeField]</c>
    /// 도 아니라 <b>코드 기본값이 배포 주소로 박혀</b> 있었다. <c>-verifyUrl=</c> 실행
    /// 인자로만 덮이는데 그것은 옛 데디케이티드 서버가 쓰던 것이라 <b>리슨 서버 호스트에는
    /// 오지 않는다</b> — 개발 빌드로 호스트를 하면 참가자 인증을 배포 백엔드에 물었다.</para>
    ///
    /// <para>하나만 어긋나도 조용히 틀리므로, 셋이 같은 base 에서 나오는지 잠근다.</para>
    /// </summary>
    public sealed class BackendEndpointsTests
    {
        [Test]
        public void ThreeUrlsComeFromOneBase()
        {
            var baseUrl = BackendEndpoints.Base;

            Assert.AreEqual(baseUrl, BackendEndpoints.HttpBaseUrl,
                "로그인 주소가 base 와 다르다");
            Assert.That(BackendEndpoints.VerifyUrl, Does.StartWith(baseUrl),
                "인증 확인 주소가 base 에서 나오지 않는다 — 이것이 조용히 배포를 가리켰던 자리다");
            Assert.That(BackendEndpoints.WebSocketUrl, Does.EndWith("/ws/game"),
                "방 주소에 경로가 없다");
        }

        [Test]
        public void TheWebSocketUrlSwitchesTheScheme()
        {
            // 손으로 적어 두면 base 를 바꿀 때 이쪽을 잊는다 — 그것이 이 파일이 생긴 이유다.
            Assert.That(BackendEndpoints.WebSocketUrl, Does.StartWith("wss://"),
                "https base 인데 ws 주소가 wss 가 아니다: " + BackendEndpoints.WebSocketUrl);
            Assert.That(BackendEndpoints.WebSocketUrl, Does.Not.Contain("https://"));
        }

        [Test]
        public void TheDevBaseIsAPathPrefixNotANewPort()
        {
            // 새 포트는 SSAFY 보안그룹이 열어 줘야 하고 우리가 못 정한다. 그래서 경로로
            // 나눈다 — nginx 가 `/dev/` 를 8081 로 넘긴다.
            Assert.That(BackendEndpoints.DevBase, Does.StartWith(BackendEndpoints.ProdBase),
                "개발 base 가 배포 base 의 경로 접두가 아니다 — 포트를 새로 열려 하고 있다");
            Assert.That(BackendEndpoints.DevBase, Does.EndWith("/dev"));
        }

        [Test]
        public void NoBaseEndsWithASlash()
        {
            // 붙이는 쪽이 `base + "/auth/guest"` 로 조립한다. 끝에 슬래시가 있으면
            // `//auth/guest` 가 되고, 서버가 404 를 준다.
            Assert.That(BackendEndpoints.ProdBase, Does.Not.EndWith("/"));
            Assert.That(BackendEndpoints.DevBase, Does.Not.EndWith("/"));
            Assert.That(BackendEndpoints.Base, Does.Not.EndWith("/"));
        }

        [Test]
        public void TheEditorPointsAtDevelopment()
        {
            // 개발자가 로컬에서 돌리는 것이 실유저 데이터를 건드리면 안 된다 —
            // 서버를 나누는 목적이 그것이다. 이 검증은 에디터에서만 뜻이 있다.
            Assert.AreEqual(BackendEndpoints.DevBase, BackendEndpoints.Base,
                "에디터가 배포 서버를 가리킨다");
            Assert.IsTrue(BackendEndpoints.IsDev);
        }

        [Test]
        public void TheAuthenticatorTakesItsDefaultFromTheSharedBase()
        {
            // **소스로 확인한다.** 이 테스트 어셈블리는 Mirror 를 참조하지 않아
            // MatchAuthenticator(NetworkAuthenticator 상속)를 만들 수 없다. 참조를 더하면
            // 네트워크 어셈블리가 편집 검증에 딸려 들어오므로 그쪽을 택하지 않았다.
            var code = TestSource.Read("Runtime/Network/MatchAuthenticator.cs");

            Assert.That(code, Does.Contain("VerifyUrl { get; set; } = BackendEndpoints.VerifyUrl"),
                "인증기의 기본 주소가 공용 base 에서 나오지 않는다 — 이것이 조용히 배포를 "
                + "가리켰던 자리다");
        }

        [Test]
        public void TheStampCarriesTheChoice()
        {
            // 빌드가 `백엔드=dev|prod` 를 스탬프에 쓴다. 그 줄이 없으면 `BackendEndpoints`
            // 가 "옛 빌드" 로 보고 배포를 가리킨다 — 개발 빌드가 실유저 서버에 붙는 길이다.
            Assert.AreEqual("백엔드=", BuildInfo.BackendPrefix);

            var stamp = TestSource.Read("Editor/BuildStamp.cs");
            Assert.That(stamp, Does.Contain("BuildInfo.BackendPrefix"),
                "BuildStamp 가 백엔드 줄을 쓰지 않는다 — 모든 빌드가 배포를 가리킨다");
            Assert.That(stamp, Does.Contain("SPACE_DEV"),
                "BuildStamp 가 dev 여부를 보지 않는다");

            // SpaceBuild 는 인자로 켠 dev 를 환경변수로 되돌려 박아야 한다 —
            // BuildPlayerProcessor 는 지역변수를 볼 수 없다.
            var build = TestSource.Read("Editor/SpaceBuild.cs");
            Assert.That(build, Does.Contain("SetEnvironmentVariable(\"SPACE_DEV\""),
                "BuildWindowsDev 의 dev 결정이 BuildStamp 에 전달되지 않는다");
        }

        [Test]
        public void TheBuildRestoresTheEnvironmentVariable()
        {
            // 박아 놓기만 하면 그 값이 **에디터 프로세스에 남는다.** 같은 세션에서
            // 이어서 Build Settings 버튼처럼 SpaceBuild 를 거치지 않는 빌드를 하면
            // 그 값이 그대로 쓰여 배포용 빌드가 `백엔드=dev` 로 찍힌다(반대도 성립).
            // 배치모드는 프로세스가 곧 끝나 뜻이 없지만 사람이 쓰는 세션은 하루를
            // 넘긴다. `!358` AI 리뷰가 잡았다.
            var build = TestSource.Read("Editor/SpaceBuild.cs");

            Assert.That(build, Does.Contain("finally"),
                "SpaceBuild 가 SPACE_DEV 를 되돌리지 않는다 — 에디터 세션에 값이 남는다");
            Assert.That(build, Does.Contain("SetEnvironmentVariable(\"SPACE_DEV\", saved)"),
                "되돌리는 줄이 없다 — 원래 값을 기억해 두고 finally 에서 돌려놔야 한다");
        }

        [Test]
        public void AStampLessDevBuildFallsBackToDevelopment()
        {
            // 스탬프에 `백엔드=` 가 없을 때 **prod 로 폴백하면 이 파일이 막으려던
            // 실패 방식이 그대로 재현된다** — 개발 빌드가 실유저 서버에 붙는 것.
            // 그래서 빌드 종류로 가른다(`!358` AI 리뷰).
            var code = TestSource.Read("Runtime/Core/BackendEndpoints.cs");

            Assert.That(code, Does.Contain("Debug.isDebugBuild"),
                "스탬프가 빈 경우를 빌드 종류로 가르지 않는다 — 개발 빌드가 "
                + "실유저 서버로 폴백한다");
            Assert.That(code, Does.Contain("devBuild ? DevBase : ProdBase"),
                "폴백이 빌드 종류를 따르지 않는다");
        }

        [Test]
        public void TheOldHardcodedAddressesAreGone()
        {
            // 흩어진 기본값이 남아 있으면 한 군데를 고쳐도 다른 곳이 배포를 가리킨다.
            foreach (var path in new[] { "Runtime/Bootstrap/LobbyBootstrap.cs",
                                         "Runtime/Network/MatchAuthenticator.cs" })
            {
                var code = TestSource.Method(TestSource.Read(path), "class ");
                Assert.That(code, Does.Not.Contain("j15b202.p.ssafy.io"),
                    path + " 에 주소가 박혀 있다 — BackendEndpoints 에서 받아야 한다");
            }
        }
    }
}
