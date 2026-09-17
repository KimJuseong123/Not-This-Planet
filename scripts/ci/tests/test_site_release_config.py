"""사이트가 실제로 무언가를 내려 주는지 (S15P21B202-452 · -406).

실행: python scripts/ci/tests/test_site_release_config.py
네트워크·토큰 없이 저장소의 파일을 글자로 읽는다.

**무엇을 막는 검증인가.** 2026-09-13 실측에서 **서버에는 값이 있는데 저장소는 비어
있었다**:

    서버   downloadUrl: "/builds/metagame-v1.0.0-20260911-1512.zip"
    저장소 downloadUrl: ""

`publish-site` 는 저장소를 서버로 복사한다. 그러니 다음 사이트 배포가 **다운로드
버튼을 조용히 꺼 버린다.** 사이트는 초록이고, 페이지도 열리고, 버튼만 "다운로드 준비 중"
으로 돌아간다 — 아무도 실패를 보지 못한다.

**그리고 세 곳이 같은 이름을 봐야 한다.** 런처를 내려 주는 자리는 이름이 고정이고,
그 이름을 **사이트·nginx·발행 스크립트** 셋이 각각 적고 있다. 하나만 바꾸면 404 가 되고,
그 404 는 사용자 쪽에서만 보인다.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.join(ROOT, "scripts", "launcher"))
os.chdir(ROOT)
sys.stdout.reconfigure(encoding="utf-8")

import publish_launcher as PL  # noqa: E402


def read(p):
    with open(p, encoding="utf-8") as f:
        return f.read()


CONFIG = read("website/release-config.js")
NGINX = read("scripts/deploy/server/nginx/metagame.conf")

print()
print("== 사이트 다운로드 ==")

# ── ① 다운로드 주소가 비어 있지 않다 ────────────────────────────────────
#
#     이것이 비면 버튼이 꺼진다. 그 상태로 배포되면 사이트는 멀쩡해 보이고
#     **받을 수만 없다.**
m = re.search(r'downloadUrl\s*:\s*"([^"]*)"', CONFIG)
assert m, "release-config.js 에서 downloadUrl 을 찾지 못했다"
url = m.group(1).strip()
assert url, ("downloadUrl 이 비어 있다 — 이대로 배포하면 다운로드 버튼이 꺼진다. "
             "2026-09-13 에 실제로 그 상태였고, 서버에만 값이 있었다")

#     상대 주소여야 한다. EC2 는 회수되므로 도메인을 박으면 그날 전부 고쳐야 한다.
assert url.startswith("/builds/"), f"다운로드 주소가 /builds/ 로 시작하지 않는다: {url!r}"

#     **토큰이 든 주소·만료 링크를 넣지 않는다** (사이트 README 규칙).
for bad in ("?token=", "private_token", "sig=", "Expires=", "X-Amz-"):
    assert bad not in url, f"다운로드 주소에 인증·만료 흔적이 있다: {bad}"
print("  다운로드 주소가 채워져 있고 상대 주소다 ✅")

print()
print("== 버전과 바뀐 것 (S15P21B202-595) ==")

# ── ② 버전을 손으로 적지 않는다 ─────────────────────────────────────────
#
#     전에는 `version` 이 이 파일에 있었고 릴리스마다 사람이 올려야 했다. 그 일이
#     어디에도 적혀 있지 않아 **v1.1.0 이 나간 지 사흘이 지나도록 사이트는
#     v1.0.0 이라고 말하고 있었다.** 이제 `release.js` 가 `/builds/latest.json` 을
#     읽는다 — `release-prod` 가 옮기는 바로 그 파일이다.
assert not re.search(r'(?:^|[{,])\s*version\s*:', CONFIG, re.M), (
    "release-config.js 에 version 이 되살아났다 — 손으로 적으면 릴리스와 어긋난다. "
    "release.js 가 /builds/latest.json 을 읽는다")

JS = read("website/release.js")
assert "/builds/latest.json" in JS, "release.js 가 latest.json 을 읽지 않는다 — 버전을 알 길이 없다"
#     못 읽어도 받는 길은 살아 있어야 한다. 서버가 잠깐 안 되는 것으로 버튼이 꺼지면 안 된다.
#     **그 fetch 블록 안에서** 본다. 파일 어디든 `.catch(` 가 하나 있으면
#     통과하게 두면, 나중에 이 체인의 catch 만 지워도 검사가 조용히 넘어간다.
_after = JS.split("'/builds/latest.json'", 1)[1][:900]
assert ".catch(" in _after, (
    "latest.json fetch 체인이 실패를 삼키지 않는다 — 버전 한 줄 때문에 화면이 깨진다")
print("  버전은 latest.json 에서 온다 ✅")

# ── ③ 사용자용 문구에 이슈·MR 번호가 없다 ───────────────────────────────
#
#     플레이어에게 `S15P21B202-440` 은 뜻이 없다. 릴리스 초안을 사람이 옮겨 넣는
#     자리라 그대로 붙여 넣기 쉽다 — `release_summary.leaks` 와 같은 규칙으로 막는다.
KEY_IN_TEXT = re.compile(r"\bS15P21B202[- ]?\d+\b|\B!\d{2,}\b")
#     **항목별로 본다.** 정규식으로 배열을 잘라 내면 항목 문자열 안의 `]` 에서 끊겨
#     그 뒤 줄의 유출을 놓친다(`!503` 리뷰). 따옴표 안팎을 세며 배열 끝을 찾는다.
def notes_items(config):
    """`notes: [...]` 안의 문자열 항목들. 배열을 못 찾으면 None."""
    at = config.find("notes")
    if at < 0:
        return None
    open_at = config.find("[", at)
    if open_at < 0:
        return None
    depth, quote, items, buf = 0, "", [], None
    for ch in config[open_at:]:
        if quote:
            if ch == quote:
                quote = ""
                items.append(buf)
                buf = None
            else:
                buf += ch
            continue
        if ch in "\"'":
            quote, buf = ch, ""
            continue
        if ch == "[":
            depth += 1
        elif ch == "]":
            depth -= 1
            if depth == 0:
                return items
    return None


items = notes_items(CONFIG)
assert items is not None, "release-config.js 의 notes 배열을 읽지 못했다 — 실을 자리가 사라졌다"
leaked = [t for t in items if KEY_IN_TEXT.search(t)]
assert not leaked, "notes 에 이슈·MR 번호가 남아 있다: " + " / ".join(leaked[:3])

#     비어 있어도 된다 — 그때는 그 절이 화면에 안 나온다. 릴리스 사이에는 비는 것이 정상이다.
IDX = read("website/index.html")
assert 'id="notes"' in IDX and "hidden" in IDX.split('id="notes"')[1][:200], (
    "바뀐 것 절이 기본으로 숨겨져 있지 않다 — 비면 빈 제목만 남아 고장으로 보인다")
print("  바뀐 것 절이 있고, 번호가 새지 않는다 ✅")


# ── ② 세 곳이 같은 이름을 본다 ──────────────────────────────────────────
#
#     사이트가 가리키는 파일 · 발행 스크립트가 놓는 파일 · nginx 가 허용하는 파일.
#     하나만 어긋나면 사용자에게만 보이는 404 가 된다.
name = url.rsplit("/", 1)[-1]
assert name == PL.PUBLIC_NAME, \
    f"사이트가 가리키는 이름과 publish_launcher.PUBLIC_NAME 이 다르다: {name!r} vs {PL.PUBLIC_NAME!r}"

allow = next((l for l in NGINX.splitlines() if l.strip().startswith("location ~ ^/builds/")), "")
assert allow, "nginx 에 /builds/ 허용 목록(정규식 location)이 없다"
#     정규식이라 `.` 이 `\.` 로 이스케이프돼 있다. 역슬래시를 걷어 내고 이름만 본다.
flat = allow.replace("\\", "")
assert PL.PUBLIC_NAME in flat, \
    f"nginx 허용 목록에 {PL.PUBLIC_NAME} 이 없다 — 사이트 버튼이 404 가 된다: {allow}"
print(f"  사이트 · publish_launcher · nginx 가 모두 {PL.PUBLIC_NAME} ✅")


# ── ③ 그 밖의 것은 여전히 막혀 있다 ─────────────────────────────────────
#
#     `-465` 에서 저장소가 공개 폴더로 새어 나간 뒤 만든 방어선이다. exe 하나를
#     열면서 그것을 넓히지 않았는지 본다.
assert "[^/]+.exe" not in flat, \
    "아무 exe 나 열었다 — 이름을 그대로 적어야 한다(그 폴더에 exe 는 하나뿐이다)"
prefix = NGINX.split("location /builds/", 1)[1].split("}", 1)[0]
assert "return 404" in prefix and "alias" not in prefix, \
    "허용 목록에 안 걸린 요청이 여전히 서빙된다 — 여는 목록이 아무것도 막지 못한다"
print("  허용 목록 밖은 여전히 404 ✅")


# ── ④ 발행 스크립트가 실제로 그 자리에 놓는다 ───────────────────────────
#
#     이름만 맞고 파일을 안 놓으면 같은 404 다.
src = read("scripts/launcher/publish_launcher.py")
body = src.split("def main(", 1)[1]
assert "PUBLIC_NAME" in body, "발행 스크립트가 사람이 받는 자리에 아무것도 놓지 않는다"
assert "os.replace(" in body, \
    "원자적으로 바꾸지 않는다 — 받는 중인 사람이 반쯤 쓰인 파일을 받는다"

#     자기 갱신 경로(`files/<sha>.gz`)는 그대로 있어야 한다. 둘은 쓰임이 다르다.
assert '".gz"' in src or "'.gz'" in src, "런처 자기 갱신이 받는 자리가 사라졌다"
print("  발행 스크립트가 두 자리에 모두 놓는다 ✅")


# ── ⑤ 안내 문구가 zip 을 말하지 않는다 ──────────────────────────────────
#
#     주는 것이 런처인데 "압축을 푸세요" 라고 적혀 있으면, 받은 사람이 그 자리에서
#     멈춘다. 표시가 사실과 어긋나는 것은 이 저장소가 계속 고쳐 온 종류의 결함이다.
html = read("website/index.html")
js = read("website/release.js")
guide = html.split('id="guide"', 1)[1].split("</section>", 1)[0]
assert "압축" not in guide, "런처를 주는데 안내가 압축 해제를 말한다"
assert PL.PUBLIC_NAME in guide, f"안내에 {PL.PUBLIC_NAME} 이 없다"
assert "ZIP" not in js.split("downloadStatus", 1)[1][:200], \
    "버튼 아래 문구가 아직 ZIP 을 말한다"
print("  안내 문구가 런처를 말한다 ✅")


# ── ⑥ 받을 수 없는 사이트는 올리지 않는다 (`!390` 리뷰) ─────────────────
#
#     `downloadUrl` 이 아직 없는 파일을 가리키면, 올리는 순간 **버튼이 켜진 채 404**
#     가 된다. 꺼져 있으면 "아직 준비 중" 으로 읽히지만, **켜진 버튼이 실패하면 받는
#     사람은 자기 쪽 문제로 여긴다.** 런처 exe 는 CI 가 아니라 사람이 놓으므로
#     순서가 어긋날 여지가 실제로 있다.
import shutil  # noqa: E402
import tempfile  # noqa: E402

sys.path.insert(0, os.path.join(ROOT, "scripts", "ci"))
import publish_site as PS  # noqa: E402

with tempfile.TemporaryDirectory() as tmp:
    site, builds = os.path.join(tmp, "site"), os.path.join(tmp, "builds")
    os.makedirs(site)
    os.makedirs(builds)
    shutil.copy("website/release-config.js", os.path.join(site, "release-config.js"))

    assert PS.download_target(site) == PL.PUBLIC_NAME, PS.download_target(site)

    def cfg(text):
        with open(os.path.join(site, "release-config.js"), "w", encoding="utf-8") as f:
            f.write(text)
        return PS.download_target(site)

    #     **따옴표는 양쪽 다 받는다** (`!392` 리뷰). 표기 하나 때문에 검사가 꺼지면 안 된다.
    assert cfg("downloadUrl: " + "'" + "/builds/x.exe" + "'") == "x.exe"
    assert cfg("downloadUrl: " + '"' + "/builds/x.exe" + '"') == "x.exe"

    #     `/builds/` 밖·빈 값·설정 없음 — 볼 것이 없다. 빈 값은 **버튼이 꺼지는 것**이고
    #     그것은 정상이다(받을 수 없는데 받으라고 하지는 않는다).
    assert cfg('window.ReleaseConfig = { downloadUrl: "https://example.com/x.exe" };') is None
    assert cfg('window.ReleaseConfig = { downloadUrl: "" };') is None
    assert cfg('window.ReleaseConfig = {};') is None

    #     **못 읽은 것은 "볼 것 없음" 이 아니다** — 이것이 이 리뷰의 핵심이다.
    #     표기가 바뀌었는데 조용히 넘어가면, 켜진 404 가 그대로 배포된다.
    assert cfg("downloadUrl: `/builds/x.exe`") is PS.UNREADABLE, \
        "downloadUrl 이 있는데 못 읽었을 때 '볼 것 없음' 으로 떨어진다 — 검사가 조용히 꺼진다"

    #     **`/builds/` 만 있으면 가리키는 파일이 없다** (`!393` 리뷰). 예전에는 빈
    #     문자열이 돌아가 부르는 쪽의 `if want:` 에서 falsy 로 떨어져 **아무 검사도
    #     없이 배포됐다** — 이 MR 이 막으려던 바로 그 모양이 안쪽에 하나 더 있었다.
    assert cfg('downloadUrl: "/builds/"') is PS.UNREADABLE, \
        "/builds/ 만 적혀 있는데 검사 없이 통과한다"

    #     **센티널이 값 공간과 겹치면 안 된다** (`!393` 리뷰). 예전에는 `"?"` 였고,
    #     `downloadUrl` 이 `/builds/?` 면 잘라 낸 결과가 같아져 **멀쩡한 경로가
    #     '못 읽음' 으로 뒤집혔다.**
    assert cfg('downloadUrl: "/builds/?"') == "?", \
        "파일 이름이 센티널과 같다고 못 읽음으로 뒤집힌다"

    #     **키로 쓰인 때만** 못 읽음으로 본다 (`!393` 리뷰). 주석이나 다른 키 하나로
    #     배포가 멈추면, 막으려던 것(조용한 통과)이 아니라 엉뚱한 정지를 만든다.
    assert cfg("// downloadUrl 은 여기에 적는다") is None, \
        "주석에 이름만 있어도 배포를 멈춘다 — 오탐이다"
    assert cfg('downloadUrlLabel: "받기"') is None, \
        "다른 키 하나로 배포를 멈춘다 — 오탐이다"

    #     **주석 줄은 빼고 본다** (`!394` 리뷰). 옛 값을 `//` 로 남겨 두는 것은 흔한
    #     일인데, 그것이 진짜 키보다 위에 있으면 **옛 값을 검사 대상으로 잡는다** —
    #     이미 지운 파일을 찾다 배포가 서거나, 남아 있는 옛 파일을 보고 통과시킨다.
    assert cfg('// downloadUrl: "/builds/old.zip"\ndownloadUrl: "/builds/new.exe"') == "new.exe", \
        "주석에 적힌 옛 값을 먼저 잡는다"
    assert cfg('// downloadUrl: "/builds/old.zip"') is None, \
        "주석뿐인데 키가 있다고 본다"

    #     **블록 주석도 빼고 본다** (`!394` 2차 리뷰). 줄 앞만 보면 `/* … */` 안에서
    #     `*` 정렬 없이 적힌 줄이 살아남고, 반대로 살아 있는 값 줄 앞에 `*` 가 붙어
    #     있으면 키를 통째로 놓쳐 **검사가 꺼진다** — 막으려던 바로 그 모양이다.
    NEW = 'downloadUrl: "/builds/new.exe"'
    NL, BS = chr(10), chr(92)
    for name, text in (
        ("정렬 없는 블록 주석", '/*\ndownloadUrl: "/builds/old.zip"\n*/\n' + NEW),
        ("* 정렬 블록 주석", '/*\n * downloadUrl: "/builds/old.zip"\n */\n' + NEW),
        ("한 줄 블록 주석", '/* downloadUrl: "/builds/old.zip" */\n' + NEW),
        ("살아 있는 값 앞의 * 설명", ' * 설명\n' + NEW),
    ):
        assert cfg(text) == "new.exe", f"{name}: 살아 있는 값을 못 잡는다 ({cfg(text)!r})"

    #     **배포 폴더 밖을 가리키면 확인한 것이 아니다** (`!394` 2차 리뷰).
    #     `/builds/../../etc/passwd` 면 `os.path.join` 이 그대로 밖으로 나가고, 그 파일이
    #     있다는 이유로 **통과시킨다** — 사용자는 404 를 받는다. 손으로 적는 값이라
    #     실수로도 이렇게 된다. (`-467` 의 `PROMOTE_VERSION` 경로 탈출과 같은 종류다.)
    for bad in ("/builds/../../etc/passwd", "/builds/a/../../x",
                "/builds/..\\..\\x", "/builds/a//b"):
        assert cfg('downloadUrl: "%s"' % bad) is PS.UNREADABLE, \
            f"배포 폴더 밖을 가리키는데 통과한다: {bad}"

    #     하위 폴더는 정당하다 — `manifest/` 아래를 가리킬 수 있어야 한다
    assert cfg('downloadUrl: "/builds/manifest/v1.0.0.json"') == "manifest/v1.0.0.json"

    #     **줄 끝 주석도 뗀다** (`!394` 3차 리뷰). 줄 전체가 주석인 것만 걸러서는
    #     `version: "1.0" // 옛 downloadUrl: "…"` 의 옛 값이 살아남는다.
    assert cfg('version: "1.0" // downloadUrl: "/builds/old.zip"\n'
               'downloadUrl: "/builds/new.exe"') == "new.exe", \
        "코드 뒤에 붙은 주석의 옛 값을 잡는다"

    #     **그렇다고 `//` 를 통째로 지우면 `https://` 가 잘린다.** 그러면 외부 주소가
    #     깨진 값이 되어 **배포가 오탐으로 멈춘다** — 막으려던 것과 반대다.
    assert cfg('downloadUrl: "https://example.com/x.exe"') is None, \
        "외부 https 주소를 못 읽음으로 본다 — 주석 제거가 문자열 안까지 자른다"
    assert PS.strip_line_comment('downloadUrl: "https://a/b"') == 'downloadUrl: "https://a/b"'

    #     **줄 주석을 블록 주석보다 먼저 뗀다** (`!394` 4차 리뷰). 반대 순서면
    #     `// 옛 /* 값` 처럼 줄 주석 안에 `/*` 가 있을 때 그 뒤의 살아 있는 줄까지
    #     통째로 삼켜 **검사가 조용히 꺼진다.**
    assert cfg('// 옛 /* 값' + NL + NEW) == "new.exe", \
        "줄 주석 안의 /* 가 살아 있는 줄을 삼킨다"

    #     이스케이프된 따옴표를 닫는 것으로 보면 뒤가 주석으로 잘려 **오탐 정지**가 난다.
    assert cfg('note: "a' + BS + '"//b"' + NL + NEW) == "new.exe", \
        "이스케이프 따옴표 뒤를 주석으로 자른다"

    #     파일이 아예 없으면 None
    os.remove(os.path.join(site, "release-config.js"))
    assert PS.download_target(site) is None

src = read("scripts/ci/publish_site.py")
body = src.split("def main(", 1)[1]
assert "download_target" in body, "사이트 배치가 다운로드 주소를 확인하지 않는다"
assert body.index("download_target") < body.index("shutil.copyfile"), \
    "파일을 옮긴 뒤에 확인한다 — 그때는 이미 켜진 404 가 나가 있다"
assert "return 1" in body.split("download_target", 1)[1][:1200], \
    "없는 파일을 가리켜도 그냥 올린다"

#     **검사가 꺼질 수 있는 자리를 남기지 않는다** (`!392` 리뷰). 마운트가 빠진 때가
#     바로 켜진 404 가 나는 때인데, 예전에는 그때만 경고로 넘어갔다.
after = body.split("download_target", 1)[1]
assert "UNREADABLE" in after, "못 읽은 경우를 가르지 않는다"
mount = after.split("os.path.isdir(BUILDS)", 1)[1][:600]
assert "return 1" in mount, \
    "배포 폴더가 없을 때 경고만 하고 올린다 — 그때가 바로 검사가 필요한 때다"
print("  다운로드 대상이 없거나 확인할 수 없으면 올리지 않는다 ✅")


# ── ⑦ 런처 발행 순서 — launcher.json 이 먼저다 ──────────────────────────
#
#     공개 exe 를 먼저 놓고 `launcher.json` 쓰기가 실패하면, 사람은 새 exe 를 받는데
#     자기 갱신 메타데이터는 옛 버전이라 **어긋난 채 남는다.** 반대 순서면 사람이 받는
#     것만 옛 런처이고, 그 런처가 켜지자마자 스스로 갱신한다 — 저절로 낫는다.
pl = read("scripts/launcher/publish_launcher.py")
tail = pl.split("def main(", 1)[1]
assert tail.index("launcher.json → ") < tail.index("사람이 받는 자리 → "), \
    "공개 exe 를 launcher.json 보다 먼저 놓는다 — 실패하면 둘이 어긋난 채 남는다"
print("  launcher.json 을 쓴 뒤에 공개 exe 를 놓는다 ✅")

print("== 통과 ==")
