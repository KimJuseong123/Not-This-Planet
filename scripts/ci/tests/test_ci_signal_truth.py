"""CI 신호가 사실을 말하는가 (S15P21B202-606).

실행: python scripts/ci/tests/test_ci_signal_truth.py
네트워크 없이 순수 함수만 부른다.

**무엇을 막는 검사인가.** 2026-09-17 에 develop 이 깨진 채로 *"초록으로 돌아왔다 — 이제
브랜치를 따도 된다"* 카드가 채널에 나갔다. 옛 커밋의 파이프라인이 늦게 끝나면서, **자기보다
나중** 파이프라인을 "직전" 으로 집어 그것의 실패를 자기가 해소했다고 말한 것이다.

거짓 안심은 침묵보다 나쁘다 — 아무도 안 보는 동안 깨진 브랜치 위로 계속 쌓인다.

같이 잠그는 것 하나 더: 실패 코멘트가 ✅ 로 덮이면서 **무엇이 왜 실패했었는지가 MR 에서
사라지던 것**(`!506` 실측).

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 통째로 사라진다.
"""
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.setdefault("CI_PROJECT_ID", "1")
os.environ.setdefault("GITLAB_TOKEN", "x")

import mm_notify as M  # noqa: E402

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


def pipe(pid, status="success", source="push"):
    return {"id": pid, "status": status, "source": source, "web_url": f"u/{pid}"}


print()
print("== CI 신호 ==")


# ── ① 나보다 새 파이프라인이 있으면 아무 말도 하지 않는다 ────────────────
#
#     2026-09-17 그대로의 배치: 내가 203566, 나보다 나중인 203567 이 실패 중.
#     전에는 203567 을 "직전" 으로 집어 "돌아왔다" 를 보냈다.
prev, skip = M.pick_previous([pipe(203567, "failed"), pipe(203566), pipe(203560)], 203566, "push")
check(prev is None, "나보다 새 파이프라인이 실패 중인데 회복 카드를 보낸다 — 거짓 안심이다")
check(skip and "새 파이프라인" in skip, f"건너뛴 이유가 그 사실을 말하지 않는다: {skip}")

#     새것이 **성공**이어도 말하지 않는다 — 그쪽이 자기 카드를 보낸다(중복).
prev, skip = M.pick_previous([pipe(300, "success"), pipe(200), pipe(100, "failed")], 200, "push")
check(prev is None, "새 파이프라인이 성공인데도 내가 회복을 말한다 — 같은 사실이 두 장이 된다")

#     새것이 **도는 중**이어도 말하지 않는다 — 브랜치 끝을 모른다.
prev, skip = M.pick_previous([pipe(300, "running"), pipe(200), pipe(100, "failed")], 200, "push")
check(prev is None, "새 파이프라인이 도는 중인데 브랜치가 초록이라고 말한다")


# ── ② "직전" 은 나보다 오래된 것 중 가장 최근 ────────────────────────────
prev, skip = M.pick_previous([pipe(200), pipe(150, "failed"), pipe(100, "success")], 200, "push")
check(prev is not None and prev["id"] == 150, f"직전을 잘못 골랐다: {prev}")
check(skip is None, f"고를 수 있는데 건너뛴다: {skip}")

#     `running`·`canceled` 는 판단 근거가 아니다 — 그 너머의 끝난 것을 본다.
prev, _ = M.pick_previous([pipe(200), pipe(180, "running"), pipe(170, "canceled"),
                           pipe(150, "failed")], 200, "push")
check(prev is not None and prev["id"] == 150, f"끝나지 않은 파이프라인을 직전으로 삼았다: {prev}")

#     직전이 성공이면 알릴 것이 없다(카드는 부르는 쪽이 거른다) — 여기서는 골라만 준다.
prev, _ = M.pick_previous([pipe(200), pipe(150, "success")], 200, "push")
check(prev is not None and prev["status"] == "success", "직전을 못 골랐다")


# ── ③ 같은 종류끼리만 견준다 (S15P21B202-533 회귀) ───────────────────────
prev, skip = M.pick_previous([pipe(200), pipe(150, "failed", "schedule")], 200, "push")
check(prev is None, "예약 실행의 실패를 머지 푸시가 해소했다고 말한다")

#     **"나보다 새 것" 도 같은 종류만 본다** (`!513` AI 리뷰가 범위를 물었다).
#     예약 실행과 머지 푸시는 도는 잡이 다르므로 서로의 상태를 말할 자격이 없다 —
#     새벽 예약이 실패 중이어도 머지 푸시의 회복 카드는 나가는 것이 맞다.
prev, skip = M.pick_previous([pipe(300, "failed", "schedule"), pipe(200),
                              pipe(150, "failed")], 200, "push")
check(prev is not None and prev["id"] == 150,
      "다른 종류(예약)의 새 파이프라인 때문에 머지 푸시의 회복 카드가 막힌다")

#     `source` 가 비면 거르지 않는다 — 없는 쪽으로 틀리면 카드가 조용히 사라진다.
prev, _ = M.pick_previous([pipe(200), {"id": 150, "status": "failed"}], 200, "push")
check(prev is not None, "source 가 없는 파이프라인을 걸러 버렸다 — 회복 카드가 사라진다")

#     내 id 를 모르면 나이를 견줄 수 없다 → 말하지 않는다.
prev, skip = M.pick_previous([pipe(150, "failed")], "", "push")
check(prev is None and skip, "내 id 를 모르는데 직전을 단정한다")


# ── ④ 실패 기록을 지우지 않고 접어서 남긴다 ──────────────────────────────
FAILED = (f"{M.FAIL_MARKER}\n🔴 **CI 실패 — 무엇이 왜 실패했는지**\n\n"
          f"- ❌ unity-compile: 새 실패 10건\n")
kept = M.history_block(FAILED)
check(M.HISTORY_OPEN in kept, "실패 내용을 접어서 남기지 않는다 — MR 에서 기록이 사라진다")
check("새 실패 10건" in kept, "접었는데 내용이 비었다")
check(M.FAIL_MARKER not in kept, "마커까지 접어 넣었다 — 코멘트가 두 개로 보인다")

#     **한 단계만 남긴다.** 기록이 기록을 품으면 코멘트가 끝없이 길어진다.
once = f"{M.FAIL_MARKER}\n🔴 두 번째 실패\n{kept}"
twice = M.history_block(once)
check(twice.count(M.HISTORY_OPEN) == 1, f"접힌 기록이 겹쳤다({twice.count(M.HISTORY_OPEN)}겹) — 코멘트가 자란다")
check("두 번째 실패" in twice, "새 실패를 남기지 않았다")
check("새 실패 10건" not in twice, "옛 기록까지 안고 간다 — 자라지 않게 한 단계만 남겨야 한다")

#     **이미 해소된 코멘트는 그 기록을 그대로 물려준다.** 통과 뒤 또 통과했다고 해서
#     실패 기록이 사라지면 안 된다(재시도·재푸시로 흔히 두 번 돈다).
resolved = f"{M.FAIL_MARKER}\n{M.RESOLVED_HEAD} — 커밋 `abc1234`\n{kept}\n<sub>자동 생성</sub>"
again = M.history_block(resolved)
check(M.HISTORY_OPEN in again and "새 실패 10건" in again,
      "두 번째 해소에서 실패 기록이 사라진다")
check(M.RESOLVED_HEAD not in again, "해소 문구까지 접어 넣었다")

#     접을 것이 없으면 빈 문자열 — 없는 기록을 꾸며 내지 않는다.
check(M.history_block(f"{M.FAIL_MARKER}\n") == "", "빈 코멘트에서 없는 기록을 만들어 낸다")
check(M.history_block("") == "", "빈 입력에서 무언가를 만들어 낸다")

#     **실패 본문이 `<details>` 를 품고 있어도 잘리지 않는다** (`!513` AI 리뷰).
#     `</details>` 로 끝을 찾으면 안쪽 닫는 태그에서 잘려 뒷조각이 본문에 남는다.
NESTED = (f"{M.FAIL_MARKER}\n🔴 **CI 실패**\n\n"
          f"<details><summary>잡 로그</summary>\n\n로그 줄\n\n</details>\n\n꼬리표 줄\n")
folded = M.history_block(NESTED)
check("꼬리표 줄" in folded and "로그 줄" in folded,
      "안쪽 </details> 에서 잘려 뒷부분이 기록에서 빠졌다")

#     **떼어 내면 정확히 나머지만 남아야 한다.** 조각이 남는지를 "무엇이 없다" 로
#     보면 우연히 통과한다 — 남는 것을 통째로 견준다.
SECOND = f"{M.FAIL_MARKER}\n🔴 두 번째 실패{folded}"
check(M.strip_history(SECOND) == f"{M.FAIL_MARKER}\n🔴 두 번째 실패".strip(),
      f"기록을 떼어 낸 자리에 조각이 남았다:\n      {M.strip_history(SECOND)[:160]!r}")

carried = M.history_block(SECOND)
check(carried.count(M.HISTORY_OPEN) == 1, "중첩 details 때문에 기록이 겹쳤다")
check(carried.count(M.HISTORY_END) == 1, "끝 표시가 여러 개 남았다 — 태그 짝이 어긋난다")
check("두 번째 실패" in carried and "꼬리표 줄" not in carried,
      "옛 기록을 못 떼어 냈다 — 안쪽 태그에서 잘렸다는 뜻이다")

#     두 경로(새 기록 · 물려받은 기록)의 **모양이 같아야** 렌더가 흔들리지 않는다.
check(folded.startswith("\n") and folded.endswith("\n"), f"새 기록의 앞뒤 개행이 없다: {folded[:12]!r}")
check(again.startswith("\n") and again.endswith("\n"), f"물려받은 기록의 모양이 다르다: {again[:12]!r}")


# ── ⑤ 호출부가 최신 것들을 가져와야 가드가 뜻을 가진다 ───────────────────
#
#     `pick_previous` 는 받은 목록 안에서만 판단한다. 호출부가 오래된 것부터
#     가져오면 **나보다 새 파이프라인이 목록에 없어** 가드가 통과해 버린다.
import io  # noqa: E402
import os.path as _p  # noqa: E402

SRC = io.open(_p.join(_p.dirname(HERE), "mm_notify.py"), encoding="utf-8").read()
call = SRC[SRC.index("def branch_resolved"):]
check("order_by=id" in call and "sort=desc" in call,
      "회복 판정이 파이프라인을 최신순으로 가져오지 않는다 — '나보다 새 것' 을 못 본다")


if fails:
    print()
    for f in fails:
        print("❌ " + f)
    raise SystemExit(1)

print("  나보다 새 파이프라인이 있으면 말하지 않는다 ✅")
print("  직전은 나보다 오래된 것 중에서만 고른다 ✅")
print("  실패 기록은 한 단계 접어서 남는다 ✅")
