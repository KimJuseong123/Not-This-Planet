"""머지된 MR 의 이슈·스택 자식 이슈를 CI 가 완료로 옮기는지 (S15P21B202-199 · 훑기 -240).

실행: python scripts/ci/tests/test_jira_complete.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다. 대기(sleep)는 주입해 즉시 끝난다.

표본은 2026-09-01 의 실제 구조를 그대로 쓴다:
  !148  -146-player-spawn        → develop            (부모)
  !149  -147-server-authority    → -146-player-spawn  (자식)
  !150  -148-remote-presentation → -146-player-spawn  (자식)

훑기(-240)는 **취소·실패로 놓친 머지를 다음 실행이 메꾸는** 장치다. 실측 배경:
  · 25초 간격 머지 3건 중 파이프라인 176664 의 잡이 시작 전에 canceled
  · 파이프라인 176670 은 스택 자식(-141)을 정확히 찾았지만 전환이 401 로 막혔다(읽기 전용 토큰)
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"; os.environ["GITLAB_TOKEN"] = "x"
os.environ["JIRA_USER"] = "x"; os.environ["JIRA_API_TOKEN"] = "x"

import ci_common, jira_complete as J

NOSLEEP = lambda _s: None            # noqa: E731 — 테스트에서 대기 제거

PARENT = {"iid": 148, "state": "merged", "source_branch": "S15P21B202-146-player-spawn",
          "target_branch": "develop"}
CHILDREN = {
    "S15P21B202-146-player-spawn": [
        {"iid": 149, "state": "merged", "source_branch": "S15P21B202-147-server-authority",
         "target_branch": "S15P21B202-146-player-spawn"},
        {"iid": 150, "state": "merged", "source_branch": "S15P21B202-148-remote-presentation",
         "target_branch": "S15P21B202-146-player-spawn"},
    ],
}

# 훑기 표본 — 최근 머지분. -300 은 develop 대상, -301 은 부모가 머지되지 않은 스택 자식
SWEEP_MRS = [
    {"iid": 200, "state": "merged", "source_branch": "S15P21B202-300-a",
     "target_branch": "develop", "merged_at": "2026-09-01T01:00:00.000Z"},
    {"iid": 201, "state": "merged", "source_branch": "S15P21B202-301-child",
     "target_branch": "S15P21B202-999-orphan", "merged_at": "2026-09-01T02:00:00.000Z"},
]
# 소스 브랜치를 대상으로 머지된 MR (부모 추적용). -999-orphan 은 없다 = 부모 미머지
MERGED_BY_SOURCE = {}

STATUS = {"S15P21B202-146": "진행 중", "S15P21B202-147": "해야 할 일", "S15P21B202-148": "진행 중",
          "S15P21B202-300": "진행 중", "S15P21B202-301": "진행 중"}
READY = {k: True for k in STATUS}
REVERT = set()
MISSING = set()
DENY_WRITE = False        # True 면 전환이 401 (읽기 전용 토큰 흉내)
posted_comments = []
transition_calls = []
sweep_enabled = False     # 기본은 훑기 표본을 비워 기존 케이스에 영향 없게


def issue_doc(key):
    st = STATUS.get(key, "해야 할 일")
    cat = "done" if st == "완료" else ("indeterminate" if st == "진행 중" else "new")
    return {"key": key, "fields": {"summary": f"테스트 {key}",
                                   "status": {"name": st, "statusCategory": {"key": cat}}}}


def fake_http(method, url, headers=None, body=None, timeout=30):
    # ── GitLab
    if "/repository/commits/" in url and "/merge_requests" in url:
        return 200, [PARENT]
    if "state=merged&target_branch=" in url:
        br = url.split("target_branch=")[1].split("&")[0].replace("%2F", "/")
        return 200, CHILDREN.get(br, [])
    if "state=merged&source_branch=" in url:
        br = url.split("source_branch=")[1].split("&")[0].replace("%2F", "/")
        mr = MERGED_BY_SOURCE.get(br)
        return 200, ([mr] if mr else [])
    if "state=merged&updated_after=" in url:                 # 훑기용 최근 머지 목록
        return 200, (SWEEP_MRS if sweep_enabled else [])
    # ── Jira 질의
    if url.endswith("/search/jql"):
        jql = (body or {}).get("jql", "")
        if "statusCategory != Done" in jql:                  # 프로젝트 전체 미완료
            return 200, {"issues": [issue_doc(k) for k, v in STATUS.items() if v != "완료"],
                         "isLast": True}
        key = jql.split("key = ")[1].split(" ")[0]           # 색인 준비 확인
        return 200, {"issues": [issue_doc(key)] if READY.get(key) else [], "isLast": True}
    # ── Jira 이슈
    if "/rest/api/3/issue/" in url and "/transitions" not in url and "/comment" not in url:
        key = url.split("/issue/")[1].split("?")[0]
        return (404, None) if key in MISSING else (200, issue_doc(key))
    if url.endswith("/transitions") and method == "GET":
        return 200, {"transitions": [{"id": "11", "to": {"name": "해야 할 일", "statusCategory": {"key": "new"}}},
                                     {"id": "31", "to": {"name": "완료", "statusCategory": {"key": "done"}}}]}
    if url.endswith("/transitions") and method == "POST":
        if DENY_WRITE:
            return 401, {"code": 401, "message": "Unauthorized; scope does not match"}
        key = url.split("/issue/")[1].split("/")[0]
        transition_calls.append((key, (body or {}).get("transition", {}).get("id")))
        STATUS[key] = "진행 중" if key in REVERT else "완료"
        return 204, None
    if url.endswith("/comment"):
        posted_comments.append(url.split("/issue/")[1].split("/")[0])
        return 201, {}
    raise AssertionError(f"예상 못한 호출: {method} {url}")


ci_common.http = fake_http
J.time.sleep = NOSLEEP

gl, jira = ci_common.GitLab(), ci_common.Jira()

# ── ① 부모 MR 에서 자기 + 자식 이슈를 모두 모은다
keys, full = J.collect_keys(gl, PARENT)
assert full is True and keys == ["S15P21B202-146", "S15P21B202-147", "S15P21B202-148"], keys
print("  스택 자손 수집 ✅", keys)

# ── ② 자식 MR 은 develop 대상이 아니므로 최상위 판정에서 제외된다
assert ci_common.completes_on_merge("develop") is True
assert ci_common.completes_on_merge("S15P21B202-146-player-spawn") is False
print("  자식 MR 은 최상위 대상 아님 ✅")

# ── ③ 실제 완료 처리 — 셋 다 완료되고 코멘트가 달린다
for k in keys:
    r = J.complete(jira, k, 148, sleep=NOSLEEP)
    assert r.endswith(": 완료"), r
assert all(STATUS[k] == "완료" for k in keys), STATUS
assert sorted(posted_comments) == keys, posted_comments
assert all(tid == "31" for _k, tid in transition_calls), transition_calls
print("  세 이슈 완료 + 코멘트 ✅")

# ── ④ 이미 완료면 아무것도 하지 않는다
transition_calls.clear(); posted_comments.clear()
assert J.complete(jira, "S15P21B202-146", 148, sleep=NOSLEEP).endswith(": 이미 완료")
assert not transition_calls and not posted_comments
print("  이미 완료 → 무동작 ✅")

# ── ⑤ 색인이 따라잡지 못하면 옮기지 않는다 (R9 되돌림을 미리 피한다)
STATUS["S15P21B202-147"] = "진행 중"; READY["S15P21B202-147"] = False
transition_calls.clear()
r = J.complete(jira, "S15P21B202-147", 149, sleep=NOSLEEP)
assert "색인이" in r and "옮기지 않음" in r, r
assert not transition_calls and STATUS["S15P21B202-147"] == "진행 중"
print("  색인 미준비 → 전환 안 함 ✅")

# ── ⑥ 옮겼는데 되돌려지면 그 사실을 보고한다
READY["S15P21B202-147"] = True; REVERT.add("S15P21B202-147")
r = J.complete(jira, "S15P21B202-147", 149, sleep=NOSLEEP)
assert "되돌려짐" in r, r
assert not posted_comments, posted_comments
print("  되돌려짐 감지 ✅")

# ── ⑦ 없는 이슈는 건너뛴다
MISSING.add("S15P21B202-999")
assert "이슈 없음" in J.complete(jira, "S15P21B202-999", 1, sleep=NOSLEEP)
print("  없는 이슈 건너뜀 ✅")

# ── ⑧ 쓰기 권한이 없으면(401) 무엇을 해야 하는지 알려준다 — 실측 사례(-242)
REVERT.clear(); STATUS["S15P21B202-147"] = "진행 중"
DENY_WRITE = True
r = J.complete(jira, "S15P21B202-147", 149, sleep=NOSLEEP)
assert "쓰기 권한이 없다" in r and "S15P21B202-242" in r, r
assert STATUS["S15P21B202-147"] == "진행 중", "실패했는데 상태가 바뀌었다"
DENY_WRITE = False
print("  401 → 조치 가능한 문구 ✅")

# ── ⑨ main(): 커밋 하나로 전체 흐름 (훑기 표본은 비어 있음)
STATUS.update({"S15P21B202-146": "진행 중", "S15P21B202-147": "진행 중", "S15P21B202-148": "진행 중"})
posted_comments.clear()
os.environ["CI_COMMIT_SHA"] = "c0ffee"
assert J.main() == 0
assert STATUS["S15P21B202-146"] == "완료" and STATUS["S15P21B202-147"] == "완료", STATUS
print("  main() 전체 흐름 ✅")

# ── ⑩ develop 대상 MR 이 없으면 커밋 처리는 건너뛴다
PARENT["target_branch"] = "S15P21B202-998-other"
transition_calls.clear()
assert J.main() == 0
assert not transition_calls, transition_calls
print("  스택 자식만 있는 커밋 → 커밋 처리 무동작 ✅")
PARENT["target_branch"] = "develop"

# ── ⑪ 훑기: 놓친 머지를 메꾼다 / 부모가 develop 에 없으면 건드리지 않는다
sweep_enabled = True
transition_calls.clear(); posted_comments.clear()
out = J.sweep(gl, jira, sleep=NOSLEEP)
assert STATUS["S15P21B202-300"] == "완료", STATUS          # develop 대상 → 메꿈
assert STATUS["S15P21B202-301"] == "진행 중", STATUS       # 부모 미머지 → 그대로
assert any("S15P21B202-300: 완료" in x for x in out), out
assert not any(k == "S15P21B202-301" for k, _ in transition_calls), transition_calls
print("  훑기: 놓친 머지 메꿈 + 부모 미머지 보호 ✅")

# ── ⑫ 훑기는 멱등하다 — 다시 돌려도 아무 일도 없다
transition_calls.clear()
J.sweep(gl, jira, sleep=NOSLEEP)
assert not any(k == "S15P21B202-300" for k, _ in transition_calls), transition_calls
print("  훑기 멱등 ✅")

# ── ⑬ 훑기 처리 건수 상한
STATUS.update({f"S15P21B202-{400 + i}": "진행 중" for i in range(8)})
READY.update({f"S15P21B202-{400 + i}": True for i in range(8)})
SWEEP_MRS.extend({"iid": 300 + i, "state": "merged", "source_branch": f"S15P21B202-{400 + i}-x",
                  "target_branch": "develop", "merged_at": "2026-09-01T03:00:00.000Z"} for i in range(8))
transition_calls.clear()
J.sweep(gl, jira, sleep=NOSLEEP)
assert len(transition_calls) <= J.SWEEP_MAX, f"{len(transition_calls)} > {J.SWEEP_MAX}"
print(f"  훑기 상한 {J.SWEEP_MAX}건 준수 ✅ (실제 {len(transition_calls)}건)")

print("TEST199 OK")
