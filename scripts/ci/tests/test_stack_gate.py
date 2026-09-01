"""스택 자식 MR 을 Draft 로 묶어 머지 순서를 강제하는지 (S15P21B202-244).

실행: python scripts/ci/tests/test_stack_gate.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

왜 이 장치가 있는가 — mr-flow §3 은 스택을 **아래(develop 에 가까운 쪽)부터** 머지하라고 정하는데,
2026-09-01 실측에서 **26 대 0** 으로 아무도 지키지 않았다(머지 139건 중 대상이 이슈 브랜치인 28건 조사).
맨 위부터 부모 브랜치에 넣으면 부모 MR 의 내용이 **리뷰 후에 바뀐다** — !148 은 리뷰 때 이슈 1건이었는데
머지 때 3건을 실어 갔다. 공지로는 이번 주에 전달할 수 없어 **기계적으로 막는다.**

GitLab 은 Draft MR 을 머지하지 못하게 하므로, 파이프라인 설정과 무관한 하드 블록이 된다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"; os.environ["GITLAB_TOKEN"] = "x"
os.environ["CI_MERGE_REQUEST_IID"] = "77"

import ci_common, mr_overlap as O

BRANCHES = set()          # 존재하는 브랜치
puts = []                 # MR 에 보낸 수정
notes = {}                # marker -> body


def fake_http(method, url, headers=None, body=None, timeout=30):
    if "/repository/branches/" in url:
        name = url.rsplit("/", 1)[-1].replace("%2F", "/")
        return (200, {"commit": {"id": "abc1234"}}) if name in BRANCHES else (404, {"message": "404"})
    if url.endswith("/notes?per_page=100&sort=desc"):
        return 200, [{"id": i, "body": b} for i, b in enumerate(notes.values(), 1)]
    if "/notes/" in url and method == "PUT":
        notes[O.GATE_MARKER] = body["body"]; return 200, {}
    if url.endswith("/notes") and method == "POST":
        notes[O.GATE_MARKER] = body["body"]; return 201, {}
    if method == "PUT":                     # MR 수정
        puts.append(body); return 200, {}
    raise AssertionError(f"예상 못한 호출: {method} {url}")


ci_common.http = fake_http
gl = ci_common.GitLab()


def run(target, title, labels, existing_branches):
    """stack_gate 를 한 번 돌린다. 반환 (최종 대상, 보낸 수정들, 코멘트).

    브랜치 존재 여부는 mr-overlap 이 앞서 fetch 한 결과를 인자로 넘긴다(API 재조회 없음).
    """
    global BRANCHES
    BRANCHES = set(existing_branches)
    puts.clear(); notes.clear()
    rep = ci_common.Report("test")
    me = {"iid": 77, "title": title, "labels": list(labels)}
    out = O.stack_gate(gl, rep, me, target, target in BRANCHES)
    return out, list(puts), dict(notes)


# ── ① 스택 자식(부모 브랜치 살아 있음) → Draft + 레이블 + 안내 코멘트
t, ps, ns = run("S15P21B202-140-x", "[Infra] feat: 배포 잡 (S15P21B202-141)", [],
                ["S15P21B202-140-x"])
assert t == "S15P21B202-140-x", t
assert len(ps) == 1, ps
assert ps[0]["title"].startswith("Draft: "), ps[0]
assert ps[0]["add_labels"] == "stack-blocked", ps[0]
assert "부모가 먼저 머지돼야" in ns[O.GATE_MARKER], ns
assert "할 일은 없습니다" in ns[O.GATE_MARKER]
print("  스택 자식 → Draft + 레이블 + 안내 ✅")

# ── ② 이미 CI Draft 면 다시 건드리지 않는다(멱등)
t, ps, ns = run("S15P21B202-140-x", "Draft: [Infra] feat: 배포 잡 (S15P21B202-141)",
                ["stack-blocked"], ["S15P21B202-140-x"])
assert not ps, ps
print("  이미 묶여 있으면 무동작 ✅")

# ── ③ 사람이 건 Draft 라도 레이블만 붙인다(제목은 그대로 — 중복 접두어 방지)
t, ps, ns = run("S15P21B202-140-x", "Draft: [Infra] feat: x (S15P21B202-141)", [],
                ["S15P21B202-140-x"])
assert len(ps) == 1 and "title" not in ps[0] and ps[0]["add_labels"] == "stack-blocked", ps
print("  사람이 건 Draft → 레이블만 ✅")

# ── ④ 부모가 머지돼 대상이 develop 으로 돌아왔다 → Draft 해제
t, ps, ns = run("develop", "Draft: [Infra] feat: 배포 잡 (S15P21B202-141)",
                ["stack-blocked"], ["develop"])
assert t == "develop"
assert len(ps) == 1 and ps[0]["title"] == "[Infra] feat: 배포 잡 (S15P21B202-141)", ps
assert ps[0]["remove_labels"] == "stack-blocked", ps
assert "풀렸습니다" in ns[O.GATE_MARKER], ns
print("  부모 머지 후 → Draft 해제 ✅")

# ── ⑤ 부모 브랜치가 사라졌다(머지됨) → 우리가 develop 으로 되돌린다 (자체 회복)
#      GitLab 이 자동 전환을 안 해 준 경우에도 스택이 영구히 묶이지 않게
t, ps, ns = run("S15P21B202-140-x", "Draft: [Infra] feat: x (S15P21B202-141)",
                ["stack-blocked"], ["develop"])          # 부모 브랜치 없음
assert t == "develop", t
assert any(p.get("target_branch") == "develop" for p in ps), ps
assert any(p.get("remove_labels") == "stack-blocked" for p in ps), ps
print("  부모 브랜치 소멸 → 대상 복구 + 해제 ✅")

# ── ⑥ 평범한 develop 대상 MR 은 아무 것도 하지 않는다
t, ps, ns = run("develop", "[Infra] feat: x (S15P21B202-1)", [], ["develop"])
assert t == "develop" and not ps and not ns, (ps, ns)
print("  일반 MR → 무동작 ✅")

# ── ⑦ hotfix 대상도 이슈 브랜치로 보지 않는다(릴리즈·핫픽스 경로 보호)
t, ps, ns = run("master", "[Infra] release: 정렬", [], ["master"])
assert t == "master" and not ps, ps
print("  master 대상 → 무동작 ✅")

print("TEST244 OK")
