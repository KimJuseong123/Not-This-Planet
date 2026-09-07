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

print("TEST271 OK")
