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
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from ci_common import env  # noqa: E402

SRC = "website"
DST = env("SITE_DIR", "/site")

# LFS 포인터 파일의 첫 줄. 이것으로 시작하면 실파일이 아니다.
LFS_MARK = b"version https://git-lfs.github.com/spec/"

# 사이트에 올리지 않는 것. README 는 배포 담당자용 안내라 공개할 이유가 없다.
SKIP = {"README.md"}


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
