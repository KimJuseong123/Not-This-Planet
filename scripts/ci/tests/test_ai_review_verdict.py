"""AI 리뷰의 판정 합치기와 실패 표시 (S15P21B202-335).

실행: python scripts/ci/tests/test_ai_review_verdict.py
네트워크·모델 없이 돈다 — `merge`·`render`·`with_fail_reasons` 는 순수 함수다.

지키려는 것
  · **리뷰어가 전부 실패해도 코멘트가 남는다.** 예전에는 그 경우 `return 1` 로 빠져나가
    아무것도 남지 않았다. `❓ 판정 불가` 경로는 다 구현돼 있었는데 그 앞에서 반환했다.
    하필 모델 이름·자격증명·CLI 경로처럼 **모든 리뷰어에 공통으로 걸리는 원인**은 항상
    전부 실패로 나타나므로, 사람이 실제로 겪는 실패가 대부분 이 조용한 쪽이었다.
  · 잡이 `allow_failure: true` 라 파이프라인도 초록이다. 그래서 "AI 코멘트가 없는 MR" 이
    ① 문서만 바뀌어 건너뛴 것과 ② 리뷰가 죽은 것을 동시에 뜻하면 안 된다.
  · **리뷰어 실패가 섞이면 ✅ 를 내지 않는다** — 일부만 성공한 판정을 "머지 가능" 으로
    올리면 안 본 부분이 통과로 보인다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import ai_review as A

ROLES = list(A.ROLES)[:2]          # 실제 역할 이름을 쓴다(각주가 label 을 찾는다)


def render_all_failed(defaults=None):
    merged = A.merge({}, ROLES)
    body = A.render("메타", ROLES, {}, {}, merged, None, [], ROLES, defaults or {})
    return merged, A.with_fail_reasons(body, {r: "claude -p 종료 코드 1 category=unknown" for r in ROLES})


# ── 전부 실패 ────────────────────────────────────────────────────────────
merged, body = render_all_failed({"scene": ("ok", "씬·프리팹·에셋 파일 변경 없음")})

assert merged["verdict"] == "unknown", merged["verdict"]
print("  전부 실패 → 판정 unknown ✅")

assert A.VERDICT_LINE["unknown"] in body, "❓ 판정 불가 줄이 없다"
print("  코멘트에 ❓ 판정 불가 ✅")

assert "실행 실패 리뷰어" in body, "각주에 실패 리뷰어가 없다"
assert "category=unknown" in body, "실패 이유가 코멘트에 없다"
print("  실패 리뷰어 각주 + 이유 ✅")

# 판정표는 ❓ 로 채워지되, **파일 목록으로 정해지는 것은 사실대로** 남는다.
# 리뷰어가 죽어도 "씬 변경이 없다" 는 것은 diff 로 아는 사실이다.
assert body.count("| ❓ |") >= 3, body.count("| ❓ |")
assert "씬·프리팹·에셋 파일 변경 없음" in body, "확정 가능한 항목까지 ❓ 로 덮었다"
print("  판정표는 ❓ · 파일로 정해지는 항목은 사실 유지 ✅")

# ── 일부만 실패하면 ✅ 를 내지 않는다 ────────────────────────────────────
one_ok = {ROLES[0]: {"verdict": "ok", "checks": {"scope": {"status": "ok", "why": "범위 안"}},
                     "findings": [], "questions": []}}
assert A.merge(one_ok, [ROLES[1]])["verdict"] != "ok", "실패가 섞였는데 ✅ 를 냈다"
assert A.merge(one_ok, [])["verdict"] == "ok", "전부 성공인데 ✅ 가 아니다"
print("  일부 실패 → ✅ 아님 · 전부 성공 → ✅ ✅")

# ── 이유가 없으면 줄을 붙이지 않는다 ────────────────────────────────────
assert A.with_fail_reasons("본문", {}) == "본문"
print("  실패가 없으면 이유 줄 없음 ✅")

# ── 실패 이유는 **우리가 쓴 문구만** 싣는다 (S15P21B202-342) ─────────────────
#    `-335` 가 실패 이유를 코멘트에 싣게 했고, 같은 MR 의 문서에 "분류만 싣는다 — 상류 응답
#    원문은 로그에만" 이라고 적었다. 그런데 코드는 `str(e)` 로 원문을 실었다(`!256` 지적).
#    `subprocess.TimeoutExpired` 의 `str()` 에는 **argv 전체**가 들어간다 — 모델명·경로·
#    `--system-prompt` 내용까지. 자격증명이 새는 것은 아니지만 공개 코멘트에 통제하지 않은
#    상류 문자열이 들어가는 구조이고, 무엇보다 **문서가 사실이 아니었다.**
import subprocess

# 우리가 만든 RuntimeError — 문구를 우리가 썼으므로 그대로 싣는다
mine = RuntimeError("Anthropic API 401 → 키·모델명·사용량 확인")
assert A.fail_reason(mine) == "Anthropic API 401 → 키·모델명·사용량 확인"
print("  우리 RuntimeError → 메시지 그대로 ✅")

# 남의 예외 — 종류만. argv 가 새어나가서는 안 된다
boom = subprocess.TimeoutExpired(
    cmd=["claude", "-p", "--model", "claude-opus-5", "--system-prompt", "내부 지침 전문"], timeout=600)
got = A.fail_reason(boom)
assert got == "TimeoutExpired", got
assert "claude-opus-5" not in got and "내부 지침" not in got, got
print("  다른 예외 → 종류만(argv 안 샌다) ✅")

# RuntimeError 의 **하위 클래스**도 원문을 싣지 않는다 — 라이브러리 예외가 자주 그렇다
class VendorError(RuntimeError):
    pass


assert A.fail_reason(VendorError("http://gitlab.example/api/v4/... 응답 본문")) == "VendorError"
print("  RuntimeError 하위 클래스 → 종류만 ✅")

# 그리고 그 결과가 실제로 각주에 실린다
foot = A.with_fail_reasons("본문", {ROLES[0]: A.fail_reason(boom)})
assert "TimeoutExpired" in foot and "claude-opus-5" not in foot, foot
print("  각주에 분류만 실린다 ✅")


# ── 출력 해석 실패한 리뷰어도 각주에 드러난다 (S15P21B202-342) ───────────────
#    호출은 됐는데 JSON 이 아니면 `results[role] = None` 이 되고 판정은 unknown 으로 옳게
#    떨어진다. 그런데 `failed` 에도 `fail_why` 에도 안 들어가서 **각주에서 사라졌다** —
#    어느 리뷰어의 출력이 깨졌는지 사람이 알 수 없었다.
assert A.parse_json("이건 JSON 이 아니다") is None
body2 = A.with_fail_reasons(
    A.render("메타", ROLES, {ROLES[0]: None}, {ROLES[0]: "쓰레기"}, A.merge({ROLES[0]: None}, []),
             None, [], [], {}),
    {ROLES[0]: "출력 해석 실패(JSON 아님)"})
assert "출력 해석 실패" in body2, body2
print("  JSON 해석 실패 리뷰어가 각주에 드러난다 ✅")

import ci_common

# ── S15P21B202-354 · 빠진 파일을 이름과 사유로 말한다 ────────────────────────
#
# 예전에는 개수만 세고 각주가 사유를 **하드코딩**했다 —
# "리뷰 대상 외 파일 2개(에셋·바이너리·메타·용량 초과)". 실제 이유가 그 넷이 아니어도
# 그렇게 적혔고 어떤 파일인지도 안 적었다. 그래서 **코드 파일이 통째로 빠져도 읽는
# 사람이 알 수 없었다.**
#
# 그리고 이 이슈를 만들 때 내가 "코드가 access_raw_diffs 를 안 붙인다" 고 잘못
# 진단했다 — 2026-08-27 부터 붙어 있었다. **그것을 지키는 테스트가 없어서** 코드를
# 확인하지 않고 단정했다. ⑤ 가 그 회귀 테스트다.
print("― S15P21B202-354 빠진 파일 보고 ―")

# ① 사유를 갈라 적는다 — collapsed 를 "용량 초과" 라고 하지 않는다
_changes = [
    {"new_path": "a.py", "old_path": "a.py", "diff": "@@ -1 +1 @@\n-x\n+y\n"},
    {"new_path": "b.py", "old_path": "b.py", "diff": "", "collapsed": True},
    {"new_path": "c.md", "old_path": "c.md", "diff": "", "too_large": True},
    {"new_path": "d.png", "old_path": "d.png", "diff": "binary"},
    {"new_path": "e.cs", "old_path": "e.cs", "diff": ""},
]
_info = A.classify(_changes)
_why = dict(_info["skipped"])
assert "접었다" in _why["b.py"], _why
assert "너무 크다" in _why["c.md"], _why
assert _why["d.png"] == "바이너리·메타", _why
assert "비어 있다" in _why["e.cs"], _why
assert "a.py" not in _why, "diff 가 있는 파일은 빠지지 않는다"
print("  collapsed·too_large·바이너리·빈 diff 를 갈라 적는다 ✅")

# ② 각주가 이름과 사유를 싣는다
_note = A.skipped_note(_info["skipped"])
assert "b.py" in _note and "접었다" in _note, _note
assert "2개" not in _note.split("—")[0] or "리뷰 대상 외 4개" in _note, _note
print("  각주가 파일 이름과 사유를 싣는다 ✅")

# ③ **코드 파일이 빠지면 크게 드러난다** — 문서·에셋이 빠지는 것과 무게가 다르다
assert "코드 파일" in _note and "근거가 부족" in _note, _note
_docs_only = A.skipped_note([("x.md", "예산 초과"), ("y.png", "바이너리·메타")])
assert "코드 파일" not in _docs_only, "문서·에셋만 빠졌으면 크게 알리지 않는다: " + _docs_only
print("  코드 파일이 빠진 경우만 크게 알린다 ✅")

# 빠진 것이 없으면 아무 말도 하지 않는다
assert A.skipped_note([]) == "", A.skipped_note([])
print("  빠진 것이 없으면 각주에 그 말이 없다 ✅")

# ④ 예산이 모자라면 **코드 파일을 먼저** 담는다
_keep = A.MAX_DIFF_CHARS
try:
    A.MAX_DIFF_CHARS = 400
    _big = "@@\n" + ("+x\n" * 90)          # 한 파일이 ~280자
    _mixed = [
        {"new_path": "doc.md", "old_path": "doc.md", "diff": _big},   # 먼저 온 문서
        {"new_path": "code.py", "old_path": "code.py", "diff": _big},  # 뒤에 온 코드
    ]
    _r = A.classify(_mixed)
    _dropped = dict(_r["skipped"])
    assert "code.py" not in _dropped, "코드가 먼저 담겨야 한다: " + str(_dropped)
    assert "doc.md" in _dropped, "예산이 모자라면 문서가 밀린다: " + str(_dropped)
    assert "예산 초과" in _dropped["doc.md"], _dropped
    assert "code.py" in _r["diff"], _r["diff"][:80]
finally:
    A.MAX_DIFF_CHARS = _keep
print("  예산이 모자라면 코드 파일을 먼저 담는다 ✅")

# ⑤ **회귀 방지** — access_raw_diffs 가 빠지면 GitLab 이 큰 diff 를 접어 보낸다.
#    2026-08-27 부터 있던 것을 내가 "없다" 고 잘못 진단했다. 그 착각을 막는다.
import inspect as _insp
_src = _insp.getsource(ci_common.GitLab.changes)
assert "access_raw_diffs=true" in _src, (
    "GitLab.changes() 에서 access_raw_diffs 가 빠졌다 — 큰 파일 diff 가 collapsed 로 "
    "비어 오고, 리뷰가 그 파일을 못 본다(S15P21B202-354)")
print("  changes() 가 access_raw_diffs 를 붙인다 ✅")

print("TEST335 OK")
