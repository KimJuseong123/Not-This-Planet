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


# ── ②-b 합칠 대상이 없는 것과 모르는 것을 가른다 (S15P21B202-641) ────────
#
#     빈 문자열로 뭉쳐 두었을 때 **develop 에서는 재사용이 한 번도 안 됐다.**
#     develop push 는 합칠 대상이 없어 `unity-base.txt` 가 빈 줄이 되는데, 읽는 쪽은
#     그것을 "이 기능 이전 잡이라 모른다" 로 읽고 매번 다시 돌렸다(잡 `578864` 로그).
#
#     `-641` 이 develop 의 `changes:` 를 없애므로 문서만 바꾼 머지도 이 잡이 생긴다.
#     그 비용을 받아 주는 것이 재사용이라, **먼저 이것이 돌아야** 그 변경이 값싸다.
check(U.NO_MERGE, "합칠 대상 없음을 나타내는 값이 없다")
check(U.NO_MERGE.strip() != "", "합칠 대상 없음이 빈 문자열이다 — '모른다' 와 구별되지 않는다")

#     둘 다 "합칠 것 없음" 이면 **그 조건은 이미 만족**이다(비교할 것이 없다).
check(U.git_changed(U.NO_MERGE, U.NO_MERGE) is False,
      "develop 끼리인데 base 가 바뀌었다고 본다 — 재사용이 영영 안 된다")

#     한쪽만 "없음" 이면 상태가 달라진 것이다 — git 이 실패하고 다시 돈다.
check(U.git_changed(U.NO_MERGE, "0" * 40) is None, "없음↔sha 를 '안 바뀌었다' 로 읽는다")

#     옛 잡(빈 값)은 여전히 "모른다" 다.
check(U.git_changed("", U.NO_MERGE) is None, "옛 잡의 빈 값을 '없음' 과 같게 본다")

#     재는 쪽도 같이 고쳐져 있어야 한다 — 한쪽만 고치면 영영 안 맞는다.
check('UNITY_MERGED_BASE:--' in read(".gitlab-ci.yml"),
      "합칠 대상이 없을 때 표시를 안 남긴다 — develop 재사용이 계속 꺼진 채다")


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


# ── ⑤ 다른 ref 의 같은 트리를 빌려 온다 (S15P21B202-642) ─────────────────
#
#     `release-windows` 가 빌드 전에 EditMode 를 스스로 돌린다. 2026-09-18 실측
#     **20분 20초**로 릴리스의 82% 였고, 결과는 같은 트리를 검사한 develop 의
#     `unity-compile` 과 실패 수까지 같았다(26 = 26).
#
#     **범위를 반드시 맞춘다.** MR 은 `!Heavy` 로 빼고 도는데 그 결과를 전체 실행
#     자리에 쓰면 Heavy 가 검사되지 않은 채 초록이 된다 — 이 파일의 ④ 가 지키려는
#     것을 재사용이 뒷문으로 무너뜨리는 모양이다.
check(U.category.__doc__, "category() 에 설명이 없다")
os.environ["CI_PIPELINE_SOURCE"] = "merge_request_event"
check(U.category() == U.CAT_NO_HEAVY, f"MR 인데 범위가 {U.category()} 다")
os.environ["CI_PIPELINE_SOURCE"] = "push"
check(U.category() == U.CAT_FULL, f"push 인데 범위가 {U.category()} 다")
os.environ.pop("CI_PIPELINE_SOURCE", None)

#     지문은 **검사 대상 경로 전부**를 담아야 한다. 하나라도 빠지면 그것만 바뀐 트리를
#     같다고 말한다.
check(U.tree_key(["METAGAME/Assets"]) != U.tree_key(U.INPUTS),
      "입력 일부만으로 만든 지문이 전체와 같다 — 경로가 지문에 안 들어간다")
check(U.tree_key(["없는/경로"]) is None, "읽지 못한 경로를 지문으로 만들었다 — 모르면 None 이어야 한다")


class _FakeGL:
    """`find_same_tree` 만 보기 위한 최소 가짜. 아티팩트는 잡 id → 한 줄."""
    token = "x"
    project = "1"

    def __init__(self, jobs, arts):
        self._jobs, self._arts = jobs, arts

    def project_jobs(self):
        return self._jobs

    def job_artifact_text(self, jid, name):
        return self._arts.get((jid, name))


def _job(jid, ref, status="success"):
    return {"id": jid, "name": U.JOB_NAME, "ref": ref, "status": status}


JOBS = [_job(30, "develop"), _job(20, "refs/merge-requests/9/head"), _job(10, "develop")]
ARTS = {(30, U.KEY_FILE): "aaaa full", (20, U.KEY_FILE): "aaaa no-heavy",
        (10, U.KEY_FILE): "bbbb full"}
gl = _FakeGL(JOBS, ARTS)

got = U.find_same_tree(gl, "aaaa", U.CAT_FULL)
check(got and got["id"] == 30, f"다른 ref 의 같은 트리를 못 찾았다: {got}")

#     **범위가 다르면 쓰지 않는다.** 여기서 20번(no-heavy)을 집으면 Heavy 가 샌다.
got = U.find_same_tree(_FakeGL([_job(20, "refs/merge-requests/9/head")], ARTS),
                       "aaaa", U.CAT_FULL)
check(got is None, f"범위가 다른 잡을 집었다: {got} — Heavy 가 검사되지 않고 지나간다")

#     지문이 다르면 그대로 돈다(핫픽스로 갈라진 경우).
check(U.find_same_tree(gl, "cccc", U.CAT_FULL) is None, "없는 지문에 아무거나 집었다")

#     지문 파일이 없는 옛 잡은 건너뛴다.
check(U.find_same_tree(_FakeGL(JOBS, {}), "aaaa", U.CAT_FULL) is None,
      "지문이 없는 잡을 같다고 봤다")

#     도는 중·취소된 잡의 아티팩트는 반쪽이다.
check(U.find_same_tree(_FakeGL([_job(30, "develop", "canceled")], ARTS),
                       "aaaa", U.CAT_FULL) is None, "취소된 잡을 집었다")

# ── ⑤-b 배선: 지문을 남기고, 릴리스가 그것을 쓴다 ────────────────────────
check("unity_reuse.py key" in CODE, "unity-compile 이 트리 지문을 남기지 않는다")
check("unity-key.txt" in JOB.split("artifacts:")[-1],
      "unity-key.txt 가 아티팩트에 없다 — 다른 ref 가 받아 갈 수 없다")
#     **머지 뒤라야 한다.** 머지 전에 남기면 소스 브랜치의 트리를 가리켜 다른 것을
#     같다고 말한다.
check(-1 < CODE.find("unity-base.txt") < CODE.find("unity_reuse.py key"),
      "지문을 머지 결과보다 먼저 남긴다 — 검사 대상과 다른 트리를 가리킨다")

rw = re.search(r"(?ms)^release-windows:.*?(?=^[a-z][a-z0-9-]*:|^# ──|\Z)", CI)
check(rw is not None, "release-windows 잡을 못 찾았다")
if rw:
    RW = "\n".join(l for l in rw.group(0).splitlines() if not l.lstrip().startswith("#"))
    check("unity_reuse.py borrow" in RW, "릴리스가 지난 결과를 빌려 오지 않는다")
    check("unity-reused.txt" in RW, "릴리스가 재사용 여부로 갈라지지 않는다")
    #  빌려 왔는데도 Unity 를 돌리면 아낀 것이 없다 — 분기가 editor 호출을 감싸야 한다.
    i_b, i_mark, i_ed = (RW.find("unity_reuse.py borrow"), RW.find("unity-reused.txt"),
                         RW.find("unity-editor"))
    check(-1 < i_b < i_mark < i_ed,
          f"릴리스의 재사용 순서가 어긋났다 (빌림 {i_b} · 분기 {i_mark} · unity {i_ed})")
# ── ⑥ develop 실행을 합친다 — **셋이 한 묶음이다** (S15P21B202-641) ──────
#
#     2026-09-18 에 머지가 몰리며 develop 의 `unity-compile` 이 8건까지 쌓였다.
#     앞의 실행은 뒤의 실행에 포함되는데도 각각 돌았고, `resource_group: unity` 로
#     직렬이라 그 줄이 MR 검사와 릴리스를 통째로 막았다.
#
#     **하나라도 빠지면 지금보다 나쁜 상태가 된다.** 자동 취소만 켜고 ①②를 빼면
#     검사되지 않은 변경이 develop 으로 조용히 들어간다 — `-309` 가 겪은 그 방식이다.
WF = re.search(r"(?ms)^workflow:.*?(?=^[a-z][a-z0-9-]*:|^# ──|\Z)", CI)
check(WF is not None, "workflow 블록을 못 찾았다")
if WF:
    dev_rule = re.search(
        r'(?ms)- if: \$CI_PIPELINE_SOURCE == "push" && \$CI_COMMIT_BRANCH == "develop"'
        r'(.*?)(?=^    - if:|\Z)', WF.group(0))
    check(dev_rule is not None, "workflow 에 develop push 규칙이 없다")
    if dev_rule:
        check("on_new_commit: interruptible" in dev_rule.group(1),
              "develop 의 자동 취소가 꺼져 있다 — 머지가 몰리면 Unity 실행이 그대로 쌓인다")

#     ① 백엔드 잡은 **어떤 경우에도** 취소되지 않는다.
BB = re.search(r"(?ms)^build-backend:.*?(?=^[a-z][a-z0-9-]*:|^# ──|\Z)", CI)
check(BB is not None, "build-backend 잡을 못 찾았다")
if BB:
    bb = "\n".join(l for l in BB.group(0).splitlines() if not l.lstrip().startswith("#"))
    check("interruptible: false" in bb,
          "build-backend 가 취소될 수 있다 — 뒤 머지가 그것을 죽이면 배포 버튼이 사라진다(-309)")

#     ② develop 쪽 `unity-compile` 에는 `changes:` 가 없다.
#        있으면 문서만 바꾼 머지의 파이프라인에 이 잡이 없고, 그것이 앞의 Unity
#        실행을 취소해 **그 변경이 develop 에서 한 번도 검사되지 않는다.**
dev_unity = re.search(
    r'(?ms)- if: \$CI_PIPELINE_SOURCE == "push" && \$CI_COMMIT_BRANCH == "develop"'
    r'(.*?)(?=^  [a-z]|\Z)', JOB)
check(dev_unity is not None, "unity-compile 에 develop push 규칙이 없다")
if dev_unity:
    tail = "\n".join(l for l in dev_unity.group(1).splitlines()
                     if not l.lstrip().startswith("#"))
    check("changes:" not in tail,
          "develop 의 unity-compile 에 changes: 가 있다 — 문서만 바꾼 머지가 앞의 "
          "Unity 실행을 취소하면 그 변경이 검사 없이 지나간다(-641 ②)")
print("  develop 실행 합치기: 자동 취소 · 백엔드 비취소 · develop changes 없음 ✅")


if fails:
    print()
    for f in fails:
        print("❌ " + f)
    raise SystemExit(1)

print("  모르면 다시 돌린다 · 둘 다 그대로일 때만 건너뛴다 ✅")
print("  무거운 테스트는 MR 에서만 빠지고 develop 에서 전부 돈다 ✅")
