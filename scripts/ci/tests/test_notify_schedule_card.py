"""예약 실행 결과 카드 — **성공해도 보낸다** (S15P21B202-675).

실행: python scripts/ci/tests/test_notify_schedule_card.py
네트워크·토큰 없이 `ci_common.http` 를 가짜로 바꿔 돌린다.

**왜 성공에도 보내는가.** 새벽에 하는 일이 늘었다 — 전체 테스트(`-662`) · 느린 테스트
측정(`-617`) · 캐시 데우기(`-664`). 그런데 알림은 실패에만 있었다. 2026-09-22 01:03
데우기가 4.2분에 성공했는데 채널에 **아무것도 안 갔다.**

특히 데우기는 스케줄 브랜치가 사라지거나 ref 를 안 옮기면 **실패하지 않고 그냥 안
돈다.** 파이프라인은 전부 초록이고 캐시만 식는다 — `-664` 가 스스로 적어 둔 위험이
바로 그것이다. 카드가 매일 오면 **안 온 날이 신호**가 된다.

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 통째로 사라진다.
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))

os.environ.update({
    "CI_PROJECT_ID": "1",
    "CI_PROJECT_URL": "https://lab/g/p",
    "GITLAB_TOKEN": "x",
    "CI_PIPELINE_ID": "900",
    "CI_PIPELINE_URL": "https://lab/g/p/-/pipelines/900",
    "CI_COMMIT_BRANCH": "S15P21B202-664-cache-warm",
    "CI_COMMIT_SHA": "c8448016092349851750d39822865a0b",
    "CI_COMMIT_SHORT_SHA": "c8448016",
    "CI_COMMIT_TITLE": "Merge branch 'x' into 'develop'",
    # 예약 실행의 멘션 대상이 되던 값. **불리면 안 된다** (-608).
    "GITLAB_USER_LOGIN": "rmawnsdud",
    "MM_WEBHOOK_URL": "https://mm.example/h",
    "CI_JOB_NAME": "mm-nightly-result",
    "CI_PIPELINE_CREATED_AT": "2026-09-21T16:03:00Z",   # KST 01:03
    # **직접 정한다** (S15P21B202-533). 안 정하면 이 테스트를 돌리는 잡 자신의 값이
    # 새어 들어와 로컬에서는 통과하고 CI 에서만 깨진다.
    "CI_PIPELINE_SOURCE": "schedule",
    "WARM_UNITY_CACHE": "true",   # 01:00 데우기 예약이다
})
os.environ.pop("CI_MERGE_REQUEST_IID", None)

import ci_common  # noqa: E402
import mm_notify as N  # noqa: E402

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


INFRA_MM = N.BY_GITLAB[N.INFRA]["mm"]
USER_MM = N.BY_GITLAB["rmawnsdud"]["mm"] if "rmawnsdud" in N.BY_GITLAB else None


def run(jobs, traces=None):
    """카드 한 장을 보낸다. (종료코드, 보낸 본문)."""
    traces = traces or {}
    sent = []

    def http(method, url, headers=None, body=None, timeout=30):
        if url.startswith("https://mm.example"):
            sent.append(body["text"]); return 200, {}
        if "/pipelines/900/jobs" in url:
            return 200, jobs
        m = re.search(r"/jobs/(\d+)/trace", url)
        if m:
            return 200, traces.get(int(m.group(1)), "")
        return 200, {}

    ci_common.http = http
    N.http = http
    rc = N.schedule_result(ci_common.GitLab())
    return rc, (sent[0] if sent else "")


OK_JOBS = [
    {"id": 11, "name": "ci-tests", "status": "success", "duration": 25.6,
     "allow_failure": False, "web_url": "https://lab/j/11"},
    {"id": 12, "name": "build-devbuild", "status": "success", "duration": 252.0,
     "allow_failure": False, "web_url": "https://lab/j/12"},
    # 자기 자신은 아직 돌고 있다 — 목록에 넣으면 "한 일" 에 제 이름이 찍힌다.
    {"id": 13, "name": "mm-nightly-result", "status": "success", "duration": 3.0,
     "allow_failure": True, "web_url": "https://lab/j/13"},
]

print()
print("== 예약 결과 카드 (S15P21B202-675) ==")


# ── ① 성공해도 카드가 나간다 — 이 파일의 핵심
rc, t = run(OK_JOBS)
check(rc == 0, f"성공 카드의 종료 코드가 {rc}")
check(bool(t), "예약이 성공했는데 카드가 한 장도 안 나갔다 — 조용한 멈춤을 못 잡는다")
check(t.startswith("\U0001f7e2"), f"성공 카드가 초록이 아니다: {t[:40]}")
check("01:03 예약" in t, f"어느 예약인지 없다: {t[:80]}")
print("  성공해도 카드가 나간다 ✅")


# ── ② **아무도 부르지 않는다** (S15P21B202-608)
#     예약 실행의 멘션 대상은 `GITLAB_USER_LOGIN`, 곧 마지막 머지를 누른 사람이라
#     실패와 아무 상관이 없다. 매일 같은 시각에 엉뚱한 사람을 부르면 그 채널의
#     멘션이 통째로 무시된다 — 정작 불러야 할 때 안 읽힌다.
check("@" not in t, f"성공 카드가 누군가를 부른다: {t}")
print("  멘션 없이 내용만 ✅")


# ── ③ 무엇을 했는지 적는다 (잡 이름 + 소요)
check("ci-tests" in t and "build-devbuild" in t, f"한 일이 안 적혔다: {t}")
check("4.2분" in t, f"소요 시간이 안 적혔다(252초 → 4.2분): {t}")
#     **값 셀을 정확히 집어야 한다** (`!570` AI 리뷰). `split("한 일")[-1].split("|")[2]`
#     로 쓰면 값이 아니라 그 뒤의 개행을 집어 **무엇을 넣어도 통과하는 죽은 검사**가 된다.
_ran = re.search(r"\| 한 일 \| (.*?) \|", t)
check(_ran is not None, f"'한 일' 행을 못 찾았다: {t}")
check(_ran is None or "mm-nightly-result" not in _ran.group(1),
      f"'한 일' 에 알림 잡 자신이 들어 있다: {_ran.group(1) if _ran else ''}")
print("  무엇을 했는지 적는다 ✅")


# ── ④ **카드가 없는 날이 신호**라는 것을 카드가 말한다
#     적지 않으면 읽는 사람이 매일 오는 이 카드를 그냥 소음으로 여기고, 그러면
#     안 온 날도 못 알아챈다 — 카드를 보내는 뜻이 사라진다.
check("없는 날" in t, f"이 카드가 안 오는 것이 신호라는 말이 없다: {t}")
print("  안 온 날이 신호라는 것을 적는다 ✅")


# ── ⑤ 잡 목록을 못 읽어도 **카드는 나간다**
#     이 카드의 존재 이유가 "왔는가" 다. 내용이 비었다고 보내지 않으면 멈춘 것과
#     구분되지 않는다 — 조용해지는 쪽으로 틀리면 안 된다.
rc, t_empty = run([])
check(bool(t_empty), "잡 목록이 비었다고 카드를 통째로 생략한다 — 멈춘 것과 구분되지 않는다")
print("  잡 목록을 못 읽어도 카드는 나간다 ✅")


# ── ⑤b **하기로 한 잡이 안 돌았으면 초록이 아니다** (`!570` AI 리뷰)
#
#     성공한 잡만 세면, 데우기 잡이 취소되거나 규칙이 어긋나 **아예 만들어지지 않은**
#     날에도 실패가 0건이라 🟢 가 나간다 — 이 카드가 잡으려던 *조용히 안 도는* 실패
#     모드와 정확히 겹친다. 그러면 카드가 있으나 마나다.
ONLY_LIGHT = [
    {"id": 31, "name": "ci-tests", "status": "success", "duration": 25.0,
     "allow_failure": False, "web_url": "https://lab/j/31"},
]
rc, t_miss = run(ONLY_LIGHT)
check(bool(t_miss), "기대한 잡이 없는데 카드가 아예 안 나갔다")
check(not t_miss.startswith("🟢"),
      f"`build-devbuild` 가 안 돌았는데 초록 '완료' 카드가 나간다 — 조용한 멈춤을 덮는다: {t_miss[:60]}")
check("build-devbuild" in t_miss and "안 돈 잡" in t_miss,
      f"무엇이 안 돌았는지 카드가 말하지 않는다: {t_miss}")
check("@" not in t_miss, f"덜 돈 카드가 누군가를 부른다: {t_miss}")
print("  하기로 한 잡이 빠지면 초록이 아니다 ✅")


# ── ⑤c 기대 잡 목록이 `.gitlab-ci.yml` 의 규칙과 **같은 조건**을 쓴다
#      어긋나면 위 검사가 있으나 마나다 — 규칙에 있는 잡을 기대 목록이 모르면
#      그 잡이 통째로 빠져도 초록이 나간다.
SRC = io.open(os.path.join(ROOT, "scripts", "ci", "mm_notify.py"), encoding="utf-8").read()
scope = SRC[SRC.index("def schedule_result"):]
scope = scope.split(chr(10) + "def ", 1)[0]
for var in ("WARM_UNITY_CACHE", "NIGHTLY_DEPLOY_DEV"):
    check(var in scope, f"기대 잡을 고를 때 {var} 를 보지 않는다")
check("build-devbuild" in scope, "기대 잡에 build-devbuild 가 없다")
print("  기대 잡을 예약 변수로 고른다 ✅")


# ── ⑥ 실패했으면 **실패 카드**다 (초록으로 덮지 않는다)
BAD_JOBS = [
    {"id": 21, "name": "unity-compile", "status": "failed", "duration": 1572.0,
     "allow_failure": False, "web_url": "https://lab/j/21"},
    {"id": 22, "name": "build-devbuild", "status": "success", "duration": 240.0,
     "allow_failure": False, "web_url": "https://lab/j/22"},
]
rc, t_bad = run(BAD_JOBS, traces={21: "  ❌ 새로 실패하는 테스트 1개\n"})
check(bool(t_bad), "예약이 실패했는데 카드가 없다")
check(t_bad.startswith("\U0001f534"), f"실패인데 카드가 빨갛지 않다: {t_bad[:60]}")
check("unity-compile" in t_bad, f"실패한 잡 이름이 없다: {t_bad}")
check("@" not in t_bad, f"예약 실패 카드가 누군가를 부른다(-608): {t_bad}")
print("  실패는 빨간 카드 · 역시 멘션 없음 ✅")


# ── ⑦ 배선: 새벽 작업은 `when: always`, 아침 점검은 `on_failure`
#     09:30 점검에는 `mm-digest` 가 제 카드를 매일 보낸다 — 성공까지 알리면 같은
#     아침에 두 장이 간다.
CI = io.open(os.path.join(ROOT, ".gitlab-ci.yml"), encoding="utf-8").read()
m = re.search(r"(?ms)^mm-nightly-result:.*?(?=^[a-z][a-z0-9-]*:|\Z)", CI)
check(m is not None, "mm-nightly-result 잡을 못 찾았다")
JOB = m.group(0) if m else ""
code = "\n".join(l for l in JOB.splitlines() if not l.lstrip().startswith("#"))
check("schedule-result" in code, "mm-nightly-result 가 schedule-result 를 부르지 않는다")
check("needs:" not in code,
      "needs 가 생겼다 — 스테이지 순서로 미뤄야 새벽 잡 전부를 기다린다(-344·-533)")

rules = code[code.index("rules:"):] if "rules:" in code else ""
always = [l for l in rules.splitlines() if "when: always" in l]
onfail = [l for l in rules.splitlines() if "when: on_failure" in l]
check(bool(always), "새벽 작업이 성공했을 때 보내는 규칙(when: always)이 없다")
check(bool(onfail),
      "아침 점검용 on_failure 규칙이 없다 — 09:30 에 mm-digest 와 카드가 두 장 간다")
warm_rule = [l for l in rules.splitlines()
             if "NIGHTLY_DEPLOY_DEV" in l and "WARM_UNITY_CACHE" in l]
check(bool(warm_rule),
      "always 규칙이 새벽 배포·데우기로 좁혀져 있지 않다 — 아침 점검에서도 성공 카드가 간다")
print("  새벽은 always · 아침 점검은 on_failure ✅")


if fails:
    print(f"\n❌ 실패 {len(fails)}건")
    for f in fails:
        print(f"  · {f}")
    raise SystemExit(1)
print("TEST675 OK")
