using Metagame.EditorTools;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 쌓인 이벤트가 <b>어느 빌드에서 나왔는지</b> 말하는지 (S15P21B202-597).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> 이벤트 머리에 <c>build</c> 가 있는데, 그 값이
    /// <see cref="Metagame.Core.BuildInfo.ReleaseVersion"/> 이었다. 그 값은 <b>서버
    /// 게이트용</b>이라 <c>vX.Y.Z</c> 여야 하고, 그래서 개발·QA 빌드에는 일부러 없다
    /// (<c>-404</c>). 그런데 <b>QA 는 개발 빌드로 한다</b> — 결과적으로 쌓아 둔 것
    /// 대부분이 <c>build: ""</c> 였다. 두 판의 수치가 달라도 <b>다른 빌드였는지 같은
    /// 빌드였는지 되짚을 수 없다.</b></para>
    ///
    /// <para>그래서 줄을 하나 더 둔다: <c>버전=</c> 은 게이트가 읽고(그대로),
    /// <c>빌드=</c> 는 이벤트가 읽는다. <b>한 줄이 두 일을 겸하려 한 것</b>이
    /// 원인이었으므로, 이 검사는 그 둘이 다시 붙지 않는지를 본다.</para>
    /// </summary>
    public sealed class BuildIdentityTests
    {
        /// <summary>
        /// 머리표는 에디터(<c>BuildStamp</c>)와 런타임(<c>BuildInfo</c>)이 <b>같은 상수</b>를
        /// 봐야 한다. 그리고 다른 줄과 겹치면 파싱이 엉뚱한 값을 담는다.
        /// </summary>
        [Test]
        public void TheIdentityPrefixIsSharedAndDistinct()
        {
            Assert.That(Metagame.Core.BuildInfo.BuildIdPrefix, Is.EqualTo("빌드="));

            foreach (string other in new[] { Metagame.Core.BuildInfo.VersionPrefix,
                                             Metagame.Core.BuildInfo.BackendPrefix,
                                             Metagame.Core.BuildInfo.ExperimentPrefix })
            {
                Assert.That(Metagame.Core.BuildInfo.BuildIdPrefix, Is.Not.EqualTo(other),
                            "머리표가 겹친다 — 한 줄이 두 값으로 읽힌다");
            }
        }

        /// <summary>
        /// 파이프라인이 준 이름을 그대로 적는다. 이 값이 배치된 파일 이름
        /// (<c>dev-versions.json</c> 의 항목)과 <b>같은 문자열</b>이어야 "이 판은 그 빌드"
        /// 를 이을 수 있다.
        /// </summary>
        [Test]
        public void TheIdentityIsWhatThePipelineNamedTheBuild()
        {
            Assert.That(BuildStamp.IdentityOf("qa-498-5275441c", ""), Is.EqualTo("qa-498-5275441c"));
            Assert.That(BuildStamp.IdentityOf("dev-20260917-9ae7220d", ""), Is.EqualTo("dev-20260917-9ae7220d"));
        }

        /// <summary>
        /// 릴리스에서는 <c>SPACE_BUILD_ID</c> 를 따로 주지 않는다 — <c>버전=</c> 과 같은
        /// 값이 들어가야 한다. <b>같은 빌드를 두 이름으로 부르면</b> 집계할 때 둘을 다른
        /// 빌드로 세고, 그 잘못은 조용하다.
        /// </summary>
        [Test]
        public void AReleaseBuildUsesItsVersionAsTheIdentity()
        {
            Assert.That(BuildStamp.IdentityOf("", "v1.1.0"), Is.EqualTo("v1.1.0"));
            Assert.That(BuildStamp.IdentityOf(null, "v1.1.0"), Is.EqualTo("v1.1.0"));

            // 주어진 것이 이기는 쪽이어야 한다 — 파이프라인이 둘을 다 줬다면 신원을
            // 적으려고 준 것이다.
            Assert.That(BuildStamp.IdentityOf("qa-498-5275441c", "v1.1.0"), Is.EqualTo("qa-498-5275441c"));
        }

        /// <summary>
        /// <b>둘 다 없으면 빈 문자열</b>이고, 그러면 <c>빌드=</c> 줄이 아예 없다 —
        /// 손으로 만든 로컬 빌드가 그렇다. 여기서 <c>unknown</c> 같은 것을 채우면 정말
        /// 모르는 빌드와 이름이 없는 빌드를 구분할 수 없게 된다.
        /// </summary>
        [Test]
        public void NoNameMeansNoLineRatherThanAMadeUpOne()
        {
            Assert.That(BuildStamp.IdentityOf("", ""), Is.Empty);
            Assert.That(BuildStamp.IdentityOf(null, null), Is.Empty);
            Assert.That(BuildStamp.IdentityOf("   ", "  "), Is.Empty);
        }

        /// <summary>
        /// 줄바꿈이 섞이면 <b>둘째 줄부터의 <c>머리표=값</c> 규칙이 깨진다</b> — 뒤쪽 줄이
        /// 통째로 다른 값으로 읽히거나 사라진다. 환경변수는 사람이 손으로 넣을 수 있으므로
        /// 들어올 수 있는 값이다.
        /// </summary>
        [Test]
        public void ANewlineCannotSplitTheStampIntoAnotherLine()
        {
            Assert.That(BuildStamp.IdentityOf("qa-1\n백엔드=prod", ""), Does.Not.Contain("\n"));
            Assert.That(BuildStamp.IdentityOf("qa-1\r\nqa-2", ""), Does.Not.Contain("\r"));

            // 치환이 남긴 꼬리 공백까지 다듬는다 — 스탬프 파일에 쓸 이유가 없다.
            Assert.That(BuildStamp.IdentityOf("qa-1\n", ""), Is.EqualTo("qa-1"));
            Assert.That(BuildStamp.IdentityOf("\nqa-1", ""), Is.EqualTo("qa-1"));

            // **이상한 값을 버리지는 않는다.** 중간에 공백이 섞인 이름은 이상한 이름이지
            // 없는 이름이 아니다 — 빈 값으로 떨어뜨리면 그 판이 어느 빌드였는지 아예
            // 사라진다. 이상한 값은 눈에 보이고, 없는 값은 안 보인다.
            Assert.That(BuildStamp.IdentityOf("qa 1", ""), Is.EqualTo("qa 1"));
        }

        /// <summary>
        /// 스탬프가 <b>릴리스가 아닌 빌드에도</b> 신원을 적는지. 이 <c>append</c> 가
        /// <c>버전=</c> 의 <c>if</c> 안으로 들어가면 릴리스 빌드에만 붙어 — 즉 고치려던
        /// 상태로 그대로 돌아간다. 값으로는 잡히지 않는 배선이라 여기서 본다.
        /// </summary>
        [Test]
        public void TheStampWritesTheIdentityOutsideTheReleaseOnlyBranch()
        {
            var src = TestSource.Read("Editor/BuildStamp.cs");
            var compose = TestSource.Method(src, "static string Compose()");

            Assert.That(compose, Does.Contain("BuildIdPrefix"),
                        "스탬프가 신원 줄을 적지 않는다 — 이벤트의 build 가 다시 빈칸이 된다");
            Assert.That(compose, Does.Contain("IdentityOf"),
                        "신원 판정이 Compose 안으로 되돌아왔다 — 검사가 그 판단을 못 본다");

            // `버전=` 을 적는 블록 **안**에 있으면 릴리스 전용이 된다.
            var releaseOnly = TestSource.Method(compose, "if (!string.IsNullOrEmpty(version))");
            Assert.That(releaseOnly, Does.Not.Contain("BuildIdPrefix"),
                        "신원 줄이 릴리스 버전이 있을 때만 붙는다 — 개발 빌드가 다시 이름을 잃는다");
        }

        /// <summary>
        /// 이벤트가 <b>신원</b>을 싣는지. 여기가 이 이슈의 완료 조건이다.
        ///
        /// <para><b>이름을 모르면 그 칸을 안 적는다</b>(<c>!505</c> AI 리뷰). 빈 값으로
        /// 보내면 <b>이름이 없는 빌드</b>와 <b>이 줄이 생기기 전 빌드</b>가 데이터에서
        /// 구분되지 않는다 — 바로 이 이슈가 고치려던 상태가 한 칸 옮겨 남는 것이다.
        /// 같은 블록의 <c>humans</c> 가 이미 같은 규칙을 쓴다.</para>
        /// </summary>
        [Test]
        public void EventsCarryTheIdentityNotTheGateVersion()
        {
            var src = TestSource.Read("Runtime/Bootstrap/SpaceBootstrap.cs");
            var awake = TestSource.Method(src, "void Awake()");

            Assert.That(awake, Does.Contain("BuildInfo.BuildId"),
                        "이벤트가 빌드 신원을 싣지 않는다");
            Assert.That(awake, Does.Not.Contain("BuildInfo.ReleaseVersion"),
                        "이벤트가 다시 게이트용 버전을 싣는다 — 개발 빌드에서는 빈칸이다");

            // 이름이 없을 때 칸을 **안 적는** 배선. 이것이 없으면 `build: ""` 가 나간다.
            Assert.That(awake, Does.Contain("IsNullOrEmpty(Core.BuildInfo.BuildId)"),
                        "이름이 없는 빌드도 build 칸을 적는다 — 빈 값과 '모른다' 가 섞인다");

            // 그 칸이 **딕셔너리 초기화 안에** 있으면 조건 없이 늘 적힌다.
            var head = TestSource.Method(awake, "var head = new System.Collections.Generic.Dictionary<string, object>");
            Assert.That(head, Does.Not.Contain("BuildInfo.BuildId"),
                        "build 칸이 조건 밖으로 나왔다 — 이름이 없어도 빈 값이 나간다");
        }

        /// <summary>
        /// <b>게이트가 읽는 값은 그대로다</b> (<c>-404</c> 회귀 검사). 여기를 신원으로
        /// 바꾸면 개발 빌드가 <c>qa-498-…</c> 를 버전이라고 보내고, 서버의 버전 비교는
        /// 그것을 판정할 수 없다 — 게이트가 조용히 뜻을 잃는다.
        /// </summary>
        [Test]
        public void TheVersionGateStillSendsTheReleaseVersion()
        {
            var src = TestSource.Read("Runtime/Network/NetworkSession.cs");

            Assert.That(src, Does.Contain("clientVersion = Metagame.Core.BuildInfo.ReleaseVersion"),
                        "로그인이 보내는 버전이 바뀌었다 — 서버 게이트의 판정 기준이다");
            Assert.That(src, Does.Not.Contain("clientVersion = Metagame.Core.BuildInfo.BuildId"),
                        "게이트에 신원을 보낸다 — 개발 빌드 이름은 vX.Y.Z 가 아니라 판정되지 않는다");
        }

        /// <summary>
        /// 런로그 헤더도 같은 값을 쓴다. 전에는 <c>버전=</c> 이 비면 사람이 읽는 스탬프를
        /// 걸러 넣었는데, 그것은 한글이라 <c>?</c> 토막이나 <c>unknown</c> 이 됐다 —
        /// <b>QA 로그가 정작 어느 빌드의 것인지 못 말했다.</b>
        /// </summary>
        [Test]
        public void TheRunLogHeaderNamesTheBuildToo()
        {
            // 값 쪽: 신원이 있으면 그것이 헤더에 간다(ASCII 라 걸러지지도 않는다).
            Assert.That(Metagame.Core.RunLogUpload.BuildTag("qa-498-5275441c", "2026-09-17 13:02 · 작업트리"),
                        Is.EqualTo("qa-498-5275441c"));

            // 배선 쪽: 인자 없는 쪽이 신원을 먼저 본다. 에디터에서는 신원이 비어 있어
            // 값으로는 이 경로를 지나갈 수 없다.
            var src = TestSource.Read("Runtime/Core/RunLogUpload.cs");
            var pick = TestSource.Method(src, "public static string BuildTag()");

            Assert.That(pick, Does.Contain("BuildInfo.BuildId"),
                        "헤더가 신원을 보지 않는다 — 개발 빌드 로그가 unknown 으로 올라간다");
        }

        /// <summary>
        /// 에디터에서는 스탬프 파일이 없으므로 <b>빈 문자열</b>이다. 여기서 무언가를
        /// 채우면 읽는 쪽이 "모른다" 를 알 수 없게 된다.
        /// </summary>
        [Test]
        public void TheEditorReportsNoIdentity()
        {
            Assert.That(Metagame.Core.BuildInfo.BuildId, Is.Empty);
        }
    }
}
