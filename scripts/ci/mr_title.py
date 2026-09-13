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


# 공백을 뺐을 때 이 길이 이하면 뜻이 없다고 본다 (S15P21B202-450).
#
# **글자에 따라 다르다** (`!387` 리뷰). 영문 2자(`ui` · `wp`)는 거의 언제나 뜻이 없지만
# **한글 2자는 멀쩡한 낱말**이다 — `잡기` · `빌드` · `복구`. 같은 자로 세면 진짜 제목을
# 지운다. 그래서 ASCII 만 2자까지 보고, 그 밖은 1자까지만 본다.
MEANINGLESS_MAX = 2
MEANINGLESS_MAX_WIDE = 1

# 한글 **자모**(ㅇ · ㅋ · ㅎ)는 길이와 무관하게 낱말이 아니다 — `ㅇㅇ` · `ㅋㅋ` 가
# 대표적인 자리표시자다. 목록이 아니라 **글자 종류**로 보므로 뒤쫓을 것이 없다.
JAMO_FIRST, JAMO_LAST = "\u3130", "\u318f"


def is_meaningless(body: str) -> bool:
    """<b>비워두고 싶은데 비울 수 없어서 아무거나 찍은 것</b>인가 (S15P21B202-450).

    GitLab 웹의 새 MR 폼은 제목을 **필수**로 받는다. 그래서 `.` · `-` · `x` 같은 값이
    들어오고, 그대로 두면 `-322` 가 제목을 Jira 기준으로 맞추려던 목적이 그 자리에서
    무너진다 — 2026-09-11 에 실제로 "그냥 `.` 하나 찍어 두면 알아서 되나" 라는 질문을
    받았고, 답은 "안 된다" 였다.

    **목록으로 잡지 않는다.** `wip` · `asdf` · `tmp` 를 목록에 넣기 시작하면 목록에 없는
    것이 계속 생겨 늘 뒤쫓게 된다. 대신 두 가지 성질로 본다:

      ① 글자도 숫자도 하나 없다 — `.` · `-` · `...` · `!!`
      ② 한글 **자모**뿐이다 — `ㅇㅇ` · `ㅋㅋ` (자모는 길이와 무관하게 낱말이 아니다)
      ③ 너무 짧다 — ASCII 는 2자까지, 그 밖은 1자까지

    ③의 자를 글자 종류로 가른다(`!387` 리뷰). 영문 2자(`ui`)는 거의 언제나 뜻이 없지만
    **한글 2자는 멀쩡한 낱말**이다(`잡기` · `빌드` · `복구`). 같은 자로 세면 진짜 제목을
    지운다.

    그래도 `UI` 같은 진짜 짧은 영문 제목은 바뀐다. 그 편이 낫다고 본다 — 그런 제목은
    받는 사람에게 아무것도 말해 주지 않고, 바꾼 내용은 잡 로그에 남으며, 사람이 다시
    고치면 그 뒤로는 형식이 맞으므로 건드리지 않는다.
    """
    b = re.sub(r"\s+", "", body or "")
    if not b:
        return True
    if not any(c.isalnum() for c in b):                         # ① 기호뿐
        return True
    if all(JAMO_FIRST <= c <= JAMO_LAST for c in b):            # ② 자모뿐
        return True
    return len(b) <= (MEANINGLESS_MAX if b.isascii()            # ③ 너무 짧다
                      else MEANINGLESS_MAX_WIDE)


def is_default_body(body: str, branch: str) -> bool:
    b = (body or "").strip()
    if is_meaningless(b):
        return True
    humanized = branch.replace("-", " ").lower()
    return b.lower() in (humanized, branch.lower()) or re.fullmatch(rf"(?i){PROJECT_KEY}[ -]\d+.*", b) is not None


def main() -> int:
    banner("mr-title")
    gl, jira = GitLab(), Jira()
    mr = gl.get_mr()
    # **모든 MR 에서 squash 를 끈다** (S15P21B202-417). 프로젝트 기본값이 squash ON 이라
    # 사람이 매번 체크를 풀어야 했고, 잊으면 그 MR 이 스택의 부모일 때 자식이 부모의 변경을
    # 다시 짊어진다. 제목 보정보다 먼저 처리한다 — develop → master 는 브랜치에 Jira 키가
    # 없어 아래 보정이 일찍 끝나기 때문이다. 결과는 mr-lint 가 검사한다.
    #
    # 처음에는 master 대상 MR 만 껐다 (S15P21B202-325). 그때는 스택 MR 이 없었다. 지금은
    # mr-overlap 이 스택을 감지해 대상을 자동 전환하고 stack-order 가 순서를 강제한다 —
    # 즉 스택이 일상이 되었고, squash 는 **스택의 부모**에서 문제가 된다:
    #
    #   squash 는 부모의 커밋을 버리고 같은 내용의 새 커밋을 만든다 → 자식과 develop 의
    #   조상 관계가 끊긴다 → 머지 베이스가 뒤로 밀린다 → 자식을 리베이스할 때 git 이
    #   **부모의 커밋까지 다시 재생**한다. 그 사이 누가 같은 줄을 고쳐 놨으면 충돌이다.
    #   (2026-09-10 재현: 같은 상황에서 squash 만 충돌, 머지 커밋은 통과)
    #
    # 껴야 하는 쪽이 부모라서 "스택만 해제" 로는 못 막는다 — 부모를 머지하는 시점에 자식이
    # 아직 없다가 나중에 생기면 그 판정이 소용없다. 그래서 전면 해제다.
    if mr.get("squash"):
        if gl.can_write:
            st, _d = gl.update_mr(squash=False)
            print("squash 해제" + ("" if st == 200 else f" 실패({st}) — mr-lint 가 막는다"))
        else:
            print("squash 가 켜져 있음 — GITLAB_TOKEN 없어 끄지 못함(mr-lint 가 막는다)")
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
            # **되돌릴 수 있게 남긴다** (S15P21B202-450). 제목이 바뀐 사람이 "내가 쓴 게
            # 어디 갔나" 를 잡 로그에서 찾을 수 있어야 한다.
            filled = summary_body(issue) or body
            if filled != body:
                print(f"제목 요약이 비어 있거나 뜻이 없어 Jira 요약으로 바꾼다: "
                      f"{body!r} → {filled!r}")
            body = filled
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
