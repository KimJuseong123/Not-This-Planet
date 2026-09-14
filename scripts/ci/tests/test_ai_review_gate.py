"""AI 리뷰 게이트 — ⛔ 만 막고 나머지는 통과, 판정을 못 읽으면 알리고 통과 (S15P21B202-341).

실행: python scripts/ci/tests/test_ai_review_gate.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "7",
                   "CI_COMMIT_SHORT_SHA": "abc1234", "MM_WEBHOOK_URL": "https://mm.example/h"})

import ci_common  # noqa: E402
import mm_notify  # noqa: E402
import ai_review as AR  # noqa: E402
import ai_review_gate as G  # noqa: E402

print("― S15P21B202-341 AI 리뷰 게이트 ―")


def comment(verdict_line: str, sha: str = "abc1234") -> str:
    """실제 코멘트와 같은 모양으로 만든다 — 첫 줄이 마커, 둘째 줄이 요약 줄."""
    return (f"{AR.MARKER}\n"
            f"🤖 **AI 리뷰 요약** · {verdict_line} · `cli/claude-opus-5` · 커밋 `{sha}`\n\n"
            f"요약 문장.\n\n**팀장이 확인할 것**\n1. 무언가\n")


def run(notes, sha="abc1234", mr=None):
    """게이트를 돌리고 (종료코드, MM 으로 보낸 카드들) 를 준다."""
    sent = []
    meta = mr if mr is not None else {"source_branch": "S15P21B202-1-x", "target_branch": "develop"}

    def http(method, url, headers=None, body=None, timeout=30):
        if url.startswith("https://mm.example"):
            sent.append(body["text"]); return 200, {}
        if "/notes" in url:
            return 200, notes
        return 200, meta

    os.environ["CI_COMMIT_SHORT_SHA"] = sha
    ci_common.http = http
    mm_notify.http = http
    return G.main(), sent


# ── ① ⛔ 는 막는다 ──────────────────────────────────────────────────────────
_rc, _sent = run([{"id": 1, "body": comment(AR.VERDICT_LINE["block"])}])
assert _rc == 1, f"⛔ 는 막아야 한다 (rc={_rc})"
print("  ⛔ 보류 권고 → 차단 ✅")

# ── ② ⚠️·✅ 는 통과한다 — ⚠️ 를 막으면 아무것도 머지되지 않는다(188건 중 73%) ──
for _key in ("warn", "ok"):
    _rc, _sent = run([{"id": 1, "body": comment(AR.VERDICT_LINE[_key])}])
    assert _rc == 0, f"{_key} 는 통과해야 한다 (rc={_rc})"
    assert not _sent, f"{_key} 에 알림을 보내면 안 된다: {_sent}"
print("  ⚠️ 확인 후 머지 · ✅ 머지 가능 → 통과, 알림 없음 ✅")

# ── ③ ❓ 는 통과시키되 **크게 알린다** ─────────────────────────────────────
_rc, _sent = run([{"id": 1, "body": comment(AR.VERDICT_LINE["unknown"])}])
assert _rc == 0, "❓ 는 막지 않는다 — 인프라 사고 하나로 모든 MR 이 멈춘다"
assert len(_sent) == 1 and "판정을 못 읽었다" in _sent[0], _sent
print("  ❓ 판정 불가 → 통과 + MM 카드 ✅")

# ── ④ 코멘트가 아예 없으면 통과 + 알림 (fail-open) ─────────────────────────
_rc, _sent = run([{"id": 9, "body": "사람이 쓴 다른 코멘트"}])
assert _rc == 0, "코멘트가 없을 때 막으면 러너·모델 사고가 팀 전체를 멈춘다"
assert len(_sent) == 1 and "판정을 못 읽었다" in _sent[0], _sent
print("  리뷰 코멘트 없음 → 통과 + MM 카드 ✅")

# ── ⑤ **낡은 코멘트로는 판정하지 않는다** ─────────────────────────────────
#     낡은 ⛔ 로 고쳐진 코드를 막아서도, 낡은 ✅ 로 새 코드를 통과시켜서도 안 된다.
_rc, _sent = run([{"id": 1, "body": comment(AR.VERDICT_LINE["block"], sha="0000aaa")}])
assert _rc == 0, "낡은 ⛔ 로 막으면 고쳐서 push 한 사람이 이유 없이 갇힌다"
assert len(_sent) == 1 and "이 커밋의 것이 아니다" in _sent[0], _sent
_rc, _sent = run([{"id": 1, "body": comment(AR.VERDICT_LINE["ok"], sha="0000aaa")}])
assert _rc == 0 and len(_sent) == 1, "낡은 ✅ 도 판정으로 쓰지 않는다"
print("  다른 커밋의 코멘트 → 판정으로 쓰지 않고 알린다 ✅")

# ── ⑥ 판정 문구를 못 읽으면 통과 + 알림 ───────────────────────────────────
_rc, _sent = run([{"id": 1, "body": f"{AR.MARKER}\n🤖 **AI 리뷰 요약** · 커밋 `abc1234`\n"}])
assert _rc == 0 and "판정 문구를 찾지 못했다" in _sent[0], _sent
print("  판정 문구 없음 → 통과 + MM 카드 ✅")

# ── ⑦ `<details>` 안의 리뷰어별 판정을 집지 않는다 ────────────────────────
#     상세에는 `판정 \\`block\\`` 같은 문자열과 사람이 쓴 인용이 섞여 있다. 문자열을
#     아무 데서나 찾으면 ✅ 인 MR 을 막는다.
_mixed = (comment(AR.VERDICT_LINE["ok"])
          + "\n<details><summary>상세</summary>\n\n"
          + f"**범위** — 판정 `block`\n- [높음] 어딘가 — {AR.VERDICT_LINE['block']}\n</details>\n")
_rc, _sent = run([{"id": 1, "body": _mixed}])
assert _rc == 0, "요약 줄이 ✅ 면 상세에 ⛔ 문구가 있어도 막지 않는다"
assert not _sent, _sent
print("  판정은 요약 줄에서만 읽는다 ✅")

# ── ⑧ **탈출구가 없다** — 게이트를 끄는 변수를 두지 않았다 ────────────────
_src = open(os.path.join(os.path.dirname(HERE), "ai_review_gate.py"), encoding="utf-8").read()
for _forbidden in ("SKIP", "BYPASS", "DISABLE", "GATE_OFF", "FORCE"):
    assert f'env("{_forbidden}' not in _src and f"env('{_forbidden}" not in _src, (
        f"게이트에 탈출구 변수({_forbidden})가 생겼다 — -341 은 탈출구를 두지 않기로 했다. "
        f"우회가 있으면 수정 과정에서 repo-hygiene·mr-lint 를 새로 위반해도 안 걸린다")
print("  게이트를 끄는 변수가 없다 ✅")

# ── ⑨ 판정 문구 맵이 ai_review 와 어긋나지 않는다 ─────────────────────────
#     게이트가 상수를 복사해 두면 문구가 바뀐 순간 모든 MR 이 ❓ 로 보인다.
assert set(G._BY_LINE.values()) == set(AR.VERDICT_LINE.keys()), G._BY_LINE
assert G.BLOCKING == ("block",), f"⛔ 만 막는다: {G.BLOCKING}"
print("  판정 문구를 ai_review 에서 가져온다 · block 만 차단 ✅")

# ── ⑩ 차단할 때 `❌` 줄을 남긴다 — 없으면 🔴 카드가 이유를 못 싣는다 ──────
import io as _io  # noqa: E402
import contextlib as _ctx  # noqa: E402
_buf = _io.StringIO()
with _ctx.redirect_stdout(_buf):
    run([{"id": 1, "body": comment(AR.VERDICT_LINE["block"])}])
_out = _buf.getvalue()
assert "❌" in _out, "차단 이유가 ❌ 줄로 안 나오면 mm_notify 가 '이유를 찾지 못함' 으로 적는다"
assert "MR 본문에 근거를 적는다" in _out, "리뷰가 틀렸을 때의 길이 로그에 있어야 한다: " + _out
print("  차단 이유가 ❌ 줄로 남는다 ✅")

def _keys_only(block: str) -> str:
    """주석을 걷어낸다 — 설명에 든 낱말이 설정으로 오인되지 않게."""
    return "\n".join(l.split("#", 1)[0] for l in block.splitlines())


# ── ⑪ `.gitlab-ci.yml` 배선 ───────────────────────────────────────────────
_yml = open(os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(HERE))), ".gitlab-ci.yml"), encoding="utf-8").read()
assert "ai-review-gate:" in _yml, "게이트 잡이 없다"
assert "python scripts/ci/ai_review_gate.py" in _yml, "게이트 스크립트를 부르지 않는다"
_gate_block = _yml.split("ai-review-gate:", 1)[1].split("\n# ──", 1)[0]
assert "allow_failure" not in _keys_only(_gate_block), "게이트가 allow_failure 면 막지 못한다"
# ai-review 는 비차단 그대로 — 되돌리기가 잡 하나 지우기로 끝나야 한다
_rev_block = _yml.split("\nai-review:", 1)[1].split("\nai-review-gate:", 1)[0]
assert "allow_failure: true" in _keys_only(_rev_block), "ai-review 는 비차단으로 남긴다(-341)"
# 알림 두 잡이 게이트를 기다린다 — 빠지면 ⛔ 로 막혀도 카드가 안 나가고(-344),
# 목록이 서로 다르면 막힌 MR 에 '해소됨' 이 찍힌다(-289)
for _job in ("mm-ci-failed:", "mm-ci-resolved:"):
    _blk = _yml.split(_job, 1)[1].split("\n  script:", 1)[0]
    assert "job: ai-review-gate" in _blk, f"{_job} 가 게이트를 needs 에 두지 않았다"
    assert "optional: true" in _blk.split("job: ai-review-gate", 1)[1], f"{_job} 의 게이트 needs 가 optional 이 아니다"
print("  잡 배선: 게이트는 차단·ai-review 는 비차단·알림 두 곳이 기다린다 ✅")


# ── ⑫ 막는지 여부가 판정 바로 옆에 있다 (S15P21B202-420) ────────────────
#
#     예전에는 이 말이 **각주**에 있었다. 그래서 ⛔ 인데도 "의견이니 넘어가도 된다" 로
#     읽혔다. 머리줄로 옮겼으므로 이제 코멘트 **앞쪽**에 있어야 한다.
_common = dict(mr_meta="`cli/x` · 커밋 `abc1234`", roles=["scope"], results={}, raw={},
               summary=None, skipped=[], failed=[], defaults={})
_blocked = AR.render(merged={"verdict": "block", "checks": {}, "findings": [], "questions": []}, **_common)
assert "ai-review-gate` 가 **머지를 막습니다**" in _blocked, _blocked[:400]
_head = _blocked.split("\n\n", 1)[0]
assert "막습니다" in _head, "막는다는 말이 머리줄에 없다: " + _head
assert "참고용" not in _blocked, "⛔ 코멘트에 '참고용' 이 남아 있다"
for _v in ("warn", "ok", "unknown"):
    _passing = AR.render(merged={"verdict": _v, "checks": {}, "findings": [], "questions": []}, **_common)
    assert "**머지를 막지 않습니다**" in _passing.split("\n\n", 1)[0], f"{_v}: " + _passing[:300]
print("  막는지 여부가 판정 바로 옆(머리줄)에 있다 ✅")


# ── ⑬ ⛔ 면 막고 있는 것이 맨 앞에 목록으로 (S15P21B202-420) ─────────────
#
#     예전에는 ⛔ 일 때도 "발견 사항 표" 에서 사람이 무엇이 막는지 찾아야 했다.
_high = {"severity": "high", "file": "a/b.cs", "line": "12", "issue": "널 참조",
         "fix": "가드 추가", "role": "code"}
_low = {"severity": "low", "file": "c.cs", "line": "", "issue": "사소", "fix": "", "role": "code"}
_m = {"verdict": "block", "checks": {}, "findings": [_high, _low], "questions": []}
assert AR.blocking_findings(_m) == [_high], "높음만 차단 사유로 세워야 한다"
_out = AR.render(merged=_m, **_common)
assert "막고 있는 것 1건" in _out, _out[:500]
# 차단 목록이 판정 표보다 앞이어야 한다 — 뒤에 있으면 찾아야 한다.
assert _out.index("막고 있는 것") < _out.index("확인 항목"), "차단 목록이 표 뒤에 있다"
assert "`a/b.cs:12`" in _out and "가드 추가" in _out, "위치와 제안이 목록에 없다"

# 높음이 없으면 중간까지 올린다 — 리뷰어가 높음 없이 block 을 낸 경우다.
_med = dict(_high, severity="medium")
assert AR.blocking_findings({"verdict": "block", "checks": {}, "findings": [_med, _low],
                             "questions": []}) == [_med]
# 막지 않는 판정에는 차단 목록이 없다.
for _v in ("ok", "warn", "unknown"):
    assert AR.blocking_findings({"verdict": _v, "checks": {}, "findings": [_high],
                                 "questions": []}) == []

# 같은 발견 사항을 목록과 표에 두 번 적으면 간략화가 아니라 두 배다.
_m2 = {"severity": "medium", "file": "d.cs", "line": "3", "issue": "캐시 없음",
       "fix": "캐시", "role": "code"}
_two = AR.render(merged={"verdict": "block", "checks": {}, "findings": [_high, _m2],
                         "questions": []}, **dict(_common, results={"scope": {"x": 1}}))
assert _two.count("널 참조") == 1, "차단 사유가 목록과 표에 두 번 나온다"
assert "캐시 없음" in _two, "차단 사유가 아닌 발견 사항이 표에서 사라졌다"
assert "위 차단 1건 외" in _two, "표가 차단 사유를 뺀 나머지임을 밝히지 않는다"

# 높음 하나뿐이면 표가 비는데, 그때 "발견 사항 없음" 은 거짓이다 — 막고 있는데 없다고 적힌다.
_only = AR.render(merged={"verdict": "block", "checks": {}, "findings": [_high],
                          "questions": []}, **dict(_common, results={"scope": {"x": 1}}))
assert "발견 사항 없음" not in _only, "막는 것이 있는데 '발견 사항 없음' 이라고 적혀 있다"

# 커밋 표기는 머리줄에만 — 게이트가 그 줄에서 읽으므로 뺄 수 없고, 둘째 줄에 또 적으면 중복이다.
assert _only.splitlines()[2].count("커밋 `") == 0, "커밋 표기가 두 줄에 나온다"

# 값이 같은 발견 사항이 둘이면, 하나만 목록에 올라가도 표에서 둘 다 사라지면 안 된다.
# 리뷰어가 여럿이라 같은 자리를 함께 지목하는 일이 실제로 생긴다.
_dup_a = {"severity": "high", "file": "e.cs", "line": "7", "issue": "같은 문제",
          "fix": "같은 제안", "role": "scope"}
_dup_b = dict(_dup_a)          # 값은 같고 객체는 다르다
assert _dup_a == _dup_b and _dup_a is not _dup_b
_dm = {"verdict": "block", "checks": {}, "findings": [_dup_a, _dup_b], "questions": []}
assert len(AR.blocking_findings(_dm)) == 2, "높음 둘 다 차단 사유여야 한다"

# 높음 하나 + 값이 같은 중간 하나 — 중간은 표에 남아야 한다.
_half = {"verdict": "block", "checks": {},
         "findings": [_dup_a, dict(_dup_a, severity="medium")], "questions": []}
assert len(AR.blocking_findings(_half)) == 1, "높음이 있으면 높음만 차단 사유다"
_half_out = AR.render(merged=_half, **dict(_common, results={"scope": {"x": 1}}))
assert "발견 사항 1건" in _half_out, ("값이 같은 중간 항목이 표에서 사라졌다: "
                                    + _half_out[:500])
print("  값이 같은 발견 사항을 표에서 함께 지우지 않는다 ✅")
print("  차단 사유·커밋 표기를 두 번 적지 않는다 ✅")
print("  ⛔ 면 막고 있는 것이 맨 앞에 목록으로 나온다 ✅")


# ── ⑭ 판정 표는 전부 ✅ 일 때만 접힌다 (S15P21B202-420) ─────────────────
_all_ok = {k: {"status": "ok", "why": "괜찮다"} for k in AR.CHECK_ORDER}
_folded = AR.render(merged={"verdict": "ok", "checks": _all_ok, "findings": [], "questions": []}, **_common)
assert "<details><summary>판정 표" in _folded, "전부 ✅ 인데 표가 펼쳐져 있다"

_one_warn = dict(_all_ok, scope={"status": "warn", "why": "확인 필요"})
_open = AR.render(merged={"verdict": "warn", "checks": _one_warn, "findings": [], "questions": []}, **_common)
assert "<details><summary>판정 표" not in _open, "⚠️ 가 있는데 표가 접혀 있다"
assert "| 범위 | ⚠️" in _open, "⚠️ 행이 보이지 않는다"
print("  판정 표가 전부 ✅ 면 접히고, ⚠️ 가 있으면 펼쳐진다 ✅")


# ── ⑮ 어느 잡의 결과인지 밝힌다 (S15P21B202-420) ────────────────────────
assert "`ai-review` 잡의 결과" in _blocked or "](" in _blocked.split("\n")[2], \
    "어느 잡의 결과인지 적혀 있지 않다: " + _blocked[:300]
print("  코멘트가 어느 잡의 결과인지 밝힌다 ✅")


# ── ⑯ render 가 낸 코멘트를 게이트가 실제로 읽는다 (S15P21B202-420) ──────
#
#     **이 테스트가 없어서 실제로 깨질 뻔했다.** `-420` 에서 머리줄을 `### 🤖 …` 로
#     바꾸고 커밋 표기를 다음 줄(`<sub>`)로 내렸더니 `verdict_of` 가 None 을 돌려줬다.
#     ⛔ 가 막지 못하는 상태였는데 아래 ①~⑮ 는 전부 통과했다 — 판정을 **손으로 만든**
#     본문으로만 시험했기 때문이다. 그래서 render 의 실제 출력으로 왕복을 확인한다.
#
#     계약(`verdict_of`): 판정 문구와 커밋 표기가 `🤖` 로 시작하는 **같은 줄**에 있다.
for _v in ("block", "warn", "ok", "unknown"):
    _body = AR.render(merged={"verdict": _v, "checks": {}, "findings": [],
                              "questions": []}, **_common)
    _why = f"게이트가 render 출력에서 {_v} 를 못 읽는다 → ⛔ 가 막지 못한다"
    assert G.verdict_of(_body) == _v, _why + "\n" + _body[:300]
    _head = _body.splitlines()[1]
    assert _head.startswith("🤖"), f"{_v}: 머리줄이 🤖 로 시작하지 않는다: {_head}"
    assert "커밋 `" in _head, f"{_v}: 커밋 표기가 머리줄에 없다: {_head}"
print("  render 출력을 게이트가 네 판정 모두 읽는다 ✅  (-420 에서 깨질 뻔했다)")


# ── ⑰ 릴리즈 MR(→ master)은 건너뛴다 ─────────────────────────────────────
#     ai_review.py 가 같은 조건에서 리뷰를 생략한다. 그러면 코멘트가 없어 게이트가
#     "판정을 못 읽었다" 로 떨어지고, **릴리스마다 헛된 🟠 카드**가 나간다. 헛된 카드가
#     반복되면 사람이 카드를 무시하게 되어 fail-open 경고 자체가 무력해진다.
for _src, _tgt in (("release/S15P21B202-318-v0.1.0", "master"), ("develop", "master")):
    _rc, _sent = run([], mr={"source_branch": _src, "target_branch": _tgt})
    assert _rc == 0, f"릴리즈 MR 을 막으면 안 된다: {_src} → {_tgt}"
    assert not _sent, f"릴리즈 MR 에 카드를 보내면 안 된다: {_sent}"
# 릴리즈가 아닌 MR 에서 코멘트가 없으면 여전히 알린다 (④ 와 같은 경로가 살아 있는지)
_rc, _sent = run([], mr={"source_branch": "S15P21B202-1-x", "target_branch": "develop"})
assert _rc == 0 and len(_sent) == 1, "일반 MR 의 fail-open 경고가 사라졌다"
# hotfix → master 는 예외가 아니다 — develop 을 거치지 않은 새 코드다
_rc, _sent = run([{"id": 1, "body": comment(AR.VERDICT_LINE["block"])}],
                 mr={"source_branch": "hotfix/S15P21B202-9-x", "target_branch": "master"})
assert _rc == 1, "hotfix → master 는 검사 예외가 아니다"
print("  릴리즈 MR 은 건너뛴다 · hotfix 는 예외가 아니다 ✅")


# ── ⑱ 커밋 표기가 없는 코멘트는 판정으로 쓰지 않는다 ─────────────────────
#     `if head and seen` 이면 commit_of() 가 None 일 때 sha 검증을 건너뛰고 판정을 그대로
#     믿는다 — 낡은 ✅ 코멘트가 새 커밋을 통과시킬 수 있다 (!279 AI 리뷰 중간).
_no_sha = f"{AR.MARKER}\n🤖 **AI 리뷰 요약** · {AR.VERDICT_LINE['ok']} · `cli/x`\n"
_rc, _sent = run([{"id": 1, "body": _no_sha}])
assert _rc == 0, "막지는 않는다(fail-open)"
assert len(_sent) == 1 and "커밋 표기가 없다" in _sent[0], _sent
# ⛔ 라도 마찬가지다 — 확인하지 못한 것은 어느 방향으로도 판정으로 쓰지 않는다
_no_sha_block = f"{AR.MARKER}\n🤖 **AI 리뷰 요약** · {AR.VERDICT_LINE['block']} · `cli/x`\n"
_rc, _sent = run([{"id": 1, "body": _no_sha_block}])
assert _rc == 0 and "커밋 표기가 없다" in _sent[0], _sent
print("  커밋 표기가 없으면 판정으로 쓰지 않는다 \u2705")

# ── ⑲ 🤖 줄이 여러 개면 전부 본다 ───────────────────────────────────────
#     첫 🤖 줄에서 못 찾고 바로 None 을 내면, 요약 줄 포맷이 바뀌어 🤖 가 다른 줄에
#     먼저 나오는 순간 **모든 MR 이 ❓ 가 되어 게이트가 사실상 꺼진다.**
_two = (f"{AR.MARKER}\n"
        f"🤖 이번 리뷰는 리뷰어 3명이 돌았습니다\n"
        f"🤖 **AI 리뷰 요약** · {AR.VERDICT_LINE['block']} · `cli/x` · 커밋 `abc1234`\n")
_rc, _sent = run([{"id": 1, "body": _two}])
assert _rc == 1, "판정 문구가 두 번째 🤖 줄에 있어도 읽어야 한다"
print("  🤖 줄이 여러 개면 전부 훑는다 \u2705")

# ── ⑳ 판정 문구를 못 찾을 때 로그에 **요약 줄**을 싣는다 ────────────────
#     첫 줄은 MARKER(HTML 주석)라 원인 추적에 쓸모가 없다.
#     카드에는 사유만, **잡 로그에는 요약 줄까지** 싣는다 — 원인 추적은 로그에서 한다.
_buf2 = _io.StringIO()
with _ctx.redirect_stdout(_buf2):
    run([{"id": 1, "body": AR.MARKER + chr(10) + "🤖 판정 문구가 없는 요약 줄 · 커밋 `abc1234`" + chr(10)}])
_log = _buf2.getvalue()
assert "판정 문구가 없는 요약 줄" in _log, "잡 로그에 요약 줄이 실려야 한다: " + _log
assert AR.MARKER not in _log, "MARKER 를 싣지 않는다: " + _log
print("  판정을 못 읽으면 로그에 요약 줄을 싣는다(MARKER 아님) ✅")

print("TEST341 OK")
