"""Unity 결과를 MR 에서 보이게 하는 보고 (S15P21B202-271).

실행: python scripts/ci/tests/test_unity_report.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

왜 이 장치가 있는가 — Unity 는 NUnit3(`<test-run>`)를 내는데 GitLab 은 JUnit(`<testsuite>`)만
파싱한다. 그래서 `reports: junit:` 을 걸어도 **조용히 0건**으로 읽혔다(2026-09-02 실측:
test_report_summary total 0). 조용히 실패하는 종류라 테스트로 못 박아 둔다.

그리고 **새 실패와 기존 실패를 갈라야** 읽힌다. 매번 17개를 나열하면 사람이 무시한다 —
mr-lint 의 스택 경고가 그렇게 무시됐다(실측 26 대 0).
"""
from pathlib import Path
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"
os.environ["GITLAB_TOKEN"] = "x"
os.environ["CI_MERGE_REQUEST_IID"] = "196"

import ci_common  # noqa: E402
import unity_report as R  # noqa: E402

NUNIT = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" total="4" passed="2" failed="2" skipped="0" duration="1.5">
  <test-suite type="TestFixture" name="ClampPullTests" fullname="Metagame.Gameplay.Tests.ClampPullTests">
    <test-case id="1" name="MovingAwayFromTheClamp_PullsIt"
               fullname="Metagame.Gameplay.Tests.ClampPullTests.MovingAwayFromTheClamp_PullsIt"
               result="Failed" duration="0.02">
      <failure><message><![CDATA[잡고 있어야 한다.
  Expected: True  But was: False]]></message></failure>
    </test-case>
    <test-case id="2" name="HoldingWithoutMoving_DoesNotPull"
               fullname="Metagame.Gameplay.Tests.ClampPullTests.HoldingWithoutMoving_DoesNotPull"
               result="Passed" duration="0.01" />
  </test-suite>
  <test-suite type="TestFixture" name="MeteorTests" fullname="Metagame.Gameplay.Tests.MeteorTests">
    <test-case id="3" name="Meteor_Settles" fullname="Metagame.Gameplay.Tests.MeteorTests.Meteor_Settles"
               result="Failed" duration="0.03">
      <failure><message>정착해야 한다</message></failure>
    </test-case>
    <test-case id="4" name="Meteor_Spawns" fullname="Metagame.Gameplay.Tests.MeteorTests.Meteor_Spawns"
               result="Passed" duration="0.01" />
  </test-suite>
</test-run>
"""

# ── ① NUnit3 파싱 — 총계와 클래스·이름 분리
d = R.parse_nunit(NUNIT)
assert (d["total"], d["passed"], d["failed"]) == (4, 2, 2), d
fails = {f'{c["cls"]}.{c["name"]}' for c in d["cases"] if c["result"] == "Failed"}
assert fails == {"Metagame.Gameplay.Tests.ClampPullTests.MovingAwayFromTheClamp_PullsIt",
                 "Metagame.Gameplay.Tests.MeteorTests.Meteor_Settles"}, fails
msg = next(c["msg"] for c in d["cases"] if c["name"] == "MovingAwayFromTheClamp_PullsIt")
assert "Expected: True" in msg and "\n" not in msg, f"여러 줄 메시지를 한 줄로 접어야 한다: {msg!r}"
print("  NUnit3 파싱 ✅")

# ── ② JUnit 변환 — GitLab 이 읽는 형식이고 classname+name 이 신원이다
j = R.to_junit(d)
assert j.startswith('<?xml') and "<testsuites" in j and "</testsuites>" in j
assert 'tests="4"' in j and 'failures="2"' in j, j[:200]
assert 'classname="Metagame.Gameplay.Tests.ClampPullTests"' in j
assert 'name="MovingAwayFromTheClamp_PullsIt"' in j
assert "<failure message=" in j
import xml.etree.ElementTree as ET  # noqa: E402
root = ET.fromstring(j)             # 실제로 파싱되는 XML 이어야 한다
assert root.tag == "testsuites"
assert len(root.findall("testsuite")) == 2
assert len(root.findall(".//testcase")) == 4
assert len(root.findall(".//failure")) == 2
print("  JUnit 변환 ✅")

# ── ③ 특수문자가 XML 을 깨지 않는다 (Unity 는 메시지를 CDATA 로 감싸므로 < & " 가 그대로 온다)
weird = NUNIT.replace("<message>정착해야 한다</message>",
                      '<message><![CDATA[값이 <null> & "빈 문자열" 이면 안 된다]]></message>')
jw = R.to_junit(R.parse_nunit(weird))
ET.fromstring(jw)
assert "&lt;null&gt;" in jw or "&lt;null>" in jw, "이스케이프돼야 한다"
print("  특수문자 이스케이프 ✅")

# ── ④ 컴파일 오류 추출 — 중복 제거, 상한
LOG = "\n".join([
    "Some noise",
    "Assets/A.cs(12,5): error CS0117: 'PlayerRig' does not contain 'HandReleaseReason'",
    "Assets/A.cs(12,5): error CS0117: 'PlayerRig' does not contain 'HandReleaseReason'",
    "Assets/B.cs(3,1): error CS1061: no member 'Foo'",
    "warning CS0168: unused",
])
errs = R.compile_errors(LOG)
assert len(errs) == 2, errs
assert all("error CS" in e for e in errs)
assert not any("warning" in e for e in errs)
print("  컴파일 오류 추출 ✅")

# ── ⑤ 코멘트 — 컴파일 실패면 그것만 크게, 테스트 목록은 안 낸다
b = R.body(d, errs, set(), "develop")
assert b.startswith(R.MARKER)
assert "컴파일 실패" in b and "CS0117" in b, b[:300]
assert "MovingAway" not in b, "컴파일이 깨졌으면 테스트 나열은 소음이다"
assert "2시간 37분" in b, "왜 급한지 근거가 있어야 한다"
print("  컴파일 실패 코멘트 ✅")

# ── ⑥ 기준선을 못 구하면 새/기존을 단정하지 않는다 (None ≠ 빈 집합)
b = R.body(d, [], None, "develop")
assert "가릴 수 없었습니다" in b, b
# 기준선이 없으면 "새 실패가 N개다" 라고 단정하거나 그 수를 표로 내지 않아야 한다.
assert "새로 실패하는 테스트" not in b, "기준선 없이 새 실패 건수를 말하면 안 된다"
assert "그중 **새로 생긴 것**" not in b, "기준선 없이 새/기존 분해표를 내면 안 된다"
print("  기준선 없음 → 단정 안 함 ✅")

# ── ⑦ 기존 실패만 있으면 초록 — 팀원이 고치는 중인 것을 나열하지 않는다
base_all = {"Metagame.Gameplay.Tests.ClampPullTests.MovingAwayFromTheClamp_PullsIt",
            "Metagame.Gameplay.Tests.MeteorTests.Meteor_Settles"}
b = R.body(d, [], base_all, "develop")
assert "새로 만든 테스트 실패 없음" in b or "만든 테스트 실패 없음" in b, b[:200]
assert "MovingAway" not in b, "기존 실패는 나열하지 않는다"
assert "| 그중 **새로 생긴 것** | **0** |" in b, b
print("  기존 실패만 → 초록 + 미나열 ✅")

# ── ⑧ 새 실패가 있으면 그것만 나열한다
base_one = {"Metagame.Gameplay.Tests.ClampPullTests.MovingAwayFromTheClamp_PullsIt"}
b = R.body(d, [], base_one, "develop")
assert "새로 실패하는 테스트 1개" in b, b[:200]
assert "Meteor_Settles" in b, "새 실패는 나열해야 한다"
assert "MovingAwayFromTheClamp_PullsIt" not in b.split("**새 실패**")[1], "기존 실패는 목록에서 빠져야 한다"
print("  새 실패만 나열 ✅")

# ── ⑨ 되살린 것도 알려 준다
base_more = base_all | {"Metagame.Gameplay.Tests.Other.WasFailing"}
b = R.body(d, [], base_more, "develop")
assert "되살린" in b and "WasFailing" in b, b
print("  되살린 테스트 표시 ✅")

# ── ⑩ 기준선 조회 — 대상 브랜치의 마지막 unity-compile 아티팩트를 쓴다
CALLS = []


def fake_http(method, url, headers=None, body=None, timeout=30):
    CALLS.append(url)
    if "/jobs?per_page=100&page=" in url:
        return 500, {}                  # 잡 목록을 못 읽는다 → 파이프라인 훑기로 폴백
    if "/pipelines?ref=" in url:
        return 200, [{"id": 900}, {"id": 899}]
    if url.endswith("/jobs?per_page=100"):
        pid = int(url.split("/pipelines/")[1].split("/")[0])
        if pid == 900:
            return 200, [{"id": 10, "name": "mr-lint", "status": "success"}]      # 이 파이프라인엔 없다
        return 200, [{"id": 20, "name": "unity-compile", "status": "failed"}]
    if "/artifacts/unity-results.xml" in url:
        return 200, NUNIT
    raise AssertionError(f"예상 못한 호출: {method} {url}")


ci_common.http = fake_http
R.http = fake_http if hasattr(R, "http") else None
gl = ci_common.GitLab()
base = R.baseline_failures(gl, "develop")
assert base == fails, base
assert any("/artifacts/unity-results.xml" in u for u in CALLS), CALLS
print("  기준선 조회 ✅")


# ── ⑪ 아티팩트가 없으면 None (빈 집합이 아니다)
def no_artifact(method, url, headers=None, body=None, timeout=30):
    if "/jobs?per_page=100&page=" in url:
        return 500, {}
    if "/pipelines?ref=" in url:
        return 200, [{"id": 899}]
    if url.endswith("/jobs?per_page=100"):
        return 200, [{"id": 20, "name": "unity-compile", "status": "failed"}]
    if "/artifacts/" in url:
        return 404, {"message": "404"}
    raise AssertionError(url)


ci_common.http = no_artifact
assert R.baseline_failures(ci_common.GitLab(), "develop") is None
print("  아티팩트 없음 → None ✅")

# ── ⑫ 종료 코드 판정 (S15P21B202-309) ────────────────────────────────────────
#    전에는 잡이 Unity 의 원시 종료 코드를 그대로 뱉었다. Unity 는 테스트가 하나라도 실패하면
#    2 로 끝나므로, 기준선에도 있는 실패 25건만 가진 MR 세 건(!228·!229·!230)이 전부 빨간 ⚠ 였다.
#    코멘트는 "새로 생긴 것 0" 이라고 정확히 적고 있었는데도 그랬다 — 판정과 종료 코드가 따로였다.
def data(failed_names, total=100):
    cases = [{"cls": "A", "name": nm, "msg": "", "result": "Failed"} for nm in failed_names]
    return {"total": total, "passed": total - len(cases), "failed": len(cases),
            "skipped": 0, "duration": 1.0, "cases": cases}


PASS, REG, UNK = R.PASS, R.REGRESSION, R.UNKNOWN
assert (PASS, REG, UNK) == (0, 1, 2), "allow_failure: exit_codes: [2] 와 짝이다"

# 기존 실패만 → **통과**. 이 한 줄이 09-06 의 거짓 경고 세 건을 없앤다.
code, why = R.decide(data(["t1", "t2"]), [], {"A.t1", "A.t2"}, 2)
assert code == PASS, (code, why)

# 이 MR 이 만든 실패 → 막는다
code, _ = R.decide(data(["t1", "t2"]), [], {"A.t1"}, 2)
assert code == REG, code

# 컴파일 오류는 기준선을 볼 필요가 없다 — develop 이 컴파일되면 이 MR 이 깬 것이다
code, _ = R.decide(data([]), ["error CS1002: ; expected"], {"A.t1"}, 1)
assert code == REG, code
code, _ = R.decide(data([]), ["error CS1002: ; expected"], None, 1)
assert code == REG, "기준선이 없어도 컴파일 오류는 막는다"

# 기준선을 못 봤다 + Unity 가 죽었다 → 판단 불가(경고). 회귀로 단정하지 않는다.
code, _ = R.decide(data(["t1"]), [], None, 2)
assert code == UNK, code

# 기준선을 못 봤지만 Unity 가 성공 → 통과
code, _ = R.decide(data([]), [], None, 0)
assert code == PASS, code

# 실패는 없는데 Unity 가 죽었다 = 라이선스·크래시·타임아웃. 인프라 일이므로 머지를 막지 않는다.
code, why = R.decide(data([]), [], set(), 1)
assert code == UNK and "환경" in why, (code, why)

# 전부 통과
assert R.decide(data([]), [], set(), 0)[0] == PASS

# 되살린 것이 있어도(기준선에 있고 지금은 없음) 통과다
assert R.decide(data([]), [], {"A.t1", "A.t2"}, 0)[0] == PASS
print("  종료 코드 판정 9가지 ✅")


# ── ⑬ 판정은 파일로 넘어간다 — 잡이 이 값으로 끝난다
import tempfile

with tempfile.TemporaryDirectory() as d:
    out = R.write_verdict(d, UNK)
    assert Path(out).name == R.VERDICT_FILE, out
    assert Path(out).read_text(encoding="utf-8") == "2"
print("  판정 파일 기록 ✅")


# ── ⑭ 결과 파일이 아예 없으면 판정 2 를 남긴다 (회귀로 단정하지 않는다)
with tempfile.TemporaryDirectory() as d:
    nunit = str(Path(d) / "unity-results.xml")      # 만들지 않는다
    ci_common.http = lambda *a, **k: (404, {})
    rc = R.main([nunit, str(Path(d) / "nope.log"), "1"])
    assert rc == 0, rc
    v = Path(d) / R.VERDICT_FILE
    assert v.exists() and v.read_text(encoding="utf-8") == "2", "판정 근거가 없으면 경고여야 한다"
print("  결과·로그 둘 다 없음 → 2 ✅")

# ── ⑮ 기준선을 **잡 목록 API** 로 찾는다 (S15P21B202-342) ─────────────────────
#    파이프라인 15개만 훑던 동안 창이 자주 비었다 — unity-compile 은 changes: METAGAME/**
#    게이트라 문서·스크립트만 머지되는 날에는 develop 에 그 잡이 아예 생기지 않는다.
#    2026-09-08 실측: 가장 최근 것이 7번째 뒤, 그 전은 14번째 뒤(창 경계).
#    기준선을 못 찾으면 판정이 2 로 떨어지고 그 잡은 비차단이라 **새 실패가 그대로 통과한다.**
JOBS_CALLS = []


def jobs_api(method, url, headers=None, body=None, timeout=30):
    JOBS_CALLS.append(url)
    if "/jobs?per_page=100&page=1" in url:
        return 200, [
            {"id": 31, "name": "mr-lint", "ref": "develop", "status": "success"},
            {"id": 30, "name": "unity-compile", "ref": "refs/merge-requests/9/head",
             "status": "failed"},                      # 다른 ref — 기준선이 아니다
            {"id": 29, "name": "unity-compile", "ref": "develop", "status": "running"},
            {"id": 28, "name": "unity-compile", "ref": "develop", "status": "failed"},
        ]
    if "/jobs/28/artifacts/unity-results.xml" in url:
        return 200, NUNIT
    if "/pipelines?ref=" in url:
        raise AssertionError("잡 목록으로 찾았으면 파이프라인을 훑지 않아야 한다")
    raise AssertionError(f"예상 못한 호출: {method} {url}")


ci_common.http = jobs_api
assert R.baseline_failures(ci_common.GitLab(), "develop") == fails
assert not any("/pipelines?ref=" in u for u in JOBS_CALLS), JOBS_CALLS
print("  기준선 — 잡 목록 API ✅")


# ── ⑯ 아티팩트가 만료된 잡은 건너뛰고 **더 옛것**을 본다
#    expire_in 을 14일로 늘렸어도 경계는 남는다. 첫 후보가 비었을 때 포기하면 안 된다.
def expired_newest(method, url, headers=None, body=None, timeout=30):
    if "/jobs?per_page=100&page=1" in url:
        return 200, [{"id": 40, "name": "unity-compile", "ref": "develop", "status": "failed"},
                     {"id": 39, "name": "unity-compile", "ref": "develop", "status": "success"}]
    if "/jobs/40/artifacts/" in url:
        return 404, {"message": "404 Not Found"}       # 만료
    if "/jobs/39/artifacts/unity-results.xml" in url:
        return 200, NUNIT
    if "/pipelines?ref=" in url:
        return 200, []
    raise AssertionError(url)


ci_common.http = expired_newest
assert R.baseline_failures(ci_common.GitLab(), "develop") == fails
print("  만료된 아티팩트 → 다음 후보 ✅")


# ── ⑰ 잡 목록이 비면 파이프라인 훑기로 되돌아간다 (둘 중 하나는 되도록)
def empty_jobs(method, url, headers=None, body=None, timeout=30):
    if "/jobs?per_page=100&page=" in url:
        return 200, []                                 # 200 이지만 비었다
    if "/pipelines?ref=" in url:
        return 200, [{"id": 899}]
    if url.endswith("/jobs?per_page=100"):
        return 200, [{"id": 20, "name": "unity-compile", "status": "failed"}]
    if "/artifacts/unity-results.xml" in url:
        return 200, NUNIT
    raise AssertionError(url)


ci_common.http = empty_jobs
assert R.baseline_failures(ci_common.GitLab(), "develop") == fails
print("  잡 목록 빔 → 파이프라인 폴백 ✅")


# ── ⑱ 판단 불가 코멘트가 **비차단이라는 사실**을 말한다 (S15P21B202-342)
#     판정 2 는 allow_failure 라 파이프라인이 초록이다. 그 사실을 적지 않으면 초록이
#     '실패를 검사했고 문제없었다' 로 읽힌다 — 실제로는 아무것도 가리지 못한 것이다.
b = R.body(data(["A.x", "A.y"]), [], None, "develop")
assert "막지 않습니다" in b, b
assert "2건" in b, b
print("  판단 불가 → 비차단임을 명시 ✅")

# 실패가 0건이면 그 경고는 없어야 한다 — 가릴 것이 없다
b0 = R.body(data([]), [], None, "develop")
assert "막지 않습니다" not in b0, b0
print("  실패 0건이면 경고 없음 ✅")


# ── ⑲ 무엇을 검사했는지 코멘트가 밝힌다 — 대상 브랜치를 합친 결과인가 (S15P21B202-342)
#     GitLab 의 MR 파이프라인은 refs/merge-requests/N/head — 소스 브랜치 끝에서 돈다.
#     2026-09-08 에 -331(테스트 7건 추가)과 -339(중력 -2.8 → -5)가 서로를 못 본 채 각자
#     초록이었고, 머지되자 3건이 깨졌다. 이제 잡이 대상 브랜치를 합친 뒤 검사하고 그 sha 를
#     UNITY_MERGED_BASE 로 넘긴다. 코멘트가 검사 범위를 밝혀야 초록의 뜻이 분명해진다.
os.environ.pop("UNITY_MERGED_BASE", None)
os.environ.pop("CI_MERGE_REQUEST_TARGET_BRANCH_NAME", None)
assert R.merged_note() == "", "MR 이 아니면 아무 말도 하지 않는다"

os.environ["CI_MERGE_REQUEST_TARGET_BRANCH_NAME"] = "develop"
assert "합치지 못해" in R.merged_note(), R.merged_note()
assert "합친 결과에서 검사했습니다" not in R.merged_note()
print("  합치지 못했으면 그렇게 적는다 ✅")

os.environ["UNITY_MERGED_BASE"] = "33d0907"
assert "33d0907" in R.merged_note() and "합친 결과" in R.merged_note(), R.merged_note()
assert "33d0907" in R.body(data(["A.x"]), [], set(), "develop"), "각주에 검사 범위가 실려야 한다"
assert "33d0907" in R.body(data(["A.x"]), [], None, "develop"), "판단 불가 코멘트에도 실려야 한다"
print("  합친 sha 를 각주에 싣는다 ✅")
os.environ.pop("UNITY_MERGED_BASE", None)
os.environ.pop("CI_MERGE_REQUEST_TARGET_BRANCH_NAME", None)

# ── ⑳ 낡은 기준선으로 회귀를 단정하지 않는다 (S15P21B202-342) ────────────────
#    ⓞ 와 ① 이 서로 얽힌다. 이제 MR 은 develop 을 합친 결과를 검사하므로 develop 의 실패를
#    함께 물려받는데, 기준선은 develop 의 *마지막* unity-compile 이고 아티팩트 14일 보관
#    덕에 며칠 전 것일 수 있다. 그 사이 develop 에 들어온 실패는 기준선에 없고 내 결과에는
#    있다 → "이 MR 이 만든 새 실패" 로 오탐되어 **남의 실패로 내 MR 이 막힌다.**
#
#    2026-09-08 실측 반례: 기준선이 한 파이프라인만 더 옛것(906fdfd2, 실패 27건)이었다면
#    -339(중력 조정)가 깨뜨린 6건이 전부 내 몫으로 계산됐다. 실제로는 8b5978e6 이 잡혀
#    그 사이 METAGAME 변경이 0건이었고 판정 0 이 나왔다 — 운이 좋았을 뿐이다.
STALE = {"job": 494810, "sha": "906fdfd2", "when": "2026-09-08T14:10"}

b = R.body(data(["A.x", "A.y"]), [], set(), "develop", stale_base=STALE)
assert "가릴 수 없습니다" in b, b
assert "906fdfd2" in b and "494810" in b, "어느 기준선이 낡았는지 밝혀야 한다"
assert "경고로 낮췄습니다" in b, b
assert "unity-compile` 을 한 번 돌려" in b, "가리는 방법을 알려 줘야 한다"
assert "🔴" not in b, "차단하지 않으므로 🔴 를 쓰지 않는다"
print("  낡은 기준선 → 코멘트가 단정하지 않는다 ✅")

# 기준선이 신선하면 평소대로 🔴
b2 = R.body(data(["A.x", "A.y"]), [], set(), "develop")
assert "🔴" in b2 and "가릴 수 없습니다" not in b2, b2
print("  신선한 기준선 → 평소대로 🔴 ✅")


# ── ㉑ METAGAME 이 안 바뀌었으면 낡은 기준선도 유효하다
#     테스트 거동이 바뀔 수 없으므로 그때는 그대로 차단해야 한다. 오늘 실제로 이 경우였다.
import subprocess as SP

_real_run = SP.run


def fake_run(argv, **kw):
    class R_:
        returncode = 0
        stdout = ""
        stderr = ""
    class Changed(R_):
        stdout = "METAGAME/Assets/_Project/Scripts/Runtime/Core/Tuning.cs"
    # a..b 가 ('old','new') 면 바뀐 것으로, ('same','new') 면 안 바뀐 것으로 답한다
    return Changed() if "old" in argv else R_()


SP.run = fake_run
R.subprocess = SP
assert R.metagame_changed("old", "new") is True
assert R.metagame_changed("same", "new") is False
assert R.metagame_changed("", "new") is None, "sha 를 모르면 판단하지 않는다"


def boom(argv, **kw):
    raise FileNotFoundError("git")


SP.run = boom
assert R.metagame_changed("a", "b") is None, "git 이 없으면 판단하지 않는다"


def rc1(argv, **kw):
    class R_:
        returncode = 128
        stdout = ""
        stderr = "bad object"
    return R_()


SP.run = rc1
assert R.metagame_changed("a", "b") is None, "git 이 실패하면 판단하지 않는다"
SP.run = _real_run
print("  METAGAME 변경 판단 — 참·거짓·판단불가 3갈래 ✅")

# ── ㉒ 컴파일 오류는 막는다. 다만 코멘트가 단정하지 않는다 (S15P21B202-342) ──
#     ⓞ(대상 브랜치를 합친 뒤 검사)가 만든 상황이다. 대상 브랜치 자신이 깨져 있으면
#     그 오류도 여기로 온다. **그래도 막는 것이 맞다** — 깨진 브랜치 위로는 아무도
#     머지하면 안 된다. 틀린 것은 판정이 아니라 "이 MR 이 깼다" 라는 문구였다.
#
#     한때 "오류 파일이 전부 이 MR 밖이면 경고로 낮춘다" 를 넣었다가 되돌렸다.
#     파일 단위 귀속은 한 방향으로만 성립한다 — A 를 바꿔 B 에서 오류가 날 수 있다
#     (시그니처 변경 → 호출부 깨짐). "오류가 내 파일 밖" 은 결백의 증거가 아닌데
#     그것으로 차단을 풀면 09-02 사고 경로가 다시 열린다.
UNITY_ERR = "Assets/_Project/Scripts/Runtime/Gameplay/Parts/LaunchTest.cs(88,17): error CS0103: 이름 'Foo'"

code, why = R.decide(data([]), [UNITY_ERR], set(), 2)
assert code == REG, (code, why)
assert "컴파일" in why, why
print("  컴파일 오류 → 차단 (기준선과 무관) ✅")

# 기준선을 못 봤어도 컴파일 오류는 막는다 — 여기서 약해지면 09-02 가 돌아온다
assert R.decide(data([]), [UNITY_ERR], None, 2)[0] == REG
assert R.decide(data(["A.x"]), [UNITY_ERR], {"A.x"}, 2)[0] == REG, "기존 실패만 있어도 컴파일 오류가 우선"
print("  기준선 없음·기존 실패만 → 그래도 차단 ✅")

# 코멘트: 막지만 대상 브랜치가 원인일 수 있음을 알린다
os.environ["UNITY_MERGED_BASE"] = "abc1234"   # 합친 경우의 문구를 본다
b = R.body(data([]), [UNITY_ERR], set(), "develop")
assert "🔴" in b, b
assert "2시간 37분" in b, "왜 급한지 근거가 있어야 한다"
assert "develop` 를 합친 결과에서 돌았습니다" in b, b
assert "최신 `unity-compile` 이 초록인지" in b, "무엇을 먼저 볼지 알려 줘야 한다"
assert UNITY_ERR in b, "오류 줄이 코멘트 맨 위에 실려야 한다"
print("  코멘트가 막으면서도 단정하지 않는다 ✅")

# 대상 브랜치 이름이 그대로 실린다 (릴리스 MR 은 master 대신 develop 기준선)
b2 = R.body(data([]), [UNITY_ERR], set(), "master")
assert "master` 를 합친 결과에서 돌았습니다" in b2, b2
os.environ.pop("UNITY_MERGED_BASE", None)
print("  대상 브랜치 이름을 그대로 쓴다 ✅")


# ── ㉓ R1 완화는 컴파일 오류에 걸리지 않는다 = R2 를 지킨다 (S15P21B202-342) ──
#     `!259` 3차 AI 리뷰가 **높음**으로 잡은 실제 결함이다. `decide` 는 컴파일 오류에도
#     REGRESSION 을 주는데, main() 의 R1 완화가 `if code == REGRESSION:` 로 조건 없이
#     걸려 있었다. 그러면 기준선이 낡았을 때 **컴파일이 깨진 MR 이 판정 2(비차단)로
#     통과한다** — 문서가 확정한 R2("컴파일은 기준선을 보지 않고 막는다")와 정면으로
#     어긋나고 09-02 사고 경로가 다시 열린다.
#
#     여기서는 main() 을 통째로 돌려 **판정 파일에 실제로 무엇이 남는지**를 본다.
#     decide/body 단위 검사로는 main() 의 게이트 실수를 못 잡는다 — 그게 이번 결함이었다.
import tempfile as _tf

_NUNIT_ERR = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" total="0" passed="0" failed="0" skipped="0" duration="0.1"></test-run>
"""
_LOG_ERR = ("Assets/_Project/Scripts/Runtime/Gameplay/Parts/LaunchTest.cs(88,17): "
            "error CS0103: The name 'Foo' does not exist in the current context")

_real_mc = R.metagame_changed
_real_bf = R.baseline_failures   # 아래에서 갈아 쓰므로 반드시 되돌린다
try:
    # 기준선은 낡았다(METAGAME 이 바뀌었다)고 답하게 만든다 — R1 이 발동할 조건
    R.metagame_changed = lambda a, b: True
    os.environ["UNITY_MERGED_BASE"] = "deadbeef"
    ci_common.http = lambda *a, **k: (404, {})      # 기준선 조회는 실패해도 무관

    with _tf.TemporaryDirectory() as d:
        nunit = os.path.join(d, "unity-results.xml")
        log = os.path.join(d, "unity.log")
        open(nunit, "w", encoding="utf-8").write(_NUNIT_ERR)
        open(log, "w", encoding="utf-8").write(_LOG_ERR)
        R.main([nunit, log, "2"])
        verdict = open(os.path.join(d, R.VERDICT_FILE), encoding="utf-8").read()
    assert verdict == str(REG), f"컴파일 오류는 낡은 기준선에도 차단해야 한다 — 판정 {verdict}"
    print("  낡은 기준선 + 컴파일 오류 → 판정 1 유지 (R2) ✅")

    # 대조: 컴파일 오류가 없고 새 실패만 있으면 R1 이 실제로 발동한다
    os.environ["CI_MERGE_REQUEST_TARGET_BRANCH_NAME"] = "develop"   # ref == tgt
    _NUNIT_NEW = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" total="1" passed="0" failed="1" skipped="0" duration="0.1">
  <test-suite type="TestFixture" name="A" fullname="A">
    <test-case id="1" name="x" fullname="A.x" result="Failed" duration="0.01">
      <failure><message>깨졌다</message></failure>
    </test-case>
  </test-suite>
</test-run>
"""
    with _tf.TemporaryDirectory() as d:
        nunit = os.path.join(d, "unity-results.xml")
        open(nunit, "w", encoding="utf-8").write(_NUNIT_NEW)
        # 기준선을 빈 집합으로 주어 'A.x' 가 새 실패가 되게 한다
        R.baseline_failures = lambda gl, ref, max_pipelines=15, meta=None: (
            meta.update(job=1, sha="0badcafe", when="2026-09-01T00:00") or set()
        ) if meta is not None else set()
        R.main([nunit, os.path.join(d, "nope.log"), "2"])
        verdict = open(os.path.join(d, R.VERDICT_FILE), encoding="utf-8").read()
    assert verdict == str(UNK), f"낡은 기준선 + 새 테스트 실패는 경고로 낮춘다(R1) — 판정 {verdict}"
    print("  낡은 기준선 + 새 테스트 실패 → 판정 2 로 완화 (R1) ✅")

    # 릴리스 MR(대상 master)에서는 R1 을 적용하지 않는다 — baseline_ref 가 develop 을
    # 쓰는데 합친 base 는 master 끝이라 두 값이 서로 다른 브랜치 위에 있다. 그 둘을 diff
    # 하면 METAGAME 이 거의 항상 다르므로, 적용하면 릴리스 MR 의 차단이 통째로 무력화된다.
    os.environ["CI_MERGE_REQUEST_TARGET_BRANCH_NAME"] = "master"     # ref=develop != tgt=master
    with _tf.TemporaryDirectory() as d:
        nunit = os.path.join(d, "unity-results.xml")
        open(nunit, "w", encoding="utf-8").write(_NUNIT_NEW)
        R.main([nunit, os.path.join(d, "nope.log"), "2"])
        verdict = open(os.path.join(d, R.VERDICT_FILE), encoding="utf-8").read()
    assert verdict == str(REG), f"릴리스 MR 에서는 R1 을 적용하지 않는다 — 판정 {verdict}"
    print("  릴리스 MR(ref≠tgt) → R1 적용 안 함, 차단 유지 ✅")
finally:
    R.metagame_changed = _real_mc
    R.baseline_failures = _real_bf
    os.environ.pop("CI_MERGE_REQUEST_TARGET_BRANCH_NAME", None)
    os.environ.pop("UNITY_MERGED_BASE", None)

# 컴파일 코멘트가 합치지 못한 경우를 단정하지 않는다 (3차 리뷰 중간 ③)
os.environ["UNITY_MERGED_BASE"] = "abc1234"
b = R.body(data([]), [_LOG_ERR], set(), "develop")
assert "합친 결과에서 돌았습니다" in b, b
os.environ.pop("UNITY_MERGED_BASE", None)
b2 = R.body(data([]), [_LOG_ERR], set(), "develop")
assert "브랜치 끝에서만" in b2 and "합친 결과에서 돌았습니다" not in b2, b2
print("  컴파일 코멘트가 검사 범위를 정직하게 말한다 ✅")

# ── ㉔ 기준선 후보에 상한이 있다 (S15P21B202-342) ──────────────────────────
#     잡 목록 API 는 최대 800건을 준다. 그중 unity-compile 이 수십~수백 건이면 아티팩트가
#     만료된 옛 잡마다 404 요청이 한 번씩 나가 잡이 오래 매달린다(14일 이전은 전부 만료).
#     `!259` 3차 AI 리뷰가 중간으로 잡았다.
ART_CALLS = []


def many_expired(method, url, headers=None, body=None, timeout=30):
    if "/jobs?per_page=100&page=1" in url:
        return 200, [{"id": 1000 + k, "name": "unity-compile", "ref": "develop", "status": "failed"}
                     for k in range(300)]
    if "/jobs?per_page=100&page=" in url:
        return 200, []
    if "/artifacts/" in url:
        ART_CALLS.append(url)
        return 404, {"message": "404"}          # 전부 만료
    if "/pipelines?ref=" in url:
        return 200, []
    raise AssertionError(url)


ci_common.http = many_expired
assert R.baseline_failures(ci_common.GitLab(), "develop") is None
assert len(ART_CALLS) == R.MAX_BASELINE_CANDIDATES,     f"후보 {R.MAX_BASELINE_CANDIDATES}개까지만 열어야 한다 — 실제 {len(ART_CALLS)}회"
print(f"  후보 300개 → 아티팩트 요청 {len(ART_CALLS)}회로 제한 ✅")


# ── ㉕ 검사를 못 했으면 **그 사실을 코멘트로 남긴다** (S15P21B202-342) ────────
#     `!259` 5차 AI 리뷰가 잡았다. 워크트리에 충돌이 남으면 잡이 Unity 를 돌리지 않고
#     끝나는데, 판정 2 는 비차단이라 파이프라인이 초록이다. 코멘트가 없으면
#     "문서만 바뀌어 잡이 안 돌았다" 와 구분되지 않는다 — **이 MR 이 없애려던 침묵과
#     같은 모양이다.** `-335` 가 AI 리뷰에서 고친 것과 같은 종류다.
POSTED = []


def capture_note(method, url, headers=None, body=None, timeout=30):
    if "/notes" in url:
        POSTED.append((method, body))
        return (200, {"id": 1}) if method in ("POST", "PUT") else (200, [])
    return 404, {}


ci_common.http = capture_note
os.environ["UNITY_ABORTED"] = "대상 브랜치를 합치다 충돌이 남아 워크트리를 되돌리지 못했다"
with tempfile.TemporaryDirectory() as d:
    # 결과 파일이 아예 없는 상태 — 잡이 Unity 를 돌리지 못했다
    rc = R.main([os.path.join(d, "unity-results.xml"), os.path.join(d, "unity.log"), "2"])
    assert rc == 0, rc
    v = Path(d) / R.VERDICT_FILE
    assert v.exists() and v.read_text(encoding="utf-8") == str(UNK), "판정 2 를 남겨야 한다"
os.environ.pop("UNITY_ABORTED", None)

assert POSTED, "검사를 못 했으면 코멘트를 남겨야 한다 — 조용히 끝내면 안 된다"
sent = str(POSTED[-1][1])
assert "검사를 하지 못했습니다" in sent, sent
assert "충돌이 남아" in sent, "사유가 실려야 한다"
assert "머지를 막지 않으므로" in sent, "비차단이라는 사실을 말해야 한다"
assert R.MARKER in sent, "같은 마커로 갱신되어야 한다"
print("  검사 못 함 → 판정 2 + 사유 코멘트 ✅")


# ── S15P21B202-347 · 비결정 테스트가 무관한 MR 을 막지 않게 한다 ──────────────
#
# 판정("기준선에 없던 실패는 이 MR 탓")은 실행이 결정적이라는 것을 전제한다. 물리
# 시뮬레이션 테스트 몇 개가 경계에 앉아 있어 그 전제가 깨져 있다. 2026-09-09 실측:
# `!267` 의 METAGAME 트리가 기준선 잡과 **바이트 단위로 동일**한데(merge-base 가 develop
# 팁이고 diff 0) 같은 커밋으로 4회 돌려 4회 다 실패했고, MR 3건이 동시에 막혔다.
#
# 두 장치를 함께 넣는다. **둘 다 필요하다** — 그날 건은 기준선 합집합만으로는 안 풀렸다
# (최근 develop 3회가 전부 통과 쪽이었다).
print("― S15P21B202-347 플래키 판별 ―")

_RECHECK_A = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="9" total="2" passed="1" failed="1" skipped="0" duration="0.4">
  <test-suite type="TestFixture" name="ShipAscentTests" fullname="G.ShipAscentTests">
    <test-case id="1" name="Hatch" fullname="G.ShipAscentTests.Hatch" result="Passed" duration="0.2" />
    <test-case id="2" name="Load" fullname="G.ShipAscentTests.Load" result="Failed" duration="0.2">
      <failure><message>버티지 못했다</message></failure>
    </test-case>
  </test-suite>
</test-run>
"""
_RECHECK_B = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="9" total="2" passed="0" failed="2" skipped="0" duration="0.4">
  <test-suite type="TestFixture" name="ShipAscentTests" fullname="G.ShipAscentTests">
    <test-case id="1" name="Hatch" fullname="G.ShipAscentTests.Hatch" result="Failed" duration="0.2">
      <failure><message>또 버티지 못했다</message></failure>
    </test-case>
    <test-case id="2" name="Load" fullname="G.ShipAscentTests.Load" result="Failed" duration="0.2">
      <failure><message>버티지 못했다</message></failure>
    </test-case>
  </test-suite>
</test-run>
"""

# ① 재실행 결과를 세 갈래로 나눈다
with _tf.TemporaryDirectory() as d:
    pa = os.path.join(d, "r1.xml")
    open(pa, "w", encoding="utf-8").write(_RECHECK_A)
    pb = os.path.join(d, "r2.xml")
    open(pb, "w", encoding="utf-8").write(_RECHECK_B)
    new = {"G.ShipAscentTests.Hatch", "G.ShipAscentTests.Load", "G.OtherTests.Gone"}
    flaky, still, unver = R.classify_rechecks(new, [pa, pb])
    assert flaky == {"G.ShipAscentTests.Hatch"}, flaky
    assert still == {"G.ShipAscentTests.Load"}, still
    assert unver == {"G.OtherTests.Gone"}, unver
print("  한 번이라도 통과 → 플래키 · 전부 실패 → 회귀 · 결과에 없음 → 확인 못함 ✅")

# 재실행을 아예 안 돌렸으면 아무것도 면제하지 않는다 — 기본값이 "막는다" 쪽이어야 한다
_f, _s, _u = R.classify_rechecks({"A.b"}, [])
assert _f == set() and _s == {"A.b"} and _u == set(), (_f, _s, _u)
print("  재실행이 없으면 면제 없음 ✅")

# 결과가 깨졌거나 없으면 면제하지 않는다 — 확인 못 한 것을 면제하면 회귀가 빠져나간다
with _tf.TemporaryDirectory() as d:
    bad = os.path.join(d, "bad.xml")
    open(bad, "w", encoding="utf-8").write("<<<깨진 XML")
    _f, _s, _u = R.classify_rechecks({"A.b"}, [bad, os.path.join(d, "없는파일.xml")])
    assert _f == set(), _f
    assert _u == {"A.b"}, _u
print("  재실행 결과가 깨졌거나 없으면 면제하지 않는다 ✅")

# ② 판정이 플래키를 뺀다
_D = {"total": 2, "passed": 0, "failed": 2, "skipped": 0, "duration": 1.0, "cases": [
    {"cls": "G.ShipAscentTests", "name": "Hatch", "result": "Failed", "msg": "x", "dur": 0.1},
    {"cls": "G.ShipAscentTests", "name": "Load", "result": "Failed", "msg": "y", "dur": 0.1},
]}
code, why = R.decide(_D, [], set(), 2, flaky={"G.ShipAscentTests.Hatch"})
assert code == REG, (code, why)
assert "1건" in why, why
print("  플래키를 빼도 남은 새 실패가 있으면 막는다 ✅")

code, why = R.decide(_D, [], set(), 2,
                     flaky={"G.ShipAscentTests.Hatch", "G.ShipAscentTests.Load"})
assert code == 0, (code, why)
assert "플래키" in why, why
print("  새 실패가 전부 플래키면 통과 ✅")

# 컴파일 오류에는 면제가 끼어들지 않는다 — 09-02 사고 경로를 다시 열면 안 된다
code, why = R.decide(_D, ["error CS0103: 뭔가"], set(), 2,
                     flaky={"G.ShipAscentTests.Hatch", "G.ShipAscentTests.Load"})
assert code == REG and "컴파일" in why, (code, why)
print("  컴파일 오류는 플래키와 무관하게 막는다 ✅")

# ③ 기준선은 최근 실행의 합집합이다
_B1 = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="1" total="1" passed="0" failed="1" skipped="0" duration="0.1">
  <test-suite type="TestFixture" name="T" fullname="G.T">
    <test-case id="1" name="a" fullname="G.T.a" result="Failed" duration="0.1">
      <failure><message>a</message></failure>
    </test-case>
  </test-suite>
</test-run>
"""
_B2 = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="1" total="1" passed="0" failed="1" skipped="0" duration="0.1">
  <test-suite type="TestFixture" name="T" fullname="G.T">
    <test-case id="1" name="b" fullname="G.T.b" result="Failed" duration="0.1">
      <failure><message>b</message></failure>
    </test-case>
  </test-suite>
</test-run>
"""


def _union_http(method, url, headers=None, body=None, timeout=30):
    if "/jobs?per_page=100&page=" in url:
        # "page=" 로 자르면 "per_page=" 에도 걸린다 — 앞의 & 까지 넣어야 한다
        page = url.split("&page=")[1].split("&")[0]
        if page != "1":
            return 200, []
        return 200, [
            {"id": 71, "name": "unity-compile", "ref": "develop", "status": "success",
             "created_at": "2026-09-09T09:45:00", "commit": {"id": "aaaaaaaa1111"}},
            {"id": 70, "name": "unity-compile", "ref": "develop", "status": "success",
             "created_at": "2026-09-09T09:41:00", "commit": {"id": "bbbbbbbb2222"}},
        ]
    if "/jobs/71/artifacts/unity-results.xml" in url:
        return 200, _B1
    if "/jobs/70/artifacts/unity-results.xml" in url:
        return 200, _B2
    raise AssertionError("예상 못한 호출: %s %s" % (method, url))


_keep_http = ci_common.http
try:
    ci_common.http = _union_http
    meta = {}
    got = R.baseline_failures(ci_common.GitLab(), "develop", meta=meta)
    assert got == {"G.T.a", "G.T.b"}, got
    # 신선도 검사는 **가장 최신 실행**을 봐야 한다 — 합집합에 옛 실행이 섞였어도 그렇다.
    assert meta.get("sha") == "aaaaaaaa", meta
    assert meta.get("runs") == ["71", "70"], meta
    # runs=1 이면 예전과 같다 — 되돌릴 수 있는 장치라는 것을 못 박아 둔다
    one = R.baseline_failures(ci_common.GitLab(), "develop", runs=1)
    assert one == {"G.T.a"}, one
finally:
    ci_common.http = _keep_http
print("  기준선 = 최근 실행 합집합 · 신선도는 최신 실행 기준 · runs=1 은 예전 동작 ✅")

# ④ 계획 단계는 판정도 코멘트도 하지 않는다
_PLAN_NUNIT = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="3" total="1" passed="0" failed="1" skipped="0" duration="0.2">
  <test-suite type="TestFixture" name="ShipAscentTests" fullname="G.ShipAscentTests">
    <test-case id="1" name="Load" fullname="G.ShipAscentTests.Load" result="Failed" duration="0.2">
      <failure><message>버티지 못했다</message></failure>
    </test-case>
  </test-suite>
</test-run>
"""
_keep_bf = R.baseline_failures
_before = len(POSTED)
try:
    R.baseline_failures = lambda gl, ref, max_pipelines=15, meta=None, runs=3: set()
    with _tf.TemporaryDirectory() as d:
        nunit = os.path.join(d, "unity-results.xml")
        open(nunit, "w", encoding="utf-8").write(_PLAN_NUNIT)
        plan = os.path.join(d, "unity-recheck.txt")
        rc = R.main([nunit, os.path.join(d, "nope.log"), "2", "--recheck-plan", plan])
        assert rc == 0, rc
        got = open(plan, encoding="utf-8").read()
        assert got.split() == ["G.ShipAscentTests.Load"], got
        assert not (Path(d) / R.VERDICT_FILE).exists(), \
            "계획 단계에서 판정을 남기면 잡이 재실행 전에 끝난다"
    assert len(POSTED) == _before, "계획 단계는 코멘트를 남기지 않는다"
finally:
    R.baseline_failures = _keep_bf
print("  --recheck-plan 은 이름만 적고 판정·코멘트를 하지 않는다 ✅")

# ⑤ 면제한 것을 코멘트가 드러낸다 — 조용히 빼면 아무도 테스트를 고치지 않는다
_txt = R.body(_D, [], set(), "develop", flaky={"G.ShipAscentTests.Hatch"})
assert "플래키로 판정에서 뺀 1건" in _txt, _txt
assert "Hatch" in _txt, _txt
assert "실행 맥락에 달려" in _txt, _txt
# **격리 통과를 무죄로 단정하지 않는다** — 이 MR 이 상태를 오염시킨 경우도 격리에서는
# 통과한다(!272 3차 리뷰). 코멘트가 그 갈래와 가리는 방법을 적어야 한다.
assert "전역·정적 상태를 오염" in _txt, _txt
assert "METAGAME/` 을 하나도 건드리지 않았다면" in _txt, _txt
# 후속 이슈를 가리켜야 한다 — -347 은 곧 닫히므로 그걸 가리키면 아무도 안 고친다
assert "S15P21B202-360" in _txt, _txt
print("  코멘트가 면제한 건을 드러낸다 ✅")

_txt2 = R.body(_D, [], set(), "develop",
               flaky={"G.ShipAscentTests.Hatch", "G.ShipAscentTests.Load"})
assert "재실행에서 통과" in _txt2, _txt2
assert "실패 없음" in _txt2, _txt2
print("  전부 플래키여도 '실패 없음' 뒤에 이유가 붙는다 ✅")

import json as _json

# ── !272 AI 리뷰 반영 ────────────────────────────────────────────────────────

# ⑥ 필터가 아무것도 못 잡으면 전부 '확인 못함' 이고, 그 사실이 크게 남아야 한다.
#    -testFilter 구분자가 이 Unity 버전에서 안 통하면 이렇게 된다 — 조용히 넘기면
#    이 장치가 있으나 마나 해진다.
_OTHER = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="9" total="1" passed="1" failed="0" skipped="0" duration="0.1">
  <test-suite type="TestFixture" name="Zzz" fullname="G.Zzz">
    <test-case id="1" name="q" fullname="G.Zzz.q" result="Passed" duration="0.1" />
  </test-suite>
</test-run>
"""
with _tf.TemporaryDirectory() as d:
    p = os.path.join(d, "unity-recheck-1.xml")
    open(p, "w", encoding="utf-8").write(_OTHER)
    _f, _s, _u = R.classify_rechecks({"G.ShipAscentTests.Load"}, [p])
    assert _f == set(), _f
    assert _u == {"G.ShipAscentTests.Load"}, _u
    assert _s == set(), _s
print("  필터가 대상을 못 잡으면 면제 없이 전부 '확인 못함' ✅")

# ⑦ 계획 단계가 기준선을 고정하고, 최종 판정이 그것을 그대로 쓴다(재조회 안 함).
#    두 단계가 각각 조회하면 그 사이 develop 잡이 끝났을 때 재실행한 집합과 판정
#    대상이 어긋난다.
_PIN_NUNIT = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="4" total="2" passed="0" failed="2" skipped="0" duration="0.4">
  <test-suite type="TestFixture" name="ShipAscentTests" fullname="G.ShipAscentTests">
    <test-case id="1" name="Load" fullname="G.ShipAscentTests.Load" result="Failed" duration="0.2">
      <failure><message>버티지 못했다</message></failure>
    </test-case>
    <test-case id="2" name="Old" fullname="G.ShipAscentTests.Old" result="Failed" duration="0.2">
      <failure><message>원래 실패</message></failure>
    </test-case>
  </test-suite>
</test-run>
"""


def _boom_http(method, url, headers=None, body=None, timeout=30):
    # 코멘트 경로는 정상 동작해야 한다 — 막을 것은 **기준선 재조회**뿐이다.
    if "/notes" in url:
        return capture_note(method, url, headers, body, timeout)
    raise AssertionError("기준선을 고정했는데 다시 조회했다: %s" % url)


_keep_bf2 = R.baseline_failures
_keep_http2 = ci_common.http
_before2 = len(POSTED)
try:
    # 계획 단계: 기준선에 Old 만 있다 → 새 실패는 Load 하나
    R.baseline_failures = (lambda gl, ref, max_pipelines=15, meta=None, runs=3:
                           (meta.update(job=1, sha="deadbeef", when="2026-09-09T09:45")
                            if meta is not None else None) or {"G.ShipAscentTests.Old"})
    with _tf.TemporaryDirectory() as d:
        nunit = os.path.join(d, "unity-results.xml")
        open(nunit, "w", encoding="utf-8").write(_PIN_NUNIT)
        plan = os.path.join(d, "unity-recheck.txt")
        R.main([nunit, os.path.join(d, "nope.log"), "2", "--recheck-plan", plan])
        assert open(plan, encoding="utf-8").read().split() == ["G.ShipAscentTests.Load"]
        pinned = os.path.join(d, R.BASELINE_FILE)
        assert os.path.isfile(pinned), "계획 단계가 기준선을 고정하지 않았다"
        saved = _json.load(open(pinned, encoding="utf-8"))
        assert saved["base"] == ["G.ShipAscentTests.Old"], saved
        assert saved["meta"]["sha"] == "deadbeef", saved

        # 최종 판정: 고정 파일을 주면 **조회하지 않는다.** 조회하면 _boom_http 가 터진다.
        R.baseline_failures = _boom_http
        ci_common.http = _boom_http
        r1 = os.path.join(d, "unity-recheck-1.xml")
        open(r1, "w", encoding="utf-8").write(_RECHECK_A.replace("Hatch", "Load"))
        # 이 테스트가 보는 것은 **기준선 고정**이다. 판정 낮추기(!272 4차)와 엉키지
        # 않게 "이 MR 은 METAGAME/ 을 안 바꿨다" 를 명시한다 — 그 경우 0 이 맞다.
        _mc = R.metagame_changed
        R.metagame_changed = lambda a, b: False
        os.environ["CI_MERGE_REQUEST_DIFF_BASE_SHA"] = "aaaaaaa1"
        os.environ["CI_COMMIT_SHA"] = "bbbbbbb2"
        try:
            R.main([nunit, os.path.join(d, "nope.log"), "2",
                    "--recheck-dir", d, "--baseline-from", pinned])
        finally:
            R.metagame_changed = _mc
            os.environ.pop("CI_MERGE_REQUEST_DIFF_BASE_SHA", None)
            os.environ.pop("CI_COMMIT_SHA", None)
        verdict = open(os.path.join(d, R.VERDICT_FILE), encoding="utf-8").read()
    # Load 는 재실행에서 통과했다(_RECHECK_A 의 Hatch 자리) → 면제되어 통과
    assert verdict == "0", f"고정 기준선 + 재실행 통과 → 판정 0 이어야 한다 (판정 {verdict})"
    assert len(POSTED) > _before2, "최종 판정은 코멘트를 남긴다"
    assert "플래키로 판정에서 뺀" in str(POSTED[-1][1]), str(POSTED[-1][1])[:300]
finally:
    R.baseline_failures = _keep_bf2
    ci_common.http = _keep_http2
print("  --baseline-from 이 기준선을 고정한다(재조회 없음) · --recheck-dir 이 결과를 모은다 ✅")

# 고정 파일이 깨졌거나 없으면 **다시 조회한다** — 조용히 기준선 없음으로 떨어지지 않는다
_keep_bf3 = R.baseline_failures
_asked = []
try:
    def _spy(gl, ref, max_pipelines=15, meta=None, runs=3):
        _asked.append(ref)
        return set()
    R.baseline_failures = _spy
    with _tf.TemporaryDirectory() as d:
        nunit = os.path.join(d, "unity-results.xml")
        open(nunit, "w", encoding="utf-8").write(_PIN_NUNIT)
        bad = os.path.join(d, "unity-baseline.json")
        open(bad, "w", encoding="utf-8").write("{깨진 JSON")
        R.main([nunit, os.path.join(d, "nope.log"), "2", "--baseline-from", bad])
    assert _asked, "고정 파일이 깨졌으면 다시 조회해야 한다"
finally:
    R.baseline_failures = _keep_bf3
print("  고정 파일이 깨지면 다시 조회한다 ✅")


# ⑧ 재실행에서 Skipped·Inconclusive 는 **통과로 세지 않는다** (`!272` AI 리뷰).
#    스킵된 것을 면제하면 진짜 회귀가 판정에서 빠진다.
_SKIPPED = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="9" total="2" passed="0" failed="0" skipped="2" duration="0.1">
  <test-suite type="TestFixture" name="ShipAscentTests" fullname="G.ShipAscentTests">
    <test-case id="1" name="Load" fullname="G.ShipAscentTests.Load" result="Skipped" duration="0" />
    <test-case id="2" name="Hatch" fullname="G.ShipAscentTests.Hatch" result="Inconclusive" duration="0" />
  </test-suite>
</test-run>
"""
with _tf.TemporaryDirectory() as d:
    p = os.path.join(d, "unity-recheck-1.xml")
    open(p, "w", encoding="utf-8").write(_SKIPPED)
    new = {"G.ShipAscentTests.Load", "G.ShipAscentTests.Hatch"}
    _f, _s, _u = R.classify_rechecks(new, [p])
    assert _f == set(), f"스킵을 면제하면 안 된다: {_f}"
    assert _u == new, f"통과도 실패도 아니면 '확인 못함' 이어야 한다: {_u}"
print("  Skipped·Inconclusive 는 면제하지 않고 '확인 못함' 으로 둔다 ✅")

# 통과가 섞여 있으면 그것만 면제한다
_MIXED = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="9" total="2" passed="1" failed="0" skipped="1" duration="0.1">
  <test-suite type="TestFixture" name="ShipAscentTests" fullname="G.ShipAscentTests">
    <test-case id="1" name="Load" fullname="G.ShipAscentTests.Load" result="Passed" duration="0.1" />
    <test-case id="2" name="Hatch" fullname="G.ShipAscentTests.Hatch" result="Skipped" duration="0" />
  </test-suite>
</test-run>
"""
with _tf.TemporaryDirectory() as d:
    p = os.path.join(d, "unity-recheck-1.xml")
    open(p, "w", encoding="utf-8").write(_MIXED)
    _f, _s, _u = R.classify_rechecks({"G.ShipAscentTests.Load", "G.ShipAscentTests.Hatch"}, [p])
    assert _f == {"G.ShipAscentTests.Load"}, _f
    assert _u == {"G.ShipAscentTests.Hatch"}, _u
print("  통과한 것만 면제하고 스킵된 것은 회귀로 남는다 ✅")

# ⑨ 계획 옵션과 재실행 옵션을 함께 주면 **말한다** — 조용히 무시되면 통과처럼 보인다
_keep_bf4 = R.baseline_failures
try:
    R.baseline_failures = lambda gl, ref, max_pipelines=15, meta=None, runs=3: set()
    with _tf.TemporaryDirectory() as d:
        nunit = os.path.join(d, "unity-results.xml")
        open(nunit, "w", encoding="utf-8").write(_PLAN_NUNIT)
        open(os.path.join(d, "unity-recheck-1.xml"), "w", encoding="utf-8").write(_RECHECK_A)
        plan = os.path.join(d, "unity-recheck.txt")
        import io as _io2
        import contextlib as _cl
        _buf = _io2.StringIO()
        with _cl.redirect_stdout(_buf):
            R.main([nunit, os.path.join(d, "nope.log"), "2",
                    "--recheck-plan", plan, "--recheck-dir", d])
        assert "--recheck-plan 과 재실행 옵션을 함께" in _buf.getvalue(), _buf.getvalue()[-400:]
finally:
    R.baseline_failures = _keep_bf4
print("  계획 옵션 + 재실행 옵션을 함께 주면 경고한다 ✅")


# ⑩ -testFilter 값은 **정규식 이스케이프**되어야 한다 (!272 3차 리뷰).
#    파라미터화 이름(`Load(1,2)`)을 그대로 넘기면 매칭에 실패하고, 그러면 전원
#    '확인 못함' → 회귀가 되어 이 장치가 무력화된다.
_fv = R.testfilter_value({"G.T.Load(1,2)", "G.T.Plain"})
assert "G\.T\.Load" in _fv, _fv
assert "\(1" in _fv, _fv
assert _fv.count(";") == 1, _fv
import re as _re
for _n in ("G.T.Load(1,2)", "G.T.Plain"):
    assert any(_re.fullmatch(_p, _n) for _p in _fv.split(";")), (_n, _fv)
assert R.testfilter_value(set()) == "", R.testfilter_value(set())
print("  -testFilter 값이 정규식 이스케이프된다 ✅")


# ⑪ 면제했는데 **이 MR 이 게임 코드를 건드렸으면** 판정을 2(경고)로 낮춘다 (!272 4차 리뷰).
#    격리 재실행 통과는 "실패가 실행 맥락에 달렸다" 는 뜻이고, 그 맥락이 이 MR 이 만든
#    전역·정적 상태 오염일 수 있다. METAGAME/ 을 안 바꿨으면 그 경우가 불가능하므로 0.
_keep_bf5 = R.baseline_failures
_keep_mc5 = R.metagame_changed
_MINE = "aaaaaaa1"
_HEAD = "bbbbbbb2"


def _run_flaky_case(touched):
    """새 실패 하나가 재실행에서 통과하는 상황을 만들고 판정을 돌려준다."""
    R.metagame_changed = lambda a, b: touched
    with _tf.TemporaryDirectory() as d:
        nunit = os.path.join(d, "unity-results.xml")
        open(nunit, "w", encoding="utf-8").write(_PLAN_NUNIT)   # G.ShipAscentTests.Load 실패
        r1 = os.path.join(d, "unity-recheck-1.xml")
        open(r1, "w", encoding="utf-8").write(_RECHECK_A.replace("Hatch", "Load"))
        R.main([nunit, os.path.join(d, "nope.log"), "2", "--recheck-dir", d])
        return open(os.path.join(d, R.VERDICT_FILE), encoding="utf-8").read()


try:
    R.baseline_failures = lambda gl, ref, max_pipelines=15, meta=None, runs=3: set()
    os.environ["CI_MERGE_REQUEST_DIFF_BASE_SHA"] = _MINE
    os.environ["CI_COMMIT_SHA"] = _HEAD

    assert _run_flaky_case(False) == "0", "METAGAME/ 을 안 바꿨으면 0 을 유지해야 한다"
    assert _run_flaky_case(True) == str(UNK), (
        "METAGAME/ 을 바꿨으면 상태 오염 가능성이 남아 2(경고)로 낮춰야 한다")
    assert _run_flaky_case(None) == str(UNK), (
        "판단하지 못했으면 낮추는 쪽으로 틀린다")

    # diff base 를 모르면(로컬 실행 등) 판단할 수 없으니 낮춘다
    os.environ.pop("CI_MERGE_REQUEST_DIFF_BASE_SHA", None)
    assert _run_flaky_case(False) == str(UNK), "diff base 가 없으면 낮춘다"
finally:
    R.baseline_failures = _keep_bf5
    R.metagame_changed = _keep_mc5
    os.environ.pop("CI_MERGE_REQUEST_DIFF_BASE_SHA", None)
    os.environ.pop("CI_COMMIT_SHA", None)
print("  면제 + 게임 코드 변경 → 판정 2 로 낮춘다 · 변경 없으면 0 유지 ✅")

print("TEST271 OK")


# ── 코멘트를 올려도 **로그에 이유가 남는다** (S15P21B202-556) ──────────────────
#
# 🔴 카드는 잡 로그에서 `❌`·`🔴` 줄을 뽑는다(`mm_notify.REASON_MARKERS`). 전에는
# 코멘트를 올릴 수 있으면(= MR 파이프라인) 로그에 "MR 코멘트 갱신" 한 줄만 남겨서,
# **MR 에서만 카드가 "로그에서 이유를 찾지 못함"** 이 됐다 — 정작 작성자에게 필요한
# 쪽이다. develop push 는 MR iid 가 없어 else 로 가서 멀쩡했다.
#
# 2026-09-15 실측: 잡 540606(MR)에는 마커 줄이 0개, 538054(develop push)에는 있었다.
import contextlib  # noqa: E402
import io as _io  # noqa: E402

NOTES = []


def note_api(method, url, headers=None, body=None, timeout=30):
    if "/notes" in url:
        if method == "GET":
            return 200, []
        NOTES.append((body or {}).get("body", ""))
        return 201, {"id": 1}
    return 404, {}


def run_main(nunit, log, rc):
    """main() 을 돌리고 **로그로 찍힌 것**을 돌려준다."""
    buf = _io.StringIO()
    with contextlib.redirect_stdout(buf):
        code = R.main([nunit, log, rc])
    return code, buf.getvalue()


for name, var in (("MR 파이프라인", "77"), ("develop push", "")):
    with tempfile.TemporaryDirectory() as d:
        os.environ["GITLAB_TOKEN"] = "t"
        os.environ["CI_PROJECT_ID"] = "1"
        if var:
            os.environ["CI_MERGE_REQUEST_IID"] = var
        else:
            os.environ.pop("CI_MERGE_REQUEST_IID", None)
        ci_common.http = note_api
        R.http = note_api

        log = str(Path(d) / "unity.log")
        Path(log).write_text(
            "Assets/X.cs(1,2): error CS1501: No overload for method 'Y' takes 2 arguments" + chr(10),
            encoding="utf-8")
        NOTES.clear()
        code, printed = run_main(str(Path(d) / "unity-results.xml"), log, "1")

        assert code == 0, (name, code)
        marked = [x for x in printed.split(chr(10)) if "🔴" in x or "❌" in x]
        assert marked, (
            name + ": 잡 로그에 마커 줄이 없다 — 카드가 '이유를 찾지 못함' 이 된다"
            + chr(10) + printed[-400:])
        assert "error CS1501" in printed, name + ": 컴파일 오류 줄이 로그에 없다"
        print(f"  {name} — 로그에 이유가 남는다 ({len(marked)}줄) ✅")

        # **로그에 찍느라 코멘트를 잃으면 안 된다.** 둘은 배타적이 아니다 —
        # 코멘트는 사람이 MR 에서 읽는 것, 로그는 카드가 읽는 것.
        if var:
            assert NOTES, name + ": 코멘트가 안 올라갔다"
            assert "error CS1501" in NOTES[0], name + ": 코멘트에 오류가 없다"
            print("  코멘트도 그대로 올라간다 ✅")
        else:
            assert not NOTES, name + ": MR iid 가 없는데 코멘트를 올렸다"
