using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Metagame.Network
{
    [Serializable] public sealed class AiPackSource { public string manifest_url; public string manifest_sha256; }
    [Serializable] public sealed class AiPackManifest
    {
        public int schema;
        public string version, platform, protocol;
        public bool release_approved;
        public AiPackArchive[] archives;
    }
    [Serializable] public sealed class AiPackArchive { public string name, sha256; public long bytes, installed_bytes; }

    /// <summary>
    /// <see cref="AiPackInstaller.Begin"/> 이 <b>왜 시작하지 않았나</b> (S15P21B202-713).
    ///
    /// <para>전에는 말없이 돌아왔다. 버튼 하나였을 때는 "눌러도 안 된다" 로 끝났지만, 진행
    /// 창을 띄우면 이것이 <b>끝나지 않는 0%</b> 가 된다 — 창은 기다리고, 설치기는 시작한 적이
    /// 없다.</para>
    /// </summary>
    public enum AiInstallStart { Started, Busy, NotHost, NoRuntime, NoSource }

    /// <summary>
    /// 받기 한 번의 결과 (S15P21B202-713).
    ///
    /// <para>전에는 <c>Message</c> 가 <c>null</c> 이면 성공이었다. 화면 글자가 흐름을 정하니,
    /// 글자를 고치면 흐름이 바뀌었다. 결과는 결과로 돌려준다.</para>
    /// </summary>
    public enum AiDownloadResult { Done, Cancelled, Network, Stalled, Oversize, CannotWrite }

    /// <summary>
    /// 디스크 작업 스레드가 올리고 메인 스레드가 읽는 바이트 수 (S15P21B202-713).
    /// <c>Task.Run</c> 안에서 건드려도 되는 것은 <b>이것뿐</b>이다 — <c>Message</c> 는
    /// <c>Time</c> 과 로그를 건드린다.
    /// </summary>
    public sealed class AiByteCounter
    {
        long value;
        public long Value => Interlocked.Read(ref value);
        public void Add(long bytes) => Interlocked.Add(ref value, bytes);
    }

    public static class AiPackValidation
    {
        public static bool HashString(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
        public static bool Source(AiPackSource source) => source != null && HashString(source.manifest_sha256)
            && Uri.TryCreate(source.manifest_url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
        public static bool Manifest(AiPackManifest value)
        {
            if (value == null || value.schema != 1 || value.platform != "windows-x64-cpu" || value.protocol != "companion-v4"
                || !value.release_approved || string.IsNullOrWhiteSpace(value.version) || value.archives == null || value.archives.Length != 2) return false;
            bool runtime = false, models = false;
            foreach (var a in value.archives)
            {
                if (a == null || !HashString(a.sha256) || a.bytes <= 0 || a.bytes > 4L * 1024 * 1024 * 1024
                    || a.installed_bytes <= 0 || a.installed_bytes > 8L * 1024 * 1024 * 1024) return false;
                if (a.name == "runtime.zip" && !runtime) runtime = true;
                else if (a.name == "models.zip" && !models) models = true;
                else return false;
            }
            return runtime && models;
        }

        /// <summary>
        /// 파일의 SHA-256(대문자 16진). <b>조각씩 읽어</b> 읽은 만큼 <paramref name="progress"/>
        /// 에 더한다 (S15P21B202-713).
        ///
        /// <para>전에는 <c>ComputeHash</c> 한 번이라 2.2GB 를 해시하는 수십 초 동안 진행도
        /// 취소도 없었다. 조각마다 취소를 보고, 읽은 바이트로 이 PC 의 해시 속도를 잰다.</para>
        /// </summary>
        public static string Hash(string path, AiByteCounter progress, CancellationToken token)
        {
            using (var input = File.OpenRead(path)) using (var sha = SHA256.Create())
            {
                // **파일 크기에 맞춰 잡는다** (S15P21B202-713 리뷰). 로비가 IMGUI 이벤트마다 1KB 짜리
                // 배포 정보를 확인하는데, 1MB 를 늘 잡으면 프레임마다 2MB 넘는 쓰레기가 생겨 GC 가 로비를
                // 끊는다. 큰 파일은 1MB 조각으로 읽는다.
                byte[] buffer = new byte[(int)Math.Max(1L, Math.Min(input.Length, 1048576L))]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    sha.TransformBlock(buffer, 0, count, null, 0);
                    progress?.Add(count);
                }
                sha.TransformFinalBlock(buffer, 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "");
            }
        }
        public static void Verify(string path, long length, string hash) => Verify(path, length, hash, null, CancellationToken.None);
        public static void Verify(string path, long length, string hash, AiByteCounter progress, CancellationToken token)
        {
            if (!HashString(hash) || (length >= 0 && new FileInfo(path).Length != length)) throw new InvalidDataException("파일 크기 불일치");
            if (!string.Equals(Hash(path, progress, token), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("다운로드 무결성 검사 실패");
        }
        public static string EntryPath(string root, string entry, string folder)
        {
            string name = entry.Replace('\\', '/');
            if (!name.StartsWith(folder + "/", StringComparison.Ordinal) || name.Contains(":")) throw new InvalidDataException("잘못된 압축 경로");
            foreach (string part in name.Split('/'))
                if (part == ".." || part == "." || part.EndsWith(".") || part.EndsWith(" ")) throw new InvalidDataException("잘못된 압축 경로");
            string target = Path.GetFullPath(Path.Combine(root, name));
            if (!target.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("압축 경로 이탈");
            return target;
        }
        /// <summary>
        /// 풀어 쓴다. 쓴 바이트는 <paramref name="progress"/> 에 더한다 — 분모는 배포 정보의
        /// <c>installed_bytes</c> 라서 남은 시간을 정확히 셀 수 있다 (S15P21B202-713).
        /// </summary>
        public static void Extract(string zipPath, string root, string folder, long maxBytes, CancellationToken token, AiByteCounter progress = null)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                if (zip.Entries.Count > 20000) throw new InvalidDataException("압축 파일 수 초과");
                long total = 0;
                foreach (var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    // Packaging includes files.json at the archive root, never executable content there.
                    if (entry.FullName == "files.json") continue;
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("심볼릭 링크 금지");
                    total = checked(total + entry.Length);
                    if (total > maxBytes) throw new InvalidDataException("압축 해제 용량 초과");
                    if (entry.FullName.StartsWith("/") || entry.FullName.StartsWith("\\")) throw new InvalidDataException("절대 경로 금지");
                    string path = EntryPath(root, folder + "/" + entry.FullName, folder);
                    if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var input = entry.Open()) using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    {
                        byte[] buffer = new byte[131072]; long written = 0; int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested(); written += count;
                            if (written > entry.Length) throw new InvalidDataException("압축 데이터 길이 불일치");
                            output.Write(buffer, 0, count);
                            progress?.Add(count);
                        }
                        if (written != entry.Length) throw new InvalidDataException("압축 데이터 누락");
                    }
                }
            }
        }
    }

    /// <summary>Explicit opt-in install, pinned HTTPS manifest, staging + same-volume atomic promotion.</summary>
    public sealed class AiPackInstaller : MonoBehaviour
    {
        public bool Busy { get; private set; }
        readonly AiStatusLog statusLog = new AiStatusLog();
        string message;

        /// <summary>
        /// 받기·설치 상황의 <b>로그 글자</b> (S15P21B202-708). <see cref="AiPrepProgress.Log"/> 가
        /// <see cref="Snapshot"/> 에서 만든다. 화면 글자는 같은 상태를
        /// <see cref="AiPrepProgress.Display"/> 로 읽는다 — 출처는 하나이고 말투만 둘이다
        /// (S15P21B202-713).
        /// </summary>
        public string Message
        {
            get => message;
            private set
            {
                if (message == value) return;
                message = value;
                if (value != null) statusLog.Write("[AI팩]", value);
            }
        }

        /// <summary>
        /// 지금 준비 상태 (S15P21B202-713). 설치가 끝나면 <see cref="AiPrepPhase.Start"/> 에 멈춘다 —
        /// <b>설치는 끝났지만 아직 완료가 아니다.</b> 그다음(데우기)은 <see cref="AiLocalRuntime"/>
        /// 이 맡고, 워커가 Ready 라고 할 때가 완료다.
        /// </summary>
        public AiPrepSnapshot Snapshot => snap;

        UnityWebRequest request;
        CancellationTokenSource cancellation;
        string work;
        AiPrepSnapshot snap;
        ByteRate netRate, hashRate, extractRate;
        AiByteCounter hashing, extracting;
        long fetchBase, fetchNow, hashBase, extractBase;
        double began, reportedAt;
        AiDownloadResult fetched;
        string fetchError;
        public void Cancel() { cancellation?.Cancel(); request?.Abort(); }

        static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>
        /// 받는 중에 <b>멈춘 것과 느린 것을 가른다</b> (S15P21B202-706).
        ///
        /// <para>2.2GB 를 받는 동안 숫자가 안 오르면 방장은 <b>멈춘 건지 느린 건지 알 수 없다.</b>
        /// 아래 고리는 60초 동안 한 바이트도 안 오면 끊는데, 그때까지 화면은 마지막 숫자 그대로
        /// 서 있었다 — 끊기고 나서야 실패를 안다.</para>
        ///
        /// <para><b>이제 로그 글자다</b> (S15P21B202-713). MB 와 초는 진단에 쓰이고, 화면은
        /// <see cref="AiPrepProgress.Display"/> 가 % 와 남은 시간으로 말한다.</para>
        ///
        /// <para>값으로 검증할 수 있게 떼어 둔다 — 실제 다운로드 없이 확인할 수 있어야 한다.</para>
        /// </summary>
        public static string DownloadMessage(float progress, long done, long total, float idle)
            => idle > StallNotice
                ? "다운로드 " + progress.ToString("P0") + " · " + (int)Math.Floor(idle) + "초째 응답 없음"
                : "다운로드 " + progress.ToString("P0") + " · " + done / 1048576 + " / " + total / 1048576 + " MB";

        /// <summary>이만큼 조용하면 화면에 말한다. 끊는 것은 <see cref="StallAbort"/>, 말하는 것은 그 한참 전.</summary>
        public const float StallNotice = 5f;

        /// <summary>
        /// 이만큼 한 바이트도 안 오면 끊는다.
        ///
        /// <para><b>전체 시간에는 상한을 두지 않는다</b> (S15P21B202-713). 전에는 파일마다 3600초에서
        /// 잘랐는데, models.zip(2,207,845,192 B)을 한 시간 안에 받으려면 초당 약 0.61MB 가
        /// 필요하다. 그보다 느린 회선은 <b>몇 번을 다시 눌러도 영영 못 받았다</b> — 흐르고 있는
        /// 받기를 자를 이유가 없다. 멈춘 받기만 이것으로 끊는다.</para>
        /// </summary>
        public const float StallAbort = 60f;

        /// <summary>설치본 안에 함께 들어가는 배포 정보. 무엇을 깔았는지가 여기 남는다.</summary>
        public const string ManifestName = "pack-manifest.json";

        /// <summary>
        /// 받아 둔 압축 파일을 두는 폴더 이름. 설치 폴더 <b>옆</b>이다 (S15P21B202-713).
        ///
        /// <para>전에는 시도마다 만드는 <c>.install-…</c> 안에 받았고, 끝나면 성공이든 실패든 그
        /// 폴더를 통째로 지웠다. 그래서 models.zip 에서 끊기면 <b>다 받아 둔 runtime.zip 까지
        /// 버리고 2.3GB 를 처음부터</b> 다시 받았다. 여기는 시도가 끝나도 지우지 않는다.</para>
        /// </summary>
        public const string CacheFolder = ".download";

        /// <summary>풀어 쓰는 동안 디스크가 꽉 차지 않게 남겨 두는 여유.</summary>
        public const long SpaceMargin = 512L * 1024 * 1024;

        /// <summary>이 빌드가 가리키는 배포 주소. 값은 빌드 때 심는다(<c>write_ai_pack_source.py</c>).</summary>
        public static AiPackSource LoadSource()
        {
            var config = Path.Combine(Application.streamingAssetsPath, "ai-pack-source.json");
            try { if (File.Exists(config)) return JsonUtility.FromJson<AiPackSource>(File.ReadAllText(config)); }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// 깔려 있는 것이 <b>이 빌드가 가리키는 바로 그 팩</b>인가 (S15P21B202-733).
        ///
        /// <para><b>버전 문자열을 견주지 않는다.</b> 설치할 때 배포 정보 파일을 그대로
        /// 넣어 두므로, 빌드가 이미 고정하고 있는 <c>manifest_sha256</c> 과 대조하면
        /// 된다 — 바이트가 같으면 같은 팩이다. 네트워크도, 버전 대소 비교도 필요 없고,
        /// <b>받을 때 쓰는 검증과 같은 함수</b>를 쓴다.</para>
        ///
        /// <para><b>모르면 "아니다" 로 본다.</b> 파일이 없는 옛 설치나 읽기 실패는
        /// 받기를 제안하는 쪽으로 떨어진다 — 헛걸음은 1.8GB 를 한 번 더 받는 것이고,
        /// 반대로 틀리면 <b>고친 것이 영영 안 나간다.</b></para>
        /// </summary>
        public static bool InstalledIsCurrent(string root, AiPackSource source)
        {
            if (string.IsNullOrEmpty(root) || !AiPackValidation.Source(source)) return false;
            try { AiPackValidation.Verify(Path.Combine(root, ManifestName), -1, source.manifest_sha256); }
            catch (Exception) { return false; }
            return true;
        }

        /// <summary>
        /// 받을 것이 있는가 — <b>안 깔렸거나, 깔린 것이 옛 팩이거나</b> (S15P21B202-733).
        ///
        /// <para>전에는 "안 깔렸을 때" 만 받았다. 그래서 새 팩을 올려도 <b>한 번 깐 사람에게는
        /// 영영 안 갔다</b> — 버튼을 눌러도 아무 말 없이 준비만 됐다.</para>
        /// </summary>
        public static bool WantsDownload(string root)
        {
            if (!AiLocalRuntime.HasPack(root)) return true;
            return WantsDownload(root, true, LoadSource());
        }

        /// <summary>
        /// <see cref="WantsDownload(string)"/> 의 판단 — 파일 검사와 떼어 값으로 확인한다.
        ///
        /// <para><b>배포 주소가 없는 빌드에서는 깔린 팩을 최신으로 본다</b> (S15P21B202-713).
        /// 저장소의 <c>ai-pack-source.json</c> 은 비어 있고, 개발 빌드와 에디터는 그대로 쓴다.
        /// 그러면 <see cref="InstalledIsCurrent"/> 가 늘 거짓이라 팩이 있어도 받기로 가고,
        /// 받기는 주소가 없어 실패한다 — <b>나갈 길이 없는 막다른 곳</b>이다. 받을 수 없는
        /// 빌드에서 "받아야 한다" 는 답은 쓸모가 없다. 릴리스 빌드는 주소 없이 나갈 수 없다
        /// (<c>test_ai_pack_source.py</c>).</para>
        /// </summary>
        public static bool WantsDownload(string root, bool hasPack, AiPackSource source)
        {
            if (!hasPack) return true;
            if (!AiPackValidation.Source(source)) return false;
            return !InstalledIsCurrent(root, source);
        }

        /// <summary>
        /// 깔린 팩의 이름표 — 함께 넣어 둔 배포 정보 파일의 해시(소문자). 없으면 null.
        /// 빌드가 고정한 <c>manifest_sha256</c> 과 같은 값이므로, 설치 전후가 같은 이름으로 만난다.
        /// </summary>
        public static string PackId(string root)
        {
            if (string.IsNullOrEmpty(root)) return null;
            try { return AiPackValidation.Hash(Path.Combine(root, ManifestName), null, CancellationToken.None).ToLowerInvariant(); }
            catch (Exception) { return null; }
        }

        /// <summary>받아 둔 파일의 폴더. 시도마다 지우는 <c>.install-…</c> 와 같은 곳에, 그 밖에 둔다.</summary>
        public static string CacheDir(string root)
        {
            if (string.IsNullOrEmpty(root)) return null;
            string parent = Path.GetDirectoryName(root.TrimEnd('/', '\\'));
            return string.IsNullOrEmpty(parent) ? null : Path.Combine(parent, CacheFolder);
        }

        /// <summary>
        /// 받아 둔 파일의 자리. <b>이름이 곧 내용의 해시</b>라서, 배포가 바뀌면 옛 파일과 이름부터
        /// 다르다. 해시는 <see cref="AiPackValidation.Manifest"/> 가 16진 64자로 확인한 뒤에만 쓴다.
        /// </summary>
        public static string CachePath(string cacheDir, AiPackArchive archive)
            => Path.Combine(cacheDir, CacheName(archive));

        static string CacheName(AiPackArchive archive) => archive.sha256.ToLowerInvariant() + ".zip";

        /// <summary>
        /// 이번 배포가 가리키지 않는 것을 지운다 — 옛 팩의 파일, 받다 만 조각(<c>.part</c>).
        /// 남겨 두면 팩이 바뀔 때마다 2GB 씩 쌓인다.
        /// </summary>
        public static void PruneCache(string cacheDir, AiPackArchive[] keep)
        {
            if (string.IsNullOrEmpty(cacheDir) || Path.GetFileName(cacheDir) != CacheFolder) return;
            string[] files;
            try
            {
                if (!Directory.Exists(cacheDir)) return;
                files = Directory.GetFiles(cacheDir);
            }
            catch (Exception) { return; }
            foreach (string file in files)
            {
                bool wanted = false;
                if (keep != null)
                    foreach (var archive in keep)
                        if (archive != null && string.Equals(Path.GetFileName(file), CacheName(archive), StringComparison.OrdinalIgnoreCase))
                        { wanted = true; break; }
                if (!wanted) TryDelete(file);
            }
        }

        /// <summary>
        /// 받아 둔 것을 이번에 쓸 수 있는가 (S15P21B202-713).
        ///
        /// <para><b>크기와 해시가 고정된 배포 정보와 맞아야만</b> 쓴다. 이 검사가 곧 이번 시도의
        /// 무결성 검사다 — 풀기 전에 다시 해시하지 않는다. 2.2GB 를 두 번 읽으면 그만큼 기다린다.</para>
        ///
        /// <para><b>안 맞으면 지우고 거짓.</b> 실패로 떨어뜨리지 않는다 — 다시 받으면 되는 일이고,
        /// 멈추면 방장은 폴더를 찾아 지우는 수밖에 없다. 취소만은 그대로 올린다(파일은 남는다).</para>
        /// </summary>
        public static bool CachedAndVerified(string cacheDir, AiPackArchive archive, AiByteCounter progress, CancellationToken token)
        {
            if (string.IsNullOrEmpty(cacheDir) || archive == null) return false;
            string path = CachePath(cacheDir, archive);
            try
            {
                if (!File.Exists(path)) return false;
                if (new FileInfo(path).Length == archive.bytes)
                {
                    AiPackValidation.Verify(path, archive.bytes, archive.sha256, progress, token);
                    return true;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { }
            TryDelete(path);
            return false;
        }

        /// <summary>설치가 끝나면 받아 둔 것을 비운다. 우리 폴더가 아니면 손대지 않는다.</summary>
        public static void ClearCache(string cacheDir)
        {
            if (string.IsNullOrEmpty(cacheDir) || Path.GetFileName(cacheDir) != CacheFolder) return;
            try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// 이번에 받아야 하는 것 — 배포 순서 그대로, 받아 두고 확인된 것만 뺀다 (S15P21B202-713).
        /// runtime.zip 을 먼저 두는 순서는 지킨다: 그것을 풀며 이 PC 의 디스크 속도를 재야
        /// models.zip 의 남은 시간을 셀 수 있다.
        /// </summary>
        public static List<AiPackArchive> ArchivesToFetch(AiPackArchive[] archives, Func<AiPackArchive, bool> cachedAndVerified)
        {
            var fetch = new List<AiPackArchive>();
            if (archives == null) return fetch;
            foreach (var archive in archives)
                if (cachedAndVerified == null || !cachedAndVerified(archive)) fetch.Add(archive);
            return fetch;
        }

        /// <summary>
        /// 필요한 빈 공간. <b>받아 둔 바이트는 이미 디스크에 있으므로 뺀다</b> (S15P21B202-713).
        /// 빼지 않으면 거의 찬 디스크에서 다시 시도할 때, 들어갈 수 있는데도 "공간 부족" 이 된다.
        /// </summary>
        public static long SpaceNeeded(long total, long installed, long cached)
            => Math.Max(0, total - Math.Max(0, cached)) + Math.Max(0, installed) + SpaceMargin;

        /// <summary>받기 결과를 실패 사유로. 취소는 실패가 아니다.</summary>
        public static AiPrepFailure FailureOf(AiDownloadResult result)
        {
            switch (result)
            {
                case AiDownloadResult.Done:
                case AiDownloadResult.Cancelled: return AiPrepFailure.None;
                case AiDownloadResult.Stalled: return AiPrepFailure.Stalled;
                case AiDownloadResult.Oversize: return AiPrepFailure.Corrupt;
                case AiDownloadResult.CannotWrite: return AiPrepFailure.Apply;
                default: return AiPrepFailure.Network;
            }
        }

        /// <summary>
        /// 받기·설치를 시작한다. 시작하지 않으면 <b>왜인지 돌려준다</b> (S15P21B202-713).
        /// 배포 주소가 없으면 그 실패를 <see cref="Snapshot"/> 에도 남긴다 — 부른 쪽이 창을
        /// 띄웠다면 그 창이 읽을 것이 있어야 한다.
        /// </summary>
        public AiInstallStart Begin()
        {
            if (Busy) return AiInstallStart.Busy;
            if (!AiHostSettings.CanConfigure) return AiInstallStart.NotHost;
            var runtime = AiLocalRuntime.Current;
            if (runtime == null) return AiInstallStart.NoRuntime;
            snap = new AiPrepSnapshot { Phase = AiPrepPhase.Check, WarmupExpected = AiPrepProgress.FallbackWarmupSeconds };
            var source = LoadSource();
            if (!AiPackValidation.Source(source))
            {
                snap.Phase = AiPrepPhase.Failed; snap.Failure = AiPrepFailure.NoSource;
                snap.Detail = "ai-pack-source.json 이 비었거나 형식이 틀림";
                Message = AiPrepProgress.Log(snap);
                return AiInstallStart.NoSource;
            }
            Busy = true;
            netRate = new ByteRate();
            hashRate = new ByteRate(AiPrepProgress.DiskMinSeconds, ByteRate.Megabyte);
            extractRate = new ByteRate(AiPrepProgress.DiskMinSeconds, ByteRate.Megabyte);
            hashing = extracting = null;
            fetchBase = fetchNow = hashBase = extractBase = 0;
            fetchError = null;
            began = Now; reportedAt = double.NegativeInfinity;
            cancellation = new CancellationTokenSource();
            runtime.PreparePackInstall();
            Tick(true);
            StartCoroutine(Install(source));
            return AiInstallStart.Started;
        }

        IEnumerator Download(string url, string path, long bytes, bool counted)
        {
            fetched = AiDownloadResult.Network; fetchError = null;
            using (request = UnityWebRequest.Get(url))
            {
                request.redirectLimit = 0; request.timeout = 0;
                UnityWebRequestAsyncOperation op = null;
                try { request.downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true }; op = request.SendWebRequest(); }
                catch (Exception e) { fetchError = Describe(e); }
                if (op == null) { request = null; fetched = AiDownloadResult.CannotWrite; yield break; }
                ulong lastBytes = 0;
                double lastDataAt = Now;
                AiDownloadResult? stop = null;
                if (counted) netRate.Add(lastDataAt, 0);
                while (!op.isDone)
                {
                    double now = Now;
                    ulong got = request.downloadedBytes;
                    // 속도는 받는 동안의 시간으로만 잰다 — 조용한 동안도 받는 시간이다.
                    if (counted) netRate.Add(now, got > lastBytes ? (long)(got - lastBytes) : 0);
                    if (got != lastBytes) { lastBytes = got; lastDataAt = now; }
                    if (cancellation.IsCancellationRequested) stop = AiDownloadResult.Cancelled;
                    else if (got > (ulong)bytes) stop = AiDownloadResult.Oversize;
                    else if (now - lastDataAt > StallAbort) stop = AiDownloadResult.Stalled;
                    if (stop.HasValue) { request.Abort(); break; }
                    if (counted) { fetchNow = (long)got; snap.Idle = now - lastDataAt; }
                    Tick();
                    yield return null;
                }
                if (counted) { netRate.Pause(Now); snap.Idle = 0; }
                if (stop.HasValue) fetched = stop.Value;
                else if (cancellation.IsCancellationRequested) fetched = AiDownloadResult.Cancelled;
                else if (request.result != UnityWebRequest.Result.Success)
                { fetched = AiDownloadResult.Network; fetchError = request.error + " · HTTP " + request.responseCode; }
                else if (request.downloadedBytes > (ulong)bytes) fetched = AiDownloadResult.Oversize;
                else fetched = AiDownloadResult.Done;
            }
            request = null;
        }

        IEnumerator Install(AiPackSource source)
        {
            string root = AiLocalRuntime.Current.PackRoot;
            string cache = CacheDir(root);
            try
            {
                work = Path.Combine(Path.GetDirectoryName(root), ".install-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                Directory.CreateDirectory(cache);
            }
            catch (Exception e) { Fail(AiPrepFailure.Apply, "설치 폴더를 만들 수 없음 · " + Describe(e)); yield break; }

            string manifestPath = Path.Combine(work, ManifestName);
            yield return Download(source.manifest_url, manifestPath, 65536, false);
            if (fetched != AiDownloadResult.Done) { Stop(fetched, "배포 정보"); yield break; }
            AiPackManifest manifest = null;
            string why = null;
            try { AiPackValidation.Verify(manifestPath, -1, source.manifest_sha256); manifest = JsonUtility.FromJson<AiPackManifest>(File.ReadAllText(manifestPath)); }
            catch (Exception e) { why = Describe(e); }
            if (!AiPackValidation.Manifest(manifest))
            { Fail(AiPrepFailure.Corrupt, "배포 정보 검증 실패 · " + (why ?? "승인된 호환 버전이 아님")); yield break; }

            var archives = manifest.archives;
            long total = 0, installed = 0;
            foreach (var a in archives) { total += a.bytes; installed += a.installed_bytes; }
            snap.HashTotal = total; snap.ExtractTotal = installed;
            snap.WarmupExpected = AiLocalRuntime.ExpectedWarmup(source.manifest_sha256, true);

            // 받아 둔 것부터 본다. 여기서 한 해시가 이번 시도의 검사다 — 풀기 전에 다시 하지 않는다.
            PruneCache(cache, archives);
            var reuse = new bool[archives.Length];
            long cached = 0;
            for (int i = 0; i < archives.Length; i++)
            {
                var archive = archives[i];
                if (!File.Exists(CachePath(cache, archive))) continue;
                snap.Archive = archive.name;
                var counter = hashing = new AiByteCounter();
                var token = cancellation.Token;
                var check = Task.Run(() => CachedAndVerified(cache, archive, counter, token), token);
                yield return AwaitDisk(check, counter, hashRate);
                hashing = null;
                if (cancellation.IsCancellationRequested) { Finish(AiPrepPhase.Cancelled); yield break; }
                if (check.Status == TaskStatus.RanToCompletion && check.Result)
                { reuse[i] = true; cached += archive.bytes; hashBase += archive.bytes; }
                else
                {
                    // 실패가 아니다 — 지웠으니 다시 받는다. 왜 다시 받았는지만 로그에 남긴다.
                    snap.Detail = "받아 둔 " + archive.name + " 불일치 · 지우고 다시 받음";
                    Tick(true);
                    snap.Detail = null;
                }
            }
            snap.Archive = null;
            var fetch = ArchivesToFetch(archives, a => reuse[Array.IndexOf(archives, a)]);
            foreach (var a in fetch) snap.FetchTotal += a.bytes;

            long needed = SpaceNeeded(total, installed, cached), free = -1;
            try { free = new DriveInfo(Path.GetPathRoot(work)).AvailableFreeSpace; } catch (Exception) { }
            if (free < needed)
            {
                snap.NeededBytes = free >= 0 ? needed - free : 0;
                Fail(AiPrepFailure.NoSpace, "필요 " + needed / 1048576 + " MB · 빈 공간 " + (free >= 0 ? free / 1048576 + " MB" : "알 수 없음"));
                yield break;
            }

            string staging = Path.Combine(work, "pack");
            try { Directory.CreateDirectory(staging); } catch (Exception e) { Fail(AiPrepFailure.Apply, "임시 설치 폴더 생성 실패 · " + Describe(e)); yield break; }
            foreach (var archive in archives)
            {
                string path = CachePath(cache, archive);
                var token = cancellation.Token;
                if (fetch.Contains(archive))
                {
                    // 받는 동안은 .part 로 둔다. 끊기면 DownloadHandlerFile 이 지우고, 다 받은 것만
                    // 제 이름을 얻는다 — 제 이름을 가진 파일은 언제나 끝까지 받은 파일이다.
                    string part = path + ".part";
                    Enter(AiPrepPhase.Fetch, archive.name);
                    yield return Download(new Uri(new Uri(source.manifest_url), archive.name).AbsoluteUri, part, archive.bytes, true);
                    if (fetched != AiDownloadResult.Done) { Stop(fetched, archive.name); yield break; }
                    fetchBase += archive.bytes; fetchNow = 0;
                    try { if (File.Exists(path)) File.Delete(path); File.Move(part, path); }
                    catch (Exception e) { Fail(AiPrepFailure.Apply, archive.name + " 옮기기 실패 · " + Describe(e)); yield break; }

                    Enter(AiPrepPhase.Unpack, archive.name);
                    var counter = hashing = new AiByteCounter();
                    var verify = Task.Run(() => AiPackValidation.Verify(path, archive.bytes, archive.sha256, counter, token), token);
                    yield return AwaitDisk(verify, counter, hashRate);
                    hashing = null;
                    if (verify.Status != TaskStatus.RanToCompletion)
                    {
                        if (cancellation.IsCancellationRequested) { Finish(AiPrepPhase.Cancelled); yield break; }
                        // 방금 받은 것이 틀렸다. 남겨 두면 다음 시도가 또 해시만 하고 지운다.
                        TryDelete(path);
                        Fail(AiPrepFailure.Corrupt, archive.name + " 확인 실패 · " + Describe(verify.Exception));
                        yield break;
                    }
                    hashBase += archive.bytes;
                }
                else Enter(AiPrepPhase.Unpack, archive.name);

                var written = extracting = new AiByteCounter();
                string folder = archive.name == "runtime.zip" ? "runtime/ntp-ai-worker" : "models";
                var extract = Task.Run(() => AiPackValidation.Extract(path, staging, folder, archive.installed_bytes, token, written), token);
                yield return AwaitDisk(extract, written, extractRate);
                extracting = null;
                if (extract.Status != TaskStatus.RanToCompletion)
                {
                    if (cancellation.IsCancellationRequested) { Finish(AiPrepPhase.Cancelled); yield break; }
                    // 전에는 이 예외를 버려서 "왜" 가 로그에 없었다 (S15P21B202-713). 해시가 맞은
                    // 파일이므로 지우지 않는다 — 디스크 문제라면 다음 시도가 그대로 쓴다.
                    var failure = extract.Exception?.GetBaseException();
                    Fail(failure is InvalidDataException ? AiPrepFailure.Corrupt : AiPrepFailure.Apply,
                         archive.name + " 풀기 실패 · 기존 팩은 유지 · " + Describe(failure));
                    yield break;
                }
                extractBase += archive.installed_bytes;
            }
            if (cancellation.IsCancellationRequested) { Finish(AiPrepPhase.Cancelled); yield break; }
            if (!AiLocalRuntime.HasPack(staging)) { Fail(AiPrepFailure.Corrupt, "설치 파일 누락 · 기존 팩은 유지"); yield break; }
            Enter(AiPrepPhase.Apply, null);
            try { File.Copy(manifestPath, Path.Combine(staging, ManifestName)); }
            catch (Exception e) { Fail(AiPrepFailure.Apply, "설치 적용 실패 · " + Describe(e)); yield break; }
            // 설치된 팩에서 뜬 프로세스가 남아 있으면 폴더를 옮기지 못한다 (S15P21B202-745). 주인 없는
            // 것은 먼저 끝내고, 백신 검사처럼 잠깐 잡히는 경우를 위해 몇 번 다시 해 본다.
            EndAbandoned(root);
            string backup; Exception blocked;
            for (int attempt = 0; ; attempt++)
            {
                blocked = Promote(staging, root, out backup);
                if (blocked == null || attempt >= PromoteRetrySeconds.Length) break;
                yield return new WaitForSecondsRealtime(PromoteRetrySeconds[attempt]);
                if (cancellation.IsCancellationRequested) { Finish(AiPrepPhase.Cancelled); yield break; }
                EndAbandoned(root);
            }
            if (blocked != null)
            {
                int running = AiPackProcesses.Running(root);
                Fail(Blocked(blocked), "설치 적용 실패 · " + Describe(blocked)
                     + (running > 0 ? " · 팩에서 실행 중인 프로세스 " + running + "개" : ""));
                yield break;
            }
            DropOldPacks(root);
            ClearCache(cache);
            AiLocalRuntime.MarkInstalled(source.manifest_sha256);
            // 여기서 "설치 완료" 라고 하지 않는다 (S15P21B202-713). 전에는 이 자리에서 그렇게
            // 말했는데, 방장에게 남은 것은 몇 분짜리 데우기였다 — 완료는 워커가 Ready 일 때다.
            Finish(AiPrepPhase.Start);
            AiLocalRuntime.Current?.Retry();
        }

        /// <summary>폴더 교체를 다시 해 보기 전에 기다리는 초. 다 쓰면 실패로 알린다 (S15P21B202-745).</summary>
        public static readonly float[] PromoteRetrySeconds = { .5f, 1f, 2f };

        /// <summary>
        /// 새 팩을 제자리에 넣는다. 설치된 팩은 옆으로 옮겨 두었다가, 새 팩을 넣지 못하면 되돌린다.
        /// 실패하면 그 예외를, 성공하면 null 을 돌려준다.
        /// </summary>
        static Exception Promote(string staging, string root, out string backup)
        {
            backup = root + BackupInfix + Guid.NewGuid().ToString("N"); bool moved = false;
            try
            {
                if (Directory.Exists(root)) { Directory.Move(root, backup); moved = true; }
                Directory.Move(staging, root);
                return null;
            }
            catch (Exception e)
            {
                if (moved && !Directory.Exists(root)) { try { Directory.Move(backup, root); } catch (Exception) { } }
                return e;
            }
        }

        /// <summary>
        /// 폴더를 옮기지 못한 까닭. 같은 볼륨 안의 이름 바꾸기가 거부되면 거의 언제나 <b>누가 안의
        /// 파일을 쓰고 있는</b> 것이다 — 전에는 이것을 "디스크 권한" 으로 안내해 사용자가 풀 수 없었다.
        /// </summary>
        public static AiPrepFailure Blocked(Exception e)
            => e is IOException || e is UnauthorizedAccessException ? AiPrepFailure.InUse : AiPrepFailure.Apply;

        static void EndAbandoned(string root)
        {
            int ended = AiPackProcesses.EndAbandoned(root);
            if (ended > 0) Debug.Log("[AI팩] 설치된 팩에 주인 없이 남은 프로세스 " + ended + "개를 끝냈다");
        }

        /// <summary>교체 때 옆으로 옮겨 둔 옛 팩의 이름 가운데. 뒤에 32자리 16진수가 붙는다.</summary>
        public const string BackupInfix = ".backup-";

        /// <summary>
        /// 이 팩 폴더의 옛 팩(<c>p3.backup-</c> + 32자리 16진수)이면 참. 우리가 붙인 이름만 지운다.
        /// </summary>
        public static bool IsOldPack(string folderName, string rootName)
        {
            string prefix = rootName + BackupInfix;
            if (folderName == null || !folderName.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string id = folderName.Substring(prefix.Length);
            if (id.Length != 32) return false;
            foreach (char c in id) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        /// <summary>
        /// 교체가 끝난 뒤 옛 팩을 지운다 (S15P21B202-745). 전에는 업데이트마다 약 2GB 가 남았다.
        /// 새 팩은 이미 제자리에 있으므로 되돌릴 일이 없다. 뒤에서 지우고, 못 지우면 다음 설치 때 또 해 본다.
        /// </summary>
        static void DropOldPacks(string root)
        {
            string parent = Path.GetDirectoryName(root), name = Path.GetFileName(root);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;
            var old = new List<string>();
            try { foreach (var dir in Directory.GetDirectories(parent)) if (IsOldPack(Path.GetFileName(dir), name)) old.Add(dir); }
            catch (Exception) { return; }
            if (old.Count == 0) return;
            Task.Run(() =>
            {
                foreach (var dir in old)
                    try { Directory.Delete(dir, true); } catch (Exception) { }
            });
        }

        /// <summary>디스크 작업을 기다리며 그 일의 속도를 잰다. 기다리는 동안만 그 시계가 간다.</summary>
        IEnumerator AwaitDisk(Task task, AiByteCounter counter, ByteRate rate)
        {
            long seen = 0;
            rate.Add(Now, 0);
            while (!task.IsCompleted)
            {
                long value = counter.Value;
                rate.Add(Now, value - seen); seen = value;
                Tick();
                yield return null;
            }
            rate.Add(Now, counter.Value - seen);
            rate.Pause(Now);
            Tick();
        }

        /// <summary>
        /// 숫자를 상태에 옮긴다. 매 프레임 불러도 가비지를 만들지 않고, 로그 글자는
        /// <see cref="ReportEvery"/> 초에 한 번만 만든다.
        /// </summary>
        void Tick(bool force = false)
        {
            double now = Now;
            snap.Elapsed = now - began;
            snap.FetchDone = fetchBase + fetchNow;
            snap.HashDone = hashBase + (hashing != null ? hashing.Value : 0);
            snap.ExtractDone = extractBase + (extracting != null ? extracting.Value : 0);
            snap.FetchRate = netRate?.PerSecond;
            snap.HashRate = hashRate?.PerSecond;
            snap.ExtractRate = extractRate?.PerSecond;
            snap.Progress = AiPrepProgress.Fraction(snap, snap.Progress);
            snap.ProgressAt = snap.Elapsed;
            if (!force && now - reportedAt < ReportEvery) return;
            reportedAt = now;
            Message = AiPrepProgress.Log(snap);
        }

        const double ReportEvery = .5;

        void Enter(AiPrepPhase phase, string archive)
        {
            snap.Phase = phase; snap.Archive = archive;
            Tick(true);
        }

        void Stop(AiDownloadResult result, string what)
        {
            if (result == AiDownloadResult.Cancelled) { Finish(AiPrepPhase.Cancelled); return; }
            Fail(FailureOf(result), what + " 받기 " + result + (fetchError != null ? " · " + fetchError : ""));
        }

        void Fail(AiPrepFailure failure, string detail)
        {
            snap.Failure = failure; snap.Detail = detail;
            Finish(AiPrepPhase.Failed);
        }

        void Finish(AiPrepPhase phase)
        {
            snap.Phase = phase; snap.Idle = 0;
            Tick(true);
            Busy = false;
            // Only our unique staging directory; never delete the installed pack or download cache here.
            // Old packs are dropped only after a successful promotion (DropOldPacks).
            if (work != null && Path.GetFileName(work).StartsWith(".install-", StringComparison.Ordinal))
                try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            work = null; cancellation?.Dispose(); cancellation = null;
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>로그에 남길 예외 한 줄. 여러 겹이면 맨 안쪽이 원인이다.</summary>
        static string Describe(Exception e)
        {
            if (e == null) return "사유 없음";
            var inner = e.GetBaseException();
            return inner.GetType().Name + ": " + inner.Message;
        }

        void OnDestroy() { Cancel(); /* Interrupted work is left for diagnosis; never race a running extraction. */ }
    }
}
