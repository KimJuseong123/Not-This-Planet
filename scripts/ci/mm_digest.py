"""mm-digest — 확인이 필요한 것들을 모아 Mattermost 채널로 보낸다 (스케줄 파이프라인).

입력: MM_WEBHOOK_URL(필수), JIRA_USER/JIRA_API_TOKEN(Jira 항목), GITLAB_TOKEN(열린 MR 항목)
항목: needs-fix 이슈 · 기한 지난 미완료 이슈 · 3일간 갱신 없는 진행 중 이슈 · 스프린트인데 담당자 없는 이슈 · 24시간 넘게 열린 MR
      · 검사가 돌지 않은 MR — 파이프라인 없음·오래 대기(S15P21B202-130)
      · 서로 충돌하는 열린 MR 쌍(실제 3-way 머지 시험 — 머지 순서 안내, S15P21B202-138)
      · 최근 7일 안에 머지된 MR(merged_at 기준) 의 브랜치 이슈가 아직 완료가 아닌 것(Jira 자동화 R6 누락 감지, S15P21B202-83)
아무 것도 없으면 보내지 않는다.
"""
import json
import os
from datetime import datetime, timedelta, timezone

from urllib.parse import quote

from ci_common import (PROJECT_KEY, GitLab, Jira, banner, completes_on_merge, env, http,
                       key_from_branch, load_team, role_member)

JIRA_BROWSE = (env("JIRA_URL", "https://ssafy.atlassian.net") or "").rstrip("/") + "/browse/"
TEAM = load_team()
MM_BY_JIRA = {m["jira"]: m["mm"] for m in TEAM["members"] if m.get("jira")}
MM_BY_GITLAB = {m["gitlab"]: m["mm"] for m in TEAM["members"] if m.get("gitlab")}
_LEAD = role_member(TEAM, "lead")
LEAD_MM = (_LEAD or {}).get("mm", "")            # 못 찾으면 멘션 없이 보낸다 — 알림 자체가 사라지는 것보다 낫다
_INFRA = role_member(TEAM, "infra") or _LEAD
INFRA_MM = (_INFRA or {}).get("mm", "")          # 조회 실패 = Infra 가 볼 일


def who(assignee: dict | None) -> str:
    """담당자 → @멘션(매핑 있으면), 없으면 표시 이름, 미지정이면 팀장 멘션."""
    if not assignee:
        return f"@{LEAD_MM}(담당자 없음)"
    mm = MM_BY_JIRA.get(assignee.get("accountId", ""))
    return f"@{mm}" if mm else (assignee.get("displayName") or "?")


def fmt_issues(issues: list[dict]) -> str:
    """| 이슈 | 담당 | 표 한 덩어리 (MM Markdown)."""
    rows = "\n".join(f"| [{i['key']}]({JIRA_BROWSE}{i['key']}) {i['fields'].get('summary', '')} | {who(i['fields'].get('assignee'))} |"
                     for i in issues)
    return "| 이슈 | 담당 |\n|---|---|\n" + rows


KEYS_PER_QUERY = 50      # 이보다 많으면 키 조회를 포기하고 전량 조회로 간다


def not_done_issues(jira: Jira, keys: list[str]) -> tuple[list[dict], bool]:
    """완료가 아닌 이슈를 모은다 — 먼저 **키로 직접** 묻고, 안 되면 전량 조회로 폴백한다.

    전량 조회(statusCategory != Done)는 머지 MR 이 몇 건이든 프로젝트의 미완료 이슈를 전부 받아오므로
    스프린트가 쌓이면 페이지 상한에 걸려 누락되고 호출도 무겁다(-134).
    그렇다고 키 조회만 쓸 수도 없다: 브랜치 이름에 오타 키나 삭제된 테스트 이슈 키가 섞이면
    Jira 가 JQL 자체를 거부한다(그래서 원래 전량 조회를 골랐다). 그래서 **시도 후 폴백** 이다."""
    fields = ["summary", "assignee", "status"]
    if len(keys) <= KEYS_PER_QUERY:
        key_list = ", ".join(sorted(keys))
        try:
            return jira.search_all(f"project = {PROJECT_KEY} AND key in ({key_list}) "
                                   f"AND statusCategory != Done AND issuetype != Epic ORDER BY key", fields)
        except Exception as e:
            print(f"키 조회 실패({type(e).__name__}) → 미완료 이슈 전량 조회로 폴백: {str(e)[:120]}")
    return jira.search_all(f"project = {PROJECT_KEY} AND statusCategory != Done AND issuetype != Epic ORDER BY key", fields)


def landed_on_develop(gl: GitLab, target: str, depth: int = 5) -> bool | None:
    """스택 자식의 코드가 결국 develop 에 도달했는가. 부모 MR 을 따라 올라간다.

    - `True`  : 어느 단계에서 develop·master 로 머지됐다 → 자식 이슈는 **지금 완료로 옮길 수 있다**
    - `False` : 부모가 아직 머지되지 않았거나, 실험용·폐기 브랜치로 들어가 develop 에 없다 → **보고하지 않는다**
                (2026-08-29 오탐 !82 가 이 경우였다: -138 코드는 develop 에 없었다)
    - `None`  : 조회 실패 → 판단 불가. 일을 숨기는 쪽이 더 나쁘므로 표시하고 물음표를 붙인다
    """
    seen: set[str] = set()
    while target and depth > 0:
        if completes_on_merge(target):
            return True
        if target in seen:            # 있을 수 없는 순환 — 무한 루프만 막는다
            return False
        seen.add(target)
        mr, ok = gl.merged_mr_for_source(target)
        if not ok:
            return None
        if mr is None:                # 부모가 아직 머지 안 됨 / 폐기된 브랜치
            return False
        target = mr.get("target_branch") or ""
        depth -= 1
    return False


def merged_not_done(gl: GitLab, jira: Jira, days: int = 7, now=None) -> tuple[list[str], list[str], bool]:
    """최근 N일 안에 머지됐는데 Jira 상태가 완료가 아닌 이슈 → 표 행. **두 갈래로 나눠** 돌려준다.

    - `rows`  : 대상이 develop·master 인 MR — R6 가 걸려야 했는데 안 걸린 것. **이상 상황**
    - `stacks`: 대상이 다른 이슈 브랜치인 스택 자식 MR — R6 가 원래 걸리지 않는다. **정상이지만 손이 필요**

    나누지 않으면 스택 자식이 "자동 완료가 빠졌다" 로 보고돼 매일 오탐이 된다
    (2026-08-31 실측: !82 가 그렇게 잡혔다 — S15P21B202-159).
    자식 이슈를 CI 가 자동 완료하던 장치(-199)는 없앴다 — 순서를 CI 가 막게 됐고(-246), Jira 상태는
    Jira 자동화만 쓴다(-242). 그래서 이 표는 **사람이 옮겨야 하는 목록**이다.

    키로 직접 조회하고(가벼움), 존재하지 않는 키(오타·삭제된 테스트 이슈)가 섞여 JQL 이 거부되면 미완료 이슈 전량 조회로 폴백해 로컬에서 교차한다(-134)."""
    by_key: dict[str, dict] = {}
    mrs, mrs_complete = gl.merged_mrs(days, now=now)
    for mr in mrs:
        k = key_from_branch(mr.get("source_branch") or "")
        if k and k not in by_key:
            by_key[k] = mr
    if not by_key:
        # 반드시 3개를 돌려준다. 예전엔 2개였고, 7일 안에 이슈 키가 붙은 머지가 없으면 main() 이
        # ValueError 를 잡아 "조회 실패 → @Infra" 오탐 카드를 냈다(-281). 매일 머지가 있어 숨어 있던 경로다.
        return [], [], mrs_complete
    issues, complete = not_done_issues(jira, list(by_key))
    rows, stacks = [], []
    for i in issues:
        mr = by_key.get(i["key"])
        if not mr:
            continue
        target = mr.get("target_branch") or ""
        head = (f"| [{i['key']}]({JIRA_BROWSE}{i['key']}) {i['fields'].get('summary', '')} "
                f"| {i['fields'].get('status', {}).get('name', '')} | [!{mr['iid']}]({mr['web_url']}) ")
        if completes_on_merge(target):
            rows.append(head + f"| {who(i['fields'].get('assignee'))} |")
            continue
        landed = landed_on_develop(gl, target)
        if landed is False:      # 부모가 아직 develop 에 없다 → 지금은 할 일이 아니다
            continue
        mark = "" if landed else " ⁇"   # None = 조회 실패. 숨기지 않고 물음표로 남긴다
        stacks.append(head + f"| `{target}`{mark} | {who(i['fields'].get('assignee'))} |")
    return rows, stacks, complete and mrs_complete


def _hdrs(gl: GitLab) -> dict:
    """S15P21B202-109 의 공개 헬퍼가 머지되기 전후 모두 동작하도록."""
    return gl.headers() if hasattr(gl, "headers") else gl._h()


def resurrected_branches(gl: GitLab, days: int = 7, now=None) -> list[str]:
    """최근 N일 안에 머지된 MR 의 소스 브랜치가 **다시 존재**하면 표 행으로. 머지 후 같은 브랜치에 push 한 경우를 잡는다
    (그렇게 push 하면 MR·파이프라인이 없어 조용히 유실된다 — S15P21B202-110)."""
    mrs, _ = gl.merged_mrs(days, now=now)
    rows = []
    for mr in mrs:
        br = mr.get("source_branch") or ""
        if not br or not mr.get("should_remove_source_branch", True):
            continue
        st, d = http("GET", f"{gl.api}/projects/{gl.project}/repository/branches/{quote(br, safe='')}", _hdrs(gl))
        if st != 200 or not isinstance(d, dict):
            continue
        author = ((d.get("commit") or {}).get("author_name")) or (mr.get("author") or {}).get("username", "?")
        mention = next((f"@{m['mm']}" for m in TEAM["members"] if m["name"] == author or m["gitlab"] == author), author)
        rows.append(f"| `{br}` | [!{mr['iid']}]({mr['web_url']}) | {mention} |")
    return rows


def pending_hours(raw: str | None = None) -> float:
    """대기 경보 임계값(시간). CI 변수에 숫자가 아닌 값이 들어와도 아침 점검 전체가 죽지 않게 기본값으로 돌아간다."""
    try:
        return float(raw if raw is not None else env("DIGEST_PENDING_HOURS", "2") or 2)
    except (TypeError, ValueError):
        print("[warn] DIGEST_PENDING_HOURS 값이 숫자가 아님 → 기본 2시간 사용")
        return 2.0


def unchecked_mrs(gl: GitLab, now: datetime) -> list[str]:
    """검사가 한 번도 돌지 않았거나 계속 대기 중인 열린 MR → 표 행.

    빨간불이 없는 MR 은 통과한 것처럼 보이지만, 다음 두 경우는 아무 검사도 받지 않은 것이다.
      · 파이프라인 없음 — 소스 브랜치에 .gitlab-ci.yml 이 없다(master 기반 브랜치. 2026-08-28 MR !74 실측:
        훅도 파이프라인도 알림도 없이 MR 이 열렸다)
      · 계속 대기 — 러너가 꺼져 있다(2026-08-28 F1 실측). 이 상태로도 머지는 막히지 않는다
    조회 실패(None)는 경보로 만들지 않는다 — 없는 문제를 매일 띄우는 쪽이 더 나쁘다."""
    rows = []
    for mr in gl.open_mrs():
        pipes = gl.mr_pipelines(mr["iid"])
        if pipes is None:
            continue
        author = (mr.get("author") or {}).get("username", "")
        mention = f"@{MM_BY_GITLAB.get(author, author)}"
        link = f"[!{mr['iid']}]({mr['web_url']}) {mr['title']}"
        if not pipes:
            rows.append(f"| {link} | 파이프라인 없음 — 브랜치에 CI 설정이 없다(master 기반?) | {mention} |")
            continue
        latest = max(pipes, key=lambda x: x.get("id", 0))   # 목록 순서에 기대지 않는다
        if latest.get("status") in ("pending", "created", "waiting_for_resource"):
            created = datetime.fromisoformat(latest["created_at"].replace("Z", "+00:00"))
            hours = (now - created).total_seconds() / 3600
            if hours >= pending_hours():
                rows.append(f"| {link} | {hours:.0f}시간째 대기 — 러너 확인 | {mention} @{INFRA_MM} |")
    return rows


# ── 배포 상태 (S15P21B202-144) ────────────────────────────────
DEPLOY_JOB = "deploy-backend"
DEPLOY_SCAN_PIPELINES = 20      # develop 파이프라인 몇 건까지 거슬러 볼지


def kst(iso: str | None) -> str:
    """GitLab 이 주는 UTC ISO 문자열을 한국 시간 표시로. 팀이 읽는 시각이 KST 다."""
    if not iso:
        return "?"
    try:
        t = datetime.fromisoformat(iso.replace("Z", "+00:00")).astimezone(timezone(timedelta(hours=9)))
        return t.strftime("%m/%d %H:%M")
    except ValueError:
        return "?"


def deploy_jobs(gl: GitLab, ref: str = "develop", max_pipelines: int = DEPLOY_SCAN_PIPELINES) -> tuple[list[dict], bool]:
    """develop 파이프라인을 최신부터 훑어 배포 잡을 모은다(최신 우선). 반환 (잡 목록, 전량 확인 여부).

    프로젝트 전체 잡 목록으로 찾지 않는 이유는 그 API 가 **이름으로 걸러 주지 않아서**다
    (ci_common.pipeline_jobs 주석 참고). 배포 잡은 `changes: backend/**` 일 때만 만들어지므로
    파이프라인 20건을 봐도 배포 잡은 몇 건뿐이다."""
    pipes = gl.branch_pipelines(ref, per_page=max_pipelines)
    found: list[dict] = []
    for p in pipes:
        same = [j for j in gl.pipeline_jobs(p.get("id")) if j.get("name") == DEPLOY_JOB]
        if same:
            found.append(max(same, key=lambda j: j.get("id") or 0))   # 재시도가 있으면 마지막 것
    # 파이프라인이 상한보다 적게 왔으면 그것이 전부다 = 배포 이력을 다 봤다
    return found, len(pipes) < max_pipelines


def deploy_state(gl: GitLab, ref: str = "develop") -> dict:
    """{'live': 성공 잡|None, 'attempt': 마지막 시도 잡|None, 'pending': 눌리지 않은 배포 수, 'complete': bool}

    `live` 와 `attempt` 를 따로 두는 이유 — 마지막 시도가 실패했어도 **서버는 그 전 성공분으로 돌고 있다.**
    둘을 섞으면 실패한 커밋이 배포된 것처럼 읽힌다."""
    jobs, complete = deploy_jobs(gl, ref)
    live = next((j for j in jobs if j.get("status") == "success"), None)
    attempt = next((j for j in jobs if j.get("status") != "manual"), None)
    pending = 0
    for j in jobs:                      # 마지막 시도보다 새로운 '눌리지 않은' 배포 = 배포 대기
        if j.get("status") != "manual":
            break
        pending += 1
    return {"live": live, "attempt": attempt, "pending": pending, "complete": complete}


def deploy_line(st: dict) -> str:
    """아침 점검 꼬리에 붙는 한 줄. 조치할 것이 없어도 나온다 — '지금 뭐가 올라가 있나' 는 항상 궁금하다."""
    live, pending = st["live"], st["pending"]
    if not live:
        head = "🚀 배포: **CI 로 올린 이력 없음**" + ("" if st["complete"] else f" (최근 파이프라인 {DEPLOY_SCAN_PIPELINES}건 안에서)")
    else:
        sha = (live.get("commit") or {}).get("short_id") or (live.get("commit") or {}).get("id", "")[:8] or "?"
        head = f"🚀 배포: `{sha}` ({kst(live.get('finished_at'))})"
    if pending:
        head += f" · 백엔드 변경 **{pending}건 배포 대기**(수동 잡을 눌러야 올라갑니다)"
    return head


def main() -> int:
    banner("mm-digest")
    url = env("MM_WEBHOOK_URL")
    if not url:
        print("MM_WEBHOOK_URL 없음 → 생략"); return 0
    jira, gl = Jira(), GitLab()
    blocks = []
    if jira.available:
        base = f"project = {PROJECT_KEY} AND statusCategory != Done"
        fields = ["summary", "assignee", "status"]
        q = [
            ("🏷 `needs-fix` — 제목 형식/상위 Epic 확인", f"{base} AND labels = needs-fix ORDER BY updated DESC"),
            ("⏰ 기한 지남", f"{base} AND duedate < startOfDay() ORDER BY duedate ASC"),
            ("🐢 3일 이상 갱신 없는 진행 중", f"{base} AND status = \"In Progress\" AND updated <= -3d ORDER BY updated ASC"),
            ("👤 스프린트에 있는데 담당자 없음", f"{base} AND sprint in openSprints() AND assignee is EMPTY AND issuetype != Epic"),
        ]
        for title, jql in q:
            try:
                issues = jira.search(jql, fields)
            except Exception as e:  # 조회 실패도 조치할 사람(Infra)을 붙여 보낸다 — 매일 조용히 빠지는 것보다 낫다
                blocks.append(f"**{title}** — 조회 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
                continue
            if issues:
                blocks.append(f"**{title}** ({len(issues)})\n\n{fmt_issues(issues)}")   # 제목-표 사이 빈 줄 필수
    else:
        blocks.append("_(JIRA_USER/JIRA_API_TOKEN 없음 — Jira 항목 생략)_")
    if gl.token and gl.project:
        now = datetime.now(timezone.utc)
        old = []
        for mr in gl.open_mrs():
            created = datetime.fromisoformat(mr["created_at"].replace("Z", "+00:00"))
            age_h = (now - created).total_seconds() / 3600
            if age_h >= 24:
                revs = [r["username"] for r in mr.get("reviewers", [])] or [TEAM["roles"]["lead"]]
                rev = " ".join(f"@{MM_BY_GITLAB.get(r, r)}" for r in revs)
                old.append(f"| [!{mr['iid']}]({mr['web_url']}) {mr['title']}{' (Draft)' if mr.get('draft') else ''} | {age_h/24:.1f}일 | {rev} |")
        if old:
            blocks.append(f"**🔀 24시간 넘게 열린 MR** ({len(old)})\n\n| MR | 경과 | 리뷰어 |\n|---|---|---|\n" + "\n".join(old))
        try:
            unchecked = unchecked_mrs(gl, now)
        except Exception as e:   # 이 항목이 실패해도 나머지 항목은 보낸다
            unchecked = []
            blocks.append(f"**🚫 검사가 돌지 않은 MR** — 조회 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
        if unchecked:
            blocks.append(f"**🚫 검사가 돌지 않은 MR** ({len(unchecked)}) — 빨간불이 없다고 통과한 것이 아니다\n\n"
                          f"| MR | 왜 | 확인 |\n|---|---|---|\n" + "\n".join(unchecked))
        try:
            from mr_overlap import conflict_pairs
            pairs = conflict_pairs(gl)
        except Exception as e:   # 이 항목이 실패해도 나머지는 보낸다
            pairs = []
            blocks.append(f"**\u2694 서로 충돌하는 열린 MR** — 계산 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
        if pairs:
            rows_p = []
            for pr in pairs:
                a, b, c = pr["first"], pr["second"], pr["cmp"]
                kind = "⛔ 텍스트 충돌" if c["kind"] == "conflict" else "⚠ 같은 씬·프리팹"
                files = " · ".join(f"`{x}`" for x in (c["conflicts"] or c["risky"])[:4])
                who = f"@{MM_BY_GITLAB.get((b.get('author') or {}).get('username', ''), (b.get('author') or {}).get('username', '?'))}"
                rows_p.append(f"| [!{a['iid']}]({a['web_url']}) → [!{b['iid']}]({b['web_url']}) | {kind} | {files} | {who} |")
            blocks.append(f"**\u2694 서로 충돌하는 열린 MR** ({len(pairs)}) — 앞의 것을 먼저 머지하고, 뒤의 것은 그 위에 얹은 뒤 머지\n\n"
                          f"| 머지 순서 | 종류 | 파일 | 뒤 MR 작성자 |\n|---|---|---|---|\n" + "\n".join(rows_p))
        if jira.available:
            try:
                rows, stacks, complete = merged_not_done(gl, jira)
            except Exception as e:  # 이 항목이 실패해도 다른 항목은 보낸다
                rows, stacks, complete = [], [], True; blocks.append(f"**🔗 머지됐는데 완료 안 된 이슈** — 조회 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
            if not complete:
                print("경고: 머지 MR 또는 미완료 이슈 수집이 페이지 상한에서 끊김 — 일부 누락 가능")
            try:
                res = resurrected_branches(gl)
            except Exception as e:  # noqa: BLE001
                res = []; blocks.append(f"**🌿 머지 뒤 다시 생긴 브랜치** — 조회 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
            if res:
                blocks.append(f"**🌿 머지 뒤 다시 생긴 브랜치** ({len(res)}) — 머지된 브랜치에 다시 push 하면 MR·파이프라인 없이 조용히 남습니다. "
                              f"후속 작업은 새 이슈·새 브랜치로, 남은 브랜치는 삭제하세요\n\n"
                              f"| 브랜치 | 머지된 MR | 마지막 커밋 |\n|---|---|---|\n" + "\n".join(res))
            note = f"\n_⚠ 목록이 페이지 상한에서 끊겨 일부가 빠졌을 수 있음 — @{INFRA_MM} 확인_" if not complete else ""
            if rows:   # 매칭이 없으면 블록 없음(잘림 경고도 각주로만) — "항목 없으면 보내지 않는다" 유지
                blocks.append(f"**🔗 머지됐는데 완료 안 된 이슈** ({len(rows)}) — develop 으로 머지됐으니 자동 완료(R6)가 걸려야 했다. 담당자가 완료로 옮기면 허용됨(머지된 MR 있음)\n\n"
                              f"| 이슈 | 상태 | MR | 담당 |\n|---|---|---|---|\n" + "\n".join(rows) + note)
            if stacks:  # 스택 자식 — R6 가 원래 걸리지 않는다(오탐이 아니라 별 항목, S15P21B202-159)
                blocks.append(f"**🧩 스택 자식 MR — 완료로 옮기면 됩니다** ({len(stacks)}) — "
                              f"대상이 develop 이 아니라 다른 이슈 브랜치여서 자동 완료(R6)가 걸리지 않습니다. "
                              f"**부모가 이미 develop 에 들어갔으니** 담당자가 완료로 옮기면 됩니다(`⁇` 는 확인 실패 — 눈으로 보세요)\n\n"
                              f"| 이슈 | 상태 | MR | 머지 대상 | 담당 |\n|---|---|---|---|---|\n" + "\n".join(stacks) + (note if not rows else ""))
    dep = {"live": None, "attempt": None, "pending": 0, "complete": True}
    if gl.token and gl.project:
        try:
            dep = deploy_state(gl)
        except Exception as e:  # noqa: BLE001 — 배포 조회 실패가 점검 전체를 막지 않는다
            blocks.append(f"**🚀 배포 상태** — 조회 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
        att = dep["attempt"]
        if att and att.get("status") in ("failed", "canceled"):
            # 마지막 시도가 깨진 채로 남아 있다 = 아무도 뒤처리를 안 했다. 이건 조치 항목이다.
            live = dep["live"]
            now_on = f"`{(live.get('commit') or {}).get('short_id', '?')}` ({kst(live.get('finished_at'))})" if live else "**확인 필요**"
            blocks.append(f"**🚀 마지막 배포 시도가 {'취소' if att.get('status') == 'canceled' else '실패'}로 남아 있습니다** → @{INFRA_MM}\n\n"
                          f"| 항목 | 내용 |\n|---|---|\n"
                          f"| 시도 | [잡 #{att.get('id')}]({att.get('web_url', '')}) · {kst(att.get('finished_at'))} |\n"
                          f"| 지금 서버 | {now_on} |\n"
                          f"| 볼 곳 | 잡 로그 · 호스트 `~/metagame/deploy/deploy.log` |")
    if not blocks:
        print("보낼 항목 없음"); return 0
    tail = deploy_line(dep) if (gl.token and gl.project) else ""
    text = f"📋 **아침 점검 — 확인 필요** ({datetime.now().strftime('%m/%d %H:%M')})\n" + "\n\n".join(blocks) + \
           (f"\n\n{tail}" if tail else "") + \
           f"\n\n_각 항목의 @담당자가 처리 · 규칙: docs/leader-checklist.md · 자동 생성(mm-digest)_"
    st, d = http("POST", url, body={"text": text, "username": "METAGAME 봇"})
    print(text)
    print(f"\nMM 전송 → {st}")
    return 0 if st == 200 else 1


if __name__ == "__main__":
    raise SystemExit(main())
