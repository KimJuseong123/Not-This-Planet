"""사람에게 인쇄되는 글이 **없어진 MR 템플릿 절**을 가리키지 않는다 (S15P21B202-517).

실행: python scripts/ci/tests/test_no_dead_template_section.py

같은 종류를 세 번 고쳤다 — `-514` 가 `workflow.md` 10곳, `-515` 가 나머지 문서 12곳,
그리고 `-517` 이 코드 두 곳이다. 절 이름이 바뀌어도 **코드가 조용히 옛 이름을 계속
인쇄**했기 때문이다.

여기서 지키는 것은 문자열 하나가 아니라 **관계**다: CI 가 사람에게 "MR 본문의 X 를
보라" 고 쓰면, X 가 실제로 템플릿에 있어야 한다. 템플릿이 다음에 또 바뀌면 이 시험이
먼저 실패한다.

`mr_lint.py` 는 대상이 아니다 — 옛 이름으로 올라온 MR 에 "이제 이 이름입니다" 라고
안내하는 문구라 **옛 이름이 있어야 한다.**
"""
import ast
import inspect
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.dirname(HERE))
os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "7",
                   "CI_COMMIT_SHORT_SHA": "abc1234", "MM_WEBHOOK_URL": "https://mm.example/h"})

import ai_review as AR  # noqa: E402
import mm_notify  # noqa: E402

print("― S15P21B202-517 없어진 템플릿 절을 가리키지 않는다 ―")

TEMPLATE = os.path.join(ROOT, ".gitlab", "merge_request_templates", "Default.md")
DEAD = "리뷰어 확인"                     # `-415` 가 없앤 절
POINTS_AT = "머지 전에 팀장이 확인할 것"   # 🟢 카드가 팀장에게 가리켜야 하는 절
ok = 0


# ── ① 템플릿에 실제로 있는 절 ───────────────────────────────────────────
#
#     기준을 문서가 아니라 **템플릿 파일 자체**에서 읽는다. 문서를 기준으로 삼으면
#     문서가 낡았을 때 시험도 같이 낡는다 — 그게 `-514`·`-515` 에서 일어난 일이다.
body = io.open(TEMPLATE, encoding="utf-8").read()
SECTIONS = set(re.findall(r"^##\s+(.+?)\s*$", body, re.M))
assert SECTIONS, "템플릿에서 `## ` 절을 하나도 못 읽었다: " + TEMPLATE
assert DEAD not in SECTIONS, f"`{DEAD}` 절이 되살아났다면 이 시험의 전제가 틀렸다"
print(f"  템플릿 절 {len(SECTIONS)}개: {' · '.join(sorted(SECTIONS))}")
ok += 1


# ── ② 🟢 리뷰 요청 카드 — 팀장이 매 MR 마다 받는 지시문 ─────────────────
#     소스를 통째로 grep 하면 **주석까지 걸린다** — 왜 이름이 바뀌었는지 적어 둔
#     설명에 옛 이름이 나오기 때문이다. 검사 대상은 사람에게 인쇄되는 것, 곧
#     **문자열 리터럴**뿐이므로 ast 로 그것만 꺼낸다.
HAS_BODY = (ast.Module, ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef)


def literals(node) -> str:
    """이 노드 아래에서 **사람에게 인쇄되는** 문자열. f-string 조각도 `ast.walk` 가
    집어 준다.

    **docstring 은 뺀다**(`!431` 리뷰 [낮음]). docstring 은 주석과 똑같이 인쇄되지
    않는 설명인데 파서에게는 문자열이다. 빼지 않으면 "왜 이 이름이 바뀌었는지" 를
    docstring 에 적는 순간 시험이 실패한다 — 주석을 피해 ast 를 쓴 이유와 정면으로
    어긋난다.
    """
    skip = set()
    for x in ast.walk(node):
        body = getattr(x, "body", None) if isinstance(x, HAS_BODY) else None
        if body and isinstance(body[0], ast.Expr) and isinstance(body[0].value, ast.Constant) \
                and isinstance(body[0].value.value, str):
            skip.add(id(body[0].value))
    return " ".join(c.value for c in ast.walk(node)
                    if isinstance(c, ast.Constant) and isinstance(c.value, str)
                    and id(c) not in skip)


def func(mod, name: str):
    """이름으로 함수를 찾는다. **못 찾으면 조용히 빈손으로 통과하지 않고 실패한다** —
    `next()` 의 StopIteration 은 무슨 일인지 말해 주지 않는다(`!431` 리뷰)."""
    tree = ast.parse(inspect.getsource(mod))
    for x in ast.walk(tree):
        if isinstance(x, (ast.FunctionDef, ast.AsyncFunctionDef)) and x.name == name:
            return x
    raise AssertionError(f"`{mod.__name__}.{name}` 을 못 찾았다 — 이름이 바뀌었으면 "
                         "이 시험도 따라가야 한다. 그냥 두면 아무것도 안 보게 된다")


#     ⓐ 모듈 전체 — 옛 이름이 **어디서든** 인쇄되면 걸린다. 함수 하나만 보면
#        문구가 상수나 헬퍼로 옮겨질 때 검사가 조용해진다(`!431` 리뷰 [낮음]).
whole = literals(ast.parse(inspect.getsource(mm_notify)))
assert DEAD not in whole, f"`mm_notify` 어딘가가 없어진 `{DEAD}` 절을 인쇄한다"

#     ⓑ 카드 함수 — **어떤 절이든 하나 걸리면 통과**로 두면 안 된다(`!431` 리뷰 [중간]).
#        `Jira` 같은 짧은 절 이름이 다른 라벨에 우연히 걸려, 조치 줄을 통째로 지워도
#        통과하는 길이 생긴다. 가리켜야 하는 절을 **이름으로 못 박는다.**
card = literals(func(mm_notify, "mr_opened"))
assert POINTS_AT in SECTIONS, f"카드가 가리키는 `{POINTS_AT}` 절이 템플릿에서 사라졌다"
assert POINTS_AT in card, ("🟢 카드가 `" + POINTS_AT + "` 을 가리키지 않는다 — 팀장은 "
                           "무엇을 보고 머지해야 하는지 알 수 없다")
print(f"  🟢 리뷰 요청 카드 → `{POINTS_AT}` (템플릿에 있다) ✅")
ok += 1


# ── ③ AI 리뷰 코멘트 — 실제로 렌더해서 본다 ─────────────────────────────
#
#     소스만 읽으면 머리글이 변수로 조립될 때 놓친다. 코멘트를 통째로 만들어 확인한다.
rendered = AR.render(
    merged={"verdict": "warn",
            "checks": {k: {"status": "warn", "why": "근거"} for k in AR.CHECK_LABELS},
            "findings": [], "questions": []},
    mr_meta="`cli/x` · 커밋 `abc1234`", roles=["scope"], results={}, raw={},
    summary=None, skipped=[], failed=[], defaults={})
assert DEAD not in rendered, f"AI 리뷰 코멘트에 없어진 `{DEAD}` 가 남아 있다"

#     다섯 항목 자체는 살아 있어야 한다 — 이름만 바꾸려다 표를 지우는 실수를 막는다.
for label in AR.CHECK_LABELS.values():
    assert label in rendered, f"판정표에서 `{label}` 이 사라졌다"
print(f"  AI 리뷰 판정표 {len(AR.CHECK_LABELS)}항목 그대로 · 옛 절 이름 없음 ✅")
ok += 1


# ── ④ mr_lint 는 예외다 ─────────────────────────────────────────────────
#
#     여기에 옛 이름이 **있어야** 한다. 위 규칙을 저장소 전체로 넓히면 이 안내가
#     같이 지워지고, 옛 양식으로 올린 사람은 무엇을 고쳐야 하는지 못 듣는다.
#     여기서도 **인쇄되는 문자열만** 본다(`!431` 리뷰 [낮음]). 소스를 통째로 grep 하면
#     `mr_lint` 위쪽 주석에 옛 이름이 있어서, 정작 사람에게 나가는 안내 문구를 지워도
#     검사가 조용히 통과한다 — ⓐ 에서 피한 함정과 같은 것이다.
import mr_lint  # noqa: E402
assert DEAD in literals(ast.parse(inspect.getsource(mr_lint))), (
    f"`mr_lint` 가 옛 이름(`{DEAD}`)으로 안내하는 문구가 사라졌다. 옛 양식으로 올린 "
    "사람은 무엇을 고쳐야 하는지 못 듣는다. **안내를 일부러 걷어낸 것이라면** — 옛 "
    "양식의 MR 이 더는 없다고 판단했다면 — 이 단언도 같이 지워라")
print("  `mr_lint` 의 옛 이름 안내는 그대로 (예외) ✅")
ok += 1

print(f"통과 {ok}/4")
