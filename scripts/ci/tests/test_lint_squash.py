"""squash 는 모든 MR 에서 꺼져 있어야 한다 (S15P21B202-417).

실행: python scripts/ci/tests/test_lint_squash.py
네트워크 없이 돈다 — `check_squash` 는 가짜 Report 와 사전을 받는다.

지키려는 것
  · **대상과 무관하게 검사한다.** 예전에는 `is_release_mr` 블록 **안**에 있어서
    develop 대상 MR 에서는 검사가 아예 돌지 않았다(`-325` 시절 규칙).
  · **스택의 부모가 squash 되는 것을 막는다.** squash 는 부모의 커밋을 버리고 같은
    내용의 새 커밋을 만든다 → 자식과 develop 의 조상 관계가 끊긴다 → 머지 베이스가
    뒤로 밀린다 → 자식을 리베이스할 때 git 이 **부모의 커밋까지 다시 재생**한다.
    그 사이 누가 같은 줄을 고쳐 놨으면 충돌이다(2026-09-10 재현).
  · **이유를 말한다.** "켜져 있음" 만으로는 무엇을 해야 하는지 모른다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import mr_lint as L


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


def judge(squash, target="develop"):
    rep = FakeReport()
    L.check_squash(rep, {"squash": squash}, target)
    return rep


# ── 대상과 무관하게 막는다 ────────────────────────────────────────────────
for target in ("develop", "master", "S15P21B202-1-x", "", None):
    rep = judge(True, target)
    assert len(rep.fails) == 1, f"{target!r}: {rep}"
    assert not rep.oks, f"{target!r}: {rep}"
print("  squash 가 켜져 있으면 대상과 무관하게 실패 ✅")
print("     (예전에는 릴리즈 블록 안이라 develop 대상은 검사조차 안 됐다)")

for target in ("develop", "master", "S15P21B202-1-x"):
    rep = judge(False, target)
    assert rep.fails == [] and len(rep.oks) == 1, f"{target!r}: {rep}"
print("  꺼져 있으면 통과 ✅")


# ── 값이 없거나 거짓 같은 것도 '꺼짐' 이다 ────────────────────────────────
# GitLab 응답에 키가 없을 수 있다. 그때 실패시키면 아무도 머지할 수 없다.
for missing in (None, False, 0, ""):
    rep = FakeReport()
    L.check_squash(rep, {"squash": missing} if missing is not None else {}, "develop")
    assert rep.fails == [], f"{missing!r} 를 켜진 것으로 봤다: {rep}"
print("  값이 없거나 거짓이면 꺼진 것으로 본다 ✅  (없다고 막으면 아무도 못 머지한다)")


# ── 이유를 말한다 ────────────────────────────────────────────────────────
msg = judge(True, "develop").fails[0]
assert "스택" in msg, msg
assert "S15P21B202-417" in msg, msg
assert "mr-title" in msg, "자동으로 꺼진다는 것과, 그래도 켜져 있으면 무엇을 볼지 말해야 한다"
assert "GITLAB_TOKEN" in msg, msg
print("  실패 메시지가 이유·자동 해제·확인할 것을 말한다 ✅")

# master 대상은 그렇다고 말해 준다 — 릴리즈에서는 영향이 더 크다.
assert "master" in judge(True, "master").fails[0]
print("  master 대상이면 그렇다고 밝힌다 ✅")


# ── 릴리즈 조기 종료보다 앞에서 불린다 ───────────────────────────────────
# `is_release_mr` 블록은 return 으로 끝난다. 그 뒤에 두면 릴리즈 MR 에서만 돌던
# 예전 상태로 되돌아간다.
src = open(os.path.join(os.path.dirname(HERE), "mr_lint.py"), encoding="utf-8").read()
call = src.index("check_squash(rep, mr, target)")
release = src.index("if is_release_mr(branch, target):")
assert call < release, "check_squash 가 릴리즈 조기 종료 뒤에 있다 — 그러면 릴리즈 MR 만 검사한다"
print("  릴리즈 조기 종료보다 앞에서 불린다 ✅")


print("\ntest_lint_squash: 통과")
