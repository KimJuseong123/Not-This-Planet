using NUnit.Framework;
using Metagame.Network;

namespace Metagame.Network.Tests
{
    /// <summary>
    /// 로비에서 입력한 서버 주소를 접속 주소로 바꾸는 규칙.
    ///
    /// 암호화 여부는 접두어가 있으면 그것을 따르고, 없으면 포트로 정한다
    /// — 포트를 생략했거나 443 이면 암호화, 아니면 평문.
    /// 배포 서버는 도메인만, 로컬 개발은 "127.0.0.1:8080" 을 넣는다.
    /// </summary>
    public sealed class ServerAddressTests
    {
        [Test]
        public void DomainOnlyUsesHttpsAndWss()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints("j15b202.p.ssafy.io", out http, out socket, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(http, Is.EqualTo("https://j15b202.p.ssafy.io:443"));
            Assert.That(socket, Is.EqualTo("wss://j15b202.p.ssafy.io:443/ws/game"));
        }

        [Test]
        public void LocalDevelopmentPortStaysPlain()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints("127.0.0.1:8080", out http, out socket, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(http, Is.EqualTo("http://127.0.0.1:8080"));
            Assert.That(socket, Is.EqualTo("ws://127.0.0.1:8080/ws/game"));
        }

        [Test]
        public void ExplicitPort443IsTreatedAsSecure()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints("j15b202.p.ssafy.io:443", out http, out socket, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(http, Is.EqualTo("https://j15b202.p.ssafy.io:443"));
            Assert.That(socket, Does.StartWith("wss://"));
        }

        [Test]
        public void SchemeWinsOverPortRule()
        {
            string http, socket, error;

            // 접두어가 있으면 포트와 무관하게 그것을 따른다.
            bool ok = NetworkSession.TryBuildEndpoints("https://example.com:8443", out http, out socket, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(http, Is.EqualTo("https://example.com:8443"));
            Assert.That(socket, Is.EqualTo("wss://example.com:8443/ws/game"));
        }

        [Test]
        public void PlainSchemeKeepsPlainEvenWithoutPort()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints("http://192.168.0.15", out http, out socket, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(http, Is.EqualTo("http://192.168.0.15:8080"));
            Assert.That(socket, Is.EqualTo("ws://192.168.0.15:8080/ws/game"));
        }

        [Test]
        public void TrailingPathAndSpacesAreIgnored()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints("  wss://j15b202.p.ssafy.io/ws/game  ", out http, out socket, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(http, Is.EqualTo("https://j15b202.p.ssafy.io:443"));
            Assert.That(socket, Is.EqualTo("wss://j15b202.p.ssafy.io:443/ws/game"));
        }

        [Test]
        public void EmptyAddressFails()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints("   ", out http, out socket, out error);

            Assert.That(ok, Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [TestCase("192.168.0.15:abc")]
        [TestCase("192.168.0.15:0")]
        [TestCase("192.168.0.15:70000")]
        public void InvalidPortFails(string address)
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints(address, out http, out socket, out error);

            Assert.That(ok, Is.False);
            Assert.That(error, Does.Contain("포트"));
        }

        [Test]
        public void UnknownSchemeFails()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints("ftp://example.com", out http, out socket, out error);

            Assert.That(ok, Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void DefaultAddressPointsAtTheDeployedServerOverHttps()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints(
                NetworkSession.DefaultServerAddress, out http, out socket, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(http, Does.StartWith("https://"));
            Assert.That(socket, Does.StartWith("wss://"));
        }
    }
}
