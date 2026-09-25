using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 사용자가 받는 <b>릴리스 빌드에서도</b> 추가팩을 게임 안에서 받을 수 있는가
    /// (S15P21B202-709).
    /// </summary>
    public sealed class AiReleasePathTests
    {
        /// <summary>
        /// 받는 길에 <b>개발 빌드 전용 조건이 없다</b> (S15P21B202-709).
        ///
        /// <para><b>왜 값으로 볼 수 없나.</b> <c>DEVELOPMENT_BUILD</c> 는 컴파일 기호다 —
        /// 에디터에서 도는 이 검사에서는 <b>개발 빌드 쪽만 켜져 있어</b> 릴리스에서 무엇이
        /// 빠지는지 실행으로는 볼 수 없다. 글자로 봐야 한다.</para>
        ///
        /// <para><b>무엇이 걸리나.</b> 시험용 기능을 개발 빌드로 가두는 것은 옳고, 실제로
        /// <c>AiHostSettings.TestModeAvailable</c> 이 그렇게 되어 있다. 위험한 것은 그 습관이
        /// <b>받는 길까지 옮겨붙는 것</b>이다 — 그러면 QA 에서는 되고 사용자 빌드에서만 안
        /// 되는데, 그 차이는 <b>내보내기 전까지 아무도 모른다.</b></para>
        /// </summary>
        [Test]
        public void NothingGatesThePackDownloadOnADevelopmentBuild()
        {
            // 이 검사가 컴파일 기호를 실제로 볼 수 있다는 것부터 고정한다. 못 보면 아래
            // 단정은 전부 헛돈다 — 늘 통과하는 검사가 된다.
            Assert.That(TestSource.Read("Runtime/Network/AiHostPolicy.cs"), Does.Contain("DEVELOPMENT_BUILD"),
                "개발 전용 게이트(TestModeAvailable)를 못 찾았다 — 이 검사가 기호를 보지 못한다는 뜻이고, "
                + "그러면 아래 단정이 모두 헛돈다");

            foreach (string file in new[]
            {
                "Runtime/Network/AiPackInstaller.cs",   // 받기·검증·설치
                "Runtime/Network/AiPackLatest.cs",      // 서버의 최신 팩 찾기 (S15P21B202-744)
                "Runtime/Network/AiPrepProgress.cs",    // 진행률·남은 시간·화면 글자
                "Runtime/UI/AiRoomControls.cs",         // 방장이 누르는 버튼
                "Runtime/UI/LobbyRoster.cs",            // 방 목록 — AI 추가·빼기 (S15P21B202-713)
                "Runtime/UI/AiAddFlow.cs",              // 추가 → 설치 → 준비 → 완료의 흐름
                "Runtime/UI/AiInstallPopup.cs",         // 설치 창 — 받기를 시작하는 유일한 화면
            })
                Assert.That(TestSource.Read(file), Does.Not.Contain("DEVELOPMENT_BUILD"),
                    file + " 가 개발 빌드에서만 도는 길을 갖고 있다 — QA 에서는 되고 사용자 "
                    + "빌드에서만 추가팩을 받을 수 없게 된다");

            var boot = TestSource.Method(
                TestSource.Read("Runtime/Network/AiLocalRuntime.cs"), "static void Boot()");
            Assert.That(boot, Does.Not.Contain("DEVELOPMENT_BUILD"),
                "실행기 자체가 개발 빌드에서만 뜬다 — 릴리스에는 받을 주체가 없다");
            Assert.That(boot, Does.Contain("UNITY_STANDALONE_WIN"),
                "Windows 조건이 사라졌다 — 이 추가팩은 Windows 전용이다");
        }
    }
}
