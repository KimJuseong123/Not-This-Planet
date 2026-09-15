"""dev·qa 빌드가 레지스트리에 올라가는가 (S15P21B202-537).

실행: python scripts/ci/tests/test_devbuild_dry_run.py

`publish_devbuild` 가 `publish_release.dry_run()` 을 그대로 쓰면서 **dev·qa 두 채널이
영원히 리허설**이었다. 빌드는 초록인데(`return 0`) 패키지가 안 올라가, `dev-versions.json`
이 404 로 남았고 `--dev` 런처에 고를 것이 없었다. 여기서는 그 두 판정이 **갈라져 있는지**
를 못박는다 — 한쪽을 되돌리면 이 파일이 깨진다.
"""
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1", "CI_JOB_TOKEN": "job-x",
                   "CI_API_V4_URL": "https://lab.example/api/v4"})

import publish_devbuild as D  # noqa: E402
import publish_release as R  # noqa: E402

TRIGGER_KEYS = ("CI_PIPELINE_SOURCE", "CI_COMMIT_BRANCH", "CI_MERGE_REQUEST_IID",
                "NIGHTLY_DEPLOY_DEV", "DEVBUILD_DRY_RUN", "RELEASE_DRY_RUN",
                "CI_COMMIT_SHORT_SHA")


def at(**kw):
    """트리거 환경을 통째로 갈아 끼운다 — 이전 경우가 새는 것을 막는다."""
    for k in TRIGGER_KEYS:
        os.environ.pop(k, None)
    os.environ.update({k: v for k, v in kw.items() if v is not None})


NIGHTLY = dict(CI_PIPELINE_SOURCE="schedule", CI_COMMIT_BRANCH="develop",
               NIGHTLY_DEPLOY_DEV="true")
MR = dict(CI_PIPELINE_SOURCE="merge_request_event", CI_MERGE_REQUEST_IID="461")

# ── 1) 실제로 도는 두 트리거는 올린다 ──
at(**NIGHTLY)
assert D.dry_run() is False, "새벽 배포가 리허설이면 dev-versions.json 이 영영 안 찬다"
at(**MR)
assert D.dry_run() is False, "MR 버튼이 리허설이면 qa 빌드를 배치할 수 없다"

# ── 2) 그 밖은 전부 리허설 ──
at(CI_PIPELINE_SOURCE="schedule", CI_COMMIT_BRANCH="develop")
assert D.dry_run() is True, "아침 점검(09:30)도 schedule 이다 — 그것까지 올리면 안 된다"
at(CI_PIPELINE_SOURCE="schedule", NIGHTLY_DEPLOY_DEV="false")
assert D.dry_run() is True
at(CI_PIPELINE_SOURCE="push", CI_COMMIT_BRANCH="develop")
assert D.dry_run() is True
at(CI_PIPELINE_SOURCE="push", CI_COMMIT_BRANCH="master")
assert D.dry_run() is True, "릴리스 파이프라인에서 dev 빌드가 새면 안 된다"
at(CI_PIPELINE_SOURCE="web", CI_COMMIT_BRANCH="develop")
assert D.dry_run() is True
at()
assert D.dry_run() is True, "트리거를 모르면 안 올린다"

# ── 3) 한 방향 스위치 — 켤 수는 있고 끌 수는 없다 ──
at(DEVBUILD_DRY_RUN="1", **NIGHTLY)
assert D.dry_run() is True, "변수로 리허설을 켤 수 있어야 한다"
at(DEVBUILD_DRY_RUN="0", CI_PIPELINE_SOURCE="push", CI_COMMIT_BRANCH="develop")
assert D.dry_run() is True, "변수로 리허설을 끌 수 있으면 아무 브랜치에서 dev 빌드가 나간다"
at(DEVBUILD_DRY_RUN="no", **NIGHTLY)
assert D.dry_run() is False, "0·false·no·빈값은 끈 것이 아니라 '안 켠 것'이다"

# ── 4) 릴리스 판정과 **갈라져 있다** — 이 파일의 핵심 ──
at(**NIGHTLY)
assert R.dry_run() is True and D.dry_run() is False, \
    "R.dry_run() 을 다시 쓰면 새벽 빌드가 또 리허설이 된다 (-537 이 고친 것)"
at(**MR)
assert R.dry_run() is True and D.dry_run() is False

# 릴리스 쪽은 건드리지 않았다 — master push 만 진짜다
at(CI_PIPELINE_SOURCE="push", CI_COMMIT_BRANCH="master")
assert R.dry_run() is False, "릴리스 경로를 바꾸면 안 된다 (-407 ④)"

# ── 5) channel()·decide_version() 과 짝이 맞는가 ──
# 올리기로 판정한 트리거에서 **버전 이름을 못 만들면** 앞뒤가 어긋난 것이다.
at(CI_COMMIT_SHORT_SHA="1a2b3c4", **NIGHTLY)
assert D.dry_run() is False and D.channel() == D.DEV
assert (D.decide_version() or "").startswith("dev-"), "새벽은 dev 채널이어야 한다"
at(CI_COMMIT_SHORT_SHA="1a2b3c4", **MR)
assert D.dry_run() is False and D.channel() == D.QA
assert D.decide_version() == "qa-461-1a2b3c4"

# ── 6) 끝까지 — 진짜로 업로드를 부른다 ──
UPLOADED: list = []
D.R.zip_dir = lambda src, dst: (open(dst, "wb").write(b"zip"), 3)[1]
D.R.upload_package = lambda path, version, name: (UPLOADED.append((version, name)), 201)[1]
D.R.package_url = lambda version, name: f"https://lab.example/{version}/{name}"

work = tempfile.mkdtemp()
os.chdir(work)
os.mkdir("build")
open(D.VERSION_FILE, "w", encoding="utf-8").write("dev-20260915-1a2b3c4\n")
os.environ["SPACE_BUILD_DIR"] = os.path.join(work, "build")

at(CI_COMMIT_SHORT_SHA="1a2b3c4", **NIGHTLY)
assert D.cmd_publish("0") == 0
assert UPLOADED == [("dev-20260915-1a2b3c4", "metagame-dev-20260915-1a2b3c4.zip")], UPLOADED
assert open(D.PACKAGE_FILE, encoding="utf-8").read().strip().endswith(".zip"), \
    "publish-devbuild 가 읽는 release-package.txt 가 있어야 배치된다"

# 리허설이면 그 파일을 안 남긴다 — 다음 잡이 옛 값을 집으면 안 된다
UPLOADED.clear()
os.remove(D.PACKAGE_FILE)
at(CI_COMMIT_SHORT_SHA="1a2b3c4", CI_PIPELINE_SOURCE="push", CI_COMMIT_BRANCH="develop")
assert D.cmd_publish("0") == 0 and UPLOADED == []
assert not os.path.exists(D.PACKAGE_FILE)

# 빌드가 실패했으면 트리거와 무관하게 안 올린다
at(CI_COMMIT_SHORT_SHA="1a2b3c4", **NIGHTLY)
assert D.cmd_publish("1") == 1 and UPLOADED == []

print("OK  publish_devbuild — 트리거 6경우 · 한 방향 스위치 · 릴리스 판정과 분리 · 업로드 왕복")
