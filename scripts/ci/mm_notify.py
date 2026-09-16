"""mm_notify — MR 파이프라인에서 Mattermost 로 보내는 '담당자 태그가 있는' 알림 2종.

  python scripts/ci/mm_notify.py mr-opened   # 🟢 리뷰 요청 — 이 MR 의 첫 파이프라인일 때만 1회, 팀장 @멘션(+작성자 표기)
  python scripts/ci/mm_notify.py ci-failed   # 🔴 CI 실패 — 실패한 잡 이름·링크, **실패 이유(❌ 줄)**, MR 작성자 @멘션 + 같은 내용을 MR 코멘트로
  python scripts/ci/mm_notify.py ci-resolved # 검사 통과 시, 이전 실패 코멘트가 있으면 "해소됨" 으로 갱신(없으면 아무 것도 하지 않음)

같은 두 명령이 **보호 브랜치 push 파이프라인에서도** 돈다(S15P21B202-344). 그때는 MR 이 없어
작성자 멘션·MR 코멘트를 쓸 수 없고 알릴 내용도 다르다 — `develop` 이 빨간 순간부터
**모든 사람의 다음 브랜치가 빨간 기준선 위에서 시작하기** 때문이다. `branch_failed`·
`branch_resolved` 가 그 경우를 맡는다.

원칙(docs/ci-and-notifications.md §4): 태그할 사람이 없는 메시지는 보내지 않는다. 첫 줄 = 등급 이모지 + 굵은 제목 + 링크,
둘째 줄 = @멘션 + 조치. 사용자 매핑은 scripts/ci/team.json. 필요 변수: MM_WEBHOOK_URL, GITLAB_TOKEN(파이프라인·잡 조회).
"""
import json
import os
import sys
from datetime import datetime, timedelta, timezone

from ci_common import GitLab, banner, bot_line, env, http, load_team, role_member, strip_draft, why

KST = timezone(timedelta(hours=9))

TEAM = load_team()
BY_GITLAB = {m["gitlab"]: m for m in TEAM["members"]}
# roles 가 비어 있어도 임포트는 되어야 한다 — 알림 스크립트가 죽으면 실패 자체가 조용해진다(-134)
LEAD = (role_member(TEAM, "lead") or {}).get("gitlab", "")
INFRA = (role_member(TEAM, "infra") or {}).get("gitlab", "") or LEAD


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
    st, resp = http("POST", url, body={"text": text, "username": "METAGAME 봇"})
    print(text); print(f"MM 전송 → {st}")
    # **성공 기준을 한 번만 정한다** (S15P21B202-484, `!410` 리뷰). 사유 판정과
    # 반환값이 서로 다른 기준을 쓰면, 2xx 인데 잡만 실패하는 조합이 생긴다.
    # 옆 호출부(publish_release.jira_release_webhook)도 같은 띠를 쓴다.
    ok = 200 <= (st or 0) < 300
    if not ok:
        # 2026-09-13: 배포 무응답 카드가 400 으로 떨어져 **아무도 못 봤다.**
        # 숫자만으로는 다음에도 못 찾는다 (S15P21B202-484).
        print(f"   ↳ 사유: {why(resp)}")
    return 0 if ok else 1


MAX_COMMENT_CHARS = 8000        # GitLab 코멘트 본문이 지나치게 커지지 않도록(-134)


def is_first_pipeline(gl: GitLab) -> bool | None:
    """이 파이프라인이 MR 의 **가장 오래된** 파이프라인인지 — 리뷰 요청 알림을 한 번만 보내기 위해.

    예전에는 파이프라인 개수(`len(pls) != 1`)로 첫 회를 판정했다. 그래서 재시도·중복 트리거로
    파이프라인이 2개가 된 MR 은 그 뒤로 **영구히** 리뷰 요청 알림을 받지 못했다(-134).
    id 가 가장 작은(=먼저 만들어진) 파이프라인만 보내면 개수와 무관하게 정확히 한 번 보낸다.
    판정할 수 없으면 None — 호출부가 생략한다(알림 하나보다 매 파이프라인 도배가 더 나쁘다)."""
    st, pls = http("GET", gl._mr_url("/pipelines?per_page=100"), gl.headers())
    if st != 200 or not isinstance(pls, list) or not pls:
        print(f"파이프라인 목록 조회 실패 {st} → 판정 불가")
        return None
    ids = [p.get("id") for p in pls if isinstance(p.get("id"), int)]
    current = env("CI_PIPELINE_ID")
    if not ids or not current:
        return None
    try:
        return int(current) == min(ids)
    except ValueError:
        return None


def mr_opened(gl: GitLab) -> int:
    mr = gl.get_mr()
    if not gl.token:
        print("GITLAB_TOKEN 없음 → 첫 파이프라인 판별 불가, 생략"); return 0
    first = is_first_pipeline(gl)
    if first is None:
        print("첫 파이프라인 판정 불가 → 리뷰 요청 알림 생략"); return 0
    if not first:
        print("이 MR 의 첫 파이프라인이 아님 → 리뷰 요청 알림 생략"); return 0
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
        # **팀장이 매 MR 마다 받는 지시문이다.** `-415` 가 MR 템플릿의
        # `## 리뷰어 확인` 절을 없앴는데 이 줄은 계속 그리로 안내했다 — 그대로
        # 따라 본문 하단으로 내려가면 그런 절이 없고, 리뷰 기준이 어디 있는지
        # 모른 채 머지하게 된다. 지금 실제로 보는 두 가지를 적는다(`-517`).
        ("조치", "AI 리뷰 코멘트의 판정표에서 ⚠️·❓ 를 보고, MR 본문의 "
                 "`머지 전에 팀장이 확인할 것` 을 확인한 뒤 머지"),
    ]))


FAIL_MARKER = "<!-- ci-failed:metagame -->"
MAX_REASONS = 3          # 카드에 싣는 이유 개수
MAX_COMMENT_REASONS = 20  # MR 코멘트 상한 — 위반이 수십 건이어도 코멘트가 비대해지지 않게
MAX_REASON_CHARS = 200

# 잡 로그에서 "실패 이유" 로 뽑을 줄의 머리표 (S15P21B202-416).
#
# **예전에는 `❌` 한 글자가 코드에 박혀 있었다.** 그런데 `unity-compile`
# (`unity_report.py`)은 판정을 `🔴` 로 적는다 — 그래서 **가장 자주 실패하는 잡의
# 이유가 구조적으로 영원히 안 나왔다.** 2026-09-10 하루에 세 번 "로그에서 이유를
# 찾지 못함" 카드가 나갔고, 세 번 다 이유가 로그에 있었다.
#
# 다른 잡(`mr-lint`·`repo-hygiene`)이 `❌` 를 쓰는 덕에 오래 안 보였다. 목록으로
# 두어 다음에 표시가 늘어도 여기 한 줄만 고치면 되게 한다.
REASON_MARKERS = ("❌", "🔴")

# 잡 이름 → "확인할 것" 문구 (S15P21B202-416).
#
# **모듈 상수여야 한다.** 함수 안의 지역 사전이면 테스트가 소스 문자열을 훑어야 하고,
# 그러면 동작이 아니라 코드 표기에 묶인다(`!322` AI 리뷰). 값으로 단정할 수 있게 둔다.
#
# `unity-compile` 은 **컴파일과 EditMode 테스트를 둘 다** 돌린다. 예전에는 "컴파일 오류"
# 라고만 적어서, 테스트로 실패한 카드가 사람을 컴파일 로그로 보냈다.
JOB_HINTS = {
    "mr-lint": "제목/본문/Jira 규칙",
    "repo-hygiene": "금지 파일·.meta·LFS·씬 선언",
    "unity-compile": "컴파일 · EditMode 테스트",
}
MAX_JOB_PAGES = 5        # 잡 목록 조회 상한 — 무한 순회 방지(잡 13개 남짓이라 1페이지로 끝난다)


def clean_reason(text: str) -> str:
    """로그 한 줄을 표·코멘트에 넣어도 안전하게 — 표 구분자와 태그를 지우고 길이를 자른다."""
    # 마크다운 굵게는 지운다 (S15P21B202-416). `unity-compile` 의 판정 줄이
    # `🔴 **이 MR 에서 새로 실패하는 테스트 1개**` 라, 그대로 실으면 카드에 별표가 남는다.
    t = " ".join(text.split()).replace("**", "")
    t = t.replace("|", "／").replace("<", "&lt;").replace(">", "&gt;")
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
        for reason in reasons_from_trace(trace):
            if (name, reason) not in out:
                out.append((name, reason))
    return out


def reasons_from_trace(trace: str) -> list[str]:
    """로그에서 이유 줄만 골라 준다 (S15P21B202-416).

    순수 함수로 떼어 둔다 — 네트워크 없이 시험할 수 있어야 한다(`-395` 의
    `human_checks_content` 와 같은 이유). 마커는 `REASON_MARKERS` 가 정한다.

    같은 줄에 마커가 여럿이면 **처음 나온 것**을 기준으로 자른다. 순서대로 찾아
    가장 앞의 것을 쓴다 — 뒤의 마커로 자르면 앞부분이 이유에서 사라진다.
    """
    out: list[str] = []
    for line in (trace or "").splitlines():
        cut = -1
        marker = ""
        for candidate in REASON_MARKERS:
            at = line.find(candidate)
            if at >= 0 and (cut < 0 or at < cut):
                cut, marker = at, candidate

        if cut < 0:
            continue

        reason = clean_reason(line[cut + len(marker):])
        if reason and reason not in out:
            out.append(reason)
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
        body = (f"{FAIL_MARKER}\n✅ **CI 실패가 해소되었습니다** — 커밋 `{env('CI_COMMIT_SHORT_SHA', '')}` 기준으로 검사를 통과했습니다.\n"
                f"{bot_line(env('CI_JOB_NAME', 'mm-ci-resolved'), '**머지를 막지 않습니다** — 해소 알림입니다', with_sha=False)}\n\n"
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
    failed, retried = failed_jobs(gl)
    title, _ = strip_draft(mr.get("title") or "")
    reasons = failure_reasons(gl, failed) if failed else []
    if failed:
        parts = " · ".join(f"[{j['name']}]({j['web_url']})" for j in failed)
        if retried:
            # 이미 지나간 실패다. 말하지 않으면 사람이 고쳐진 것을 다시 쫓는다 (-361).
            parts += " — **재시도 뒤 통과**"
        hints = ", ".join(sorted({JOB_HINTS.get(j["name"], j["name"]) for j in failed}))
        if retried:
            # 덮어쓰지 않는다 — 잡별 분류("제목/본문/Jira 규칙" 등)가 카드의 본론이다.
            hints = f"{hints} · 이미 통과했으니 확인만 하면 된다"
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
    # MR 코멘트: 사람이 이미 보고 있는 곳에 이유를 그대로 남긴다.
    #
    # **재시도로 이미 통과했으면 코멘트를 건드리지 않는다**(-361). 그 경우 같은
    # 파이프라인에서 `mm-ci-resolved` 가 **같은 마커**에 ✅ 를 쓴다 — 2026-09-09 실측:
    # 파이프라인 184897 에서 `mm-ci-failed`(500790)와 `mm-ci-resolved`(500791)가 둘 다
    # 돌았다. 둘이 경쟁하면 최종 내용이 비결정적이 되고(-289 가 고친 그 사고), 초록인
    # 파이프라인에 🔴 코멘트가 남을 수 있다. 카드는 그대로 보낸다 — 실패가 있었던 사실은
    # 사라지면 안 되고, MM 카드는 덮어쓰이지 않는 통로다. 지속 상태는 resolved 에 맡긴다.
    if reasons:
        lines = "\n".join(f"- **{name}** — {reason}" for name, reason in capped)
        if len(reasons) > len(capped):
            lines += f"\n- … 외 {len(reasons) - len(capped)}건 (전체는 잡 로그에서)"
        if len(lines) > MAX_COMMENT_CHARS:   # 이유가 아주 길면 코멘트가 API 본문 한도에 걸린다(-134)
            lines = lines[:MAX_COMMENT_CHARS] + "\n- … (너무 길어 잘랐습니다 — 잡 로그를 보세요)"
    else:
        lines = "- 로그에서 ❌ 줄을 찾지 못했습니다. 위 잡 링크에서 직접 확인해 주세요."
    # **카드 자체는 막지 않는다 — 그러나 실패한 잡이 막고 있다.** 둘을 구별해 주지
    # 않으면 "알림이니 넘어가도 된다" 로 읽힌다 (S15P21B202-425).
    comment = (f"{FAIL_MARKER}\n🔴 **CI 실패 — 무엇이 왜 실패했는지**\n"
               f"{bot_line(env('CI_JOB_NAME', 'mm-ci-failed'), '이 카드가 막는 것은 아니지만, **아래 표의 실패한 잡 때문에 머지는 막혀 있습니다**')}\n\n{lines}\n\n"
               f"고친 뒤 다시 push 하면 자동으로 재검사되고 이 코멘트도 갱신됩니다. "
               f"({parts})\n\n<sub>자동 생성(mm-notify) · 규칙 설명은 docs/git-convention.md · docs/ci-and-notifications.md</sub>")
    if draft:
        print("Draft MR → 🔴 카드 생략(코멘트만 남김)")
        rc = 0
    else:
        rc = send(card("🔴 **CI 실패**", rows))   # 카드 먼저 — 코멘트 경로가 막혀도 알림은 나간다
    if retried:
        # **재시도로 이미 통과했으면 코멘트를 건드리지 않는다**(-361). 그 경우 같은
        # 파이프라인에서 `mm-ci-resolved` 가 **같은 마커**(FAIL_MARKER)에 ✅ 를 쓴다 —
        # 2026-09-09 실측: 파이프라인 184897 에서 `mm-ci-failed`(500790)와
        # `mm-ci-resolved`(500791)가 둘 다 돌았다. 둘이 같은 코멘트를 경쟁해서 쓰면
        # 최종 내용이 비결정적이 되고(-289 가 고친 그 사고), 초록인 파이프라인에 🔴
        # 코멘트가 남을 수 있다. **카드는 그대로 보낸다** — 실패가 있었던 사실은 사라지면
        # 안 되고 MM 카드는 덮어쓰이지 않는 통로다. 지속 상태는 resolved 에 맡긴다.
        print("재시도 뒤 통과 — MR 코멘트는 mm-ci-resolved 에 맡긴다(마커 경합 방지)")
    else:
        upsert_mr_comment(gl, comment)
    return rc


# ── 보호 브랜치(develop) 가 빨간 경우 (S15P21B202-344) ───────────
#
# **왜 따로 다루나.** MR 실패와 성격이 다르다. MR 은 그 사람이 고칠 때까지 남의 일이
# 아닌데, `develop` 이 빨간 순간부터는 **모든 사람의 다음 브랜치가 빨간 기준선 위에서
# 시작한다.** 그리고 `unity-compile` 은 머지 직후에도 도는데(개별 MR 은 통과했는데
# 합쳐서 깨지는 경우를 잡는다) 그 실패에 알림이 없었다 — 2026-09-08 14:11 에 두 커밋
# 연속 실패하고 카드가 0건이었다.
#
# MR 이 없으므로 작성자 멘션 경로를 쓸 수 없다. **밀어 넣은 사람**(머지 버튼을 누른
# 사람이 대부분이다)과 Infra 를 함께 부른다 — 원인이 두 MR 의 조합이면 저자가 하나로
# 정해지지 않으므로, 되돌릴 수 있는 사람을 부르는 것이 맞다.


def _blocking_failures(jobs: list[dict]) -> list[dict]:
    """막는 실패만. `allow_failure` 잡의 실패는 파이프라인을 빨갛게 만들지 않는다."""
    return [j for j in jobs if j.get("status") == "failed" and not j.get("allow_failure")]


def _all_jobs(gl: GitLab, base: str, retried: bool = False) -> list[dict] | None:
    """파이프라인의 잡 전부. 조회가 실패하면 None — "잡이 없다" 와 섞으면 잘못 판단한다.

    **페이지를 끝까지 읽는다.** `include_retried=true` 는 잡 수가 시도 수만큼 늘어나므로,
    한 페이지만 읽으면 상한을 넘는 순간 조용히 잘려 **이 함수가 고치려는 그 실패 모드가
    그대로 재발한다**(가려진 실패를 못 찾는다). 지금 파이프라인은 잡 13개 남짓이라 한
    페이지로 끝나지만, 조용히 틀리는 쪽으로 여지를 남기지 않는다.
    """
    extra = "&include_retried=true" if retried else ""
    out: list[dict] = []
    for page in range(1, MAX_JOB_PAGES + 1):
        st, chunk = http("GET", f"{base}?per_page=100&page={page}{extra}", gl.headers())
        if st != 200 or not isinstance(chunk, list):
            return None if page == 1 else out
        out.extend(chunk)
        if len(chunk) < 100:
            return out
    print(f"  잡 목록이 {MAX_JOB_PAGES} 페이지를 넘는다 — 뒤쪽을 못 봤을 수 있다")
    return out


def failed_jobs(gl: GitLab) -> tuple[list[dict], bool]:
    """(막는 실패 잡, 재시도로 가려졌는가). MR 경로와 develop 경로가 같은 규칙을 쓴다.

    **왜 두 번 묻는가** (S15P21B202-361). GitLab 의 기본 잡 목록은 이름당 **최신 시도만**
    준다. 그래서 빠르게 실패한 검사(`mr-lint` 6초)를 재실행하는 동안 느린 잡
    (`unity-compile` 2분)이 아직 돌고 있으면, `when: on_failure` + `needs` 인 알림 잡이
    도는 시점에 목록에 실패 상태 잡이 **하나도 없다.** 그러면 카드가
    "실패한 잡 | 파이프라인 · 이유 | 로그에서 이유를 찾지 못함" 이라는 **눈먼 카드**가 된다
    (2026-09-09 실측: 파이프라인 184897 · `mr-lint` 500782 실패 → 500804 재실행 통과 →
    500790 카드). 카드가 거짓을 말한 것은 아니고 **쓸모가 없어진 것**이며, 그 카드의 존재
    이유가 로그를 열지 않고도 무엇이 왜 깨졌는지 아는 것이다.

    그래서 현재 실패가 없으면 `include_retried=true` 로 다시 물어 **실패했던 시도**를 찾는다.
    카드를 생략하지는 않는다 — 실패가 있었던 사실 자체가 사라지면 조용해지는 쪽으로 틀린다.
    대신 두 번째 값으로 "이미 재시도 뒤 통과했다" 를 알려 카드가 그 사실을 말하게 한다.
    """
    pid = env("CI_PIPELINE_ID")
    if not (gl.token and pid):
        return [], False
    base = f"{gl.api}/projects/{gl.project}/pipelines/{pid}/jobs"
    jobs = _all_jobs(gl, base)
    if jobs is None:
        return [], False
    live = _blocking_failures(jobs)
    if live:
        return live, False

    # 현재 실패가 없다 — 재시도에 가려진 실패를 찾는다.
    all_tries = _all_jobs(gl, base, retried=True)
    if all_tries is None:
        return [], False
    # 이름별 최신 시도. id 는 단조 증가하므로 그것으로 최신을 가른다.
    latest: dict[str, int] = {}
    for j in all_tries:
        n = j.get("name") or "?"
        latest[n] = max(latest.get(n, -1), int(j.get("id") or 0))
    hidden = [j for j in _blocking_failures(all_tries)
              if int(j.get("id") or 0) != latest.get(j.get("name") or "?", -1)]
    # 같은 이름이 여러 번 실패했으면 가장 최근 실패 하나만 싣는다 — 카드는 짧아야 한다.
    best: dict[str, dict] = {}
    for j in hidden:
        n = j.get("name") or "?"
        if int(j.get("id") or 0) >= int((best.get(n) or {}).get("id") or 0):
            best[n] = j
    return list(best.values()), bool(best)


def branch_pipeline_jobs(gl: GitLab) -> list[dict]:
    """옛 이름 — 실패 잡만 준다. 재시도 여부까지 필요하면 `failed_jobs()` 를 쓴다."""
    return failed_jobs(gl)[0]


def is_scheduled() -> bool:
    """예약 실행인가. 새벽 배포·아침 점검이 여기 든다."""
    return env("CI_PIPELINE_SOURCE", "") == "schedule"


def schedule_name() -> str:
    """어느 예약인지 사람 말로. 시각을 쓰는 이유는 스케줄 이름을 잡이 모르기 때문이다.

    **KST 로 바꿔야 한다** (`!448` AI 리뷰). `CI_PIPELINE_CREATED_AT` 은 UTC 라
    글자를 그대로 잘라 쓰면 04:00 예약이 카드에 **"19:00 예약"** 으로 찍힌다 —
    스케줄이 04:00·09:30 둘뿐인데 둘 다 못 알아보는 이름이 된다.
    """
    raw = env("CI_PIPELINE_CREATED_AT", "")
    if not raw:
        return "예약"
    try:
        ts = datetime.fromisoformat(raw.replace("Z", "+00:00"))
    except ValueError:
        return "예약"
    if ts.tzinfo is None:          # 오프셋이 없으면 UTC 로 본다 — GitLab 이 주는 모양이다
        ts = ts.replace(tzinfo=timezone.utc)
    return ts.astimezone(KST).strftime("%H:%M") + " 예약"


def branch_failed(gl: GitLab) -> int:
    branch = env("CI_COMMIT_BRANCH", "") or "?"
    sha = env("CI_COMMIT_SHORT_SHA", "") or "?"
    title = env("CI_COMMIT_TITLE", "") or ""

    pusher = env("GITLAB_USER_LOGIN", "")
    mentions = [m for m in (mm_of(pusher), mm_of(INFRA)) if m]
    # 매핑이 없어도 알림은 나가야 한다 — 조용해지는 것이 가장 나쁘다.
    mention = " ".join(dict.fromkeys(mentions)) or f"@{BY_GITLAB[INFRA]['mm']}"

    failed, retried = failed_jobs(gl)
    reasons = failure_reasons(gl, failed) if failed else []
    if failed:
        parts = " · ".join(f"[{j['name']}]({j['web_url']})" for j in failed)
        if retried:
            parts += " — **재시도 뒤 통과**"
    else:
        parts = f"[파이프라인]({env('CI_PIPELINE_URL', '')})"

    shown = reasons[:MAX_REASONS]
    numbered = " ".join(f"{i}) {r}" for i, (_, r) in enumerate(shown, 1)) if len(shown) > 1 else (shown[0][1] if shown else "")
    more = f" … 외 {len(reasons) - len(shown)}건" if len(reasons) > len(shown) else ""
    reason_cell = (numbered + more) if shown else "로그에서 이유를 찾지 못함 — 잡 링크에서 확인"

    # **master 는 말이 다르다** (S15P21B202-407 ①).
    #
    # develop 이 빨간 것은 "모든 사람의 다음 브랜치가 빨간 기준선 위에서 시작한다" 다.
    # master 는 아무도 그 위에서 브랜치를 따지 않는다 — 대신 **릴리스가 나가지 않았다.**
    # 같은 문구를 쓰면 읽는 사람이 할 일을 잘못 고른다(develop 이면 고치는 MR, master 면
    # 릴리스를 다시 내보내는 것).
    if branch == "master":
        head, tail = "🔴 **릴리스가 실패했다**", [
            ("영향", "**이 릴리스는 나가지 않았다** — 패키지·Release·태그가 만들어지지 않았고 "
                     "사용자가 받을 것이 없다. master 코드는 머지된 그대로 남아 있다"),
            ("담당", mention),
            ("조치", "잡 로그의 ❌ 줄을 보고 고친다. 버전을 못 정한 경우라면 머지 커밋에 "
                     "`release/<키>-vX.Y.Z` 가 없는 것이다(git-convention §8). 고친 뒤 "
                     "이 파이프라인을 재실행하면 릴리스가 다시 시도된다"),
        ]
    elif is_scheduled():
        # **예약 실행은 말이 또 다르다** (S15P21B202-533).
        #
        # 새벽 배포·아침 점검이 실패한 것은 `develop` 의 코드가 빨간 것이 아니다 —
        # 그 커밋은 이미 MR 파이프라인을 통과해 머지된 것이다. 여기에 대고
        # "빨간 기준선 위에서 시작한다" 고 하면 **읽는 사람이 할 일을 잘못 고른다**
        # (있지도 않은 원인 MR 을 되돌리려 든다).
        #
        # 2026-09-15 04:03 에 `build-devbuild` 가 죽었는데 이 잡이 `schedule` 에서
        # 아예 돌지 않아 **아무도 못 들었다.** 그래 놓고 08:31 푸시가 ✅ 회복 카드를
        # 보냈다 — 알린 적 없는 실패가 해소됐다고 알려진 것이다.
        head, tail = f"🔴 **예약 실행이 실패했다** ({schedule_name()})", [
            ("영향", "`" + branch + "` 의 코드가 빨간 것이 아니다 — 그 커밋은 이미 검사를 통과했다. "
                     "**그 시간에 하기로 한 일이 안 됐다**(개발 서버 갱신 · QA 빌드 등)"),
            ("담당", mention),
            ("조치", "잡 로그의 ❌ 줄을 보고 고친다. 고칠 때까지 **매일 같은 시각에 다시 실패한다**"),
        ]
    else:
        head, tail = f"🔴 **{branch} 가 빨갛다**", [
            ("영향", f"지금 `{branch}` 에서 딴 브랜치는 **빨간 기준선 위에서 시작한다** — 남의 실패를 물려받는다"),
            ("담당", mention),
            ("조치", "고치는 MR 을 올리거나 원인 MR 을 되돌린다. 고쳐지면 다음 초록 파이프라인이 ✅ 카드를 보낸다"),
        ]

    return send(card(head, [
        ("커밋", f"[`{sha}`]({env('CI_PROJECT_URL', '')}/-/commit/{env('CI_COMMIT_SHA', '')}) {title}".strip()),
        ("실패한 잡", parts),
        ("이유", reason_cell),
    ] + tail))


def branch_resolved(gl: GitLab) -> int:
    """빨갛던 보호 브랜치가 초록으로 돌아왔을 때만 알린다.

    **직전 파이프라인이 실패였는지 본다.** 그것을 안 보면 초록일 때마다 카드가 나가
    채널이 시끄러워지고, 반대로 아무것도 안 보내면 사람들이 언제 다시 브랜치를 따도
    되는지 알 수 없다. 🔴 카드가 "빨간 기준선 위에서 시작한다" 고 경고했으므로 **풀렸다는
    말도 같은 채널에 있어야 한다.**
    """
    branch = env("CI_COMMIT_BRANCH", "") or "?"
    pid = env("CI_PIPELINE_ID", "")
    if not (gl.token and branch != "?"):
        print("토큰·브랜치 없음 → 생략"); return 0

    # **master 는 이 카드를 보내지 않는다** (S15P21B202-407 ①).
    #
    # 이 카드의 내용은 "이제 이 브랜치에서 브랜치를 따도 된다" 인데, master 에서는 아무도
    # 브랜치를 따지 않는다. 그리고 master 가 초록이면 그것은 곧 릴리스가 성공한 것이고,
    # `publish_release.py` 가 이미 내려받기 링크·들어간 MR 목록이 든 🟢 카드를 보낸다.
    # 여기서 또 보내면 같은 사실이 두 장이 되고, 정보가 적은 쪽이 나중에 온다.
    if branch == "master":
        print("master 초록 — 🟢 릴리스 카드가 publish_release 에서 나간다 → 생략"); return 0

    st, pipes = http("GET", f"{gl.api}/projects/{gl.project}/pipelines"
                            f"?ref={branch}&per_page=10&order_by=id&sort=desc", gl.headers())
    if st != 200 or not isinstance(pipes, list):
        print(f"파이프라인 목록 조회 실패 {st} → 생략"); return 0

    # **같은 종류끼리만 견준다** (S15P21B202-533).
    #
    # 예약 실행과 머지 푸시는 **도는 잡이 다르다** — `changes:` 로 걸러지기 때문이다.
    # 2026-09-15 에 실제로 이렇게 됐다: 04:03 새벽(`build-devbuild` 실패)을 직전으로
    # 보고, 08:31 머지 푸시가 ✅ 회복 카드를 보냈다. 그런데 그 푸시는 `scripts/ci/**`
    # 만 바꿔 **실질 잡이 `retarget-pipelines` 하나**였다 — 아무것도 검사하지 않고
    # "이제 브랜치를 따도 된다" 고 말한 것이다.
    source = env("CI_PIPELINE_SOURCE", "")
    previous = None
    for p in pipes:
        if str(p.get("id")) == str(pid):
            continue
        # **키가 있을 때만 견준다** (`!448` AI 리뷰). GitLab 은 늘 `source` 를 주지만
        # (실측 2026-09-15: `195117`·`195114`·`195112` 모두 `push`), 혹시 없으면
        # 모든 후보가 걸러져 **회복 카드가 조용히 사라진다** — 없는 쪽으로 틀리면 안 된다.
        other = p.get("source")
        if source and other and other != source:   # 종류가 다르면 견줄 대상이 아니다
            continue
        if p.get("status") in ("success", "failed"):   # running·canceled 는 판단 근거가 아니다
            previous = p
            break

    if previous is None:
        print("직전 파이프라인을 찾지 못했다 → 생략"); return 0
    if previous.get("status") != "failed":
        print(f"직전 파이프라인이 {previous.get('status')} → 알릴 것 없음"); return 0

    sha = env("CI_COMMIT_SHORT_SHA", "") or "?"
    title = env("CI_COMMIT_TITLE", "") or ""
    return send(card(f"✅ **{branch} 가 초록으로 돌아왔다**", [
        ("커밋", f"[`{sha}`]({env('CI_PROJECT_URL', '')}/-/commit/{env('CI_COMMIT_SHA', '')}) {title}".strip()),
        ("직전", f"[실패한 파이프라인]({previous.get('web_url', '')})"),
        # **예약 실행은 여기 오지 않는다** (S15P21B202-533). 회복 카드를 예약에서
        # 보내면 09:30 점검 성공이 04:00 새벽 실패를 해소한 것처럼 말하게 된다 —
        # 둘 다 `source=schedule` 이라 갈라낼 수가 없다. `.gitlab-ci.yml` 의
        # `mm-nightly-failed` 주석에 근거를 적어 뒀다.
        ("조치", f"이제 `{branch}` 에서 브랜치를 따도 된다"),
    ]))


# ── 배포 (S15P21B202-144) ─────────────────────────────────────
# 왜 잡을 따로 두지 않고 deploy_backend.py 가 직접 부르나 — 결과를 아는 곳이 거기다.
# 스크립트는 성공/롤백/무응답/실패를 구분하는데, 잡의 종료 코드는 '실패' 하나로 뭉갠다.
DEPLOY_GRADE = {
    "ok":       ("🟢", "배포 완료", False),
    "rollback": ("🔴", "배포 롤백", True),
    "timeout":  ("🔴", "배포 무응답", True),
    "fail":     ("🔴", "배포 실패", True),
}


def deploy(result: str, detail: str = "") -> int:
    """배포 결과 카드를 보낸다. 실패면 조치할 사람(Infra)을 멘션한다.

    이 알림이 필요한 이유는 **같은 서버로 테스트하던 다른 팀원이 재시작을 모른다**는 데 있다 —
    재시작은 진행 중인 WebSocket 세션을 전부 끊는다. 그래서 성공도 알린다.

    **누가 배포했는지는 적지 않는다**(S15P21B202-566). 전에는 `GITLAB_USER_LOGIN` 을 "누른
    사람" 으로 실었는데, 그 값은 예약 파이프라인에서 **예약을 만든 사람**이다 — 새벽 자동
    배포(-455)가 돌 때마다 아무것도 안 한 사람이 누른 것으로 찍혔다. 배포 뒤에 문제가 생기면
    그 이름을 먼저 찾게 되므로 **틀린 이름은 없는 것보다 나쁘다.**

    지운 것이지 다른 데로 옮긴 것이 아니다. 이 카드를 읽는 사람에게 필요한 것은 "서버가
    재시작됐다" 뿐이고, 누른 사람 본인은 잡 로그를 이미 보고 있다. 누가 돌렸는지는
    파이프라인 링크에 그대로 남는다.
    """
    emoji, label, blame = DEPLOY_GRADE.get(result, ("🔴", f"배포 결과 불명({result})", True))
    sha = (env("CI_COMMIT_SHA", "") or "")[:8] or "?"
    title = env("CI_COMMIT_TITLE", "") or ""
    rows = [("커밋", f"`{sha}` {title}".strip())]
    if detail:
        rows.append(("결과", detail))
    if result == "rollback":
        rows.append(("지금 서버", "**직전 jar 로 돌고 있습니다** — 이 커밋의 변경은 반영되지 않았습니다"))
    elif result == "timeout":
        rows.append(("지금 서버", "**상태를 확인해야 합니다** — 배포가 시작됐는지조차 응답이 없었습니다"))
    elif result == "ok":
        rows.append(("주의", "재시작으로 **진행 중이던 접속이 모두 끊겼습니다** — 테스트 중이었다면 다시 접속하세요"))
    links = []
    if env("CI_PIPELINE_URL"):
        links.append(f"[파이프라인]({env('CI_PIPELINE_URL')})")
    if env("CI_JOB_URL"):
        links.append(f"[배포 잡 로그]({env('CI_JOB_URL')})")
    if links:
        rows.append(("링크", " · ".join(links)))
    if blame:
        rows.append(("볼 곳", "잡 로그 · 호스트 `~/metagame/deploy/deploy.log` · `systemctl status metagame-deploy.path`"))
    infra = mm_of(INFRA) if blame else None      # 못 찾으면 멘션 없이 — 알림 자체가 사라지는 것보다 낫다
    return send(card(f"{emoji} **{label}**{' ' + infra if infra else ''}", rows))


def main() -> int:
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    banner(f"mm-notify {mode}")
    if mode == "deploy":       # push 파이프라인이라 MR iid 가 없다 — iid 검사보다 앞에 둔다
        return deploy(sys.argv[2] if len(sys.argv) > 2 else "fail",
                      sys.argv[3] if len(sys.argv) > 3 else "")
    gl = GitLab()
    if not gl.iid:
        # 보호 브랜치 push 파이프라인 (S15P21B202-344). MR 이 없으므로 작성자 멘션·MR
        # 코멘트 경로를 쓸 수 없고, 알릴 내용도 다르다 — branch_* 가 따로 처리한다.
        branch = env("CI_COMMIT_BRANCH", "")
        if branch and mode == "ci-failed":
            return branch_failed(gl)
        if branch and mode == "ci-resolved":
            return branch_resolved(gl)
        print("MR 파이프라인이 아님 → 생략"); return 0
    if mode == "mr-opened":
        return mr_opened(gl)
    if mode == "ci-failed":
        return ci_failed(gl)
    if mode == "ci-resolved":
        return ci_resolved(gl)
    print("사용법: mm_notify.py mr-opened | ci-failed | ci-resolved | deploy <ok|rollback|timeout|fail> [상세]"); return 2


if __name__ == "__main__":
    raise SystemExit(main())
