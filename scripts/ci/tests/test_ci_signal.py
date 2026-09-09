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
assert "tags: [deploy]" in job("deploy-backend"), "deploy-backend 가 배포 러너로 안 간다"
print("  ④ 스풀 격리 검사 · 배포 태그 ✅")

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
assert "unity-recheck-args.txt" in _uc, (
    "재실행 결과를 unity_report.py 에 넘기지 않는다 — 돌려도 판정에 반영되지 않는다")
assert "-testFilter" in _uc, "재실행이 그 테스트만 골라 돌지 않는다"
# 계획 단계가 판정 파일을 남기면 잡이 재실행 전에 끝난다 — 두 호출의 순서를 확인한다
_plan_at = _uc.index("--recheck-plan")
_final_at = _uc.rindex("unity_report.py")
assert _plan_at < _final_at, "계획 호출이 최종 보고보다 뒤에 있다 — 순서가 뒤집혔다"
# 재실행 근거를 남겨야 판정을 다시 볼 수 있다
assert "unity-recheck-1.xml" in _uc, "재실행 결과가 아티팩트에 없다 — 판정 근거가 사라진다"
print("  ⑨ 플래키 재실행 단계 · 결과 전달 · 아티팩트 ✅")

print("TEST289 OK")
