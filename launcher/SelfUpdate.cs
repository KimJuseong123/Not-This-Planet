using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotThisPlanet.Launcher;

/// <summary>서버가 말하는 런처 버전 — <c>/builds/launcher.json</c>.</summary>
public sealed class LauncherInfo
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("gz")] public long Gz { get; set; }
    /// <summary>이 버전보다 낮은 런처는 <b>게임을 켜지 못한다.</b> 비우면 강제하지 않는다.</summary>
    [JsonPropertyName("minLauncher")] public string MinLauncher { get; set; } = "";
}

/// <summary>
/// 런처 자신을 최신으로 맞춘다 (S15P21B202-406).
///
/// <para><b>왜 필요한가.</b> 사이트에서 런처를 받아 가면 그 파일은 사용자 PC 에
/// 남는다. 런처의 규약(매니페스트 형식·주소·검증 방식)이 바뀌면 옛 런처는
/// <b>조용히 틀린 일을 하거나 아무것도 못 한다.</b> 게임 버전만 강제해도 런처가
/// 낡으면 그 강제 자체가 동작하지 않는다.</para>
///
/// <para><b>런처는 자기 자신을 덮어쓸 수 없다</b> — 실행 중 파일이 잠긴다. 그래서
/// 옆에 새 것을 받아 두고, 그것을 띄우고, <b>새 것이 옛 자리를 덮는다.</b>
/// 3단계로 손을 바꾼다:</para>
/// <list type="number">
///   <item>지금 런처: 새 exe 를 <c>.new.exe</c> 로 받아 해시 확인 → 그것을
///         <c>--finish-update &lt;내 경로&gt; &lt;내 PID&gt;</c> 로 띄우고 <b>끝난다</b></item>
///   <item>새 exe: 내 PID 가 사라질 때까지 기다림 → 자기를 옛 자리에 복사 →
///         옛 자리의 exe 를 띄우고 <b>끝난다</b></item>
///   <item>제자리로 돌아온 런처: 남은 <c>.new.exe</c> 를 지우고 평소대로 돈다</item>
/// </list>
///
/// <para><b>실패해도 게임을 막지 않는다</b> — 단, 서버가 "이 버전 미만은 안 된다"
/// 고 말할 때만 막는다. 그때는 고칠 방법(주소)을 화면에 준다.</para>
/// </summary>
public static class SelfUpdate
{
    public const string FinishFlag = "--finish-update";
    const string NewSuffix = ".new.exe";

    /// <summary>이 런처의 버전. 빌드 때 <c>Version</c> 프로퍼티로 박힌다.</summary>
    public static string MyVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "0.0.0";
            // `1.2.3+abcdef` 처럼 뒤에 붙는 것을 떼고, `v` 를 붙여 서버 값과 모양을 맞춘다.
            var plus = v.IndexOf('+');
            if (plus > 0) v = v[..plus];
            return v.StartsWith('v') ? v : "v" + v;
        }
    }

    /// <summary>`vX.Y.Z` 를 견준다. 모양이 아니면 <c>null</c>.</summary>
    public static int? Compare(string a, string b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        if (pa is null || pb is null) return null;
        for (int i = 0; i < 3; i++)
            if (pa[i] != pb[i]) return pa[i].CompareTo(pb[i]);
        return 0;
    }

    static int[]? Parse(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var s = v.Trim();
        if (s.StartsWith('v')) s = s[1..];
        var parts = s.Split('.');
        if (parts.Length != 3) return null;
        var o = new int[3];
        for (int i = 0; i < 3; i++)
            if (!int.TryParse(parts[i], out o[i]) || o[i] < 0) return null;
        return o;
    }

    public enum Outcome
    {
        /// <summary>최신이거나 확인할 수 없다 — 그대로 계속한다.</summary>
        Continue,
        /// <summary>새 런처를 띄웠다 — 이 프로세스는 조용히 끝나야 한다.</summary>
        Handoff,
        /// <summary>낡았는데 갱신도 못 했다 — <b>게임을 켜지 않는다.</b></summary>
        Blocked,
    }

    /// <summary>
    /// 서버의 런처 버전을 보고 필요하면 갱신을 시작한다.
    /// </summary>
    public static async Task<Outcome> EnsureLatest(HttpClient http, string baseUrl)
    {
        LauncherInfo? info;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/launcher.json");
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var res = await http.SendAsync(req);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                return Outcome.Continue;   // 서버가 런처 버전을 아직 안 말한다 — 강제하지 않는다
            res.EnsureSuccessStatusCode();
            await using var s = await res.Content.ReadAsStreamAsync();
            info = (LauncherInfo?)await JsonSerializer.DeserializeAsync(s, typeof(LauncherInfo), LauncherJson.Default);
        }
        catch (Exception e)
        {
            // **막지 않는다.** 서버 문제로 게임을 못 켜게 하지 않는다(`-406` 결정).
            Console.WriteLine($"⚠ 런처 버전을 확인하지 못했다: {e.GetType().Name}");
            return Outcome.Continue;
        }

        if (info is null || info.Schema > ManifestCheck.SupportedSchema) return Outcome.Continue;
        var mine = MyVersion;
        var cmp = Compare(mine, info.Version);
        if (cmp is null)
        {
            Console.WriteLine($"⚠ 런처 버전을 견줄 수 없다 (내 것 {mine} · 서버 {info.Version})");
            return Outcome.Continue;
        }
        if (cmp >= 0)
        {
            Console.WriteLine($"런처 {mine} (최신)");
            return Outcome.Continue;
        }

        Console.WriteLine($"런처 {mine} → {info.Version} 로 갱신한다");

        // 강제 여부를 **먼저** 정해 둔다 — 갱신이 실패했을 때 막을지 판단하는 근거다.
        bool tooOld = info.MinLauncher.Length > 0 && Compare(mine, info.MinLauncher) is int c && c < 0;

        try
        {
            await Handoff(http, baseUrl, info);
            return Outcome.Handoff;
        }
        catch (Exception e)
        {
            Console.WriteLine($"⚠ 런처 갱신에 실패했다: {e.GetType().Name}: {e.Message}");
            if (!tooOld)
            {
                Console.WriteLine("  이 버전으로도 돌 수 있다 — 계속한다.");
                return Outcome.Continue;
            }
            // **여기서만 막는다.** 서버가 "이 버전 미만은 안 된다" 고 말했고,
            // 우리가 고치지 못했다. 사용자가 할 수 있는 일을 알려 준다.
            Console.WriteLine($"❌ 이 런처({mine})는 더 쓸 수 없다 — 최소 {info.MinLauncher} 가 필요하다.");
            Console.WriteLine("   https://j15b202.p.ssafy.io/play/ 에서 런처를 다시 받아 주세요.");
            return Outcome.Blocked;
        }
    }

    static async Task Handoff(HttpClient http, string baseUrl, LauncherInfo info)
    {
        if (!ManifestCheck.IsSha256(info.Sha256))
            throw new InvalidDataException("서버가 준 런처 해시가 sha256 이 아니다");

        var me = Environment.ProcessPath
                 ?? throw new InvalidOperationException("내 실행 경로를 알 수 없다");
        var next = me + NewSuffix;

        // 게임 파일과 **같은 저장소**에서 받는다 — 형식도 같다(`files/<sha256>.gz`).
        var url = $"{baseUrl}/files/{info.Sha256}.gz";
        using (var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            res.EnsureSuccessStatusCode();
            await using var netStream = await res.Content.ReadAsStreamAsync();
            await using var gz = new GZipStream(netStream, CompressionMode.Decompress);
            await using var f = new FileStream(next, FileMode.Create, FileAccess.Write, FileShare.None);
            await gz.CopyToAsync(f, 1 << 20);
        }

        // **띄우기 전에 확인한다.** 여기서 틀리면 사용자가 받은 적 없는 exe 를 돌리게 된다.
        var got = Sha256(next);
        if (got != info.Sha256)
        {
            File.Delete(next);
            throw new InvalidDataException($"받은 런처의 해시가 다르다 ({got[..12]}… ≠ {info.Sha256[..12]}…)");
        }

        Process.Start(new ProcessStartInfo(next)
        {
            ArgumentList = { FinishFlag, me, Environment.ProcessId.ToString() },
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(me)!,
        });
        Console.WriteLine("새 런처를 띄웠다 — 이 창은 닫힌다.");
    }

    /// <summary>
    /// 새 런처가 옛 자리를 덮는 쪽. <c>--finish-update &lt;옛 경로&gt; &lt;옛 PID&gt;</c>.
    /// </summary>
    public static int Finish(string[] args)
    {
        int i = Array.IndexOf(args, FinishFlag);
        if (i < 0 || i + 2 >= args.Length)
        {
            Console.WriteLine($"❌ {FinishFlag} 에는 <옛 경로> <옛 PID> 가 필요하다");
            return 1;
        }
        var target = args[i + 1];
        if (!int.TryParse(args[i + 2], out var pid))
        {
            Console.WriteLine("❌ PID 를 읽을 수 없다");
            return 1;
        }

        Console.WriteLine("런처를 교체하는 중…");
        WaitForExit(pid, TimeSpan.FromSeconds(30));

        var me = Environment.ProcessPath!;
        // 잠금이 늦게 풀릴 수 있다 — 몇 번 다시 시도한다.
        Exception? last = null;
        for (int n = 0; n < 10; n++)
        {
            try
            {
                File.Copy(me, target, overwrite: true);
                last = null;
                break;
            }
            catch (IOException e)
            {
                last = e;
                Thread.Sleep(500);
            }
        }
        if (last is not null)
        {
            Console.WriteLine($"❌ 옛 런처를 덮지 못했다: {last.GetType().Name}");
            Console.WriteLine($"   {Path.GetFileName(me)} 를 {Path.GetFileName(target)} 로 손수 바꿔 주세요.");
            return 1;
        }

        Process.Start(new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(target)!,
        });
        return 0;
    }

    static void WaitForExit(int pid, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) return;
                p.WaitForExit(500);
            }
            catch (ArgumentException)
            {
                return;      // 이미 없다
            }
        }
    }

    /// <summary>지난 갱신이 남긴 <c>.new.exe</c> 를 치운다. 실패해도 넘어간다.</summary>
    public static void CleanLeftovers()
    {
        try
        {
            var me = Environment.ProcessPath;
            if (me is null) return;
            var leftover = me + NewSuffix;
            if (File.Exists(leftover)) File.Delete(leftover);
        }
        catch (IOException) { /* 다음에 지운다 */ }
        catch (UnauthorizedAccessException) { /* 다음에 지운다 */ }
    }

    static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }
}
