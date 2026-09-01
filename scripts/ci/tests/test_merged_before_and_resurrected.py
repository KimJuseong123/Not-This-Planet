"""이미 머지된 브랜치로 다시 MR·push 하는 것을 잡는지 (S15P21B202-110).

실행: python scripts/ci/tests/test_merged_before_and_resurrected.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

머지된 브랜치에 다시 push 하면 MR·파이프라인 없이 조용히 남는다. 두 곳에서 잡는다:
- `mr_lint.merged_before` — 같은 브랜치로 머지된 MR 이 있으면 경고
- `mm_digest.resurrected_branches` — 머지된 MR 의 브랜치가 다시 존재하면 아침 점검 표 행
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1443653", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "60",
                   "JIRA_USER": "x", "JIRA_API_TOKEN": "x"})

import ci_common, mr_lint, mm_digest


# ── ① mr-lint: 같은 브랜치로 머지된 MR 이 있으면 경고
def lint_with(merged):
    def http(method, url, headers=None, body=None, timeout=30):
        assert "state=merged" in url and "source_branch=" in url, url
        return 200, merged

    ci_common.http = http; mr_lint.http = http
    return mr_lint.merged_before(ci_common.GitLab(), "S15P21B202-102-ci-failure-reason")


w = lint_with([{"iid": 46}, {"iid": 12}])
assert w and "!46" in w and "!12" in w and "새 이슈" in w, w
assert lint_with([]) is None
print("  mr-lint 머지 이력 경고 ✅")

# ── ② mm-digest: 머지된 MR 의 브랜치가 다시 존재하면 표 행
MRS = [{"iid": 46, "web_url": "https://lab/mr/46", "source_branch": "S15P21B202-102-x",
        "merged_at": "2026-08-28T00:57:43.000Z", "should_remove_source_branch": True,
        "author": {"username": "rmawnsdud"}},
       {"iid": 45, "web_url": "https://lab/mr/45", "source_branch": "S15P21B202-100-y",
        "merged_at": "2026-08-28T00:10:00.000Z", "should_remove_source_branch": True,
        "author": {"username": "chlwos1313"}}]
EXISTING = {"S15P21B202-102-x"}   # 되살아난 브랜치만 존재한다


def http2(method, url, headers=None, body=None, timeout=30):
    if "/merge_requests?state=merged" in url:
        return 200, MRS
    if "/repository/branches/" in url:
        name = url.rsplit("/", 1)[-1].replace("%2F", "/")
        if name in EXISTING:
            return 200, {"name": name, "commit": {"author_name": "금준영"}}
        return 404, {"message": "404 Branch Not Found"}
    raise AssertionError(url)


ci_common.http = http2; mm_digest.http = http2
rows = mm_digest.resurrected_branches(mm_digest.GitLab())
assert len(rows) == 1 and "S15P21B202-102-x" in rows[0] and "!46" in rows[0] and "@rmawnsdud" in rows[0], rows
print("  아침 점검 되살아난 브랜치 ✅")

print("TEST110 OK")
