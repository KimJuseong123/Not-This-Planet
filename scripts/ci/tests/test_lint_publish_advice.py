"""경고 노출 — MR 코멘트 생성·갱신, 해소되면 ✅ 로 바뀜 (S15P21B202-90).

실행: python scripts/ci/tests/test_lint_publish_advice.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1443653", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "99",
                   "MM_WEBHOOK_URL": "https://mm.example/hook"})

import ci_common, mr_lint, mm_notify

MR = {"title": "[Infra] feat: 테스트 (S15P21B202-90)", "web_url": "https://lab/mr/99",
      "author": {"username": "rmawnsdud"}}


def harness(existing_notes):
    calls = {"notes": [], "mm": []}

    def http(method, url, headers=None, body=None, timeout=30):
        if url.startswith("https://mm.example"):
            calls["mm"].append(body["text"]); return 200, {}
        if url.endswith("/notes?per_page=100&sort=desc"):
            return 200, existing_notes
        if "/notes" in url:
            calls["notes"].append((method, body["body"])); return 201, {}
        raise AssertionError(url)
    ci_common.http = http
    mm_notify.http = http
    mr_lint.env = lambda k, d=None: os.environ.get(k, d)
    return calls


# 1) master 기반 경고 → 코멘트 + MM 카드
c = harness([])
mr_lint.publish_advice(ci_common.GitLab(), MR,
                       [("warn", "이 브랜치는 develop 이 아니라 master(또는 그 이전) 지점에서 갈라졌습니다 — 시작점 abcdef12")], True)
assert len(c["notes"]) == 1 and mr_lint.ADVICE_MARKER in c["notes"][0][1] and "확인 사항 1건" in c["notes"][0][1]
assert "force-with-lease" in c["notes"][0][1]
assert len(c["mm"]) == 1 and "🟠 **브랜치 기준 확인 필요**" in c["mm"][0] and "@rmawnsdud" in c["mm"][0] and "| 항목 | 내용 |" in c["mm"][0]

# 2) 뒤처짐 경고만 → 코멘트만(MM 없음)
c = harness([])
mr_lint.publish_advice(ci_common.GitLab(), MR, [("warn", "브랜치 시작점이 develop 보다 40커밋 뒤처졌습니다")], False)
assert len(c["notes"]) == 1 and not c["mm"]

# 3) 경고 없음 + 기존 코멘트 없음 → 아무 것도 안 함
c = harness([])
mr_lint.publish_advice(ci_common.GitLab(), MR, [("ok", "브랜치 시작점 OK (develop 대비 2커밋 뒤)")], False)
assert not c["notes"] and not c["mm"]

# 4) 경고 없음 + 기존 코멘트 있음 → ✅ 해소로 갱신
c = harness([{"id": 7, "body": mr_lint.ADVICE_MARKER + "\n⚠ 확인 사항 1건"}])
mr_lint.publish_advice(ci_common.GitLab(), MR, [("ok", "브랜치 시작점 OK")], False)
assert len(c["notes"]) == 1 and "해소되었습니다" in c["notes"][0][1] and c["notes"][0][0] == "PUT"
print("TEST90c OK")
