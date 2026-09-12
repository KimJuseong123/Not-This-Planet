"""CI 가 거짓 신호를 내지 않는지 (S15P21B202-289).

실행: python scripts/ci/tests/test_ci_signal.py
네트워크·토큰 없이 돈다.

세 가지가 거짓을 말하거나 말할 수 있었다.
  ① `mm-ci-resolved` 의 needs 가 `mm-ci-failed` 와 달라, ci-tests·mr-overlap 이 실패해도
     '해소됨' 을 찍었다. 게다가 같은 파이프라인에서 🔴 와 ✅ 가 **같은 마커에 동시에** 써서
     나중에 끝난 쪽이 이겼다(비결정적)
  ② `stack-order` 와 `mr-overlap` 이 둘 다 `target_branch` 를 쓰는데 같은 스테이지에서
     동시에 돌아 서로의 결과를 못 봤다
  ③ `ci-tests` 가 `scripts/ci/**` 를 건드린 MR 에서만 돌아, 코드가 안 바뀌고 **시간이 흘러**
     깨지는 것을 아무도 못 봤다(-281)

①②③ 은 YAML 불변식이라 여기서 파일을 직접 읽어 검사한다. 사람이 나중에 needs 를 한쪽만
고치면 다시 어긋나므로, 주석이 아니라 테스트로 묶는다.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))   # repo 루트
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"
os.environ["GITLAB_TOKEN"] = "x"

import ci_common  # noqa: E402
import mm_digest as D  # noqa: E402

CI = open(os.path.join(ROOT, ".gitlab-ci.yml"), encoding="utf-8").read()


def job(name: str) -> str:
    m = re.search(r"(?ms)^" + re.escape(name) + r":.*?(?=^[a-z][a-z0-9-]*:|^# ──|\Z)", CI)
    assert m, f"잡을 못 찾음: {name}"
    return m.group(0)


NL_ = chr(10)


def needs_of(name: str) -> set[str]:
    b = job(name)
    m = re.search(r"(?ms)^  needs:(.*?)(?=^  [a-z])", b)
    if not m:
        return set()
    return set(re.findall(r"(?:- job:\s*|\[|,\s*)([a-z][a-z0-9-]+)", m.group(1)))


# ── ① 해소 알림은 실패 알림과 **같은 잡들**을 봐야 한다
failed, resolved = needs_of("mm-ci-failed"), needs_of("mm-ci-resolved")
assert failed, "mm-ci-failed 의 needs 를 못 읽었다"
assert failed == resolved, (
    f"mm-ci-failed 와 mm-ci-resolved 의 needs 가 다르다.\n"
    f"  failed  : {sorted(failed)}\n  resolved: {sorted(resolved)}\n"
    f"  → 한쪽에만 있는 잡이 실패하면 '해소됨' 오탐이 나가고, 같은 파이프라인에서 🔴 와 ✅ 가 충돌한다")
assert "ci-tests" in resolved and "mr-overlap" in resolved, sorted(resolved)

# ── ①-b `unity-compile` 이 두 목록에 다 있어야 한다 (S15P21B202-344)
#     빠져 있던 동안에는 그 잡만 실패하면 on_failure 조건을 못 만나 카드가 안 나갔다.
for name, got in (("mm-ci-failed", failed), ("mm-ci-resolved", resolved)):
    assert "unity-compile" in got, (
        f"{name} 의 needs 에 unity-compile 이 없다 → 그 잡만 실패하면 알림이 안 나간다(-344).\n"
        f"  현재: {sorted(got)}")

# ── ①-c 두 잡의 needs 는 **전부 optional** 이어야 한다 (S15P21B202-344)
#     develop push 파이프라인에는 mr-lint·repo-hygiene·mr-overlap 이 아예 없다
#     (그 잡들의 rules 가 merge_request_event 다). optional 이 아닌 needs 는 그 자리에서
#     파이프라인 생성을 실패시키므로, 알림이 안 나가는 것으로 끝나지 않고 **파이프라인이 죽는다.**
for name in ("mm-ci-failed", "mm-ci-resolved"):
    block = re.search(r"(?ms)^  needs:(.*?)(?=^  [a-z])", job(name)).group(1)
    entries = re.findall(r"- job:\s*([a-z][a-z0-9-]+)[^\n]*((?:\n(?!\s*- job:)[^\n]*)*)", block)
    assert entries, f"{name} 의 needs 항목을 못 읽었다"
    for jobname, tail in entries:
        assert "optional: true" in tail, (
            f"{name} 의 needs 에서 {jobname} 이 optional 이 아니다 → develop push 파이프라인이 "
            f"생성에 실패한다(-344)")

# ── ①-d 두 잡이 develop push 에서도 돌아야 한다 (S15P21B202-344)
#     2026-09-08 14:11 에 develop 이 두 커밋 연속 빨갰는데 카드가 0건이었다.
for name in ("mm-ci-failed", "mm-ci-resolved"):
    body = job(name)
    assert '$CI_COMMIT_BRANCH == "develop"' in body, (
        f"{name} 에 develop push 규칙이 없다 → develop 이 빨개도 알림이 없다(-344)")
print("  ① 해소/실패 알림의 needs 일치 ✅")

# ── ② target_branch 를 쓰는 두 잡은 순서가 있어야 한다
writers = [f for f in ("mr_overlap.py", "stack_order.py")
           if "update_mr(target_branch" in open(os.path.join(ROOT, "scripts/ci", f), encoding="utf-8").read()]
assert set(writers) == {"mr_overlap.py", "stack_order.py"}, writers
assert "mr-overlap" in needs_of("stack-order"), (
    "stack-order 가 mr-overlap 을 기다리지 않는다 — 둘 다 target_branch 를 쓰므로 동시에 돌면 덮어쓴다")
print("  ② 재대상 경합 방지(stack-order → mr-overlap) ✅")

# ── ③ 테스트가 스케줄에서도 돈다 + 실패해도 아침 카드는 나간다
assert 'CI_PIPELINE_SOURCE == "schedule"' in job("ci-tests"), "ci-tests 가 스케줄에서 안 돈다"
dg = job("mm-digest")
assert "when: always" in dg, "mm-digest 가 when: always 가 아니다 — ci-tests 가 실패하면 카드가 안 나간다"
assert "ci-tests" in needs_of("mm-digest"), "mm-digest 가 ci-tests 를 기다리지 않으면 결과를 실을 수 없다"
assert "when: always" in job("stack-order"), (
    "stack-order 에 when: always 가 없다 — mr-overlap 이 실패하면 순서 강제가 조용히 건너뛰어진다. "
    "only_allow_merge_if_pipeline_succeeds=false 라 빨간 파이프라인도 머지할 수 있다")
print("  ③ 스케줄 테스트 + 실패해도 카드 발송 · 순서 강제는 항상 실행 ✅")

# ── ④ 스풀 격리 회귀 검사가 살아 있다 (-252)
assert "/spool" in job("repo-hygiene"), "repo-hygiene 에 스풀 격리 검사가 없다"
# **배포 잡을 이름으로 세지 않는다.** -284 에서 하나가 둘로 갈렸는데, 이름을
# 적어 두는 방식이면 셋째가 생길 때 이 검사가 조용히 그것을 놓친다. 배포 잡의
# 정의는 "`deploy_backend.py` 를 돌리는 잡" 이므로 그것으로 찾는다.
# `^([a-z][a-z0-9-]*):` 는 `stages`·`variables`·`default`·`workflow` 같은 최상위
# 키까지 잡는다. 그것을 그대로 job() 에 넘기면 이 검사가 YAML 모양 변화에 걸려
# 넘어질 수 있다 — **잡의 표시는 `stage:` 를 가진 것**이므로 그것으로 먼저 거른다.
TOP_KEYS = re.findall(r"(?m)^([a-z][a-z0-9-]*):", CI)
ALL_JOBS = sorted({n for n in TOP_KEYS if re.search(r"(?m)^  stage:", job(n))})
assert {"deploy-dev", "deploy-prod", "unity-compile"} <= set(ALL_JOBS), ALL_JOBS

DEPLOYERS = sorted({n for n in ALL_JOBS if "deploy_backend.py" in job(n)})
assert len(DEPLOYERS) >= 2, f"배포 잡을 못 찾았다: {DEPLOYERS}"
for name in DEPLOYERS:
    body = job(name)
    assert "tags: [deploy]" in body, (
        f"{name} 가 배포 러너로 안 간다 — 무태그가 되면 스풀이 없는 공용 러너로 "
        f"흘러가거나, 반대로 다른 잡이 배포 러너로 들어온다(-252)")
    assert "interruptible: false" in body, (
        f"{name} 가 취소될 수 있다 — 배포 도중 취소되면 반쯤 적용된 상태가 남는다")

# **개발 배포는 스풀이 달라야 한다.** 이 한 줄이 없으면 develop 머지가
# 실유저 서버에 개발 jar 을 올린다 — 서버를 나눈 목적이 그것이다(-284).
assert "DEPLOY_SPOOL: /spool-dev" in job("deploy-dev"), (
    "deploy-dev 가 기본 스풀을 쓴다 — 개발 jar 이 배포 서버로 간다")
# 주석을 걷어 내고 본다 — deploy-prod 의 주석이 "DEPLOY_SPOOL 을 주지 않는다" 고
# 말하고 있어서, 글자만 찾으면 주석이 검사를 속인다.
prod = NL_.join(l for l in job("deploy-prod").split(NL_)
                if not l.strip().startswith("#"))
assert "DEPLOY_SPOOL" not in prod, (
    "deploy-prod 가 스풀을 따로 지정한다 — 기본값(/spool)이어야 한다")
print(f"  ④ 스풀 격리 검사 · 배포 잡 {len(DEPLOYERS)}개 태그·비취소·스풀 분리 ✅")

# ── ⑤ test_health — 통과·미완료는 조용하고, 실패만 말한다
CASES: dict[str, list[dict]] = {}


def fake_http(method, url, headers=None, body=None, timeout=30):
    if "/pipelines/" in url and "/jobs" in url:
        return 200, CASES.get("jobs", [])
    raise AssertionError(f"예상 못한 호출: {method} {url}")


ci_common.http = fake_http
D.http = fake_http
gl = ci_common.GitLab()

os.environ.pop("CI_PIPELINE_ID", None)
assert D.test_health(gl) is None, "파이프라인 id 가 없으면 조용해야 한다"
os.environ["CI_PIPELINE_ID"] = "178884"

CASES["jobs"] = []
assert D.test_health(gl) is None, "ci-tests 잡이 없으면 조용해야 한다"

# canceled 가 여기 들어 있는 것이 핵심이다. GitLab 이 중복 파이프라인의 대기 잡을 자동 취소하는데,
# 그것을 실패로 세면 아침 카드 맨 위에 거짓 경고가 뜬다 — 배포 카드가 같은 이유로 오탐을 냈다(-282).
for quiet in ("success", "running", "pending", "created", "skipped", "manual",
              "canceled", "waiting_for_resource", "preparing", "scheduled"):
    CASES["jobs"] = [{"id": 1, "name": "ci-tests", "status": quiet, "web_url": "https://lab/j/1"}]
    assert D.test_health(gl) is None, f"{quiet} 인데 말을 했다"
print("  ⑤ 통과·미완료는 조용함 ✅")

CASES["jobs"] = [{"id": 1, "name": "ci-tests", "status": "failed", "web_url": "https://lab/j/1"}]
msg = D.test_health(gl)
assert msg and "failed" in msg, msg
assert f"@{D.INFRA_MM}" in msg, "고칠 사람이 안 붙었다"
assert "https://lab/j/1" in msg, "로그 링크가 없다"
assert "-281" in msg, "왜 스케줄에서 도는지가 안 적혀 있다"
print("  ⑥ 실패는 @Infra 와 로그 링크를 달아 말함 ✅")

# 재시도가 있으면 **마지막** 잡을 본다
CASES["jobs"] = [{"id": 1, "name": "ci-tests", "status": "failed", "web_url": "https://lab/j/1"},
                 {"id": 9, "name": "ci-tests", "status": "success", "web_url": "https://lab/j/9"},
                 {"id": 5, "name": "mm-digest", "status": "running", "web_url": "https://lab/j/5"}]
assert D.test_health(gl) is None, "재시도로 통과했는데 실패라고 말했다"
print("  ⑦ 재시도 뒤 통과는 조용함 ✅")


def boom(method, url, headers=None, body=None, timeout=30):
    raise OSError("network down")


ci_common.http = boom
D.http = boom
msg = D.test_health(ci_common.GitLab())
assert msg and f"@{D.INFRA_MM}" in msg, "조회 실패도 조치할 사람을 붙여야 한다"
print("  ⑧ 조회 실패 → @Infra ✅")


# ── ⑨ 플래키 재실행 단계가 살아 있어야 한다 (S15P21B202-347)
#
# 이 단계가 사라지면 판정이 다시 "단 한 번의 실행" 을 근거로 막는다. 2026-09-09 에
# 그래서 MR 3건이 동시에 막혔다 — 같은 커밋 4회가 전부 실패했고 기준선만 통과였다.
# 조용히 없어질 수 있는 종류라 YAML 을 직접 읽어 못 박는다.
_uc = job("unity-compile")
assert "--recheck-plan" in _uc, (
    "unity-compile 에서 --recheck-plan 이 사라졌다 — 새 실패를 다시 돌리지 않으면 "
    "비결정 테스트가 무관한 MR 을 무작위로 막는다(-347)")
assert "--recheck-dir" in _uc, (
    "재실행 결과를 unity_report.py 에 넘기지 않는다 — 돌려도 판정에 반영되지 않는다")
assert "--baseline-from" in _uc, (
    "최종 판정이 기준선을 다시 조회한다 — 계획과 어긋날 창이 열린다(!272 리뷰)")
assert "-testFilter" in _uc, "재실행이 그 테스트만 골라 돌지 않는다"
# 계획 단계가 판정 파일을 남기면 잡이 재실행 전에 끝난다 — 두 호출의 순서를 확인한다
_plan_at = _uc.index("--recheck-plan")
_final_at = _uc.rindex("unity_report.py")
assert _plan_at < _final_at, "계획 호출이 최종 보고보다 뒤에 있다 — 순서가 뒤집혔다"
# 재실행 근거를 남겨야 판정을 다시 볼 수 있다
assert "unity-recheck-*.xml" in _uc, "재실행 결과가 아티팩트에 없다 — 판정 근거가 사라진다"
assert "unity-baseline.json" in _uc, "고정한 기준선이 아티팩트에 없다 — 판정을 다시 볼 수 없다"
# 횟수·상한은 **한 곳에만** 있어야 한다 — 파이썬에 같은 상수를 두면 한쪽만 바뀐다
assert "RECHECK_RUNS" in _uc and "RECHECK_MAX_NEW" in _uc, "재실행 횟수·상한이 변수로 없다"
import unity_report as _UR
assert not hasattr(_UR, "RECHECK_RUNS"), (
    "unity_report.py 에 RECHECK_RUNS 가 다시 생겼다 — YAML 과 이중 관리가 된다(!272 리뷰)")
# 지난 실행이 남긴 재실행 결과를 지워야 한다 — 워크스페이스가 재사용되면 옛 통과
# 기록으로 새 실패가 면제된다(!272 리뷰)
assert "rm -f" in _uc and "unity-recheck-*.xml" in _uc.split("rm -f")[1][:200], (
    "재실행 전에 지난 결과를 지우지 않는다 — 옛 통과 기록으로 새 실패가 면제될 수 있다")
# **파일 이름이 파이썬 상수와 YAML 양쪽에 있다.** 파이썬만 바꾸면 잡의 cat·
# --baseline-from 이 조용히 깨져 재실행 면제가 사라진다(!272 5차 리뷰).
# RECHECK_RUNS 처럼 한 곳으로 모을 수는 없다 — 잡이 파일을 읽어야 하고 파이썬이
# 써야 한다. 그래서 **양쪽이 같은 이름을 쓰는지**를 여기서 못 박는다.
for _const, _val in (("RECHECK_PLAN", _UR.RECHECK_PLAN),
                     ("RECHECK_FILTER", _UR.RECHECK_FILTER),
                     ("BASELINE_FILE", _UR.BASELINE_FILE)):
    assert _val in _uc, (
        f"unity_report.{_const} = {_val!r} 이 unity-compile 잡에 없다 — "
        f"한쪽만 바뀌면 재실행 면제가 조용히 사라진다")
print("  ⑨ 플래키 재실행 단계 · 결과 전달 · 아티팩트 · 지난 결과 정리 · 파일명 일치 ✅")

print("TEST289 OK")
