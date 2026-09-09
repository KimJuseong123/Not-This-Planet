"""재시도가 실패한 잡을 가려도 카드가 이름과 이유를 말한다 (S15P21B202-361).

실행: python scripts/ci/tests/test_notify_retried.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

**왜 이 테스트가 있나.** GitLab 의 기본 잡 목록은 이름당 최신 시도만 준다. 빠르게 실패한
검사(`mr-lint` 6초)를 재실행하는 동안 느린 잡(`unity-compile` 2분)이 아직 돌고 있으면,
`when: on_failure` + `needs` 인 알림 잡이 도는 시점에 목록에 실패 상태 잡이 하나도 없다.
2026-09-09 실측(파이프라인 184897)에서 카드가 이렇게 나갔다.

    | 실패한 잡 | 파이프라인 |                          ← 잡 이름이 아니다
    | 이유      | 로그에서 이유를 찾지 못함 |          ← 로그에는 있었다

카드가 거짓을 말한 것은 아니고 **쓸모가 없어진 것**이며, 그 카드의 존재 이유가 로그를
열지 않고도 무엇이 왜 깨졌는지 아는 것이다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1443653", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "41",
                   "CI_PIPELINE_ID": "184897", "MM_WEBHOOK_URL": "https://mm.example/hook",
                   "CI_PIPELINE_URL": "https://lab/pipelines/184897"})

import ci_common  # noqa: E402
import mm_notify as N  # noqa: E402

print("― S15P21B202-361 재시도에 가려진 실패 ―")

TRACE = ("===== mr-lint =====\n"
         "  ❌ 본문 템플릿 섹션 누락: ## 리뷰어 확인 — 템플릿 섹션 제목은 지우지 말 것\n"
         "== mr-lint: 실패 1 ==\n")

MR = {"title": "[Infra] fix(ci): x (S15P21B202-347)", "web_url": "https://lab/mr/41",
      "author": {"username": "rmawnsdud"}}

# 실측 그대로의 모양 — 500782 실패 → 500804 재실행 통과. `include_retried` 없이 물으면
# 최신 시도(500804, 통과)만 오고, 붙여 물으면 둘 다 온다.
LINT_FAILED = {"id": 500782, "name": "mr-lint", "status": "failed", "allow_failure": False,
               "web_url": "https://lab/jobs/500782"}
LINT_RETRY_OK = {"id": 500804, "name": "mr-lint", "status": "success", "allow_failure": False,
                 "web_url": "https://lab/jobs/500804"}
UNITY_OK = {"id": 500790, "name": "unity-compile", "status": "success", "allow_failure": False,
            "web_url": "https://lab/jobs/500790"}


def run(live: list[dict], retried: list[dict] | None):
    """(카드, MR 코멘트들) — live 는 기본 조회, retried 는 include_retried 조회 결과."""
    sent, notes = [], []

    def http(method, url, headers=None, body=None, timeout=30):
        if url.startswith("https://mm.example"):
            sent.append(body["text"]); return 200, {}
        if "/pipelines/" in url and "include_retried=true" in url:
            return (200, retried) if retried is not None else (500, {})
        if "/pipelines/" in url and "/jobs?per_page=100" in url:
            return 200, live
        if url.endswith("/jobs/500782/trace"):
            return 200, TRACE
        if url.endswith("/notes?per_page=100&sort=desc"):
            return 200, []
        if "/notes" in url:
            notes.append(body["body"]); return 201, {}
        if "/merge_requests/41" in url:
            return 200, MR
        raise AssertionError(url)

    ci_common.http = http
    N.http = http
    assert N.ci_failed(ci_common.GitLab()) == 0
    return sent[0], notes


def cell(card: str, name: str) -> str:
    return card.split(f"| {name} |", 1)[1].split("|", 1)[0]


# ── ① 재시도에 가려진 실패 — 이름·이유가 뜨고 통과 사실도 말한다 ────────────
_card, _notes = run([LINT_RETRY_OK, UNITY_OK], [LINT_FAILED, LINT_RETRY_OK, UNITY_OK])
assert "mr-lint" in cell(_card, "실패한 잡"), _card
assert "재시도 뒤 통과" in cell(_card, "실패한 잡"), "이미 지나간 실패임을 말해야 한다: " + _card
assert "본문 템플릿 섹션 누락" in cell(_card, "이유"), _card
assert "로그에서 이유를 찾지 못함" not in _card, "눈먼 카드가 남아 있다: " + _card
assert "500782" in _card, "실패한 그 시도로 링크해야 한다: " + _card
print("  재시도에 가려진 실패 → 이름·이유·'재시도 뒤 통과' ✅")

# ── ② 지금 실패가 있으면 재시도 조회를 하지 않고 그대로 쓴다 ────────────────
#     그 경우 include_retried 를 부르면 안 된다(호출이 500 이어도 결과가 같아야 한다).
_card, _notes = run([LINT_FAILED, UNITY_OK], None)
assert "mr-lint" in cell(_card, "실패한 잡") and "재시도 뒤 통과" not in _card, _card
assert "본문 템플릿 섹션 누락" in cell(_card, "이유"), _card
print("  현재 실패가 있으면 그대로 · 재시도 조회에 매이지 않는다 ✅")

# ── ③ 어느 쪽에도 실패가 없으면 예전 문구로 돌아간다 ───────────────────────
#     실패가 없는데 카드가 나가는 경우(needs 밖의 잡이 깨진 등)까지 없애지는 않는다 —
#     조용해지는 것이 가장 나쁘다.
_card, _notes = run([LINT_RETRY_OK, UNITY_OK], [LINT_RETRY_OK, UNITY_OK])
assert "파이프라인" in cell(_card, "실패한 잡"), _card
assert "로그에서 이유를 찾지 못함" in cell(_card, "이유"), _card
print("  실패가 아예 없으면 예전 문구 ✅")

# ── ④ 같은 이름이 여러 번 실패했으면 **가장 최근 실패 하나만** 싣는다 ──────
_older = dict(LINT_FAILED, id=500700, web_url="https://lab/jobs/500700")
_card, _notes = run([LINT_RETRY_OK], [_older, LINT_FAILED, LINT_RETRY_OK])
_jobs = cell(_card, "실패한 잡")
assert "500782" in _jobs and "500700" not in _jobs, "가장 최근 실패만 싣는다: " + _jobs
print("  같은 잡이 여러 번 실패해도 최근 것 하나 ✅")

# ── ⑤ `allow_failure` 잡의 가려진 실패는 세지 않는다 ───────────────────────
#     막지 않는 실패는 파이프라인을 빨갛게 만들지 않으므로 카드의 원인이 아니다.
_ai_failed = {"id": 500795, "name": "ai-review", "status": "failed", "allow_failure": True,
              "web_url": "https://lab/jobs/500795"}
_ai_ok = {"id": 500799, "name": "ai-review", "status": "success", "allow_failure": True,
          "web_url": "https://lab/jobs/500799"}
_card, _notes = run([_ai_ok, UNITY_OK], [_ai_failed, _ai_ok, UNITY_OK])
assert "ai-review" not in cell(_card, "실패한 잡"), _card
assert "파이프라인" in cell(_card, "실패한 잡"), _card
print("  allow_failure 잡의 가려진 실패는 세지 않는다 ✅")

# ── ⑥ 재시도 조회가 실패해도 카드는 나간다 ────────────────────────────────
#     조회 하나가 죽어서 알림이 사라지면 안 된다.
_card, _notes = run([LINT_RETRY_OK, UNITY_OK], None)
assert "🔴" in _card, "재시도 조회가 죽어도 카드는 나간다: " + _card
print("  재시도 조회가 죽어도 카드는 나간다 ✅")

# ── ⑦ develop 경로도 같은 규칙을 쓴다 ─────────────────────────────────────
assert N.branch_pipeline_jobs.__doc__ and "failed_jobs" in N.branch_pipeline_jobs.__doc__
import inspect  # noqa: E402
_src = inspect.getsource(N.branch_failed)
assert "failed_jobs(gl)" in _src, "develop 카드가 재시도 폴백을 안 쓴다"
assert "재시도 뒤 통과" in _src, "develop 카드도 그 사실을 말해야 한다"
print("  develop 카드도 같은 규칙 ✅")

print("TEST361 OK")
