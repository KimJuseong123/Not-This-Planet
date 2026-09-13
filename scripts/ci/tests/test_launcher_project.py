"""런처를 빌드할 수 있는 상태인지 (S15P21B202-475 · -406).

실행: python scripts/ci/tests/test_launcher_project.py
네트워크·토큰 없이 저장소의 파일과 `git` 만 읽는다.

**무엇을 막는 검증인가.** `launcher/NotThisPlanetLauncher.csproj` 는 **한 번도 커밋된
적이 없었다.** 저장소 루트 `.gitignore` 의 `*.csproj` — Unity 가 다시 만드는 프로젝트
파일을 걸러내려던 규칙 — 이 런처 것까지 삼켰다. `git add` 가 조용히 건너뛰므로
아무도 빠진 것을 보지 못했다.

그 결과가 나쁜 이유는 **아무것도 빨갛지 않았다**는 것이다. CI 는 런처를 빌드하지
않는다(`launcher/README.md` — 릴리스마다 바뀌는 물건이 아니다). `launcher/README.md`
는 `cd launcher && dotnet publish` 를 하라고 적어 두었는데, 저장소를 받은 사람은
**그 명령을 실행할 수 없었다.** 만든 사람 PC 에만 있는 파일이라 그 PC 가 사라지면
런처를 다시 만들 방법이 없다.

그래서 여기서 보는 것은 "빌드가 되는가" 가 아니라 **"빌드에 필요한 것이 저장소에
있는가"** 다. `dotnet` 이 없는 러너에서도 돈다.

**한계 — ②-b 는 로컬 전용 신호다.** "디스크에 있는데 무시된다" 를 보는 검사는 CI
체크아웃에 추적된 파일만 있으므로 **거기서는 늘 통과한다.** 그 상태가 생기는 자리가
파일을 만드는 사람의 PC 라서 그렇게 뒀다. CI 로 이 사고를 막아 주지는 못한다.
"""
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.join(ROOT, "scripts", "launcher"))
os.chdir(ROOT)
sys.stdout.reconfigure(encoding="utf-8")

import publish_launcher as PL  # noqa: E402

CSPROJ = "launcher/NotThisPlanetLauncher.csproj"

fails = []
passes = 0


def check(ok, msg):
    global passes
    print(("  ✅ " if ok else "  ❌ ") + msg)
    if ok:
        passes += 1
    else:
        fails.append(msg)


def git(*args):
    """(exit, stdout). git 이 없으면 예외가 나게 둔다 — 조용히 건너뛰면 검사가 아니다."""
    r = subprocess.run(["git"] + list(args), cwd=ROOT, capture_output=True,
                       text=True, encoding="utf-8", errors="replace")
    return r.returncode, r.stdout


def ignored(path):
    """이 경로가 무시되는가.

    `git check-ignore` 는 **무시 0 · 아님 1 · 오류 128** 이다. `rc == 0` 만 보고 나머지를
    "무시 아님" 으로 묶으면 **오류가 통과로 둔갑한다** — 검사가 조용히 죽는 모양이다.
    """
    rc, _ = git("check-ignore", "-q", "--", path)
    if rc not in (0, 1):
        raise RuntimeError(f"git check-ignore 가 {rc} 로 끝났다: {path}")
    return rc == 0


def read(p):
    # `utf-8-sig` 로 연다. MSBuild 프로젝트 파일은 편집기를 거치면 BOM 이 붙는다.
    # (2026-09-13 실측: 진짜 BOM 을 붙여도 `ET.fromstring` 은 통과했다 — 지금 깨지는
    #  것은 아니다. 파서 동작에 기대는 대신 읽는 쪽에서 떼는 편이 읽기 쉽다.)
    with open(os.path.join(ROOT, p), encoding="utf-8-sig") as f:
        return f.read()


def localname(el):
    """`{네임스페이스}PropertyGroup` → `PropertyGroup`.

    옛 형식 csproj 에는 `xmlns` 가 붙는다. 그때 `findall("PropertyGroup")` 은 **하나도
    못 찾고 예외도 내지 않는다** — 모든 값이 빈 문자열이 되어 검사가 조용히 통과한다.
    """
    return el.tag.rpartition("}")[2]


def prop(root, name):
    """csproj 의 <name>값</name>. 없으면 빈 문자열.

    **글자로 긁지 않고 XML 로 읽는다.** 정규식은 주석 안의 값이나 `Condition` 이 붙은
    PropertyGroup 의 값도 똑같이 잡아서, **실제 빌드에 쓰이지 않는 값을 검사**할 수 있다.
    ElementTree 는 주석을 버리므로 그 함정이 사라진다. 조건이 붙지 않은 PropertyGroup 만
    보고, 같은 이름이 여럿이면 MSBuild 와 같이 **마지막 것**이 이긴다.
    """
    found = ""
    for group in root:
        if localname(group) != "PropertyGroup" or "Condition" in group.attrib:
            continue
        for el in group:
            if localname(el) == name:
                found = (el.text or "").strip()
    return found


def missing_hint(name):
    """값을 못 읽었을 때 붙일 한 줄.

    여기서는 `root` 의 **바로 아래** PropertyGroup 만 본다. 설정을 `Import`·`Choose` 로
    옮기면 값이 빈 문자열이 되어 검사가 실패하는데(막는 쪽이라 안전하다), 실패 메시지가
    "false 다" 뿐이면 왜 그런지 찾는 데 시간이 든다.
    """
    return f" — {name} 를 읽지 못했습니다(Import·Choose 로 옮겼거나 Condition 이 붙었나요?)"


print()
print("== 런처 프로젝트가 저장소에 있다 ==")

# ── ① csproj 가 추적된다 ────────────────────────────────────────────────
# 이것이 이 파일의 존재 이유다. 실패하면 아무도 런처를 빌드할 수 없다.
# `-z` 로 읽는다 — 기본 출력은 공백 포함 경로가 쪼개지고, 비ASCII 경로는 git 이
# 따옴표로 감싸 내보내 이름이 달라진다. 그러면 ②가 **엉뚱한 경로**를 검사한다.
tracked = set(p for p in git("-c", "core.quotepath=false", "ls-files", "-z",
                             "launcher/")[1].split("\0") if p)
check(CSPROJ in tracked, f"{CSPROJ} 이 git 에 있다")
if CSPROJ not in tracked:
    # **여기서 끝낸다.** 아래는 전부 그 파일을 읽는 검사라, 그냥 두면 FileNotFoundError
    # 스택트레이스가 나고 요약이 안 나온다 — 이 테스트가 있는 바로 그 상황에서.
    print()
    print(f"== 런처 프로젝트: 통과 {passes} · 실패 {len(fails)} ==")
    print("   ❌ " + " / ".join(fails))
    print("   → `.gitignore` 의 `!/launcher/*.csproj` 와 repo_hygiene.ALLOWED 를 함께 보세요")
    raise SystemExit(1)

# ── ② .gitignore 가 그것을 다시 삼키지 않는다 ────────────────────────────
# ①만 보면 늦다. 추적 중인 파일은 무시 규칙이 생겨도 계속 추적되므로, 규칙이 되살아난
# 사실은 **다음에 누가 이 파일을 지웠다가 다시 만들 때** 드러난다. 그때는 원인이
# 멀어진다. `check-ignore` 는 추적 여부와 무관하게 규칙만 보므로 지금 잡을 수 있다.
#
# 런처 소스 전체를 본다 — 삼킨 것이 `*.csproj` 하나뿐이라는 보장이 없다.
for path in sorted(tracked):
    check(not ignored(path), f"{path} 이 무시 대상이 아니다")

# ── ②-b 디스크에 있는데 추적되지 않는 소스 ──────────────────────────────
# ② 는 `ls-files` 를 돌므로 **이미 추적된 것만** 본다. 정작 이 파일이 생긴 사고는
# "파일이 디스크에 있는데 추적되지 않았다" 였다 — ② 로는 그 상태를 못 잡는다.
# 디스크 쪽에서 한 번 더 본다. CI 체크아웃에는 추적된 것만 있으므로 여기서는 조용하고,
# **파일을 만드는 로컬에서** 걸린다. 그 자리가 잡아야 할 자리다.
#
# **빌드에 들어가는 확장자만** 본다. 전부 보면 로컬 메모 파일 하나에도 빨개지고,
# 그렇게 자주 틀리는 검사는 사람들이 곧 무시한다. 놓치는 것(추적 안 된 문서)보다
# 잡는 것(추적 안 된 소스·프로젝트 파일)이 훨씬 무겁다.
BUILD_INPUTS = (".cs", ".csproj", ".props", ".targets", ".resx", ".manifest")
disk = []
for base, dirs, names in os.walk(os.path.join(ROOT, "launcher")):
    dirs[:] = [d for d in dirs if d not in ("bin", "obj")]      # 빌드 산출물은 뺀다
    for n in names:
        if not n.endswith(BUILD_INPUTS):
            continue
        rel = os.path.relpath(os.path.join(base, n), ROOT).replace(os.sep, "/")
        disk.append(rel)
# **무시되어 사라지는 것만** 실패로 본다. 추적되지 않은 것을 전부 걸면, 런처 코드를
# 쓰는 도중 아직 `git add` 안 한 새 `.cs` 하나에 스위트가 빨개진다 — 늘 빨간 검사는
# 곧 무시당하고, 그러면 정작 이 검사가 잡으려던 것을 못 잡는다. 사고의 본질은
# "추적이 안 됐다" 가 아니라 **"무시돼서 조용히 사라진다"** 였다.
missing = sorted(p for p in set(disk) - tracked if ignored(p))
check(not missing, "디스크의 런처 빌드 입력이 무시되지 않는다"
      + (f" — 빠진 것: {', '.join(missing)}" if missing else ""))

root = ET.fromstring(read(CSPROJ))

print()
print("== 빌드 설정이 다른 곳과 같은 것을 가리킨다 ==")

# ── ③ 나오는 exe 이름 ───────────────────────────────────────────────────
# 사이트·nginx 허용 목록·publish_launcher 가 같은 이름을 적고 있다(test_site_release_config).
# 그 셋이 서로 맞아도 **빌드가 다른 이름을 뱉으면** 올릴 파일이 없다.
assembly = prop(root, "AssemblyName")
want = os.path.splitext(PL.PUBLIC_NAME)[0]
check(assembly == want, f"AssemblyName {assembly!r} == {want!r} (publish_launcher.PUBLIC_NAME)")

# ── ④ README 가 적은 publish 경로가 실제 조합과 같다 ─────────────────────
# 사람은 README 의 명령을 복사해 붙인다. `bin/Release/<tfm>/<rid>/publish/` 는 이 두 값이
# 만드는 경로인데, 둘 중 하나를 올리면 README 의 경로가 **없는 폴더**가 된다.
tfm = prop(root, "TargetFramework")
rid = prop(root, "RuntimeIdentifier")
readme = read("launcher/README.md")
expected = f"bin/Release/{tfm}/{rid}/publish/"
check(bool(tfm) and bool(rid), f"TargetFramework={tfm!r} RuntimeIdentifier={rid!r} 이 비지 않았다")
check(expected in readme, f"launcher/README.md 가 {expected} 를 적는다")

print()
print("== 조용히 깨지는 조합을 막는다 ==")

# ── ⑤ 트리밍을 켰으면 리플렉션 JSON 은 꺼져 있어야 한다 ──────────────────
# 트리머는 리플렉션으로만 닿는 프로퍼티를 지운다. 지워도 **빌드는 통과하고 실행만
# 조용히 깨진다** — 런처가 서버 JSON 을 못 읽고, 사용자는 원인을 알 수 없다.
# `JsonSerializerIsReflectionEnabledByDefault=false` 가 그것을 컴파일 시점 경고로 끌어올린다.
# **분기를 두지 않는다.** 예전에는 "PublishTrimmed 이 꺼져 있으면 볼 것이 없다" 로
# 빠졌는데, 값을 못 읽었을 때도 똑같이 그 길로 가서 **조용히 통과**한다. 런처는 트리밍을
# 켠 채로 배포하는 것이 전제(70.2 MB → 14.4 MB)이므로 그냥 켜져 있기를 요구한다.
for key, want in (("PublishTrimmed", "true"),
                  ("JsonSerializerIsReflectionEnabledByDefault", "false")):
    got = prop(root, key)
    check(got.lower() == want, f"{key}={want}" + (missing_hint(key) if not got else ""))

# ── ⑥ 단일 파일 + self-contained ────────────────────────────────────────
# 받는 사람에게 ".NET 을 먼저 설치하세요" 를 요구하면 런처가 없애려던 수동 절차가 남는다.
check(prop(root, "SelfContained").lower() == "true", "SelfContained=true")
check(prop(root, "PublishSingleFile").lower() == "true", "PublishSingleFile=true")

# ── ⑦ 버전을 빠뜨리는 실수를 빌드가 막는다 ───────────────────────────
# 예전에는 "x.y.z 형식인가" 만 봤는데, **그것이 바로 문제였다.** 기본값이 `1.0.0` 이면
# 형식은 맞고 `-p:Version=` 을 빠뜨려도 검사가 통과한다. 그렇게 나온 exe 를 v1.0.1 로
# 올리면 서버는 1.0.1 이라 하고 exe 는 1.0.0 이라 해서, 받는 사람이 **끝나지 않는 자기
# 갱신**에 빠진다(2026-09-13 실측: 인자 없이 publish 하면 v1.0.0 이 나왔다).
#
# 그래서 보는 것을 바꿨다 — 기본값이 **그럴듯하지 않은 표시값**인지, 그리고 그 표시값인
# 채로 릴리스 publish 를 하면 **빌드가 서는지**.
DEV_VERSION = "0.0.0-dev"
check(prop(root, "Version") == DEV_VERSION,
      f"Version 기본값이 {DEV_VERSION!r} — 진짜 같은 수를 기본값으로 두지 않는다")

guard = None
for el in root:
    if localname(el) == "Target" and el.attrib.get("Name") == "RequireExplicitVersionOnRelease":
        guard = el
cond = (guard.attrib.get("Condition", "") if guard is not None else "")
check(guard is not None, "릴리스 버전 누락을 막는 Target 이 있다")
check(DEV_VERSION in cond and "Release" in cond,
      "그 Target 이 Release + 기본값일 때만 선다 (개발 빌드는 막지 않는다)")
check(guard is not None and any(localname(c) == "Error" for c in guard),
      "그 Target 이 경고가 아니라 Error 를 낸다")

print()
print("== repo-hygiene 도 이 파일을 막지 않는다 ==")

# ── ⑧ 같은 가정이 두 곳에 있었다 ────────────────────────────────────────
# `.gitignore` 만 풀면 `git add` 는 되지만 CI 가 거절한다 — `repo_hygiene.FORBIDDEN` 의
# `.csproj` 규칙도 "csproj 는 Unity 가 만든 것" 을 전제한다. 2026-09-13 파이프라인
# 191565 에서 실제로 그렇게 막혔다. 그래서 `ALLOWED` 예외를 뒀고, **그 예외가 좁은지**를
# 여기서 고정한다 — 넓은 예외는 위생 검사를 조용히 무력화한다.
sys.path.insert(0, os.path.join(ROOT, "scripts", "ci"))
import repo_hygiene as RH  # noqa: E402


def forbidden(path):
    """repo_hygiene 가 이 경로를 막는가.

    **판정을 다시 짜지 않고 그쪽 함수를 부른다.** 여기서 ALLOWED/FORBIDDEN 을 직접
    조합하면 repo_hygiene 쪽 순서가 바뀌었을 때 이 테스트는 초록인 채 CI 만 막는다.
    """
    return bool(RH.forbidden_reasons(path))


check(not forbidden(CSPROJ), f"{CSPROJ} 을 막지 않는다")

# 예외는 **면제할 사유를 이름으로 가리킨다.** 그 이름이 FORBIDDEN 에 없으면 예외는
# 아무것도 면제하지 않는 죽은 줄이 되는데, 겉보기에는 살아 있어 보인다.
reasons = {why for _, why in RH.FORBIDDEN}
for _, exempt in RH.ALLOWED:
    check(exempt in reasons, f"예외가 가리키는 사유 {exempt!r} 가 FORBIDDEN 에 실재한다")
for blocked in ("METAGAME/Assembly-CSharp.csproj",      # Unity 가 다시 만드는 것 — 원래 목적
                "launcher/sub/Nested.csproj",           # 하위 폴더까지 열지 않는다
                "launcher/NotThisPlanetLauncher.sln",   # 확장자를 못 박았다
                "launcher/bin/Release/NotThisPlanetLauncher.exe"):
    check(forbidden(blocked), f"{blocked} 은 그대로 막는다")

print()
print(f"== 런처 프로젝트: 통과 {passes} · 실패 {len(fails)} ==")
if fails:
    print("   ❌ " + " / ".join(fails))
raise SystemExit(1 if fails else 0)
