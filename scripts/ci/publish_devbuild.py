"""개발·QA 빌드를 패키지 레지스트리로 올린다 (S15P21B202-460).

실행:
    python scripts/ci/publish_devbuild.py version        버전 이름을 정해 파일로 남긴다
    python scripts/ci/publish_devbuild.py publish <rc>   zip → 패키지 레지스트리

**왜 `publish_release.py` 를 안 쓰나.** 그쪽은 릴리스 전용이다 — 태그를 읽고, GitLab
Release 를 만들고, 변경 목록을 뽑고, MM 카드를 보낸다. dev·qa 빌드는 그중 **아무것도**
하지 않는다. 채널을 그 700줄에 끼워 넣으면 **릴리스 경로가 같이 흔들린다.** 작은 것을
따로 두고, 겹치는 조각(zip·업로드)만 그쪽에서 가져다 쓴다.

**패키지 레지스트리는 운반 수단일 뿐이다.** 빌드는 공용 러너(`tags: [linux]`)에서 돌고
배포 폴더는 배포 러너(`tags: [deploy]`)에만 마운트돼 있다. 아티팩트 상한이 100 MB 라
156 MB zip 을 잡 사이로 못 넘긴다. 그래서 레지스트리를 거친다 — 그리고 배치가 끝나면
**그 패키지는 지운다**(`publish_builds.py`). 릴리스와 달리 dev·qa zip 은 보관할 이유가
없고, 매일 156 MB 씩 쌓으면 저장소 할당량을 먹는다.
"""
import os
import re
import subprocess
import sys
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.stdout.reconfigure(encoding="utf-8")

import publish_release as R  # noqa: E402

KST = timezone(timedelta(hours=9))

# `publish_builds.py stage` 가 읽는 이름을 그대로 쓴다 — 그쪽을 고치지 않기 위해서다.
VERSION_FILE = "release-version.txt"
PACKAGE_FILE = "release-package.txt"

DEV, QA = "dev", "qa"

# **커밋 해시만 받는다.** 이 값이 그대로 `manifest/<버전>.json` 경로가 되므로, 브랜치
# 이름처럼 `/` 가 들어갈 수 있는 것을 넣으면 배포 폴더 밖을 가리킬 수 있다.
SHA_RE = re.compile(r"^[0-9a-f]{7,40}$")


def env(name: str, default: str = "") -> str:
    return (os.environ.get(name) or default).strip()


def channel() -> str:
    """스케줄이면 dev, MR 이면 qa.

    **파이프라인 종류로 가른다.** 사람이 고르게 하면 MR 에서 dev 채널을 올릴 수 있고,
    그러면 `dev-latest.json` 이 머지되지도 않은 코드를 가리킨다.
    """
    return DEV if env("CI_PIPELINE_SOURCE") == "schedule" else QA


def short_sha() -> str:
    sha = env("CI_COMMIT_SHORT_SHA")
    if not sha:
        try:
            sha = subprocess.run(["git", "rev-parse", "--short", "HEAD"],
                                 capture_output=True, text=True, check=True).stdout.strip()
        except Exception:
            sha = ""
    return sha.lower()


def decide_version(now: datetime | None = None) -> str | None:
    """채널에 맞는 버전 이름. 못 만들면 None.

    dev  `dev-20260914-1a2b3c4`  — 날짜가 있어야 목록에서 사람이 고를 수 있다
    qa   `qa-401-1a2b3c4`        — MR 번호가 있어야 "그 MR 빌드" 를 찾을 수 있다
    """
    sha = short_sha()
    if not SHA_RE.match(sha):
        print(f"❌ 커밋 해시를 읽지 못했다: {sha!r}")
        return None

    if channel() == DEV:
        stamp = (now or datetime.now(KST)).strftime("%Y%m%d")
        return f"dev-{stamp}-{sha}"

    iid = env("CI_MERGE_REQUEST_IID")
    if not iid.isdigit():
        print(f"❌ MR 번호를 읽지 못했다: {iid!r} — MR 파이프라인에서만 돌아야 한다")
        return None
    return f"qa-{iid}-{sha}"


def cmd_version() -> int:
    v = decide_version()
    if v is None:
        return 1
    with open(VERSION_FILE, "w", encoding="utf-8") as f:
        f.write(v + "\n")
    print(f"=== {channel()} 빌드 {v} ===")
    print(f"브랜치: {env('CI_COMMIT_REF_NAME') or '(모름)'}")
    return 0


def cmd_publish(rc: str) -> int:
    """빌드 산출물을 zip 으로 묶어 레지스트리에 올린다."""
    if rc != "0":
        print(f"❌ 빌드가 실패했다(exit {rc}) — 올리지 않는다")
        return 1
    if not os.path.exists(VERSION_FILE):
        print(f"❌ {VERSION_FILE} 이 없다 — `version` 을 먼저 돌려야 한다")
        return 1

    version = open(VERSION_FILE, encoding="utf-8").read().strip()
    build_dir = env("SPACE_BUILD_DIR")
    if not build_dir or not os.path.isdir(build_dir):
        print(f"❌ 빌드 폴더가 없다: {build_dir!r}")
        return 1

    name = f"metagame-{version}.zip"
    zip_path = os.path.join(os.getcwd(), name)
    size = R.zip_dir(build_dir, zip_path)
    print(f"zip: {name} · {size / 1048576:.1f} MB")

    if R.dry_run():
        print("(dry-run) 레지스트리에 올리지 않는다")
        return 0

    code = R.upload_package(zip_path, version, name)
    if code >= 300:
        print(f"❌ 레지스트리 업로드 실패({code})")
        return 1

    with open(PACKAGE_FILE, "w", encoding="utf-8") as f:
        f.write(name + "\n")
    print(f"올렸다: {R.package_url(version, name)}")
    # **여기서 끝이 아니다.** 배포 러너의 `publish-dev-build` 가 받아서 배치하고
    # 그 패키지를 지운다 — 레지스트리는 운반 수단일 뿐이다.
    return 0


def main(argv: list[str] | None = None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    if not args:
        print(__doc__)
        return 1
    if args[0] == "version":
        return cmd_version()
    if args[0] == "publish":
        return cmd_publish(args[1] if len(args) > 1 else "1")
    print(f"❌ 모르는 명령: {args[0]}")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
