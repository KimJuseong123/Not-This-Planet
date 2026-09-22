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
    }
}
