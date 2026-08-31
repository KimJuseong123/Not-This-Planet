"""브랜치 베이스 경고 네 경우 — master 기반 / 오래된 시작점 / 정상 / 다른 이슈 키 (S15P21B202-90).

실행: python scripts/ci/tests/test_lint_base_warnings.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1443653", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "99"})

import ci_common, mr_lint

DEV_HEAD = "d" * 40
MASTER_TIP = "m" * 40
FRESH_BASE = "f" * 40


def fake(merge_bases, behind, commits):
    def http(method, url, headers=None, body=None, timeout=30):
        if "merge_base" in url:
            ref = "master" if "master" in url else "develop"
            return 200, {"id": merge_bases[ref]}
        if "repository/branches/develop" in url:
            return 200, {"commit": {"id": DEV_HEAD}}
        if "repository/compare" in url:
            return 200, {"commits": [{"id": str(i)} for i in range(behind)]}
        if "/commits" in url:
            return 200, commits
        raise AssertionError(url)
    return http


def run(merge_bases, behind, commits, branch="S15P21B202-91-x", bkey="S15P21B202-91"):
    ci_common.http = fake(merge_bases, behind, commits)
    return mr_lint.base_warnings(ci_common.GitLab(), branch, "develop", bkey)


# 1) master 기반: 두 merge-base 가 같고 develop tip 이 아님 → 경고
r = run({"develop": MASTER_TIP, "master": MASTER_TIP}, 61, [])
assert len(r) == 1 and r[0][0] == "warn" and "master(또는 그 이전)" in r[0][1] and "61커밋 뒤" in r[0][1], r
# 2) develop 기반이지만 오래됨 → 뒤처짐 경고
r = run({"develop": FRESH_BASE, "master": MASTER_TIP}, 40, [])
assert len(r) == 1 and r[0][0] == "warn" and "40커밋 뒤처졌습니다" in r[0][1], r
# 3) 정상(최근 develop 기반) → ok 만
r = run({"develop": FRESH_BASE, "master": MASTER_TIP}, 3, [])
assert len(r) == 1 and r[0][0] == "ok" and "3커밋 뒤" in r[0][1], r
# 4) 다른 이슈 키 커밋 섞임 → 추가 경고
r = run({"develop": FRESH_BASE, "master": MASTER_TIP}, 1,
        [{"title": "feat: 내 작업 (S15P21B202-91)"}, {"title": "feat: 남의 작업 (S15P21B202-66)"}])
assert len(r) == 2 and r[1][0] == "warn" and "S15P21B202-66" in r[1][1], r
# 5) hotfix 는 제외
assert run({"develop": MASTER_TIP, "master": MASTER_TIP}, 61, [], branch="hotfix/S15P21B202-92-x") == []
# 6) 대상이 master 인 릴리즈 MR 도 제외
ci_common.http = fake({"develop": MASTER_TIP, "master": MASTER_TIP}, 61, [])
assert mr_lint.base_warnings(ci_common.GitLab(), "develop", "master", None) == []
print("TEST90 OK")
