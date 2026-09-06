#!/usr/bin/env python3
"""스택 MR 의 머지 순서를 강제한다 — 자식은 부모가 develop 에 들어간 뒤에만 머지 (S15P21B202-244).

`docs/mr-flow.md` §3 은 스택을 **아래(develop 에 가까운 쪽)부터** 머지하라고 정한다. 그러면 부모가
머지될 때 GitLab 이 자식의 대상을 develop 으로 되돌리고, 자식이 develop 으로 머지되며 Jira 자동화
R6 가 각 이슈를 자연히 완료시킨다. 빈틈이 없다.

**실측 26 대 0 으로 아무도 그 순서를 지키지 않았다**(2026-09-01, 머지 139건 중 대상이 이슈
브랜치였던 28건 조사). `mr-lint` 는 이미 같은 조건을 경고로 달고 있었지만 "머지를 막지는 않는다"
는 문구 그대로 무시됐다. 그래서 **검사를 실패시킨다.**

맨 위부터 부모 브랜치에 넣으면
  · 부모 MR 의 내용이 **리뷰 후에 바뀐다** — !148 은 리뷰 때 이슈 1건이었는데 머지 때 3건을 실어 갔다
  · develop 커밋 하나에 여러 이슈가 뭉쳐 되돌릴 때 분리가 안 된다
  · 자식 이슈가 완료로 가지 않는다 (그건 증상이고 원인은 이 순서다)

왜 별도 잡인가 — `mm-ci-failed` 의 `needs` 에 들지 않아 **🔴 카드를 보내지 않는다.** 작성자가
무엇을 잘못한 것이 아니라 아직 순서가 아닐 뿐이므로, 실패 이유는 MR 코멘트로만 남긴다.

왜 Draft 를 쓰지 않는가 — 팀 규칙이 Draft 를 금지한다(`docs/git-convention.md` §5: "만들지 않는다.
MR 생성 = 리뷰 요청"). CI 가 그 상태를 대신 쓰면 규칙에 예외가 생기고, 그 예외를 사람이 흉내 내
검사를 우회할 수 있다.

**막는 방식은 미해소 스레드다**(S15P21B202-297). 검사 실패만으로는 부족하다 —
`Pipelines must succeed` 는 MR 의 **마지막 파이프라인**만 보므로, 부모가 머지돼 자식이 재대상된
직후의 **낡은 초록**(부모 기준으로 통과한 결과)을 그대로 통과시킨다. 실측한 재대상 → 머지 간격
중위값이 약 20초라 그 창에 들어간다(재대상 16건 중 파이프라인 미생성 14건, 그중 6건이 초록).

그래서 이 잡은 안내를 **스레드로** 달고 미해소로 둔다. `only_allow_merge_if_all_discussions_are_resolved`
가 켜져 있으므로(2026-09-04 확인) **미해소 스레드 하나가 머지 버튼을 잠근다.** 부모가 머지되고
`retarget-pipelines`(-295)가 만든 새 파이프라인에서 이 잡이 통과하면 **스레드를 자동으로 해소**한다.
사람이 개입하는 자리가 없고, 불확실하면 잠긴 채로 남는다(fail-safe).

로컬 실행: CI_MERGE_REQUEST_IID=167 GITLAB_TOKEN=… python scripts/ci/stack_order.py

"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from ci_common import GitLab, Report, banner, completes_on_merge, env  # noqa: E402

MARKER = "<!-- stack-order:metagame -->"

BLOCKED = MARKER + """
🔒 **부모가 먼저 머지돼야 합니다 — 그때까지 `stack-order` 검사가 실패합니다**

이 MR 의 대상은 `{target}` 로, 다른 이슈의 작업 브랜치입니다{parent}. 이대로 머지하면 develop 에는
아무것도 도착하지 않고 그 브랜치 안으로만 들어갑니다. 그러면 **부모 MR 의 내용이 리뷰 후에 바뀝니다** —
실제로 그렇게 머지된 MR 하나가 이슈 3건의 변경을 실어 갔습니다.

**고칠 것은 없습니다. 이 MR 은 잘못되지 않았습니다.** 순서만 기다리면 됩니다:

1. 부모 MR 을 먼저 develop 에 머지합니다
2. GitLab 이 이 MR 의 대상을 자동으로 develop 으로 되돌립니다
3. `retarget-pipelines` 가 **새 파이프라인을 만듭니다**(GitLab 은 그때 파이프라인을 만들지 않습니다)
4. 그 파이프라인에서 이 검사가 통과하면서 **이 스레드가 자동으로 해소되고** 머지 버튼이 풀립니다

**이 스레드를 손으로 해소하지 마세요.** 아직 순서가 아니라는 사실은 클릭으로 바뀌지 않으므로,
다음 파이프라인이 다시 미해소로 되돌립니다.

<sub>순서 규칙: `docs/mr-flow.md` §3 (머지는 아래부터) · 자동 생성(stack-order)</sub>"""

CLEARED = MARKER + """
🔓 **풀렸습니다** — 부모가 develop 에 머지되어 이 MR 의 대상이 `develop` 으로 돌아왔습니다.
`stack-order` 검사가 통과했고 이 스레드도 해소됐습니다. 이제 리뷰·머지 대상입니다.

<sub>자동 생성(stack-order)</sub>"""


def parent_note(gl: GitLab, target: str) -> str:
    """부모 MR 번호를 찾아 코멘트에 넣는다. 못 찾아도 검사 결과는 달라지지 않는다."""
    for m in gl.open_mrs():
        if m.get("source_branch") == target:
            return f"(부모 MR [!{m['iid']}]({m.get('web_url', '')}))"
    return ""




def main() -> int:
    banner("stack-order")
    rep = Report("stack-order")
    gl = GitLab()
    if not gl.iid:
        print("MR 파이프라인이 아님 → 생략")
        return 0

    mr = gl.get_mr()
    target = mr.get("target_branch") or ""

    if completes_on_merge(target):
        # 대상이 develop·master 다. 전에 막혔던 MR 이면 풀렸다고 알린다.
        if any(MARKER in (n.get("body") or "") for n in gl.notes()):
            _st, what = gl.upsert_thread(MARKER, CLEARED, resolved=True)
            rep.ok(f"부모가 머지돼 대상이 `{target}` 으로 돌아왔다 — 순서 조건 충족 ({what})")
        else:
            rep.ok(f"대상이 `{target}` — 스택이 아니다")
        return rep.finish()

    # 여기부터는 대상이 다른 이슈의 작업 브랜치다.
    # ① 부모 브랜치가 사라졌다 = 부모가 머지되고 브랜치까지 지워졌다
    gone = gl.branch_head(target) is None
    # ② 브랜치는 남아 있는데 그 브랜치를 소스로 한 MR 이 이미 머지됐다 —
    #    머지 뒤 브랜치를 지우지 않은 경우다. 이때 실패시키면 억울한 차단이 된다.
    merged = None
    if not gone:
        merged, _ok = gl.merged_mr_for_source(target)

    if gone or merged:
        why = ("부모 브랜치가 이미 지워졌다" if gone
               else f"부모 MR !{merged['iid']} 가 이미 머지됐다(브랜치만 남아 있다)")
        st, _ = gl.update_mr(target_branch="develop")
        if st == 200:
            _st, what = gl.upsert_thread(MARKER, CLEARED, resolved=True)
            rep.ok(f"{why} → 대상을 `develop` 으로 되돌렸다. 다음 파이프라인에서 통과한다 ({what})")
            return rep.finish()
        rep.fail(f"{why} 그런데 대상 전환에 실패했다({st}) — "
                 f"MR 화면에서 대상을 `develop` 으로 바꿔 주세요")
        return rep.finish()

    # ③ 부모가 아직 develop 에 없다 → 지금 머지하면 안 된다.
    #    검사 실패만으로는 낡은 초록을 못 막으므로 **미해소 스레드**로 머지 버튼까지 잠근다(-297).
    _st, what = gl.upsert_thread(
        MARKER, BLOCKED.format(target=target, parent=" " + parent_note(gl, target)), resolved=False)
    print(f"  스레드: {what}")
    if gl.project_flag("only_allow_merge_if_all_discussions_are_resolved") is False:
        rep.warn("프로젝트 설정 'All threads must be resolved' 가 꺼져 있어 스레드가 머지를 막지 못한다 "
                 "— Settings → Merge requests 에서 켜야 이 잡의 차단이 실제로 작동한다")
    rep.fail(f"대상이 `{target}`(다른 이슈의 작업 브랜치)이고 그 부모가 아직 develop 에 없다 — "
             f"**부모를 먼저 머지하세요.** 이 MR 은 고칠 것이 없고, 부모가 머지되면 이 검사는 "
             f"저절로 통과한다(GitLab 이 대상을 develop 으로 되돌린다). 규칙: docs/mr-flow.md §3")
    if env("STACK_ORDER_ENFORCE", "1") == "0":
        print("STACK_ORDER_ENFORCE=0 → 실패로 만들지 않는다(경고만)")
        return 0
    return rep.finish()


if __name__ == "__main__":
    raise SystemExit(main())
