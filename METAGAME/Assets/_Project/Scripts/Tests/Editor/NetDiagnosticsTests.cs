using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 멀티 QA 의 로그가 <b>빠짐없이·읽을 수 있게</b> 남는지 (S15P21B202-658).
    ///
    /// <para>이 진단은 두 사람이 각자 창을 띄워 논 뒤 <c>scripts/ci/netdiag.py</c> 로
    /// 대조하려고 있는 것이다. 그래서 망가지는 길이 둘이다 —
    /// <b>한쪽이 안 켜져서 짝지을 줄이 없거나</b>, <b>늘 다르다고 나와서 아무도 안 읽거나</b>.
    /// 둘 다 "조용히" 일어나고, 알아챌 때는 이미 다 놀고 난 뒤다.</para>
    /// </summary>
    public sealed class NetDiagnosticsTests
    {
        [Test]
        public void TheFlagStillTurnsItOn()
        {
            // 회귀 검사. 고치는 것은 "빌드가 스스로 켠다" 이고, 손으로 켜는 길은 그대로여야 한다.
            Assert.IsTrue(NetDiagnostics.EnabledFor(new[] { "game.exe", "-netdiag" }, null, ""));
            Assert.IsTrue(NetDiagnostics.EnabledFor(new[] { "game.exe", "-NETDIAG" }, null, ""),
                "대소문자를 가린다 — 손으로 치는 인자다");
            Assert.IsTrue(NetDiagnostics.EnabledFor(new[] { "game.exe" }, "1", ""));
        }

        [Test]
        public void BuildsWeTestWithTurnThemselvesOn()
        {
            // **켜는 일을 사람 기억에 맡기지 않는다.** 두 사람이 각자 `-netdiag` 를 붙여야
            // 하는데 한 쪽만 잊어도 짝지을 줄이 없어 판 하나가 통째로 버려진다.
            Assert.IsTrue(NetDiagnostics.EnabledFor(new[] { "game.exe" }, null, "qa-561-1a2b3c4"),
                "QA 빌드가 스스로 켜지지 않는다 — 인자를 잊으면 그 판의 로그가 없다");

            // **`dev-` 도 켠다** (S15P21B202-711). `qa-` 만 켜던 규칙은 "QA 는 MR 버튼
            // 빌드로 한다" 를 가정했는데 실제로는 `dev-` 로도 한다. 2026-09-22 에 그
            // 판의 성능 자료가 통째로 없었고, 2026-09-23 에는 MR 이 다 머지돼
            // `qa-` 를 만들 길조차 없었다.
            Assert.IsTrue(NetDiagnostics.EnabledFor(new[] { "game.exe" }, null, "dev-20260921-1a2b3c4"),
                "개발 빌드가 스스로 켜지지 않는다 — 이 빌드로도 QA 한다");
        }

        [Test]
        public void EverydayBuildsStayOff()
        {
            // 릴리스는 그대로 꺼져 있어야 한다 — 실유저 기계에서 매초 한 줄씩 남길
            // 이유가 없다. 손으로 만든 빌드(신원 없음)도 켜지 않는다: 켜는 쪽은
            // **이름이 붙은 개발 빌드**뿐이다 (S15P21B202-711).
            Assert.IsFalse(NetDiagnostics.EnabledFor(new[] { "game.exe" }, null, "v1.2.0"),
                "릴리스 빌드에서 진단이 켜진다");
            Assert.IsFalse(NetDiagnostics.EnabledFor(new[] { "game.exe" }, null, "development-x"),
                "`dev-` 로 시작하는 것처럼 보이는 다른 신원까지 켠다");
            Assert.IsFalse(NetDiagnostics.EnabledFor(new[] { "game.exe" }, null, ""),
                "신원이 없는 빌드(손으로 만든 로컬)에서 켜진다");
            Assert.IsFalse(NetDiagnostics.EnabledFor(new[] { "game.exe" }, "0", ""),
                "환경변수 0 을 켜짐으로 읽는다");
        }

        [Test]
        public void NothingAtAllIsNotACrash()
        {
            // `GetCommandLineArgs` 는 늘 값을 주지만, 순수 함수는 밖에서 무엇이 와도
            // 견뎌야 한다 — 검사에서 null 을 넣는 것이 곧 그 확인이다.
            Assert.IsFalse(NetDiagnostics.EnabledFor(null, null, null));
        }

        [Test]
        public void TheBuildIdIsReadNotGuessed()
        {
            // 빌드 신원은 스탬프에서 온다. 여기서 다시 판단하면 두 곳이 각각 판단하는
            // 것이 되어 어긋난다(`BuildStamp` 가 같은 이유로 `SPACE_DEV` 를 되읽는다).
            var src = TestSource.Read("Runtime/Network/NetDiagnostics.cs");

            Assert.That(src, Does.Contain("Core.BuildInfo.BuildId"),
                "빌드 신원을 스탬프에서 읽지 않는다");
            Assert.That(src, Does.Contain("QaBuildPrefix"),
                "QA 빌드의 머리표가 상수로 없다 — 문자열이 흩어지면 한쪽만 바뀐다");
        }

        [Test]
        public void ScarsAreCountedOnBothSidesSoTheSignalMeansSomething()
        {
            // **참가자에게는 진짜 흔적이 없다** — 그림만 있다(`ScarGhostCount`).
            // `Scars` 만 찍으면 호스트 5 대 참가자 0 이라 `netdiag.py` 의 "다르다" 가
            // 매 초 참이 된다. 늘 빨간 신호는 아무도 안 읽고, 그러면 **진짜로 갈렸을
            // 때도 못 본다** — 바로 위 `live` 줄이 같은 이유로 이미 합쳐 찍고 있었다.
            var src = TestSource.Read("Runtime/Network/NetDiagnostics.cs");

            Assert.That(src, Does.Contain("spawner.Scars.Count + spawner.ScarGhostCount"),
                "흔적을 한쪽 것만 찍는다 — 대조가 매 초 거짓 빨강을 낸다");
            Assert.That(src, Does.Contain("spawner.Live.Count + spawner.Ghosts.Count"),
                "낙하 운석의 합산이 사라졌다 — 같은 거짓 빨강이 돌아온다");
        }
    }
}
