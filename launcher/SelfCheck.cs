namespace NotThisPlanet.Launcher;

/// <summary>
/// 런처의 **안전 판정**을 실행 가능한 검사로 묶는다 (S15P21B202-406).
///
/// <para><c>NotThisPlanetLauncher.exe --selfcheck</c> 로 돈다. 테스트 프레임워크를
/// 들이지 않은 이유: 런처는 단일 exe 로 배포되는 작은 물건이고, 테스트 프로젝트를
/// 더하면 배포 산출물과 빌드 절차가 둘로 갈린다. 대신 <b>배포되는 그 exe 안에서</b>
/// 검사가 돌게 해서, 손에 있는 파일이 곧 검사 대상이 되게 한다.</para>
///
/// <para><b>무엇을 막나.</b> <c>Manifest.path</c> 는 서버에서 받은 문자열이고 런처는
/// 그것으로 <b>파일을 쓴다.</b> 한 줄이 잘못 통과하면 게임 폴더 밖 — 최악의 경우
/// 시스템 폴더 — 에 파일이 생긴다. 만드는 쪽(<c>make_manifest.py</c>)도 같은 검사를
/// 하지만 <b>두 쪽 다</b> 해야 한다: 서버가 바뀔 수 있고, 중간에서 끼어들 수도 있다.</para>
/// </summary>
public static class SelfCheck
{
    public static int Run()
    {
        int fail = 0;

        // ── ① 통과해야 하는 것 ────────────────────────────────────────────
        foreach (var good in new[]
                 {
                     "NotThisPlanet.exe",
                     "NotThisPlanet_Data/Managed/Metagame.Gameplay.dll",
                     "MonoBleedingEdge/etc/mono/2.0/machine.config",
                     "a/b/c/d/e.dat",
                     "이름에 한글.dat",
                 })
        {
            if (!ManifestCheck.IsSafeRelativePath(good))
            {
                Console.WriteLine($"  ❌ 정상 경로를 거부했다: {good}");
                fail++;
            }
        }

        // ── ② 막아야 하는 것 ──────────────────────────────────────────────
        // 하나라도 통과하면 런처가 게임 폴더 밖에 쓴다.
        var evil = new (string Path, string Why)[]
        {
            ("../evil.dll", "상위로 올라간다"),
            ("a/../../evil.dll", "중간에서 올라간다"),
            ("/etc/passwd", "절대 경로"),
            ("C:/Windows/System32/evil.dll", "드라이브 지정"),
            ("C:\\Windows\\evil.dll", "드라이브 + 백슬래시"),
            ("\\\\서버\\공유\\evil.dll", "UNC 공유"),
            ("a\\b.dll", "백슬래시 — 매니페스트 규칙은 `/` 다"),
            ("a//b.dll", "빈 칸이 생긴다"),
            ("a/./b.dll", "현재 폴더 표기"),
            ("a/b/", "폴더로 끝난다"),
            ("", "빈 문자열"),
            ("   ", "공백뿐"),
            ("a/ b.dll", "앞 공백 — 윈도우가 떼어 낸다"),
            ("a/b.dll ", "뒤 공백 — 윈도우가 떼어 낸다"),
            ("a/b.", "점으로 끝난다 — 윈도우가 떼어 내 다른 파일이 된다"),
            ("a/b|c.dll", "파일 이름에 쓸 수 없는 글자"),
        };
        foreach (var (p, why) in evil)
        {
            if (ManifestCheck.IsSafeRelativePath(p))
            {
                Console.WriteLine($"  ❌ 위험한 경로를 통과시켰다: {p.Replace("\\", "\\\\")}  ({why})");
                fail++;
            }
        }

        // ── ③ 해시 모양 ───────────────────────────────────────────────────
        if (!ManifestCheck.IsSha256(new string('a', 64))) { Console.WriteLine("  ❌ 정상 sha256 거부"); fail++; }
        foreach (var bad in new[] { "", new string('a', 63), new string('a', 65),
                                    new string('A', 64), new string('g', 64), "0x" + new string('a', 62) })
        {
            if (ManifestCheck.IsSha256(bad)) { Console.WriteLine($"  ❌ sha256 이 아닌데 통과: {bad.Length}자"); fail++; }
        }

        // ── ④ 매니페스트 전체 검사 ────────────────────────────────────────
        var okManifest = new Manifest
        {
            Schema = 1, Version = "v1.0.0", Exe = "Game.exe", TotalBytes = 4,
            Files = new() { new() { Path = "Game.exe", Sha256 = new string('b', 64), Size = 4 } },
        };
        if (ManifestCheck.Validate(okManifest) is string why0)
        {
            Console.WriteLine($"  ❌ 정상 매니페스트를 거부했다: {why0}");
            fail++;
        }

        var cases = new (Func<Manifest> Make, string Label)[]
        {
            (() => { var m = Clone(okManifest); m.Schema = 99; return m; },
                "모르는 형식 — 런처를 갱신해야 한다"),
            (() => { var m = Clone(okManifest); m.Files.Clear(); return m; },
                "빈 목록"),
            (() => { var m = Clone(okManifest); m.Exe = "../Game.exe"; return m; },
                "실행 파일이 폴더 밖"),
            (() => { var m = Clone(okManifest); m.Exe = "Game.dll"; return m; },
                "실행 파일이 .exe 가 아니다"),
            (() => { var m = Clone(okManifest); m.Exe = "없는것.exe"; return m; },
                "실행 파일이 목록에 없다"),
            (() => { var m = Clone(okManifest); m.Files[0].Path = "../evil.dll"; m.Exe = "../evil.dll"; return m; },
                "경로가 폴더 밖"),
            (() => { var m = Clone(okManifest); m.Files[0].Sha256 = "짧다"; return m; },
                "해시가 sha256 이 아니다"),
            (() => { var m = Clone(okManifest); m.Files[0].Size = -1; return m; },
                "크기가 음수"),
            (() => { var m = Clone(okManifest);
                     m.Files.Add(new() { Path = "GAME.EXE", Sha256 = new string('c', 64), Size = 1 });
                     return m; },
                "대소문자만 다른 경로 — 윈도우에서 서로 덮어쓴다"),
        };
        foreach (var (make, label) in cases)
        {
            if (ManifestCheck.Validate(make()) is null)
            {
                Console.WriteLine($"  ❌ 걸러야 하는데 통과시켰다: {label}");
                fail++;
            }
        }

        // ── ⑤ 런처 버전 비교 ─────────────────────────────────────────────
        // 이 판정이 틀리면 **자체 갱신이 무한히 돌거나 아예 안 돈다.** 같으면
        // 0, 낮으면 음수, 높으면 양수여야 한다.
        foreach (var (a, b, want) in new (string, string, int)[]
                 {
                     ("v1.0.0", "v1.0.0", 0),
                     ("v1.0.0", "v1.0.1", -1),
                     ("v1.0.1", "v1.0.0", 1),
                     ("v1.0.0", "v1.1.0", -1),
                     ("v1.0.0", "v2.0.0", -1),
                     ("v1.9.0", "v1.10.0", -1),   // 문자열 비교면 틀리는 자리
                     ("v0.9.9", "v1.0.0", -1),
                     ("1.0.0", "v1.0.0", 0),      // `v` 가 없어도 같게 본다
                 })
        {
            var got = SelfUpdate.Compare(a, b);
            if (got is null || Math.Sign(got.Value) != want)
            {
                Console.WriteLine($"  ❌ 버전 비교가 틀렸다: {a} vs {b} → {got} (기대 부호 {want})");
                fail++;
            }
        }
        foreach (var bad in new[] { "", "v1", "v1.0", "v1.0.0.0", "vX.Y.Z", "v1.0.-1", "최신" })
        {
            if (SelfUpdate.Compare(bad, "v1.0.0") is not null)
            {
                Console.WriteLine($"  ❌ 버전 모양이 아닌데 견주었다: {bad}");
                fail++;
            }
        }
        // 내 버전이 견줄 수 있는 모양이어야 한다 — 아니면 자체 갱신이 늘 건너뛴다.
        if (SelfUpdate.Compare(SelfUpdate.MyVersion, "v1.0.0") is null)
        {
            Console.WriteLine($"  ❌ 내 버전을 견줄 수 없다: {SelfUpdate.MyVersion}");
            fail++;
        }

        Console.WriteLine($"  (이 런처: {SelfUpdate.MyVersion})");
        Console.WriteLine(fail == 0
            ? "✅ 자체 검사 통과 — 경로·해시·매니페스트·버전 판정"
            : $"❌ 자체 검사 실패 {fail}건");
        return fail == 0 ? 0 : 1;
    }

    static Manifest Clone(Manifest m) => new()
    {
        Schema = m.Schema, Version = m.Version, Exe = m.Exe, TotalBytes = m.TotalBytes,
        Files = m.Files.Select(f => new Manifest.FileEntry
        {
            Path = f.Path, Sha256 = f.Sha256, Size = f.Size, Gz = f.Gz,
        }).ToList(),
    };
}
