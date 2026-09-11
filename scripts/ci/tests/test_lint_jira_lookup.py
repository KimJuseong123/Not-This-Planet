"""Jira 조회 실패/이슈 없음/정상/담당자 불일치 판정 (S15P21B202-94).

실행: python scripts/ci/tests/test_lint_jira_lookup.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.setdefault("CI_PROJECT_ID", "1"); os.environ.setdefault("CI_MERGE_REQUEST_IID", "1")

import mr_lint as L

ME = "712020:7ad4a939-164f-49b5-9317-c427332be323"      # 금준영
OTHER = "712020:2cf5f075-711f-44fa-90e8-1cc7ce9ff5dd"   # 최재현
issue = lambda acc: {"fields": {"assignee": {"accountId": acc, "displayName": "최재현" if acc == OTHER else "금준영"}}}

# 1) 조회 실패 → 판정하지 않음 (핵심 버그)
assert L.existence_problem("S15P21B202-9999", None, True) is None
# 2) 조회 성공 + 이슈 없음 → 실패 메시지(오타 경고 포함)
p = L.existence_problem("S15P21B202-9999", None, False)
assert p and "존재하지 않음" in p and "다른 이슈" in p, p
# 3) 이슈 있음 → 문제 없음
assert L.existence_problem("S15P21B202-93", issue(ME), False) is None
# 4) 키 없음 → 여기서는 판정하지 않음(다른 검사가 담당)
assert L.existence_problem(None, None, False) is None

mr_me = {"author": {"username": "rmawnsdud"}}
mr_bot = {"author": {"username": "project_1443653_bot"}}
# 5) 작성자 == 담당자 → 경고 없음
assert L.assignee_mismatch(mr_me, issue(ME)) is None
# 6) 작성자 ≠ 담당자 → 경고
w = L.assignee_mismatch(mr_me, issue(OTHER))
assert w and "금준영" in w and "최재현" in w and "대행" in w, w
# 7) team.json 에 없는 계정(봇) → 건너뜀
assert L.assignee_mismatch(mr_bot, issue(OTHER)) is None
# 8) 담당자 없는 이슈 → 여기서는 건너뜀(담당자 필수 검사가 따로 실패시킴)
assert L.assignee_mismatch(mr_me, {"fields": {"assignee": None}}) is None
print("TEST94 OK")
