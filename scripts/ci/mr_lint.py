"""mr-lint — MR 제목·브랜치·Jira 키·템플릿 검사. 위반 시 exit 1 (파이프라인 빨간불).

규칙: docs/git-convention.md §5, docs/jira-convention.md, .gitlab/merge_request_templates/Default.md
"""
import json
import os
import re

from urllib.parse import quote

from ci_common import (BRANCH_RE, KEY_RE, PROJECT_KEY, STORY_POINTS_FIELD, TITLE_RE, GitLab, Jira,
                       Report, banner, env,
                       env_int, find_section, http, is_release_mr, key_from_branch, load_team,
                       part_of_issue, release_version, sections, strip_draft)

# 본문에 있어야 하는 절 (S15P21B202-419).
#
# **`확인 방법`·`산출물 위치`·`리뷰어 확인` 을 뺐다.**
#   · `산출물 위치` — Changes 탭이 그대로 보여 준다. 사람이 한 번 더 옮겨 적을 이유가 없다
#   · `확인 방법`·`리뷰어 확인` — `머지 전에 팀장이 확인할 것` 하나로 합쳤다. 같은 질문의
#     세 판본이었고, 셋으로 나뉘어 있는 동안 아무도 읽지 않았다(2026-09-10 팀 확인)
#
# `-395`(작성자 체크리스트)·`-415`(리뷰어 체크리스트)와 같은 근거다 — 기계가 이미 보는
# 것을 사람에게 다시 적으라고 하면 채우기만 한다.
REQUIRED_SECTIONS = ["Jira", "무엇을 · 왜"]

# 머지 전에 사람이 해야 하는 확인 (S15P21B202-395).
#
# 예전에는 `작성자 체크리스트` 상자 5개를 **전부 체크**하라고 했다. 그런데 그중 4개는
# **CI 가 이미 검사한다** — `develop` 최신·충돌(merge_status·mr-overlap·pre-push 훅) ·
# 컴파일(unity-compile) · `.meta`·위치와 금지 파일(repo-hygiene). 기계가 증명한 것을
# 사람에게 다시 단정하라고 하니 한계 정보가 0 이고, 그래서 **아무도 읽지 않고 기계적으로
# 찍었다.** 템플릿의 리뷰어 절 주석이 이미 "형식·메타·컴파일·위생은 CI가 봤고" 라고
# 적고 있었다.
#
# 남은 하나(Jira 완료 조건)는 **기계가 못 보고, 올리는 시점에 대개 거짓**이다. 우리
# 완료 조건에는 사람이 해야 하는 실물 확인이 자주 들어간다. 그래서 정직하게 비워 두면
# 이 검사가 막고, 도장을 찍으면 통과했다 — **인센티브가 뒤집혀 있었다**(2026-09-10
# 실제로 그랬다: !290 AI 리뷰가 "미수행인데 체크돼 있다" 고 해서 풀었고 !295·!297 이
# 막혔다).
#
# 그래서 상자를 없애고 **문장**을 요구한다. 상자는 찍고 싶게 만들고 문장은 그렇지 않다.
# 그리고 문장은 AI 리뷰가 diff 와 맞춰 판정할 수 있다.
#
# `-419` 에서 이름을 한 번 더 바꿨다. 이 절이 `확인 방법`·`리뷰어 확인` 까지 흡수했으므로
# "사람이 해야 하는 확인" 보다 "팀장이 머지 전에 확인할 것" 이 그 역할을 그대로 말한다.
HUMAN_CHECKS_SECTION = "머지 전에 팀장이 확인할 것"

# 옛 이름들 — 이미 열려 있는 MR 을 막지 않기 위해 **경고**로 둔다. 규칙을 바꾸는 순간
# 열려 있던 MR 이 전부 빨간불이 되면, 규칙이 아니라 사람이 막힌다(`-395` 에서 정한 것).
LEGACY_CHECK_SECTIONS = ["머지 전에 사람이 해야 하는 확인", "작성자 체크리스트"]
_TEAM = load_team()
JIRA_BY_GITLAB = {m["gitlab"]: m.get("jira", "") for m in _TEAM["members"]}
NAME_BY_GITLAB = {m["gitlab"]: m.get("name", "") for m in _TEAM["members"]}


def is_bot_thread(first_note: dict) -> bool:
    """봇이 스스로 관리하는 스레드인가 (S15P21B202-420).

    `stack-order` 는 **일부러** 미해소 스레드를 남긴다 — 스택 순서를 어긴 머지를 막는
    수단이 그것이고, 조건이 풀리면 스스로 해결 처리한다(`upsert_thread(resolved=True)`).
    그래서 이것을 사람 답글과 같이 세면, 스택 MR 마다 "봇이 스레드를 남겼다" 가 떠서
    **정작 찾으려던 사람 답글이 묻힌다.**

    **첫 줄 마커로 가른다.** 봇 코멘트는 갱신을 위해 첫 줄에 HTML 주석 마커를 둔다는
    규약이 이미 있다(`ai-review:metagame` · `stack-order:metagame` · `mr-lint:advice` 등).
    이름 목록을 여기 박지 않는 이유는 잡이 늘 때마다 이 함수를 고쳐야 하기 때문이고,
    `:metagame` 만 보면 `mr-lint:advice` 를 놓친다.

    계정 이름으로 가르지 않는 이유: CI 토큰이 어느 계정으로 도는지는 설정에 따라 달라지고,
    사람이 그 계정으로 답글을 달 수도 있다. 사람은 첫 줄에 HTML 주석을 쓰지 않는다.
    """
    lines = (first_note.get("body") or "").lstrip().splitlines()[:1]
    first = lines[0].strip() if lines else ""
    return first.startswith("<!--") and first.endswith("-->")


def check_open_threads(rep: Report, gl: GitLab) -> None:
    """미해소 스레드가 있으면 알린다 (S15P21B202-420).

    **왜 필요한가.** 프로젝트 설정
    `only_allow_merge_if_all_discussions_are_resolved` 가 켜져 있으면 **미해소 스레드
    하나가 머지 버튼을 잠근다.** 그런데 그 사실이 어디에도 나오지 않는다 — 파이프라인은
    초록이고 카드도 안 나온다. MR 화면에서 "미해결 스레드" 를 직접 찾아야 안다.

    2026-09-10 에 `!320`·`!321`·`!322`·`!324` 가 그 상태였다. 전부 초록인데 잠겨 있었고,
    원인은 사람이 남긴 답글이 스레드로 만들어진 것이었다(`glab mr note create`).

    **실패로 만들지 않는다.** 스레드는 리뷰가 진행 중이라는 정상 상태일 수 있다. 막는
    것이 목적이 아니라 **모르는 채로 두지 않는 것**이 목적이다(`-309` 와 같은 결).

    설정이 꺼져 있으면 잠기지 않으므로 말하지 않는다. 설정을 못 읽으면(권한·오프라인)
    조용히 넘긴다 — 확인하지 못한 것을 경고로 만들면 늑대 소년이 된다.
    """
    if not gl.token:
        return
    if gl.project_flag("only_allow_merge_if_all_discussions_are_resolved") is not True:
        return

    threads = gl.discussions()
    if threads is None:
        return

    open_by = {}
    bots = 0
    for d in threads:
        notes = d.get("notes") or []
        first = notes[0] if notes else None
        if not first or not first.get("resolvable"):
            continue
        if all(n.get("resolved") for n in notes if n.get("resolvable")):
            continue
        if is_bot_thread(first):
            bots += 1
            continue
        who = (first.get("author") or {}).get("username", "?")
        open_by[who] = open_by.get(who, 0) + 1

    total = sum(open_by.values())
    if not total:
        rep.ok("미해소 스레드 없음" + (f" (봇 스레드 {bots}개는 조건이 해소되면 스스로 풀립니다)" if bots else ""))
        return

    who = " · ".join(f"{u} {n}개" for u, n in sorted(open_by.items(), key=lambda kv: -kv[1]))
    rep.warn(f"미해소 스레드 {total}개({who}) — **파이프라인이 초록이어도 머지가 잠깁니다** "
             f"(프로젝트 설정: 모든 스레드 해결 필수). 답한 스레드는 해결 처리하세요")


def check_squash(rep: Report, mr: dict, target: str) -> None:
    """squash 는 **모든 MR 에서** 꺼져 있어야 한다 (S15P21B202-417).

    `mr-title` 이 먼저 API 로 끄므로(needs) 여기서 켜져 있다면 토큰·권한 문제거나
    사람이 다시 켠 것이다.

    **왜 전면인가.** 처음에는 master 대상만 껐다(`-325`) — 그때는 스택 MR 이 없었다.
    지금은 `mr-overlap` 이 스택을 감지해 대상을 자동 전환하고 `stack-order` 가 순서를
    강제한다. squash 는 **스택의 부모**에서 문제가 된다: 부모의 커밋을 버리고 같은
    내용의 새 커밋을 만들므로 자식과 develop 의 조상 관계가 끊기고, 머지 베이스가 뒤로
    밀려 자식을 리베이스할 때 **부모의 커밋까지 다시 재생**한다. 그 사이 누가 같은 줄을
    고쳐 놨으면 충돌이다(2026-09-10 재현).

    껴야 하는 쪽이 부모라서 "스택만 해제" 로는 못 막는다 — 부모를 머지할 때 자식이 아직
    없다가 나중에 생기면 그 판정이 소용없다.

    **잃는 것이 없다.** `merge_method = merge` 라 MR 하나마다 `Merge branch '…'` 커밋이
    반드시 하나 생긴다 — "이슈 1개 = develop 커밋 1개" 가 원한 표식이 그것이고,
    `publish_release.collect_entries` 가 세는 것도 그 머지 커밋이다.
    """
    if not mr.get("squash"):
        rep.ok("squash 해제")
        return

    where = "master 대상 MR" if target == "master" else "MR"
    rep.fail(f"{where} 에 squash 가 켜져 있음 — 스택 MR 의 부모가 squash 되면 자식이 부모의 변경을 "
             f"다시 짊어지고, 그 사이 누가 같은 줄을 고쳤으면 충돌한다(S15P21B202-417). "
             f"mr-title 이 자동으로 끄는데 아직 켜져 있으면 GITLAB_TOKEN 권한을 확인하거나 "
             f"MR 화면에서 'Squash commits' 를 해제")


def human_checks_content(section: str | None) -> list[str]:
    """섹션에서 **내용 있는 줄**만 골라 준다.

    주석(`<!-- -->`)과 빈 줄은 내용이 아니다 — 템플릿을 그대로 두고 올리는 경우가
    통과하면 안 된다. 목록 기호만 있는 줄(`-`)도 내용이 아니다."""
    if section is None:
        return []

    lines = []
    in_comment = False
    for raw in section.splitlines():
        line = raw.strip()
        if in_comment:
            if "-->" in line:
                in_comment = False
                line = line.split("-->", 1)[1].strip()
            else:
                continue
        while "<!--" in line:
            head, rest = line.split("<!--", 1)
            if "-->" in rest:
                line = (head + rest.split("-->", 1)[1]).strip()
            else:
                line = head.strip()
                in_comment = True
                break
        line = line.lstrip("-*").strip()
        if line:
            lines.append(line)
    return lines


def check_human_checks(rep: Report, secs) -> None:
    """머지 전에 팀장이 확인할 것 (S15P21B202-395 · -419).

    옛 이름(`작성자 체크리스트` · `머지 전에 사람이 해야 하는 확인`)으로 올라온 MR 은
    **경고**로 둔다. 규칙을 바꾸는 순간 이미 열려 있던 MR 이 전부 빨간불이 되면,
    규칙이 아니라 사람이 막힌다."""
    section = find_section(secs, HUMAN_CHECKS_SECTION)
    if section is not None:
        lines = human_checks_content(section)
        if not lines:
            rep.fail(f"본문 `## {HUMAN_CHECKS_SECTION}` 이 비어 있음 — 기계가 못 보는 확인을 "
                     f"적거나 `없음` 이라고 쓸 것(주석만 남기면 통과하지 않는다)")
        else:
            rep.ok(f"머지 전 사람 확인 {len(lines)}줄")
        return

    for legacy in LEGACY_CHECK_SECTIONS:
        if find_section(secs, legacy) is not None:
            rep.warn(f"옛 `## {legacy}` 절 — 새 템플릿의 `## {HUMAN_CHECKS_SECTION}` 으로 "
                     f"바꿔 주세요(S15P21B202-419). 그 절이 `확인 방법`·`리뷰어 확인` 까지 "
                     f"흡수했습니다")
            return

    rep.fail(f"본문 템플릿 섹션 누락: ## {HUMAN_CHECKS_SECTION} — 템플릿 섹션 제목은 지우지 말 것")


def existence_problem(key: str | None, issue: dict | None, lookup_failed: bool) -> str | None:
    """이슈 실존 검사 결과(실패 메시지 또는 None).
    조회 자체가 실패했으면 판정하지 않는다 — Jira 장애·토큰 만료를 '이슈가 없다' 로 오해하면 모든 MR 이 잘못된 이유로 빨간불이 된다(S15P21B202-94)."""
    if lookup_failed or not key:
        return None
    if issue is None:
        return (f"Jira 이슈 {key} 가 존재하지 않음 — 브랜치 이름의 번호를 확인하세요. "
                f"번호를 잘못 적었다면 그 번호의 **다른 이슈**가 자동으로 진행 중으로 바뀌었을 수 있습니다")
    return None


# 스토리 포인트를 매기지 않는 유형. **R11(진행 중 게이트)의 제외 조건과 같아야 한다** —
# 그 둘이 어긋나면 같은 이슈가 한 자리에서는 막히고 다른 자리에서는 통과한다.
POINTLESS_TYPES = ("에픽", "Epic", "스토리", "Story")


def check_story_points(rep: Report, key: str, fields: dict) -> None:
    """스토리 포인트가 비어 있으면 막는다 (S15P21B202-568).

    **왜 MR 시점인가.** 이 검사는 전에 두 번 빠졌다 — `-88` 은 R5 자동화에서(브랜치만
    만든 이슈가 되돌아가서), `-482` 는 생성 화면에서(*"만드는 순간에는 예측이 안 서는
    이슈가 있고, 막으면 아무 숫자나 넣게 된다"*). 두 반대 다 **만들 때·진행 중으로 갈 때**
    막는 것에 대한 것이었다. MR 을 올리는 시점에는 작업이 끝나 있어 크기가 추측이 아니므로
    그 실패 모드가 없다. 생성 시 필수로 만들지 않는 결정은 그대로다.

    **번호가 틀리면 모든 MR 이 막힌다.** Jira 는 모르는 필드를 조용히 빼고 주므로,
    "값이 없다" 와 "필드를 못 받았다" 를 가른다 — 후자는 우리 설정 문제이지 작성자의
    잘못이 아니다. 팀 전체를 세우는 대신 경고로 알린다.

    **`0` 은 유효한 값이다.** 참/거짓으로 보면 0 포인트가 빈 값이 된다.
    """
    if STORY_POINTS_FIELD not in fields:
        rep.warn(f"스토리 포인트 필드({STORY_POINTS_FIELD})를 Jira 가 돌려주지 않음 — "
                 "번호가 바뀌었는지 확인 필요(ci_common.STORY_POINTS_ID). 이 검사는 건너뜀")
        return
    if fields[STORY_POINTS_FIELD] is None:
        rep.fail(f"Jira 이슈 스토리 포인트 없음 — 담당자·상위 Epic 과 같은 필수 필드입니다. "
                 f"{key} 를 열어 채우세요(척도: docs/jira-convention.md §5). "
                 "만들 때의 예측이 아니라 **끝난 작업의 크기**를 적으면 됩니다")
        return
    rep.ok(f"스토리 포인트 {fields[STORY_POINTS_FIELD]:g}")


def assignee_mismatch(mr: dict, issue: dict) -> str | None:
    """MR 작성자 ≠ Jira 담당자면 경고 문구. 브랜치 이름의 번호를 오타로 적어 남의 이슈를 건드린 경우가 여기서 드러난다.
    대행 작업도 정상이므로 차단하지 않는다. team.json 에 매핑이 없는 계정(봇 등)은 건너뛴다."""
    author = (mr.get("author") or {}).get("username", "")
    author_jira = JIRA_BY_GITLAB.get(author)
    assignee = issue.get("fields", {}).get("assignee") or {}
    if not author_jira or not assignee.get("accountId"):
        return None
    if author_jira == assignee.get("accountId"):
        return None
    return (f"MR 작성자({NAME_BY_GITLAB.get(author, author)})와 Jira 담당자({assignee.get('displayName')})가 다릅니다 — "
            f"브랜치 이름의 이슈 번호를 잘못 적었다면 그 이슈가 자동으로 진행 중이 됩니다(대행 작업이면 무시)")
# 이 커밋 수 이상 뒤처진 시작점이면 경고. 임포트 시점에 int() 하지 않는다 — 변수 오설정으로
# 차단 잡이 죽으면 머지 자체가 막힌다(-134). 필요할 때 읽고, 숫자가 아니면 기본값으로 계속한다.
STALE_WARN_DEFAULT = 25


def stale_warn() -> int:
    return env_int("MR_STALE_WARN_COMMITS", STALE_WARN_DEFAULT)
ADVICE_MARKER = "<!-- mr-lint:advice -->"
REBASE_HINT = ("`git fetch origin && git rebase origin/develop && git push --force-with-lease` — MR·코멘트는 그대로 유지되고 "
               "파이프라인이 다시 돌면 이 경고는 사라집니다. 씬·프리팹이 겹쳐 충돌하면 한쪽을 고르거나 다시 편집해야 합니다(머지 때 치를 비용을 미리 치르는 것)")


def _hdrs(gl: GitLab) -> dict:
    """S15P21B202-109 의 공개 헬퍼가 머지되기 전후 모두 동작하도록."""
    return gl.headers() if hasattr(gl, "headers") else gl._h()


def merged_before(gl: GitLab, branch: str) -> str | None:
    """같은 소스 브랜치로 **이미 머지된 MR** 이 있으면 경고 문구. 머지된 브랜치를 되살려 계속 작업하는 것을 잡는다.
    (머지 시 브랜치가 자동 삭제되므로, 같은 이름으로 다시 push 하면 MR·파이프라인 없이 조용히 남는다 — S15P21B202-110)"""
    if not gl.token or not branch:
        return None
    st, d = http("GET", f"{gl.api}/projects/{gl.project}/merge_requests"
                        f"?state=merged&source_branch={quote(branch, safe='')}&per_page=5", _hdrs(gl))
    if st != 200 or not isinstance(d, list) or not d:
        return None
    iids = ", ".join(f"!{m.get('iid')}" for m in d[:3])
    return (f"이 브랜치로 이미 머지된 MR 이 있습니다({iids}) — 머지된 브랜치를 되살려 계속 작업하면 "
            f"MR·파이프라인 없이 조용히 남을 수 있습니다. 후속 작업은 **새 이슈 · 새 브랜치**로 하세요")


def base_warnings(gl: GitLab, branch: str, target: str, bkey: str | None) -> list[tuple[str, str]]:
    """브랜치가 develop 이 아닌 곳에서 갈라졌는지 검사해 (레벨, 메시지) 목록을 돌려준다. 차단하지 않는다(warn/ok).
    - master(또는 그 이전) 기반: develop 기준 merge-base 와 master 기준 merge-base 가 같음(master 는 develop 의 조상)
    - 오래된 시작점: develop 대비 MR_STALE_WARN_COMMITS(기본 25) 커밋 이상 뒤
    - 다른 이슈 키가 섞인 커밋: 다른 작업 브랜치에서 갈라졌을 가능성
    hotfix/* 는 master 기반이 정상이므로 제외."""
    out: list[tuple[str, str]] = []
    if not gl.token or target != "develop" or branch.startswith("hotfix/") or branch in ("develop", "master"):
        return out
    base_dev = gl.merge_base("develop", branch)
    base_master = gl.merge_base("master", branch)
    dev_head = gl.branch_head("develop")
    behind = gl.behind_count(base_dev) if base_dev else None
    if base_dev and base_master and base_dev == base_master and dev_head and base_dev != dev_head:
        out.append(("warn", f"이 브랜치는 develop 이 아니라 master(또는 그 이전) 지점에서 갈라졌습니다 — 시작점 {base_dev[:8]}"
                            + (f", develop 보다 {behind}커밋 뒤" if behind else "")
                            + ". 최신 씬·에셋·스크립트가 없는 코드일 수 있으니 develop 에서 브랜치를 새로 만들어 작업을 옮기거나 "
                              "`git fetch && git rebase origin/develop` 후 다시 push 하세요 (hotfix/* 는 master 기반이 정상)"))
    elif behind is not None and behind >= stale_warn():
        out.append(("warn", f"브랜치 시작점이 develop 보다 {behind}커밋 뒤처졌습니다 — 씬·프리팹·에셋이 겹치면 머지에서 충돌하거나 "
                            f"최신 작업을 덮어쓸 수 있습니다. `git fetch && git rebase origin/develop` 권장"))
    elif behind is not None:
        out.append(("ok", f"브랜치 시작점 OK (develop 대비 {behind}커밋 뒤)"))
    other = sorted({k for c in gl.mr_commits() for k in KEY_RE.findall(c.get("title") or "") if bkey and k != bkey})
    if other:
        out.append(("warn", f"이 MR 의 커밋에 다른 이슈 키가 섞여 있습니다: {', '.join(other[:5])} — 다른 작업 브랜치에서 갈라졌을 수 있고, "
                            f"머지하면 남의 미완성 변경이 develop 에 들어갑니다"))
    return out


def target_verdict(branch: str, target: str) -> tuple[str, str]:
    """대상 브랜치 판정 — (레벨, 메시지). 레벨: ok | warn | fail
      develop  : 기본 경로
      master   : 릴리즈·핫픽스만(develop · release/<키>-vX.Y.Z · hotfix/*) — S15P21B202-325
      작업 브랜치: **스택 MR** 허용(팀 결정 2026-08-28, S15P21B202-99). 단 머지해도 Jira 이슈가 자동 완료되지 않는다 —
                  부모 브랜치가 develop 에 머지된 뒤 손으로 완료해야 한다(R9 가 허용).

    R6 의 실제 조건은 "대상이 develop 일 때만 완료" 가 아니다(-134 정정). 조건은
    `{{pullRequest.destinationBranch.name.match("^(?:hotfix/)?(S15P21B202-\\d+)")}}` **EQUALS** `""` —
    대상 이름에서 이슈 키가 잡히면(=작업 브랜치면) 조건이 실패해 전환하지 않는다. 스마트 값이 비어도 `""` 가
    되어 완료가 유지되는 **fail-open** 설계다. 그래서 대상이 develop 이든 master 든 키가 안 잡히면 완료된다.
    이후 R6 를 손볼 사람이 조건을 "대상 = develop" 으로 오해하지 않도록 여기에 실제 식을 적어 둔다.
    """
    if target == "develop":
        return "ok", "대상 브랜치 develop"
    if target == "master":
        if branch.startswith("hotfix/") or branch == "develop":
            return "ok", "대상 브랜치 master (릴리즈·핫픽스)"
        if branch.startswith("release/"):
            ver = release_version(branch)
            if ver:
                return "ok", f"대상 브랜치 master — 릴리스 브랜치 {branch} (버전 {ver}; 머지되면 CI 가 태그·Release 를 만든다)"
            return "fail", (f"릴리스 브랜치 이름 형식 위반: {branch!r} — `release/{PROJECT_KEY}-번호-vX.Y.Z` "
                            f"(예: release/{PROJECT_KEY}-318-v0.1.0). 버전이 이름에 있어야 CI 가 태그·Release 를 만든다")
        return "fail", "master 로는 develop · hotfix/* · release/<키>-vX.Y.Z 만 머지 (git-convention §8)"
    if target.startswith("hotfix/"):
        # hotfix 는 master 로 직행하는 브랜치다. 그 위에 또 MR 을 쌓으면 R6 조건이 대상 이름에서 키를 잡아
        # 완료 전환이 막히는 데다, 검증받지 않은 변경이 핫픽스에 얹혀 master 로 들어간다(-134).
        return "fail", f"hotfix 브랜치를 대상으로 하는 MR 은 만들지 않는다: {target!r} — develop 대상으로 다시 만드세요"
    if BRANCH_RE.match(target):
        return "warn", (f"스택 MR — 대상이 develop 이 아니라 작업 브랜치({target})입니다. "
                        f"**부모를 먼저 develop 에 머지해야 합니다** — 그 순서는 `stack-order` 검사가 막습니다(-244). "
                        f"부모가 머지되면 GitLab 이 이 MR 의 대상을 develop 으로 되돌리고(18.11 실측), "
                        f"그때 이 MR 을 머지하면 Jira 이슈도 R6 가 저절로 완료시킵니다. "
                        f"지금 이대로 머지하면 develop 에는 아무것도 도착하지 않고 부모 MR 의 내용이 리뷰 후에 바뀝니다")
    return "fail", f"대상 브랜치는 develop · master(릴리즈) · 작업 브랜치(스택 MR) 중 하나여야 함: {target!r}"
def publish_advice(gl: GitLab, mr: dict, warns: list[tuple[str, str]], base_warned: bool) -> None:
    """경고를 눈에 보이는 곳에 남긴다(차단 아님).
      A) MR 코멘트 1개 — 마커로 갱신되므로 재실행해도 쌓이지 않고, 경고가 사라지면 '해소됨' 으로 바뀐다
      B) master(또는 그 이전) 기반일 때만 MM 🟠 카드 — 작성자 @멘션 (그 밖의 경고는 채널로 보내지 않는다)"""
    if not gl.can_write:
        return
    msgs = [m for lvl, m in warns if lvl == "warn"]
    if msgs:
        rows = "\n".join(f"- ⚠ {m}" for m in msgs)
        body = (f"{ADVICE_MARKER}\n⚠ **확인 사항 {len(msgs)}건** — 머지를 막지는 않지만 리뷰 전에 보세요.\n\n{rows}\n\n"
                f"**브랜치를 develop 위로 옮기려면**: {REBASE_HINT}\n\n"
                f"<sub>자동 생성(mr-lint) · 다시 push 하면 이 코멘트가 갱신됩니다</sub>")
    else:
        if not any(ADVICE_MARKER in (n.get("body") or "") for n in gl.notes()):
            return
        body = f"{ADVICE_MARKER}\n✅ 이전 확인 사항이 해소되었습니다. <sub>자동 생성(mr-lint)</sub>"
    st, _ = gl.upsert_note(ADVICE_MARKER, body)
    print(f"  · 확인 사항 코멘트 {'게시/갱신' if st in (200, 201) else '실패 ' + str(st)}")
    if base_warned and env("MM_WEBHOOK_URL"):
        try:
            from mm_notify import card, mm_of, send   # 지연 임포트: MM 설정이 없으면 건드리지 않는다
            author = (mr.get("author") or {}).get("username", "")
            mention = mm_of(author) or author or "?"
            send(card("🟠 **브랜치 기준 확인 필요**", [
                ("MR", f"[!{gl.iid} {(mr.get('title') or '')[:60]}]({mr.get('web_url')})"),
                ("작성", mention),
                ("무엇", "이 브랜치가 develop 이 아니라 master(또는 그 이전) 지점에서 갈라졌습니다 — 최신 씬·에셋·스크립트가 없는 코드일 수 있습니다"),
                ("조치", "`git fetch origin && git rebase origin/develop && git push --force-with-lease` (hotfix 는 정상이므로 이 알림 대상이 아님)"),
            ]))
        except Exception as e:  # 알림 실패가 검사 결과를 바꾸지 않도록
            print(f"  · MM 알림 실패(무시): {type(e).__name__}: {e}")


def main() -> int:
    banner("mr-lint")
    rep = Report("mr-lint")
    gl, jira = GitLab(), Jira()
    mr = gl.get_mr()
    raw_title = mr.get("title") or ""
    title, is_draft = strip_draft(raw_title)
    branch = mr.get("source_branch") or ""
    target = mr.get("target_branch") or ""
    desc = mr.get("description") or ""
    print(f"MR: {raw_title!r}\n브랜치: {branch} → {target}")

    # 1) Draft 금지 — 예외 없음.
    #    스택 순서는 별도 잡 `stack-order` 가 강제한다. Draft 를 그 수단으로 쓰면 팀 규칙
    #    (docs/git-convention.md §5 "만들지 않는다")에 예외가 생기고, 사람이 그 예외를 흉내 내
    #    이 검사를 우회할 수 있다 (S15P21B202-244).
    if is_draft or mr.get("draft"):
        rep.fail("Draft MR 금지 — MR 생성 = 리뷰 요청 (진행 공유는 Jira 코멘트로). 'Mark as ready' 후 재실행")
    else:
        rep.ok("Draft 아님")

    check_squash(rep, mr, target)
    check_open_threads(rep, gl)

    # 1b) 릴리즈 MR(develop → master)은 여기까지만 — 브랜치·제목·템플릿은 이 MR 에 해당하지 않는다.
    #    내용은 develop 으로 들어올 때 이미 검사했다 (S15P21B202-122)
    if is_release_mr(branch, target):
        tlevel, tmsg = target_verdict(branch, target)   # 대상 규칙은 가정하지 않고 실제로 판정한다
        {"ok": rep.ok, "warn": rep.warn, "fail": rep.fail}[tlevel](tmsg)
        if branch.startswith("release/"):
            # 릴리스 브랜치는 develop 의 스냅샷이다. 자기 커밋이 있으면 그 변경은 develop 에 없어 다음 릴리스에서
            # 사라지고, develop 재머지라는 절차가 하나 더 생긴다. 규칙: 릴리스 브랜치에는 커밋하지 않는다.
            ahead = gl.ahead_count(branch, "develop") if gl.token else None
            if ahead is None:
                rep.warn("릴리스 브랜치에 develop 밖 커밋이 있는지 확인하지 못함(GITLAB_TOKEN 없음 또는 조회 실패)")
            elif ahead:
                rep.fail(f"릴리스 브랜치에 develop 에 없는 커밋 {ahead}개 — 릴리스 브랜치에는 커밋하지 않는다(develop 의 스냅샷). "
                         f"develop 에서 고친 뒤 브랜치를 다시 따세요(git-convention §8)")
            else:
                rep.ok("릴리스 브랜치에 자기 커밋 없음 — develop 스냅샷 그대로")
        rep.ok(f"릴리즈 MR ({branch} → master) — 이슈 키 · 제목 형식 · 템플릿 · 체크리스트 검사는 "
               "적용하지 않음 (내용은 develop 에 머지될 때 이미 검사됨)")
        return rep.finish()

    # 2) 브랜치 이름
    bkey = key_from_branch(branch)
    if bkey:
        rep.ok(f"소스 브랜치 형식 OK (키 {bkey})")
    else:
        rep.fail(f"소스 브랜치 형식 위반: {branch!r} — `{PROJECT_KEY}-번호[-설명]` · `hotfix/…` · `release/{PROJECT_KEY}-번호-vX.Y.Z`")
    tlevel, tmsg = target_verdict(branch, target)
    {"ok": rep.ok, "warn": rep.warn, "fail": rep.fail}[tlevel](tmsg)

    # 2b) 브랜치 베이스 (경고만 — 차단하지 않음, S15P21B202-90)
    base_notes = base_warnings(gl, branch, target, bkey)
    prev = merged_before(gl, branch)
    if prev:
        base_notes.append(("warn", prev))
    for level, msg in base_notes:
        (rep.ok if level == "ok" else rep.warn)(msg)
    # (경고 목록은 rep.warns 로 모아서 마지막에 한 번에 코멘트로 올린다 — S15P21B202-105)
    base_warned = any(lvl == "warn" and "master(또는 그 이전)" in m for lvl, m in base_notes)

    # 3) 제목
    m = TITLE_RE.match(title)
    if m:
        part, typ, _scope, body, tkey = m.group(1), m.group(2), m.group(3), m.group(4), m.group(5)
        rep.ok(f"제목 형식 OK — [{part}] {typ}: … ({tkey})")
        if bkey and tkey != bkey:
            rep.fail(f"제목의 키({tkey})와 브랜치의 키({bkey})가 다름")
        if re.fullmatch(rf"(?i){PROJECT_KEY}[ -]\d+.*", body):
            rep.fail(f"요약이 GitLab 기본 제목(브랜치명)입니다: {body!r} — 무엇을 했는지 한 줄로")
    else:
        part = typ = tkey = None
        hint = "GitLab 기본 제목(브랜치명)을 그대로 두면 키가 `S15p21b202 21`처럼 깨집니다" if \
            re.match(rf"(?i)^(\[\w+\]\s*)?{PROJECT_KEY}[ -]\d+", title) else ""
        rep.fail(f"제목 형식 위반: {title!r} — `[파트] type: 요약 ({PROJECT_KEY}-번호)`. {hint}".strip())
        km = KEY_RE.search(title)
        tkey = km.group(0) if km else None

    # 4) 본문 템플릿
    secs = sections(desc)
    missing = [s for s in REQUIRED_SECTIONS if find_section(secs, s) is None]
    if missing:
        rep.fail(f"본문 템플릿 섹션 누락: {', '.join('## ' + s for s in missing)} — 템플릿 섹션 제목은 지우지 말 것")
    else:
        rep.ok(f"템플릿 섹션 {len(REQUIRED_SECTIONS)}개 존재")
    jira_sec = find_section(secs, "Jira") or ""
    dm = re.search(rf"이슈\s*:\s*`?({PROJECT_KEY}-\d+)`?", jira_sec)
    if not dm:
        rep.fail("본문 `## Jira` 의 `- 이슈: KEY` 줄이 비어 있음 — 이 줄이 없으면 Jira 개발 패널에 MR이 연결되지 않아 머지 후 자동 완료가 안 됨")
    elif bkey and dm.group(1) != bkey:
        rep.fail(f"본문 이슈 키({dm.group(1)})와 브랜치 키({bkey})가 다름")
    else:
        rep.ok(f"본문 이슈 키 {dm.group(1)}")
    check_human_checks(rep, secs)

    # 5) Jira 실존·상태·파트 일치
    key = bkey or tkey
    if not key:
        rep.fail("Jira 키를 브랜치·제목 어디서도 찾지 못함")
    elif not jira.available:
        rep.warn("JIRA_USER/JIRA_API_TOKEN 없음 → Jira 실존 확인 생략 (CI 변수 등록 필요)")
    else:
        lookup_failed = False
        try:
            issue = jira.get_issue(key)
        except Exception as e:   # 네트워크·형식 오류까지 — 장애를 '이슈 없음' 으로 판정하지 않기 위해
            rep.warn(f"Jira 조회 오류로 실존·담당자 검사 생략({type(e).__name__}: {e}) — 토큰·네트워크 확인 필요")
            issue, lookup_failed = None, True
        problem = existence_problem(key, issue, lookup_failed)
        if problem:
            rep.fail(problem)
        elif issue:
            f = issue["fields"]
            status = (f.get("status") or {}).get("name", "")
            itype = (f.get("issuetype") or {}).get("name", "")
            rep.ok(f"Jira {key} 존재 — {itype} · {status} · {f.get('summary', '')!r}")
            if itype in ("에픽", "Epic"):
                rep.fail("Epic 에 직접 MR 을 걸 수 없음 — 하위 Task 로")
            if status in ("완료", "Done"):
                rep.warn("이슈가 이미 완료 상태 — 후속 작업이면 새 이슈를 만들 것 (머지 후 push 금지)")
            if not f.get("assignee"):
                rep.fail("Jira 이슈 담당자 없음 — 담당자 필수")
            else:
                mismatch = assignee_mismatch(mr, issue)
                if mismatch:
                    rep.warn(mismatch)
            if itype not in ("에픽", "Epic") and not f.get("parent"):
                rep.fail("Jira 이슈에 상위 Epic 없음 — 상위 항목 필수")
            # **R11 과 같은 기준으로 뺀다** (S15P21B202-568). 스토리·에픽은 자기 브랜치를
            # 갖지 않고 사람이 손으로 옮기는 유형이라 진행 중 게이트(R11)도 빼 두었다.
            # 두 겹의 대상이 다르면 어느 쪽이 맞는지 아무도 모르게 된다.
            if itype not in POINTLESS_TYPES:
                check_story_points(rep, key, f)
            jpart = part_of_issue(issue)
            if part and jpart and part != jpart:
                rep.fail(f"MR 제목 파트 [{part}] ≠ Jira 이슈 파트 [{jpart}] — MR 파트는 이슈 파트를 따름")
            elif part and jpart:
                rep.ok(f"파트 일치 [{part}]")
            elif part and not jpart:
                rep.warn("Jira 이슈에 파트 태그/레이블이 없음 (needs-fix 상태일 수 있음)")

    # rep.warns 에는 지금까지 남긴 모든 경고(담당자 불일치·완료된 이슈·파트 없음·브랜치 베이스 …)가 들어 있다.
    # 경고를 잡 로그에만 두면 아무도 보지 않으므로 전부 MR 코멘트로 올린다(S15P21B202-105).
    publish_advice(gl, mr, [("warn", w) for w in rep.warns], base_warned)
    return rep.finish()


if __name__ == "__main__":
    raise SystemExit(main())
