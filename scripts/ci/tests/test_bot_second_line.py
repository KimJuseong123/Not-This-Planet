"""봇 코멘트마다 "어느 잡 · 막는지" 둘째 줄이 있는지 (S15P21B202-425).

실행: python scripts/ci/tests/test_bot_second_line.py
네트워크·토큰 없이 돈다.

**왜 이 검사가 있나.** MR 하나에 봇 코멘트가 다섯 개까지 붙는다. 사람이 그 코멘트에서
알아야 하는 것은 둘이다 — **어느 잡이 냈는가**(로그를 보려면 필요하다)와
**이것이 머지를 막는가**(이쪽이 더 중요하다).

2026-09-10 에 `!320` 이 파이프라인 초록인 채 머지가 잠겼고 이유가 어디에도 없었다.
미해소 스레드 하나였다. 코멘트마다 그 말이 적혀 있으면 그 자리에서 갈린다.

**문구는 잡마다 다르다.** `bot_line` 이 문구를 정하지 않는 이유가 그것이다 —
`unity-compile` 은 기존 실패만 있으면 통과시키므로 "막는다" 를 무조건 적으면 거짓이
된다(`-309`). 그래서 여기서는 **각 잡이 실제 동작과 맞는 말을 하는지**를 본다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.dirname(HERE))
os.chdir(ROOT)
sys.stdout.reconfigure(encoding="utf-8")

os.environ.setdefault("CI_PROJECT_ID", "1443653")

import ci_common  # noqa: E402
import mr_overlap as O  # noqa: E402
import stack_order as S  # noqa: E402
import unity_report as U  # noqa: E402

print()
print("== 봇 코멘트 둘째 줄 ==")

JOB_URL = "https://lab.example/jobs/1234"


def with_job(name: str, url: str = JOB_URL):
    os.environ["CI_JOB_NAME"] = name
    if url:
        os.environ["CI_JOB_URL"] = url
    else:
        os.environ.pop("CI_JOB_URL", None)


def second(text: str) -> str:
    """마커 다음 줄이 머리줄, 그다음이 이 줄이다."""
    lines = [l for l in text.split("\n")]
    assert lines[0].startswith("<!--"), lines[:2]
    return lines[2]


# ── ① unity-compile — 판정과 문구가 어긋나지 않는다 ──────────────────────
#
#     이 잡은 **종료 코드로** 막는다. `REGRESSION`(1)만 파이프라인을 실패시키고
#     `UNKNOWN`(2)은 `allow_failure` 로 통과한다. 문구를 따로 판단하면 두 곳이
#     어긋나므로, `decide()` 가 정한 값을 그대로 받아 쓴다.
with_job("unity-compile")


def data(failed=()):
    cases = [{"cls": "A", "name": n.split(".")[-1], "result": "Failed", "msg": ""} for n in failed]
    return {"total": len(cases) + 1, "passed": 1, "failed": len(cases), "cases": cases}


_b = U.body(data(["A.x"]), [], set(), "develop", verdict=U.REGRESSION)
assert "unity-compile" in second(_b) and "막습니다" in second(_b), second(_b)
assert JOB_URL in second(_b), "잡 링크가 없다 — 로그를 보려면 파이프라인을 열어야 한다"

_b = U.body(data(["A.x"]), [], None, "develop", verdict=U.UNKNOWN)
assert "막지 않습니다" in second(_b), second(_b)

_b = U.body(data(), [], set(), "develop", verdict=U.PASS)
assert "막지 않습니다" in second(_b), second(_b)

#     **컴파일 실패도 막는다** — 기준선을 보지 않는다(`-342`).
_b = U.body(data(), ["Assets/A.cs(1,1): error CS0103: x"], set(), "develop", verdict=U.REGRESSION)
assert "막습니다" in second(_b), second(_b)

#     **판정을 모르고 부르면 막는지 말하지 않는다.** 모르면서 아는 척하지 않는다 —
#     빈 칸을 그대로 이으면 줄이 ` · ` 로 끝나 사람이 잘린 줄로 읽는다.
_b = U.body(data(), [], set(), "develop")
assert "unity-compile" in second(_b), second(_b)
assert "막" not in second(_b), second(_b)
assert not second(_b).rstrip().endswith("·"), second(_b)
print("  unity-compile — 판정 셋 + 판정 모름 ✅")


# ── ② stack-order — 유일한 하드 블록이다 ─────────────────────────────────
#
#     이 잡은 **미해소 스레드**로 머지 버튼을 잠근다. 파이프라인이 초록인데 머지가
#     안 되는 이유가 여기 있으므로, 코멘트가 그것을 직접 말해야 한다.
with_job("stack-order")
_t = S.blocked("S15P21B202-1-parent", "")
assert "stack-order" in second(_t) and "막습니다" in second(_t), second(_t)
assert "손으로 해소하지 마세요" in second(_t), second(_t)

_t = S.cleared()
assert "막지 않습니다" in second(_t), second(_t)
print("  stack-order — 막는다 / 풀렸다 ✅")


# ── ③ mr-overlap — 막지 않는다, 그리고 남의 MR 에도 붙는다 ───────────────
with_job("mr-overlap")
ME = {"iid": 11, "web_url": "https://lab.example/mr/11", "title": "내 MR",
      "source_branch": "S15P21B202-11-mine"}
OTHER = {"iid": 9, "web_url": "https://lab.example/mr/9", "title": "남의 MR",
         "source_branch": "S15P21B202-9-theirs"}
CMP = {"kind": "conflict", "conflicts": ["METAGAME/A.cs"], "risky": [], "overlap": []}

_m = O.render_mine(ME, [{"other": OTHER, "cmp": CMP, "ai": None, "i_am_newer": True}], None)
assert "mr-overlap" in second(_m) and "막지 않습니다" in second(_m), second(_m)

#     **남의 MR 에 붙는 코멘트는 "어느 잡" 이 헷갈린다** — 링크가 *이* MR 의
#     파이프라인을 가리키므로, 읽는 사람은 자기 파이프라인에서 그 잡을 찾다 못 찾는다.
#     그래서 누가 남긴 것인지 문장으로 적는다.
_o = O.render_other(ME, OTHER, CMP, None, True)
assert "막지 않습니다" in second(_o), second(_o)
assert "!11" in second(_o), "남의 MR 코멘트인데 누가 남긴 것인지 안 적혀 있다: " + second(_o)
print("  mr-overlap — 내 MR / 남의 MR ✅")


# ── ④ 잡 링크가 없으면 이름만 남는다 ────────────────────────────────────
#
#     로컬에서 손으로 돌릴 때가 그렇다. 빈 링크로 `[`job`]()` 을 만들면 코멘트에
#     깨진 링크가 남는다.
with_job("stack-order", url="")
_t = S.blocked("x", "")
assert "`stack-order` 잡의 결과" in second(_t), second(_t)
assert "](" not in second(_t), "링크가 없는데 빈 링크를 만든다: " + second(_t)
print("  CI_JOB_URL 없음 → 이름만 ✅")


# ── ⑤ 마커로 코멘트를 찾는 코드가 깨지지 않는다 ──────────────────────────
#
#     둘째 줄을 끼워 넣으면서 **마커가 첫 줄에서 밀리면** `upsert_note`·`upsert_thread`
#     가 자기 코멘트를 못 찾아 매번 새로 단다. 그러면 MR 이 봇 코멘트로 덮인다.
with_job("unity-compile")
for name, text in (("unity-compile", U.body(data(), [], set(), "develop", verdict=U.PASS)),
                   ("stack-order(막음)", S.blocked("x", "")),
                   ("stack-order(풀림)", S.cleared()),
                   ("mr-overlap(내)", O.render_mine(ME, [], None))):
    assert text.startswith("<!--"), f"{name}: 마커가 첫 줄이 아니다 — 코멘트가 매번 새로 달린다"
assert O.render_other(ME, OTHER, CMP, None, True).startswith("<!--"), "mr-overlap(남): 마커가 첫 줄이 아니다"
print("  마커는 여전히 첫 줄 ✅")


# ── ⑥ ci_common.bot_line — 빈 문구면 그 칸을 뺀다 ───────────────────────
os.environ["CI_JOB_URL"] = JOB_URL
os.environ["CI_COMMIT_SHORT_SHA"] = "abc1234"
assert ci_common.bot_line("x", "") == f"> [`x`]({JOB_URL}) 잡의 결과 · 커밋 `abc1234`"
assert ci_common.bot_line("x", "막습니다").endswith("막습니다")
assert ci_common.bot_line("x", "", with_sha=False) == f"> [`x`]({JOB_URL}) 잡의 결과"
print("  bot_line — 빈 문구면 칸을 뺀다 ✅")

print("== 통과 ==")
