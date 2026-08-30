"""리뷰 요청 알림은 정확히 한 번 · 실패 코멘트는 길이 상한 (S15P21B202-134).

실행: python scripts/ci/tests/test_notify_once.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "1",
                   "MM_WEBHOOK_URL": "https://mm.example/h"})

import ci_common  # noqa: E402
import mm_notify as N  # noqa: E402

MR = {"title": "[Infra] docs: x (S15P21B202-1)", "web_url": "https://lab/mr/1",
      "author": {"username": "rmawnsdud"}, "reviewers": [], "draft": False}


def run_opened(pipeline_ids, current, list_ok=True):
    sent = []

    def http(method, url, headers=None, body=None, timeout=30):
        if url.startswith("https://mm.example"):
            sent.append(body["text"]); return 200, {}
        if "/pipelines" in url:
            return (200, [{"id": i} for i in pipeline_ids]) if list_ok else (500, {})
        return 200, MR

    os.environ["CI_PIPELINE_ID"] = str(current)
    ci_common.http = http
    N.http = http
    N.mr_opened(ci_common.GitLab())
    return sent


# 1) 파이프라인이 하나 — 보낸다
assert len(run_opened([100], 100)) == 1
print("  첫 파이프라인 → 리뷰 요청 1건")

# 2) 파이프라인이 여러 개인데 내가 가장 오래된 것 — 보낸다
#    (예전 코드는 len(pls) != 1 이라 여기서 영구히 안 보냈다)
assert len(run_opened([100, 101, 102], 100)) == 1
print("  파이프라인 3개 + 내가 최초 → 리뷰 요청 1건 (예전에는 미발송)")

# 3) 뒤에 만들어진 파이프라인 — 보내지 않는다
assert run_opened([100, 101], 101) == []
print("  뒤에 만들어진 파이프라인 → 생략")

# 4) 목록 조회 실패 — 판정 불가라 생략(매 파이프라인 도배 방지)
assert run_opened([], 100, list_ok=False) == []
print("  목록 조회 실패 → 생략")


# 5) 실패 코멘트 길이 상한
def run_failed(n_reasons):
    notes = []
    trace = "".join(f"  ❌ 이유 {i} " + ("가" * 200) + "\n" for i in range(n_reasons))

    def http(method, url, headers=None, body=None, timeout=30):
        if url.startswith("https://mm.example"):
            return 200, {}
        if "/pipelines/999/jobs" in url:
            return 200, [{"id": 1, "name": "repo-hygiene", "status": "failed",
                          "allow_failure": False, "web_url": "https://lab/j/1"}]
        if url.endswith("/jobs/1/trace"):
            return 200, trace
        if url.endswith("/notes?per_page=100&sort=desc"):
            return 200, []
        if "/notes" in url:
            notes.append(body["body"]); return 201, {}
        return 200, MR

    os.environ["CI_PIPELINE_ID"] = "999"
    ci_common.http = http
    N.http = http
    N.ci_failed(ci_common.GitLab())
    return notes[0] if notes else ""

body = run_failed(60)
assert body, "코멘트가 없다"
assert len(body) <= N.MAX_COMMENT_CHARS + 500, len(body)
assert "외 " in body and "건" in body, body[-200:]
print(f"  이유 60건 → 코멘트 {len(body)}자로 제한 + '외 N건' 안내")

body2 = run_failed(2)
assert "외 " not in body2.split("고친 뒤")[0], body2
print("  이유 2건 → 그대로 전부 표시")
print("TEST_NOTIFY_ONCE OK")
