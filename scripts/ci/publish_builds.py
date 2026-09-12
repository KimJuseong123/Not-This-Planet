#!/usr/bin/env python3
"""릴리스 산출물을 **사용자가 받는 자리**에 놓는다 (S15P21B202-405).

    python scripts/ci/publish_builds.py stage      # zip·매니페스트·저장소를 올린다
    python scripts/ci/publish_builds.py promote    # latest.json 을 이 버전으로 옮긴다

**왜 릴리스 잡이 직접 못 하나.** `release-windows` 는 공용 러너(`linux`)에서 돈다.
거기에 `/var/www` 를 rw 로 마운트하면 **아무 MR 잡이나 배포 폴더에 쓸 수 있다** —
`-252` 가 스풀을 배포 전용 러너로 뺀 이유와 같다. 그래서 배치는 `deploy` 러너에서
한다.

**왜 아티팩트로 넘기지 않나.** zip 이 156 MB 이고 아티팩트 상한이 100 MB 다
(인스턴스 설정이라 우리가 못 늘린다). 그래서 **패키지 레지스트리를 운반 수단으로**
쓴다 — `release-windows` 가 이미 거기 올리고, 이 잡이 같은 호스트에서 받는다.
넘어오는 아티팩트는 파일 이름이 적힌 한 줄(`release-package.txt`)뿐이다.

**두 단계로 나눈 이유.** `stage` 는 자동으로 돌아 파일을 다 올려 두고, `promote` 는
사람이 누른다. `latest.json` 을 옮기는 순간 **모든 사용자의 런처가 그 버전으로
간다** — 그 시점은 사람이 고르는 것이 맞다(`deploy-prod` 와 같은 결). 올려 두는
것은 위험하지 않다: 아무도 그 버전을 가리키지 않으므로 받는 사람이 없다.

되돌리기도 `promote` 다 — `PROMOTE_VERSION` 을 옛 버전으로 주고 돌리면 된다.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "launcher"))

from ci_common import env  # noqa: E402

VERSION_FILE = "release-version.txt"
PACKAGE_FILE = "release-package.txt"
PACKAGE_NAME = "metagame"

# 배포 러너에 마운트되는 자리. `-324` 규칙대로 **데이터만** 들어간다.
# **`/builds` 를 기본값으로 두지 않는다** (S15P21B202-465). 그 자리는 GitLab Runner 가
# 저장소를 클론하는 기본 작업 디렉터리라, 공개 폴더를 거기 물리면 **잡이 돌 때마다
# 저장소가 공개 폴더에 떨어진다.** 실제로 2026-09-12 에 835 MB 가 무인증으로
# 받아졌다. 마운트는 `/publish/builds` 에 건다.
BUILDS = env("BUILDS_DIR", "/publish/builds")


def run(*args: str) -> str:
    r = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0:
        raise RuntimeError(f"{args[0]} 실패({r.returncode}): {(r.stderr or '')[:300]}")
    return r.stdout


def read_line(path: str) -> str:
    with open(path, encoding="utf-8") as f:
        return f.read().strip()


def package_url(version: str, file_name: str) -> str:
    from urllib.parse import quote
    api = env("CI_API_V4_URL", "").rstrip("/")
    pid = env("CI_PROJECT_ID", "")
    return f"{api}/projects/{pid}/packages/generic/{PACKAGE_NAME}/{quote(version)}/{quote(file_name)}"


def download(url: str, dest: str) -> int:
    """패키지 레지스트리에서 받는다. 잡 토큰으로 인증한다 — 사람 토큰을 쓰지 않는다."""
    import urllib.request

    req = urllib.request.Request(url)
    tok = env("CI_JOB_TOKEN")
    if tok:
        req.add_header("JOB-TOKEN", tok)
    else:
        # 로컬에서 손으로 돌릴 때. 값은 찍지 않는다.
        pt = env("GITLAB_TOKEN")
        if not pt:
            raise RuntimeError("CI_JOB_TOKEN 도 GITLAB_TOKEN 도 없다 — 받을 수 없다")
        req.add_header("PRIVATE-TOKEN", pt)

    tmp = dest + ".part"
    with urllib.request.urlopen(req, timeout=300) as r, open(tmp, "wb") as f:
        shutil.copyfileobj(r, f, 1 << 20)
    os.replace(tmp, dest)
    return os.path.getsize(dest)


def stage() -> int:
    """zip 을 받아 풀고, 매니페스트와 저장소를 만들고, zip 도 그대로 둔다."""
    if not os.path.isdir(BUILDS):
        print(f"❌ 배포 폴더가 없다: {BUILDS}")
        print("   배포 러너 config.toml 의 volumes 에 /var/www/metagame-builds 가 rw 로")
        print("   마운트돼 있어야 한다 — scripts/deploy/host/mount-release-dirs.sh 가 해 준다")
        return 1
    for f in (VERSION_FILE, PACKAGE_FILE):
        if not os.path.exists(f):
            print(f"❌ {f} 가 없다 — release-windows 아티팩트가 넘어오지 않았다")
            return 1

    version = read_line(VERSION_FILE)
    name = read_line(PACKAGE_FILE)
    print(f"=== 배치 {version} ===")
    print(f"패키지: {name}")

    tmp = tempfile.mkdtemp(prefix="publish-builds-")
    try:
        zip_path = os.path.join(tmp, name)
        size = download(package_url(version, name), zip_path)
        print(f"레지스트리에서 받음: {size / 1048576:.1f} MB")

        game = os.path.join(tmp, "game")
        with zipfile.ZipFile(zip_path) as z:
            # **zip 안의 경로도 남이 준 값이다.** `..` 이나 절대 경로가 들어 있으면
            # 압축을 푸는 것만으로 폴더 밖에 쓴다. 우리가 만든 zip 이지만 여기서
            # 한 번 더 본다 — 푸는 쪽이 막는 것이 맞다.
            for n in z.namelist():
                if n.startswith("/") or ".." in n.replace("\\", "/").split("/"):
                    print(f"❌ zip 안에 위험한 경로가 있다: {n}")
                    return 1
            z.extractall(game)
        print(f"풀었다: 파일 {sum(len(f) for _, _, f in os.walk(game))}개")

        # 매니페스트와 내용 주소 저장소. **`latest.json` 은 건드리지 않는다.**
        import make_manifest

        rc = make_manifest.main([game, version, "--out", BUILDS, "--no-latest"])
        if rc != 0:
            print(f"❌ 매니페스트 생성 실패({rc})")
            return rc

        # zip 도 같이 둔다 — 런처를 쓰지 않는 사람이 받는 길이 남아야 한다.
        dst = os.path.join(BUILDS, name)
        if os.path.exists(dst):
            print(f"zip 은 이미 있다: {name}")
        else:
            shutil.copyfile(zip_path, dst + ".part")
            os.replace(dst + ".part", dst)
            print(f"zip 배치: {name}")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    print()
    print(f"✅ {version} 을 올려 뒀다 — 아직 **아무도 이 버전을 받지 않는다.**")
    print("   `promote-builds` 버튼을 눌러야 latest.json 이 옮겨진다.")
    return 0


def promote() -> int:
    """`latest.json` 을 옮긴다. 되돌리기도 이 경로다."""
    if not os.path.isdir(BUILDS):
        print(f"❌ 배포 폴더가 없다: {BUILDS}")
        return 1

    # 사람이 되돌릴 때는 변수로 옛 버전을 준다.
    version = env("PROMOTE_VERSION") or (
        read_line(VERSION_FILE) if os.path.exists(VERSION_FILE) else "")
    if not version:
        print("❌ 옮길 버전을 모른다 — PROMOTE_VERSION 을 주거나 release-version.txt 가 있어야 한다")
        return 1

    mpath = os.path.join(BUILDS, "manifest", f"{version}.json")
    if not os.path.exists(mpath):
        print(f"❌ {version} 의 매니페스트가 없다: {mpath}")
        print("   올린 적이 없거나 보관 기간이 지나 회수됐다. 있는 것:")
        d = os.path.join(BUILDS, "manifest")
        if os.path.isdir(d):
            print("   " + " ".join(sorted(n[:-5] for n in os.listdir(d) if n.endswith(".json"))))
        return 1

    # **가리키기 전에 파일이 실제로 있는지 본다.** 하나라도 없으면 그 버전을 받는
    # 사람이 중간에 멈춘다 — 옮기고 나서 알면 이미 늦다.
    with open(mpath, encoding="utf-8") as f:
        manifest = json.load(f)
    missing = [e["path"] for e in manifest.get("files", [])
               if not os.path.exists(os.path.join(BUILDS, "files", e["sha256"] + ".gz"))]
    if missing:
        print(f"❌ {version} 이 쓰는 파일 {len(missing)}개가 저장소에 없다 — 옮기지 않는다")
        for p in missing[:5]:
            print(f"   {p}")
        return 1

    before = ""
    lpath = os.path.join(BUILDS, "latest.json")
    if os.path.exists(lpath):
        try:
            with open(lpath, encoding="utf-8") as f:
                before = json.load(f).get("version", "")
        except (OSError, ValueError):
            pass

    body = {"schema": 1, "version": version, "manifest": f"manifest/{version}.json"}
    tmp = lpath + ".part"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(body, f, ensure_ascii=False, indent=2, sort_keys=True)
        f.write("\n")
    os.replace(tmp, lpath)

    arrow = f"{before} → {version}" if before and before != version else version
    print(f"✅ latest.json {arrow}")
    print(f"   파일 {len(manifest.get('files', []))}개 · "
          f"{manifest.get('totalBytes', 0) / 1048576:.1f} MB")
    if before and before != version:
        print(f"   되돌리려면 PROMOTE_VERSION={before} 로 이 잡을 다시 돌린다")
    return 0


def main(argv: list[str]) -> int:
    what = argv[0] if argv else ""
    if what == "stage":
        return stage()
    if what == "promote":
        return promote()
    print("사용: publish_builds.py stage|promote")
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
