#!/usr/bin/env python3
"""배포 사이트를 nginx 가 내보내는 자리로 옮긴다 (S15P21B202-405 · -452).

    python scripts/ci/publish_site.py

**왜 릴리스에 묶지 않나.** 사이트는 릴리스와 **다른 박자**로 바뀐다 — `website/` 가
develop 에 머지될 때다. 릴리스 잡에 넣으면 사이트 오타 하나를 고치려고 릴리스를
내야 한다.

**LFS 포인터를 올리면 안 된다.** `website/assets/` 는 LFS 로 들고 있고, 포인터는
몇 줄짜리 텍스트다. 그것을 올리면 트레일러·GIF·이미지가 **전부 깨지는데 200 으로
내려간다** — 화면만 보고는 원인을 못 찾는다. 그래서 올리기 전에 실파일인지 본다.
"""
from __future__ import annotations

import os
import re
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from ci_common import env  # noqa: E402

SRC = "website"
# `/builds` 와 같은 이유로 `/publish/site` 에 건다 (S15P21B202-465) — 컨테이너가
# 이미 쓰는 자리에 공개 폴더를 물리면 그 폴더가 잡의 작업 공간이 된다.
DST = env("SITE_DIR", "/publish/site")

# LFS 포인터 파일의 첫 줄. 이것으로 시작하면 실파일이 아니다.
LFS_MARK = b"version https://git-lfs.github.com/spec/"

# 사이트에 올리지 않는 것. README 는 배포 담당자용 안내라 공개할 이유가 없다.
SKIP = {"README.md"}

# 다운로드 주소가 가리키는 파일이 실제로 있는지 볼 자리 (S15P21B202-452).
# 같은 배포 러너에 둘 다 마운트돼 있다 — `/publish/site` 와 `/publish/builds`.
BUILDS = env("BUILDS_DIR", "/publish/builds")
CONFIG = "release-config.js"


# 우리 폴더를 가리키지 않는다 — 볼 것이 없다(설정 없음·외부 주소·일부러 비움).
NOTHING_TO_CHECK = None

# **읽었는데 쓸 수 있는 값을 못 얻었다.** 위와 반드시 구별한다 (`!392` 리뷰).
#
# **값이 아니라 객체다** (`!393` 리뷰). 예전에는 `"?"` 였는데, `downloadUrl` 이
# `/builds/?` 이면 잘라 낸 결과가 같은 문자열이 되어 **멀쩡한 경로가 '못 읽음' 으로
# 뒤집힌다.** 센티널은 값 공간과 겹치면 안 된다.
UNREADABLE = object()

# 따옴표는 양쪽 다 받는다. 그래도 못 읽으면 **모른다고 말한다.**
URL_RE = re.compile(r"""\bdownloadUrl\s*:\s*(['"])(.*?)\1""")
# **키로 쓰인 때만** 못 읽음으로 본다 (`!393` 리뷰). 예전에는 글자만 있으면 됐는데,
# 그러면 주석(`// downloadUrl 은 …`)이나 `downloadUrlLabel` 같은 다른 키 하나로
# **배포가 멈춘다.** 막으려던 것은 조용한 통과이지 엉뚱한 정지가 아니다.
KEY_RE = re.compile(r"\bdownloadUrl\s*:")

BUILDS_PREFIX = "/builds/"


def strip_line_comment(line: str) -> str:
    """줄 끝 `//` 주석을 뗀다. **문자열 안의 `//` 는 건드리지 않는다.**

    줄 전체가 주석인 것만 걸러서는 부족하다 (`!394` 3차 리뷰) —
    `version: "1.0"  // 옛 downloadUrl: "/builds/old.zip"` 처럼 **코드 뒤에 붙은**
    주석의 옛 값이 살아남고, 그 줄이 진짜 키보다 위에 있으면 그것을 잡는다.

    그렇다고 `//` 를 통째로 지우면 **`https://` 가 잘린다.** 그러면 외부 주소를 쓴
    `downloadUrl`·`feedbackUrl` 이 깨진 값이 되어 **배포가 오탐으로 멈춘다** — 막으려던
    것(조용한 통과)이 아니라 엉뚱한 정지다. 그래서 따옴표 밖의 `//` 만 자른다.
    """
    quote = ""
    for i, c in enumerate(line):
        if quote:
            if c == quote:
                quote = ""
        elif c in "\"'":
            quote = c
        elif c == "/" and line[i + 1:i + 2] == "/":
            return line[:i]
    return line


def download_target(site_dir: str) -> object:
    """`release-config.js` 의 `downloadUrl` 이 `/builds/` 아래 무엇을 가리키나.

    셋 중 하나를 돌려준다.
      · `str` 경로           — `/builds/` 아래다. 그 파일이 있는지 봐야 한다
      · ``NOTHING_TO_CHECK`` — 설정이 없거나, 비었거나, 우리 폴더 밖이다
      · ``UNREADABLE``       — **`downloadUrl` 은 있는데 쓸 수 있는 값이 아니다**

    **왜 셋인가** (`!392` 리뷰). 예전에는 못 알아본 것도 "볼 것 없음" 으로 떨어뜨렸다.
    그러면 `release-config.js` 의 표기가 바뀌는 순간(작은따옴표·템플릿 리터럴·줄바꿈)
    **검사가 조용히 꺼지고 켜진 404 가 그대로 나간다.** 검사가 가장 필요한 때에
    꺼지는 것 — 이 이슈에서 반복해 나온 실패 방식이다.
    """
    try:
        with open(os.path.join(site_dir, CONFIG), encoding="utf-8") as f:
            text = f.read()
    except OSError:
        return NOTHING_TO_CHECK          # 설정 파일 자체가 없다 — 쓰지 않는 사이트다

    # **주석은 빼고 본다** (`!394` 리뷰). 옛 값을 `// downloadUrl: "/builds/old.zip"`
    # 으로 남겨 두는 것은 흔한 일인데, 그것이 진짜 키보다 위에 있으면 **옛 값을 검사
    # 대상으로 잡는다.** 그러면 이미 지운 파일을 찾다가 배포가 서거나, 더 나쁘게는
    # 남아 있는 옛 파일을 보고 통과시킨다.
    #
    # **블록 주석을 먼저 지운다.** 줄 앞만 보면 `/* … */` 안에서 `*` 정렬 없이 적힌
    # 줄이 살아남는다(리뷰가 짚었다). 반대로 살아 있는 값 줄 앞에 `*` 가 붙어 있으면
    # 키를 통째로 놓쳐 **검사가 꺼진다** — 이 함수가 막으려던 바로 그 모양이다.
    live = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    live = "\n".join(strip_line_comment(l) for l in live.splitlines())

    m = URL_RE.search(live)
    if not m:
        return UNREADABLE if KEY_RE.search(live) else NOTHING_TO_CHECK

    url = m.group(2).strip()
    if not url:
        return NOTHING_TO_CHECK          # 일부러 비웠다 = 버튼이 꺼진다. 그것은 정상이다
    if not url.startswith(BUILDS_PREFIX):
        return NOTHING_TO_CHECK          # 우리 폴더 밖 — 우리가 확인할 수 있는 것이 아니다

    rest = url[len(BUILDS_PREFIX):]
    # `/builds/` 만 있으면 **가리키는 파일이 없다.** 예전에는 빈 문자열이 돌아가
    # 부르는 쪽의 `if want:` 에서 falsy 로 떨어져 **아무 검사도 없이 배포됐다**
    # (`!393` 리뷰) — 이번에 막으려던 바로 그 모양이다.
    if not rest:
        return UNREADABLE

    # **배포 폴더 밖을 가리키면 확인한 것이 아니다** (`!394` 2차 리뷰).
    # `/builds/../../etc/passwd` 면 `os.path.join` 이 그대로 밖으로 나가고, 그 파일이
    # 있다는 이유로 **통과시킨다** — 사용자는 404 를 받는다. 사람이 손으로 적는 값이라
    # 실수로도 이렇게 될 수 있다. `..` 는 이 자리에서 정당한 쓰임이 없다.
    # (`-467` 의 `PROMOTE_VERSION` 경로 탈출과 같은 종류다.)
    parts = rest.replace("\\", "/").split("/")
    if any(p in ("..", "") for p in parts) or rest.startswith("/"):
        return UNREADABLE
    return rest


def is_pointer(path: str) -> bool:
    # 포인터는 130바이트 안팎이다. 큰 파일은 읽지 않고 넘긴다.
    if os.path.getsize(path) > 1024:
        return False
    with open(path, "rb") as f:
        return f.read(len(LFS_MARK)) == LFS_MARK


def main() -> int:
    if not os.path.isdir(SRC):
        print(f"❌ {SRC}/ 가 없다 — 체크아웃이 잘못됐다")
        return 1
    if not os.path.isdir(DST):
        print(f"❌ 사이트 폴더가 없다: {DST}")
        print("   배포 러너 config.toml 의 volumes 에 /var/www/metagame-site 가 rw 로")
        print("   마운트돼 있어야 한다 — scripts/deploy/host/install.sh 가 해 준다")
        return 1

    files: list[str] = []
    pointers: list[str] = []
    for dirpath, _, names in os.walk(SRC):
        for n in sorted(names):
            full = os.path.join(dirpath, n)
            rel = os.path.relpath(full, SRC).replace(os.sep, "/")
            if rel in SKIP:
                continue
            files.append(rel)
            if is_pointer(full):
                pointers.append(rel)

    if pointers:
        print(f"❌ LFS 포인터가 {len(pointers)}개 섞여 있다 — 올리면 화면이 깨진다")
        for p in pointers[:8]:
            print(f"   {p}")
        print("   잡에서 `git lfs pull` 이 돌지 않았다(GIT_LFS_SKIP_SMUDGE 확인)")
        return 1

    # ── 받을 수 없는 사이트는 올리지 않는다 (S15P21B202-452, `!390` 리뷰) ──
    #
    # `downloadUrl` 이 아직 서버에 없는 파일을 가리키면, 올리는 순간 **버튼이 켜진 채
    # 404 를 준다.** 그것은 버튼이 꺼져 있는 것보다 나쁘다 — 꺼져 있으면 "아직 준비
    # 중" 으로 읽히지만, 켜진 버튼이 실패하면 받는 사람은 **자기 쪽 문제로 여긴다.**
    #
    # 런처 exe 는 `publish_launcher.py` 가 놓는데 그것은 **CI 가 부르지 않는 수동
    # 스크립트**다. 그래서 순서가 어긋날 여지가 실제로 있다. 여기서 막는다.
    want = download_target(SRC)
    if want is UNREADABLE:
        # **모르면 올리지 않는다** (`!392` 리뷰). 여기서 넘어가면 표기가 바뀌는 순간
        # 검사가 조용히 꺼지고, 그 사실은 사용자가 404 를 받을 때에야 드러난다.
        print(f"❌ {CONFIG} 의 downloadUrl 에서 **가리키는 파일을 정할 수 없다**")
        print( "   둘 중 하나다:")
        print( "     · 표기가 바뀌었다 — 따옴표는 ' 와 \" 둘 다 받는다. 그 밖의 모양")
        print( "       (템플릿 리터럴 등)으로 바꿨다면 publish_site.URL_RE 도 같이 고친다")
        print(f"     · 값이 `{BUILDS_PREFIX}` 뿐이다 — 파일 이름이 빠졌다")
        print( "   확인할 수 없는 채로 올리면 **버튼이 켜진 채 404** 가 될 수 있다")
        return 1
    if want:
        target = os.path.join(BUILDS, want.replace("/", os.sep))
        if not os.path.isdir(BUILDS):
            # **마운트가 빠진 때가 바로 켜진 404 가 나는 때다** (`!392` 리뷰).
            # 그때만 검사가 꺼지면 이 방어는 있으나 마나다. 배포 러너에는
            # `/publish/builds` 가 붙어 있고(2026-09-13 확인), 없다는 것은 설정이
            # 어긋났다는 뜻이다 — 그 상태로 사이트를 올리면 안 된다.
            print(f"❌ 배포 폴더가 없어 다운로드 주소를 확인할 수 없다: {BUILDS}")
            print(f"   /builds/{want} 가 있는지 볼 방법이 없는데 올리면 **버튼이 켜진 채")
            print( "   404** 가 될 수 있다. 러너 config.toml 의 volumes 에")
            print( "   /var/www/metagame-builds 가 rw 로 마운트돼 있어야 한다")
            print( "   — scripts/deploy/host/mount-release-dirs.sh")
            return 1
        if not os.path.isfile(target):
            print(f"❌ 다운로드 주소가 없는 파일을 가리킨다: /builds/{want}")
            print( "   이대로 올리면 **버튼이 켜진 채 404** 가 된다 — 꺼진 것보다 나쁘다.")
            print( "   먼저 그 파일을 놓으세요. 런처면:")
            print( "     python scripts/launcher/publish_launcher.py <런처exe> <버전> --out " + BUILDS)
            print(f"   있는 것: " + (" ".join(sorted(os.listdir(BUILDS))[:8]) or "(비어 있다)"))
            return 1
        print(f"다운로드 주소 확인: /builds/{want} "
              f"({os.path.getsize(target) / 1048576:.1f} MB) ✅")

    total = sum(os.path.getsize(os.path.join(SRC, f.replace("/", os.sep))) for f in files)
    print(f"=== 사이트 배치 ===")
    print(f"올릴 것: {len(files)}개 · {total / 1048576:.1f} MB")

    # ── 옮긴다 ──────────────────────────────────────────────────────────
    # 파일 단위로 원자적으로 놓는다. 통째로 지우고 새로 쓰면 그 사이에 들어온
    # 사람이 **빈 사이트**를 본다.
    changed = same = 0
    for rel in files:
        src = os.path.join(SRC, rel.replace("/", os.sep))
        dst = os.path.join(DST, rel.replace("/", os.sep))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        if os.path.exists(dst) and os.path.getsize(dst) == os.path.getsize(src):
            with open(src, "rb") as a, open(dst, "rb") as b:
                if a.read() == b.read():
                    same += 1
                    continue
        tmp = dst + ".part"
        shutil.copyfile(src, tmp)
        os.replace(tmp, dst)
        changed += 1

    # ── 없어진 것은 지운다 ──────────────────────────────────────────────
    keep = {f.replace("/", os.sep) for f in files}
    gone = 0
    for dirpath, _, names in os.walk(DST):
        for n in names:
            rel = os.path.relpath(os.path.join(dirpath, n), DST)
            if rel in keep or rel.endswith(".part"):
                continue
            os.remove(os.path.join(dirpath, n))
            gone += 1

    print(f"바뀐 것 {changed}개 · 그대로 {same}개 · 지운 것 {gone}개")
    print(f"✅ https://j15b202.p.ssafy.io/play/")
    return 0


if __name__ == "__main__":
    sys.exit(main())
