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


def dry_run() -> bool:
    """리허설인가. **릴리스 판정을 쓰면 안 된다** (S15P21B202-537).

    `publish_release.dry_run()` 은 *릴리스가 `master` push 로만 나가게* 하는 잠금이다
    (`-407 ④`). 그 판정을 dev·qa 가 그대로 쓰면 **두 채널 모두 영원히 리허설**이 된다 —
    dev 는 `schedule`, qa 는 `merge_request_event` 로 도니 `master` push 인 적이 없다.
    그래서 zip 은 만들어지는데 레지스트리에 올라가는 일이 없었고, `dev-versions.json`
    이 계속 404 였다. 잡은 `return 0` 이라 초록이었다 — 로그만 봐서는 안 드러난다.

    여기서도 **트리거로 잠근다**(그쪽과 같은 결). 다만 통과시키는 트리거가 다르다 —
    `channel()` 이 채널을 가르는 기준과 짝이 맞아야 한 쪽만 어긋나지 않는다.

    한 방향 스위치도 그대로 둔다 — `DEVBUILD_DRY_RUN` 으로 *켤* 수는 있고 *끌* 수는 없다.
    """
    return rehearsal_requested() or not trigger_allowed()


def rehearsal_requested() -> bool:
    """사람이 일부러 리허설을 켰는가. **끄는 데는 못 쓴다.**"""
    return env("DEVBUILD_DRY_RUN").lower() not in ("", "0", "false", "no")


def trigger_allowed() -> bool:
    """이 트리거에서 올려도 되는가.

    `build-devbuild` 의 `rules:` 와 **같은 집합**이어야 한다. 둘이 어긋나면 빌드는
    도는데 산출물이 안 나가는, 알아채기 어려운 상태가 된다 — `cmd_publish` 가 그때
    실패로 끝내는 이유다.
    """
    source = env("CI_PIPELINE_SOURCE")
    if source == "schedule":
        # 새벽 배포일 때만 — 아침 점검(09:30)도 `schedule` 이라 이것으로 갈라야 한다.
        return env("NIGHTLY_DEPLOY_DEV").lower() == "true"
    return source == "merge_request_event"


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

    # **`R.dry_run()` 이 아니다** (S15P21B202-537) — 그쪽은 릴리스 전용 잠금이라
    # dev·qa 를 늘 리허설로 만든다.
    if dry_run():
        if rehearsal_requested() or not env("CI_PIPELINE_SOURCE"):
            print("(dry-run) 레지스트리에 올리지 않는다 — 켜 둔 리허설이다")
            return 0
        # **조용히 초록으로 끝내지 않는다.** 파이프라인에서 돌았다는 것은
        # `build-devbuild` 의 `rules:` 를 통과했다는 뜻이고, 그 rules 는
        # `trigger_allowed()` 와 같은 집합이다. 그런데 여기서 리허설로 빠졌다면
        # **둘이 어긋난 것**이다 — 90분짜리 빌드가 통째로 버려지고
        # `dev-versions.json` 은 404 로 남는다. 그것이 `-537` 의 증상이었고,
        # `return 0` 이라 잡이 초록이어서 **로그를 읽어야만** 알 수 있었다.
        print("❌ 파이프라인에서 돌면서 리허설로 빠졌다 — 이 빌드는 아무 데도 안 간다")
        print(f"   트리거: {env('CI_PIPELINE_SOURCE')!r}"
              f" · NIGHTLY_DEPLOY_DEV={env('NIGHTLY_DEPLOY_DEV')!r}")
        print("   build-devbuild 의 rules 와 publish_devbuild.trigger_allowed() 가 어긋났습니다.")
        return 1

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
