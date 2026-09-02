"""Unity 결과를 MR 에서 보이게 하는 보고 (S15P21B202-271).

실행: python scripts/ci/tests/test_unity_report.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

왜 이 장치가 있는가 — Unity 는 NUnit3(`<test-run>`)를 내는데 GitLab 은 JUnit(`<testsuite>`)만
파싱한다. 그래서 `reports: junit:` 을 걸어도 **조용히 0건**으로 읽혔다(2026-09-02 실측:
test_report_summary total 0). 조용히 실패하는 종류라 테스트로 못 박아 둔다.

그리고 **새 실패와 기존 실패를 갈라야** 읽힌다. 매번 17개를 나열하면 사람이 무시한다 —
mr-lint 의 스택 경고가 그렇게 무시됐다(실측 26 대 0).
"""
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

print("TEST271 OK")
