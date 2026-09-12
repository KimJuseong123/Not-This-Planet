using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotThisPlanet.Launcher;

/// <summary>
/// 서버가 주는 목록 (S15P21B202-406).
///
/// <para><b>이 값들은 남이 준 것으로 다룬다.</b> 매니페스트는 서버에서 받아 온
/// 문자열이고, 런처는 그 안의 <c>path</c> 로 <b>파일을 쓴다.</b> 그러니 여기서
/// 검사하지 않으면 매니페스트 한 줄이 게임 폴더 밖에 파일을 만들 수 있다.
/// 만드는 쪽(<c>make_manifest.py</c>)도 같은 검사를 하지만, <b>두 쪽 다</b> 해야
/// 한다 — 서버가 바뀌거나 중간에서 끼어들 수 있는 값이다.</para>
/// </summary>
public sealed class Manifest
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("exe")] public string Exe { get; set; } = "";
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; set; }
    [JsonPropertyName("files")] public List<FileEntry> Files { get; set; } = new();

    public sealed class FileEntry
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("gz")] public long Gz { get; set; }
    }
}

public sealed class LatestPointer
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("manifest")] public string ManifestPath { get; set; } = "";
}

public static class ManifestCheck
{
    /// <summary>런처가 아는 형식. 서버가 더 큰 수를 주면 <b>런처를 갱신해야 한다.</b></summary>
    public const int SupportedSchema = 1;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 매니페스트의 상대 경로가 게임 폴더 안에 머무는지 본다.
    ///
    /// <para>막는 것: <c>..</c> 로 올라가기 · 절대 경로(<c>/x</c>) · 드라이브
    /// 지정(<c>C:\x</c>) · UNC(<c>\\서버\공유</c>) · 백슬래시 섞기 · 예약어.
    /// 하나라도 통과시키면 런처가 시스템 파일을 덮어쓸 수 있다.</para>
    /// </summary>
    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        // 매니페스트는 항상 `/` 로 적는다(만드는 쪽이 고정한다). 백슬래시가 오면
        // 만든 쪽이 규칙을 어긴 것이므로 받아들이지 않는다 — 섞이면 같은 파일을
        // 다른 것으로 보고 매번 다시 받는 문제도 생긴다.
        if (path.Contains('\\')) return false;
        if (path.StartsWith('/')) return false;
        if (path.Contains(':')) return false;                 // C: · UNC 흉내
        if (path.Contains("//")) return false;
        foreach (var seg in path.Split('/'))
        {
            if (seg.Length == 0) return false;                 // 빈 칸 = `a//b` 나 끝 슬래시
            if (seg == "." || seg == "..") return false;
            if (seg != seg.Trim()) return false;               // 앞뒤 공백은 윈도우가 조용히 떼어 낸다
            if (seg.EndsWith('.')) return false;               // 윈도우가 떼어 내 다른 파일이 된다
            if (seg.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        }
        return true;
    }

    /// <summary>sha256 16진수 64자인지.</summary>
    public static bool IsSha256(string s)
    {
        if (s is null || s.Length != 64) return false;
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }

    /// <summary>
    /// 쓰기 전에 매니페스트 전체를 검사한다. 문제가 있으면 <b>아무 파일도 건드리지 않고</b>
    /// 사유를 돌려준다 — 반쯤 적용된 상태가 가장 고치기 어렵다.
    /// </summary>
    public static string? Validate(Manifest m)
    {
        // **먼저 null 을 막는다.** 서버가 `"files": null` 이나 `"exe": null` 을 주면
        // 역직렬화가 프로퍼티를 null 로 두고, 아래에서 NullReferenceException 이
        // 난다. 이 함수를 부르는 자리는 try 블록 밖이라 **런처가 그대로 죽는다** —
        // "서버 값을 못 믿는다" 는 이 파일의 전제와 정면으로 어긋난다.
        // (`!364` AI 리뷰가 잡았다.)
        if (m.Files is null) return "파일 목록이 null 이다";
        if (m.Exe is null) return "실행 파일 이름이 null 이다";
        if (m.Version is null) return "버전이 null 이다";
        if (m.Schema > SupportedSchema)
            return $"서버 매니페스트 형식이 {m.Schema} 인데 이 런처는 {SupportedSchema} 까지 안다 — 런처를 새로 받아야 한다";
        if (m.Files.Count == 0) return "파일 목록이 비어 있다";
        if (!IsSafeRelativePath(m.Exe) || !m.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return $"실행 파일 이름이 이상하다: {m.Exe}";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in m.Files)
        {
            if (!IsSafeRelativePath(f.Path)) return $"경로가 게임 폴더를 벗어난다: {f.Path}";
            if (!IsSha256(f.Sha256)) return $"해시가 sha256 이 아니다: {f.Path}";
            if (f.Size < 0) return $"크기가 음수다: {f.Path}";
            // 윈도우 파일 이름은 대소문자를 구분하지 않는다. 목록에 `A.dll` 과
            // `a.dll` 이 같이 있으면 하나가 다른 하나를 덮어써서 **어느 쪽이 남는지
            // 알 수 없다.** 그 상태로 해시를 확인하면 매번 다시 받는다.
            if (!seen.Add(f.Path)) return $"대소문자만 다른 경로가 겹친다: {f.Path}";
        }
        if (!m.Files.Any(f => f.Path.Equals(m.Exe, StringComparison.OrdinalIgnoreCase)))
            return $"실행 파일이 목록에 없다: {m.Exe}";
        return null;
    }
}
