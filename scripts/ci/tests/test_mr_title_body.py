"""뜻 없는 MR 제목이 Jira 요약으로 바뀌는지 (S15P21B202-450).

실행: python scripts/ci/tests/test_mr_title_body.py
네트워크·토큰 없이 `ci_common.http` 와 Jira 조회를 가짜로 바꿔 돌린다.

**무엇을 막는 검증인가.** GitLab 웹의 새 MR 폼은 제목을 **필수**로 받는다. 그래서
"비워두고 싶은데 비울 수 없으니 아무거나 찍는다" 가 자연스러운 행동이 되고, `.` 하나가
그대로 남는다 — 2026-09-11 에 실제로 "그냥 `.` 찍어 두면 알아서 되나" 라는 질문을 받았고
답은 "안 된다" 였다. `mr-lint` 도 형식만 보므로 **조용히 통과**한다.

`-322` 가 제목 보정을 만든 값은 **사람이 규칙을 몰라도 되게** 하는 것이었다. 뜻 없는
값이 그대로 남으면 그 값이 그 자리에서 무너진다.
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
sys.stdout.reconfigure(encoding="utf-8")

os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "1",
                   "JIRA_USER": "x", "JIRA_API_TOKEN": "x", "JIRA_URL": "https://jira.example"})

import ci_common  # noqa: E402

MR: dict = {}
PUTS: list = []


def http(method, url, headers=None, body=None, timeout=30):
    if "/repository/compare" in url:
        return 200, {"commits": []}
    if "/merge_requests/1/changes" in url or url.endswith("/changes"):
        return 200, {"changes": [{"new_path": "scripts/ci/x.py"}]}
    if method == "PUT":
        PUTS.append(dict(body or {}))
        return 200, MR
    return 200, MR


ci_common.http = http
import mr_title as T  # noqa: E402

T.http = http

SUMMARY = "[Infra] 런처가 옛 빌드를 가리킨다"


class FakeJira:
    available = True

    def get_issue(self, key):
        return {"key": key, "fields": {"summary": SUMMARY, "labels": ["Infra"]}}


T.Jira = lambda: FakeJira()


def run(title, branch="S15P21B202-450-empty-title", desc="## Jira\n- 이슈: S15P21B202-450\n"):
    """`mr_title.main()` 을 돌리고 (제안된 제목, 잡 로그) 를 돌려준다."""
    MR.clear()
    MR.update({"title": title, "source_branch": branch, "target_branch": "develop",
               "description": desc, "draft": False, "squash": False,
               "author": {"username": "rmawnsdud"}})
    PUTS.clear()
    buf = io.StringIO()
    old, sys.stdout = sys.stdout, buf
    try:
        rc = T.main()
    finally:
        sys.stdout = old
    assert rc == 0, buf.getvalue()
    log = buf.getvalue()
    put = next((p["title"] for p in PUTS if "title" in p), None)
    return put, log


print()
print("== 뜻 없는 MR 제목 ==")

# ── ① 판정 자체 — 순수 함수로 본다 ────────────────────────────────────────
#
#     목록이 아니라 **두 가지 성질**로 본다: 기호뿐이거나, 아주 짧거나.
#     목록(`wip` `asdf` …)을 만들면 목록에 없는 것이 계속 생겨 늘 뒤쫓게 된다.
for junk in (".", "-", "...", "!!", "??", "x", "ui", "UI", "  .  ", "", "   ", "1",
             "ㅇㅇ", "ㅋㅋㅋ", "ㅎ"):
    assert T.is_meaningless(junk), f"뜻 없는 값을 통과시킨다: {junk!r}"

for real in ("HUD", "수정함", "런처 서명", "잡기 고침", "빌드 스탬프 고침", "fix login"):
    assert not T.is_meaningless(real), f"진짜 제목을 뜻 없다고 본다: {real!r}"

#     **한글 2자는 멀쩡한 낱말이다** (`!387` 리뷰). 영문 2자(`ui`)와 같은 자로 세면
#     `잡기` · `빌드` · `복구` 같은 진짜 제목을 지운다. 글자 종류로 가른다.
for word in ("잡기", "빌드", "복구", "정렬"):
    assert not T.is_meaningless(word), f"한글 낱말을 뜻 없다고 본다: {word!r}"

#     자모는 길이와 무관하게 낱말이 아니다 — 길이 규칙과 **따로** 잡아야 한다.
assert T.is_meaningless("ㅇㅇ") and T.is_meaningless("ㅋㅋㅋㅋ"), "자모 자리표시자를 통과시킨다"

#     **`wip` 는 못 잡는다** — 알고 두는 구멍이다. 목록을 만들지 않기로 한 대가이고,
#     잡으려면 `MEANINGLESS_MAX` 를 올려야 하는데 그러면 진짜 짧은 제목이 더 지워진다.
assert not T.is_meaningless("wip"), "이 검사가 목록 방식으로 바뀌었다 — 결정을 다시 보라"
print("  기호뿐 · 2자 이하를 잡고, 진짜 제목은 건드리지 않는다 ✅")


# ── ② `.` 하나를 찍은 MR — 이 이슈가 시작된 자리 ──────────────────────────
put, log = run(".")
assert put and SUMMARY.split("] ", 1)[1] in put, put
assert "(S15P21B202-450)" in put and put.startswith("[Infra] "), put
print("  '.' → Jira 요약으로 바뀐다 ✅")

#     **무엇을 무엇으로 바꿨는지 로그에 남는다** — 제목이 바뀐 사람이 "내가 쓴 게
#     어디 갔나" 를 잡 로그에서 찾을 수 있어야 한다.
assert "'.'" in log and "Jira 요약으로 바꾼다" in log, log

#     **형식이 깨진 제목에서는 원본과 요약이 다르다.** `LOOSE_RE` 가 뽑아 낸 요약만
#     찍으면 찾는 사람이 자기가 쓴 문자열을 못 본다(`!387` 리뷰).
_, log2 = run("[x] .")
assert "'[x] .'" in log2, f"원본 제목이 로그에 없다: {log2}"
print("  바꾼 내용이 잡 로그에 남는다(원본 제목 포함) ✅")


# ── ③ 이미 형식이 맞는데 **요약만** 뜻 없는 경우 ──────────────────────────
#
#     `[Infra] chore: . (S15P21B202-450)` 은 `TITLE_RE` 를 통과한다. 형식만 보면
#     정상이라 예전에는 그대로 남았다 — 그것이 이 이슈의 실제 증상이다.
put, _ = run("[Infra] chore: . (S15P21B202-450)")
assert put and ": ." not in put, f"형식이 맞으면 요약이 뜻 없어도 그냥 둔다: {put!r}"
assert SUMMARY.split("] ", 1)[1] in put, put
print("  형식은 맞고 요약만 뜻 없는 제목도 바뀐다 ✅")


# ── ④ 회귀 — 진짜 제목은 건드리지 않는다 ──────────────────────────────────
good = "[Infra] fix: 런처가 옛 빌드를 가리키던 것 (S15P21B202-450)"
put, log = run(good)
assert put is None or put == good, f"정상 제목을 바꿨다: {put!r}"
assert "변경 없음" in log, log
print("  정상 제목 → 변경 없음 ✅")


# ── ⑤ 브랜치 이름·이슈 키를 그대로 둔 경우(예전부터 되던 것) ──────────────
for title in ("S15P21B202-450-empty-title", "S15p21b202 450", "S15P21B202-450"):
    put, _ = run(title)
    assert put and SUMMARY.split("] ", 1)[1] in put, f"{title!r} → {put!r}"
print("  브랜치 이름 · 이슈 키도 그대로 바뀐다(회귀 없음) ✅")


# ── ⑥ Jira 요약이 비면 원래 값을 지우지 않는다 ────────────────────────────
#
#     바꿀 것이 없는데 지우면 제목이 `[Infra] chore:  (KEY)` 처럼 빈 채로 남는다.
#     **모르면 건드리지 않는다.**
class EmptySummaryJira(FakeJira):
    def get_issue(self, key):
        return {"key": key, "fields": {"summary": "", "labels": ["Infra"]}}


T.Jira = lambda: EmptySummaryJira()
put, _ = run("x")
assert put and put.endswith("x (S15P21B202-450)"), f"Jira 요약이 없는데 원래 값을 지웠다: {put!r}"
T.Jira = lambda: FakeJira()
print("  Jira 요약이 비면 원래 값을 남긴다 ✅")

print("== 통과 ==")
