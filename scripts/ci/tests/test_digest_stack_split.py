"""아침 점검이 스택 자식 MR 을 어떻게 다루는지 (S15P21B202-159).

실행: python scripts/ci/tests/test_digest_stack_split.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

배경과 규칙:
- R6 은 **대상 브랜치가 이슈 브랜치로 보이지 않을 때만** 이슈를 완료로 옮긴다. 그래서 스택 자식 MR
  (대상 = 부모의 이슈 브랜치)은 머지돼도 완료되지 않는다 — 설계대로다.
- 자식을 보고할 조건은 **그 코드가 develop 에 도달했을 때**다. 부모가 아직 머지되지 않았거나
  실험용·폐기 브랜치로 들어갔으면 지금 할 일이 아니므로 보고하지 않는다
  (2026-08-29 오탐 !82: -138 코드는 develop 에 없었다).
- 커밋 포함 여부로는 판정할 수 없다 — squash 가 기본이라 부모가 머지될 때 자식 커밋이 사라진다
  (2026-08-31 실측). 그래서 **부모 MR 을 따라 올라간다**.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"; os.environ["GITLAB_TOKEN"] = "x"
os.environ["JIRA_USER"] = "x"; os.environ["JIRA_API_TOKEN"] = "x"

import ci_common, mm_digest as d

# ── ① 판정 함수
assert d.completes_on_merge("develop") is True
assert d.completes_on_merge("master") is True
assert d.completes_on_merge("S15P21B202-166-physics") is False
assert d.completes_on_merge("hotfix/S15P21B202-12-x") is False
assert d.completes_on_merge("") is True          # 대상 정보가 없으면 develop 취급(기존 동작 유지)

# ── ② 표본: 네 갈래
#  -70 : develop 대상인데 이슈가 미완료          → 🔗 (R6 누락)
#  -82 : 부모(-70-a)가 develop 에 머지됨          → 🧩 (완료로 옮기면 됨)
#  -83 : 부모(-99-experiment)가 아무 데도 안 감  → 표시 안 함 (예전 오탐)
#  -84 : 두 단계 위(-85-mid → -70-a → develop)    → 🧩 (체인 추적)
#  -90 : 이미 완료된 이슈                         → 어느 쪽에도 없음
MRS = [
    {"iid": 70, "web_url": "https://lab/mr/70", "source_branch": "S15P21B202-70-a",
     "target_branch": "develop", "merged_at": "2026-08-31T01:00:00.000Z"},
    {"iid": 82, "web_url": "https://lab/mr/82", "source_branch": "S15P21B202-82-child",
     "target_branch": "S15P21B202-70-a", "merged_at": "2026-08-31T02:00:00.000Z"},
    {"iid": 83, "web_url": "https://lab/mr/83", "source_branch": "S15P21B202-83-orphan",
     "target_branch": "S15P21B202-99-experiment", "merged_at": "2026-08-31T02:30:00.000Z"},
    {"iid": 84, "web_url": "https://lab/mr/84", "source_branch": "S15P21B202-84-deep",
     "target_branch": "S15P21B202-85-mid", "merged_at": "2026-08-31T02:40:00.000Z"},
    {"iid": 90, "web_url": "https://lab/mr/90", "source_branch": "S15P21B202-90-done",
     "target_branch": "develop", "merged_at": "2026-08-31T03:00:00.000Z"},
]

# 소스 브랜치별로 "머지된 MR" 을 돌려준다. -99-experiment 는 어디에도 머지되지 않았다.
MERGED_BY_SOURCE = {
    "S15P21B202-70-a": {"iid": 70, "target_branch": "develop"},
    "S15P21B202-85-mid": {"iid": 85, "target_branch": "S15P21B202-70-a"},
}

ISSUES = [  # -90 은 Jira 가 미완료 목록에 주지 않는다(= 완료됨)
    {"key": "S15P21B202-70", "fields": {"summary": "부모", "status": {"name": "진행 중"}, "assignee": None}},
    {"key": "S15P21B202-82", "fields": {"summary": "스택 자식", "status": {"name": "진행 중"}, "assignee": None}},
    {"key": "S15P21B202-83", "fields": {"summary": "폐기 브랜치로 머지", "status": {"name": "진행 중"}, "assignee": None}},
    {"key": "S15P21B202-84", "fields": {"summary": "두 단계 자식", "status": {"name": "진행 중"}, "assignee": None}},
]

fail_refs = False   # True 로 바꾸면 부모 조회가 실패한다


def fake_http(method, url, headers=None, body=None, timeout=30):
    if "source_branch=" in url:                       # merged_mr_for_source
        if fail_refs:
            return 500, None
        src = url.split("source_branch=")[1].split("&")[0]
        mr = MERGED_BY_SOURCE.get(src)
        return 200, ([mr] if mr else [])
    if "merge_requests" in url:
        return 200, MRS
    return 200, {"issues": ISSUES, "isLast": True}


ci_common.http = fake_http

# ── ③ landed_on_develop 단독
assert d.landed_on_develop(d.GitLab(), "S15P21B202-70-a") is True          # 부모가 develop 으로
assert d.landed_on_develop(d.GitLab(), "S15P21B202-85-mid") is True        # 두 단계 체인
assert d.landed_on_develop(d.GitLab(), "S15P21B202-99-experiment") is False  # 아무 데도 안 감
assert d.landed_on_develop(d.GitLab(), "develop") is True

# ── ④ 분류 결과
rows, stacks, complete = d.merged_not_done(d.GitLab(), ci_common.Jira())
assert complete is True, complete
assert len(rows) == 1 and "S15P21B202-70" in rows[0] and "!70" in rows[0], rows
assert not any("S15P21B202-82" in r for r in rows), rows        # 자식이 첫 표에 섞이지 않는다

keys_in_stacks = [k for k in ("S15P21B202-82", "S15P21B202-83", "S15P21B202-84")
                  if any(k in s for s in stacks)]
assert keys_in_stacks == ["S15P21B202-82", "S15P21B202-84"], keys_in_stacks   # -83 은 빠진다(오탐 방지)
assert any("S15P21B202-70-a" in s for s in stacks), "스택 행에는 머지 대상 브랜치가 보여야 한다"
assert not any("S15P21B202-90" in x for x in rows + stacks), (rows, stacks)   # 완료된 이슈는 없다
assert not any("⁇" in s for s in stacks), "조회가 성공했으면 물음표가 없어야 한다"

# ── ⑤ 부모 조회 실패 → 숨기지 않고 물음표로 남긴다
fail_refs = True
rows2, stacks2, _ = d.merged_not_done(d.GitLab(), ci_common.Jira())
assert len(rows2) == 1, rows2
assert len(stacks2) == 3 and all("⁇" in s for s in stacks2), stacks2

print("TEST159 OK")
