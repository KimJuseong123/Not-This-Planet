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

# ── ㉒ 남의 컴파일 오류로 사람을 막지 않는다 (S15P21B202-342) ────────────────
#     ⓞ 가 만든 위험이다. decide 는 컴파일 오류를 기준선과 견주지 않고 바로 차단하는데,
#     그 근거는 "develop 이 컴파일되면 이 MR 이 깬 것" 이었다. 이제 잡이 develop 을 합친
#     뒤 검사하므로 **develop 자신의 컴파일 오류가 이 MR 의 오류로 나타난다.** 09-02 에
#     develop 은 2시간 37분 깨져 있었다 — 그 사이 올라온 모든 MR 이 "이 MR 이 깼다" 라는
#     사실이 아닌 이유로 막힌다. `!259` 의 AI 리뷰가 이 구멍을 지적했다.
UNITY_ERR = "Assets/_Project/Scripts/Runtime/Gameplay/Parts/LaunchTest.cs(88,17): error CS0103: 이름 'Foo'"
WIN_ERR = "METAGAME\\Assets\\_Project\\Scripts\\Runtime\\Core\\Tuning.cs(12,5): error CS1002: ; expected"

assert R.error_files([UNITY_ERR]) == {"Assets/_Project/Scripts/Runtime/Gameplay/Parts/LaunchTest.cs"}
assert R.error_files([WIN_ERR]) == {"METAGAME/Assets/_Project/Scripts/Runtime/Core/Tuning.cs"}
assert R.error_files(["error CS9999: 경로가 없는 줄"]) == set()
print("  오류 줄에서 파일 경로 추출 (유닉스·윈도우·경로없음) ✅")

_own = R.mr_own_files


def own(files):
    R.mr_own_files = lambda target: (set(files) if target else None)


# 오류 파일이 이 MR 것 → 우리 것
own({"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/LaunchTest.cs", "docs/x.md"})
assert R.errors_are_ours([UNITY_ERR], "develop") is True
print("  오류가 내 파일 → True(차단) ✅")

# 오류 파일이 이 MR 밖 → 대상 브랜치 문제
own({"scripts/ci/unity_report.py", "docs/x.md"})
assert R.errors_are_ours([UNITY_ERR], "develop") is False
print("  오류가 내 파일 밖 → False(경고로) ✅")

# 하나라도 우리 것이면 우리 것 — 애매할 때 통과시키지 않는다
own({"METAGAME/Assets/_Project/Scripts/Runtime/Core/Tuning.cs"})
assert R.errors_are_ours([UNITY_ERR, WIN_ERR], "develop") is True
print("  섞여 있으면 → True ✅")

# 판단 못 하는 두 경우는 None — 단정하지 않는다
own(None)
R.mr_own_files = lambda target: None
assert R.errors_are_ours([UNITY_ERR], "develop") is None
own({"a.cs"})
assert R.errors_are_ours(["error CS9999: 경로가 없는 줄"], "develop") is None
print("  파일 목록·경로를 못 읽으면 → None ✅")

# decide: None 이면 지금까지처럼 차단한다 (기본을 약하게 만들지 않는다)
R.mr_own_files = lambda target: None
assert R.decide(data([]), [UNITY_ERR], set(), 2, target="develop")[0] == REG
print("  판단 불가 → 차단 유지 ✅")

# decide: 남의 오류면 2(경고)
R.mr_own_files = lambda target: {"scripts/ci/unity_report.py"}
code, why = R.decide(data([]), [UNITY_ERR], set(), 2, target="develop")
assert code == UNK and "바꾸지 않은 파일" in why, (code, why)
print("  남의 컴파일 오류 → 판정 2 ✅")

# 코멘트도 그렇게 말한다
os.environ["CI_MERGE_REQUEST_TARGET_BRANCH_NAME"] = "develop"
b = R.body(data([]), [UNITY_ERR], set(), "develop")
assert "이 MR 이 바꾼 파일이 아닙니다" in b and "전부 이 MR 밖" in b, b
assert "🔴" not in b, "차단하지 않으므로 🔴 를 쓰지 않는다"
R.mr_own_files = lambda target: {"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/LaunchTest.cs"}
b2 = R.body(data([]), [UNITY_ERR], set(), "develop")
assert "🔴" in b2 and "2시간 37분" in b2, b2
os.environ.pop("CI_MERGE_REQUEST_TARGET_BRANCH_NAME", None)
print("  코멘트가 남의 오류와 내 오류를 가른다 ✅")
R.mr_own_files = _own

print("TEST271 OK")
