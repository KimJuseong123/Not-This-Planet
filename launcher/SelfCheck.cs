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

        fail += CheckStale();

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

        // ── ⑥ 개발 모드 인자 판정 ────────────────────────────────────────
        // `--dev` 를 못 알아보면 **개발 모드가 배포 폴더를 덮어쓰고 실유저 서버를
        // 가리킨다** — exe 를 하나로 둔 대가가 여기 걸린다(-406).
        if (!DevMode.IsOn(new[] { "--dev" })) { Console.WriteLine("  ❌ --dev 를 못 알아본다"); fail++; }
        if (!DevMode.IsOn(new[] { "--no-launch", "--dev" })) { Console.WriteLine("  ❌ --dev 가 뒤에 오면 못 알아본다"); fail++; }
        if (DevMode.IsOn(Array.Empty<string>())) { Console.WriteLine("  ❌ 인자가 없는데 개발 모드다"); fail++; }
        if (DevMode.IsOn(new[] { "--devil" })) { Console.WriteLine("  ❌ --devil 을 --dev 로 봤다"); fail++; }

        if (DevMode.RequestedVersion(new[] { "--dev", "--version", "v1.2.3" }) != "v1.2.3")
        { Console.WriteLine("  ❌ --version 값을 못 읽는다"); fail++; }
        if (DevMode.RequestedVersion(new[] { "--dev" }) is not null)
        { Console.WriteLine("  ❌ --version 이 없는데 값을 읽었다"); fail++; }
        // 값이 빠진 채 끝나면 **뒤 인덱스를 읽어 터지면 안 된다**
        if (DevMode.RequestedVersion(new[] { "--version" }) is not null)
        { Console.WriteLine("  ❌ --version 뒤가 비었는데 값을 읽었다"); fail++; }

        // 두 폴더가 달라야 한다 — 같으면 개발 설치가 배포 설치를 덮는다.
        // 상수끼리 견주면 컴파일러가 접어 버려 검사가 죽는다(CS0162). 그래서
        // 배포 쪽 이름을 프로퍼티로 받아 **실제로** 견준다.
        if (string.Equals(Program.ProdGameDirName, DevMode.GameDirName,
                          StringComparison.OrdinalIgnoreCase))
        { Console.WriteLine("  ❌ 개발 모드가 배포용과 같은 폴더를 쓴다"); fail++; }
        // 개발 백엔드가 `/dev` 를 가리켜야 한다 — 아니면 실유저 데이터를 건드린다.
        if (!DevMode.DevBackend.EndsWith("/dev"))
        { Console.WriteLine($"  ❌ 개발 백엔드가 /dev 가 아니다: {DevMode.DevBackend}"); fail++; }

        // 목록에 없는 버전을 달라 하면 **멈춰야 한다.** 조용히 최신으로 바꿔 주면
        // "옛 버전으로 재현해 본다" 는 목적이 사라진다.
        var list = new VersionList
        {
            Schema = 1,
            Versions = new()
            {
                new() { Version = "v1.0.2", Files = 3, TotalBytes = 3 },
                new() { Version = "v1.0.1", Files = 3, TotalBytes = 3 },
            },
        };
        if (DevMode.Choose(list, new[] { "--dev", "--version", "v9.9.9" }) is not null)
        { Console.WriteLine("  ❌ 없는 버전을 달라 했는데 무언가를 골랐다"); fail++; }
        if (DevMode.Choose(list, new[] { "--dev", "--version", "v1.0.1" }) != "v1.0.1")
        { Console.WriteLine("  ❌ 있는 버전을 고르지 못했다"); fail++; }
        if (DevMode.Choose(new VersionList { Schema = 1 }, new[] { "--dev" }) is not null)
        { Console.WriteLine("  ❌ 빈 목록인데 무언가를 골랐다"); fail++; }

        // ── ⑦ 채널 (S15P21B202-460) ──────────────────────────────────────
        //
        // 개발 목록(dev·qa)과 배포 목록을 합쳐 고른다. **개발 목록이 앞**이라야
        // Enter(= 최신)가 "방금 develop" 을 고른다 — 개발 모드에서 가장 자주 쓰는 것이다.
        var mixed = new VersionList
        {
            Schema = 1,
            Versions = new()
            {
                new() { Version = "dev-20260915-2b3c4d5", Channel = "dev",
                        Branch = "develop", Commit = "2b3c4d5", Files = 3, TotalBytes = 3 },
                new() { Version = "qa-401-3c4d5e6", Channel = "qa",
                        Branch = "S15P21B202-401-x", Commit = "3c4d5e6", Files = 3, TotalBytes = 3 },
                new() { Version = "v1.0.0", Channel = "release", Files = 3, TotalBytes = 3 },
            },
        };
        // 릴리스도 **같은 화면에서** 고를 수 있어야 한다 — "이 버그가 v1.0.0 에도 있었나".
        if (DevMode.Choose(mixed, new[] { "--dev", "--version", "v1.0.0" }) != "v1.0.0")
        { Console.WriteLine("  ❌ 섞인 목록에서 릴리스를 고르지 못했다"); fail++; }
        if (DevMode.Choose(mixed, new[] { "--dev", "--version", "qa-401-3c4d5e6" }) != "qa-401-3c4d5e6")
        { Console.WriteLine("  ❌ 섞인 목록에서 qa 빌드를 고르지 못했다"); fail++; }

        // 목록에 **무엇으로 만든 빌드인지**가 보여야 한다. 이것이 비면 버전 문자열만
        // 남아서 "이거 언제 코드예요?" 를 매번 사람에게 물어야 한다.
        if (mixed.Versions[0].Origin != "develop @ 2b3c4d5")
        { Console.WriteLine($"  ❌ dev 빌드의 출처 표시가 이상하다: {mixed.Versions[0].Origin}"); fail++; }
        if (mixed.Versions[2].Origin != "릴리스")
        { Console.WriteLine($"  ❌ 릴리스의 출처 표시가 이상하다: {mixed.Versions[2].Origin}"); fail++; }
        // 옛 목록(채널·브랜치가 없던 것)도 그대로 읽혀야 한다 — 서버가 먼저 바뀌지 않는다.
        if (new VersionList.Entry { Version = "v1.0.0" }.Origin != "릴리스")
        { Console.WriteLine("  ❌ 옛 목록 항목의 출처 표시가 깨졌다"); fail++; }

        // **배포 런처가 보는 파일과 개발 목록이 달라야 한다.** 같아지면 일반 사용자에게
        // dev 빌드가 보이고, 그것을 받아 가면 우리가 모르는 조합이 생긴다.
        if (DevMode.DevListName == DevMode.ReleaseListName)
        { Console.WriteLine("  ❌ 개발 목록과 배포 목록이 같은 파일이다"); fail++; }

        Console.WriteLine($"  (이 런처: {SelfUpdate.MyVersion})");
        Console.WriteLine(fail == 0
            ? "✅ 자체 검사 통과 — 경로·해시·매니페스트·버전·개발 모드·채널 판정"
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

    /// <summary>
    /// <b>런처는 자기가 깐 것만 지운다</b> (S15P21B202-507).
    ///
    /// <para>2026-09-14 실측: 런처가 켜지며 게임이 만든 <c>logs/</c> 의 플레이 로그
    /// 3개를 지웠다. 업로드는 <b>다음 실행의 로그인 때</b> 일어나는데, 그 다음 실행을
    /// 런처로 켜니 직전 로그가 이미 없었다 — 배포판 사용자는 전부 런처로 켠다.</para>
    /// </summary>
    static int CheckStale()
    {
        int fail = 0;
        var wanted = new HashSet<string>(
            new[] { "NotThisPlanet.exe", "NotThisPlanet_Data/app.dat" },
            StringComparer.OrdinalIgnoreCase);

        // ① 기록이 없으면 **아무것도 안 지운다**
        if (Program.StaleFiles(null, wanted).Count != 0)
        {
            Console.WriteLine("  ❌ 설치 기록이 없는데 지우려 한다");
            fail++;
        }

        // ② 옛 버전이 깐 것은 **여전히 지운다** — 이 기능을 죽이면 안 된다
        var installed = new List<string>
        {
            "NotThisPlanet.exe",
            "NotThisPlanet_Data/app.dat",
            "NotThisPlanet_Data/옛것.dat",
        };
        var stale = Program.StaleFiles(installed, wanted);
        if (stale.Count != 1 || stale[0] != "NotThisPlanet_Data/옛것.dat")
        {
            Console.WriteLine($"  ❌ 옛 파일을 못 지운다: [{string.Join(", ", stale)}]");
            fail++;
        }

        // ③ **게임이 만든 것은 기록에 없으므로 어떤 경우에도 안 지워진다**
        //    기록은 매니페스트 경로만 담는다 — 그것이 이 설계의 핵심이다.
        foreach (var mine in new[] { "logs/20260914-093329.log", "logs/오늘.log" })
        {
            if (Program.StaleFiles(installed, wanted).Contains(mine))
            {
                Console.WriteLine($"  ❌ 게임이 만든 파일을 지우려 한다: {mine}");
                fail++;
            }
        }

        if (fail == 0) Console.WriteLine("  ✅ 지우기 — 우리가 깐 것만 (기록 없으면 안 지움)");
        return fail;
    }
}
