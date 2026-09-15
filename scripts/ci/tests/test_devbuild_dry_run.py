"""dev·qa 빌드가 레지스트리에 올라가는가 (S15P21B202-537).

실행: python scripts/ci/tests/test_devbuild_dry_run.py

`publish_devbuild` 가 `publish_release.dry_run()` 을 그대로 쓰면서 **dev·qa 두 채널이
영원히 리허설**이었다. 빌드는 초록인데(`return 0`) 패키지가 안 올라가, `dev-versions.json`
이 404 로 남았고 `--dev` 런처에 고를 것이 없었다. 여기서는 그 두 판정이 **갈라져 있는지**
를 못박는다 — 한쪽을 되돌리면 이 파일이 깨진다.
"""
import io
import os
import re
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
# 뒤에서 os.chdir 로 옮겨 가므로 **지금** 절대경로로 잡아 둔다.
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))

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

# rehearsal_requested() 는 '켰는가' 만 본다 — 트리거와 섞이면 안 된다
at(DEVBUILD_DRY_RUN="yes")
assert D.rehearsal_requested() is True and D.trigger_allowed() is False
at(**MR)
assert D.rehearsal_requested() is False and D.trigger_allowed() is True

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

# ── 6) `build-devbuild` 의 rules 와 **같은 집합**인가 ──
#
# `cmd_publish` 는 "잡이 돌았다면 여기도 통과한다" 를 전제로 어긋남을 실패로 낸다.
# 그 전제가 깨지면 90분짜리 새벽 빌드가 **마지막 줄에서** 빨갛게 죽는다. 주석으로
# 약속만 해 두면 rules 를 고치는 사람이 이 파일을 볼 까닭이 없으므로 여기서 못박는다.
yml = io.open(os.path.join(ROOT, ".gitlab-ci.yml"), encoding="utf-8").read().split(chr(10))
TOP = re.compile(r"^[A-Za-z_.][A-Za-z0-9_.-]*:")
s = next(i for i, l in enumerate(yml) if l.startswith("build-devbuild:"))
# 마지막 잡이면 다음 최상위 키가 없다 — 그때는 파일 끝까지가 그 잡이다.
e = next((i for i in range(s + 1, len(yml)) if TOP.match(yml[i])), len(yml))
# 공백 차이로는 깨지지 않게 한다 — 들여쓰기·줄맞춤을 고쳤다고 새벽 빌드가 빨개지면 안 된다.
found = [" ".join(l.split()) for l in yml[s:e] if l.strip().startswith("- if:")]
assert found == [
    '- if: $CI_PIPELINE_SOURCE == "schedule" && $NIGHTLY_DEPLOY_DEV == "true"',
    '- if: $CI_PIPELINE_SOURCE == "merge_request_event"',
], ("build-devbuild 의 rules 가 바뀌었다. trigger_allowed() 도 같이 고쳐야 한다 — "
    "안 그러면 그 트리거로 돈 빌드가 마지막 줄에서 실패한다." + chr(10) + repr(found))

# ── 7) 끝까지 — 진짜로 업로드를 부른다 ──
UPLOADED: list = []


def fake_zip(src, dst):
    with open(dst, "wb") as f:      # 핸들을 열어 둔 채 두면 Windows 에서 지워지지 않는다
        f.write(b"zip")
    return 3


ORIGINAL = (D.R.zip_dir, D.R.upload_package, D.R.package_url, os.getcwd())
D.R.zip_dir = fake_zip
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

# ── 8) 파이프라인에서 리허설로 빠지면 **실패**한다 · 그것도 zip 전에 ──
ZIPPED: list = []
_zip = D.R.zip_dir
D.R.zip_dir = lambda src, dst: (ZIPPED.append(dst), _zip(src, dst))[1]
# 잡이 돌았다는 건 `build-devbuild` 의 rules 를 통과했다는 뜻이다. 그런데도 리허설이면
# rules 와 trigger_allowed() 가 어긋난 것이고, 90분 빌드가 통째로 버려진다. `-537` 의
# 증상이 **초록 잡**이었던 까닭이 `return 0` 이었으므로 여기서 갈라 둔다.
UPLOADED.clear()
os.remove(D.PACKAGE_FILE)
at(CI_COMMIT_SHORT_SHA="1a2b3c4", CI_PIPELINE_SOURCE="push", CI_COMMIT_BRANCH="develop")
assert D.cmd_publish("0") == 1, "조용히 초록으로 끝나면 -537 이 그대로 재발한다"
assert UPLOADED == [] and not os.path.exists(D.PACKAGE_FILE)
assert ZIPPED == [], "어긋남이 확정인데 156MB 를 묶고 나서 죽을 이유가 없다"
assert D.mismatch() is True

# 새벽 스케줄인데 NIGHTLY_DEPLOY_DEV 가 빠진 경우 — 가장 있을 법한 재발 경로
at(CI_COMMIT_SHORT_SHA="1a2b3c4", CI_PIPELINE_SOURCE="schedule", CI_COMMIT_BRANCH="develop")
assert D.cmd_publish("0") == 1

# 반대로 **일부러 켠** 리허설은 조용히 통과하고, zip 까지 진짜로 한다 — 그것이 리허설의 값어치다
at(CI_COMMIT_SHORT_SHA="1a2b3c4", DEVBUILD_DRY_RUN="1", **NIGHTLY)
assert D.cmd_publish("0") == 0 and UPLOADED == []
assert ZIPPED and D.mismatch() is False

# 로컬에서 손으로 돌릴 때도 실패가 아니다 (CI_PIPELINE_SOURCE 가 없다)
at(CI_COMMIT_SHORT_SHA="1a2b3c4")
assert D.cmd_publish("0") == 0 and UPLOADED == []
assert not os.path.exists(D.PACKAGE_FILE)

# 빌드가 실패했으면 트리거와 무관하게 안 올린다
at(CI_COMMIT_SHORT_SHA="1a2b3c4", **NIGHTLY)
assert D.cmd_publish("1") == 1 and UPLOADED == []

# 모듈 전역을 건드렸으므로 되돌린다 — run_all 은 프로세스를 나누지만 여기에 기대지 않는다.
D.R.zip_dir, D.R.upload_package, D.R.package_url, _cwd = ORIGINAL
os.chdir(_cwd)

print("OK  publish_devbuild — 트리거 6경우 · 한 방향 스위치 · 릴리스 판정과 분리 ·"
      " rules 대조 · 업로드 왕복 · 어긋남은 실패")
