"""mr-title — Jira 이슈 기준으로 MR 제목을 `[Part] type: 요약 (KEY)` 로 보정하고,
본문 `- 이슈: KEY` 줄을 채운다. GitLab 이 브랜치명으로 만든 기본 제목(`S15p21b202 21`)처럼
키가 깨진 경우를 자동으로 고친다.

- 파트: Jira 이슈의 레이블/제목 태그(사람·AI 추측 없음, 결정적)
- type: 기존 제목에 있으면 유지, 없으면 변경 파일로 추정 (docs → docs, .unity/.prefab → scene, .cs → feat, 그 외 Assets → asset, 기타 chore)
- 요약: 기존 제목의 요약이 의미 있으면 유지, 기본 제목이면 Jira 제목 사용
- GITLAB_TOKEN 없으면 제안만 출력(exit 0). JIRA 자격 없으면 파트를 알 수 없어 생략.
"""
import re

from ci_common import (PARTS, PROJECT_KEY, TITLE_RE, TYPES, GitLab, Jira, banner, find_section,
                       key_from_branch, part_of_issue, sections, strip_draft, summary_body)

LOOSE_RE = re.compile(
    rf"^(?:\[(?P<part>\w+)\]\s*)?(?:(?P<type>[a-z]+)(?P<scope>\([^)]+\))?\s*:\s*)?(?P<body>.*?)\s*(?:\((?P<key>{PROJECT_KEY}-\d+)\))?\s*$")


def infer_type(paths: list[str]) -> str:
    if not paths:
        return "chore"
    if all(p.startswith("docs/") or p.lower().endswith(".md") for p in paths):
        return "docs"
    if any(p.lower().endswith((".unity", ".prefab")) for p in paths):
        return "scene"
    if any(p.lower().endswith(".cs") for p in paths):
        return "feat"
    if any(p.startswith("METAGAME/Assets/") and not p.lower().endswith(".meta") for p in paths):
        return "asset"
    return "chore"


def is_default_body(body: str, branch: str) -> bool:
    b = (body or "").strip()
    if not b:
        return True
    humanized = branch.replace("-", " ").lower()
    return b.lower() in (humanized, branch.lower()) or re.fullmatch(rf"(?i){PROJECT_KEY}[ -]\d+.*", b) is not None


def main() -> int:
    banner("mr-title")
    gl, jira = GitLab(), Jira()
    mr = gl.get_mr()
    raw_title = mr.get("title") or ""
    title, is_draft = strip_draft(raw_title)
    branch = mr.get("source_branch") or ""
    desc = mr.get("description") or ""
    key = key_from_branch(branch)
    print(f"현재 제목: {raw_title!r}\n브랜치: {branch}")
    if not key:
        print("브랜치에서 Jira 키를 찾을 수 없어 보정 생략 (mr-lint 가 실패 처리)")
        return 0
    if not jira.available:
        print("JIRA_USER/JIRA_API_TOKEN 없음 → 파트를 알 수 없어 보정 생략")
        return 0
    try:
        issue = jira.get_issue(key)
    except Exception as e:   # Jira 일시 장애·토큰 만료 — 제목 보정은 있으면 좋은 것이라 죽을 이유가 없다(-134)
        print(f"Jira 조회 실패({type(e).__name__}: {e}) → 보정 생략")
        return 0
    if not issue:
        print(f"Jira {key} 없음 → 보정 생략 (mr-lint 가 실패 처리)")
        return 0
    part = part_of_issue(issue)
    if not part:
        print(f"Jira {key} 에 파트 태그/레이블 없음(needs-fix?) → 제목 보정 생략")
        part_known = False
    else:
        part_known = True

    m = TITLE_RE.match(title)
    if m and (not part_known or m.group(1) == part) and m.group(5) == key and not is_default_body(m.group(4), branch):
        new_title = raw_title
        print("제목 형식·파트·키 모두 정상 — 변경 없음")
    else:
        lm = LOOSE_RE.match(title) or LOOSE_RE.match("")
        typ = lm.group("type") if lm and lm.group("type") in TYPES else None
        body = lm.group("body") if lm else ""
        if is_default_body(body, branch):
            body = summary_body(issue) or body
        if not typ:
            paths = [c.get("new_path") or c.get("old_path") for c in gl.changes()]
            typ = infer_type([p for p in paths if p])
        use_part = part if part_known else (lm.group("part") if lm and lm.group("part") in PARTS else "Infra")
        scope = lm.group("scope") if lm and lm.group("scope") else ""
        new_title = f"[{use_part}] {typ}{scope}: {body} ({key})"
        if is_draft:
            new_title = "Draft: " + new_title
        print(f"제안 제목: {new_title!r}")

    # 본문 `- 이슈: KEY`
    new_desc = desc
    secs = sections(desc)
    jira_sec = find_section(secs, "Jira")
    if jira_sec is None:
        new_desc = f"## Jira\n- 이슈: {key}\n- 작업 유형: (채우기)\n\n" + desc
        print("본문에 `## Jira` 섹션이 없어 앞에 추가")
    elif not re.search(rf"이슈\s*:\s*`?{re.escape(key)}`?", jira_sec):
        if re.search(r"이슈\s*:", jira_sec):
            new_desc = re.sub(r"(-\s*이슈\s*:\s*)[^\n]*", rf"\g<1>{key}", desc, count=1)
        else:
            new_desc = desc.replace("## Jira", f"## Jira\n- 이슈: {key}", 1)
        print(f"본문 이슈 줄을 `- 이슈: {key}` 로 채움")

    if new_title == raw_title and new_desc == desc:
        return 0
    if not gl.can_write:
        print("GITLAB_TOKEN 없음 → 위 제안을 사람이 반영해 주세요 (mr-lint 가 검사)")
        return 0
    fields = {}
    if new_title != raw_title:
        fields["title"] = new_title
    if new_desc != desc:
        fields["description"] = new_desc
    st, d = gl.update_mr(**fields)
    if st == 200:
        print(f"MR 갱신 완료: {list(fields)}")
        return 0
    print(f"MR 갱신 실패 {st}: {str(d)[:300]}")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
