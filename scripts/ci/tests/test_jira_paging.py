"""Jira 검색 페이징과 '머지됐는데 완료 안 된 이슈' 매칭 (S15P21B202-83).

실행: python scripts/ci/tests/test_jira_paging.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"; os.environ["GITLAB_TOKEN"] = "x"; os.environ["JIRA_USER"] = "x"; os.environ["JIRA_API_TOKEN"] = "x"

import ci_common, mm_digest as d

calls = []
def fake_http(method, url, headers=None, body=None, timeout=30):
    calls.append((url, body))
    if "merge_requests" in url:
        return 200, [{"iid": 32, "web_url": "https://lab/mr/32", "source_branch": "S15P21B202-75", "merged_at": "2026-08-27T08:43:17.000Z"},
                     {"iid": 31, "web_url": "https://lab/mr/31", "source_branch": "S15P21B202-74-x", "merged_at": "2026-08-27T08:06:16.000Z"},
                     {"iid": 5, "web_url": "https://lab/mr/5", "source_branch": "S15P21B202-999-typo", "merged_at": "2026-08-27T09:00:00.000Z"},
                     {"iid": 2, "web_url": "https://lab/mr/2", "source_branch": "S15P21B202-34-old", "merged_at": "2026-08-01T00:00:00.000Z"},
                     {"iid": 9, "web_url": "https://lab/mr/9", "source_branch": "no-key-branch", "merged_at": "2026-08-27T09:00:00.000Z"}]
    # Jira search/jql: 2 페이지 (Jira 가 요청보다 작은 페이지를 돌려주는 경우 시뮬레이션)
    if body and body.get("nextPageToken") == "p2":
        return 200, {"issues": [{"key": "S15P21B202-74", "fields": {"summary": "테스트 이슈", "status": {"name": "진행 중"},
                                                                  "assignee": {"accountId": "712020:7ad4a939-164f-49b5-9317-c427332be323", "displayName": "금준영"}}}],
                     "isLast": True}
    return 200, {"issues": [{"key": "S15P21B202-34", "fields": {"summary": "오래된 머지", "status": {"name": "진행 중"}, "assignee": None}},
                            {"key": "S15P21B202-20", "fields": {"summary": "머지 없음", "status": {"name": "진행 중"}, "assignee": None}}],
                 "isLast": False, "nextPageToken": "p2"}
ci_common.http = fake_http

from datetime import datetime, timezone  # noqa: E402
NOW = datetime(2026, 8, 28, 0, 0, tzinfo=timezone.utc)   # 픽스처(08-27)가 창 안에 들도록 고정(-281)

rows, stacks, complete = d.merged_not_done(d.GitLab(), ci_common.Jira(), now=NOW)
print(rows, stacks, complete)
assert complete is True and len(rows) == 1 and "!31" in rows[0] and "@rmawnsdud" in rows[0], rows   # 2페이지째 이슈가 잡힘
assert stacks == [], stacks
jira_calls = [c for c in calls if "search/jql" in c[0]]
assert len(jira_calls) == 2 and jira_calls[1][1]["nextPageToken"] == "p2"

# 잘림 케이스: max_pages=1 이면 complete=False, 매칭 0 → 블록 없어야 함(호출측 규칙)
issues, complete2 = ci_common.Jira().search_all("x", ["summary"], max_pages=1)
assert complete2 is False and len(issues) == 2
print("TEST83d OK")
