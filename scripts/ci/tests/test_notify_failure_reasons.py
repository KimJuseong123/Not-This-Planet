"""CI 실패 이유 추출과 카드·MR 코멘트 반영 (S15P21B202-102).

실행: python scripts/ci/tests/test_notify_failure_reasons.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1443653", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "41",
                   "CI_PIPELINE_ID": "175444", "MM_WEBHOOK_URL": "https://mm.example/hook"})

import ci_common, mm_notify as N

TRACE = ("===== mr-lint =====\n"
         "  ✅ Draft 아님\n"
         "  ❌ 작성자 체크리스트 미완료 5개 — 전부 체크(해당 없으면 '해당 없음' 표기 후 체크)\n"
         "  ❌ 대상 브랜치는 develop(릴리즈 MR만 master): 'S15P21B202-91-x'\n"
         "== mr-lint: 통과 8 · 경고 0 · 실패 2 ==\n")
sent, notes = [], []


def fake_http(method, url, headers=None, body=None, timeout=30):
    if url.startswith("https://mm.example"):
        sent.append(body["text"]); return 200, {}
    if url.endswith("/jobs?per_page=50") or "/pipelines/" in url and url.endswith("/jobs?per_page=50"):
        return 200, [{"id": 1, "name": "mr-lint", "status": "failed", "allow_failure": False,
                      "web_url": "https://lab/jobs/1"},
                     {"id": 2, "name": "ai-review", "status": "failed", "allow_failure": True,
                      "web_url": "https://lab/jobs/2"}]
    if url.endswith("/jobs/1/trace"):
        return 200, TRACE
    if url.endswith("/notes?per_page=100&sort=desc"):
        return 200, []
    if "/notes" in url:
        notes.append(body["body"]); return 201, {}
    if "/merge_requests/41" in url:
        return 200, {"title": "[Puzzle] feat: 1stage 탐험 구조 생성 (S15P21B202-91)", "web_url": "https://lab/mr/41",
                     "author": {"username": "suho6347"}}
    raise AssertionError(url)


ci_common.http = fake_http
N.http = fake_http
rc = N.ci_failed(ci_common.GitLab())
assert rc == 0, rc
card = sent[0]
print(card)
# -109 이후 카드의 이유 칸은 <br> 없이 번호로 잇는다(Mattermost 표 셀은 줄바꿈을 렌더하지 않음)
assert "| 이유 |" in card and "작성자 체크리스트 미완료 5개" in card, card
assert "<br>" not in card and "1) " in card and "2) " in card, card
assert "@suho6347" in card
assert len(notes) == 1 and N.FAIL_MARKER in notes[0]
assert "**mr-lint** — 작성자 체크리스트 미완료" in notes[0] and "대상 브랜치는 develop" in notes[0], notes[0]
# allow_failure 잡(ai-review)은 실패로 세지 않는다
assert "ai-review" not in card.split("| 실패한 잡 |")[1].split("|")[0]
print("\nTEST102 OK")
