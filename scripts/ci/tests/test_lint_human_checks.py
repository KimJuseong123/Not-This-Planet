"""머지 전에 사람이 해야 하는 확인 (S15P21B202-395).

실행: python scripts/ci/tests/test_lint_human_checks.py
네트워크 없이 돈다 — `human_checks_content` 는 순수 함수이고 `check_human_checks` 는
가짜 Report 를 받는다.

지키려는 것
  · **도장 찍기가 통과하지 않는다.** 예전에는 `작성자 체크리스트` 상자 5개를 전부
    체크하라고 했는데, 그중 4개는 CI 가 이미 검사한다(merge_status·mr-overlap·
    pre-push 훅 / unity-compile / repo-hygiene). 기계가 증명한 것을 사람에게 다시
    단정하라고 하니 한계 정보가 0 이고, 그래서 아무도 읽지 않고 찍었다.
  · **정직이 막히지 않는다.** 남은 하나(Jira 완료 조건)는 올리는 시점에 대개 거짓이라,
    비워 두면 lint 가 막고 찍으면 통과했다 — 인센티브가 뒤집혀 있었다. 2026-09-10 에
    실제로 그랬다(`!290` AI 리뷰가 "미수행인데 체크돼 있다" → 풀었더니 `!295`·`!297`
    이 막혔다).
  · **템플릿을 그대로 두고 올리면 통과하지 않는다.** 주석만 남은 절은 빈 절이다.
  · **규칙을 바꾸는 순간 열려 있던 MR 이 전부 빨간불이 되지 않는다.** 옛 절은 경고다 —
    규칙이 아니라 사람이 막히면 안 된다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import mr_lint as L
from ci_common import sections


class FakeReport:
    """`Report` 의 세 메서드만 흉내낸다."""

    def __init__(self):
        self.oks, self.warns, self.fails = [], [], []

    def ok(self, msg):
        self.oks.append(msg)

    def warn(self, msg):
        self.warns.append(msg)

    def fail(self, msg):
        self.fails.append(msg)

    def __repr__(self):
        return f"ok={self.oks} warn={self.warns} fail={self.fails}"


def judge(body: str) -> FakeReport:
    rep = FakeReport()
    L.check_human_checks(rep, sections(body))
    return rep


NEW = "## " + L.HUMAN_CHECKS_SECTION
OLDS = ["## " + name for name in L.LEGACY_CHECK_SECTIONS]


# ── 내용을 골라내는 규칙 ──────────────────────────────────────────────────
assert L.human_checks_content(None) == []
assert L.human_checks_content("") == []
assert L.human_checks_content("\n\n  \n") == []
print("  빈 절은 내용 0줄 ✅")

only_comment = """<!-- ★ 기계가 못 보는 것만 적는다. 없으면 "없음".
     예: "3인 세션에서 참가자→호스트 일으키기" -->
"""
assert L.human_checks_content(only_comment) == [], L.human_checks_content(only_comment)
print("  주석만 남은 절은 내용 0줄 ✅  (템플릿을 그대로 두면 통과하지 않는다)")

assert L.human_checks_content("-\n*\n  -  \n") == []
print("  목록 기호만 있는 줄은 내용이 아니다 ✅")

mixed = """<!-- 안내 -->
- 3인 세션에서 참가자→호스트 일으키기
- 빌드해서 창 제목 확인
"""
assert L.human_checks_content(mixed) == ["3인 세션에서 참가자→호스트 일으키기",
                                         "빌드해서 창 제목 확인"], L.human_checks_content(mixed)
print("  주석을 걷고 항목만 남긴다 ✅")

inline = "설명 <!-- 곁주석 --> 뒤에도 내용\n"
assert L.human_checks_content(inline) == ["설명  뒤에도 내용"], L.human_checks_content(inline)
print("  한 줄 안의 주석도 걷는다 ✅")

spanning = """앞줄
<!-- 여러
줄 주석 -->
뒷줄
"""
assert L.human_checks_content(spanning) == ["앞줄", "뒷줄"], L.human_checks_content(spanning)
print("  여러 줄 주석도 걷는다 ✅")


# ── 판정 ─────────────────────────────────────────────────────────────────
rep = judge(f"{NEW}\n- 3인 세션에서 일으키기 확인\n")
assert rep.fails == [] and len(rep.oks) == 1, rep
print("  적었으면 통과 ✅")

rep = judge(f"{NEW}\n없음\n")
assert rep.fails == [] and len(rep.oks) == 1, rep
print("  `없음` 도 통과 ✅  (내용 있는 주장이다)")

rep = judge(f"{NEW}\n{only_comment}")
assert len(rep.fails) == 1 and "비어 있음" in rep.fails[0], rep
print("  주석만 남기면 실패 ✅")

rep = judge(f"{NEW}\n\n")
assert len(rep.fails) == 1, rep
print("  빈 절은 실패 ✅")


# ── 이행 — 옛 절은 경고 ──────────────────────────────────────────────────
# 옛 이름이 둘이다 — `작성자 체크리스트`(-395 이전)와
# `머지 전에 사람이 해야 하는 확인`(-419 이전). 둘 다 경고여야 한다.
for OLD in OLDS:
    legacy_all_checked = f"""{OLD}
- [x] `develop` 최신을 반영했고 충돌이 없다
- [x] Jira 완료 조건을 모두 충족했다
"""
    rep = judge(legacy_all_checked)
    assert rep.fails == [] and len(rep.warns) == 1, f"{OLD}: {rep}"
    assert "S15P21B202-419" in rep.warns[0], rep.warns[0]

    legacy_unchecked = f"""{OLD}
- [x] `develop` 최신을 반영했고 충돌이 없다
- [ ] Jira 완료 조건을 모두 충족했다
"""
    rep = judge(legacy_unchecked)
    assert rep.fails == [], f"{OLD}: 정직하게 비워 둔 옛 MR 이 막힌다: {rep}"
    assert len(rep.warns) == 1, f"{OLD}: {rep}"

    # 옛 절에 문장만 적어 둔 경우(=-419 이전 템플릿의 기본값 "없음")도 경고다.
    rep = judge(f"{OLD}\n없음\n")
    assert rep.fails == [], f"{OLD}: {rep}"
print(f"  옛 절 {len(OLDS)}가지 모두 경고이고 막지 않는다 ✅  (열려 있던 MR 이 빨간불이 되지 않는다)")


# ── 절이 아예 없으면 실패 ────────────────────────────────────────────────
rep = judge("## 무엇을 · 왜\n무언가\n")
assert len(rep.fails) == 1 and L.HUMAN_CHECKS_SECTION in rep.fails[0], rep
print("  절을 지우면 실패 ✅")


# ── 새 절이 옛 절보다 우선한다 ───────────────────────────────────────────
for OLD in OLDS:
    rep = judge(f"{NEW}\n없음\n\n{OLD}\n- [ ] 남아 있는 옛 줄\n")
    assert rep.fails == [] and rep.warns == [], f"{OLD}: {rep}"
print("  둘 다 있으면 새 절로 판정한다 ✅  (옮기는 중인 MR 이 경고까지 받지 않는다)")


# ── 필수 섹션 목록에서 빠졌는지 ──────────────────────────────────────────
for name in L.LEGACY_CHECK_SECTIONS:
    assert name not in L.REQUIRED_SECTIONS, \
        f"옛 절({name})이 필수로 남아 있으면 새 템플릿이 실패한다"
assert L.HUMAN_CHECKS_SECTION not in L.REQUIRED_SECTIONS, \
    "새 절은 check_human_checks 가 따로 본다 — 필수 목록에 넣으면 옛 MR 이 막힌다"

# **없앤 절이 필수로 남아 있으면 새 템플릿이 통과하지 못한다** (S15P21B202-419).
for gone in ("확인 방법", "산출물 위치", "리뷰어 확인", "씬 / 프리팹 변경", "스크린샷 / 영상"):
    assert gone not in L.REQUIRED_SECTIONS, f"없앤 절이 필수에 남아 있다: {gone}"
print("  필수 섹션 목록이 이행을 막지 않고, 없앤 절도 요구하지 않는다 ✅")


# ── 템플릿 자신이 새 절을 쓰는지 ─────────────────────────────────────────
# scripts/ci/tests → scripts/ci → scripts → 저장소 루트 (세 단)
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
tpl = os.path.join(REPO, ".gitlab", "merge_request_templates", "Default.md")
with open(tpl, encoding="utf-8") as f:
    template = f.read()

assert NEW in template, "템플릿이 새 절을 쓰지 않는다"
for OLD in OLDS:
    assert OLD not in template, f"템플릿에 옛 절이 남아 있다: {OLD}"
assert "- [ ]" not in template, "템플릿에 상자가 남아 있다 — 무의미한 클릭이 다시 생긴다"

# **4절이다** (S15P21B202-419). 없앤 절이 다시 들어오면 같은 질문의 판본이 늘어난다.
heads = [ln[3:].strip() for ln in template.splitlines() if ln.startswith("## ")]
assert heads == ["Jira", "무엇을 · 왜", L.HUMAN_CHECKS_SECTION, "남은 것"], heads
print(f"  템플릿이 4절이다 ✅  {heads}")

# **주석 안에는 마크다운 강조를 쓰지 않는다** — 렌더되지 않아 별표가 그대로 보인다.
import re as _re
for block in _re.findall(r"<!--(.*?)-->", template, _re.S):
    assert "**" not in block, f"주석 안에 강조 표기가 있다: {block[:80]}"
print("  주석 안에 마크다운 강조가 없다 ✅  (렌더되지 않아 별표가 그대로 보인다)")

# 템플릿을 그대로 올리면 통과해야 한다 — 기본값이 "없음" 이다.
rep = judge(template)
assert rep.fails == [], f"템플릿 그대로가 실패한다: {rep}"
print("  템플릿 기본값(`없음`)이 통과한다 ✅")

# 리뷰어 절은 `-415` 에서 상자를 없앴고, `-419` 에서 절 자체를 없앴다 — `확인 방법`·
# `머지 전에 사람이 해야 하는 확인` 과 함께 `머지 전에 팀장이 확인할 것` 하나로 합쳤다.
# 같은 질문의 세 판본이 셋으로 나뉘어 있는 동안 아무도 읽지 않았다.
assert "## 리뷰어 확인" not in template, "합친 절이 남아 있다"
assert "AI 리뷰" in template, "무엇을 기계가 보는지 절 안에 적혀 있어야 한다"
assert "ai-review-gate" in template, "무엇이 막는지 적혀 있어야 한다"
print("  세 절이 하나로 합쳐졌고 무엇을 기계가 보는지 적혀 있다 ✅")


print("\ntest_lint_human_checks: 통과")
