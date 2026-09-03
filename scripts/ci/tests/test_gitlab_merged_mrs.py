"""머지된 MR 수집의 페이징·상한 판정 (S15P21B202-83).

실행: python scripts/ci/tests/test_gitlab_merged_mrs.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"; os.environ["GITLAB_TOKEN"] = "x"; os.environ["JIRA_USER"] = "x"; os.environ["JIRA_API_TOKEN"] = "x"

import ci_common, mm_digest as d

MRS = [{"iid": 32, "web_url": "https://lab/mr/32", "source_branch": "S15P21B202-75", "merged_at": "2026-08-27T08:43:17.000Z"},
       {"iid": 31, "web_url": "https://lab/mr/31", "source_branch": "S15P21B202-74-x", "merged_at": "2026-08-27T08:06:16.000Z"},
       {"iid": 5, "web_url": "https://lab/mr/5", "source_branch": "S15P21B202-999-typo", "merged_at": "2026-08-27T09:00:00.000Z"},
       {"iid": 2, "web_url": "https://lab/mr/2", "source_branch": "S15P21B202-34-old", "merged_at": "2026-08-01T00:00:00.000Z"},
       {"iid": 9, "web_url": "https://lab/mr/9", "source_branch": "no-key-branch", "merged_at": "2026-08-27T09:00:00.000Z"}]
calls = []
def fake_http(method, url, headers=None, body=None, timeout=30):
    calls.append((url, body))
    if "merge_requests" in url:
        return 200, MRS
    if body and body.get("nextPageToken") == "p2":
        return 200, {"issues": [{"key": "S15P21B202-74", "fields": {"summary": "테스트 이슈", "status": {"name": "진행 중"},
                                                                  "assignee": {"accountId": "712020:7ad4a939-164f-49b5-9317-c427332be323", "displayName": "금준영"}}}], "isLast": True}
    return 200, {"issues": [{"key": "S15P21B202-34", "fields": {"summary": "오래된 머지", "status": {"name": "진행 중"}, "assignee": None}},
                            {"key": "S15P21B202-20", "fields": {"summary": "머지 없음", "status": {"name": "진행 중"}, "assignee": None}}],
                 "isLast": False, "nextPageToken": "p2"}
ci_common.http = fake_http

# 픽스처의 merged_at 은 2026-08-27 로 고정돼 있다. 창을 실제 현재 시각으로 자르면 이 테스트는
# 2026-09-03 17:06 KST 부터 깨진다(실제로 그랬다, -281). 그래서 now 를 고정해 넘긴다.
from datetime import datetime, timezone  # noqa: E402
NOW = datetime(2026, 8, 28, 0, 0, tzinfo=timezone.utc)

mrs, ok = d.GitLab().merged_mrs(7, now=NOW)
assert ok is True and [m["iid"] for m in mrs] == [32, 31, 5, 9], mrs          # -34 는 7일 밖
rows, stacks, complete = d.merged_not_done(d.GitLab(), ci_common.Jira(), now=NOW)
assert complete is True and len(rows) == 1 and "!31" in rows[0] and "@rmawnsdud" in rows[0], rows
assert stacks == [], stacks   # 이 표본에는 대상 브랜치가 없어 전부 develop 취급(-159)
assert d.INFRA_MM == "rmawnsdud"

# 7일 안에 이슈 키가 붙은 머지가 하나도 없는 날 — 반환값은 여전히 3개여야 한다(-281 의 두 번째 결함).
# 예전엔 2개를 돌려줘 main() 이 ValueError 를 잡아 "조회 실패 → @Infra" 오탐 카드를 냈다.
from datetime import timedelta  # noqa: E402
empty = d.merged_not_done(d.GitLab(), ci_common.Jira(), now=NOW + timedelta(days=60))
assert empty == ([], [], True), f"빈 경로도 (rows, stacks, complete) 3개여야 한다: {empty!r}"
print("  머지 0건 → 3-tuple ✅")

# 페이지 상한(100건 꽉 찬 페이지가 max_pages 만큼) → complete False
def full_pages(method, url, headers=None, body=None, timeout=30):
    return 200, [dict(MRS[0], iid=i) for i in range(100)]
ci_common.http = full_pages
mrs2, ok2 = d.GitLab().merged_mrs(7, max_pages=2, now=NOW)
assert ok2 is False and len(mrs2) == 200
print("TEST83e OK")
