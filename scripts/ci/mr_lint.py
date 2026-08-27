"""mr-lint — MR 제목·브랜치·Jira 키·템플릿 검사. 위반 시 exit 1 (파이프라인 빨간불).

규칙: docs/git-convention.md §5, docs/jira-convention.md, .gitlab/merge_request_templates/Default.md
"""
import re

from ci_common import (BRANCH_RE, KEY_RE, PROJECT_KEY, TITLE_RE, GitLab, Jira, Report, banner,
                       find_section, key_from_branch, part_of_issue, sections, strip_draft)

REQUIRED_SECTIONS = ["Jira", "무엇을 · 왜", "산출물 위치", "확인 방법", "작성자 체크리스트", "리뷰어 확인"]


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
    if target not in ("develop", "master"):
        rep.fail(f"대상 브랜치는 develop(릴리즈 MR만 master): {target!r}")
    elif target == "master" and not branch.startswith("hotfix/") and branch != "develop":
        rep.fail("master 로는 develop 또는 hotfix/* 만 머지")
    else:
        rep.ok(f"대상 브랜치 {target}")

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
        try:
            issue = jira.get_issue(key)
        except RuntimeError as e:
            rep.warn(f"Jira 조회 오류(검사 생략): {e}")
            issue = None
        if jira.available and issue is None and key:
            rep.fail(f"Jira 이슈 {key} 가 존재하지 않음")
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
            if itype not in ("에픽", "Epic") and not f.get("parent"):
                rep.fail("Jira 이슈에 상위 Epic 없음 — 상위 항목 필수")
            jpart = part_of_issue(issue)
            if part and jpart and part != jpart:
                rep.fail(f"MR 제목 파트 [{part}] ≠ Jira 이슈 파트 [{jpart}] — MR 파트는 이슈 파트를 따름")
            elif part and jpart:
                rep.ok(f"파트 일치 [{part}]")
            elif part and not jpart:
                rep.warn("Jira 이슈에 파트 태그/레이블이 없음 (needs-fix 상태일 수 있음)")

    return rep.finish()


if __name__ == "__main__":
    raise SystemExit(main())
