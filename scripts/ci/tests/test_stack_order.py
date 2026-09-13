"""스택 자식 MR 의 머지를 부모가 들어갈 때까지 막는지 (S15P21B202-244).

실행: python scripts/ci/tests/test_stack_order.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

왜 이 검사가 있는가 — mr-flow §3 은 스택을 **아래(develop 에 가까운 쪽)부터** 머지하라고 정하는데,
2026-09-01 실측에서 **26 대 0** 으로 아무도 지키지 않았다(머지 139건 중 대상이 이슈 브랜치인 28건 조사).
`mr-lint` 는 이미 같은 조건을 경고로 달고 있었지만 "머지를 막지는 않는다" 는 문구 그대로 무시됐다.

왜 Draft 가 아니라 검사 실패인가 — 팀 규칙이 Draft 를 금지한다(git-convention §5). CI 가 그 상태를
대신 쓰면 규칙에 예외가 생기고, 사람이 그 예외를 흉내 내 검사를 우회할 수 있다.

억울한 차단을 만들지 않는 것이 이 테스트의 절반이다: 부모가 이미 머지됐는데 브랜치만 남아 있는
경우(우리 리포에 7건 있었다)를 실패로 처리하면 안 된다.

왜 검사 실패만으로는 부족한가(-297) — `Pipelines must succeed` 는 MR 의 **마지막 파이프라인**만
본다. 부모가 머지돼 자식이 재대상되면 그 마지막 파이프라인은 **부모 기준으로 통과한 낡은 초록**이고,
GitLab 은 그걸 보고 머지를 허용한다. 실측한 재대상 → 머지 간격 중위값이 약 20초라 그 창에 들어간다.
그래서 안내를 **미해소 스레드**로 달아 머지 버튼까지 잠근다(`resolvable=True` ·
`blocking_discussions_resolved=False` — 2026-09-04 실측).
"""
import io
import contextlib
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"
os.environ["GITLAB_TOKEN"] = "x"
os.environ["CI_MERGE_REQUEST_IID"] = "77"
os.environ.pop("STACK_ORDER_ENFORCE", None)

import ci_common  # noqa: E402
import stack_order as S  # noqa: E402

STATE = {}          # 한 경우분 설정


def fake_http(method, url, headers=None, body=None, timeout=30):
    if "/repository/branches/" in url:
        name = url.rsplit("/", 1)[-1].replace("%2F", "/")
        return (200, {"commit": {"id": "abc1234"}}) if name in STATE["branches"] else (404, {"message": "404"})
    if "state=merged&source_branch=" in url:
        br = url.split("source_branch=")[1].split("&")[0].replace("%2F", "/")
        return 200, ([{"iid": 164, "source_branch": br}] if br in STATE["merged_sources"] else [])
    if "state=opened" in url:
        return 200, STATE["open"]
    if "/notes/" in url and method == "DELETE":         # 예전 개별 노트 제거(-297 이행)
        STATE["deleted_notes"].append(int(url.rsplit("/", 1)[-1]))
        return 204, {}
    if url.endswith("/discussions?per_page=100"):
        return 200, list(STATE["threads"])
    if "/discussions/" in url and "/notes/" in url and method == "PUT":
        STATE["posted"] = body["body"]
        for t in STATE["threads"]:
            for nt in t["notes"]:
                if nt["id"] == int(url.rsplit("/", 1)[-1]):
                    nt["body"] = body["body"]
        return 200, {}
    if "/discussions/" in url and method == "PUT":          # 해소/해제
        did = url.rsplit("/", 1)[-1]
        STATE["resolved_calls"].append((did, body.get("resolved")))
        for t in STATE["threads"]:
            if t["id"] == did:
                for nt in t["notes"]:
                    nt["resolved"] = bool(body.get("resolved"))
        return 200, {}
    if url.endswith("/discussions") and method == "POST":
        STATE["posted"] = body["body"]
        t = {"id": "d1", "individual_note": False,
             "notes": [{"id": 5, "body": body["body"], "resolved": False, "resolvable": True}]}
        STATE["threads"].append(t)
        STATE["created_thread"] = True
        return 201, t
    if url.endswith("/notes?per_page=100&sort=desc"):
        return 200, [{"id": 5, "body": b} for b in STATE["notes"]]
    if "/notes/" in url and method == "PUT":
        STATE["posted"] = body["body"]; return 200, {}
    if url.endswith("/notes") and method == "POST":
        STATE["posted"] = body["body"]; return 201, {}
    if method == "PUT":                       # MR 수정
        STATE["puts"].append(body)
        return STATE.get("put_status", 200), {}
    if url.endswith("/projects/1443653") and method == "GET":
        return 200, {"only_allow_merge_if_all_discussions_are_resolved": STATE["threads_setting"]}
    if method == "GET":                       # MR 조회
        return 200, {"iid": 77, "title": STATE["title"], "labels": list(STATE["labels"]),
                     "target_branch": STATE["target"], "web_url": "https://lab/mr/77"}
    raise AssertionError(f"예상 못한 호출: {method} {url}")


ci_common.http = fake_http


def run(target, branches=(), merged_sources=(), notes=(), open_mrs=(), put_status=200,
        title="[Character] feat: x (S15P21B202-243)", labels=(), threads=(), threads_setting=True):
    """stack_order.main() 을 한 번 돌린다. 반환 (종료코드, 보낸 수정들, 코멘트, 출력)."""
    STATE.clear()
    STATE.update(target=target, branches=set(branches), merged_sources=set(merged_sources),
                 notes=list(notes), open=list(open_mrs), puts=[], posted=None, put_status=put_status,
                 title=title, labels=list(labels),
                 threads=[dict(t, notes=[dict(x) for x in t["notes"]]) for t in threads],
                 resolved_calls=[], created_thread=False, threads_setting=threads_setting,
                 deleted_notes=[])
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        rc = S.main()
    return rc, list(STATE["puts"]), STATE["posted"], buf.getvalue()


PARENT_MR = [{"iid": 164, "source_branch": "S15P21B202-237-physics", "web_url": "https://lab/mr/164"}]

# ── ① 대상이 develop → 통과, 아무 것도 하지 않는다
rc, puts, note, _ = run("develop")
assert rc == 0 and not puts and note is None, (rc, puts, note)
print("  develop 대상 → 무동작 ✅")

# ── ② master 대상(릴리즈)도 스택이 아니다
rc, puts, note, _ = run("master")
assert rc == 0 and not puts and note is None, (rc, puts, note)
print("  master 대상 → 무동작 ✅")

# ── ③ 스택 자식 + 부모가 아직 살아 있다 → **실패** + 🔒 코멘트, 대상은 건드리지 않는다
rc, puts, note, out = run("S15P21B202-237-physics", branches=["S15P21B202-237-physics"], open_mrs=PARENT_MR)
assert rc != 0, "부모가 안 들어갔으면 실패해야 한다"
assert not puts, f"대상을 바꾸면 안 된다: {puts}"
assert note and "부모가 먼저 머지돼야" in note, note
assert "고칠 것은 없습니다" in note, "작성자가 뭘 잘못한 게 아니라는 것을 먼저 말해야 한다"
assert "!164" in note, f"부모 MR 번호를 알려줘야 한다: {note}"
assert "Draft" not in note, "Draft 는 팀 규칙이 금지한다 — 문구에도 남기지 않는다"
print("  스택 자식 → 실패 + 안내 ✅")

# ── ④ 부모 MR 을 못 찾아도 판정은 같다(코멘트만 짧아진다)
rc, puts, note, _ = run("S15P21B202-237-physics", branches=["S15P21B202-237-physics"], open_mrs=[])
assert rc != 0 and note and "!164" not in note, (rc, note)
print("  부모 MR 못 찾음 → 판정 동일 ✅")

# ── ⑤ 부모 브랜치가 사라졌다(머지되고 삭제) → 대상 복구 + 통과
rc, puts, note, _ = run("S15P21B202-237-physics", branches=["develop"])
assert rc == 0, rc
assert puts == [{"target_branch": "develop"}], puts
assert note and "풀렸습니다" in note, note
print("  부모 브랜치 소멸 → 대상 복구 ✅")

# ── ⑥ 브랜치는 남아 있는데 그 소스의 MR 이 이미 머지됐다 → 억울하게 막지 않는다
#      (머지 뒤 브랜치를 지우지 않은 경우. 우리 리포에 실제로 7건 있었다)
rc, puts, note, out = run("S15P21B202-237-physics",
                          branches=["S15P21B202-237-physics"],
                          merged_sources=["S15P21B202-237-physics"])
assert rc == 0, f"이미 머지된 부모면 통과해야 한다: {out}"
assert puts == [{"target_branch": "develop"}], puts
assert "이미 머지됐다" in out, out
print("  머지된 부모 + 남은 브랜치 → 통과 ✅")

# ── ⑦ 대상 전환이 막히면(권한 등) 실패로 알리고 무엇을 하라고 말한다
rc, puts, note, out = run("S15P21B202-237-physics", branches=["develop"], put_status=403)
assert rc != 0 and "develop" in out, out
print("  전환 실패 → 안내 ✅")

# ── ⑧ 부모가 머지돼 대상이 develop 으로 돌아왔다 → 🔓 로 갱신
rc, puts, note, _ = run("develop", notes=[S.MARKER + "\n🔒 이전에 막혔던 코멘트"])
assert rc == 0 and note and "풀렸습니다" in note, (rc, note)
print("  대상 복귀 → 🔓 갱신 ✅")

# ── ⑨ STACK_ORDER_ENFORCE=0 이면 코멘트만 남기고 실패시키지 않는다(비상 스위치)
os.environ["STACK_ORDER_ENFORCE"] = "0"
rc, puts, note, _ = run("S15P21B202-237-physics", branches=["S15P21B202-237-physics"])
assert rc == 0 and note and "부모가 먼저" in note, (rc, note)
os.environ.pop("STACK_ORDER_ENFORCE")
print("  비상 스위치 ✅")

# ── ⑩ MR 파이프라인이 아니면 생략
os.environ.pop("CI_MERGE_REQUEST_IID")
with contextlib.redirect_stdout(io.StringIO()):
    assert S.main() == 0
os.environ["CI_MERGE_REQUEST_IID"] = "77"
print("  MR 아님 → 생략 ✅")

# ── ⑪ CI 는 제목·레이블을 건드리지 않는다
#    -244 의 Draft 방식이 남긴 흔적을 지우던 이행 코드(clear_legacy)는 -301 에서 제거했다.
#    대상이던 MR 5건(!173·!174·!176·!177·!178)이 전부 머지돼 다시 돌 일이 없었다.
#    사람이 건 Draft 는 mr-lint 가 본다 — 이 잡은 순서만 본다.
rc, puts, note, _ = run("develop", branches=["develop"],
                        title="Draft: [Puzzle] feat: x (S15P21B202-247)", labels=["stack-blocked"])
assert not puts, f"제목·레이블을 건드리면 안 된다: {puts}"
print("  ⑪ 제목·레이블 무개입 ✅")

# ══ 여기부터 S15P21B202-297 — 검사 실패만으로는 낡은 초록을 못 막으므로 스레드로 잠근다 ══

def calls():
    return list(STATE["resolved_calls"])


PARENT_ALIVE = dict(target="S15P21B202-237-physics", branches=["S15P21B202-237-physics"],
                    open_mrs=PARENT_MR)

# ── ⑬ 스택 자식 → 스레드를 **새로 만들고 미해소로 둔다**(머지 버튼 잠김)
rc, puts, note, out = run(**PARENT_ALIVE)
assert rc != 0
assert STATE["created_thread"], "안내가 스레드로 달려야 한다 — 개별 노트는 해소 대상이 아니라 머지를 막지 못한다"
assert not any(r for _d, r in calls()), f"미해소여야 한다: {calls()}"
assert "손으로 해소하지 마세요" in note, "사람이 풀면 안 된다는 것을 본문이 말해야 한다"
assert "미해소" in out or "머지 잠김" in out, out
print("  ⑬ 스택 자식 → 미해소 스레드 생성(머지 잠김) ✅")

# ── ⑭ 사람이 손으로 해소해 뒀는데 아직 스택이면 → **다시 미해소로 되돌린다**
RESOLVED_THREAD = [{"id": "d9", "individual_note": False,
                    "notes": [{"id": 5, "body": S.blocked("x", ""),
                               "resolved": True, "resolvable": True}]}]
rc, puts, note, out = run(**PARENT_ALIVE, threads=RESOLVED_THREAD)
assert rc != 0
assert ("d9", False) in calls(), f"미해소로 되돌려야 한다: {calls()}"
print("  ⑭ 사람이 해소해 둔 것을 되돌림 ✅")

# ── ⑮ 부모가 머지돼 대상이 develop 로 돌아오면 → **스레드를 해소**한다
BLOCKED_THREAD = [{"id": "d9", "individual_note": False,
                   "notes": [{"id": 5, "body": S.blocked("x", ""),
                              "resolved": False, "resolvable": True}]}]
rc, puts, note, out = run("develop", branches=["develop"],
                          notes=[S.MARKER + " 이전 안내"], threads=BLOCKED_THREAD)
assert rc == 0
assert ("d9", True) in calls(), f"해소해야 한다: {calls()}"
assert note and "풀렸습니다" in note, note
print("  ⑮ 대상 복귀 → 스레드 해소 ✅")

# ── ⑯ 예전 방식으로 남은 **개별 노트**는 해소가 안 되므로 스레드로 이행한다
INDIVIDUAL = [{"id": "d0", "individual_note": True,
               "notes": [{"id": 5, "body": S.MARKER + " 예전 개별 노트", "resolvable": False}]}]
rc, puts, note, out = run(**PARENT_ALIVE, threads=INDIVIDUAL)
assert rc != 0
assert STATE["created_thread"], "개별 노트를 지우고 스레드로 다시 만들어야 한다"
assert STATE["deleted_notes"] == [5], STATE["deleted_notes"]
print("  ⑯ 예전 개별 노트 → 스레드로 이행 ✅")

# ── ⑰ 프로젝트 설정이 꺼져 있으면 **경고한다** — 조용히 무력화되면 안 된다
rc, puts, note, out = run(**PARENT_ALIVE, threads_setting=False)
assert rc != 0
assert "All threads must be resolved" in out, out
print("  ⑰ 설정이 꺼져 있으면 경고 ✅")

print("TEST244 OK")
