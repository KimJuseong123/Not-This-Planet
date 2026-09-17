"""검사 대상이 같을 때만 Unity 를 건너뛰는가 (S15P21B202-602).

실행: python scripts/ci/tests/test_unity_reuse.py
네트워크·Unity 없이 저장소의 실제 소스와 `.gitlab-ci.yml` 을 읽는다.

**무엇을 막는 검사인가.** 이 최적화는 틀리면 **검사하지 않은 코드가 초록으로 지나간다.**
그래서 지키는 것이 두 방향이다.

  · 건너뛰면 안 되는데 건너뛴다 → 회귀가 그대로 머지된다 (치명적)
  · 건너뛰어도 되는데 안 건너뛴다 → 6분 낭비 (아깝지만 안전하다)

전자를 막기 위해 **모르면 다시 돌린다**(fail-closed)를 값으로 못박고, 후자는 실측으로
확인한다. 그리고 무거운 테스트를 MR 에서 빼는 쪽도 여기서 함께 잠근다 — **develop 에서도
빠지면 그 21건은 아무 데서도 안 돈다.**

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 통째로 사라진다.
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))

os.environ.setdefault("CI_PROJECT_ID", "1")

import unity_reuse as U  # noqa: E402

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


def read(rel):
    with io.open(os.path.join(ROOT, *rel.split("/")), encoding="utf-8") as f:
        return f.read()


print()
print("== unity 재사용 게이트 ==")


# ── ① 모르면 다시 돌린다 ────────────────────────────────────────────────
#
#     `None` 은 "물어봤는데 대답을 못 들었다" 다. 그것을 `False`(안 바뀌었다)로 읽으면
#     git 이 한 번 삐끗할 때 **검사를 통째로 건너뛴다.**
for branch, base, why in [
    (None, False, "브랜치 판단 불가"),
    (False, None, "base 판단 불가"),
    (None, None, "둘 다 판단 불가"),
]:
    reuse, reason = U.decide(branch, base)
    check(reuse is False, f"{why} 인데 재사용한다 — 검사 안 한 코드가 초록으로 지나간다")
    check("판단하지" in reason, f"{why} 의 이유가 '바뀌었다' 와 구분되지 않는다: {reason}")

# 바뀌었으면 당연히 다시 돌린다. 이유도 갈라 적는다 — 로그를 읽는 사람이
# "바빠서 돌았다" 와 "몰라서 돌았다" 를 구분할 수 있어야 한다.
reuse, reason = U.decide(True, False)
check(reuse is False and "브랜치" in reason, f"브랜치가 바뀌었는데 재사용한다: {reason}")
reuse, reason = U.decide(False, True)
check(reuse is False and "develop" in reason, f"develop 이 바뀌었는데 재사용한다: {reason}")

# 둘 다 그대로일 때만 재사용한다.
reuse, reason = U.decide(False, False)
check(reuse is True, f"둘 다 그대로인데 다시 돈다 — 이 이슈가 없애려던 6분이다: {reason}")


# ── ② 무엇을 '입력' 으로 보는가 ─────────────────────────────────────────
#
#     Unity 가 읽는 것만으로는 부족하다. **검사하는 쪽**이 바뀌어도 결과가 달라진다 —
#     잡 정의(`.gitlab-ci.yml`)와 판정 스크립트(`unity_report.py`)가 그것이다.
for must in ("METAGAME/Assets", "METAGAME/Packages", "METAGAME/ProjectSettings",
             ".gitlab-ci.yml", "scripts/ci/unity_report.py"):
    check(must in U.INPUTS, f"입력 집합에 {must} 가 없다 — 그것만 바뀐 push 를 건너뛰게 된다")

# 같은 sha 면 git 을 부르지 않고 '안 바뀌었다' 로 답한다(비용 0).
check(U.git_changed("abc", "abc") is False, "같은 sha 인데 바뀌었다고 한다")
# 한쪽이 비면 판단하지 못한 것이다 — 빈 문자열을 '안 바뀌었다' 로 읽으면 안 된다.
check(U.git_changed("", "abc") is None, "빈 sha 를 '안 바뀌었다' 로 읽는다")
check(U.git_changed("abc", "") is None, "빈 sha 를 '안 바뀌었다' 로 읽는다")
# 없는 커밋을 물으면 git 이 실패한다 → None(판단 불가)이어야 한다.
check(U.git_changed("0" * 40, "1" * 40) is None, "git 실패를 '안 바뀌었다' 로 읽는다")


# ── ③ 잡 배선 ───────────────────────────────────────────────────────────
CI = read(".gitlab-ci.yml")
m = re.search(r"(?ms)^unity-compile:.*?(?=^[a-z][a-z0-9-]*:|^# ──|\Z)", CI)
if not m:
    raise SystemExit("❌ unity-compile 잡을 못 찾았다")
JOB = m.group(0)
CODE = "\n".join(l for l in JOB.splitlines() if not l.lstrip().startswith("#"))

#     합친 base 를 남기지 않으면 **다음 실행이 영원히 재사용하지 못한다** — 최적화가
#     조용히 꺼진 상태가 되고, 아무도 알아채지 못한다.
check("unity-base.txt" in CODE, "합친 base 를 파일로 남기지 않는다 — 다음 실행이 판단할 수 없다")
check("unity_reuse.py" in CODE, "재사용 판단을 부르지 않는다")
check(re.search(r"(?m)^\s*paths:.*unity-base\.txt", JOB, re.S)
      or "unity-base.txt" in JOB.split("artifacts:")[-1],
      "unity-base.txt 가 아티팩트에 없다 — 다음 실행이 받아 갈 수 없다")

#     순서: base 를 쓰고 → 판단하고 → 그 결과로 Unity 를 건너뛴다.
i_base = CODE.find("unity-base.txt")
i_reuse = CODE.find("unity_reuse.py")
i_skip = CODE.find("unity-reused.txt")
i_unity = CODE.find("unity-editor")
check(-1 < i_base < i_reuse < i_skip < i_unity,
      f"재사용 판단 순서가 어긋났다 (base {i_base} · 판단 {i_reuse} · 분기 {i_skip} · unity {i_unity})")


# ── ④ 무거운 테스트는 **어딘가에서는** 돈다 ─────────────────────────────
#
#     MR 에서 빼는 것이 이 이슈의 결정이다. 그런데 develop 쪽에서도 빠지면 그 21건은
#     **아무 데서도 안 돈다** — 검사가 있는데 아무것도 안 지키는 상태가 된다.
check('-testCategory "!Heavy"' in CODE, "무거운 테스트를 MR 에서 빼는 배선이 없다")

merge_only = re.search(r'(?ms)if \[ "\$CI_PIPELINE_SOURCE" = "merge_request_event" \];(.*?)\bfi\b', CODE)
check(merge_only is not None, "Heavy 제외가 MR 조건 안에 있는지 확인할 수 없다")
if merge_only:
    body = merge_only.group(1)
    head, _, tail = body.partition("else")
    check('-testCategory' in head, "Heavy 제외가 MR 쪽 가지에 없다")
    check('-testCategory' not in tail,
          "develop 쪽에서도 Heavy 를 뺀다 — 그러면 그 21건이 아무 데서도 안 돈다")

#     "덜 돌았다" 를 코멘트가 말해야 한다. 안 적으면 초록이 '전부 돌았다' 로 읽힌다.
check("UNITY_SKIPPED_CATEGORY" in CODE, "무거운 테스트를 뺀 사실을 코멘트에 넘기지 않는다")
REPORT = read("scripts/ci/unity_report.py")
check("UNITY_SKIPPED_CATEGORY" in REPORT, "판정 스크립트가 그 사실을 읽지 않는다")
check("UNITY_REUSED_FROM" in REPORT, "판정 스크립트가 재사용 사실을 읽지 않는다")

#     실제로 붙어 있어야 필터가 뜻을 가진다. 21건은 실측값이고 테스트는 늘고 줄므로
#     정확한 수가 아니라 **하한**을 본다 — 전부 떼어 내면 여기서 걸린다.
TESTS = os.path.join(ROOT, "METAGAME", "Assets", "_Project", "Scripts", "Tests", "Editor")
marked = 0
for name in os.listdir(TESTS):
    if name.endswith(".cs"):
        with io.open(os.path.join(TESTS, name), encoding="utf-8") as f:
            marked += f.read().count('[Category("Heavy")]')
check(marked >= 15, f"Heavy 로 표시된 테스트가 {marked}건뿐이다 — 필터가 거의 아무것도 안 뺀다")
print(f"  Heavy 표시 {marked}건 · 입력 {len(U.INPUTS)}종")


if fails:
    print()
    for f in fails:
        print("❌ " + f)
    raise SystemExit(1)

print("  모르면 다시 돌린다 · 둘 다 그대로일 때만 건너뛴다 ✅")
print("  무거운 테스트는 MR 에서만 빠지고 develop 에서 전부 돈다 ✅")
