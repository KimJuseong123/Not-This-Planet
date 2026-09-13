#!/usr/bin/env python3
"""런처 exe 를 서버에 등록한다 (S15P21B202-406).

사용:
    python scripts/launcher/publish_launcher.py <런처 exe> <버전> --out <배포루트>
    python scripts/launcher/publish_launcher.py <런처 exe> <버전> --out <배포루트> --min <버전>

만드는 것:
    <배포루트>/files/<sha256>.gz              게임 파일과 **같은 저장소**에 넣는다
    <배포루트>/NotThisPlanetLauncher.exe      **사람이 받는 자리** (S15P21B202-452)
    <배포루트>/launcher.json                  런처가 자기 버전과 견주는 파일

**왜 둘 다 두나.** 쓰임이 다르다.
  · `files/<sha256>.gz` 는 **런처가 자기를 갱신할 때** 받는다. 받는 코드가 이미 있고
    해시로 검증한다 — 그 경로를 바꿀 이유가 없다.
  · 고정 이름 exe 는 **처음 받는 사람**을 위한 것이다. 해시 이름의 gzip 은 사이트
    버튼에 걸 수 있는 주소가 아니다. 이름이 고정이라 사이트가 버전마다 고쳐지지 않는다.
같은 원본에서 같이 나오므로 둘이 어긋날 수 없다.

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

# **사람이 받는 자리의 이름** (S15P21B202-452). 사이트의 다운로드 버튼과 nginx 허용
# 목록이 이 이름을 그대로 쓴다 — **바꾸면 세 곳을 같이 고쳐야 한다**(검사가 잡는다).
# 버전이 이름에 없는 것은 일부러다: 사이트가 릴리스마다 고쳐지지 않아야 하고,
# 런처는 스스로 갱신하므로 사람은 늘 "지금 것" 하나만 받으면 된다.
PUBLIC_NAME = "NotThisPlanetLauncher.exe"
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
    ap.add_argument("--min", dest="minimum", default=None,
                    help="이 버전 미만 런처는 게임을 못 켠다. 규약을 깰 때만 준다. "
                         "주지 않으면 **서버의 기존 값을 그대로 둔다**")
    ap.add_argument("--min-clear", action="store_true",
                    help="최소 버전 강제를 **푼다**. 내리는 것은 명시해야 한다")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args(argv)

    if not os.path.isfile(a.exe):
        print(f"❌ 런처 파일이 없다: {a.exe}")
        return 1
    if parse_version(a.version) is None:
        print(f"❌ 버전이 {VERSION_RE_HINT} 모양이 아니다: {a.version}")
        return 1
    if a.minimum is not None and a.min_clear:
        print("❌ --min 과 --min-clear 를 함께 줄 수 없다")
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

    path = os.path.join(a.out, "launcher.json")

    # ── 기존 `minLauncher` 를 이어받는다 ─────────────────────────────────
    #
    # **이것이 없으면 강제가 조용히 풀린다.** `--min v1.2.0` 으로 한 번 잠가 두고
    # 다음에 그냥 `publish_launcher.py … v1.3.0` 을 돌리면, 예전 방식은
    # `minLauncher: ""` 로 덮어써서 **옛 런처가 다시 통과한다.** 아무 오류도 안 난다.
    # (`!364` AI 리뷰가 잡았다.)
    #
    # 그래서 주지 않으면 **그대로 두고**, 내리려면 `--min-clear` 로 명시하게 한다 —
    # 강제를 거는 것보다 푸는 것이 더 위험하므로 푸는 쪽에 말을 요구한다.
    previous = ""
    if os.path.exists(path):
        try:
            with open(path, encoding="utf-8") as f:
                old_info = json.load(f)
            previous = old_info.get("minLauncher") or ""
            print(f"지금 서버: {old_info.get('version')} · 최소 {previous or '(없음)'}")
        except (OSError, ValueError):
            print("지금 서버: launcher.json 을 읽지 못했다 — 최소 버전을 이어받지 못한다")

    if a.min_clear:
        minimum = ""
        print("  ⚠ 최소 버전 강제를 **푼다** — 옛 런처가 다시 통과한다")
    elif a.minimum is not None:
        minimum = a.minimum
    else:
        minimum = previous
        if previous:
            print(f"  최소 버전 {previous} 를 그대로 이어받는다 (풀려면 --min-clear)")

    info = {
        "schema": SCHEMA,
        "version": a.version,
        "sha256": digest,
        "size": size,
        "gz": gz_size,
        "minLauncher": minimum,
        "createdAt": datetime.now(KST).isoformat(timespec="seconds"),
    }

    print(f"새로 쓸 것: {a.version} · 최소 {minimum or '(없음)'}")
    if minimum and minimum != previous:
        print("  ⚠ 최소 버전을 올렸다 — 그보다 낮은 런처는 **갱신에 실패하면 게임을 "
              "켜지 못한다.** 규약을 깨는 변경일 때만 맞다")

    if a.dry_run:
        print("(dry-run) launcher.json 을 쓰지 않았다")
        print(f"(dry-run) {PUBLIC_NAME} 을 놓지 않았다")
        return 0

    os.makedirs(a.out, exist_ok=True)
    tmp = path + ".part"
    with io.open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(info, f, ensure_ascii=False, indent=2, sort_keys=True)
        f.write("\n")
    os.replace(tmp, path)
    print(f"launcher.json → {path}")

    # **사람이 받는 자리에 놓는 것은 맨 마지막이다** (S15P21B202-452, `!390` 리뷰).
    # 해시 이름의 gzip 은 사이트 버튼에 걸 수 없으므로 고정 이름으로도 놓는다.
    #
    # **순서에 뜻이 있다.** 여기가 실패하면 사람이 받는 것만 옛 런처로 남는데, 그 런처는
    # 켜지자마자 `launcher.json` 을 보고 **스스로 새 버전으로 갱신한다** — 저절로 낫는다.
    # 반대로 이것을 먼저 놓고 `launcher.json` 쓰기가 실패하면, 사람은 새 exe 를 받는데
    # 자기 갱신 메타데이터는 옛 버전이라 **둘이 어긋난 채 남는다.**
    public = os.path.join(a.out, PUBLIC_NAME)
    tmp_pub = public + ".part"
    shutil.copyfile(a.exe, tmp_pub)
    os.replace(tmp_pub, public)
    print(f"사람이 받는 자리 → {public} ({size / 1048576:.1f} MB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
