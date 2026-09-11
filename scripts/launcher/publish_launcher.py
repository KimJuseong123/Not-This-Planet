#!/usr/bin/env python3
"""런처 exe 를 서버에 등록한다 (S15P21B202-406).

사용:
    python scripts/launcher/publish_launcher.py <런처 exe> <버전> --out <배포루트>
    python scripts/launcher/publish_launcher.py <런처 exe> <버전> --out <배포루트> --min <버전>

만드는 것:
    <배포루트>/files/<sha256>.gz   게임 파일과 **같은 저장소**에 넣는다
    <배포루트>/launcher.json       런처가 자기 버전과 견주는 파일

**왜 게임과 같은 저장소인가.** 받는 방식이 이미 있다(`files/<sha256>.gz`, 풀어서
해시 확인). 런처 전용 경로를 따로 만들면 받는 코드가 두 벌이 되고, 그중 하나는
덜 검사받는다.

**`--min` 이 강제다.** `minLauncher` 보다 낮은 런처는 갱신에 실패했을 때 **게임을
켜지 못한다.** 그 값을 올리는 것은 "옛 런처로는 더 못 돈다" 는 선언이므로, 규약을
깨는 변경(매니페스트 형식·주소·검증 방식)을 낼 때만 올린다. 그냥 고친 것이면
`--min` 을 주지 않는다 — 갱신은 하되 실패해도 게임은 돌게 한다.

**런처 버전은 게임 릴리스 버전과 무관하다.** 게임은 매주 나가도 런처는 몇 달째
그대로일 수 있다. 두 수를 같게 맞추려 하면 뜻 없는 릴리스가 생긴다.
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import io
import json
import os
import shutil
import sys
from datetime import datetime, timedelta, timezone

KST = timezone(timedelta(hours=9))
SCHEMA = 1
VERSION_RE_HINT = "vX.Y.Z"


def parse_version(v: str) -> tuple[int, int, int] | None:
    s = v.strip()
    if s.startswith("v"):
        s = s[1:]
    parts = s.split(".")
    if len(parts) != 3:
        return None
    try:
        nums = tuple(int(p) for p in parts)
    except ValueError:
        return None
    if any(n < 0 for n in nums):
        return None
    return nums  # type: ignore[return-value]


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="런처 exe → 서버 등록")
    ap.add_argument("exe", help="dotnet publish 로 만든 단일 exe")
    ap.add_argument("version", help=f"런처 버전 ({VERSION_RE_HINT})")
    ap.add_argument("--out", required=True, help="배포 루트(= nginx 가 /builds/ 로 내보내는 폴더)")
    ap.add_argument("--min", dest="minimum", default="",
                    help="이 버전 미만 런처는 게임을 못 켠다. 규약을 깰 때만 준다")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args(argv)

    if not os.path.isfile(a.exe):
        print(f"❌ 런처 파일이 없다: {a.exe}")
        return 1
    if parse_version(a.version) is None:
        print(f"❌ 버전이 {VERSION_RE_HINT} 모양이 아니다: {a.version}")
        return 1
    if a.minimum:
        if parse_version(a.minimum) is None:
            print(f"❌ --min 이 {VERSION_RE_HINT} 모양이 아니다: {a.minimum}")
            return 1
        # 스스로를 막는 값을 올리면 **아무도 게임을 켤 수 없다.**
        if parse_version(a.minimum) > parse_version(a.version):  # type: ignore[operator]
            print(f"❌ --min({a.minimum}) 이 올리는 버전({a.version})보다 높다 — "
                  "이러면 방금 올린 런처도 거부된다")
            return 1

    size = os.path.getsize(a.exe)
    h = hashlib.sha256()
    with open(a.exe, "rb") as f:
        while True:
            b = f.read(1 << 20)
            if not b:
                break
            h.update(b)
    digest = h.hexdigest()

    print(f"=== 런처 {a.version} ===")
    print(f"파일: {a.exe}")
    print(f"크기: {size / 1048576:.1f} MB · sha256 {digest[:12]}…")

    store = os.path.join(a.out, "files")
    dst = os.path.join(store, digest + ".gz")
    gz_size = 0
    if os.path.exists(dst):
        gz_size = os.path.getsize(dst)
        print(f"저장소에 이미 있다 ({gz_size / 1048576:.1f} MB) — 같은 내용이다")
    elif a.dry_run:
        print("(dry-run) 저장소에 넣지 않았다")
    else:
        os.makedirs(store, exist_ok=True)
        tmp = dst + ".part"
        with open(a.exe, "rb") as fin, open(tmp, "wb") as fout:
            with gzip.GzipFile(fileobj=fout, mode="wb", compresslevel=6, mtime=0) as gz:
                shutil.copyfileobj(fin, gz, 1 << 20)
        os.replace(tmp, dst)
        gz_size = os.path.getsize(dst)
        print(f"저장소 → files/{digest[:12]}….gz ({gz_size / 1048576:.1f} MB, "
              f"{gz_size / size * 100:.0f}%)")

    info = {
        "schema": SCHEMA,
        "version": a.version,
        "sha256": digest,
        "size": size,
        "gz": gz_size,
        "minLauncher": a.minimum,
        "createdAt": datetime.now(KST).isoformat(timespec="seconds"),
    }
    path = os.path.join(a.out, "launcher.json")

    # 옛 값을 보여 준다 — `minLauncher` 를 실수로 올리는 것이 가장 아픈 실수다.
    if os.path.exists(path):
        try:
            with io.open(path, encoding="utf-8") as f:
                old = json.load(f)
            print(f"지금 서버: {old.get('version')} · 최소 {old.get('minLauncher') or '(없음)'}")
        except (OSError, ValueError):
            print("지금 서버: launcher.json 을 읽지 못했다")

    print(f"새로 쓸 것: {a.version} · 최소 {a.minimum or '(없음)'}")
    if a.minimum:
        print("  ⚠ 최소 버전을 주었다 — 그보다 낮은 런처는 **갱신에 실패하면 게임을 "
              "켜지 못한다.** 규약을 깨는 변경일 때만 맞다")

    if a.dry_run:
        print("(dry-run) launcher.json 을 쓰지 않았다")
        return 0

    os.makedirs(a.out, exist_ok=True)
    tmp = path + ".part"
    with io.open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(info, f, ensure_ascii=False, indent=2, sort_keys=True)
        f.write("\n")
    os.replace(tmp, path)
    print(f"launcher.json → {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
