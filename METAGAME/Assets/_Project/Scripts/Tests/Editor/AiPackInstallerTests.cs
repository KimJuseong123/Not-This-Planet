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
        /// <summary>
        /// 작은 파일을 해시할 때 <b>1MB 를 잡지 않는다</b> (S15P21B202-713 리뷰). 크기에 안 맞춘 버퍼는
        /// 1KB 배포 정보를 확인할 때마다 1MB 쓰레기가 되어 GC 가 로비를 끊는다.
        ///
        /// <para><b>로비는 이제 이벤트마다 확인하지 않는다</b> (S15P21B202-713). 전에는 상태 줄의 임시 준비 버튼이
        /// IMGUI 이벤트마다(0.5초 보관으로 줄였어도) 배포 정보를 해시했다. 그 버튼을 설치 창이 대신하면서
        /// 확인은 <b>"AI 동료 추가" 를 누를 때 한 번</b>, 작업 스레드에서 한다.</para>
        /// </summary>
        [Test] public void HashingASmallFileDoesNotGrabAMegabyte()
        {
            string hash = TestSource.Method(TestSource.Read("Runtime/Network/AiPackInstaller.cs"), "public static string Hash(");
            Assert.That(hash, Does.Not.Contain("new byte[1048576]"), "파일 크기와 상관없이 1MB 를 잡는다");
            Assert.That(hash, Does.Contain("Math.Min(input.Length, 1048576L)"), "버퍼를 파일 크기에 맞추지 않는다");

            string controls = TestSource.Read("Runtime/UI/AiRoomControls.cs");
            foreach (string call in new[] { "WantsDownload(", "InstalledIsCurrent(", "LoadSource(", "HasPack(" })
                Assert.That(TestSource.Count(controls, call), Is.Zero, "상태 줄이 그릴 때마다 팩을 확인한다 — " + call);

            string popup = TestSource.Read("Runtime/UI/AiInstallPopup.cs");
            Assert.That(TestSource.Count(popup, "InstalledIsCurrent("), Is.EqualTo(1), "설치 창이 팩을 한 자리에서만 확인하지 않는다");
            Assert.That(TestSource.Method(popup, "public void Begin("), Does.Contain("StartCheck();"),
                "누를 때 확인하지 않는다");
            Assert.That(TestSource.Method(popup, "public string Draw("), Does.Not.Contain("StartCheck"),
                "그릴 때마다 팩을 확인한다 — 예전의 이벤트마다 해시가 돌아왔다");
        }

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

        /// <summary>
        /// 받아 둔 파일은 <b>시도가 끝나도 지워지지 않는 곳</b>에 있다 (S15P21B202-713).
        ///
        /// <para>전에는 시도마다 만드는 <c>.install-…</c> 안에 받았고, 끝나면 그 폴더를 통째로
        /// 지웠다. 그래서 models.zip 에서 끊기면 다 받은 runtime.zip 까지 버리고 2.3GB 를
        /// 처음부터 받았다. <c>Finish</c> 는 <c>.install-</c> 로 시작하는 폴더만 지운다.</para>
        /// </summary>
        [Test] public void TheCacheLivesOutsideTheFolderEachAttemptDeletes()
        {
            string pack = Path.Combine(root, "p3");
            string cache = AiPackInstaller.CacheDir(pack);
            Assert.That(Path.GetDirectoryName(cache), Is.EqualTo(root), "받아 둔 곳이 설치 폴더 옆이 아니다");
            Assert.That(Path.GetFileName(cache), Does.Not.StartWith(".install-"),
                "받아 둔 곳이 시도마다 지우는 폴더와 같은 이름이다 — 다시 시도하면 처음부터 받는다");
            Assert.That(cache, Does.Not.StartWith(pack + Path.DirectorySeparatorChar),
                "받아 둔 곳이 설치본 안이다 — 새 팩으로 바꿔 끼울 때 함께 옮겨진다");

            string other = Path.Combine(root, "elsewhere"); Directory.CreateDirectory(other);
            AiPackInstaller.ClearCache(other);
            Assert.That(Directory.Exists(other), Is.True, "우리 폴더가 아닌 것을 비웠다");
        }

        /// <summary>
        /// 크기와 해시가 맞으면 <b>다시 받지 않는다</b> (S15P21B202-713). 그 확인이 곧 이번
        /// 시도의 무결성 검사다.
        /// </summary>
        [Test] public void ACachedArchiveIsReusedWhenSizeAndHashMatch()
        {
            string cache = AiPackInstaller.CacheDir(Path.Combine(root, "p3"));
            var runtime = Cached(cache, "runtime.zip", "runtime payload");
            var models = new AiPackArchive { name = "models.zip", bytes = 5, installed_bytes = 5, sha256 = new string('c', 64) };

            var hashed = new AiByteCounter();
            Assert.That(AiPackInstaller.CachedAndVerified(cache, runtime, hashed, CancellationToken.None), Is.True,
                "받아 둔 그대로인데 다시 받는다");
            Assert.That(File.Exists(AiPackInstaller.CachePath(cache, runtime)), Is.True, "쓸 파일을 지웠다");
            Assert.That(hashed.Value, Is.EqualTo(runtime.bytes), "확인한 바이트를 세지 않는다 — 해시 속도를 잴 수 없다");

            var fetch = AiPackInstaller.ArchivesToFetch(new[] { runtime, models },
                a => AiPackInstaller.CachedAndVerified(cache, a, null, CancellationToken.None));
            Assert.That(fetch, Is.EqualTo(new[] { models }), "받아 둔 것까지 받거나, 받을 것을 빼먹는다");
        }

        /// <summary>
        /// 맞지 않으면 <b>지우고 다시 받는다 — 실패로 멈추지 않는다</b> (S15P21B202-713).
        ///
        /// <para>멈추면 방장이 할 수 있는 것은 숨은 폴더를 찾아 지우는 것뿐이다. 다시 받으면
        /// 되는 일이다.</para>
        /// </summary>
        [Test] public void AMismatchedCacheIsDeletedAndFetchedAgain()
        {
            string cache = AiPackInstaller.CacheDir(Path.Combine(root, "p3"));
            var archive = Cached(cache, "runtime.zip", "runtime payload");
            string path = AiPackInstaller.CachePath(cache, archive);

            File.WriteAllText(path, "RUNTIME PAYLOAD");   // 같은 크기, 다른 내용
            Assert.That(AiPackInstaller.CachedAndVerified(cache, archive, null, CancellationToken.None), Is.False,
                "내용이 다른 파일을 그대로 쓴다");
            Assert.That(File.Exists(path), Is.False, "틀린 파일을 남겨 둔다 — 다음 시도도 또 해시하고 버린다");

            File.WriteAllText(path, "short");             // 받다 만 크기
            Assert.That(AiPackInstaller.CachedAndVerified(cache, archive, null, CancellationToken.None), Is.False);
            Assert.That(File.Exists(path), Is.False);

            var fetch = AiPackInstaller.ArchivesToFetch(new[] { archive },
                a => AiPackInstaller.CachedAndVerified(cache, a, null, CancellationToken.None));
            Assert.That(fetch, Is.EqualTo(new[] { archive }), "지운 것을 다시 받지 않는다");

            File.WriteAllText(path, "runtime payload");
            Assert.That(() => AiPackInstaller.CachedAndVerified(cache, archive, null, new CancellationToken(true)),
                Throws.InstanceOf<OperationCanceledException>(), "취소를 불일치로 삼켰다 — 취소했는데 계속 받는다");
            Assert.That(File.Exists(path), Is.True, "취소했다고 받아 둔 것을 지웠다 — 다음에 또 처음부터 받는다");
        }

        /// <summary>
        /// <b>옛 팩의 파일은 쓰지 않고 치운다</b> (S15P21B202-713). 이름이 곧 내용의 해시라,
        /// 배포가 바뀌면 옛 파일과 이름부터 다르다. 받다 만 조각(<c>.part</c>)도 함께 치운다 —
        /// 남겨 두면 팩이 바뀔 때마다 2GB 씩 쌓인다.
        /// </summary>
        [Test] public void AStaleShaIsIgnoredAndCleared()
        {
            string cache = AiPackInstaller.CacheDir(Path.Combine(root, "p3"));
            var current = Cached(cache, "runtime.zip", "new runtime");
            var old = Cached(cache, "runtime.zip", "old runtime");
            string part = AiPackInstaller.CachePath(cache, current) + ".part";
            File.WriteAllText(part, "half");

            AiPackInstaller.PruneCache(cache, new[] { current });
            Assert.That(File.Exists(AiPackInstaller.CachePath(cache, old)), Is.False, "옛 팩의 파일을 남겨 둔다");
            Assert.That(File.Exists(part), Is.False, "받다 만 조각을 남겨 둔다");
            Assert.That(File.Exists(AiPackInstaller.CachePath(cache, current)), Is.True, "이번 팩의 파일까지 지웠다");

            var renamed = new AiPackArchive { name = "runtime.zip", bytes = current.bytes, installed_bytes = 1, sha256 = new string('d', 64) };
            Assert.That(AiPackInstaller.CachedAndVerified(cache, renamed, null, CancellationToken.None), Is.False,
                "해시가 다른 배포인데 받아 둔 파일을 쓴다");
        }

        /// <summary>
        /// 공간 검사는 <b>이미 받아 둔 바이트를 뺀다</b> (S15P21B202-713). 빼지 않으면 거의 찬
        /// 디스크에서 다시 시도할 때, 들어갈 수 있는데도 "공간 부족" 이 된다.
        /// </summary>
        [Test] public void TheSpaceCheckSubtractsWhatIsAlreadyHere()
        {
            long fresh = AiPackInstaller.SpaceNeeded(2300, 2500, 0);
            Assert.That(fresh, Is.EqualTo(2300 + 2500 + AiPackInstaller.SpaceMargin));
            Assert.That(AiPackInstaller.SpaceNeeded(2300, 2500, 128), Is.EqualTo(fresh - 128),
                "받아 둔 만큼을 또 요구한다");
            Assert.That(AiPackInstaller.SpaceNeeded(2300, 2500, 9999), Is.EqualTo(2500 + AiPackInstaller.SpaceMargin),
                "받아 둔 것이 전체보다 많다고 풀 자리까지 깎는다");
        }

        /// <summary>
        /// <b>배포 주소가 없는 빌드에서는 깔린 팩을 최신으로 본다</b> (S15P21B202-713).
        ///
        /// <para>저장소의 배포 주소 파일은 비어 있고 개발 빌드와 에디터는 그대로 쓴다. 그러면
        /// 팩이 있어도 늘 "받아야 한다" 가 되고, 받기는 주소가 없어 실패한다 — 나갈 길이 없다.</para>
        /// </summary>
        [Test] public void NoSourceButAPackCountsAsCurrent()
        {
            Assert.That(AiPackInstaller.WantsDownload(root, true, null), Is.False,
                "받을 수 없는 빌드에서 깔린 팩을 버리라고 한다 — 막다른 곳이다");
            Assert.That(AiPackInstaller.WantsDownload(root, true, new AiPackSource()), Is.False);
            Assert.That(AiPackInstaller.WantsDownload(root, false, null), Is.True, "팩이 없는데 받지 않는다");

            var source = new AiPackSource { manifest_url = "https://example.com/pack-manifest.json", manifest_sha256 = new string('a', 64) };
            Assert.That(AiPackInstaller.WantsDownload(root, true, source), Is.True,
                "배포 주소가 있는데 모르는 팩을 최신으로 본다 — 고친 것이 영영 안 나간다 (S15P21B202-733)");
        }

        /// <summary>
        /// 시작하지 않으면 <b>왜인지 말한다</b> (S15P21B202-713). 전에는 말없이 돌아와서, 창을
        /// 띄우면 그것이 끝나지 않는 0% 가 됐다.
        /// </summary>
        [Test] public void BeginSaysWhyItDidNotStart()
        {
            if (AiLocalRuntime.Current != null) Assert.Ignore("실행기가 떠 있으면 진짜 받기가 시작된다");
            var host = new UnityEngine.GameObject("installer begin test");
            try
            {
                var installer = host.AddComponent<AiPackInstaller>();
                Assert.That(installer.Begin(), Is.EqualTo(AiInstallStart.NotHost), "방장이 아닌데 조용히 돌아왔다");

                var lobby = AiHostPolicyTests.CreateLobby();
                try
                {
                    Assert.That(installer.Begin(), Is.EqualTo(AiInstallStart.NoRuntime), "실행기가 없는데 조용히 돌아왔다");
                    Assert.That(installer.Busy, Is.False, "시작하지 않았는데 바쁘다고 한다 — 창이 영영 기다린다");
                }
                finally { UnityEngine.Object.DestroyImmediate(lobby.gameObject); }
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        /// <summary>받기 결과가 <b>실패 사유로 옮겨진다</b>. 취소는 실패가 아니다.</summary>
        [TestCase(AiDownloadResult.Done, AiPrepFailure.None)]
        [TestCase(AiDownloadResult.Cancelled, AiPrepFailure.None)]
        [TestCase(AiDownloadResult.Network, AiPrepFailure.Network)]
        [TestCase(AiDownloadResult.Stalled, AiPrepFailure.Stalled)]
        [TestCase(AiDownloadResult.Oversize, AiPrepFailure.Corrupt)]
        [TestCase(AiDownloadResult.CannotWrite, AiPrepFailure.Apply)]
        public void ADownloadResultNamesItsFailure(AiDownloadResult result, AiPrepFailure failure)
            => Assert.That(AiPackInstaller.FailureOf(result), Is.EqualTo(failure));

        static AiPackArchive Cached(string cache, string name, string content)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
            string sha;
            using (var hash = SHA256.Create()) sha = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
            var archive = new AiPackArchive { name = name, bytes = bytes.Length, installed_bytes = bytes.Length, sha256 = sha };
            Directory.CreateDirectory(cache);
            File.WriteAllBytes(AiPackInstaller.CachePath(cache, archive), bytes);
            return archive;
        }
    }
}
