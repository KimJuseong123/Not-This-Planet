"""mm-digest — 확인이 필요한 것들을 모아 Mattermost 채널로 보낸다 (스케줄 파이프라인).

입력: MM_WEBHOOK_URL(필수), JIRA_USER/JIRA_API_TOKEN(Jira 항목), GITLAB_TOKEN(열린 MR 항목)
항목: needs-fix 이슈 · 기한 지난 미완료 이슈 · 3일간 갱신 없는 진행 중 이슈 · 스프린트인데 담당자 없는 이슈 · 24시간 넘게 열린 MR
아무 것도 없으면 보내지 않는다.
"""
import json
import os
from datetime import datetime, timezone

from ci_common import PROJECT_KEY, GitLab, Jira, banner, env, http

JIRA_BROWSE = (env("JIRA_URL", "https://ssafy.atlassian.net") or "").rstrip("/") + "/browse/"
TEAM = json.load(open(os.path.join(os.path.dirname(__file__), "team.json"), encoding="utf-8"))
MM_BY_JIRA = {m["jira"]: m["mm"] for m in TEAM["members"]}
MM_BY_GITLAB = {m["gitlab"]: m["mm"] for m in TEAM["members"]}
LEAD_MM = next(m["mm"] for m in TEAM["members"] if m["gitlab"] == TEAM["roles"]["lead"])


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
            except RuntimeError as e:
                blocks.append(f"**{title}** — 조회 실패: {e}")
                continue
            if issues:
                blocks.append(f"**{title}** ({len(issues)})\n{fmt_issues(issues)}")
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
            blocks.append(f"**🔀 24시간 넘게 열린 MR** ({len(old)})\n| MR | 경과 | 리뷰어 |\n|---|---|---|\n" + "\n".join(old))
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
