using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    public sealed class AiPackInstallerTests
    {
        string root;
        [SetUp] public void Setup() { root = Path.Combine(Path.GetTempPath(), "ntp-pack-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        [Test] public void SourceRequiresPinnedHttps()
        {
            var s = new AiPackSource { manifest_url = "https://example.com/pack-manifest.json", manifest_sha256 = new string('a', 64) };
            Assert.That(AiPackValidation.Source(s), Is.True);
            s.manifest_url = "http://example.com/a"; Assert.That(AiPackValidation.Source(s), Is.False);
            s.manifest_url = "https://user:pass@example.com/a"; Assert.That(AiPackValidation.Source(s), Is.False);
        }
        [TestCase("runtime/../escape")]
        [TestCase("/runtime/escape")]
        [TestCase("runtime/C:/escape")]
        [TestCase("runtime/foo./escape")]
        [TestCase("runtime\\..\\escape")]
        public void RejectsTraversal(string name) => Assert.Throws<InvalidDataException>(() => AiPackValidation.EntryPath(root, name, "runtime"));
        [Test] public void RejectsHashMismatch()
        {
            var p = Path.Combine(root, "a"); File.WriteAllText(p, "payload");
            Assert.Throws<InvalidDataException>(() => AiPackValidation.Verify(p, 7, new string('a', 64)));
            using (var sha = SHA256.Create())
                Assert.DoesNotThrow(() => AiPackValidation.Verify(p, 7, BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-", "")));
        }
        [Test] public void ExtractsWithinBudgetAndRejectsOverflow()
        {
            string zip = Path.Combine(root, "a.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) using (var w = new StreamWriter(z.CreateEntry("test.txt").Open())) w.Write("abc");
            string destination = Path.Combine(root, "ok"); Directory.CreateDirectory(destination);
            AiPackValidation.Extract(zip, destination, "runtime", 3, CancellationToken.None);
            Assert.That(File.ReadAllText(Path.Combine(destination, "runtime/test.txt")), Is.EqualTo("abc"));
            Assert.Throws<InvalidDataException>(() => AiPackValidation.Extract(zip, Path.Combine(root, "bad"), "runtime", 2, CancellationToken.None));
        }
        [Test] public void PreviewManifestCannotBeInstalledFromInternet()
        {
            var m = new AiPackManifest { schema = 1, version = "1", platform = "windows-x64-cpu", protocol = "companion-v4", release_approved = false };
            Assert.That(AiPackValidation.Manifest(m), Is.False);
        }
        [Test] public void ApprovedManifestRequiresBothDistinctArchives()
        {
            var m = new AiPackManifest { schema = 1, version = "1", platform = "windows-x64-cpu", protocol = "companion-v4", release_approved = true,
                archives = new[] { new AiPackArchive { name = "runtime.zip", bytes = 10, installed_bytes = 20, sha256 = new string('a', 64) },
                    new AiPackArchive { name = "models.zip", bytes = 10, installed_bytes = 20, sha256 = new string('b', 64) } } };
            Assert.That(AiPackValidation.Manifest(m), Is.True);
            m.archives[1].name = "runtime.zip"; Assert.That(AiPackValidation.Manifest(m), Is.False);
        }
        [Test] public void CancelledExtractionDoesNotWriteFiles()
        {
            string zip = Path.Combine(root, "cancel.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) using (var w = new StreamWriter(z.CreateEntry("test.txt").Open())) w.Write("abc");
            Assert.Throws<OperationCanceledException>(() => AiPackValidation.Extract(zip, root, "runtime", 3, new CancellationToken(true)));
            Assert.That(File.Exists(Path.Combine(root, "runtime/test.txt")), Is.False);
        }

        /// <summary>
        /// <b>깔린 것이 이 빌드가 가리키는 그 팩인지</b> 알아본다 (S15P21B202-733).
        ///
        /// <para>전에는 이 물음 자체가 없었다. <c>HasPack</c> 이 참이면 받지 않았으므로
        /// <b>새 팩을 올려도 한 번 깐 사람에게는 영영 안 갔다</b> — 버튼을 눌러도 아무
        /// 말 없이 준비만 됐다. 2026-09-23 에 실제로 겪었다.</para>
        ///
        /// <para>버전 문자열이 아니라 <b>배포 정보 파일의 해시</b>를 견준다. 빌드가 이미
        /// 그 값을 고정하고 있으므로 새 개념이 필요 없고, 받을 때 쓰는 검증과 같은
        /// 함수를 쓴다.</para>
        /// </summary>
        [Test] public void AnOldPackIsNotMistakenForTheCurrentOne()
        {
            string manifest = Path.Combine(root, AiPackInstaller.ManifestName);
            File.WriteAllText(manifest, "{\"version\":\"0.1.0-preview\"}");
            string installed;
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(manifest))
                installed = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");

            var same = new AiPackSource { manifest_url = "https://example.com/pack-manifest.json", manifest_sha256 = installed };
            Assert.That(AiPackInstaller.InstalledIsCurrent(root, same), Is.True,
                "같은 팩인데 다르다고 본다 — 누를 때마다 1.8GB 를 다시 받는다");

            var newer = new AiPackSource { manifest_url = "https://example.com/pack-manifest.json", manifest_sha256 = new string('b', 64) };
            Assert.That(AiPackInstaller.InstalledIsCurrent(root, newer), Is.False,
                "새 팩이 올라왔는데 최신이라고 본다 — 고친 것이 영영 안 나간다");
        }

        /// <summary>
        /// <b>모르면 "아니다" 로 본다</b> (S15P21B202-733). 배포 정보가 없는 옛 설치나
        /// 읽기 실패는 받기를 제안하는 쪽으로 떨어진다.
        ///
        /// <para>헛걸음은 1.8GB 를 한 번 더 받는 것이고, 반대로 틀리면 <b>고친 것이
        /// 영영 안 나간다.</b> 값이 다른 두 실패라 안전한 쪽이 정해져 있다.</para>
        /// </summary>
        [Test] public void AnUnknownInstallAsksToDownload()
        {
            var source = new AiPackSource { manifest_url = "https://example.com/pack-manifest.json", manifest_sha256 = new string('a', 64) };
            Assert.That(AiPackInstaller.InstalledIsCurrent(root, source), Is.False, "배포 정보가 없는데 최신이라고 본다");
            Assert.That(AiPackInstaller.InstalledIsCurrent(null, source), Is.False);
            Assert.That(AiPackInstaller.InstalledIsCurrent(root, null), Is.False, "가리키는 곳이 없는데 최신이라고 본다");
        }
    }
}
