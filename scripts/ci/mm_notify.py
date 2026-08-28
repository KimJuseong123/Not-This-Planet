"""mm_notify — MR 파이프라인에서 Mattermost 로 보내는 '담당자 태그가 있는' 알림 2종.

  python scripts/ci/mm_notify.py mr-opened   # 🟢 리뷰 요청 — 이 MR 의 첫 파이프라인일 때만 1회, 팀장 @멘션(+작성자 표기)
  python scripts/ci/mm_notify.py ci-failed   # 🔴 CI 실패 — 실패한 잡 이름·링크, **실패 이유(❌ 줄)**, MR 작성자 @멘션 + 같은 내용을 MR 코멘트로
  python scripts/ci/mm_notify.py ci-resolved # 검사 통과 시, 이전 실패 코멘트가 있으면 "해소됨" 으로 갱신(없으면 아무 것도 하지 않음)

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


def card(title: str, rows: list[tuple[str, str]]) -> str:
    """모든 봇 메시지의 공통 카드: 등급 이모지 제목 한 줄 + | 항목 | 내용 | 표 (MM Markdown)."""
    body = "| 항목 | 내용 |\n|---|---|\n" + "\n".join(f"| {k} | {v} |" for k, v in rows)
    return f"{title}\n\n{body}"   # 제목과 표 사이 빈 줄 필수 — 없으면 MM 이 표를 문단으로 붙여 raw 로 보임(2026-08-27 실측)


def send(text: str) -> int:
    url = env("MM_WEBHOOK_URL")
    if not url:
        print("MM_WEBHOOK_URL 없음 → 전송 생략(아래는 보냈을 문구)"); print(text); return 0
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
    return send(card("🟢 **리뷰 요청**", [
        ("MR", f"[!{gl.iid} {title}]({mr.get('web_url')})"),
        ("리뷰어", mentions),
        ("작성", author_txt.lstrip("@")),
        ("조치", "파이프라인 결과 확인 후 MR 본문 하단 '리뷰어 확인' 5항목 기준으로 리뷰·머지"),
    ]))


FAIL_MARKER = "<!-- ci-failed:metagame -->"
MAX_REASONS = 3          # 카드에 싣는 이유 개수
MAX_COMMENT_REASONS = 20  # MR 코멘트 상한 — 위반이 수십 건이어도 코멘트가 비대해지지 않게
MAX_REASON_CHARS = 200


def clean_reason(text: str) -> str:
    """로그 한 줄을 표·코멘트에 넣어도 안전하게 — 표 구분자와 태그를 지우고 길이를 자른다."""
    t = " ".join(text.split()).replace("|", "／").replace("<", "&lt;").replace(">", "&gt;")
    return t[:MAX_REASON_CHARS] + ("…" if len(t) > MAX_REASON_CHARS else "")


def failure_reasons(gl: GitLab, jobs: list[dict]) -> list[tuple[str, str]]:
    """실패한 잡의 로그에서 ❌ 줄을 뽑아 [(잡 이름, 이유)] 로. 로그를 못 읽어도 알림은 나가야 하므로 조용히 건너뛴다.
    사람이 로그를 찾아 들어가지 않아도 되게 하는 것이 목적(S15P21B202-102)."""
    out: list[tuple[str, str]] = []
    for j in jobs:
        name = j.get("name", "?")
        try:
            st, trace = http("GET", f"{gl.api}/projects/{gl.project}/jobs/{j.get('id')}/trace", gl.headers())
        except Exception as e:  # noqa: BLE001
            print(f"  [{name}] 로그 조회 실패(무시): {type(e).__name__}: {e}"); continue
        # 잡 로그는 JSON 이 아니라 text/plain 이며, 공용 http() 는 파싱 실패 시 원문 문자열을 돌려준다(ci_common.http)
        if st != 200 or not isinstance(trace, str):
            continue
        for line in trace.splitlines():
            if "❌" in line:
                reason = clean_reason(line.split("❌", 1)[1])
                if reason and (name, reason) not in out:
                    out.append((name, reason))
    return out


def upsert_mr_comment(gl: GitLab, body: str) -> None:
    """같은 내용을 MR 코멘트로도 남긴다(마커로 갱신 → 재실행해도 쌓이지 않음).
    실패해도 예외를 밖으로 던지지 않는다 — 코멘트 실패가 MM 카드 발송까지 막으면 안 된다."""
    if not gl.can_write:
        print("GITLAB_TOKEN 없음 → MR 코멘트 생략"); return
    try:
        st, _ = gl.upsert_note(FAIL_MARKER, body)
        print(f"MR 코멘트 {'게시/갱신' if st in (200, 201) else '실패 ' + str(st)}")
    except Exception as e:  # noqa: BLE001
        print(f"MR 코멘트 게시 실패(무시): {type(e).__name__}: {e}")


def ci_resolved(gl: GitLab) -> int:
    """검사가 통과했을 때, 남아 있는 실패 코멘트를 '해소됨' 으로 갱신한다(없으면 아무 것도 하지 않음).
    mm-ci-failed 는 on_failure 전용이라 통과 시점에는 실행되지 않으므로 별도 잡으로 처리한다."""
    if not gl.can_write:
        print("GITLAB_TOKEN 없음 → 생략"); return 0
    try:
        if not any(FAIL_MARKER in (n.get("body") or "") for n in gl.notes()):
            print("이전 실패 코멘트 없음 → 생략"); return 0
        body = (f"{FAIL_MARKER}\n✅ **CI 실패가 해소되었습니다** — 커밋 `{env('CI_COMMIT_SHORT_SHA', '')}` 기준으로 검사를 통과했습니다.\n\n"
                f"<sub>자동 생성(mm-notify) · 이전 실패 내용은 이 코멘트가 대체합니다</sub>")
        st, _ = gl.upsert_note(FAIL_MARKER, body)
        print(f"해소 코멘트 {'갱신' if st in (200, 201) else '실패 ' + str(st)}")
    except Exception as e:  # noqa: BLE001
        print(f"해소 코멘트 갱신 실패(무시): {type(e).__name__}: {e}")
    return 0


def ci_failed(gl: GitLab) -> int:
    mr = gl.get_mr()
    # Draft 는 아직 리뷰 요청이 아니다 — 🟢 리뷰 요청 카드를 건너뛰는 것과 같은 기준으로 🔴 카드도 보내지 않는다.
    # 실패 이유는 아래 MR 코멘트에 그대로 남으므로 작성자는 MR 화면에서 확인한다(팀 결정 2026-08-28).
    _title, is_draft = strip_draft(mr.get("title") or "")
    draft = bool(is_draft or mr.get("draft"))
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
    reasons = failure_reasons(gl, failed) if failed else []
    if failed:
        parts = " · ".join(f"[{j['name']}]({j['web_url']})" for j in failed)
        hint = {"mr-lint": "제목/본문/Jira 규칙", "repo-hygiene": "금지 파일·.meta·LFS·씬 선언", "unity-compile": "컴파일 오류"}
        hints = ", ".join(sorted({hint.get(j["name"], j["name"]) for j in failed}))
    else:
        parts, hints = f"[파이프라인]({env('CI_PIPELINE_URL', '')})", "잡 로그 확인"
    shown = reasons[:MAX_REASONS]
    capped = reasons[:MAX_COMMENT_REASONS]
    # Mattermost 표 셀은 줄바꿈도 <br> 도 렌더하지 않는다(2026-08-28 실측) → 번호를 붙여 한 줄로 잇는다
    numbered = " ".join(f"{i}) {r}" for i, (_, r) in enumerate(shown, 1)) if len(shown) > 1 else (shown[0][1] if shown else "")
    more = f" … 외 {len(reasons) - len(shown)}건" if len(reasons) > len(shown) else ""
    reason_cell = (numbered + more) if shown else "로그에서 이유를 찾지 못함 — 잡 링크에서 확인"
    rows = [
        ("MR", f"[!{gl.iid} {title}]({mr.get('web_url')})"),
        ("작성자", mention),
        ("실패한 잡", parts),
        ("이유", reason_cell),
        ("확인할 것", hints),
        ("조치", "MR 코멘트의 '무엇이 왜 실패했는지' 를 보고 고친 뒤 push 하면 자동 재검사"),
    ]
    # MR 코멘트: 사람이 이미 보고 있는 곳에 이유를 그대로 남긴다
    if reasons:
        lines = "\n".join(f"- **{name}** — {reason}" for name, reason in capped)
        if len(reasons) > len(capped):
            lines += f"\n- … 외 {len(reasons) - len(capped)}건 (전체는 잡 로그에서)"
    else:
        lines = "- 로그에서 ❌ 줄을 찾지 못했습니다. 위 잡 링크에서 직접 확인해 주세요."
    comment = (f"{FAIL_MARKER}\n🔴 **CI 실패 — 무엇이 왜 실패했는지**\n\n{lines}\n\n"
               f"고친 뒤 다시 push 하면 자동으로 재검사되고 이 코멘트도 갱신됩니다. "
               f"({parts})\n\n<sub>자동 생성(mm-notify) · 규칙 설명은 docs/git-convention.md · docs/ci-and-notifications.md</sub>")
    if draft:
        print("Draft MR → 🔴 카드 생략(코멘트만 남김)")
        rc = 0
    else:
        rc = send(card("🔴 **CI 실패**", rows))   # 카드 먼저 — 코멘트 경로가 막혀도 알림은 나간다
    upsert_mr_comment(gl, comment)
    return rc


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
    if mode == "ci-resolved":
        return ci_resolved(gl)
    print("사용법: mm_notify.py mr-opened | ci-failed | ci-resolved"); return 2


if __name__ == "__main__":
    raise SystemExit(main())
