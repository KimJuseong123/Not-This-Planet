"""실패 카드의 '이유' 칸 한 줄 렌더 · Draft MR 은 카드 생략 (S15P21B202-109).

실행: python scripts/ci/tests/test_notify_card_and_draft.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

- MM 표 안에서는 줄바꿈을 쓸 수 없어 `<br>` 을 넣었더니 글자로 보였다 → 번호를 붙여 한 줄로.
- Draft MR 은 아직 리뷰 요청이 아니므로 채널에 카드를 보내지 않고 MR 코멘트만 남긴다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "1",
                   "CI_PIPELINE_ID": "999", "MM_WEBHOOK_URL": "https://mm.example/h"})

import ci_common, mm_notify as N

# ── ① 이유 칸: <br> 없이 한 줄, 번호 + "외 N건"
sent = []
TRACE = "  ❌ 첫째 이유\n  ❌ 둘째 이유\n  ❌ 셋째 이유\n  ❌ 넷째 이유\n"


def http_reason(method, url, headers=None, body=None, timeout=30):
    if url.startswith("https://mm.example"):
        sent.append(body["text"]); return 200, {}
    if "/pipelines/999/jobs" in url:
        return 200, [{"id": 1, "name": "mr-lint", "status": "failed", "allow_failure": False,
                      "web_url": "https://lab/j/1"}]
    if url.endswith("/jobs/1/trace"):
        return 200, TRACE
    if url.endswith("/notes?per_page=100&sort=desc"):
        return 200, []
    if "/notes" in url:
        return 201, {}
    return 200, {"title": "t", "web_url": "https://lab/mr/1", "author": {"username": "rmawnsdud"}}


ci_common.http = http_reason
N.http = http_reason
N.ci_failed(ci_common.GitLab())
row = [l for l in sent[0].split("\n") if l.startswith("| 이유")][0]
assert "<br>" not in row, row
assert "1) 첫째 이유 2) 둘째 이유 3) 셋째 이유 … 외 1건" in row, row

# 이유가 하나뿐이면 번호를 붙이지 않는다
sent.clear()
TRACE = "  ❌ 단 하나의 이유\n"
N.ci_failed(ci_common.GitLab())
row1 = [l for l in sent[0].split("\n") if l.startswith("| 이유")][0]
assert "1)" not in row1 and "단 하나의 이유" in row1, row1
print("  이유 칸 한 줄 렌더 ✅")


# ── ② Draft MR 은 카드 없이 코멘트만
def run(title, draft_flag):
    out_sent, out_notes = [], []

    def http(method, url, headers=None, body=None, timeout=30):
        if url.startswith("https://mm.example"):
            out_sent.append(body["text"]); return 200, {}
        if "/pipelines/999/jobs" in url:
            return 200, [{"id": 1, "name": "mr-lint", "status": "failed", "allow_failure": False,
                          "web_url": "https://lab/j/1"}]
        if url.endswith("/jobs/1/trace"):
            return 200, "  ❌ Draft MR 금지 — …\n"
        if url.endswith("/notes?per_page=100&sort=desc"):
            return 200, []
        if "/notes" in url:
            out_notes.append(body["body"]); return 201, {}
        return 200, {"title": title, "draft": draft_flag, "web_url": "https://lab/mr/1",
                     "author": {"username": "rmawnsdud"}}

    ci_common.http = http; N.http = http
    return N.ci_failed(ci_common.GitLab()), out_sent, out_notes


rc, s, notes = run("Draft: [Infra] docs: x (S15P21B202-1)", True)
assert rc == 0 and not s and len(notes) == 1, (rc, len(s), len(notes))
print("  Draft: 카드 0개 · 코멘트 1개 ✅")

rc, s, notes = run("[Infra] docs: x (S15P21B202-1)", False)
assert rc == 0 and len(s) == 1 and len(notes) == 1, (rc, len(s), len(notes))
print("  일반 MR: 카드 1개 · 코멘트 1개 ✅")

print("TEST109 OK")
