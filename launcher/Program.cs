using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace NotThisPlanet.Launcher;

/// <summary>
/// NOT THIS PLANET 런처 — 바뀐 파일만 받아 최신 배포 버전을 맞춘다 (S15P21B202-406).
///
/// <para><b>하는 일.</b> 서버의 <c>latest.json</c> → 매니페스트를 읽고, 로컬 게임
/// 폴더와 견줘 <b>다른 파일만</b> 받고, 없어진 파일은 지우고, 게임을 띄운다.</para>
///
/// <para><b>하지 않는 일 — 구버전 막기.</b> 그건 서버 게이트(<c>-404</c>)의 몫이다.
/// 런처는 우회된다(압축 푼 폴더의 exe 를 직접 더블클릭하는 것이 가장 흔하다).
/// 그래서 <b>서버에 못 닿으면 막지 않고 그냥 실행시킨다</b> — EC2 가 한 번
/// 딸꾹질할 때 전원이 게임을 못 켜는 것이 더 나쁘다(2026-09-10 팀 확인).</para>
/// </summary>
public static class Program
{
    /// <summary>배포 주소. 사이트와 같은 호스트의 <c>/builds/</c> 다.</summary>
    const string DefaultBase = "https://j15b202.p.ssafy.io/builds";

    /// <summary>게임을 두는 곳. 런처 옆의 폴더다 — 런처는 자기 자신을 못 지운다.</summary>
    const string GameDirName = "game";

    /// <summary>
    /// 배포용 게임 폴더 이름. <b>자체 검사가 쓴다.</b>
    ///
    /// <para>상수끼리 견주면 컴파일러가 결과를 미리 접어서 검사가 <b>죽은 코드</b>가
    /// 된다(CS0162). 프로퍼티로 한 번 감싸면 실제로 돌아, 누군가 두 이름을 같게
    /// 바꿨을 때 잡힌다 — 그러면 개발 설치가 배포 설치를 덮는다.</para>
    /// </summary>
    internal static string ProdGameDirName => GameDirName;

    static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        // 서버가 죽어 있을 때 사용자를 오래 붙잡지 않는다. 받는 것은 아래에서
        // 파일마다 따로 시간을 잡는다.
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var baseUrl = (Environment.GetEnvironmentVariable("NTP_BASE") ?? DefaultBase).TrimEnd('/');
        var root = AppContext.BaseDirectory;
        bool dev = DevMode.IsOn(args);
        // 개발 모드는 **게임 폴더를 따로 쓴다** — 배포용 설치를 덮어쓰지 않으므로
        // 실수로 켜 봐도 원래 것이 남는다(-406, exe 를 하나로 둔 대가).
        var gameDir = Path.Combine(root, dev ? DevMode.GameDirName : GameDirName);
        // 배포되는 그 exe 안에서 안전 판정을 검사한다 — 테스트 프로젝트를 따로
        // 두면 배포 산출물과 검사 대상이 갈린다.
        if (args.Contains("--selfcheck")) return SelfCheck.Run();

        // **교체하는 쪽**으로 불렸으면 그것만 하고 끝낸다(자기 자신을 덮을 수 없어
        // 새 exe 가 옛 자리를 덮는다). 다른 일을 섞으면 잠금이 안 풀린다.
        if (args.Contains(SelfUpdate.FinishFlag)) return SelfUpdate.Finish(args);

        // 지난 갱신이 남긴 `.new.exe` 를 치운다.
        SelfUpdate.CleanLeftovers();

        bool checkOnly = args.Contains("--check");
        bool noLaunch = args.Contains("--no-launch") || checkOnly;

        Console.WriteLine("=== NOT THIS PLANET 런처 ===");
        Console.WriteLine($"게임 폴더: {gameDir}");

        // **런처 자신이 먼저 최신이어야 한다.** 규약(매니페스트 형식·주소·검증)이
        // 바뀌면 옛 런처는 조용히 틀린 일을 하거나 아무것도 못 한다 — 게임 버전만
        // 강제해도 런처가 낡으면 그 강제가 동작하지 않는다.
        if (!checkOnly)
        {
            switch (await SelfUpdate.EnsureLatest(Http, baseUrl, args))
            {
                case SelfUpdate.Outcome.Handoff:
                    return 0;      // 새 런처가 이어받았다 — 조용히 끝낸다
                case SelfUpdate.Outcome.Blocked:
                    return 1;      // 서버가 이 버전을 거부했고 고치지 못했다
            }
        }

        Manifest? manifest = null;
        try
        {
            // 개발 모드는 **목록에서 골라** 받는다(-405 §④ 팀 결정). 배포 모드는
            // 늘 최신이다 — 고를 여지를 주면 그것이 곧 구버전 잔류 경로가 된다.
            manifest = dev
                ? await FetchChosenManifest(baseUrl, args)
                : await FetchManifest(baseUrl);
        }
        catch (Exception e)
        {
            // **여기서 멈추지 않는다.** 서버 문제로 게임을 못 켜게 하지 않는다.
            Console.WriteLine($"⚠ 업데이트 확인에 실패했다: {e.GetType().Name}");
            Console.WriteLine("  최신 여부를 확인하지 못했다 — 있는 것으로 실행한다.");
        }

        if (manifest is not null)
        {
            var bad = ManifestCheck.Validate(manifest);
            if (bad is not null)
            {
                // 검사에 걸리면 **아무것도 쓰지 않는다.** 반쯤 적용된 폴더가 가장 나쁘다.
                Console.WriteLine($"⚠ 서버 목록을 신뢰할 수 없다: {bad}");
                Console.WriteLine("  파일을 건드리지 않고 있는 것으로 실행한다.");
                manifest = null;
            }
        }

        if (manifest is null && dev)
        {
            // 개발 모드에서 버전을 못 정한 것은 **사용자가 잘못 골랐거나 서버가
            // 비었다는 뜻**이다. 그때 배포 버전으로 조용히 넘어가면 안 된다 —
            // 옛 버전으로 재현하려던 목적이 사라진다.
            Console.WriteLine("개발 모드에서 받을 버전을 정하지 못했다 — 아무것도 하지 않는다.");
            return 1;
        }

        if (manifest is not null)
        {
            if (dev) DevMode.Banner(manifest.Version);
            Console.WriteLine($"서버 버전: {manifest.Version} · 파일 {manifest.Files.Count}개");
            var ok = await Sync(manifest, gameDir, baseUrl, checkOnly);
            if (!ok) return 1;
        }

        if (noLaunch) return 0;
        return Launch(gameDir, manifest?.Exe, dev);
    }

    /// <summary>개발 모드 — 목록을 받아 고르고, 그 버전의 매니페스트를 읽는다.</summary>
    static async Task<Manifest?> FetchChosenManifest(string baseUrl, string[] args)
    {
        var list = await DevMode.Fetch(Http, baseUrl);
        if (list is null)
        {
            Console.WriteLine("서버에 버전 목록이 없다 — 최신으로 간다.");
            return await FetchManifest(baseUrl);
        }
        if (list.Schema > ManifestCheck.SupportedSchema)
        {
            Console.WriteLine($"버전 목록 형식이 {list.Schema} 다 — 런처를 새로 받아야 한다.");
            return null;
        }
        var pick = DevMode.Choose(list, args);
        if (pick is null) return null;

        var m = await GetJson<Manifest>($"{baseUrl}/manifest/{pick}.json");
        if (m is null) throw new InvalidDataException($"{pick} 매니페스트를 읽지 못했다");
        if (m.Version != pick)
            throw new InvalidDataException($"{pick} 을 달라 했는데 매니페스트는 {m.Version} 이다");
        return m;
    }

    static async Task<Manifest> FetchManifest(string baseUrl)
    {
        // `latest.json` 만 바뀌는 파일이다 — 롤백은 그것을 되돌리는 것이다.
        // 그래서 이것만은 **캐시를 쓰지 않는다.**
        var latest = await GetJson<LatestPointer>($"{baseUrl}/latest.json");
        if (latest is null || string.IsNullOrWhiteSpace(latest.ManifestPath))
            throw new InvalidDataException("latest.json 을 읽지 못했다");
        if (!ManifestCheck.IsSafeRelativePath(latest.ManifestPath))
            throw new InvalidDataException($"latest.json 의 경로가 이상하다: {latest.ManifestPath}");

        var m = await GetJson<Manifest>($"{baseUrl}/{latest.ManifestPath}");
        if (m is null) throw new InvalidDataException("매니페스트를 읽지 못했다");
        if (m.Version != latest.Version)
            throw new InvalidDataException(
                $"latest.json 은 {latest.Version} 인데 매니페스트는 {m.Version} 이다 — 올리는 중일 수 있다");
        return m;
    }

    static async Task<T?> GetJson<T>(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var res = await Http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        await using var s = await res.Content.ReadAsStreamAsync();
        // 소스 생성기로 읽는다 — 리플렉션이 없어야 트리밍이 안전하다.
        return (T?)await JsonSerializer.DeserializeAsync(s, typeof(T), LauncherJson.Default);
    }

    /// <summary>로컬을 매니페스트에 맞춘다. 받을 것만 받고, 남는 것은 지운다.</summary>
    static async Task<bool> Sync(Manifest m, string gameDir, string baseUrl, bool checkOnly)
    {
        Directory.CreateDirectory(gameDir);

        // 지난번에 끝내지 못했으면 그 사실을 말한다. 해시로 어차피 다시 맞추지만,
        // **사용자가 "왜 또 받지" 를 알 수 있어야 한다.**
        var leftover = Path.Combine(gameDir, ".syncing");
        if (File.Exists(leftover))
            Console.WriteLine("지난번 업데이트가 끝나지 않았다 — 이어서 맞춘다.");

        // ── 무엇을 받아야 하나 ──────────────────────────────────────────────
        // 크기가 같아도 내용이 다를 수 있으므로 **해시로 판단한다.** 대신 크기가
        // 다르면 해시를 잴 필요가 없다 — 355 MB 를 매번 해시하면 시작이 느려진다.
        var need = new List<Manifest.FileEntry>();
        long haveBytes = 0;
        foreach (var f in m.Files)
        {
            var local = Path.Combine(gameDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(local) || new FileInfo(local).Length != f.Size)
            {
                need.Add(f);
                continue;
            }
            if (Sha256File(local) != f.Sha256) need.Add(f);
            else haveBytes += f.Size;
        }

        // ── 무엇을 지워야 하나 ──────────────────────────────────────────────
        // 남겨 두면 옛 에셋이 섞여 돈다 — 그 상태의 버그는 재현이 안 된다.
        var wanted = new HashSet<string>(
            m.Files.Select(f => f.Path.Replace('/', Path.DirectorySeparatorChar)),
            StringComparer.OrdinalIgnoreCase);
        var stale = Directory.Exists(gameDir)
            ? Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(gameDir, p))
                .Where(rel => !wanted.Contains(rel))
                // 우리가 만든 표시 파일과 받다 만 조각은 "옛 에셋" 이 아니다.
                .Where(rel => rel != ".syncing" && !rel.EndsWith(".part", StringComparison.Ordinal))
                .ToList()
            : new List<string>();

        long dlBytes = need.Sum(f => f.Gz > 0 ? f.Gz : f.Size);
        Console.WriteLine($"맞출 것: 받기 {need.Count}개 ({Mb(dlBytes)}) · 지우기 {stale.Count}개 "
                          + $"· 그대로 {m.Files.Count - need.Count}개 ({Mb(haveBytes)})");

        if (checkOnly)
        {
            foreach (var f in need.Take(10)) Console.WriteLine($"  + {f.Path}");
            if (need.Count > 10) Console.WriteLine($"  + … {need.Count - 10}개 더");
            foreach (var s in stale.Take(10)) Console.WriteLine($"  - {s}");
            if (stale.Count > 10) Console.WriteLine($"  - … {stale.Count - 10}개 더");
            return true;
        }

        if (need.Count == 0 && stale.Count == 0)
        {
            Console.WriteLine("이미 최신이다.");
            return true;
        }

        // ── 받는다 ─────────────────────────────────────────────────────────
        //
        // **먼저 "맞추는 중" 표시를 남긴다.** 중간에 실패하거나 사용자가 창을 닫으면
        // 폴더가 섞인 상태로 남는데, 그 사실이 어디에도 없으면 다음에 켤 때 아무도
        // 모른다. 다 끝나면 지운다.
        var marker = Path.Combine(gameDir, ".syncing");
        try { File.WriteAllText(marker, m.Version); } catch (IOException) { }

        long done = 0;
        int n = 0;
        foreach (var f in need)
        {
            n++;
            Console.Write($"\r[{n}/{need.Count}] {Mb(done)} / {Mb(dlBytes)}   ");
            try
            {
                await Download(baseUrl, f, gameDir);
            }
            catch (Exception e)
            {
                Console.WriteLine();
                Console.WriteLine($"❌ {f.Path} 를 받지 못했다: {e.GetType().Name}: {e.Message}");
                // **사실대로 말한다.** 앞서 받은 파일은 이미 제자리에 들어갔으므로
                // 폴더는 새것과 옛것이 **섞인 상태**다. "그대로 두었다" 는 거짓이고,
                // 그 말을 믿으면 사용자가 게임을 켜서 섞인 채로 논다(`!364` AI 리뷰).
                Console.WriteLine($"   {n - 1}개는 이미 적용됐고 나머지는 옛 파일이다 — "
                                  + "섞인 상태다.");
                Console.WriteLine("   다시 실행하면 남은 것만 이어받는다. "
                                  + "그때까지 게임을 켜지 마라.");
                return false;
            }
            done += f.Gz > 0 ? f.Gz : f.Size;
        }
        if (need.Count > 0) Console.WriteLine($"\r받기 완료 {Mb(dlBytes)}            ");

        // ── 지운다 — **받기가 다 끝난 뒤에** ────────────────────────────────
        // 먼저 지우면, 받기가 중간에 실패했을 때 돌던 게임이 못 돌게 된다.
        foreach (var rel in stale)
        {
            var p = Path.Combine(gameDir, rel);
            try { File.Delete(p); }
            catch (Exception e) { Console.WriteLine($"⚠ {rel} 를 지우지 못했다: {e.GetType().Name}"); }
        }
        if (stale.Count > 0) Console.WriteLine($"지우기 완료 {stale.Count}개");
        RemoveEmptyDirs(gameDir);

        try { File.Delete(marker); } catch (IOException) { }
        Console.WriteLine($"✅ {m.Version} 으로 맞췄다.");
        return true;
    }

    /// <summary>
    /// 한 파일을 받아 검증하고 제자리에 놓는다.
    ///
    /// <para>순서가 중요하다: <c>.part</c> 로 받아 → <b>푼 바이트의 해시를 확인</b>
    /// → 원자적으로 이름 바꾸기. 받는 중 끊겨도 게임 폴더에는 반쪽 파일이 남지 않는다.</para>
    /// </summary>
    static async Task Download(string baseUrl, Manifest.FileEntry f, string gameDir)
    {
        var local = Path.Combine(gameDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        var part = local + ".part";

        var url = $"{baseUrl}/files/{f.Sha256}.gz";
        using (var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            res.EnsureSuccessStatusCode();
            await using var net = await res.Content.ReadAsStreamAsync();
            await using var gz = new GZipStream(net, CompressionMode.Decompress);
            await using var outFile = new FileStream(part, FileMode.Create, FileAccess.Write,
                                                     FileShare.None, 1 << 20, useAsync: true);
            await gz.CopyToAsync(outFile, 1 << 20);
        }

        var got = Sha256File(part);
        if (got != f.Sha256)
        {
            File.Delete(part);
            throw new InvalidDataException(
                $"해시가 다르다 (받은 것 {got[..12]}… · 기대 {f.Sha256[..12]}…)");
        }
        var size = new FileInfo(part).Length;
        if (size != f.Size)
        {
            File.Delete(part);
            throw new InvalidDataException($"크기가 다르다 ({size} ≠ {f.Size})");
        }

        // 실행 중인 게임의 파일은 잠겨 있어 바꿀 수 없다 — 그때는 사유를 말한다.
        try
        {
            File.Move(part, local, overwrite: true);
        }
        catch (IOException)
        {
            File.Delete(part);
            throw new IOException(
                "파일이 사용 중이다 — 게임이 실행 중이면 닫고 다시 실행하라");
        }
    }

    static string Sha256File(string path)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    static void RemoveEmptyDirs(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (IOException) { /* 사용 중이면 둔다 */ }
        }
    }

    static int Launch(string gameDir, string? exeFromManifest, bool dev = false)
    {
        // **매니페스트가 이름을 준다.** 하드코딩하면 `-402` 의 실행 파일 이름 변경에서
        // 깨진다. 서버에 못 닿아 매니페스트가 없을 때만 폴더에서 찾는다.
        string? exe = exeFromManifest is null
            ? null
            : Path.Combine(gameDir, exeFromManifest);

        if (exe is null || !File.Exists(exe))
        {
            var found = Directory.Exists(gameDir)
                ? Directory.GetFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(p => !Path.GetFileName(p).Contains("CrashHandler", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : Array.Empty<string>();
            if (found.Length == 1) exe = found[0];
            else
            {
                Console.WriteLine(found.Length == 0
                    ? $"❌ 실행 파일이 없다. 게임을 아직 받지 못한 것이다 — 인터넷을 확인하고 다시 실행하라."
                    : $"❌ 실행 파일이 여럿이라 무엇을 켜야 할지 알 수 없다: {found.Length}개");
                return 1;
            }
        }

        Console.WriteLine($"실행: {Path.GetFileName(exe)}"
                          + (dev ? $"  ({DevMode.DevBackend})" : ""));
        try
        {
            var psi = new ProcessStartInfo(exe) { WorkingDirectory = gameDir, UseShellExecute = true };
            if (dev)
            {
                // **개발 백엔드를 인자로 넘긴다.** 실행 인자가 가장 센 우선순위라
                // (-284) 빌드 스탬프가 prod 라고 적혀 있어도 개발 서버를 가리킨다.
                // 그래야 개발 모드가 실유저 데이터를 건드리지 않는다.
                psi.ArgumentList.Add("-backend=" + DevMode.DevBackend);
            }
            // 게임을 띄우면 런처는 물러난다 — 기다리지 않는다.
            Process.Start(psi);
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine($"❌ 실행하지 못했다: {e.GetType().Name}: {e.Message}");
            return 1;
        }
    }

    static string Mb(long b) => $"{b / 1048576.0:0.0} MB";
}
