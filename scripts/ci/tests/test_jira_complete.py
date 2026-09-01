"""머지된 MR 의 이슈와 스택 자식 이슈를 CI 가 완료로 옮기는지 (S15P21B202-199).

실행: python scripts/ci/tests/test_jira_complete.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다. 대기(sleep)는 주입해 즉시 끝난다.

표본은 2026-09-01 의 실제 구조를 그대로 쓴다:
  !148  -146-player-spawn        → develop            (부모)
  !149  -147-server-authority    → -146-player-spawn  (자식)
  !150  -148-remote-presentation → -146-player-spawn  (자식)
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

# 이슈 상태 저장소 — 전환이 실제로 반영되는지 보려고 가변으로 둔다
STATUS = {"S15P21B202-146": "진행 중", "S15P21B202-147": "해야 할 일", "S15P21B202-148": "진행 중"}
READY = {"S15P21B202-146": True, "S15P21B202-147": True, "S15P21B202-148": True}
REVERT = set()            # 여기 든 키는 전환 뒤 되돌려진다(R9 흉내)
MISSING = set()           # 존재하지 않는 이슈
posted_comments = []
transition_calls = []


def issue_doc(key):
    st = STATUS.get(key, "해야 할 일")
    cat = "done" if st == "완료" else ("indeterminate" if st == "진행 중" else "new")
    return {"key": key, "fields": {"summary": f"테스트 {key}", "status": {"name": st, "statusCategory": {"key": cat}}}}


def fake_http(method, url, headers=None, body=None, timeout=30):
    # ── GitLab
    if "/repository/commits/" in url and url.endswith("/merge_requests?per_page=20"):
        return 200, [PARENT]
    if "/merge_requests?state=merged&target_branch=" in url:
        br = url.split("target_branch=")[1].split("&")[0].replace("%2F", "/")
        return 200, CHILDREN.get(br, [])
    # ── Jira 색인 질의
    if url.endswith("/search/jql"):
        jql = (body or {}).get("jql", "")
        key = jql.split("key = ")[1].split(" ")[0]
        return 200, {"issues": [issue_doc(key)] if READY.get(key) else []}
    # ── Jira 이슈 조회
    if "/rest/api/3/issue/" in url and "/transitions" not in url and "/comment" not in url:
        key = url.split("/issue/")[1].split("?")[0]
        return (404, None) if key in MISSING else (200, issue_doc(key))
    # ── Jira 전환
    if url.endswith("/transitions") and method == "GET":
        return 200, {"transitions": [{"id": "11", "to": {"name": "해야 할 일", "statusCategory": {"key": "new"}}},
                                     {"id": "31", "to": {"name": "완료", "statusCategory": {"key": "done"}}}]}
    if url.endswith("/transitions") and method == "POST":
        key = url.split("/issue/")[1].split("/")[0]
        transition_calls.append((key, (body or {}).get("transition", {}).get("id")))
        STATUS[key] = "진행 중" if key in REVERT else "완료"      # REVERT = R9 가 되돌린 것처럼
        return 204, None
    if url.endswith("/comment"):
        key = url.split("/issue/")[1].split("/")[0]
        posted_comments.append(key)
        return 201, {}
    raise AssertionError(f"예상 못한 호출: {method} {url}")


ci_common.http = fake_http
J.time.sleep = NOSLEEP        # main() 경로가 실제로 자지 않게

gl, jira = ci_common.GitLab(), ci_common.Jira()

# ── ① 부모 MR 에서 자기 + 자식 이슈를 모두 모은다
keys, full = J.collect_keys(gl, PARENT)
assert full is True, full
assert keys == ["S15P21B202-146", "S15P21B202-147", "S15P21B202-148"], keys
print("  스택 자손 수집 ✅", keys)

# ── ② 자식 MR 은 develop 대상이 아니므로 최상위 판정에서 제외된다
assert ci_common.completes_on_merge("develop") is True
assert ci_common.completes_on_merge("S15P21B202-146-player-spawn") is False
print("  자식 MR 은 최상위 대상 아님 ✅")

# ── ③ 실제 완료 처리 — 셋 다 완료되고 코멘트가 달린다
for k in keys:
    r = J.complete(jira, k, 148, sleep=NOSLEEP)
    assert r.endswith(": 완료"), r
assert STATUS == {"S15P21B202-146": "완료", "S15P21B202-147": "완료", "S15P21B202-148": "완료"}, STATUS
assert sorted(posted_comments) == keys, posted_comments
assert all(tid == "31" for _k, tid in transition_calls), transition_calls   # statusCategory=done 전환을 골랐다
print("  세 이슈 완료 + 코멘트 ✅")

# ── ④ 이미 완료면 아무것도 하지 않는다
transition_calls.clear(); posted_comments.clear()
r = J.complete(jira, "S15P21B202-146", 148, sleep=NOSLEEP)
assert r.endswith(": 이미 완료"), r
assert not transition_calls and not posted_comments
print("  이미 완료 → 무동작 ✅")

# ── ⑤ 색인이 따라잡지 못하면 옮기지 않는다 (R9 되돌림을 미리 피한다)
STATUS["S15P21B202-147"] = "진행 중"; READY["S15P21B202-147"] = False
transition_calls.clear()
r = J.complete(jira, "S15P21B202-147", 149, sleep=NOSLEEP)
assert "색인이" in r and "옮기지 않음" in r, r
assert not transition_calls, transition_calls
assert STATUS["S15P21B202-147"] == "진행 중"
print("  색인 미준비 → 전환 안 함 ✅")

# ── ⑥ 옮겼는데 되돌려지면 그 사실을 보고한다
READY["S15P21B202-147"] = True; REVERT.add("S15P21B202-147")
r = J.complete(jira, "S15P21B202-147", 149, sleep=NOSLEEP)
assert "되돌려짐" in r, r
assert not posted_comments, posted_comments        # 되돌려졌으면 완료 코멘트를 달지 않는다
print("  되돌려짐 감지 ✅")

# ── ⑦ 없는 이슈는 건너뛴다
MISSING.add("S15P21B202-999")
r = J.complete(jira, "S15P21B202-999", 1, sleep=NOSLEEP)
assert "이슈 없음" in r, r
print("  없는 이슈 건너뜀 ✅")

# ── ⑧ main(): 커밋 하나로 전체 흐름
STATUS.update({"S15P21B202-146": "진행 중", "S15P21B202-147": "진행 중", "S15P21B202-148": "진행 중"})
REVERT.clear(); posted_comments.clear()
os.environ["CI_COMMIT_SHA"] = "c0ffee"
assert J.main() == 0
assert STATUS["S15P21B202-146"] == "완료" and STATUS["S15P21B202-147"] == "완료", STATUS
print("  main() 전체 흐름 ✅")

# ── ⑨ develop 대상 MR 이 없으면 할 일 없음
PARENT["target_branch"] = "S15P21B202-999-other"
transition_calls.clear()
assert J.main() == 0
assert not transition_calls, transition_calls
print("  스택 자식만 있는 커밋 → 무동작 ✅")

print("TEST199 OK")
