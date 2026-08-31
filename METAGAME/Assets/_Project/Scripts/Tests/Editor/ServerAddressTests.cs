using NUnit.Framework;
using Metagame.Network;

namespace Metagame.Network.Tests
{
    /// <summary>
    /// 로비에서 입력한 서버 주소를 접속 주소로 바꾸는 규칙.
    /// 서버 배포 전 LAN 테스트에서 방장 PC를 직접 가리켜야 하므로 입력 형태가 느슨하다.
    /// </summary>
    public sealed class ServerAddressTests
    {
        [Test]
        public void HostWithPortBuildsBothEndpoints()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints("192.168.0.15:8080", out http, out socket, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(http, Is.EqualTo("http://192.168.0.15:8080"));
            Assert.That(socket, Is.EqualTo("ws://192.168.0.15:8080/ws/game"));
        }

        [Test]
        public void MissingPortFallsBackToDefault()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints("192.168.0.15", out http, out socket, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(http, Is.EqualTo("http://192.168.0.15:8080"));
            Assert.That(socket, Is.EqualTo("ws://192.168.0.15:8080/ws/game"));
        }

        [Test]
        public void SchemeAndTrailingPathAreIgnored()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints("  http://192.168.0.15:9000/ws/game  ", out http, out socket, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(http, Is.EqualTo("http://192.168.0.15:9000"));
            Assert.That(socket, Is.EqualTo("ws://192.168.0.15:9000/ws/game"));
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
        public void DefaultAddressIsUsableAsIs()
        {
            string http, socket, error;

            bool ok = NetworkSession.TryBuildEndpoints(NetworkSession.DefaultServerAddress, out http, out socket, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(http, Is.EqualTo("http://127.0.0.1:8080"));
            Assert.That(socket, Is.EqualTo("ws://127.0.0.1:8080/ws/game"));
        }
    }
}
