using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 구버전이라 막혔을 때 사용자가 보는 것 (S15P21B202-404).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> 판정은 서버가 한다(<c>ClientVersionGate</c>).
    /// 클라이언트의 몫은 <b>거절을 사람이 읽을 말로 바꾸는 것</b>인데, 그 자리는 조용히
    /// 쓸모없어지기 쉽다 — 본문 형식이 바뀌거나 중간에서 무언가 끼어들면 빈 문자열이
    /// 화면에 뜨고, 사용자는 <b>게임이 고장 난 줄 안다.</b></para>
    ///
    /// <para>그래서 "본문이 이상해도 쓸모 있는 말이 나온다" 를 값으로 고정한다.</para>
    /// </summary>
    public sealed class ClientVersionGateTests
    {
        const string Good =
            "{\"message\":\"게임 버전이 낮아 접속할 수 없습니다. 최소 v1.2.0 이 필요합니다.\"," +
            "\"downloadUrl\":\"https://example.test/play/\",\"minVersion\":\"v1.2.0\"}";

        [Test]
        public void ItShowsWhatIsNeededAndWhereToGetIt()
        {
            string shown = NetworkSession.OutdatedMessage(Good);

            Assert.That(shown, Does.Contain("v1.2.0"), "무엇이 필요한지가 없다");
            Assert.That(shown, Does.Contain("https://example.test/play/"), "받을 곳이 없다");
        }

        /// <summary>
        /// 본문이 비거나 깨져도 **쓸모 있는 말**이 나와야 한다. 여기서 빈 문자열이 나오면
        /// 사용자는 아무 설명 없는 실패 화면을 본다.
        /// </summary>
        [Test]
        public void ABrokenBodyStillSaysSomethingUseful()
        {
            foreach (string body in new[] { null, "", "   ", "not json", "{", "{\"other\":1}" })
            {
                string shown = NetworkSession.OutdatedMessage(body);
                Assert.That(shown, Is.Not.Null.And.Not.Empty, "본문 " + (body ?? "null") + " 에서 빈 말");
                Assert.That(shown, Does.Contain("버전"), "무엇 때문인지가 없다: " + shown);
            }
        }

        /// <summary>
        /// 받을 곳이 없으면 <b>문장만</b> 준다. 예전 방식대로 꼬리를 붙이면 "받을 곳: " 뒤가
        /// 비어 사용자가 링크를 찾다가 만다.
        /// </summary>
        [Test]
        public void NoUrlMeansNoDanglingLabel()
        {
            string shown = NetworkSession.OutdatedMessage(
                "{\"message\":\"버전이 낮습니다.\",\"downloadUrl\":\"\",\"minVersion\":\"v1.2.0\"}");

            Assert.That(shown, Is.EqualTo("버전이 낮습니다."));
            Assert.That(shown, Does.Not.Contain("받을 곳"));
        }

        /// <summary>
        /// <b>426 을 쓴다.</b> 401·403 과 뜻이 달라야 클라이언트가 다시 로그인해 보는
        /// 헛수고를 하지 않는다. 이 수가 바뀌면 거절이 그냥 "로그인 실패" 로 보인다.
        /// </summary>
        [Test]
        public void TheStatusCodeIsUpgradeRequired()
        {
            Assert.That(NetworkSession.OutdatedStatus, Is.EqualTo(426));
        }

        /// <summary>
        /// 스탬프 줄의 머리표는 에디터(<c>BuildStamp</c>)와 런타임(<c>BuildInfo</c>)이
        /// **같은 상수**를 봐야 한다. 예전에 파일 이름을 양쪽에 적어 두고 어긋난 적이 있다.
        /// </summary>
        [Test]
        public void TheVersionLinePrefixIsSharedAndDistinct()
        {
            Assert.That(Metagame.Core.BuildInfo.VersionPrefix, Is.EqualTo("버전="));
            Assert.That(Metagame.Core.BuildInfo.VersionPrefix,
                        Is.Not.EqualTo(Metagame.Core.BuildInfo.BackendPrefix));
            Assert.That(Metagame.Core.BuildInfo.VersionPrefix,
                        Is.Not.EqualTo(Metagame.Core.BuildInfo.ExperimentPrefix));
        }

        /// <summary>
        /// 릴리스 빌드가 아니면 <b>빈 문자열</b>이다. 에디터에서 도는 이 테스트가 그 상태다.
        /// 여기서 억지로 무언가를 채우면 서버가 "모르는 버전" 을 판정할 수 없게 된다.
        /// </summary>
        [Test]
        public void ANonReleaseBuildReportsNoVersion()
        {
            Assert.That(Metagame.Core.BuildInfo.ReleaseVersion, Is.Empty);
        }
    }
}
