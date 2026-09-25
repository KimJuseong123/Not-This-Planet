using System.Security.Cryptography;
using System.Text;
using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 게임이 서버의 최신 AI 추가팩을 스스로 찾는가 (S15P21B202-744).
    ///
    /// <para>전에는 빌드가 manifest 해시를 박아 두어, 팩을 올릴 때마다 CI 변수를 고치고 다시 빌드해야
    /// 했다. 이제 빌드는 고정 주소만 알고, 거기서 받은 manifest 를 <b>지금과 같은 규칙</b>으로 검사해
    /// 통과할 때만 쓴다.</para>
    /// </summary>
    public sealed class AiPackLatestTests
    {
        const string Url = "https://j15b202.p.ssafy.io/ai-packs/latest-companion-v4/pack-manifest.json";
        static readonly string Hash64 = new string('a', 64);

        static string ManifestJson(string protocol = "companion-v4", bool approved = true)
            => "{\"schema\":1,\"version\":\"0.3.0-preview\",\"platform\":\"windows-x64-cpu\",\"protocol\":\"" + protocol
               + "\",\"release_approved\":" + (approved ? "true" : "false") + ",\"archives\":["
               + "{\"name\":\"runtime.zip\",\"bytes\":10,\"installed_bytes\":20,\"sha256\":\"" + Hash64 + "\"},"
               + "{\"name\":\"models.zip\",\"bytes\":30,\"installed_bytes\":40,\"sha256\":\"" + Hash64 + "\"}]}";

        static string Sha(byte[] body)
        {
            using (var sha = SHA256.Create())
            {
                var text = new StringBuilder();
                foreach (var b in sha.ComputeHash(body)) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }

        [Test]
        public void AValidManifestBecomesTheSourceWithItsOwnHash()
        {
            var body = Encoding.UTF8.GetBytes(ManifestJson());
            var found = AiPackLatest.FromManifest(Url, body);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.manifest_url, Is.EqualTo(Url), "zip 은 이 manifest 옆에서 받으므로 주소가 그대로여야 한다");
            Assert.That(found.manifest_sha256, Is.EqualTo(Sha(body)),
                "받은 바이트의 해시여야 설치할 때 다시 받은 manifest 와 맞는다");
            Assert.That(AiPackValidation.Source(found), Is.True, "고른 값이 설치기의 검사를 통과해야 한다");
        }

        [Test]
        public void AManifestTheInstallerWouldRejectIsNotUsed()
        {
            Assert.That(AiPackLatest.FromManifest(Url, Encoding.UTF8.GetBytes(ManifestJson("companion-v9"))), Is.Null,
                "프로토콜이 다른 팩을 최신으로 보면 설치 뒤 워커가 안 맞는다");
            Assert.That(AiPackLatest.FromManifest(Url, Encoding.UTF8.GetBytes(ManifestJson(approved: false))), Is.Null,
                "승인 안 된 팩");
            Assert.That(AiPackLatest.FromManifest(Url, Encoding.UTF8.GetBytes("<html>404</html>")), Is.Null,
                "주소가 틀려 HTML 이 오면 쓰지 않는다");
            Assert.That(AiPackLatest.FromManifest(Url, new byte[AiPackLatest.MaxManifestBytes + 1]), Is.Null, "너무 크다");
            Assert.That(AiPackLatest.FromManifest("http://j15b202.p.ssafy.io/x/pack-manifest.json",
                                                  Encoding.UTF8.GetBytes(ManifestJson())), Is.Null, "https 가 아니다");
            Assert.That(AiPackLatest.Url("https://h/x?token=1"), Is.False, "질의 붙은 주소 — 설치기가 서명 URL 을 모른다");
        }

        [Test]
        public void TheLatestWinsOnlyWhenItIsValid()
        {
            var built = new AiPackSource { manifest_url = "https://j15b202.p.ssafy.io/ai-packs/0.2.0-preview/pack-manifest.json",
                                           manifest_sha256 = Hash64, latest_url = Url };
            var latest = new AiPackSource { manifest_url = Url, manifest_sha256 = new string('b', 64) };

            var chosen = AiPackLatest.Choose(built, latest);
            Assert.That(chosen.manifest_sha256, Is.EqualTo(latest.manifest_sha256), "받은 최신을 쓴다");
            Assert.That(chosen.latest_url, Is.EqualTo(Url), "최신 주소는 그대로 남는다");

            Assert.That(AiPackLatest.Choose(built, null), Is.SameAs(built), "못 받았으면 빌드의 값 — 지금과 같다");
            Assert.That(AiPackLatest.Choose(built, new AiPackSource { manifest_url = Url, manifest_sha256 = "short" }), Is.SameAs(built),
                "검사를 못 넘는 최신은 쓰지 않는다");
            var onlyLatest = AiPackLatest.Choose(new AiPackSource { latest_url = Url, manifest_url = "", manifest_sha256 = "" }, latest);
            Assert.That(AiPackValidation.Source(onlyLatest), Is.True, "대체값이 없는 빌드도 최신으로 받을 수 있다");
        }

        /// <summary>설치 · 최신 판단 · 설치 창이 모두 거치는 한 곳이 최신을 고른다.</summary>
        [Test]
        public void EveryReaderGoesThroughTheLatest()
        {
            string installer = TestSource.Read("Runtime/Network/AiPackInstaller.cs");
            Assert.That(TestSource.Method(installer, "public static AiPackSource LoadSource()"),
                Does.Contain("AiPackLatest.Choose(LoadBuiltSource(), AiPackLatest.Resolved)"));
            Assert.That(TestSource.Method(installer, "void Start()"),
                Does.Contain("StartCoroutine(AiPackLatest.Fetch(built.latest_url));"), "게임이 뜰 때 최신을 확인하지 않는다");
        }
    }
}
