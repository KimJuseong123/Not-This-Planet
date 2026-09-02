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
    if url.endswith("/notes?per_page=100&sort=desc"):
        return 200, [{"id": 5, "body": b} for b in STATE["notes"]]
    if "/notes/" in url and method == "PUT":
        STATE["posted"] = body["body"]; return 200, {}
    if url.endswith("/notes") and method == "POST":
        STATE["posted"] = body["body"]; return 201, {}
    if method == "PUT":                       # MR 수정
        STATE["puts"].append(body)
        return STATE.get("put_status", 200), {}
    if method == "GET":                       # MR 조회
        return 200, {"iid": 77, "title": STATE["title"], "labels": list(STATE["labels"]),
                     "target_branch": STATE["target"], "web_url": "https://lab/mr/77"}
    raise AssertionError(f"예상 못한 호출: {method} {url}")


ci_common.http = fake_http


def run(target, branches=(), merged_sources=(), notes=(), open_mrs=(), put_status=200,
        title="[Character] feat: x (S15P21B202-243)", labels=()):
    """stack_order.main() 을 한 번 돌린다. 반환 (종료코드, 보낸 수정들, 코멘트)."""
    STATE.clear()
    STATE.update(target=target, branches=set(branches), merged_sources=set(merged_sources),
                 notes=list(notes), open=list(open_mrs), puts=[], posted=None, put_status=put_status,
                 title=title, labels=list(labels))
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

# ── ⑪ 이행: -244 의 Draft 방식이 걸어 둔 흔적을 지운다 (2026-09-02 실제로 !173·!174·!176 이 그 상태였다)
rc, puts, note, out = run("S15P21B202-241-puzzle", branches=["S15P21B202-241-puzzle"],
                          title="Draft: [Puzzle] feat: x (S15P21B202-247)", labels=["stack-blocked"])
assert any(p.get("remove_labels") == "stack-blocked" for p in puts), puts
assert any(p.get("title") == "[Puzzle] feat: x (S15P21B202-247)" for p in puts), puts
assert rc != 0, "흔적을 지운 뒤에도 스택 자식이면 실패해야 한다"
assert note and "부모가 먼저 머지돼야" in note, note
print("  예전 Draft 흔적 제거 + 판정 유지 ✅")

# ── ⑫ 레이블이 없으면 이행 코드가 아무 것도 하지 않는다(사람이 건 Draft 는 mr-lint 가 본다)
rc, puts, note, _ = run("develop", branches=["develop"],
                        title="Draft: [Puzzle] feat: x (S15P21B202-247)")
assert not puts, f"사람이 건 Draft 를 CI 가 풀면 안 된다: {puts}"
print("  사람이 건 Draft 는 건드리지 않음 ✅")

print("TEST244 OK")
