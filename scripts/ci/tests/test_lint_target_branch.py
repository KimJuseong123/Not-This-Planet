"""대상 브랜치 판정 다섯 경우 — develop / master / 스택 MR / 그 밖 (S15P21B202-99).

실행: python scripts/ci/tests/test_lint_target_branch.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.setdefault("CI_PROJECT_ID", "1"); os.environ.setdefault("CI_MERGE_REQUEST_IID", "1")

import mr_lint as L

# develop 대상 → ok
assert L.target_verdict("S15P21B202-96-x", "develop") == ("ok", "대상 브랜치 develop")
# 작업 브랜치 대상 → warn (스택 MR 허용)
lvl, msg = L.target_verdict("S15P21B202-96-puzzle", "S15P21B202-91-puzzle-1-stage")
assert lvl == "warn" and "스택 MR" in msg, (lvl, msg)
# 문구는 **부모를 먼저 머지하라**고 말해야 한다. 예전 문구("자동 완료되지 않으니 손으로 완료하세요")는
# 잘못된 순서를 정당화했다 — 실측 26 대 0 의 원인 중 하나다 (S15P21B202-244).
assert "부모를 먼저" in msg and "stack-order" in msg, msg
assert "손으로 완료" not in msg, "순서를 어긴 뒤 손으로 메우라는 안내를 다시 넣지 않는다"
# hotfix → master : ok
assert L.target_verdict("hotfix/S15P21B202-40-crash", "master")[0] == "ok"
# develop → master (릴리즈) : ok
assert L.target_verdict("develop", "master")[0] == "ok"
# 일반 작업 브랜치 → master : fail
assert L.target_verdict("S15P21B202-96-x", "master") == ("fail", "master 로는 develop 또는 hotfix/* 만 머지")
# 알 수 없는 대상(키 없는 브랜치) → fail
lvl, msg = L.target_verdict("S15P21B202-96-x", "some-random-branch")
assert lvl == "fail" and "스택 MR" in msg, (lvl, msg)
print("TEST99 OK")
