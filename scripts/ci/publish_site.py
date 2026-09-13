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


def download_target(site_dir: str) -> str | None:
    """`release-config.js` 의 `downloadUrl` 이 `/builds/` 아래 무엇을 가리키나.

    가리키는 것이 없거나 `/builds/` 밖이면 None — 그때는 검사할 것이 없다.
    """
    try:
        with open(os.path.join(site_dir, CONFIG), encoding="utf-8") as f:
            m = re.search(r'downloadUrl' + chr(92) + r's*:' + chr(92) + r's*"([^"]*)"', f.read())
    except OSError:
        return None
    url = (m.group(1).strip() if m else "")
    return url[len("/builds/"):] if url.startswith("/builds/") else None


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
    if want:
        target = os.path.join(BUILDS, want.replace("/", os.sep))
        if not os.path.isdir(BUILDS):
            print(f"⚠ 배포 폴더가 없어 다운로드 주소를 확인하지 못했다: {BUILDS}")
            print(f"   사이트는 올린다 — 다만 /builds/{want} 가 있는지 눈으로 보세요")
        elif not os.path.isfile(target):
            print(f"❌ 다운로드 주소가 없는 파일을 가리킨다: /builds/{want}")
            print( "   이대로 올리면 **버튼이 켜진 채 404** 가 된다 — 꺼진 것보다 나쁘다.")
            print( "   먼저 그 파일을 놓으세요. 런처면:")
            print( "     python scripts/launcher/publish_launcher.py <런처exe> <버전> --out " + BUILDS)
            print(f"   있는 것: " + (" ".join(sorted(os.listdir(BUILDS))[:8]) or "(비어 있다)"))
            return 1
        else:
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
