"""mm-digest — 확인이 필요한 것들을 모아 Mattermost 채널로 보낸다 (스케줄 파이프라인).

입력: MM_WEBHOOK_URL(필수), JIRA_USER/JIRA_API_TOKEN(Jira 항목), GITLAB_TOKEN(열린 MR 항목)
항목: needs-fix 이슈 · 기한 지난 미완료 이슈 · 3일간 갱신 없는 진행 중 이슈 · 스프린트인데 담당자 없는 이슈 · 24시간 넘게 열린 MR
      · 최근 7일 안에 머지된 MR(merged_at 기준) 의 브랜치 이슈가 아직 완료가 아닌 것(Jira 자동화 R6 누락 감지, S15P21B202-83)
아무 것도 없으면 보내지 않는다.
"""
import json
import os
from datetime import datetime, timezone

from ci_common import PROJECT_KEY, GitLab, Jira, banner, env, http, key_from_branch

JIRA_BROWSE = (env("JIRA_URL", "https://ssafy.atlassian.net") or "").rstrip("/") + "/browse/"
TEAM = json.load(open(os.path.join(os.path.dirname(__file__), "team.json"), encoding="utf-8"))
MM_BY_JIRA = {m["jira"]: m["mm"] for m in TEAM["members"]}
MM_BY_GITLAB = {m["gitlab"]: m["mm"] for m in TEAM["members"]}
LEAD_MM = next(m["mm"] for m in TEAM["members"] if m["gitlab"] == TEAM["roles"]["lead"])
INFRA_MM = next((m["mm"] for m in TEAM["members"] if m["gitlab"] == TEAM["roles"].get("infra")), LEAD_MM)   # 조회 실패 = Infra 가 볼 일


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


def merged_not_done(gl: GitLab, jira: Jira, days: int = 7) -> tuple[list[str], bool]:
    """최근 N일 안에 머지된 MR 의 소스 브랜치 키 중 Jira 상태가 완료가 아닌 이슈 → 표 행. 자동 완료(R6)가 빠진 경우를 잡는다.
    존재하지 않는 키(오타·삭제된 테스트 이슈)가 브랜치에 섞여도 깨지지 않도록, 키 목록 JQL 대신 미완료 이슈 전체를 받아 로컬에서 교차한다."""
    by_key: dict[str, dict] = {}
    mrs, mrs_complete = gl.merged_mrs(days)
    for mr in mrs:
        k = key_from_branch(mr.get("source_branch") or "")
        if k and k not in by_key:
            by_key[k] = mr
    if not by_key:
        return [], mrs_complete
    issues, complete = jira.search_all(f"project = {PROJECT_KEY} AND statusCategory != Done AND issuetype != Epic ORDER BY key",
                                       ["summary", "assignee", "status"])
    rows = []
    for i in issues:
        mr = by_key.get(i["key"])
        if not mr:
            continue
        rows.append(f"| [{i['key']}]({JIRA_BROWSE}{i['key']}) {i['fields'].get('summary', '')} | {i['fields'].get('status', {}).get('name', '')} "
                    f"| [!{mr['iid']}]({mr['web_url']}) | {who(i['fields'].get('assignee'))} |")
    return rows, complete and mrs_complete


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
        if jira.available:
            try:
                rows, complete = merged_not_done(gl, jira)
            except Exception as e:  # 이 항목이 실패해도 다른 항목은 보낸다
                rows, complete = [], True; blocks.append(f"**🔗 머지됐는데 완료 안 된 이슈** — 조회 실패 → @{INFRA_MM} 확인: {type(e).__name__}: {e}")
            if not complete:
                print("경고: 머지 MR 또는 미완료 이슈 수집이 페이지 상한에서 끊김 — 일부 누락 가능")
            if rows:   # 매칭이 없으면 블록 없음(잘림 경고도 각주로만) — "항목 없으면 보내지 않는다" 유지
                note = f"\n_⚠ 목록이 페이지 상한에서 끊겨 일부가 빠졌을 수 있음 — @{INFRA_MM} 확인_" if not complete else ""
                blocks.append(f"**🔗 머지됐는데 완료 안 된 이슈** ({len(rows)}) — 자동 완료(R6)가 빠진 것. 담당자가 완료로 옮기면 허용됨(머지된 MR 있음)\n\n"
                              f"| 이슈 | 상태 | MR | 담당 |\n|---|---|---|---|\n" + "\n".join(rows) + note)
    if not blocks:
        print("보낼 항목 없음"); return 0
    text = f"📋 **아침 점검 — 확인 필요** ({datetime.now().strftime('%m/%d %H:%M')})\n" + "\n\n".join(blocks) + \
           f"\n\n_각 항목의 @담당자가 처리 · 규칙: docs/leader-checklist.md · 자동 생성(mm-digest)_"
    st, d = http("POST", url, body={"text": text, "username": "METAGAME 봇"})
    print(text)
    print(f"\nMM 전송 → {st}")
    return 0 if st == 200 else 1


if __name__ == "__main__":
    raise SystemExit(main())
