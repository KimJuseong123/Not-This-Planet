"""mm_notify — MR 파이프라인에서 Mattermost 로 보내는 '담당자 태그가 있는' 알림 2종.

  python scripts/ci/mm_notify.py mr-opened   # 🟢 리뷰 요청 — 이 MR 의 첫 파이프라인일 때만 1회, 팀장 @멘션(+작성자 표기)
  python scripts/ci/mm_notify.py ci-failed   # 🔴 CI 실패 — 실패한 잡 이름·링크, MR 작성자 @멘션

원칙(docs/ci-and-notifications.md §5): 태그할 사람이 없는 메시지는 보내지 않는다. 첫 줄 = 등급 이모지 + 굵은 제목 + 링크,
둘째 줄 = @멘션 + 조치. 사용자 매핑은 scripts/ci/team.json. 필요 변수: MM_WEBHOOK_URL, GITLAB_TOKEN(파이프라인·잡 조회).
"""
import json
import os
import sys

from ci_common import GitLab, banner, env, http, strip_draft

TEAM = json.load(open(os.path.join(os.path.dirname(__file__), "team.json"), encoding="utf-8"))
BY_GITLAB = {m["gitlab"]: m for m in TEAM["members"]}
LEAD = TEAM["roles"]["lead"]
INFRA = TEAM["roles"]["infra"]


def mm_of(gitlab_username: str | None) -> str | None:
    m = BY_GITLAB.get(gitlab_username or "")
    return f"@{m['mm']}" if m else None


def send(text: str) -> int:
    url = env("MM_WEBHOOK_URL")
    if not url:
        print("MM_WEBHOOK_URL 없음 → 전송 생략"); return 0
    st, _ = http("POST", url, body={"text": text, "username": "METAGAME 봇"})
    print(text); print(f"MM 전송 → {st}")
    return 0 if st == 200 else 1


def mr_opened(gl: GitLab) -> int:
    mr = gl.get_mr()
    if not gl.token:
        print("GITLAB_TOKEN 없음 → 첫 파이프라인 판별 불가, 생략"); return 0
    st, pls = http("GET", gl._mr_url("/pipelines?per_page=5"), gl._h())
    if st != 200 or not isinstance(pls, list):
        print(f"파이프라인 목록 조회 실패 {st} → 생략"); return 0
    if len(pls) != 1:
        print(f"이 MR 의 파이프라인 {len(pls)}개 → 첫 파이프라인이 아니므로 리뷰 요청 알림 생략"); return 0
    title, is_draft = strip_draft(mr.get("title") or "")
    if is_draft or mr.get("draft"):
        print("Draft MR → 리뷰 요청 알림 생략 (Draft 는 컨벤션 위반, mr-lint 가 실패 처리)"); return 0
    author = (mr.get("author") or {}).get("username", "")
    reviewers = [r.get("username") for r in (mr.get("reviewers") or [])] or [LEAD]
    mentions = " ".join(x for x in (mm_of(r) for r in reviewers) if x) or f"@{BY_GITLAB[LEAD]['mm']}"
    author_txt = mm_of(author) or author or "?"
    text = (f"🟢 **리뷰 요청** — [!{gl.iid} {title}]({mr.get('web_url')})\n"
            f"{mentions} · 작성 {author_txt.lstrip('@')} · 파이프라인 결과는 MR 상단에서, 리뷰 기준은 MR 본문 하단 '리뷰어 확인'")
    return send(text)


def ci_failed(gl: GitLab) -> int:
    mr = gl.get_mr()
    author = (mr.get("author") or {}).get("username", "")
    mention = mm_of(author)
    if not mention:
        print(f"작성자 {author!r} 매핑 없음 → 인프라에게"); mention = f"@{BY_GITLAB[INFRA]['mm']}"
    failed = []
    if gl.token and env("CI_PIPELINE_ID"):
        st, jobs = http("GET", f"{gl.api}/projects/{gl.project}/pipelines/{env('CI_PIPELINE_ID')}/jobs?per_page=50", gl._h())
        if st == 200 and isinstance(jobs, list):
            failed = [j for j in jobs if j.get("status") == "failed" and not j.get("allow_failure")]
    title, _ = strip_draft(mr.get("title") or "")
    if failed:
        parts = " · ".join(f"[{j['name']}]({j['web_url']})" for j in failed)
        hint = {"mr-lint": "제목/본문/Jira 규칙", "repo-hygiene": "금지 파일·.meta·LFS·씬 선언", "unity-compile": "컴파일 오류"}
        hints = ", ".join(sorted({hint.get(j["name"], j["name"]) for j in failed}))
    else:
        parts, hints = f"[파이프라인]({env('CI_PIPELINE_URL', '')})", "잡 로그 확인"
    text = (f"🔴 **CI 실패** — [!{gl.iid} {title}]({mr.get('web_url')})\n"
            f"{mention} · 실패: {parts} · 확인할 것: {hints} · 고친 뒤 push 하면 자동 재검사")
    return send(text)


def main() -> int:
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    banner(f"mm-notify {mode}")
    gl = GitLab()
    if not gl.iid:
        print("MR 파이프라인이 아님 → 생략"); return 0
    if mode == "mr-opened":
        return mr_opened(gl)
    if mode == "ci-failed":
        return ci_failed(gl)
    print("사용법: mm_notify.py mr-opened | ci-failed"); return 2


if __name__ == "__main__":
    raise SystemExit(main())
