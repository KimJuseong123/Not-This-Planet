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
import re
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

# **버전은 이 형태만 받는다** (S15P21B202-467, `!368` 리뷰). `PROMOTE_VERSION` 은
# 사람이 화면에 손으로 적는 값인데 그대로 `manifest/<값>.json` 경로가 되고 그대로
# `latest.json` 에 실린다. `../` 가 섞이면 배포 폴더 **밖**을 읽고, 이상한 문자열이
# 실리면 런처 쪽에서 무슨 일이 나는지 우리가 모른다.
#
# 리허설이 쓰는 `v0.0.0-rehearsal` 도 여기서 걸린다 — **그게 맞다.** 리허설 산출물은
# 올라간 적이 없으므로 사용자를 그리로 보내면 안 된다.
# 채널 빌드도 여기를 지난다 (S15P21B202-460). **좁게 적는다** — 이 값이 그대로
# `manifest/<값>.json` 경로가 되므로, 넓히는 순간 경로 탈출이 다시 열린다.
# 커밋 해시는 16진수만, MR 번호는 숫자만, 날짜는 8자리만 받는다.
VERSION_RE = re.compile(
    r"v?\d+\.\d+\.\d+"
    r"|dev-\d{8}-[0-9a-f]{7,40}"
    r"|qa-\d+-[0-9a-f]{7,40}")

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
    # **다른 파이프라인에서 올린 것을 배치하는 길** (S15P21B202-460). MR 에서 만든
    # qa 빌드가 이 경로로 온다 — 배포 러너는 `ref_protected` 라 **MR ref 의 잡을 아예
    # 집지 않으므로**(`-405` 실측), MR 파이프라인 안에서는 배치할 수가 없다. 그래서
    # 보호 브랜치의 web 파이프라인에서 버전을 주고 이 잡만 돌린다.
    staged = env("STAGE_VERSION", "").strip()
    if staged:
        if not VERSION_RE.fullmatch(staged):
            print(f"❌ STAGE_VERSION 이 허용된 모양이 아니다: {staged!r}")
            return 1
        version, name = staged, f"metagame-{staged}.zip"
    else:
        for f in (VERSION_FILE, PACKAGE_FILE):
            if not os.path.exists(f):
                print(f"❌ {f} 가 없다 — 빌드 잡 아티팩트가 넘어오지 않았다")
                print("   다른 파이프라인에서 올린 것을 배치하려면 STAGE_VERSION 을 주세요")
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

        import make_manifest

        channel = make_manifest.channel_of(version)
        # **모르는 이름이면 여기서 멈춘다.** 예전 모양이라면 `channel != RELEASE` 가 참이
        # 되어 **zip 배치를 건너뛰고 패키지까지 지우는** dev 경로로 조용히 빠진다 —
        # 릴리스를 올렸는데 아무도 받을 수 없게 되고 아무 오류도 안 난다.
        if channel is None:
            print(f"❌ 버전 이름이 어느 채널도 아니다: {version!r}")
            return 1

        rc = make_manifest.main(manifest_args(game, version, channel, staged))
        if rc != 0:
            print(f"❌ 매니페스트 생성 실패({rc})")
            return rc

        if channel == make_manifest.RELEASE:
            # zip 도 같이 둔다 — 런처를 쓰지 않는 사람이 받는 길이 남아야 한다.
            dst = os.path.join(BUILDS, name)
            if os.path.exists(dst):
                print(f"zip 은 이미 있다: {name}")
            else:
                shutil.copyfile(zip_path, dst + ".part")
                os.replace(dst + ".part", dst)
                print(f"zip 배치: {name}")
        else:
            # **dev·qa zip 은 공개 폴더에 두지 않는다.** nginx 허용 목록이 `*.zip` 을
            # 열어 두므로 두면 누구나 받을 수 있고, 그것은 "배포 런처에 dev 빌드가
            # 보이면 안 된다" 는 결정과 어긋난다. 런처가 받는 길은 이미 있다.
            print("dev·qa zip 은 공개 폴더에 두지 않는다 — 런처로 받는다")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    if channel != make_manifest.RELEASE:
        # **전송용 패키지를 지운다.** 레지스트리는 잡 사이로 156 MB 를 넘기는 수단일
        # 뿐이고, 내용은 이미 `files/<sha>.gz` 에 있다. 매일 쌓아 두면 저장소 할당량을
        # 먹는데 아무도 그것을 보지 않는다.
        delete_package(version)
        print()
        print(f"✅ {version} 배치 완료 — 개발 런처 목록에 보인다.")
        return 0

    print()
    print(f"✅ {version} 을 올려 뒀다 — 아직 **아무도 이 버전을 받지 않는다.**")
    print("   `promote-builds` 버튼을 눌러야 latest.json 이 옮겨진다.")
    return 0


def manifest_args(game: str, version: str, channel: str, staged: str) -> list[str]:
    """`make_manifest` 에 넘길 인자. **채널마다 "최신" 을 가져가는지가 다르다.**

    이것이 `stage()` 밖에 있는 이유는 검사하기 위해서다 — `stage()` 는 레지스트리에서
    156 MB 를 받아 푸는 일까지 하므로 오프라인 검사에서 부를 수가 없고, 그러면 이
    판정이 통째로 검사 밖에 남는다.

    |채널|`latest` 를 가져가나|왜|
    |---|---|---|
    |release|**아니오**|다 올라가기 전에 최신이 바뀌면 그 사이에 켠 사람이 없는 파일을 받는다. 사람이 `promote-builds` 로 옮긴다|
    |dev|예|develop 빌드라 기본값이 되는 것이 맞다. 아침마다 사람이 눌러 주게 만들면 그 버튼은 안 눌린다|
    |qa|**아니오** (S15P21B202-582)|dev 와 `dev-latest.json` 을 함께 쓴다 — qa 가 그것을 가져가면 **누가 MR 버튼을 누를 때마다 팀 전체 개발 런처의 기본값이 그 미머지 브랜치가 된다.** 한 사람이 그 MR 을 QA 하려고 누른 것 때문에 나머지가 모르는 코드를 켜게 된다. 목록에는 올라가므로 **골라서** 쓴다|
    """
    import make_manifest

    args = [game, version, "--out", BUILDS]
    if channel == make_manifest.RELEASE:
        return args + ["--no-latest"]
    # **무엇으로 만든 빌드인지 싣는다.** 버전 문자열만으로는 목록에서
    # "이거 언제 코드예요?" 를 매번 사람에게 물어야 한다.
    args += ["--branch", stage_branch(staged), "--commit", stage_commit(staged, version)]
    if channel == make_manifest.QA:
        args.append("--no-latest")
    return args


def stage_branch(staged: str) -> str:
    """이 빌드를 만든 브랜치. 모르면 **빈 문자열**이다.

    **`CI_COMMIT_REF_NAME` 을 그대로 쓰면 거짓말이 된다.** qa 빌드는 MR 파이프라인에서
    만들어지지만 배치는 `develop` 의 web 파이프라인에서 하므로, 그 변수는 `develop` 이다.
    목록에 `develop @ …` 이 찍히면 **출처를 말한다는 것이 곧 틀린 것을 말하는 것**이 된다.

    그래서 두 단계 배치일 때는 `STAGE_BRANCH` 로 **명시해서** 받고, 없으면 비운다.
    비어 있는 것은 "모른다" 이고, 그것이 틀린 값보다 낫다.
    """
    if staged:
        return env("STAGE_BRANCH", "").strip()
    return env("CI_COMMIT_REF_NAME", "")


def stage_commit(staged: str, version: str) -> str:
    """이 빌드를 만든 커밋. 두 단계 배치면 **버전 이름에서** 꺼낸다.

    `qa-401-1a2b3c4` · `dev-20260914-1a2b3c4` 의 마지막 조각이 그 커밋이다 —
    `publish_devbuild.py` 가 거기에 넣어 둔 값이라 다른 데서 찾을 이유가 없다.
    """
    if not staged:
        return env("CI_COMMIT_SHORT_SHA", "")
    tail = version.rsplit("-", 1)[-1]
    return tail if re.fullmatch(r"[0-9a-f]{7,40}", tail) else ""


def delete_package(version: str) -> None:
    """그 버전의 전송용 패키지를 레지스트리에서 지운다 (S15P21B202-460).

    **실패해도 배치는 성공이다.** 파일은 이미 배포 폴더에 있고, 못 지운 패키지는
    디스크를 먹을 뿐 사용자에게 아무 영향이 없다. 여기서 잡을 빨갛게 만들면
    "빨간데 괜찮은 경우" 를 하나 더 만드는 것이다.
    """
    token = env("CI_JOB_TOKEN") or env("GITLAB_TOKEN")
    if not token:
        print("⚠ 토큰이 없어 전송용 패키지를 지우지 못했다")
        return
    try:
        import json as _json
        import urllib.request as _req

        base = f"{env('CI_API_V4_URL')}/projects/{env('CI_PROJECT_ID')}/packages"
        head = {"JOB-TOKEN": token} if env("CI_JOB_TOKEN") else {"PRIVATE-TOKEN": token}
        # **찾는 것을 서버에 맡긴다.** 첫 페이지만 훑으면 패키지가 쌓인 뒤에는 대상이
        # 뒤 페이지로 밀려 **정리가 조용히 멈춘다** — 그것이 바로 막으려던 것이다.
        query = (f"?package_type=generic&package_name={PACKAGE_NAME}"
                 f"&package_version={version}&per_page=100")
        with _req.urlopen(_req.Request(base + query, headers=head), timeout=30) as r:
            packages = _json.loads(r.read().decode("utf-8"))
        for pkg in packages:
            if pkg.get("version") != version:
                continue
            req = _req.Request(f"{base}/{pkg['id']}", headers=head, method="DELETE")
            with _req.urlopen(req, timeout=30) as r:
                print(f"전송용 패키지 정리: {version} ({r.status})")
            return
        print(f"⚠ 지울 패키지를 찾지 못했다: {version}")
    except Exception as e:
        print(f"⚠ 전송용 패키지를 지우지 못했다({type(e).__name__}) — 배치는 끝났다")


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
    if not VERSION_RE.fullmatch(version):
        # 경로가 되기 전에 막는다. 값은 찍되 **줄여서** 찍는다.
        print(f"❌ 버전 형식이 아니다: {version[:40]!r} — vX.Y.Z 로 주세요")
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
