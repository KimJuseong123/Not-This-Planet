"""Draft 접두어 반복 제거 · 모든 경고가 하나의 MR 코멘트에 실리는지 (S15P21B202-105).

실행: python scripts/ci/tests/test_lint_draft_and_advice.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

Draft 를 한 번만 떼면 남은 'Draft:' 가 요약의 일부로 취급돼 제목 보정이 중복된다
(실측 2026-08-28: 'Draft: [Infra] docs: Draft: [Infra] docs: …').
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1443653", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "49"})

import ci_common, mr_lint as L

# ── ① Draft 접두어 반복 제거
assert ci_common.strip_draft("Draft: [Infra] docs: x (S15P21B202-1)") == ("[Infra] docs: x (S15P21B202-1)", True)
assert ci_common.strip_draft("Draft: Draft: [Infra] docs: x (S15P21B202-1)") == ("[Infra] docs: x (S15P21B202-1)", True)
assert ci_common.strip_draft("draft: DRAFT: WIP: 제목")[1] is True
assert ci_common.strip_draft("[Infra] docs: x (S15P21B202-1)") == ("[Infra] docs: x (S15P21B202-1)", False)
# 중복 접두어여도 제목 정규식이 통과해야 한다(보정 중복 방지)
t, d = ci_common.strip_draft("Draft: Draft: [Infra] docs: B6c 테스트 (S15P21B202-104)")
assert ci_common.TITLE_RE.match(t) and d, t

# ── ② 경고 세 건이 **하나의** 코멘트에 모두 실린다
notes = []


def fake_http(method, url, headers=None, body=None, timeout=30):
    if url.endswith("/notes?per_page=100&sort=desc"):
        return 200, []
    if "/notes" in url:
        notes.append(body["body"]); return 201, {}
    raise AssertionError(url)


ci_common.http = fake_http
MR = {"title": "t", "web_url": "https://lab/mr/49", "author": {"username": "rmawnsdud"}}
warns = ["MR 작성자(금준영)와 Jira 담당자(김수호)가 다릅니다 — …",
         "이슈가 이미 완료 상태 — 후속 작업이면 새 이슈를 만들 것",
         "브랜치 시작점이 develop 보다 40커밋 뒤처졌습니다"]
L.publish_advice(ci_common.GitLab(), MR, [("warn", w) for w in warns], False)

assert len(notes) == 1, notes
body = notes[0]
assert "확인 사항 3건" in body and all(w[:20] in body for w in warns), body

print("TEST105 OK")
