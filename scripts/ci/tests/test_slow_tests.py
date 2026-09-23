"""느린 테스트를 재는 쪽이 조용히 틀리지 않는가 (S15P21B202-617).

실행: python scripts/ci/tests/test_slow_tests.py
Unity 없이 저장소의 실제 소스와 `.gitlab-ci.yml` 을 읽는다.

**무엇을 막는 검사인가.** 이 목록은 다음 단계에서 **테스트를 실제로 빼는 데** 쓰인다.
그래서 틀리는 방향이 둘이고, 위험이 다르다.

  · 빼면 안 되는 것을 뺀다 → **검사 안 한 코드가 초록으로 지나간다** (치명적)
  · 빼도 되는 것을 안 뺀다 → MR 이 느리다 (아깝지만 안전하다)

전자를 막는 것이 이 파일의 대부분이다 — 특히 **정규식이 이름을 넘치게 잡는 것**과
**재지 못한 것을 '뺄 것 없음' 으로 읽는 것**이다.

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

import slow_tests as S  # noqa: E402

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


def read(rel):
    with io.open(os.path.join(ROOT, *rel.split("/")), encoding="utf-8") as f:
        return f.read()


print()
print("== 느린 테스트 측정 ==")


# ── ① 고른다 ────────────────────────────────────────────────────────────
CASES = [
    {"cls": "A.Fast", "name": "T1", "time": "0.01"},
    {"cls": "A.Slow", "name": "T2", "time": "9.5"},
    {"cls": "B.Edge", "name": "T3", "time": str(S.THRESHOLD_SECONDS)},   # 경계는 포함
    {"cls": "B.Under", "name": "T4", "time": str(S.THRESHOLD_SECONDS - 0.01)},
    {"cls": "C.Broken", "name": "T5", "time": "알수없음"},                # 못 읽는 값
]
got = S.slow_cases(CASES)
names = [n for _s, n in got]
check("A.Slow.T2" in names, "9.5초가 느린 것으로 안 잡힌다")
check("B.Edge.T3" in names, f"기준({S.THRESHOLD_SECONDS}초)과 같은 것이 빠진다 — 경계를 넣기로 했다")
check("B.Under.T4" not in names, "기준 아래인데 느린 것으로 잡는다")
check("A.Fast.T1" not in names, "0.01초를 느린 것으로 잡는다")

#     **못 읽은 시간은 빼지 않는다.** 0 으로 보면 "빠르다" 가 되어 빠지지 않고,
#     큰 값으로 보면 **검사에서 사라진다.** 안전한 쪽은 빼지 않는 것이다.
check("C.Broken.T5" not in names, "시간을 못 읽은 것을 느린 것으로 단정한다 — 그 테스트가 사라진다")

#     느린 것부터 나와야 사람이 로그에서 위 몇 줄만 봐도 된다.
check(got == sorted(got, key=lambda t: -t[0]), "느린 순으로 정렬하지 않는다")


# ── ② 정규식이 **넘치게 잡지 않는다** ───────────────────────────────────
#
#     이 파일에서 가장 중요한 검사다. `^(?!.*(?:Foo)).*` 처럼 담기만 하면 `Foo` 를 뺄 때
#     `FooBar` 까지 빠진다 — 그러면 **아무도 모르게 검사가 사라진다.**
rx = re.compile(S.exclude_regex(["N.Foo", "N.Bar"]))
check(rx.match("N.Baz") is not None, "빼기로 한 적 없는 이름이 걸러진다")
check(rx.match("N.Foo") is None, "빼기로 한 이름이 안 걸러진다")
check(rx.match("N.FooBar") is not None,
      "`N.Foo` 를 빼면서 `N.FooBar` 까지 뺀다 — 이름이 겹치는 테스트가 조용히 사라진다")
check(rx.match("X.N.Foo") is not None,
      "이름 일부만 같은 것까지 뺀다 — 끝을 `$` 로 묶지 않았다")

#     파라미터화 이름은 정규식 메타문자를 품는다. 이스케이프하지 않으면 그 이름이
#     **다른 것을 잡는 패턴**이 된다.
para = 'S.빌드씬("Assets/Scenes/Main.unity")'
rx2 = re.compile(S.exclude_regex([para]))
check(rx2.match(para) is None, "파라미터화 이름을 못 뺀다 — 이스케이프가 빠졌다")
check(rx2.match('S.빌드씬("Assets/Scenes/Other.unity")') is not None,
      "괄호·따옴표를 정규식으로 해석해 다른 인자까지 뺀다")

#     **뺄 것이 없으면 빈 문자열**이어야 한다. `^(?!(?:)$).*$` 같은 것을 내보내면
#     받는 쪽이 그것을 필터로 넘겨 무엇이 걸릴지 알 수 없다.
check(S.exclude_regex([]) == "", "뺄 것이 없는데 빈 문자열이 아니다 — 받는 쪽이 필터로 넘긴다")


# ── ③ 재지 못한 것을 '뺄 것 없음' 으로 쓰지 않는다 ──────────────────────
#
#     빈 목록을 남기면 받는 쪽은 "뺄 것이 없다" 로 읽고 **전부 돌린다** — 그건 안전하다.
#     위험한 것은 그 반대이므로, 읽기 실패에서 목록 파일을 **건드리지 않는** 것이 맞다.
src = read("scripts/ci/slow_tests.py")
scope = src[src.index("def cmd_measure"):]
scope = scope.split("\ndef ", 1)[0]
check("except Exception" in scope, "결과를 못 읽는 경우를 다루지 않는다")
i_except = scope.index("except Exception")
check("write(" not in scope[i_except:scope.index("return 0", i_except)],
      "읽기에 실패하고도 목록 파일을 쓴다 — 앞 실행의 목록을 빈 것으로 덮는다")


# ── ④ 잡 배선 ──────────────────────────────────────────────────────────
CI = read(".gitlab-ci.yml")
m = re.search(r"(?ms)^unity-compile:.*?(?=^[a-z][a-z0-9-]*:|^# ──|\Z)", CI)
check(m is not None, "unity-compile 잡을 못 찾았다")
JOB = m.group(0) if m else ""
CODE = "\n".join(l for l in JOB.splitlines() if not l.lstrip().startswith("#"))

check("slow_tests.py measure" in CODE, "재는 것을 부르지 않는다")

#     **전체 실행에서만 잰다.** 거르고 돈 실행으로 목록을 만들면 이미 빠진 테스트의
#     시간을 모르므로, 한 번 빠진 것이 영영 빠진 채로 굳는다.
i_sched = CODE.find('"$CI_PIPELINE_SOURCE" = "schedule"')
i_meas = CODE.find("slow_tests.py measure")
check(0 <= i_sched < i_meas,
      "전체 실행 조건 없이 잰다 — 거르고 돈 결과로 목록을 만들면 뺀 것이 영영 굳는다")

#     남기지 않으면 다음 실행이 받아 갈 수 없다 — 최적화가 조용히 꺼진 상태가 된다.
for f in (S.FILTER_FILE, S.LIST_FILE, S.EXPECT_FILE):
    check(f in JOB, f"{f} 가 아티팩트에 없다 — 받는 쪽이 읽을 수 없다")

#     재는 것은 **판정 뒤**여야 한다. 앞에 두면 그 스크립트가 죽을 때 판정이 통째로
#     날아간다 — 보고가 먼저다.
check(CODE.find("unity_report.py") < i_meas,
      "판정보다 먼저 잰다 — 재는 쪽이 죽으면 판정이 사라진다")


print(f"  기준 {S.THRESHOLD_SECONDS:g}초 · 파일 {S.FILTER_FILE}·{S.LIST_FILE}·{S.EXPECT_FILE}")

# ── 받는 쪽 (S15P21B202-701) ────────────────────────────────────────────────
#
# 재는 쪽이 돌고 있었는데 **읽는 쪽이 없어서** 사람이 세 번 손으로 붙였다
# (`-602`·`-611`·`-731`). 아래는 그 읽는 쪽이 조용히 틀리지 않는가를 본다.

# ① 집합은 한 방향으로만 자란다
#
#    매일 그날 느린 것만 빼면 어제 느렸던 테스트가 오늘 4.9초라고 MR 로 돌아온다.
#    시간이 들쭉날쭉해지고, 무엇이 왜 빠졌는지 아무도 모르게 된다.
kept, left = S.merge_monotone(["A", "B"], [(9.0, "C"), (8.0, "A")], cap=5)
check(set(kept) >= {"A", "B"}, f"이전 집합을 잃었다: {kept}")
check("C" in kept, f"새로 잰 것을 안 더했다: {kept}")
check(left == [], f"상한 안인데 남겼다: {left}")

# ② 상한에 걸린 것은 **버리지 않고 돌려준다**
#
#    조용히 자르면 "왜 아직 안 빠지지" 를 아무도 못 본다.
kept, left = S.merge_monotone([], [(9.0, "X"), (8.0, "Y"), (7.0, "Z")], cap=1)
check(kept == ["X"], f"상한을 안 지켰다: {kept}")
check(left == ["Y", "Z"], f"넘친 것을 안 돌려줬다: {left}")

# ③ 머리말이 이름으로 읽히지 않는다
names = S.names_from_list(S.HEADER + "    9.00" + chr(9) + "Cls.Test" + chr(10))
check(names == ["Cls.Test"], f"목록을 되돌리지 못했다: {names}")

# ④ **0건 실행에 초록**을 잡는다 — 이 방식에서 가장 위험한 실패다
check(S.enough(0, 100) is False, "정규식이 다 걸러도 통과시킨다")
check(S.enough(50, 100) is True, "예상의 절반이면 통과여야 한다")
check(S.enough(10, 0) is True, "예상이 없으면 막지 않는다")

# ⑤ 받는 쪽이 `.gitlab-ci.yml` 에 **실제로 배선돼 있다**
#
#    스크립트만 있고 부르는 곳이 없던 것이 `-701` 이 존재한 이유다. 같은 일이
#    다시 나지 않게 값으로 못박는다.
i_apply = CODE.find("slow_tests.py apply")
i_filter = CODE.find("-testFilter")
i_verify = CODE.find("slow_tests.py verify")
check(i_apply >= 0, "목록을 받아 오는 곳이 없다 — 재기만 하고 아무도 안 읽는다")
check(i_filter > i_apply >= 0, "받아 온 목록을 -testFilter 로 넘기지 않는다")
check(i_verify > i_filter >= 0, "거른 뒤 정말 돌았는지 확인하지 않는다")

# ⑥ 거르는 배선이 **전체 실행 가지 앞**에 있다
#
#    전체 실행은 거르면 안 된다 — 재는 쪽이자, 검증이 사라지면 안 되는 쪽이다.
#    거르는 줄이 그 가지 안이나 뒤로 옮겨 가면 새벽이 스스로를 걸러 버린다.
i_full = CODE.find("(전체)")
check(i_full >= 0, "전체 실행 가지를 못 찾았다 — 이 검사가 헛돈다")
check(0 <= i_filter < i_full,
      "거르는 줄이 전체 실행 가지 안이나 뒤에 있다 — 새벽이 스스로를 걸러 버린다")
print("  받아 읽고 · 한 방향으로 자라고 · 0건 실행을 잡는다 ✅")

if fails:
    print(f"\n❌ 실패 {len(fails)}건")
    for f in fails:
        print(f"  · {f}")
    raise SystemExit(1)
print("  느린 것을 재서 남긴다 · 정규식이 넘치게 잡지 않는다 ✅")
