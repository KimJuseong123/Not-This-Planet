"""master 릴리스 경로 — 조용한 실패 · 태그 형식 · 빌드 전 테스트 · 리허설 (S15P21B202-407).

2026-09-10 실측: origin/master 는 초기 커밋에 멈춰 있고 파이프라인 이력이 0건이었다.
즉 90분짜리 release-windows 는 **한 번도 돈 적이 없고 첫 릴리스가 곧 첫 시험**이다.
그 상태에서 실패하면 알림도 없었다 — 이 파일이 그 네 가지를 잠근다.
"""
import os
import sys

HERE = os.path.dirname(__file__)
sys.path.insert(0, os.path.join(HERE, '..'))
import publish_release as R


def _read(rel):
    p = os.path.join(HERE, '..', '..', '..', rel)
    return open(p, encoding='utf-8').read()


NL = chr(10)

print()
print("== 릴리스 경로 ==")


# ── ① 옛 태그 `1.0.1` 을 버전으로 읽는다 ────────────────────────────────
#
#     예전 TAG_RE 는 `^v…$` 였고 all_tags 는 `git tag --list v*` 였다. 저장소의 유일한
#     태그가 `1.0.1`(v 없음)이라 **핫픽스를 먼저 하면** latest_tag 가 None →
#     "이전 태그가 없어 버전을 정할 수 없다" → exit 1 → 그리고 알림이 없었다.
assert R.parse_tag("1.0.1") == (1, 0, 1), "v 없는 옛 태그를 못 읽는다"
assert R.parse_tag("v1.0.1") == (1, 0, 1)
assert R.parse_tag("1.0") is None and R.parse_tag("abc") is None

#     **만들 때는 항상 v 를 붙인다** — 두 형식이 섞여 있으면 다음 사람이 또 헷갈린다.
assert R.bump_patch("1.0.1") == "v1.0.2", "새 태그에 v 가 없다"
assert R.bump_patch("v1.0.1") == "v1.0.2"

#     핫픽스가 조용히 멈추던 그 길이 실제로 막혔는지 본다 — **v 태그가 하나도 없을 때**
#     가 그 상황이다. 폴백이 사라지면 여기가 다시 None 이 된다.
_v, _why, _rc = R.decide_version("Merge branch 'hotfix/S15P21B202-9-x' into 'master'", ["1.0.1"])
assert _v == "v1.0.2", f"핫픽스가 옛 태그에서 버전을 못 정한다: {_v} ({_why})"
print("  옛 태그 1.0.1 을 읽고, 새 태그는 v 를 붙인다 ✅")


# ── ①b 형식이 섞이면 `v` 쪽이 이긴다 (S15P21B202-446) ───────────────────
#
#     읽기를 넓힌 대가로 생긴 문제다. 저장소에 시험용 `1.0.1`(산출물 0개)이 남아
#     있어서, v1.0.0 을 낸 뒤 핫픽스를 하면 그것이 최신으로 잡혀 **v1.0.1 을
#     건너뛰고 v1.0.2** 가 된다. 받는 사람은 없어진 버전을 찾게 된다.
assert R.latest_tag(["1.0.1", "v0.9.0"]) == "v0.9.0", \
    "형식이 섞이면 v 쪽에서 골라야 한다 — 손으로 만든 자리표시자가 이기면 안 된다"
assert R.latest_tag(["1.0.1", "v1.0.0"]) == "v1.0.0"

#     **폴백은 남아 있어야 한다** — v 태그가 하나도 없으면 넓게 읽는다(-407 이 막은 길).
assert R.latest_tag(["1.0.1", "0.9.0"]) == "1.0.1", \
    "v 태그가 없을 때의 폴백이 사라졌다 — 핫픽스가 다시 조용히 막힌다"
assert R.latest_tag([]) is None

#     이것이 이 이슈의 완료 조건이다: v1.0.0 뒤의 핫픽스는 v1.0.1 이어야 한다.
_v, _why, _rc = R.decide_version("Merge branch 'hotfix/S15P21B202-9-x' into 'master'",
                            ["1.0.1", "v1.0.0"])
assert _v == "v1.0.1", f"v1.0.0 다음 핫픽스가 v1.0.1 이 아니다: {_v} ({_why})"

#     이미 쓴 번호를 **다른 커밋**에 또 붙이지 않는다 — 90분짜리 빌드가 끝난 뒤
#     태그에서 실패하는 대신 앞에서 멈춘다.
_v, _why, _rc = R.decide_version("Merge branch 'release/S15P21B202-9-v1.0.0' into 'master'",
                            ["v1.0.0"])
assert _v is None and "이미 붙어 있다" in _why, f"같은 번호를 다시 내보낸다: {_v} ({_why})"

_v, _why, _rc = R.decide_version("Merge branch 'hotfix/S15P21B202-9-x' into 'master'",
                            ["v1.0.0", "v1.0.1"])
assert _v == "v1.0.2", f"이미 v1.0.1 이 있으면 v1.0.2 여야 한다: {_v} ({_why})"

#     **재시도는 막지 않는다.** 태그가 이 커밋을 가리키면 이번 릴리스를 다시 도는
#     것이다 — 2026-09-11 v1.0.0 에서 Release·MM 까지 다 끝난 뒤 마지막 한 줄이 터져
#     실제로 그 길로 되살렸다. 여기서 막으면 그 길이 닫힌다.
_v, _why, _rc = R.decide_version("Merge branch 'release/S15P21B202-9-v1.0.0' into 'master'",
                            ["v1.0.0"], points_here=lambda t: t == "v1.0.0")
assert _v == "v1.0.0", f"같은 커밋의 재시도를 막는다: {_v} ({_why})"

#     **옛 `1.0.1` 이 `v1.0.1` 을 막으면 안 된다.** `v` 를 무시하고 비교하면 이 이슈가
#     고치려던 것보다 나빠진다 — 버전을 건너뛰는 대신 릴리스가 아예 멈춘다.
assert not R.tag_taken("v1.0.1", ["1.0.1"]),     "손으로 만든 옛 태그가 같은 번호의 정식 태그를 막는다"
assert R.tag_taken("1.0.1", ["v1.0.1"]), "같은 번호를 다른 커밋에 또 붙이는 것은 막아야 한다"
assert not R.tag_taken("v1.0.2", ["v1.0.1"])

#     커밋을 못 읽으면(points_here 가 False) 막는 쪽으로 기운다 — 조용히 덮어쓰는
#     것보다 멈추고 사람이 보는 편이 낫다.
assert R.tag_taken("v1.0.0", ["v1.0.0"], points_here=lambda t: False)
print("  형식이 섞이면 v 가 이기고, 폴백은 남아 있다 ✅")


# ── ② master 파이프라인 실패가 카드로 나간다 ────────────────────────────
#     mm-ci-failed 의 rules 에 master 가 없어서 릴리스 실패가 조용했다.
_ci = _read('.gitlab-ci.yml')
_failed = _ci.split("mm-ci-failed:", 1)[1].split("mm-ci-resolved:", 1)[0]
_resolved = _ci.split("mm-ci-resolved:", 1)[1].split("\n\n", 1)[0]
for _name, _blk in (("mm-ci-failed", _failed), ("mm-ci-resolved", _resolved)):
    assert "CI_COMMIT_BRANCH == \"master\"" in _blk, f"{_name} 의 rules 에 master 가 없다"
    assert "release-windows" in _blk, f"{_name} 의 needs 에 release-windows 가 없다"

#     두 잡의 needs 는 **같은 목록**이어야 한다(-289). 다르면 실패한 릴리스에
#     "해소됨" 이 찍히거나, 반대로 해소를 놓친다.
def _needs(block):
    # 줄 끝 주석을 떼야 이름만 남는다 — 두 잡의 주석은 서로 다르다.
    return sorted(l.split("job:", 1)[1].split("#", 1)[0].strip()
                  for l in block.splitlines() if "- job:" in l)


assert _needs(_failed) == _needs(_resolved), (
    "두 알림 잡의 needs 가 다르다 — " + str(_needs(_failed)) + " vs " + str(_needs(_resolved)))
print("  master 실패가 카드로 나가고, 두 알림 잡의 needs 가 같다 ✅")


# ── ③ master 카드는 develop 카드와 말이 다르다 ──────────────────────────
#     develop 이 빨간 것은 "빨간 기준선 위에서 시작한다" 다. master 는 아무도 그 위에서
#     브랜치를 따지 않는다 — 대신 **릴리스가 나가지 않았다.** 같은 문구를 쓰면 읽는
#     사람이 할 일을 잘못 고른다.
_mm = _read('scripts/ci/mm_notify.py')
_bf = _mm.split("def branch_failed(", 1)[1].split("\ndef ", 1)[0]
assert "if branch == \"master\"" in _bf, "branch_failed 가 master 를 가르지 않는다"
assert "릴리스가 실패했다" in _bf, "master 카드 제목이 릴리스를 말하지 않는다"
assert "이 릴리스는 나가지 않았다" in _bf, "무엇이 안 나갔는지 적혀 있지 않다"

#     ✅ 해소 카드는 master 에서 보내지 않는다 — publish_release 가 내려받기 링크가 든
#     🟢 카드를 이미 보낸다. 여기서 또 보내면 정보가 적은 쪽이 나중에 온다.
_br = _mm.split("def branch_resolved(", 1)[1].split("\ndef ", 1)[0]
assert "if branch == \"master\"" in _br, "branch_resolved 가 master 를 가르지 않는다"
print("  master 카드가 릴리스의 말로 적히고, 해소 카드는 중복되지 않는다 ✅")


# ── ④ 빌드 전에 EditMode 테스트가 돈다 ──────────────────────────────────
#     master push 에서 생기는 잡은 release-windows 하나뿐이었다. unity-compile 의
#     rules 는 merge_request_event·develop 이라 여기 없고, MR 파이프라인은 **소스
#     브랜치**를 검사하므로 머지 결과 트리는 어디서도 검사되지 않은 채 릴리스됐다.
_rel = _ci.split("release-windows:", 1)[1].split("\n\n", 1)[0]
assert "-runTests -testPlatform EditMode" in _rel, "릴리스 잡이 EditMode 테스트를 돌리지 않는다"
assert _rel.index("-runTests") < _rel.index("SpaceBuild.BuildWindows"), (
    "테스트가 빌드 **뒤**에 있다 — 깨진 트리로 90분을 쓴 다음 알게 된다")
assert "publish_release.py gate" in _rel, "테스트 결과로 빌드를 가르는 단계가 없다"
print("  EditMode 테스트가 빌드보다 앞에 있다 ✅")


# ── ⑤ gate 가 결과를 읽고 빌드 여부를 가른다 ────────────────────────────
import tempfile

PASS_XML = ("<test-run total=\"2\" passed=\"2\" failed=\"0\" skipped=\"0\" duration=\"1.5\">"
            "<test-case fullname=\"N.C.a\" result=\"Passed\" duration=\"1\"/>"
            "<test-case fullname=\"N.C.b\" result=\"Passed\" duration=\"0.5\"/>"
            "</test-run>")
FAIL_XML = ("<test-run total=\"2\" passed=\"1\" failed=\"1\" skipped=\"0\" duration=\"1.5\">"
            "<test-case fullname=\"N.C.a\" result=\"Passed\" duration=\"1\"/>"
            "<test-case fullname=\"N.C.b\" result=\"Failed\" duration=\"0.5\">"
            "<failure><message>기대 1, 실제 2</message></failure></test-case>"
            "</test-run>")


_REAL_BASELINE = R.unity_report.baseline_failures


def _baseline(names):
    """기준선을 갈아 끼운다 (S15P21B202-440).

    `publish_release` 는 **모듈**을 임포트하므로 여기서 함수를 바꿔 끼우면 그쪽이 본다.
    `None` 은 "기준선을 못 읽었다" 이고 빈 집합과 뜻이 다르다 — 그 둘을 섞으면 없는
    회귀를 만들어 낸다(`unity_report.baseline_failures` 독스트링과 같은 이유)."""
    R.unity_report.baseline_failures = lambda *a, **k: names


def _gate(xml_text, trc=0, log_text=None):
    """gate 한 번. **환경을 되돌린다** (`!336` AI 리뷰) — CI_PROJECT_DIR 을 전역에
    남기면 뒤 절이 남의 임시 폴더를 보고 엉뚱한 것을 단정한다."""
    d = tempfile.mkdtemp()
    _saved = os.environ.get("CI_PROJECT_DIR")
    os.environ["CI_PROJECT_DIR"] = d
    xml = ""
    if xml_text is not None:
        xml = os.path.join(d, "r.xml")
        open(xml, "w", encoding="utf-8").write(xml_text)
    else:
        xml = os.path.join(d, "missing.xml")
    log = ""
    if log_text is not None:
        log = os.path.join(d, "u.log")
        open(log, "w", encoding="utf-8").write(log_text)
    try:
        return R.phase_gate(trc, xml, log), d
    finally:
        if _saved is None:
            os.environ.pop("CI_PROJECT_DIR", None)
        else:
            os.environ["CI_PROJECT_DIR"] = _saved


_baseline(set())
_rc, _d = _gate(PASS_XML)
assert _rc == 0, "전부 통과인데 빌드를 막았다"
#     실패한 테스트를 파이프라인 화면에서 바로 보게 한다 — unity-compile 과 같은 결.
assert os.path.exists(os.path.join(_d, "unity-junit.xml")), "JUnit 변환 산출물이 없다"

# **기존 실패는 릴리스를 막지 않는다** (S15P21B202-440).
#
# 처음에는 실패가 하나라도 있으면 막았다. 그런데 develop 에는 기존 실패가 33건
# 있어서(`-346` 27건 + `-343` 6건) 그 게이트는 무조건 걸렸고 **실제 릴리스가 나갈 수
# 없었다.** `unity-compile` 이 이미 푼 문제를 릴리스 경로에서 되풀이한 것이다(`-309`).
_baseline({"N.C.b"})                      # 그 실패가 기준선에 있다 = 기존 실패
_rc, _ = _gate(FAIL_XML)
assert _rc == 0, "기존 실패가 릴리스를 막는다 — 33건이 있으므로 아무것도 나갈 수 없다"

_baseline(set())                          # 기준선에 없다 = 이 트리가 만든 실패
_rc, _ = _gate(FAIL_XML)
assert _rc == 1, "이 트리가 만든 실패인데 빌드로 넘어간다"

# **종료 코드로 다시 막으면 안 된다** (`!350` AI 리뷰가 잡은 것).
#
# 기준선 비교를 통과해도 그 아래 `if test_rc != 0` 이 남아 있으면 릴리스는 여전히
# 나가지 못한다. Unity 는 테스트가 하나라도 실패하면 0이 아닌 코드로 끝나므로,
# 기존 실패 33건이 있는 한 그 분기가 늘 걸린다 — 이 MR 의 목적이 통째로 무너진다.
#
# 앞의 검사가 `trc=0` 으로만 불러 이 경로를 못 봤다. 그것이 이 절이 있는 이유다.
_baseline({"N.C.b"})
_rc, _ = _gate(FAIL_XML, trc=2)
assert _rc == 0, ("기존 실패뿐인데 Unity 종료 코드로 막는다 — 기준선 비교가 뜻을 잃는다")

# 반대로 테스트가 전부 통과인데 종료 코드가 0이 아니면 그것은 테스트 밖의 고장이다.
_baseline(set())
_rc, _ = _gate(PASS_XML, trc=2)
assert _rc == 1, "테스트는 통과인데 종료 코드가 0이 아니다 — 이때는 막아야 한다"

# **기준선이 돌리지 않은 테스트의 실패로 릴리스를 막지 않는다** (S15P21B202-739).
#
# develop 의 MR·푸시 실행은 느린 목록을 건너뛴다. 기준선에는 그 테스트의 결과가 없는데
# 릴리스는 전부 돌리므로, 원래부터 실패하던 느린 테스트가 여기서 처음 "새 실패" 로 보인다.
def _baseline_blind(names, blind):
    def fake(*a, meta=None, **k):
        if meta is not None:
            meta["slow_skipped"] = sorted(blind)
        return names
    R.unity_report.baseline_failures = fake


_baseline_blind(set(), {"N.C.b"})
_rc, _ = _gate(FAIL_XML)
if _rc != 0:
    raise AssertionError("기준선이 건너뛴 느린 테스트의 실패로 릴리스를 막는다 — 이 트리 탓인지 모른다")
_baseline_blind(set(), {"N.C.x"})         # 다른 이름이 건너뛰어졌다 — 이 실패는 여전히 새것이다
_rc, _ = _gate(FAIL_XML)
if _rc != 1:
    raise AssertionError("건너뛴 목록에 없는 새 실패까지 봐준다")

_baseline(None)                           # 기준선을 못 읽었다
_rc, _ = _gate(FAIL_XML)
assert _rc == 0, ("기준선을 못 읽었는데 막는다 — 릴리스 MR 과 develop 머지에서 이미 "
                  "unity-compile 이 돌았고, 두 번째 그물이 API 딸꾹질로 릴리스를 "
                  "막는 것은 그물이 없는 것보다 나쁘다")

#     **결과 XML 이 없는 경우가 가장 중요하다.** 컴파일이 깨지면 Unity 는 테스트를
#     돌리지 못하고 XML 을 남기지 않는다. 그것을 "0건 통과" 로 읽으면 없는 것을
#     통과로 읽는 것이다(-134 에서 고친 실패 방식).
_rc, _ = _gate(None, trc=1, log_text="Assets/A.cs(3,5): error CS0103: x")
assert _rc == 1, "결과 XML 이 없는데 빌드로 넘어간다 — 컴파일이 깨진 경우다"

#     결과는 전부 통과인데 종료 코드가 0이 아닌 경우 — 테스트 밖에서 깨진 것이다.
_rc, _ = _gate(PASS_XML, trc=2)
assert _rc == 1, "종료 코드가 0이 아닌데 빌드로 넘어간다"
print("  gate 가 실패·XML 없음·종료 코드를 모두 막는다 ✅")


# ── ⑥ 막을 때 `❌` 줄을 남긴다 ──────────────────────────────────────────
#     mm-ci-failed 가 그 줄을 카드의 "이유" 로 싣는다(mm_notify.REASON_MARKERS).
#     남기지 않으면 카드가 "로그에서 이유를 찾지 못함" 이 된다 — 눈먼 카드다.
import io as _io
import contextlib

_baseline(set())     # 막는 경로를 보려면 기준선이 비어 있어야 한다
for _label, _args in (("테스트 실패", (FAIL_XML, 0, None)),
                      ("XML 없음", (None, 1, "error CS0103: x")),
                      ("종료 코드", (PASS_XML, 2, None))):
    _buf = _io.StringIO()
    with contextlib.redirect_stdout(_buf):
        _gate(*_args)
    _out = _buf.getvalue()
    assert "❌" in _out, f"{_label}: ❌ 줄이 없다 → 카드가 이유를 못 싣는다"
    # 같은 조건을 두 번 적어 두었던 자리다(`!336` AI 리뷰) — 무엇을 막는지 적는다.
    assert "빌드하지 않는다" in _out, f"{_label}: 빌드를 세운다는 말이 없다 — {_out[:200]}"

import mm_notify as M
_baseline(set())
for _label, _args in (("테스트 실패", (FAIL_XML, 0, None)),):
    _buf = _io.StringIO()
    with contextlib.redirect_stdout(_buf):
        _gate(*_args)
    _reasons = M.reasons_from_trace(_buf.getvalue())
    assert _reasons, "mm_notify 가 gate 출력에서 이유를 뽑지 못한다"
    # 카드의 '이유' 칸에 실릴 문구다. 무엇이 막았는지가 그 한 줄에 있어야 한다.
    assert "EditMode" in _reasons[0], _reasons[0]
    assert "빌드하지 않는다" in _reasons[0], _reasons[0]
print("  막을 때 ❌ 줄을 남기고, mm_notify 가 그것을 이유로 읽는다 ✅")


# ── ⑦ 리허설이 밖으로 내보내지 않는다 ──────────────────────────────────
#     트리거가 master push 하나뿐이라 **시험 실행이 곧 실제 릴리스**였다.
assert "CI_PIPELINE_SOURCE == \"web\"" in _rel, "리허설 트리거가 없다"
assert "RELEASE_DRY_RUN" in _rel, "리허설 변수가 잡에 없다"

#     변수 읽기는 **master push 안에서만** 뜻이 있다(아래 ⑧ 이 그 잠금을 단정한다).
os.environ["CI_PIPELINE_SOURCE"] = "push"
os.environ["CI_COMMIT_BRANCH"] = "master"
for _val, _want in (("", False), ("0", False), ("false", False), ("no", False),
                    ("1", True), ("true", True)):
    os.environ["RELEASE_DRY_RUN"] = _val
    assert R.dry_run() is _want, f"RELEASE_DRY_RUN={_val!r} → {R.dry_run()}"
os.environ.pop("RELEASE_DRY_RUN", None)
assert R.dry_run() is False, "master push 에 변수가 없으면 실제 릴리스여야 한다"

#     리허설은 아무 브랜치에서나 돈다 — 릴리스 브랜치 머지 커밋이 없는 것이 정상이다.
#     거기서 멈추면 정작 시험하려던 빌드·zip 경로를 한 번도 못 지나간다.
os.environ["RELEASE_DRY_RUN"] = "1"
os.environ["CI_COMMIT_MESSAGE"] = "feat: 아무 커밋"
_d = tempfile.mkdtemp()
_cwd = os.getcwd()
try:
    os.chdir(_d)
    assert R.phase_version() == 0, "리허설인데 버전 단계에서 멈춘다"
    assert "rehearsal" in open(R.VERSION_FILE, encoding="utf-8").read()
finally:
    os.chdir(_cwd)
    os.environ.pop("RELEASE_DRY_RUN", None)
    os.environ.pop("CI_COMMIT_MESSAGE", None)

#     리허설이 건너뛰는 것이 코드에 실제로 적혀 있는지 — 문구가 아니라 호출 순서다.
_pub = open(os.path.join(HERE, "..", "publish_release.py"), encoding="utf-8").read()
_body = _pub.split("def phase_publish(", 1)[1].split("\ndef ", 1)[0]
assert _body.index("if dry_run():") < _body.index("upload_package("), (
    "리허설 검사가 패키지 업로드 **뒤**에 있다 — 진짜로 올라간다")
print("  리허설이 빌드·zip 까지만 하고 밖으로 내보내지 않는다 ✅")


# ── ⑧ 변수로 리허설을 **끌 수 없다** (`!336` AI 리뷰) ───────────────────
#
#     `rules:` 의 `variables:` 는 Run pipeline 화면에서 사람이 넣은 같은 이름 변수보다
#     **우선순위가 낮다.** 그래서 리허설 화면에서 RELEASE_DRY_RUN=0 을 주면
#     .gitlab-ci.yml 이 준 1 이 덮이고 **아무 브랜치에서 진짜 Release·태그가 나간다** —
#     안전 장치로 만든 것이 반대로 새는 길이 된다.
#
#     그래서 트리거로 잠갔다: 실제 릴리스는 master 로의 push 하나뿐이다.
def _trigger(source, branch, var=None):
    os.environ["CI_PIPELINE_SOURCE"] = source
    os.environ["CI_COMMIT_BRANCH"] = branch
    if var is None:
        os.environ.pop("RELEASE_DRY_RUN", None)
    else:
        os.environ["RELEASE_DRY_RUN"] = var
    return R.dry_run()


# 변수를 0 으로 줘도 master push 가 아니면 리허설이다.
for _src, _br in (("web", "develop"), ("web", "master"), ("web", "feat/x"),
                  ("push", "develop"), ("schedule", "master"),
                  ("merge_request_event", "master")):
    assert _trigger(_src, _br, "0") is True, (
        f"{_src}/{_br} 에서 RELEASE_DRY_RUN=0 이 리허설을 껐다 — 진짜 릴리스가 나간다")

# master push 는 실제 릴리스다. 그리고 그때만 변수로 리허설을 켤 수 있다.
assert _trigger("push", "master") is False, "master push 가 리허설로 잡힌다 — 릴리스가 안 나간다"
assert _trigger("push", "master", "0") is False, "master push + 0 은 실제 릴리스여야 한다"
assert _trigger("push", "master", "1") is True, "master push 에서 리허설을 켤 수 없다"

for _k in ("CI_PIPELINE_SOURCE", "CI_COMMIT_BRANCH", "RELEASE_DRY_RUN"):
    os.environ.pop(_k, None)
print("  변수로 리허설을 끌 수 없다 — 실제 릴리스는 master push 하나뿐 ✅")


# ── ⑨ 리허설 실패도 알림 대상이다 ───────────────────────────────────────
#     리허설의 목적이 "릴리스 전에 안다" 인데 그 결과가 조용하면 화면을 직접 봐야 한다.
for _name, _blk in (("mm-ci-failed", _failed), ("mm-ci-resolved", _resolved)):
    assert 'CI_PIPELINE_SOURCE == "web"' in _blk, (
        f"{_name} 의 rules 에 web 이 없다 — 리허설 실패가 조용하다")
print("  리허설(web) 실패도 카드로 나간다 ✅")


# ── ⑩ 리허설이 **파이프라인 단계에서** 허용된다 (S15P21B202-407) ─────────
#
#     **이것이 없어서 리허설이 돌지 않았다.** `release-windows` 에 `web` 조건을 넣어
#     두었는데도 Run pipeline 을 누르면 아무것도 만들어지지 않았다 — 최상위
#     `workflow: rules:` 가 catch-all 없는 화이트리스트라 `web` 이 어느 항목에도
#     걸리지 않았고, 파이프라인 자체가 생성되지 않으니 잡 규칙은 닿지도 않는 죽은
#     코드였다(2026-09-11 확인).
#
#     그래서 잡 규칙만 보는 검사로는 이 실패를 못 잡는다. 두 층을 **함께** 본다.
_wf = _ci.split("workflow:", 1)[1].split("stages:", 1)[0]
assert 'CI_PIPELINE_SOURCE == "web"' in _wf, (
    "workflow rules 에 web 이 없다 — Run pipeline 을 눌러도 파이프라인이 생성되지 않고, "
    "release-windows 의 web 조건은 죽은 코드가 된다")

#     리허설은 90분 빌드다. 중간에 죽으면 반쯤 만든 산출물이 남고 "경로가 끝까지
#     가는지" 를 볼 수 없다 — master 와 같은 이유로 자동 취소를 끈다.
_web_at = _wf.index('CI_PIPELINE_SOURCE == "web"')
assert "on_new_commit: none" in _wf[_web_at:], (
    "리허설에 auto_cancel 이 꺼져 있지 않다 — 뒤따르는 커밋이 90분 빌드를 죽인다")
print("  리허설이 workflow 단계에서 허용되고 자동 취소가 꺼져 있다 ✅  (이것이 빠져 돌지 않았다)")

# **갈아 끼운 것을 되돌린다** (`!350` AI 리뷰). 같은 프로세스에서 이어 도는 다른
# 절·모듈이 가짜 기준선을 보고 조용히 통과하면 안 된다 — `_gate` 가 환경변수를
# 되돌리는 것과 같은 이유다.
R.unity_report.baseline_failures = _REAL_BASELINE

# ── ⑧ 되돌리기가 실제로 돌 수 있다 (S15P21B202-467) ─────────────────────
#
#     `release-prod` 주석은 "PROMOTE_VERSION 을 주고 이 잡만 돌린다" 고 말해 왔지만
#     그 길은 **파이프라인 생성 단계에서 거절**됐다. `needs` 에 적힌 `publish-builds`
#     가 web 파이프라인에는 없는 잡이고, GitLab 은 없는 잡을 needs 로 걸면 파이프라인
#     자체를 만들지 않는다. 되돌리기는 급할 때 쓰는 길이라 그때 알면 늦다.
_promote = _ci.split("release-prod:", 1)[1].split("\npublish-site:", 1)[0]
#     주석에도 같은 말이 적혀 있으므로 **주석을 뺀 needs 블록만** 센다.
_nb = _promote.split(chr(10) + "  needs:", 1)[1].split(chr(10) + "  interruptible:", 1)[0]
_nb = [l for l in _nb.splitlines() if l.strip() and not l.strip().startswith("#")]
_deps = [l for l in _nb if "- job:" in l]
assert len(_deps) >= 2, _nb
assert sum(1 for l in _nb if "optional: true" in l) == len(_deps), \
    "release-prod 의 needs 에 optional 이 빠졌다 — 되돌리기가 안 만들어진다: " + str(_nb)

#     그리고 되돌리기에 **90분짜리 Unity 빌드가 딸려 오면 안 된다.** `release-windows`
#     는 web 파이프라인에서 리허설로 도는데, 그 90분을 `release-prod` 가 기다린다.
#     여기도 **주석을 뺀 규칙 줄만** 본다 — 위 주석에 RELEASE_DRY_RUN 이 이미 적혀 있다.
_relwin = _ci.split(NL + "release-windows:", 1)[1].split(NL + "variables:", 1)[0]
_rules = [l.strip() for l in _relwin.split("  rules:", 1)[1].splitlines()
          if l.strip() and not l.strip().startswith("#")]
_rules = _rules[:next((i for i, l in enumerate(_rules) if l.startswith("script:")), len(_rules))]
#     `when: never` 는 **둘**이다 — 되돌리기(여기)와 서버 전용 배포(`-464`). 첫 번째를
#     집으면 엉뚱한 규칙을 보게 되므로 `$PROMOTE_VERSION` 이 있는 줄을 찾아서 본다.
_pv = next(i for i, l in enumerate(_rules) if "$PROMOTE_VERSION" in l)
assert _rules[_pv + 1] == "when: never", \
    "되돌리기 규칙이 release-windows 를 막지 않는다: " + str(_rules)

#     **리허설 규칙보다 앞이어야 한다** — rules 는 먼저 걸리는 쪽이 이긴다.
_dry = next(i for i, l in enumerate(_rules) if "RELEASE_DRY_RUN" in l)
assert _pv < _dry, \
    "되돌리기 제외 규칙이 리허설 규칙보다 뒤에 있다 — 먼저 걸리는 쪽이 이긴다: " + str(_rules)

#     **조건이 너무 넓으면 안 된다** (`!379` 리뷰). 브랜치를 안 보면 아무 브랜치의
#     리허설에서도 그 변수만 있으면 90분 빌드가 조용히 사라진다.
assert '$CI_COMMIT_BRANCH == "master"' in _rules[_pv], \
    "되돌리기 제외 규칙이 브랜치를 안 본다 — 다른 브랜치의 리허설까지 사라진다: " + _rules[_pv]
print("  되돌리기 파이프라인이 만들어지고, 90분 빌드를 안 끌고 온다 ✅")

print("== 통과 ==")
