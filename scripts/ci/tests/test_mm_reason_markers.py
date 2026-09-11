"""CI 실패 카드가 이유를 찾는 규칙 (S15P21B202-416).

실행: python scripts/ci/tests/test_mm_reason_markers.py
네트워크 없이 돈다 — `reasons_from_trace` 와 `clean_reason` 은 순수 함수다.

지키려는 것
  · **`unity-compile` 의 이유가 나온다.** 예전에는 `❌` 한 글자가 코드에 박혀 있었고
    그 잡은 판정을 `🔴` 로 적어서, **가장 자주 실패하는 잡의 이유가 구조적으로 영원히
    안 나왔다.** 2026-09-10 하루에 세 번 "로그에서 이유를 찾지 못함" 카드가 나갔고
    세 번 다 이유가 로그에 있었다.
  · **다른 잡이 회귀하지 않는다.** `mr-lint`·`repo-hygiene` 은 `❌` 를 쓴다.
  · **표가 깨지지 않는다.** 마크다운 굵게가 별표로 새거나 `|` 가 셀을 쪼개면 안 된다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import mm_notify as N


# ── 마커 목록 ─────────────────────────────────────────────────────────────
assert "❌" in N.REASON_MARKERS, "다른 잡이 쓰는 표시가 빠지면 그쪽 이유가 사라진다"
assert "🔴" in N.REASON_MARKERS, "unity-compile 이 쓰는 표시가 없으면 그 잡은 영원히 이유가 없다"
print("  마커 목록에 ❌ 와 🔴 이 둘 다 있다 ✅")


# ── 잡별 로그 ─────────────────────────────────────────────────────────────
UNITY = """===== unity-report =====
JUnit 변환: /builds/x/unity-junit.xml (총 1102 · 실패 34)
기준선: `develop` 실패 33건
🔴 **이 MR 에서 새로 실패하는 테스트 1개**
판정 1 (이 MR 이 만든 문제 — 막는다): 이 MR 이 만든 테스트 실패 1건
unity-compile 종료 1 (0 통과 · 1 이 MR 이 만든 문제 · 2 판단 불가)
"""
got = N.reasons_from_trace(UNITY)
assert got == ["이 MR 에서 새로 실패하는 테스트 1개"], got
print("  🔴 만 있는 로그에서 이유가 나온다 ✅  (이것이 세 번 놓친 그 경우다)")
assert "**" not in got[0], "굵게 표시가 카드에 별표로 샌다"
print("  굵게 표시를 지운다 ✅")

LINT = """===== mr-lint =====
❌ 본문 템플릿 섹션 누락: ## 무엇을 · 왜
✅ 제목 규칙 통과
❌ Jira 이슈를 찾지 못함
"""
got = N.reasons_from_trace(LINT)
assert got == ["본문 템플릿 섹션 누락: ## 무엇을 · 왜", "Jira 이슈를 찾지 못함"], got
print("  ❌ 로그는 그대로 나온다 ✅  (회귀 없음)")

BOTH = """❌ 앞의 실패
🔴 뒤의 판정
"""
assert N.reasons_from_trace(BOTH) == ["앞의 실패", "뒤의 판정"], N.reasons_from_trace(BOTH)
print("  둘 다 있으면 둘 다 나온다 ✅")

assert N.reasons_from_trace("아무 표시도 없는 로그\n두 줄째\n") == []
assert N.reasons_from_trace("") == []
assert N.reasons_from_trace(None) == []
print("  표시가 없으면 빈 목록 ✅  (그때만 '찾지 못함' 이라고 말해야 한다)")


# ── 같은 줄에 마커가 여럿이면 앞의 것으로 자른다 ─────────────────────────
# 뒤의 것으로 자르면 앞부분이 이유에서 사라진다.
got = N.reasons_from_trace("❌ 진짜 이유 🔴 뒤에 붙은 표시\n")
assert got == ["진짜 이유 🔴 뒤에 붙은 표시"], got
print("  한 줄에 여럿이면 가장 앞의 표시로 자른다 ✅")


# ── 같은 이유가 여러 줄에 있으면 한 번만 ─────────────────────────────────
assert N.reasons_from_trace("❌ 같은 이유\n❌ 같은 이유\n") == ["같은 이유"]
print("  중복은 접는다 ✅")


# ── 표를 깨뜨리지 않는다 ─────────────────────────────────────────────────
cell = N.clean_reason(" a | b <tag> **굵게**  여러   칸 ")
assert "|" not in cell, cell
assert "<" not in cell and ">" not in cell, cell
assert "**" not in cell, cell
assert "  " not in cell, cell
print("  표 구분자·태그·굵게·연속 공백을 지운다 ✅")

long_reason = N.clean_reason("가" * (N.MAX_REASON_CHARS + 50))
assert len(long_reason) == N.MAX_REASON_CHARS + 1 and long_reason.endswith("…")
print("  너무 길면 자른다 ✅")


# ── '확인할 것' 이 컴파일이라고 단정하지 않는다 ──────────────────────────
# unity-compile 은 컴파일 + EditMode 테스트를 돌린다. 2026-09-10 세 건은 전부 테스트
# 실패였고 컴파일은 통과했다 — 카드가 컴파일이라고 말하면 엉뚱한 로그를 뒤진다.
#
# **값으로 단정한다.** 첫 판은 mm_notify.py 소스를 훑었는데, 그러면 동작이 아니라 코드
# 표기에 묶여 사전 포맷만 바꿔도 깨진다(`!322` AI 리뷰). 그래서 JOB_HINTS 를 모듈
# 상수로 빼고 여기서 값을 본다.
unity_hint = N.JOB_HINTS["unity-compile"]
assert "테스트" in unity_hint, f"확인할 것에 테스트가 들어가야 한다: {unity_hint!r}"
assert unity_hint != "컴파일 오류", "unity-compile 은 테스트도 돌린다"
print("  확인할 것이 컴파일이라고 단정하지 않는다 ✅")

# 다른 잡의 문구가 사라지면 카드가 잡 이름만 말한다.
for job in ("mr-lint", "repo-hygiene"):
    assert N.JOB_HINTS.get(job), f"{job} 의 안내 문구가 없다"
print("  다른 잡의 안내 문구도 남아 있다 ✅")


print("\ntest_mm_reason_markers: 통과")
