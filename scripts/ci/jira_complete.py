#!/usr/bin/env python3
"""머지된 MR 의 이슈와 **스택 자식 이슈**를 완료로 옮긴다 (S15P21B202-199).

develop 에 push(=머지)가 일어날 때 도는 잡. 커밋에서 MR 을 역추적해 그 MR 이 데려온
이슈들을 모두 완료로 옮긴다.

왜 CI 인가 — Jira 자동화(R6)로는 두 가지가 안 된다:
  1. R6 는 **대상이 develop 인 MR** 만 완료시킨다(설계). 스택 자식 MR(대상 = 부모의 이슈 브랜치)은
     부모가 develop 에 들어가도 영원히 미완료로 남아 사람이 손으로 옮겨야 했다.
  2. R6 가 놓치는 경우가 실측됐다 — 머지 시점에 이슈가 `해야 할 일` 이면 완료되지 않는다
     (2026-08-31~09-01: -159 · -133). CI 는 머지 사실과 대상 브랜치를 모두 알기 때문에 판정할 수 있다.

왜 기다리는가 — 완료 전환은 R9(수동 완료 되돌림)의 트리거이기도 하다. R9 는 Jira 의
`development[pullrequests]` 색인을 보는데 머지 직후에는 그 색인이 늦어, 바로 옮기면 R9 가 되돌린다
(2026-08-31 실측: 23:24 머지 → 23:29 완료 → **2초 뒤 되돌림**). 그래서 R9 와 **같은 JQL** 로
색인이 따라잡았는지 확인한 뒤 옮긴다. 끝까지 안 따라잡으면 옮기지 않고 남긴다 —
아침 점검의 `🔗`·`🧩` 항목이 다음날 잡아 준다(오탐보다 누락이 낫다는 판단이 아니라, 되돌려질
전환을 굳이 시도하지 않는다는 뜻).

로컬 실행: python scripts/ci/jira_complete.py [커밋SHA]
"""
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from ci_common import (PROJECT_KEY, GitLab, Jira, banner, completes_on_merge,  # noqa: E402
                       env, key_from_branch)

MAX_DEPTH = 5            # 스택 깊이 상한 — 순환·폭주 방지
SWEEP_DAYS = 3           # 훑기 범위 — 이 안에 머지된 것만 본다
SWEEP_MAX = 5            # 한 실행에서 메꿀 최대 건수(잡이 길어지지 않게)
SWEEP_READY = 20         # 훑기 대상은 오래된 머지라 색인이 이미 따라잡았을 것 — 짧게 기다린다
READY_TIMEOUT = 180      # 색인 대기 상한(초)
READY_INTERVAL = 20      # 색인 확인 간격(초)
VERIFY_DELAY = 12        # 전환 뒤 되돌림 확인까지 기다리는 시간(초)


def collect_keys(gl: GitLab, mr: dict, depth: int = 0, seen: set[str] | None = None) -> tuple[list[str], bool]:
    """이 MR 이 develop 에 데려온 이슈 키 전부(자기 + 스택 자손). 반환 (keys, 완전히 훑었는가)."""
    seen = seen if seen is not None else set()
    src = mr.get("source_branch") or ""
    keys: list[str] = []
    ok_all = True

    k = key_from_branch(src)
    if k and k not in seen:
        seen.add(k)
        keys.append(k)

    if depth >= MAX_DEPTH or not src:
        return keys, ok_all

    children, ok = gl.merged_mrs_for_target(src)
    if not ok:
        return keys, False
    for child in children:
        sub, sub_ok = collect_keys(gl, child, depth + 1, seen)
        keys += sub
        ok_all = ok_all and sub_ok
    return keys, ok_all


def landed_on_develop(gl: GitLab, mr: dict, depth: int = MAX_DEPTH) -> bool | None:
    """이 MR 의 변경이 결국 develop 에 도달했는가. 스택이면 부모를 따라 올라간다.
    None = 조회 실패(모름). 훑기에서 **모르면 건드리지 않는다** — 아직 develop 에 없는 것을 완료로
    옮기면 보드가 거짓이 된다."""
    target = mr.get("target_branch") or ""
    seen: set[str] = set()
    while depth > 0:
        if completes_on_merge(target):
            return True
        if not target or target in seen:
            return False
        seen.add(target)
        parent, ok = gl.merged_mr_for_source(target)
        if not ok:
            return None
        if parent is None:            # 부모가 아직 머지되지 않았다
            return False
        target = parent.get("target_branch") or ""
        depth -= 1
    return False


def sweep(gl: GitLab, jira: Jira, sleep=time.sleep) -> list[str]:
    """최근 머지 중 아직 완료가 아닌 이슈를 메꾼다.

    왜 필요한가 — 이 잡은 놓칠 수 있다:
      · 머지가 연달아 일어나면 **잡이 시작되기 전에 파이프라인이 취소된다**
        (2026-09-01 실측: 25초 간격 머지 3건 중 176664 가 canceled. `interruptible: false` 는
         이미 시작된 잡만 보호한다)
      · 토큰 권한·러너 장애로 전환이 실패할 수도 있다(-242)
    그래서 **매 실행이 최근 며칠을 한 번 더 훑는다.** 이미 완료된 이슈에는 아무 일도 하지 않으므로
    몇 번 돌아도 안전하고, 원인이 고쳐지는 순간 다음 머지가 그동안 놓친 것을 정리한다.
    """
    mrs, ok = gl.merged_mrs(SWEEP_DAYS)
    if not ok:
        print("훑기: 머지 목록 조회가 상한에서 끊겼다 — 이번엔 건너뛴다")
    by_key: dict[str, dict] = {}
    for mr in mrs:
        k = key_from_branch(mr.get("source_branch") or "")
        if k and k not in by_key:
            by_key[k] = mr
    if not by_key:
        return []

    # 프로젝트 전체 미완료를 한 번 조회해 교차한다 — 키 목록으로 JQL 을 만들면
    # 존재하지 않는 키 하나에 질의 전체가 거부된다(-134 에서 겪음).
    try:
        issues, _ = jira.search_all(
            f"project = {PROJECT_KEY} AND statusCategory != Done AND issuetype != Epic ORDER BY key",
            ["summary"])
    except Exception as e:      # noqa: BLE001 — 훑기 실패가 본 작업을 막지 않는다
        print(f"훑기: 미완료 이슈 조회 실패({type(e).__name__}) — 건너뛴다"); return []
    pending = [i["key"] for i in issues if i["key"] in by_key]
    if not pending:
        print(f"훑기: 최근 {SWEEP_DAYS}일 머지분에 미완료 이슈 없음"); return []

    print(f"훑기: 미완료 {len(pending)}건 발견 — 최대 {SWEEP_MAX}건 처리 ({', '.join(pending)})")
    out = []
    for k in pending[:SWEEP_MAX]:
        mr = by_key[k]
        if landed_on_develop(gl, mr) is not True:
            print(f"  · {k}: 아직 develop 에 없다(스택 부모 미머지 또는 확인 불가) → 건드리지 않음")
            continue
        r = complete(jira, k, mr["iid"], sleep=sleep, ready_timeout=SWEEP_READY)
        print(f"  · {r}")
        out.append(r)
    return out


def is_done(issue: dict | None) -> bool:
    cat = (((issue or {}).get("fields") or {}).get("status") or {}).get("statusCategory") or {}
    return cat.get("key") == "done"


def wait_ready(jira: Jira, key: str, timeout: int = READY_TIMEOUT, interval: int = READY_INTERVAL,
               sleep=time.sleep) -> bool:
    """R9 가 보는 색인이 따라잡을 때까지 기다린다."""
    waited = 0
    while True:
        if jira.dev_index_ready(key):
            return True
        if waited >= timeout:
            return False
        sleep(interval)
        waited += interval


def complete(jira: Jira, key: str, mr_iid: int, sleep=time.sleep,
             ready_timeout: int = READY_TIMEOUT) -> str:
    """한 이슈를 완료로. 반환: 사람이 읽는 결과 문자열."""
    issue = jira.get_issue(key)
    if issue is None:
        return f"{key}: 이슈 없음(오타·삭제) → 건너뜀"
    if is_done(issue):
        return f"{key}: 이미 완료"

    if not wait_ready(jira, key, timeout=ready_timeout, sleep=sleep):
        return (f"{key}: 개발 정보 색인이 {ready_timeout}초 안에 따라잡지 못함 → 옮기지 않음"
                " (지금 옮기면 R9 가 되돌린다. 아침 점검이 다음날 표시한다)")

    ok, detail = jira.transition_to_done(key)
    if not ok:
        return f"{key}: 전환 실패 — {detail}"

    sleep(VERIFY_DELAY)
    again = jira.get_issue(key)
    if not is_done(again):
        now = (((again or {}).get("fields") or {}).get("status") or {}).get("name", "?")
        return f"{key}: 완료로 옮겼으나 {VERIFY_DELAY}초 뒤 `{now}` 로 되돌려짐 → 아침 점검에 남는다"

    jira.comment(key, f"CI 가 완료로 옮겼습니다 — MR !{mr_iid} 가 develop 에 머지되어 이 이슈의 변경이 "
                      f"develop 에 들어왔습니다. (스택 자식 MR 은 자동 완료(R6) 대상이 아니라 CI 가 판정합니다)")
    return f"{key}: 완료"


def main() -> int:
    banner("jira-complete")
    sha = (sys.argv[1] if len(sys.argv) > 1 else None) or env("CI_COMMIT_SHA")
    if not sha:
        print("커밋 SHA 없음 → 생략"); return 0

    jira = Jira()
    if not jira.available:
        print("JIRA_USER/JIRA_API_TOKEN 없음 → 생략"); return 0

    gl = GitLab()
    mrs, ok = gl.mrs_for_commit(sha)
    if not ok:
        print(f"커밋 {sha[:8]} 의 MR 조회 실패 → 생략(아침 점검이 잡는다)"); return 0
    merged = [m for m in mrs if m.get("state") == "merged" and completes_on_merge(m.get("target_branch") or "")]
    if not merged:
        print(f"커밋 {sha[:8]}: develop·master 로 머지된 MR 이 없음")
        sweep(gl, jira)               # 그래도 놓친 것이 있을 수 있다
        return 0

    lines = []
    for mr in merged:
        keys, full = collect_keys(gl, mr)
        extra = "" if full else "  ⚠ 자식 MR 조회가 일부 실패 — 누락 가능"
        print(f"MR !{mr['iid']} ({mr.get('source_branch')} → {mr.get('target_branch')}) "
              f"이슈 {len(keys)}건: {', '.join(keys) or '없음'}{extra}")
        for k in keys:
            r = complete(jira, k, mr["iid"])
            print(f"  · {r}")
            lines.append(r)

    print("")
    lines += sweep(gl, jira)          # 취소·실패로 놓친 머지를 메꾼다

    done = [x for x in lines if x.endswith(": 완료")]
    print(f"\n== jira-complete: 완료 {len(done)} · 그 밖 {len(lines) - len(done)} ==")
    return 0            # 알림 잡이므로 파이프라인을 막지 않는다


if __name__ == "__main__":
    raise SystemExit(main())
