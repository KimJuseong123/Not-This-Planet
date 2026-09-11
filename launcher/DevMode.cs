using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotThisPlanet.Launcher;

/// <summary>서버가 주는 버전 목록 — <c>/builds/versions.json</c>.</summary>
public sealed class VersionList
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("versions")] public List<Entry> Versions { get; set; } = new();

    public sealed class Entry
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";
        [JsonPropertyName("totalBytes")] public long TotalBytes { get; set; }
        [JsonPropertyName("files")] public int Files { get; set; }
    }
}

/// <summary>
/// 개발 모드 — <c>--dev</c> (S15P21B202-406).
///
/// <para><b>왜 있나.</b> 2026-09-10 팀 결정(`-405` §④): <b>개발 런처가 "목록에서
/// 골라 되돌리기" 를 한다.</b> 배포 런처는 늘 최신을 강제하지만, 우리는 "이 버그가
/// 어느 버전부터인지" 를 알아야 할 때가 있다.</para>
///
/// <para><b>왜 exe 를 두 개로 나누지 않았나.</b> 백신이 서명 없는 exe 를 막는다
/// (<c>-456</c>: AhnLab V3 가 실제로 런처와 게임 실행을 막았고, 테스터 전원이 같은
/// 백신을 쓴다). exe 가 둘이면 <b>화이트리스트 등재도 두 번, 사용자의 "한 번만
/// 실행" 도 두 번</b>이다. 그 비용이 "섞일 여지" 보다 크다고 팀이 판단했다
/// (2026-09-11).</para>
///
/// <para><b>섞이지 않게 하는 장치.</b> 개발 모드는 ① 화면에 크게 알리고
/// ② <b>게임 폴더를 따로 쓴다</b>(<c>game-dev/</c>) — 배포용 설치를 덮어쓰지 않으므로
/// 실수로 개발 모드를 켜 봐도 원래 설치가 남는다 ③ 게임에 <c>-backend=</c> 로 개발
/// 백엔드를 넘겨 <b>실유저 데이터를 건드리지 않는다.</b></para>
/// </summary>
public static class DevMode
{
    public const string Flag = "--dev";
    public const string PickFlag = "--version";

    /// <summary>개발 모드가 쓰는 게임 폴더. 배포용과 **섞이지 않는다.**</summary>
    public const string GameDirName = "game-dev";

    /// <summary>
    /// 개발 백엔드 주소. 클라이언트의 <c>BackendEndpoints.DevBase</c> 와 같은 값이고,
    /// 게임에 <c>-backend=</c> 로 넘긴다 — 그쪽이 가장 센 우선순위다(`-284`).
    /// </summary>
    public const string DevBackend = "https://j15b202.p.ssafy.io/dev";

    public static bool IsOn(string[] args) => args.Contains(Flag);

    /// <summary><c>--version vX.Y.Z</c> 로 고른 값. 없으면 <c>null</c>.</summary>
    public static string? RequestedVersion(string[] args)
    {
        int i = Array.IndexOf(args, PickFlag);
        if (i < 0 || i + 1 >= args.Length) return null;
        return args[i + 1];
    }

    public static async Task<VersionList?> Fetch(HttpClient http, string baseUrl)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/versions.json");
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var res = await http.SendAsync(req);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            res.EnsureSuccessStatusCode();
            await using var s = await res.Content.ReadAsStreamAsync();
            return (VersionList?)await JsonSerializer.DeserializeAsync(
                s, typeof(VersionList), LauncherJson.Default);
        }
        catch (Exception e)
        {
            Console.WriteLine($"⚠ 버전 목록을 읽지 못했다: {e.GetType().Name}");
            return null;
        }
    }

    /// <summary>
    /// 설치할 버전을 정한다.
    ///
    /// <para><c>--version</c> 이 있으면 그것을 쓰고, 목록에 없으면 <b>멈춘다</b> —
    /// 없는 버전을 조용히 최신으로 바꿔 주면 "옛 버전으로 재현해 보려던" 목적이
    /// 사라진다. 인자가 없으면 목록을 보여 주고 고르게 한다.</para>
    ///
    /// <para>콘솔이 없거나(리다이렉트) 사람이 그냥 Enter 를 누르면 <b>최신</b>이다.</para>
    /// </summary>
    public static string? Choose(VersionList list, string[] args)
    {
        var wanted = RequestedVersion(args);
        var have = list.Versions.Select(v => v.Version).ToList();
        if (have.Count == 0)
        {
            Console.WriteLine("❌ 서버 버전 목록이 비어 있다");
            return null;
        }

        if (wanted is not null)
        {
            var hit = have.FirstOrDefault(v => v.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            if (hit is null)
            {
                Console.WriteLine($"❌ {wanted} 은(는) 서버에 없다. 있는 것: {string.Join(" ", have)}");
                return null;
            }
            return hit;
        }

        Console.WriteLine();
        Console.WriteLine("설치할 버전 (Enter = 최신)");
        for (int i = 0; i < list.Versions.Count; i++)
        {
            var v = list.Versions[i];
            var when = v.CreatedAt.Length >= 10 ? v.CreatedAt[..10] : v.CreatedAt;
            Console.WriteLine($"  [{i + 1}] {v.Version,-10} {when,-12} "
                              + $"{v.Files}개 · {v.TotalBytes / 1048576.0:0.0} MB"
                              + (i == 0 ? "   ← 최신" : ""));
        }
        Console.Write("번호> ");

        string? line;
        try
        {
            line = Console.ReadLine();
        }
        catch (IOException)
        {
            line = null;     // 콘솔이 없다(리다이렉트) — 최신으로 간다
        }
        if (string.IsNullOrWhiteSpace(line)) return have[0];
        if (int.TryParse(line.Trim(), out var n) && n >= 1 && n <= have.Count) return have[n - 1];

        // 번호가 아니면 버전 문자열로도 받아 준다 — 사람이 그렇게 칠 때가 있다.
        var typed = have.FirstOrDefault(v => v.Equals(line.Trim(), StringComparison.OrdinalIgnoreCase));
        if (typed is not null) return typed;

        Console.WriteLine($"❌ 그 번호나 버전은 없다: {line.Trim()}");
        return null;
    }

    /// <summary>개발 모드임을 화면에 크게 알린다 — 모르고 켜면 안 된다.</summary>
    public static void Banner(string version)
    {
        Console.WriteLine("──────────────────────────────────────────────");
        Console.WriteLine("  개발 모드 (--dev)");
        Console.WriteLine($"  버전   {version}");
        Console.WriteLine($"  백엔드 {DevBackend}   ← 실유저 서버가 아니다");
        Console.WriteLine($"  폴더   {GameDirName}/   ← 배포용 설치와 섞이지 않는다");
        Console.WriteLine("──────────────────────────────────────────────");
    }
}
