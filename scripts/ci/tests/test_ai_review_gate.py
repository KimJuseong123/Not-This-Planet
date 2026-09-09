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


def run(notes, sha="abc1234"):
    """게이트를 돌리고 (종료코드, MM 으로 보낸 카드들) 를 준다."""
    sent = []

    def http(method, url, headers=None, body=None, timeout=30):
        if url.startswith("https://mm.example"):
            sent.append(body["text"]); return 200, {}
        if "/notes" in url:
            return 200, notes
        return 200, {}

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

print("TEST341 OK")
