using System;
using System.Collections;
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
        public static void Verify(string path, long length, string hash)
        {
            if (!HashString(hash) || (length >= 0 && new FileInfo(path).Length != length)) throw new InvalidDataException("파일 크기 불일치");
            using (var input = File.OpenRead(path)) using (var sha = SHA256.Create())
                if (!string.Equals(BitConverter.ToString(sha.ComputeHash(input)).Replace("-", ""), hash, StringComparison.OrdinalIgnoreCase))
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
        public static void Extract(string zipPath, string root, string folder, long maxBytes, CancellationToken token)
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
        public float Progress { get; private set; }
        public string Message { get; private set; }
        UnityWebRequest request;
        CancellationTokenSource cancellation;
        Task diskTask;
        string work;
        public void Cancel() { cancellation?.Cancel(); request?.Abort(); }

        /// <summary>
        /// 받는 중에 <b>멈춘 것과 느린 것을 가른다</b> (S15P21B202-706).
        ///
        /// <para>2.2GB 를 받는 동안 숫자가 안 오르면 방장은 <b>멈춘 건지 느린 건지 알 수 없다.</b>
        /// 아래 고리는 60초 동안 한 바이트도 안 오면 끊는데, 그때까지 화면은 마지막 숫자 그대로
        /// 서 있었다 — 끊기고 나서야 실패를 안다.</para>
        ///
        /// <para>값으로 검증할 수 있게 떼어 둔다 — 실제 다운로드 없이 확인할 수 있어야 한다.</para>
        /// </summary>
        public static string DownloadMessage(float progress, long done, long total, float idle)
            => idle > StallNotice
                ? "다운로드 " + progress.ToString("P0") + " · " + Mathf.FloorToInt(idle) + "초째 응답 없음"
                : "다운로드 " + progress.ToString("P0") + " · " + done / 1048576 + " / " + total / 1048576 + " MB";

        /// <summary>이만큼 조용하면 화면에 말한다. 끊는 것은 <c>60</c>초, 말하는 것은 그 한참 전.</summary>
        public const float StallNotice = 5f;
        public void Begin()
        {
            if (Busy || !AiHostSettings.CanConfigure || AiLocalRuntime.Current == null) return;
            var config = Path.Combine(Application.streamingAssetsPath, "ai-pack-source.json");
            AiPackSource source = null;
            try { if (File.Exists(config)) source = JsonUtility.FromJson<AiPackSource>(File.ReadAllText(config)); } catch (Exception) { }
            if (!AiPackValidation.Source(source)) { Message = "다운로드 배포 주소가 아직 설정되지 않았어요"; return; }
            Busy = true; Progress = 0; Message = "추가팩 정보 확인 중";
            cancellation = new CancellationTokenSource();
            AiLocalRuntime.Current.PreparePackInstall();
            StartCoroutine(Install(source));
        }
        IEnumerator Download(string url, string path, long bytes, long completed, long total)
        {
            using (request = UnityWebRequest.Get(url))
            {
                request.redirectLimit = 0; request.timeout = 0;
                UnityWebRequestAsyncOperation op = null;
                try { request.downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true }; op = request.SendWebRequest(); }
                catch (Exception) { Message = "다운로드 파일 생성 실패 · 디스크 권한/공간을 확인해 주세요"; }
                if (op == null) { request = null; yield break; }
                ulong lastBytes = 0;
                float lastDataAt = Time.realtimeSinceStartup, began = lastDataAt;
                while (!op.isDone)
                {
                    if (lastBytes != request.downloadedBytes) { lastBytes = request.downloadedBytes; lastDataAt = Time.realtimeSinceStartup; }
                    if (cancellation.IsCancellationRequested || request.downloadedBytes > (ulong)bytes
                        || Time.realtimeSinceStartup - lastDataAt > 60 || Time.realtimeSinceStartup - began > 3600) { request.Abort(); break; }
                    Progress = Mathf.Clamp01((float)(completed + (long)request.downloadedBytes) / total);
                    if (total > 65536)
                        Message = DownloadMessage(Progress, completed + (long)request.downloadedBytes, total,
                                                  Time.realtimeSinceStartup - lastDataAt);
                    yield return null;
                }
                if (request.result != UnityWebRequest.Result.Success || request.downloadedBytes > (ulong)bytes)
                    Message = cancellation.IsCancellationRequested ? "설치 취소됨" : "다운로드 실패 · 연결/서버 주소를 확인하고 다시 시도해 주세요";
                else Message = null;
            }
            request = null;
        }
        IEnumerator Install(AiPackSource source)
        {
            string root = AiLocalRuntime.Current.PackRoot;
            try { work = Path.Combine(Path.GetDirectoryName(root), ".install-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work); }
            catch (Exception) { Finish("설치 폴더를 만들 수 없어요"); yield break; }
            string manifestPath = Path.Combine(work, "pack-manifest.json");
            yield return Download(source.manifest_url, manifestPath, 65536, 0, 65536);
            if (Message != null) { Finish(Message); yield break; }
            AiPackManifest manifest = null;
            try { AiPackValidation.Verify(manifestPath, -1, source.manifest_sha256); manifest = JsonUtility.FromJson<AiPackManifest>(File.ReadAllText(manifestPath)); }
            catch (Exception) { }
            if (!AiPackValidation.Manifest(manifest)) { Finish("추가팩 정보 검증 실패 · 승인된 호환 버전이 아니에요"); yield break; }
            long total = 0, installed = 0;
            foreach (var a in manifest.archives) { total += a.bytes; installed += a.installed_bytes; }
            bool space = false;
            try { space = new DriveInfo(Path.GetPathRoot(work)).AvailableFreeSpace >= total + installed + 512L * 1024 * 1024; } catch (Exception) { }
            if (!space) { Finish("설치 공간 부족 · 약 " + ((total + installed) / 1073741824f + .5f).ToString("F1") + " GB 필요"); yield break; }
            long completed = 0;
            string staging = Path.Combine(work, "pack");
            try { Directory.CreateDirectory(staging); } catch (Exception) { Finish("임시 설치 폴더 생성 실패"); yield break; }
            foreach (var archive in manifest.archives)
            {
                string path = Path.Combine(work, archive.name);
                yield return Download(new Uri(new Uri(source.manifest_url), archive.name).AbsoluteUri, path, archive.bytes, completed, total);
                if (Message != null) { Finish(Message); yield break; }
                Message = "검증 및 압축 해제 중 · " + archive.name;
                var token = cancellation.Token;
                diskTask = Task.Run(() => { AiPackValidation.Verify(path, archive.bytes, archive.sha256); AiPackValidation.Extract(path, staging, archive.name == "runtime.zip" ? "runtime/ntp-ai-worker" : "models", archive.installed_bytes, token); }, token);
                while (!diskTask.IsCompleted) yield return null;
                if (diskTask.IsFaulted || diskTask.IsCanceled) { Finish(cancellation.IsCancellationRequested ? "설치 취소됨" : "검증/압축 해제 실패 · 기존 추가팩은 유지됩니다"); yield break; }
                completed += archive.bytes;
            }
            if (cancellation.IsCancellationRequested) { Finish("설치 취소됨"); yield break; }
            if (!AiLocalRuntime.HasPack(staging)) { Finish("설치 파일 누락 · 기존 추가팩은 유지됩니다"); yield break; }
            string backup = root + ".backup-" + Guid.NewGuid().ToString("N"); bool moved = false;
            try
            {
                File.Copy(manifestPath, Path.Combine(staging, "pack-manifest.json"));
                if (Directory.Exists(root)) { Directory.Move(root, backup); moved = true; }
                Directory.Move(staging, root);
            }
            catch (Exception)
            {
                if (moved && !Directory.Exists(root)) { try { Directory.Move(backup, root); } catch (Exception) { } }
                Finish("설치 적용 실패 · 파일 사용 여부와 디스크 권한을 확인해 주세요"); yield break;
            }
            Progress = 1; Finish("추가팩 설치 완료 · AI 준비 중");
            AiLocalRuntime.Current?.Retry();
        }
        void Finish(string message)
        {
            Message = message; Busy = false;
            // Only our unique staging directory; never delete the installed pack or backup.
            if (work != null && Path.GetFileName(work).StartsWith(".install-", StringComparison.Ordinal))
                try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            work = null; cancellation?.Dispose(); cancellation = null;
        }
        void OnDestroy() { Cancel(); /* Interrupted work is left for diagnosis; never race a running extraction. */ }
    }
}
