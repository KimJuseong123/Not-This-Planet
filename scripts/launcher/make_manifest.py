#!/usr/bin/env python3
"""빌드 폴더를 **런처가 읽는 매니페스트 + 내용 주소 저장소**로 바꾼다 (S15P21B202-406).

사용:
    python scripts/launcher/make_manifest.py <빌드폴더> <버전> --out <배포루트>
    python scripts/launcher/make_manifest.py <빌드폴더> <버전> --out <배포루트> --dry-run

만드는 것:
    <배포루트>/manifest/<버전>.json   경로 → 해시 목록
    <배포루트>/latest.json            최신 버전을 가리키는 **하나뿐인 가변 파일**
    <배포루트>/files/<sha256>.gz      파일을 해시 이름으로, gzip 해서 저장

**왜 통짜 zip 이 아닌가.** 2026-09-10 실측(`-406`): 서로 다른 두 빌드(각 339 파일 ·
377 MB)를 파일별로 견주니 321개 359.3 MB 가 **바이트까지 같았고** 바뀐 것은 18개
16.5 MB 였다. 안 바뀐 쪽에 `UnityPlayer.dll` 84 MB 와 `sharedassets1.assets.resS`
159 MB 가 있다 — 그 둘이 전체의 2/3 다. 그래서 매번 160 MB 를 다시 받을 이유가 없다.

**왜 해시 이름으로 저장하나.** 버전 폴더를 쌓으면 377 MB × 버전 수다. 해시로 두면
위에서 잰 359 MB 공통 부분이 버전 간에 **공유된다** — 10개 버전이 약 530 MB.
런처의 무결성 검증도 같은 해시로 끝난다.

**`latest.json` 만 바뀐다.** 롤백은 그 한 줄을 옛 버전으로 되돌리는 것이다. 매니페스트와
파일은 한 번 쓰면 고치지 않는다(해시가 이름이므로 고칠 수도 없다).
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
import shutil
import sys
from datetime import datetime, timedelta, timezone

KST = timezone(timedelta(hours=9))
SCHEMA = 1

# 빌드 폴더에 있으면 **배포를 거부한다.** Unity 가 만드는 디버그 산출물이고, 릴리스
# 파이프라인이 이미 지우지만 여기서 한 번 더 막는다 — 손으로 만든 빌드를 올릴 때가 있다.
FORBIDDEN_DIR_SUFFIX = (
    "_BurstDebugInformation_DoNotShip",
    "_BackUpThisFolder_ButDontShipItWithYourGame",
)

# 실행 파일 이름을 매니페스트에 담는다 — 런처가 하드코딩하면 `-402` 의 이름 변경에서
# 깨진다. 후보를 여기서 찾고, 하나로 정해지지 않으면 사람에게 물어 멈춘다.
EXE_HINTS = ("NotThisPlanet.exe", "METAGAME.exe", "Metagame.exe")


def sha256_of(path: str, chunk: int = 1 << 20) -> tuple[str, int]:
    """파일의 sha256 과 크기. 큰 파일이 있으므로 스트리밍으로 읽는다(트레일러 88 MB)."""
    h = hashlib.sha256()
    size = 0
    with open(path, "rb") as f:
        while True:
            b = f.read(chunk)
            if not b:
                break
            h.update(b)
            size += len(b)
    return h.hexdigest(), size


def safe_relpath(root: str, path: str) -> str:
    """매니페스트에 담을 상대 경로. **런처가 이 문자열로 파일을 쓴다.**

    그래서 여기서 막아야 한다 — `..` 이나 절대 경로가 들어가면 런처가 게임 폴더
    밖에 쓴다. 윈도우에서 돌지만 매니페스트는 nginx 가 내보내므로 **구분자를 `/` 로
    고정한다**: 섞이면 런처가 같은 파일을 다른 것으로 보고 매번 다시 받는다.
    """
    rel = os.path.relpath(path, root).replace(os.sep, "/")
    if rel.startswith("../") or rel == ".." or rel.startswith("/") or ":" in rel:
        raise ValueError(f"경로가 빌드 폴더를 벗어난다: {rel}")
    return rel


def collect(root: str) -> list[dict]:
    """빌드 폴더의 모든 파일을 경로·해시·크기로 적는다."""
    out: list[dict] = []
    for dirpath, dirnames, filenames in os.walk(root):
        for d in list(dirnames):
            if d.endswith(FORBIDDEN_DIR_SUFFIX):
                raise SystemExit(
                    f"❌ 배포하면 안 되는 폴더가 빌드에 있다: {d}\n"
                    "   Unity 디버그 산출물이다. 릴리스 파이프라인은 이것을 지운다 —\n"
                    "   손으로 만든 빌드라면 지우고 다시 돌려라."
                )
        for name in sorted(filenames):
            full = os.path.join(dirpath, name)
            if os.path.islink(full):
                # 심볼릭 링크를 그대로 담으면 런처가 무엇을 쓸지 알 수 없다.
                raise SystemExit(f"❌ 심볼릭 링크가 있다: {full}")
            digest, size = sha256_of(full)
            out.append({"path": safe_relpath(root, full), "sha256": digest, "size": size})
    return sorted(out, key=lambda e: e["path"])


def pick_exe(files: list[dict]) -> str:
    """실행 파일을 정한다. 최상위의 `.exe` 만 후보로 본다."""
    top = [e["path"] for e in files if e["path"].lower().endswith(".exe") and "/" not in e["path"]]
    for hint in EXE_HINTS:
        if hint in top:
            return hint
    # Unity 는 크래시 핸들러도 최상위에 둔다 — 그것은 실행 대상이 아니다.
    cand = [p for p in top if "crashhandler" not in p.lower()]
    if len(cand) == 1:
        return cand[0]
    raise SystemExit(
        f"❌ 실행 파일을 하나로 정할 수 없다: {top}\n"
        "   `EXE_HINTS` 에 이름을 더하거나 빌드 폴더를 확인하라."
    )


def publish(files: list[dict], root: str, out: str,
            dry: bool) -> tuple[int, int, dict[str, int], set[str]]:
    """파일을 `files/<sha256>.gz` 로 놓는다. 이미 있으면 건너뛴다(= 버전 간 공유).

    **왜 gzip 인가.** 저장소는 파일을 그대로 두므로 압축이 없다 — v1.0.0 을 재
    보니 **압축 없이 355.5 MB, zip 은 156 MB** 였다. 그대로 두면 런처의 **첫
    설치가 지금 zip 을 받는 것보다 2배 이상 무거워진다.** 업데이트만 가벼워지고
    첫 경험이 나빠지는 것은 바꿀 이유가 못 된다.

    2026-09-11 실측(큰 파일 20개 = 전체의 91%): 324.0 MB → 144.7 MB, **45%**.
    `sharedassets1.assets.resS` 187 MB → 82 MB, `UnityPlayer.dll` 35.4 MB → 15.5 MB.
    이미 압축된 것도 있다(`resources.resource` 6.4 MB → 100%) — gzip 은 그때
    원본만큼만 커지므로 손해가 없다.

    **nginx `gzip_static` 을 쓰지 않는다.** 그 방식은 압축본과 원본을 둘 다
    둬야 해서 디스크가 두 배가 된다. 런처가 직접 풀면 디스크는 압축본 한 벌뿐이고,
    해시는 **푼 뒤의 바이트**로 확인하므로 검증이 약해지지 않는다.

    `mtime=0` 으로 굽는다 — 같은 내용이면 같은 바이트가 나와야 다시 올릴 때 헛일이
    생기지 않는다(gzip 은 기본으로 현재 시각을 헤더에 넣는다).

    돌려주는 것: (새로 놓은 개수, 이미 있던 개수, 해시 → 압축 크기, 새로 놓은 해시들).
    """
    store = os.path.join(out, "files")
    if not dry:
        os.makedirs(store, exist_ok=True)
    added = shared = 0
    gz_size: dict[str, int] = {}
    fresh: set[str] = set()
    for e in files:
        digest = e["sha256"]
        # 같은 빌드 안에 바이트가 같은 파일이 둘 있을 수 있다(빈 파일 등).
        # 두 번 세면 "늘어나는 용량" 이 틀린다.
        if digest in gz_size:
            shared += 1
            continue
        dst = os.path.join(store, digest + ".gz")
        if os.path.exists(dst):
            shared += 1
            gz_size[digest] = os.path.getsize(dst)
            continue
        added += 1
        fresh.add(digest)
        src = os.path.join(root, e["path"].replace("/", os.sep))
        if dry:
            gz_size[digest] = 0
            continue
        # **원자적으로 놓는다.** 받는 쪽이 반쯤 쓰인 파일을 해시로 믿으면 안 된다.
        tmp = dst + ".part"
        with open(src, "rb") as fin, open(tmp, "wb") as fout:
            with gzip.GzipFile(fileobj=fout, mode="wb", compresslevel=6, mtime=0) as gz:
                shutil.copyfileobj(fin, gz, 1 << 20)
        os.replace(tmp, dst)
        gz_size[digest] = os.path.getsize(dst)
    return added, shared, gz_size, fresh


def write_json(path: str, data: dict, dry: bool) -> None:
    if dry:
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".part"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=2, sort_keys=True)
        f.write("\n")
    os.replace(tmp, path)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="빌드 폴더 → 매니페스트 + 내용 주소 저장소")
    ap.add_argument("build_dir")
    ap.add_argument("version", help="예: v1.0.0")
    ap.add_argument("--out", required=True, help="배포 루트(= nginx 가 /builds/ 로 내보내는 폴더)")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--no-latest", action="store_true",
                    help="latest.json 을 건드리지 않는다 — 올려 두고 나중에 전환할 때")
    a = ap.parse_args(argv)

    if not os.path.isdir(a.build_dir):
        print(f"❌ 빌드 폴더가 없다: {a.build_dir}")
        return 1
    if not a.version.startswith("v"):
        print(f"❌ 버전은 `v` 로 시작해야 한다(릴리스 태그와 같은 모양): {a.version}")
        return 1

    print(f"=== 매니페스트 {a.version} ===")
    print(f"빌드 폴더: {a.build_dir}")
    files = collect(a.build_dir)
    total = sum(e["size"] for e in files)
    exe = pick_exe(files)
    print(f"파일 {len(files)}개 · {total / 1048576:.1f} MB · 실행 파일 {exe}")

    added, shared, gz_size, new_digests = publish(files, a.build_dir, a.out, a.dry_run)
    # **압축 크기를 매니페스트에 싣는다.** 런처가 "얼마나 받는지" 를 먼저 말할 수
    # 있어야 사람이 기다릴지 판단한다. 무결성은 `sha256`(푼 뒤의 바이트)이 지킨다.
    for e in files:
        e["gz"] = gz_size.get(e["sha256"], 0)
    gz_total = sum(gz_size.values())
    # **두 수를 따로 말한다.** 섞으면 이 방식의 값을 잘못 읽는다.
    #   처음 받는 사람 = 전체(= 지금 zip 을 받는 것과 같은 양)
    #   업데이트하는 사람 = 이번에 새로 생긴 파일만
    new_gz = sum(v for k, v in gz_size.items() if k in new_digests)
    print(f"저장소: 새로 {added}개 · 이미 있던 것 {shared}개")
    print(f"첫 설치:   {gz_total / 1048576:.1f} MB "
          f"(압축 없이 {total / 1048576:.1f} MB"
          + (f", 압축률 {gz_total / total * 100:.0f}%)" if total else ")"))
    if shared:
        print(f"업데이트:  {new_gz / 1048576:.1f} MB  ← 이미 있던 {shared}개는 받지 않는다"
              f" ({new_gz / gz_total * 100:.1f}% 만 받는다)" if gz_total else "")

    manifest = {
        "schema": SCHEMA,
        "version": a.version,
        "exe": exe,
        "createdAt": datetime.now(KST).isoformat(timespec="seconds"),
        "totalBytes": total,
        "files": files,
    }
    mpath = os.path.join(a.out, "manifest", f"{a.version}.json")
    write_json(mpath, manifest, a.dry_run)
    print(f"{'(dry-run) ' if a.dry_run else ''}매니페스트 → {mpath}")

    if not a.no_latest:
        write_json(os.path.join(a.out, "latest.json"),
                   {"schema": SCHEMA, "version": a.version,
                    "manifest": f"manifest/{a.version}.json"}, a.dry_run)
        print(f"{'(dry-run) ' if a.dry_run else ''}latest.json → {a.version}")
    else:
        print("latest.json 은 건드리지 않았다 — 전환은 따로 한다")
    return 0


if __name__ == "__main__":
    sys.exit(main())
