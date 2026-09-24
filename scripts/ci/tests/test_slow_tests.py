"""느린 테스트 목록이 조용히 틀리지 않는가 (S15P21B202-617 · -701 · -739).

실행: python scripts/ci/tests/test_slow_tests.py
Unity·네트워크 없이 저장소의 실제 소스와 `.gitlab-ci.yml` 을 읽는다.

**무엇을 막는 검사인가.** 이 목록은 MR 에서 **테스트를 실제로 건너뛰는 데** 쓰인다.
그래서 틀리는 방향이 둘이고, 위험이 다르다.

  · 빼면 안 되는 것을 뺀다 → **검사 안 한 코드가 초록으로 지나간다** (치명적)
  · 빼도 되는 것을 안 뺀다 → MR 이 느리다 (아깝지만 안전하다)

그리고 `-739` 가 보탠 세 번째 — **빼는 줄 알았는데 안 빠진다.** `-701` 의 `-testFilter`
는 닷새 동안 한 건도 못 뺐고, 확인은 개수만 봐서 그것을 못 봤다. 그래서 여기서는
"돌았어야 할 것" 과 "빠졌어야 할 것" 을 **이름으로** 가리는 쪽이 제대로 배선됐는지를
긍정으로 못박는다(목록에 있는데 돈 결과를 주면 반드시 1 이 나와야 한다).

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 통째로 사라진다.
"""
import io
import json
import os
import re
import subprocess
import sys
import tempfile
import urllib.error

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))

import ci_common  # noqa: E402
import slow_tests as S  # noqa: E402

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


def read(rel):
    with io.open(os.path.join(ROOT, *rel.split("/")), encoding="utf-8") as f:
        return f.read()


def quietly(fn, *a):
    """`fn` 을 부르고 (돌려준 값, 찍은 글) 을 준다."""
    buf = io.StringIO()
    keep, sys.stdout = sys.stdout, buf
    try:
        return fn(*a), buf.getvalue()
    finally:
        sys.stdout = keep


class FakeGitLab:
    """`project_jobs`·`job_artifact_raw` 만 흉내 낸다. 새 잡이 먼저 온다.

    `arts` = {잡 id: {파일: 원문}}. `refs` 에 없는 잡은 develop 에서 돈 것이다.
    `broken` 의 잡은 아티팩트를 읽을 때 OSError 를 던진다(404 가 아닌 실패).

    `partial` 이면 잡 목록이 **2쪽에서 끊긴다** — 진짜 `project_jobs` 처럼 기본 모양은
    1쪽(MR 잡뿐)만 주고, `strict=True` 면 OSError 를 던진다."""

    def __init__(self, arts, refs=None, broken=(), partial=False):
        self.arts, self.refs, self.broken = arts, dict(refs or {}), set(broken)
        self.partial = partial

    def project_jobs(self, strict=False):
        if self.partial:
            if strict:
                raise OSError("잡 목록 2쪽 HTTP 502")
            return [{"id": 999, "name": "unity-compile", "status": "success",
                     "ref": "refs/merge-requests/9/head"}]
        return [{"id": i, "name": "unity-compile", "status": "success",
                 "ref": self.refs.get(i, "develop")} for i in sorted(self.arts, reverse=True)]

    def job_artifact_raw(self, job_id, name):
        if job_id in self.broken:
            raise OSError("HTTP 502")
        return self.arts.get(job_id, {}).get(name)


def with_gitlab(gl, fn, *a):
    """`S._gitlab` 을 바꿔 끼우고 부른다 — (돌려준 값, 찍은 글)."""
    keep, S._gitlab = S._gitlab, (lambda: gl)
    try:
        return quietly(fn, *a)
    finally:
        S._gitlab = keep


def files_in(d):
    return sorted(f for f in os.listdir(d) if not f.endswith(".xml"))


print()
print("== 느린 테스트 목록 ==")


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

#     `leaves()` 가 주는 `full`(XML 의 fullname)을 그대로 쓴다 — Unity 는 그 글자와 같은지로 건너뛴다.
check(S.slow_cases([{"full": 'N.C.P("a.b")', "time": "9"}]) == [(9.0, 'N.C.P("a.b")')],
      "fullname 을 이름으로 쓰지 않는다 — 점이 든 인자에서 이름이 달라진다")


# ── ② Heavy 는 **조상까지** 본다 (S15P21B202-739) ───────────────────────────
#
#     `-701` 의 목록 100건 중 99건이 Heavy 였다 — `-testCategory "!Heavy"` 가 이미 빼는
#     것을 또 넣어서, "목록이 정말 빠지나" 를 볼 이름이 1건밖에 없었다. Heavy 표시는
#     메서드에도, `[TestCase]` 를 묶는 ParameterizedMethod 에도, 픽스처에도 붙는다(실제
#     새벽 결과에 ParameterizedMethod 가 4곳 있다). 이 결과는 2026-09-24 새벽 결과
#     (잡 631830)의 모양을 줄인 것이다.
T = "Metagame.Gameplay.Tests."
FULL_XML = f"""<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="8" result="Passed" total="8" passed="8" failed="0" inconclusive="0" skipped="0">
  <test-suite type="TestSuite" id="1000" name="Not This Planet" fullname="Not This Planet">
    <test-suite type="Assembly" id="1" name="Metagame.Gameplay.Tests.Editor.dll" fullname="/builds/x/Metagame.Gameplay.Tests.Editor.dll">
      <properties><property name="platform" value="EditMode" /></properties>
      <test-suite type="TestSuite" id="2" name="Tests" fullname="Metagame.Gameplay.Tests">
        <test-suite type="TestFixture" id="3" name="Stage2DemoTests" fullname="{T}Stage2DemoTests">
          <properties />
          <test-case id="4" name="YardOptOutRetainsUnderlyingGameplayAndSessionInput" fullname="{T}Stage2DemoTests.YardOptOutRetainsUnderlyingGameplayAndSessionInput" result="Passed" duration="13.825784"><properties /></test-case>
          <test-case id="5" name="FullSnapshotFitsKcpAndReachesReadyConnections" fullname="{T}Stage2DemoTests.FullSnapshotFitsKcpAndReachesReadyConnections" result="Passed" duration="20.1"><properties><property name="Category" value="Heavy" /></properties></test-case>
          <test-case id="6" name="Fast" fullname="{T}Stage2DemoTests.Fast" result="Passed" duration="0.01"><properties /></test-case>
        </test-suite>
        <test-suite type="TestFixture" id="7" name="Stage2ArrivalTests" fullname="{T}Stage2ArrivalTests">
          <test-suite type="ParameterizedMethod" id="8" name="CommonClockIncludesCrashBeforeStage2" fullname="{T}Stage2ArrivalTests.CommonClockIncludesCrashBeforeStage2">
            <properties><property name="Category" value="Heavy" /></properties>
            <test-case id="9" name="CommonClockIncludesCrashBeforeStage2(1)" fullname="{T}Stage2ArrivalTests.CommonClockIncludesCrashBeforeStage2(1)" result="Passed" duration="9.0"><properties /></test-case>
            <test-case id="10" name="CommonClockIncludesCrashBeforeStage2(4)" fullname="{T}Stage2ArrivalTests.CommonClockIncludesCrashBeforeStage2(4)" result="Passed" duration="8.0"><properties /></test-case>
          </test-suite>
        </test-suite>
        <test-suite type="TestFixture" id="11" name="WholeFixtureHeavyTests" fullname="{T}WholeFixtureHeavyTests">
          <properties><property name="Category" value="Heavy" /></properties>
          <test-case id="12" name="Slow" fullname="{T}WholeFixtureHeavyTests.Slow" result="Passed" duration="30.0"><properties /></test-case>
        </test-suite>
        <test-suite type="TestFixture" id="13" name="MeteorReplicaTests" fullname="{T}MeteorReplicaTests">
          <test-case id="14" name="NetworkSerializationFitsTheDeclaredPacketBudgets" fullname="{T}MeteorReplicaTests.NetworkSerializationFitsTheDeclaredPacketBudgets" result="Passed" duration="6.5"><properties /></test-case>
          <test-case id="15" name="WasSlowYesterday" fullname="{T}MeteorReplicaTests.WasSlowYesterday" result="Passed" duration="1.0"><properties /></test-case>
        </test-suite>
      </test-suite>
    </test-suite>
  </test-suite>
</test-run>
"""
YARD = T + "Stage2DemoTests.YardOptOutRetainsUnderlyingGameplayAndSessionInput"
METEOR = T + "MeteorReplicaTests.NetworkSerializationFitsTheDeclaredPacketBudgets"
YESTERDAY = T + "MeteorReplicaTests.WasSlowYesterday"
ON_CASE = T + "Stage2DemoTests.FullSnapshotFitsKcpAndReachesReadyConnections"
ON_PARAM = T + "Stage2ArrivalTests.CommonClockIncludesCrashBeforeStage2(1)"
ON_FIXTURE = T + "WholeFixtureHeavyTests.Slow"
GONE = T + "DeletedTests.WasRemoved"

LEAVES = S.leaves(FULL_XML)
heavy = {c["full"] for c in LEAVES if c["heavy"]}
check(len(LEAVES) == 8, f"잎을 {len(LEAVES)}건 읽었다 — 8건이어야 한다")
check(ON_CASE in heavy, "테스트에 직접 붙은 Heavy 를 못 본다")
check(ON_PARAM in heavy, "ParameterizedMethod 에 붙은 Heavy 를 못 본다 — 그 아래 케이스가 목록에 들어간다")
check(ON_FIXTURE in heavy, "픽스처에 붙은 Heavy 를 못 본다 — 그 픽스처 전체가 목록에 들어간다")
check(YARD not in heavy and METEOR not in heavy, "Heavy 가 아닌 것을 Heavy 로 본다 — 목록에서 빠져 MR 에서 돈다")
check(S.expected_executed(LEAVES, [YARD, METEOR, GONE]) == 8 - 6,
      "남을 수가 '전체 − |Heavy ∪ 목록|' 이 아니다 — Heavy 를 안 빼면 기준을 조이는 순간 오탐이다")


# ── ③ 목록 파일 — **온전하지 않으면 통째로 버린다** ──────────────────────────
#
#     남을 수를 따로 두었던 `unity-slow-expect.txt` 는 `json.loads` 를 지나며 숫자가 되어
#     한 번도 읽히지 않았다. 이제 목록 파일 머리줄에 함께 싣고, 없으면 받지 않는다.
SECS = {YARD: 13.83, METEOR: 6.5, 'N.C.P("a;b")': 7.0}
TEXT = S.list_text([YARD, METEOR, 'N.C.P("a;b")'], SECS, 2409)
parsed = S.read_list(TEXT)
check(parsed is not None, "방금 쓴 목록을 되읽지 못한다")
if parsed:
    check(parsed["expect"] == 2409, f"남을 수를 잃었다: {parsed['expect']}")
    check(parsed["names"] == [YARD, METEOR, 'N.C.P("a;b")'], f"이름을 잃었다: {parsed['names']}")
    check(parsed["entries"][0] == (13.83, YARD), f"초를 잃었다: {parsed['entries'][0]}")
check(TEXT.startswith(S.HEADER), "머리줄이 없다 — 사람이 열었을 때 무엇인지 모른다")

#     `-739` 이전 목록(머리줄에 남을 수가 없다)은 **받지 않는다** — 그 100건은 99%가 Heavy 다.
OLD = "# 전체 실행에서 잰 느린 테스트 (S15P21B202-617 · -701)\n   10.59\t" + YARD + "\n"
check(S.read_list(OLD) is None, "남을 수가 없는 옛 목록을 받는다 — 건너뛰고 확인은 못 하는 반쪽이 된다")
check(S.read_list(TEXT.replace(f"{S.COUNT_KEY} 3", f"{S.COUNT_KEY} 4")) is None,
      "이름 수가 머리줄과 다른데 받는다 — 잘린 목록으로 건너뛴다")
check(S.read_list(TEXT.replace(f"{S.EXPECT_KEY} 2409\n", "")) is None,
      "남을 수 줄이 없는데 받는다 — 건너뛰고 확인은 못 하는 반쪽이 된다")
check(S.read_list(TEXT.replace(f"{S.COUNT_KEY} 3\n", "")) is None,
      "이름 수 줄이 없는데 받는다 — 잘렸는지 알 수 없다")
check(S.read_list(TEXT.replace(f"{S.EXPECT_KEY} 2409", f"{S.EXPECT_KEY} 이천")) is None,
      "남을 수가 숫자가 아닌데 받는다 — 확인이 조용히 꺼진다")
check(S.read_list(TEXT.replace("   13.83\t", "  13.83 ")) is None, "초와 이름이 탭으로 갈리지 않았는데 받는다")
check(S.read_list(TEXT + "    1.00\t" + YARD + "\n") is None, "같은 이름이 두 번 있는데 받는다")
check(S.read_list(2413) is None and S.read_list(None) is None, "글자가 아닌 것을 목록으로 받는다")
_tabbed = S.read_list(S.list_text(["N.C.P(\"a\tb\")"], {}, 1))
check(_tabbed is not None and _tabbed["names"] == ["N.C.P(\"a\tb\")"], "이름 안의 탭에서 이름을 자른다")


# ── ④ 잰다 — **무엇이든 못 읽으면 아무것도 쓰지 않는다** ─────────────────────
def measure_with(gl, xml=FULL_XML):
    """`cmd_measure` 를 돌리고 (목록 원문 또는 None, 찍은 글)."""
    with tempfile.TemporaryDirectory() as d:
        path = os.path.join(d, "unity-results.xml")
        if xml is not None:
            with open(path, "w", encoding="utf-8") as f:
                f.write(xml)
        _rc, log = with_gitlab(gl, S.cmd_measure, path, d)
        out = os.path.join(d, S.LIST_FILE)
        wrote = files_in(d)
        text = open(out, encoding="utf-8").read() if os.path.exists(out) else None
        check(wrote in ([], [S.LIST_FILE]), f"재는 쪽이 목록 말고 다른 것을 썼다: {wrote}")
        return text, log


PREV = S.list_text([YESTERDAY, ON_PARAM, GONE], {}, 1000)   # ON_PARAM 은 그 뒤 Heavy 가 됐다
text, log = measure_with(FakeGitLab({300: {S.LIST_FILE: PREV}}))
got = S.read_list(text)
check(got is not None, "잰 목록이 온전하지 않다: " + str(text))
if got:
    check(YARD in got["names"] and METEOR in got["names"], f"Heavy 가 아닌 느린 것을 안 넣었다: {got['names']}")
    check(not (heavy & set(got["names"])), f"Heavy 를 목록에 넣었다 — 이미 빠지는 것이다: {sorted(heavy & set(got['names']))}")
    #     집합은 한 방향으로만 자란다 — 어제 넣은 것은 오늘 1초여도 남는다. 다만 그 사이
    #     Heavy 가 된 것은 내린다(빠지는 집합 Heavy ∪ 목록은 그대로다).
    check(YESTERDAY in got["names"] and GONE in got["names"], f"이전 목록을 잃었다: {got['names']}")
    check(ON_PARAM not in got["names"], "Heavy 가 된 이전 이름을 목록에 남긴다")
    check(got["expect"] == 8 - 7, f"남을 수가 틀렸다: {got['expect']} (전체 8 − Heavy 4 − 목록 중 트리에 있는 3)")
    check(got["entries"] and dict((n, s) for s, n in got["entries"]).get(YARD) == 13.83,
          "잰 초를 목록에 싣지 않는다 — 상한에 걸릴 때 느린 것부터 고르지 못한다")

#     이전 목록이 **없으면**(찾아봤는데 없다) 이번에 잰 것부터 시작한다.
text, log = measure_with(FakeGitLab({300: {}}))
got = S.read_list(text)
check(got is not None and sorted(got["names"]) == sorted([YARD, METEOR]),
      f"이전 목록이 없을 때 이번에 잰 것으로 시작하지 않는다: {text}")

#     **모르면**(조회 실패) 아무것도 쓰지 않는다. "없다" 로 치면 오늘 잰 것만 남아
#     집합이 줄어든 목록이 최신이 되고, 다음 MR 들이 그것을 받아 간다.
#
#     **목록이 중간에 끊긴 것도 "모른다" 다** (S15P21B202-739). 1쪽이 MR 잡뿐이고 2쪽이
#     502 면 develop 잡을 하나도 못 본다 — 기본 `project_jobs` 는 그 1쪽을 주므로 "없다" 가
#     되어, 이전 목록을 버린 새 목록이 최신이 됐다.
for label, gl in (("잡 목록 조회가 비었다", FakeGitLab({})),
                  ("아티팩트를 못 읽었다", FakeGitLab({300: {S.LIST_FILE: PREV}}, broken={300})),
                  ("잡 목록이 2쪽에서 끊겼다", FakeGitLab({300: {S.LIST_FILE: PREV}}, partial=True)),
                  ("토큰이 없다", None)):
    text, _log = measure_with(gl)
    check(text is None, f"{label} — 그런데 목록을 썼다(줄어든 목록이 최신이 된다)")
try:
    S.find_published(FakeGitLab({300: {S.LIST_FILE: PREV}}, partial=True))
    check(False, "끊긴 잡 목록을 '없다' 로 읽는다 — LookupFailed 를 던져야 한다")
except S.LookupFailed as e:
    check("2쪽" in str(e), f"끊긴 사유가 로그에 남지 않는다: {e}")

#     **새벽에 통과한 것만 넣는다** (S15P21B202-739). 느린 테스트에서 흔한 것이 시한을 다
#     채우고 **실패한** 것이다 — 넣으면 다음 날부터 MR 이 그것을 건너뛰어, 고치는 MR 도
#     확인 없이 초록이 된다. 그리고 목록에 **있던** 것이 실패하면 내린다 — "한 방향으로만
#     자란다" 의 유일한 예외다. 없어진 이름(GONE)처럼 실패가 아닌 것은 그대로 둔다.
BROKE_XML = (FULL_XML
             .replace(f'fullname="{YARD}" result="Passed"', f'fullname="{YARD}" result="Failed"')
             .replace(f'fullname="{YESTERDAY}" result="Passed"', f'fullname="{YESTERDAY}" result="Failed"'))
check(BROKE_XML.count('result="Failed"') == 2, "실패 모양을 못 만들었다 — 검사 자체가 틀렸다")
text, log = measure_with(FakeGitLab({300: {S.LIST_FILE: PREV}}), xml=BROKE_XML)
got = S.read_list(text)
check(got is not None, "실패가 섞인 밤에 목록을 못 쓴다: " + str(text))
if got:
    check(YARD not in got["names"], "새벽에 실패한 느린 테스트를 목록에 넣는다 — 깨진 것이 MR 에서 사라진다")
    check(METEOR in got["names"], f"통과한 느린 것을 안 넣었다: {got['names']}")
    check(YESTERDAY not in got["names"],
          "목록에 있던 것이 새벽에 실패했는데 내리지 않는다 — 새벽 말고는 아무도 그 실패를 못 본다")
    check(GONE in got["names"], "실패가 아닌 이전 이름(결과에 없음)까지 내린다 — 한 방향으로 자라야 한다")
    #     내린 것은 다시 도는 쪽으로 센다 — 남을 수 = 전체 8 − Heavy 4 − 목록 중 트리에 있는 METEOR.
    check(got["expect"] == 3, f"내린 이름을 남을 수에 안 넣었다: {got['expect']}")
check(YESTERDAY in log, "목록에서 내린 이름을 로그에 안 남긴다 — 조용히 줄어든다: " + log)
check(YARD in log, "통과 못 해 넣지 않은 느린 이름을 로그에 안 남긴다: " + log)
text, _log = measure_with(FakeGitLab({300: {S.LIST_FILE: PREV}}), xml=None)
check(text is None, "결과를 못 읽었는데 목록을 썼다 — 앞 실행의 목록을 덮는다")
text, _log = measure_with(FakeGitLab({300: {S.LIST_FILE: PREV}}), xml="<test-run/>")
check(text is None, "테스트 0건 결과로 목록을 썼다 — 컴파일이 깨진 밤의 목록이 최신이 된다")

#     **검토를 거친 목록만 이어받는다.** MR 잡의 아티팩트는 리뷰 전 스크립트가 쓴 것이다.
text, _log = measure_with(FakeGitLab({400: {S.LIST_FILE: S.list_text([T + "Mr.Planted"], {}, 1)},
                                      300: {S.LIST_FILE: PREV}},
                                     refs={400: "refs/merge-requests/9/head"}))
got = S.read_list(text)
check(got is not None and T + "Mr.Planted" not in got["names"] and YESTERDAY in got["names"],
      "MR 잡의 목록을 이어받는다 — 한 MR 이 다른 MR 들이 건너뛸 것을 정할 수 있다")


# ── ⑤ 집합은 한 방향으로만 자란다 (S15P21B202-701) ──────────────────────────
kept, left = S.merge_monotone(["A", "B"], [(9.0, "C"), (8.0, "A")], cap=5)
check(set(kept) >= {"A", "B"}, f"이전 집합을 잃었다: {kept}")
check("C" in kept, f"새로 잰 것을 안 더했다: {kept}")
check(left == [], f"상한 안인데 남겼다: {left}")

#     상한에 걸린 것은 **버리지 않고 돌려준다** — 조용히 자르면 "왜 아직 안 빠지지" 를 못 본다.
kept, left = S.merge_monotone([], [(9.0, "X"), (8.0, "Y"), (7.0, "Z")], cap=1)
check(kept == ["X"], f"상한을 안 지켰다: {kept}")
check(left == ["Y", "Z"], f"넘친 것을 안 돌려줬다: {left}")


# ── ⑥ Unity 설정 파일 ──────────────────────────────────────────────────────
#
#     깨진 JSON 은 Unity 를 **멈추게** 한다(90분 타임아웃까지 unity 슬롯을 문다). 그래서
#     ASCII 로만 쓰고, 이상한 이름도 글자 그대로 되돌아와야 한다.
ODD = [YARD, 'N.C.RejectsUnsupportedAndMalformedOrders("follow;weld")', 'N.C.P("a\\"b(c)")', "N.C.빌드씬(\"한글 (괄호)\")"]
blob = S.settings_json(ODD, 631830)
check(blob.isascii(), "설정 파일에 ASCII 밖 글자가 있다 — ensure_ascii 를 껐다")
doc = json.loads(blob)
check([e["test"] for e in doc["ignoreTests"]] == ODD, "이름이 글자 그대로 되돌아오지 않는다")
check(all(e["ignoreComment"].startswith(S.MARK) and "631830" in e["ignoreComment"] for e in doc["ignoreTests"]),
      f"사유가 '{S.MARK}' 로 시작하지 않거나 출처 잡이 없다 — verify 가 우리 것을 못 가린다")
check(S.read_settings(blob) == ODD, "쓴 설정 파일을 되읽지 못한다")
for label, bad in (("JSON 이 아니다", blob[:-5]), ("목록이 아니다", '{"ignoreTests": {}}'),
                   ("이름이 없다", '{"ignoreTests": [{"ignoreComment": "slow-list"}]}'),
                   ("사유가 우리 것이 아니다", '{"ignoreTests": [{"test": "A.b", "ignoreComment": "x"}]}'),
                   ("통째로 목록이다", "[]")):
    check(S.read_settings(bad) is None, f"{label} — 그런데 설정 파일로 받는다")

#     상한에 걸리면 **느린 것부터** 건너뛰고 나머지는 돌린다.
kept, dropped = S.cap_entries([(1.0, "a"), (9.0, "b"), (5.0, "c")], cap=2)
check(kept == ["b", "c"] and dropped == ["a"], f"상한에서 느린 것부터 고르지 않는다: {kept} / {dropped}")


# ── ⑦ 받는다 — **다 쓰거나 아무것도 안 쓴다** ────────────────────────────────
LIST_OK = S.list_text([YARD, METEOR, ODD[1]], {YARD: 13.83, METEOR: 6.5, ODD[1]: 5.5}, 2409)


def apply_into(gl, before=()):
    """가짜 API 로 `cmd_apply` 를 돌리고 (쓴 파일들, {파일: 내용}, 찍은 글)."""
    with tempfile.TemporaryDirectory() as d:
        for name in before:
            S.write(os.path.join(d, name), "지난 실행이 남긴 것\n")
        rc, log = with_gitlab(gl, S.cmd_apply, d)
        check(rc == 0, "목록 때문에 apply 가 실패한다 — 그날 아무도 머지하지 못한다")
        wrote = files_in(d)
        return wrote, {f: open(os.path.join(d, f), encoding="utf-8").read() for f in wrote}, log


wrote, got, log = apply_into(FakeGitLab({300: {S.LIST_FILE: LIST_OK}, 200: {S.LIST_FILE: OLD},
                                         100: {S.LIST_FILE: LIST_OK.replace("2409", "2000")}},
                                        refs={300: "refs/merge-requests/9/head"}))
check(wrote == sorted(S.PUBLISHED), f"셋을 다 쓰지 않았다: {wrote}")
if wrote == sorted(S.PUBLISHED):
    marker = S.read_marker(got[S.APPLIED_FILE])
    check(marker is not None and marker["source"] == "100",
          f"MR 잡(300)이나 옛 목록(200)을 출처로 골랐다: {got[S.APPLIED_FILE]!r}")
    check(marker is not None and marker["expect"] == 2000 and marker["listed"] == 3,
          f"표시에 남을 수·건수가 없다: {got[S.APPLIED_FILE]!r}")
    check(S.read_settings(got[S.SETTINGS_FILE]) is not None
          and sorted(S.read_settings(got[S.SETTINGS_FILE])) == sorted([YARD, METEOR, ODD[1]]),
          "설정 파일의 이름이 목록과 다르다")
    check(got[S.LIST_FILE] == LIST_OK.replace("2409", "2000"),
          "목록을 원문 그대로 싣지 않는다 — develop 잡이 이것을 다음 실행에 넘긴다")
    check(all("잡 100" in e["ignoreComment"] for e in json.loads(got[S.SETTINGS_FILE])["ignoreTests"]),
          "사유에 출처 잡이 없다 — 무엇이 어디서 온 목록으로 빠졌는지 결과에서 못 본다")

#     온전한 목록이 없으면 **아무것도 쓰지 않는다** — 지난 실행이 남긴 것도 지운다.
for label, gl in (("옛 목록뿐", FakeGitLab({200: {S.LIST_FILE: OLD}})),
                  ("MR 잡에만 있다", FakeGitLab({300: {S.LIST_FILE: LIST_OK}}, refs={300: "refs/merge-requests/9/head"})),
                  ("조회가 비었다", FakeGitLab({})),
                  ("아티팩트를 못 읽었다", FakeGitLab({300: {S.LIST_FILE: LIST_OK}}, broken={300})),
                  ("잡 목록이 2쪽에서 끊겼다", FakeGitLab({300: {S.LIST_FILE: LIST_OK}}, partial=True)),
                  ("목록이 비었다", FakeGitLab({300: {S.LIST_FILE: S.list_text([], {}, 2500)}})),
                  ("토큰이 없다", None)):
    wrote, _got, _log = apply_into(gl, before=S.PUBLISHED)
    check(wrote == [], f"{label} — 그런데 {wrote} 가 남았다(옛 파일로 건너뛴다)")
#     끊긴 목록은 "없다" 가 아니라 **조회 실패**로 적는다 — 로그가 사유를 틀리게 말하면
#     "왜 오늘 13분이지" 를 엉뚱한 곳에서 찾는다.
_w, _g, _log = apply_into(FakeGitLab({300: {S.LIST_FILE: LIST_OK}}, partial=True))
check("조회 실패" in _log and "온전한 목록이 없다" not in _log,
      "끊긴 잡 목록을 '온전한 목록이 없다' 로 적는다: " + _log)

#     상한에 걸리면 느린 것부터 건너뛰고, **넘친 이름을 로그에 남기고**, 목록은 원문 그대로 싣는다.
_keep_cap, S.MAX_LISTED = S.MAX_LISTED, 2
try:
    wrote, got, log = apply_into(FakeGitLab({300: {S.LIST_FILE: LIST_OK}}))
finally:
    S.MAX_LISTED = _keep_cap
check(S.read_settings(got.get(S.SETTINGS_FILE, "")) == [YARD, METEOR],
      f"상한에서 느린 것부터 건너뛰지 않는다: {S.read_settings(got.get(S.SETTINGS_FILE, ''))}")
check(ODD[1] in log, "상한에 걸려 돌리는 이름을 로그에 안 남긴다 — 조용히 자른다")
check(got.get(S.LIST_FILE) == LIST_OK, "상한으로 자른 목록을 싣는다 — 사슬에서 이름이 영영 빠진다")

#     되읽은 이름이 다르면 넘기지 않는다 — 깨진 JSON 은 Unity 를 멈춘다.
_keep_json, S.settings_json = S.settings_json, (lambda names, job: '{"ignoreTests": [')
try:
    wrote, _got, log = apply_into(FakeGitLab({300: {S.LIST_FILE: LIST_OK}}))
finally:
    S.settings_json = _keep_json
check(wrote == [], f"되읽지 못한 설정 파일을 남긴다 — Unity 가 멈춘다: {wrote}")


# ── ⑧ 목록은 **원문으로** 받는다 (S15P21B202-739) ─────────────────────────────
#
#     `job_artifact_text` 는 본문을 `json.loads` 에 먼저 넣는다. `2413` 만 든 파일은 숫자가
#     되어 None 으로 떨어졌고, `-701` 은 그 탓에 "(남을 것 ?건)" 을 찍었다.
class _Resp:
    def __init__(self, body):
        self.body, self.status = body, 200

    def read(self):
        return self.body

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False


def _fake_urlopen(req, timeout=60):
    url = req.full_url
    if "/jobs/404/" in url:
        raise urllib.error.HTTPError(url, 404, "Not Found", None, io.BytesIO(b'{"message":"404 Not Found"}'))
    if "/jobs/502/" in url:
        raise urllib.error.HTTPError(url, 502, "Bad Gateway", None, io.BytesIO(b"<html>bad</html>"))
    if url.endswith("/settings.json"):
        return _Resp(b'{"ignoreTests": []}\n')
    return _Resp(b"2413\n")


_keep_open = ci_common.urllib.request.urlopen
ci_common.urllib.request.urlopen = _fake_urlopen
try:
    _gl = ci_common.GitLab()
    _gl.api, _gl.project, _gl.token = "https://lab.example/api/v4", "1443653", "t"
    check(_gl.job_artifact_raw(1, "unity-slow-expect.txt") == "2413\n",
          "숫자만 든 아티팩트가 글자로 오지 않는다 — json.loads 를 지났다")
    check(_gl.job_artifact_raw(1, "settings.json") == '{"ignoreTests": []}\n',
          "JSON 아티팩트를 풀어서 준다 — 원문이어야 한다")
    check(_gl.job_artifact_raw(404, S.LIST_FILE) is None, "없는 아티팩트(404)가 None 이 아니다")
    try:
        _gl.job_artifact_raw(502, S.LIST_FILE)
        check(False, "502 를 '없다' 로 준다 — 모르는 것을 없는 것으로 읽으면 목록이 줄어든다")
    except OSError:
        pass
finally:
    ci_common.urllib.request.urlopen = _keep_open

#     **끝까지 읽은 잡 목록** (`project_jobs(strict=True)`, S15P21B202-739). 기본 모양은 어느
#     쪽에서 끊기면 그때까지 모은 것을 준다 — 기준선 찾기에는 그게 맞지만(폴백이 있다),
#     느린 목록에는 "없다" 와 "끝까지 못 봤다" 를 섞는 길이 된다.
_PAGES = {}


def _pages_http(method, url, headers=None, body=None, timeout=60):
    page = int(url.split("&page=")[1].split("&")[0])
    return _PAGES.get(page, (200, []))


_keep_http = ci_common.http
ci_common.http = _pages_http
try:
    _gl = ci_common.GitLab()
    _gl.api, _gl.project, _gl.token = "https://lab.example/api/v4", "1443653", "t"
    _full = [{"id": i} for i in range(100)]
    _PAGES = {1: (200, _full), 2: (502, "<html>bad</html>")}
    check(len(_gl.project_jobs()) == 100, "기본 모양이 끊긴 쪽까지 모은 것을 주지 않는다 — 기준선 폴백이 바뀐다")
    try:
        _gl.project_jobs(strict=True)
        check(False, "2쪽이 502 인데 strict 가 끊긴 목록을 준다 — '없다' 와 '모른다' 가 섞인다")
    except OSError as e:
        check("2쪽" in str(e) and "502" in str(e), f"어느 쪽이 왜 끊겼는지 말하지 않는다: {e}")
    _PAGES = {1: (200, _full), 2: (200, {"message": "error"})}
    try:
        _gl.project_jobs(strict=True)
        check(False, "2쪽이 목록이 아닌데 strict 가 끊긴 목록을 준다")
    except OSError:
        pass
    #     200 에 빈 쪽·덜 찬 쪽은 **목록의 끝**이다 — 실패가 아니다.
    _PAGES = {1: (200, _full), 2: (200, [])}
    check(len(_gl.project_jobs(strict=True)) == 100, "빈 2쪽(목록의 끝)을 실패로 본다 — 목록이 영영 안 시작한다")
    _PAGES = {1: (200, _full[:30]), 2: (502, "")}
    check(len(_gl.project_jobs(strict=True)) == 30, "덜 찬 1쪽 뒤를 또 읽는다 — 목록이 끝났다")
finally:
    ci_common.http = _keep_http


# ── ⑨ 확인한다 — **이름으로** (S15P21B202-739) ──────────────────────────────
#
#     2026-09-23 MR 잡 630674(!627)의 결과를 줄인 것이다. 그 잡의 목록 100건 중 99건은
#     Heavy 라 결과에 없고, Heavy 가 아닌 1건 `YardOptOut…` 은 **돌았다**(Passed 13.83초).
#     `-701` 의 확인은 "절반은 돌았나" 뿐이라 이것을 초록으로 넘겼다.
G = "Metagame.Gameplay.Tests."
C = "Metagame.AIExperiment.Tests.CompanionTests."
#     XML 속성에 쓰인 모양 그대로(`&quot;`) — 풀어서 견주는 것은 ElementTree 의 몫이다.
FOLLOW_ATTR = C + "RejectsUnsupportedAndMalformedOrders(&quot;follow;weld&quot;)"
WELD_ATTR = C + "RejectsUnsupportedAndMalformedOrders(&quot;weld&quot;)"
FOLLOW = C + 'RejectsUnsupportedAndMalformedOrders("follow;weld")'
WELD = C + 'RejectsUnsupportedAndMalformedOrders("weld")'
LOCAL = G + "AiRuntimeTests.LocalPackTextControlsActualServerAi"
#     그 잡의 목록 중 셋 — Heavy 둘(결과에 없다)과 Heavy 가 아닌 YardOptOut.
LIST_630674 = [YARD, C + "FourPinsSelectNearestWithoutLookingAndRejectOutOfRange",
               G + "AiPinTaskTests.ActualStagePinCanBeReachedAndPulledUsingPlayerInputs"]


def tc(full_attr, duration, skipped, result="Passed", label=None, reason=None):
    """테스트 한 건. `skipped` 에 든 것은 Unity 가 목록으로 건너뛴 모양으로 낸다."""
    if full_attr in skipped:
        result, label, reason = "Skipped", "Ignored", f"{S.MARK} · 잡 631830"
    lab = f' label="{label}"' if label else ""
    why = f"<reason><message><![CDATA[{reason}]]></message></reason>" if reason else ""
    return (f'<test-case name="{full_attr.split("(")[0].rsplit(".", 1)[-1]}" fullname="{full_attr}" '
            f'result="{result}"{lab} duration="{duration}"><properties />{why}</test-case>')


def replay_xml(skipped=()):
    """잡 630674 결과의 모양. 조상 사슬(루트 → 어셈블리 → 네임스페이스 → 픽스처)도 그대로다."""
    local = tc(LOCAL, "0.015995", skipped, result="Skipped", label="Ignored",
               reason="Opt-in local models integration; no model downloads.")
    return f"""<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="5" result="Failed(Child)" total="5" passed="4" failed="0" inconclusive="0" skipped="1">
  <test-suite type="TestSuite" id="1000" name="Not This Planet" fullname="Not This Planet" result="Failed">
    <test-suite type="Assembly" id="1042" name="Metagame.AIExperiment.Tests.dll" fullname="/builds/s15-metaverse-game-sub1/S15P21B202/METAGAME/Library/ScriptAssemblies/Metagame.AIExperiment.Tests.dll">
      <test-suite type="TestSuite" id="1045" name="Tests" fullname="Metagame.AIExperiment.Tests">
        <test-suite type="TestFixture" id="1001" name="CompanionTests" fullname="Metagame.AIExperiment.Tests.CompanionTests">
          <test-suite type="ParameterizedMethod" id="1021" name="RejectsUnsupportedAndMalformedOrders" fullname="{C}RejectsUnsupportedAndMalformedOrders">
            {tc(WELD_ATTR, "0.000075", skipped)}
            {tc(FOLLOW_ATTR, "0.000075", skipped)}
          </test-suite>
        </test-suite>
      </test-suite>
    </test-suite>
    <test-suite type="Assembly" id="3843" name="Metagame.Gameplay.Tests.Editor.dll" fullname="/builds/s15-metaverse-game-sub1/S15P21B202/METAGAME/Library/ScriptAssemblies/Metagame.Gameplay.Tests.Editor.dll">
      <test-suite type="TestSuite" id="3844" name="Metagame" fullname="Metagame">
        <test-suite type="TestSuite" id="3849" name="Gameplay" fullname="Metagame.Gameplay">
          <test-suite type="TestSuite" id="3850" name="Tests" fullname="Metagame.Gameplay.Tests">
            <test-suite type="TestFixture" id="1237" name="AiRuntimeTests" fullname="{G}AiRuntimeTests">
              {local}
            </test-suite>
            <test-suite type="TestFixture" id="3339" name="Stage2DemoTests" fullname="{G}Stage2DemoTests" result="Passed">
              {tc(YARD, "13.825784", skipped)}
            </test-suite>
            <test-suite type="TestFixture" id="3320" name="MeteorReplicaTests" fullname="{G}MeteorReplicaTests">
              {tc(METEOR, "6.5", skipped)}
            </test-suite>
          </test-suite>
        </test-suite>
      </test-suite>
    </test-suite>
  </test-suite>
</test-run>
"""


REPLAY_XML = replay_xml()
a = S.audit(REPLAY_XML, LIST_630674)
check(a["leaked"] == [YARD], f"잡 630674 를 되돌리면 YardOptOut 이 새어 나간 것으로 잡혀야 한다: {a['leaked']}")
check(sorted(a["absent"]) == sorted(LIST_630674[1:]), f"결과에 없는 Heavy 두 건을 못 센다: {a['absent']}")
check(a["stray"] == [] and a["executed"] == 4, f"센 것이 틀렸다: {a}")

SKIP_XML = replay_xml({YARD})
a = S.audit(SKIP_XML, LIST_630674)
check(a["leaked"] == [] and a["skipped"] == [YARD] and a["executed"] == 3,
      f"목록 사유로 건너뛴 것을 '건너뜀' 으로 세지 않는다: {a}")

#     이름에 `"`·`;` 가 든 것 — ElementTree 가 `&quot;` 를 풀어 **글자 그대로** 견준다.
a = S.audit(replay_xml({FOLLOW_ATTR}), [FOLLOW])
check(a["skipped"] == [FOLLOW] and a["leaked"] == [], f"따옴표·; 가 든 이름을 못 맞춘다: {a}")
check(WELD not in a["skipped"], "옆 케이스까지 건너뛴 것으로 센다")

#     목록 밖이 목록 사유로 빠졌다 = 넘치게 뺐다.
a = S.audit(replay_xml({METEOR}), [YARD])
check(a["stray"] == [METEOR], f"목록에 없는데 건너뛴 것을 못 잡는다: {a}")

#     `[Ignore]` 같은 **다른 사유**로 안 돈 것은 새어 나감도, 넘침도 아니다.
a = S.audit(REPLAY_XML, [LOCAL])
check(a["leaked"] == [] and a["stray"] == [] and a["other"] == [LOCAL], f"다른 사유로 안 돈 것을 잘못 센다: {a}")

#     Unity 가 받았다고 찍은 수 — 번호로 세므로 리로드 뒤 덤프가 또 나와도 같다.
DUMP = ("Running tests for ExecutionSettings with details:\n"
        "   categoryNames = !Heavy\n"
        "ignoreTests[0] = 'A.b': 'slow-list · 잡 1'\n"
        "ignoreTests[1] = 'A.c(\"x;y\")': 'slow-list · 잡 1'\n"
        "featureFlags = null\n")
check(S.log_ignore_count(DUMP) == 2, f"로그의 ignoreTests 를 못 센다: {S.log_ignore_count(DUMP)}")
check(S.log_ignore_count(DUMP + DUMP) == 2, "덤프가 두 번 찍히면 두 배로 센다")
check(S.log_ignore_count("Running tests for ExecutionSettings with details:\nignoreTests = {}\n") == 0,
      "빈 ignoreTests 를 0 으로 세지 않는다")
check(S.log_ignore_count("Exiting batchmode\n") is None, "설정 덤프가 없는데 0 으로 센다 — '받지 않았다' 와 섞인다")


def log_for(names):
    return ("Running tests for ExecutionSettings with details:\n"
            + "".join(f"ignoreTests[{i}] = '{n}': '{S.MARK} · 잡 631830'\n" for i, n in enumerate(names)))


def verify_in(xml, listed=None, expect=3, log="auto", reused=None, settings="auto", flag="auto", marker=True):
    """폴더를 꾸며 `cmd_verify` 를 돌리고 (반환값, 찍은 글). `listed` 가 None 이면 넘기지 않은 잡이다.

    `flag` 는 잡이 `-testSettingsFile` 을 붙인 가지에서 쓰는 표시다 — "auto" 면 `listed` 가
    있을 때 쓰고, None 이면 쓰지 않는다(apply 는 목록을 만들었는데 잡이 넘기지 않은 실행)."""
    with tempfile.TemporaryDirectory() as d:
        path = os.path.join(d, "unity-results.xml")
        if xml is not None:
            S.write(path, xml)
        if listed is not None:
            if marker:
                S.write(os.path.join(d, S.APPLIED_FILE), S.marker_text(631830, len(listed), expect, 0))
            if settings == "auto":
                S.write(os.path.join(d, S.SETTINGS_FILE), S.settings_json(listed, 631830))
            if flag == "auto":
                S.write(os.path.join(d, S.FLAG_FILE), os.path.join(d, S.SETTINGS_FILE) + "\n")
        if log == "auto":
            log = log_for(listed or [])
        if log is not None:
            S.write(os.path.join(d, S.LOG_FILE), log)
        if reused:
            S.write(os.path.join(d, S.REUSED_FILE), reused + "\n")
        return quietly(S.cmd_verify, path, d)


#     **긍정으로 못박는다** — 잡 630674 를 되돌리면 반드시 1 이다. 새어 나감 검사를 지우면 여기서 걸린다.
rc, log = verify_in(REPLAY_XML, LIST_630674)
check(rc == 1, f"잡 630674 에서 목록의 테스트가 돌았는데 통과시킨다 — `-701` 의 구멍 그대로다: {log}")
check("YardOptOutRetainsUnderlyingGameplayAndSessionInput" in log, "새어 나간 이름을 로그에 안 남긴다")

rc, log = verify_in(SKIP_XML, LIST_630674)
check(rc == 0, "목록을 다 건너뛰었는데 막는다: " + log)
check("✅" in log, "통과를 말하지 않는다: " + log)

rc, log = verify_in(replay_xml({YARD, METEOR}), LIST_630674)
check(rc == 1, "목록 밖을 건너뛰었는데 통과시킨다: " + log)

rc, log = verify_in(SKIP_XML, LIST_630674, expect=100)
check(rc == 1, "예상의 절반도 안 돌았는데 통과시킨다 — 0건 실행에 초록: " + log)

rc, log = verify_in(SKIP_XML, LIST_630674, log=log_for(LIST_630674[:1]))
check(rc == 1, "Unity 가 받은 수가 넘긴 수와 다른데 통과시킨다: " + log)
rc, log = verify_in(SKIP_XML, LIST_630674, log="Exiting batchmode\n")
check(rc == 1, "로그에 실행 설정이 없는데 통과시킨다: " + log)
check(S.SETTINGS_DUMP in log, "실행 설정이 없는 것과 수가 다른 것을 가려 말하지 않는다: " + log)
rc, log = verify_in(SKIP_XML, LIST_630674, log=None)
check(rc == 0 and S.LOG_FILE in log, "로그가 없을 때 막거나, 확인 못 한 사실을 말하지 않는다: " + log)

rc, log = verify_in(SKIP_XML, LIST_630674, settings=None)
check(rc == 1 and "무엇을 건너뛰었는지 읽지 못한다" in log,
      "건너뛴다고 표시해 놓고 설정 파일이 없는데 통과시키거나, 그 사실을 말하지 않는다: " + log)
with tempfile.TemporaryDirectory() as _d:
    S.write(os.path.join(_d, "unity-results.xml"), SKIP_XML)
    S.write(os.path.join(_d, S.APPLIED_FILE), S.marker_text(631830, 3, 3, 0))
    S.write(os.path.join(_d, S.SETTINGS_FILE), S.settings_json(LIST_630674[:2], 631830))   # 표시는 3건
    S.write(os.path.join(_d, S.FLAG_FILE), "넘겼다\n")
    S.write(os.path.join(_d, S.LOG_FILE), log_for(LIST_630674[:2]))
    rc, log = quietly(S.cmd_verify, os.path.join(_d, "unity-results.xml"), _d)
check(rc == 1 and "무엇을 건너뛰었는지 읽지 못한다" in log,
      "표시의 건수와 설정 파일의 건수가 다른데 통과시킨다: " + log)
rc, log = verify_in(None, LIST_630674)
check(rc == 1, "건너뛰고 결과가 없는데 통과시킨다: " + log)

#     **"건너뛰었다" 는 잡이 플래그를 붙였다는 뜻이다** (S15P21B202-739). apply 의 표시만 있고
#     잡의 표시가 없으면 잡이 넘기지 않은 것이다 — 이 기능 이전 YAML 로 도는 MR 이 그렇다.
#     잡 630674 처럼 목록의 테스트가 돌고 로그에 ignoreTests 가 없어도, 그것은 장치 고장이
#     아니므로 1 을 주지 않고 **그 사실을 말한다.** ❌ 로 적으면 카드가 그 줄을 이유로 싣는다.
rc, log = verify_in(REPLAY_XML, LIST_630674, flag=None, log="Exiting batchmode\n")
check(rc == 0, "잡이 넘기지 않았는데 '목록이 돌았다 — 장치 고장' 으로 막는다: " + log)
check("넘기지 않았다" in log and S.FLAG_FILE in log, "잡이 넘기지 않은 사실을 말하지 않는다: " + log)
check("❌" not in log, "넘기지 않은 실행을 ❌ 로 적는다 — 틀린 곳을 가리키는 경고가 카드에 실린다: " + log)
#     잡은 넘겼다고 표시했는데 apply 의 표시가 없다 — 무엇을 건너뛰었는지 모른다.
rc, log = verify_in(SKIP_XML, LIST_630674, marker=False)
check(rc == 1 and "무엇을 건너뛰었는지 읽지 못한다" in log,
      "넘겼다는 표시만 있고 apply 의 표시가 없는데 통과시킨다: " + log)

#     넘기지 않은 실행(새벽·목록 없음) — 확인할 것이 없다. 다만 목록 사유 행이 있으면 이상하다.
rc, log = verify_in(REPLAY_XML)
check(rc == 0 and "건너뛰지 않았다" in log, "넘기지 않았는데 막는다: " + log)
rc, log = verify_in(SKIP_XML)
check(rc == 1, "넘기지 않았는데 목록 사유로 빠진 것이 있는데 통과시킨다: " + log)
rc, log = verify_in(None)
check(rc == 0, "넘기지 않았고 결과도 없는데 막는다(판정은 unity_report 가 한다)")
#     다른 잡의 결과를 그대로 쓴 실행 — 그 잡이 이미 확인했다.
rc, log = verify_in(SKIP_XML, reused="630674")
check(rc == 0 and "630674" in log, "재사용한 결과를 이 잡의 목록과 견준다: " + log)


# ── ⑩ **0건 실행에 초록**을 잡는다 ─────────────────────────────────────────
check(S.enough(0, 100) is False, "다 건너뛰어도 통과시킨다")
check(S.enough(50, 100) is True, "예상의 절반이면 통과여야 한다")
check(S.enough(10, 0) is True, "예상이 없으면 막지 않는다")


# ── ⑪ 잡 배선 ──────────────────────────────────────────────────────────
CI = read(".gitlab-ci.yml")
m = re.search(r"(?ms)^unity-compile:.*?(?=^[a-z][a-z0-9-]*:|^# ──|\Z)", CI)
check(m is not None, "unity-compile 잡을 못 찾았다")
JOB = m.group(0) if m else ""
CODE = "\n".join(l for l in JOB.splitlines() if not l.lstrip().startswith("#"))

check("slow_tests.py measure" in CODE, "재는 것을 부르지 않는다")

#     **전체 실행에서만 잰다.** 건너뛰고 돈 실행으로 목록을 만들면 이미 빠진 테스트의
#     시간을 모르므로, 한 번 빠진 것이 영영 빠진 채로 굳는다.
i_sched = CODE.find('"$CI_PIPELINE_SOURCE" = "schedule"')
i_meas = CODE.find("slow_tests.py measure")
check(0 <= i_sched < i_meas,
      "전체 실행 조건 없이 잰다 — 건너뛰고 돈 결과로 목록을 만들면 뺀 것이 영영 굳는다")

#     남기지 않으면 다음 실행이 받아 갈 수 없고, 사람이 무엇을 건너뛰었는지 볼 수 없다.
_paths = re.search(r"(?ms)^    paths: \[(.*?)\]", JOB)
for f in S.PUBLISHED:
    check(_paths is not None and f in _paths.group(1), f"{f} 가 아티팩트에 없다")
check(S.LOG_FILE in (_paths.group(1) if _paths else ""), "unity.log 가 아티팩트에 없다 — verify 근거가 사라진다")

#     재는 것은 **판정 뒤**여야 한다. 앞에 두면 그 스크립트가 죽을 때 판정이 통째로 날아간다.
check(CODE.find("unity_report.py") < i_meas, "판정보다 먼저 잰다 — 재는 쪽이 죽으면 판정이 사라진다")

#     받는 쪽이 **실제로 배선돼 있다.** 스크립트만 있고 부르는 곳이 없던 것이 `-701` 의 이유였다.
i_apply = CODE.find("slow_tests.py apply")
i_flag = CODE.find("-testSettingsFile")
i_verify = CODE.find("slow_tests.py verify")
i_full = CODE.find("(전체)")
check(i_apply >= 0, "목록을 받아 오는 곳이 없다 — 재기만 하고 아무도 안 읽는다")
check(i_flag > i_apply >= 0, "받아 온 목록을 -testSettingsFile 로 넘기지 않는다")
check(i_verify > i_flag >= 0, "건너뛴 뒤 이름으로 확인하지 않는다")
check(CODE.count("-testSettingsFile") == 1, "-testSettingsFile 이 두 곳 이상에 있다 — 새벽이나 재실행이 건너뛴다")

#     `-testFilter` 는 **아무것도 못 뺐다**(`-739`). 본 실행 쪽에 다시 생기면 같은 구멍이다.
#     재실행 단계의 `-testFilter` 는 그 테스트만 **고르는**(포함) 쓰임이라 그대로 둔다.
check(i_full > i_apply >= 0 and "-testFilter" not in CODE[i_apply:i_full],
      "본 실행에 -testFilter 가 다시 들어왔다 — Unity 는 그것을 포함 필터로 써서 아무것도 안 뺀다")

#     **설정 파일과 표시가 함께 있을 때만** 넘긴다. apply 는 되읽어 확인한 뒤에 표시를 쓴다 —
#     조건이 빠지면 깨진 JSON 이 Unity 로 가고, Unity 는 90분 타임아웃까지 멈춘다.
_head, _, _flag_start = CODE[:i_flag].rpartition("\n")
_flag_line = _flag_start + CODE[i_flag:].split("\n", 1)[0]
_cond = _head.rsplit("\n", 1)[-1]                 # 주석을 걷어 냈으므로 바로 윗줄이 조건이다
check(f'-testSettingsFile "$CI_PROJECT_DIR/{S.SETTINGS_FILE}"' in _flag_line,
      f"넘기는 파일이 {S.SETTINGS_FILE} 가 아니다 — apply 가 쓰는 것과 어긋난다")
check(_cond.lstrip().startswith("if ") and f'-s "$CI_PROJECT_DIR/{S.SETTINGS_FILE}"' in _cond
      and f'-s "$CI_PROJECT_DIR/{S.APPLIED_FILE}"' in _cond,
      "설정 파일과 표시가 둘 다 있을 때만 넘기지 않는다: " + _cond.strip())
#     **둘 다**다 — `||` 로 바뀌면 되읽기에 실패해 표시가 없는 설정 파일도 넘어간다(`-739` 리뷰).
#     아래 진리표가 동작으로 못박고, 셸이 없는 곳에서도 이 줄이 연산자를 본다.
check("] && [" in _cond and "||" not in _cond, "두 파일 조건이 && 가 아니다: " + _cond.strip())

#     넘기는 가지(`if` 부터 그 `else` 까지)와 안 넘기는 가지를 떼어 본다.
_lines = CODE.splitlines()
_ci = next((i for i, l in enumerate(_lines) if l == _cond), -1)
_ind = len(_cond) - len(_cond.lstrip())
_else = next((i for i in range(_ci + 1, len(_lines))
              if _lines[i].strip() == "else" and len(_lines[i]) - len(_lines[i].lstrip()) == _ind), -1)
_fi = next((i for i in range(max(_else, _ci) + 1, len(_lines))
            if _lines[i].strip() == "fi" and len(_lines[i]) - len(_lines[i].lstrip()) == _ind), -1)
check(0 <= _ci < _else < _fi, "넘기는 if/else/fi 를 못 찾았다 — 아래 검사가 헛돈다")
PASS_BRANCH = "\n".join(_lines[_ci + 1:_else])
SKIP_BRANCH = "\n".join(_lines[_else + 1:_fi])
BLOCK = "\n".join(l[_ind:] for l in _lines[_ci:_fi + 1])

#     **verify 가 믿는 표시는 넘긴 가지가 쓴다** — apply 의 표시만으로는 넘겼는지 모른다.
_flag_write = f'> "$CI_PROJECT_DIR/{S.FLAG_FILE}"'
check(_flag_write in PASS_BRANCH, f"넘기는 가지가 {S.FLAG_FILE} 를 쓰지 않는다 — verify 가 '건너뛰었다' 를 모른다")
check(CODE.count(_flag_write) == 1, f"{S.FLAG_FILE} 를 넘기는 가지 밖에서도 쓴다 — 안 넘긴 실행이 넘긴 것이 된다")
#     지난 실행의 표시는 **가지가 갈리기 전에** 지운다 — 남으면 새벽 전체 실행이 "건너뛴 실행" 이 된다.
_i_rm = CODE.find(f'rm -f "$CI_PROJECT_DIR/{S.FLAG_FILE}"')
check(0 <= _i_rm < CODE.find('if [ "$CI_PIPELINE_SOURCE" != "schedule" ]'),
      f"가지가 갈리기 전에 지난 {S.FLAG_FILE} 를 지우지 않는다 — 재사용된 워크스페이스에서 새벽이 '건너뛴 실행' 이 된다")
check(S.FLAG_FILE in (_paths.group(1) if _paths else ""), f"{S.FLAG_FILE} 가 아티팩트에 없다 — 넘겼는지를 나중에 못 본다")

#     **코멘트가 "느린 목록을 건너뛰었다" 를 말한다** — 넘긴 가지에서만. Heavy 와 같은 이유다.
check("export UNITY_SKIPPED_SLOW=" in PASS_BRANCH, "넘기는 가지가 UNITY_SKIPPED_SLOW 를 내보내지 않는다 — 코멘트가 건너뛴 것을 숨긴다")
check(CODE.count("UNITY_SKIPPED_SLOW=") == 1 and "UNITY_SKIPPED_SLOW" not in SKIP_BRANCH,
      "UNITY_SKIPPED_SLOW 를 넘기지 않은 가지에서도 내보낸다 — 전부 돈 실행에 '건너뛰었다' 가 적힌다")
check(S.APPLIED_FILE in PASS_BRANCH and "listed" in PASS_BRANCH,
      f"건너뛴 수를 {S.APPLIED_FILE} 의 listed 에서 읽지 않는다 — 코멘트의 수가 넘긴 수와 어긋난다")


def _shell():
    """진리표를 돌릴 셸. `run_all.shell` 과 같은 규칙으로 찾는다."""
    try:
        from run_all import shell
    except Exception:  # noqa: BLE001
        return None
    return shell()


#     **동작으로 못박는다** — 넘기는 블록을 셸로 돌려 두 파일의 9가지 조합을 본다.
#     넘기는 것 · 표시를 쓰는 것 · 건너뛴 수를 내보내는 것이 **셋 다 같은 조건**이어야 한다.
_sh = _shell()
if _sh is None:
    print("  ⏭ sh·bash 를 찾지 못해 넘기는 블록의 진리표를 건너뛴다(위 정적 검사만 했다)")
else:
    _probe = ('set -- -testCategory "!Heavy"\n' + BLOCK + "\n"
              + 'for a in "$@"; do echo "ARG=$a"; done\n'
              + 'echo "SLOW=${UNITY_SKIPPED_SLOW-unset}"\n')
    _marker = S.marker_text(631830, 2, 2409, 0)
    for s_state in ("없음", "빈 파일", "있음"):
        for a_state in ("없음", "빈 파일", "있음", "listed 없음"):
            if a_state == "listed 없음" and s_state != "있음":
                continue
            with tempfile.TemporaryDirectory() as d:
                if s_state != "없음":
                    S.write(os.path.join(d, S.SETTINGS_FILE), "" if s_state == "빈 파일" else "{}\n")
                if a_state != "없음":
                    S.write(os.path.join(d, S.APPLIED_FILE),
                            {"빈 파일": "", "있음": _marker, "listed 없음": "source 631830\n"}[a_state])
                r = subprocess.run([_sh, "-c", _probe], capture_output=True, text=True,
                                   encoding="utf-8", errors="replace",
                                   env=dict(os.environ, CI_PROJECT_DIR=d.replace("\\", "/")))
                args = [l[4:] for l in r.stdout.splitlines() if l.startswith("ARG=")]
                slow = next((l[5:] for l in r.stdout.splitlines() if l.startswith("SLOW=")), None)
                flagged = os.path.exists(os.path.join(d, S.FLAG_FILE))
            want = s_state == "있음" and a_state in ("있음", "listed 없음")
            where = f"설정 파일 {s_state} · 표시 {a_state}"
            check(r.returncode == 0, f"{where}: 넘기는 블록이 셸에서 실패했다: {r.stderr.strip()[:200]}")
            check(("-testSettingsFile" in args) == want,
                  f"{where}: -testSettingsFile 을 {'안 ' if want else ''}넘긴다 — 둘 다 있을 때만이어야 한다: {args}")
            check(flagged == want, f"{where}: 넘긴 것과 {S.FLAG_FILE} 가 어긋난다(넘김 {want} · 표시 {flagged})")
            check(slow == ({"있음": "2", "listed 없음": "?"}[a_state] if want else "unset"),
                  f"{where}: UNITY_SKIPPED_SLOW 가 {slow!r} 다 — 넘겼을 때만, 넘긴 수로 내보내야 한다")

#     Unity 가 쓰는 사유 머리와 코멘트가 읽는 머리가 같아야 한다 — 어긋나면 기준선이
#     건너뛴 테스트를 못 가려 **남의 실패로 MR 이 막힌다.**
import unity_report as R  # noqa: E402
check(R.SLOW_SKIP_MARK == S.MARK, f"unity_report.SLOW_SKIP_MARK({R.SLOW_SKIP_MARK!r}) ≠ slow_tests.MARK({S.MARK!r})")
_keep_env = os.environ.get("UNITY_SKIPPED_SLOW")
os.environ["UNITY_SKIPPED_SLOW"] = "2"
try:
    check(S.SETTINGS_FILE in R.merged_note(), f"코멘트가 가리키는 파일이 {S.SETTINGS_FILE} 가 아니다: {R.merged_note()}")
finally:
    if _keep_env is None:
        os.environ.pop("UNITY_SKIPPED_SLOW", None)
    else:
        os.environ["UNITY_SKIPPED_SLOW"] = _keep_env

#     건너뛰는 배선이 **전체 실행 가지 앞**에 있고, 전체 실행은 인자를 통째로 비운다.
check(0 <= i_flag < i_full, "건너뛰는 줄이 전체 실행 가지 안이나 뒤에 있다 — 새벽이 스스로를 건너뛴다")
_full_branch = CODE[:i_full].rsplit("else", 1)[-1]
check(re.search(r"(?m)^\s*set --\s*$", _full_branch) is not None,
      "전체 실행 가지가 인자를 비우지 않는다 — 남은 설정 파일이 새벽에 넘어갈 수 있다")

#     verify 의 1 은 **2(판단 불가)** 로 올린다 — 우리 도구가 이상한 것이지 그 MR 탓이 아니다.
_after_verify = CODE[i_verify:i_verify + 200]
check('RC=2' in _after_verify, "verify 가 실패해도 판정을 올리지 않는다 — 안 빠진 것이 초록으로 지나간다")
check(f'-logFile "$CI_PROJECT_DIR/{S.LOG_FILE}"' in CODE, f"Unity 로그가 {S.LOG_FILE} 가 아니다 — verify 가 못 읽는다")
check(S.REUSED_FILE in CODE, f"재사용 표시가 {S.REUSED_FILE} 가 아니다 — verify 가 재사용을 모른다")

print(f"  기준 {S.THRESHOLD_SECONDS:g}초 · 상한 {S.MAX_LISTED}건 · 파일 {'·'.join(S.PUBLISHED)}")

if fails:
    print(f"\n❌ 실패 {len(fails)}건")
    for f in fails:
        print(f"  · {f}")
    raise SystemExit(1)
print("  Heavy 를 빼고 재고 · 온전한 것만 받고 · 이름으로 확인한다 ✅")
