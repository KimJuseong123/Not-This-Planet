"""mr-lint — MR 제목·브랜치·Jira 키·템플릿 검사. 위반 시 exit 1 (파이프라인 빨간불).

규칙: docs/git-convention.md §5, docs/jira-convention.md, .gitlab/merge_request_templates/Default.md
"""
import json
import os
import re

from urllib.parse import quote

from ci_common import (BRANCH_RE, KEY_RE, PROJECT_KEY, TITLE_RE, GitLab, Jira, Report, banner, env, http,
                       find_section, key_from_branch, part_of_issue, sections, strip_draft)

REQUIRED_SECTIONS = ["Jira", "무엇을 · 왜", "산출물 위치", "확인 방법", "작성자 체크리스트", "리뷰어 확인"]
_TEAM = json.load(open(os.path.join(os.path.dirname(__file__), "team.json"), encoding="utf-8"))
JIRA_BY_GITLAB = {m["gitlab"]: m["jira"] for m in _TEAM["members"]}
NAME_BY_GITLAB = {m["gitlab"]: m["name"] for m in _TEAM["members"]}


def existence_problem(key: str | None, issue: dict | None, lookup_failed: bool) -> str | None:
    """이슈 실존 검사 결과(실패 메시지 또는 None).
    조회 자체가 실패했으면 판정하지 않는다 — Jira 장애·토큰 만료를 '이슈가 없다' 로 오해하면 모든 MR 이 잘못된 이유로 빨간불이 된다(S15P21B202-94)."""
    if lookup_failed or not key:
        return None
    if issue is None:
        return (f"Jira 이슈 {key} 가 존재하지 않음 — 브랜치 이름의 번호를 확인하세요. "
                f"번호를 잘못 적었다면 그 번호의 **다른 이슈**가 자동으로 진행 중으로 바뀌었을 수 있습니다")
    return None


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
STALE_WARN = int(env("MR_STALE_WARN_COMMITS", "25") or "25")   # 이 커밋 수 이상 뒤처진 시작점이면 경고
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
    - 오래된 시작점: develop 대비 STALE_WARN 커밋 이상 뒤
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
    elif behind is not None and behind >= STALE_WARN:
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
      master   : 릴리즈·핫픽스만(develop 또는 hotfix/*)
      작업 브랜치: **스택 MR** 허용(팀 결정 2026-08-28, S15P21B202-99). 단 머지해도 Jira 이슈가 자동 완료되지 않는다 —
                  R6 는 대상이 develop 일 때만 완료시키므로, 부모 브랜치가 develop 에 머지된 뒤 손으로 완료해야 한다(R9 가 허용).
    """
    if target == "develop":
        return "ok", "대상 브랜치 develop"
    if target == "master":
        if branch.startswith("hotfix/") or branch == "develop":
            return "ok", "대상 브랜치 master (릴리즈·핫픽스)"
        return "fail", "master 로는 develop 또는 hotfix/* 만 머지"
    if BRANCH_RE.match(target):
        return "warn", (f"스택 MR — 대상이 develop 이 아니라 작업 브랜치({target})입니다. 머지해도 이 이슈는 **자동 완료되지 않습니다**: "
                        f"부모 브랜치가 develop 에 머지된 뒤 이 이슈를 손으로 완료하세요(머지된 MR 이 있으므로 허용됩니다). "
                        f"부모 브랜치가 사라지면 이 MR 도 닫히니 순서에 주의")
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

    # 1) Draft 금지
    if is_draft or mr.get("draft"):
        rep.fail("Draft MR 금지 — MR 생성 = 리뷰 요청 (진행 공유는 Jira 코멘트로). 'Mark as ready' 후 재실행")
    else:
        rep.ok("Draft 아님")

    # 2) 브랜치 이름
    bkey = key_from_branch(branch)
    if bkey:
        rep.ok(f"소스 브랜치 형식 OK (키 {bkey})")
    else:
        rep.fail(f"소스 브랜치 형식 위반: {branch!r} — `{PROJECT_KEY}-번호[-설명]` 또는 `hotfix/…`")
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
        rep.ok("템플릿 섹션 6개 존재")
    jira_sec = find_section(secs, "Jira") or ""
    dm = re.search(rf"이슈\s*:\s*`?({PROJECT_KEY}-\d+)`?", jira_sec)
    if not dm:
        rep.fail("본문 `## Jira` 의 `- 이슈: KEY` 줄이 비어 있음 — 이 줄이 없으면 Jira 개발 패널에 MR이 연결되지 않아 머지 후 자동 완료가 안 됨")
    elif bkey and dm.group(1) != bkey:
        rep.fail(f"본문 이슈 키({dm.group(1)})와 브랜치 키({bkey})가 다름")
    else:
        rep.ok(f"본문 이슈 키 {dm.group(1)}")
    author = find_section(secs, "작성자 체크리스트")
    if author is not None:
        unchecked = [ln.strip() for ln in author.splitlines() if re.match(r"^\s*-\s*\[\s\]", ln)]
        if unchecked:
            rep.fail(f"작성자 체크리스트 미완료 {len(unchecked)}개 — 전부 체크(해당 없으면 '해당 없음' 표기 후 체크)")
        else:
            rep.ok("작성자 체크리스트 완료")

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
