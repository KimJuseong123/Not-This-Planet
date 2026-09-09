"""실패 이유 정제·개수 상한 · 카드 우선 발송 · 해소 코멘트 (S15P21B202-102 후속).

실행: python scripts/ci/tests/test_notify_sanitize_and_caps.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

- 이유 문자열에 `|` 나 `<b>` 가 있으면 MM 표가 깨지므로 치환한다.
- 위반이 많을 때 카드는 3건 + "외 N건", MR 코멘트는 20건 + "외 N건" 까지만.
- **카드를 코멘트보다 먼저** 보낸다 — 코멘트 실패로 카드까지 못 나가면 아무도 모른다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1443653", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "41",
                   "CI_PIPELINE_ID": "175444", "MM_WEBHOOK_URL": "https://mm.example/hook",
                   "CI_COMMIT_SHORT_SHA": "abc1234"})

import ci_common, mm_notify as N

MR = {"title": "[Puzzle] feat: x (S15P21B202-91)", "web_url": "https://lab/mr/41",
      "author": {"username": "suho6347"}}


def harness(trace, notes=None, note_fail=False):
    state = {"sent": [], "notes": [], "order": []}

    def fake_http(method, url, headers=None, body=None, timeout=30):
        if url.startswith("https://mm.example"):
            state["sent"].append(body["text"]); state["order"].append("card"); return 200, {}
        if "/pipelines/" in url and "/jobs?per_page=100" in url:
            return 200, [{"id": 1, "name": "mr-lint", "status": "failed", "allow_failure": False,
                          "web_url": "https://lab/jobs/1"}]
        if url.endswith("/jobs/1/trace"):
            return 200, trace
        if url.endswith("/notes?per_page=100&sort=desc"):
            return 200, (notes or [])
        if "/notes" in url:
            if note_fail:
                raise ConnectionError("network down")
            state["notes"].append(body["body"]); state["order"].append("note"); return 201, {}
        if "/merge_requests/41" in url:
            return 200, MR
        raise AssertionError(url)

    ci_common.http = fake_http; N.http = fake_http
    return state


# ── ① 정제: | 와 < > 치환 + 길이 제한, 표 구조 유지
long = "x" * 300
s = harness(f"  ❌ 규칙 | 위반 <b> {long}\n")
N.ci_failed(ci_common.GitLab())
card = s["sent"][0]
assert "／" in card and "&lt;b&gt;" in card and "…" in card, card
assert card.count("|") % 2 == 0, card
print("  이유 정제 · 표 구조 유지 ✅")

# ── ② 상한: ❌ 25줄 → 카드 3 + "외 22건", 코멘트 20 + "외 5건"
s = harness("\n".join(f"  ❌ 위반 {i}" for i in range(25)))
N.ci_failed(ci_common.GitLab())
card, note = s["sent"][0], s["notes"][0]
assert "… 외 22건" in card, card
assert note.count("- **mr-lint** —") == 20 and "… 외 5건" in note, note
print("  개수 상한(카드 3 · 코멘트 20) ✅")

# ── ③ 카드가 코멘트보다 먼저, 코멘트가 실패해도 카드는 나간다
s = harness("  ❌ 하나\n", note_fail=True)
rc = N.ci_failed(ci_common.GitLab())
assert rc == 0 and len(s["sent"]) == 1 and s["order"][0] == "card", s["order"]
print("  카드 우선 발송 ✅")

# ── ④ 해소: 기존 실패 코멘트가 있으면 ✅ 로 갱신
s = harness("", notes=[{"id": 9, "body": N.FAIL_MARKER + "\n🔴 CI 실패"}])
N.ci_resolved(ci_common.GitLab())
assert len(s["notes"]) == 1 and "해소되었습니다" in s["notes"][0] and "abc1234" in s["notes"][0], s["notes"]

# ── ⑤ 해소: 실패 코멘트가 없으면 아무 것도 하지 않는다
s = harness("", notes=[{"id": 9, "body": "그냥 코멘트"}])
N.ci_resolved(ci_common.GitLab())
assert not s["notes"], s["notes"]
print("  해소 코멘트 ✅")

print("TEST102b OK")
