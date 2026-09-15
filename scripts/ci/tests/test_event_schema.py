"""이벤트 정의가 코드와 어긋나면 잡는다 (S15P21B202-552).

실행: python scripts/ci/tests/test_event_schema.py
네트워크·Unity 없이 저장소의 실제 소스를 읽는다.

**왜 이 검사가 있나.** 수집할 이벤트는 게임 코드의 값에 얹혀 있다. `floors: [2,1,0,0]`
에서 2가 `Welded` 라는 것은 `FloorState` 의 **순서**가 정한다. 거기에 값이 하나
끼어들면 어제까지 쌓인 데이터의 뜻이 **조용히** 달라진다 — 숫자는 그대로 들어오고
질의도 그대로 돌아가며, 틀린 답만 나온다.

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 `assert` 는 통째로
사라져 이 파일이 아무것도 안 하면서 성공으로 끝난다. 검사를 무력화하는 가장 조용한
길이라 막아 둔다.
"""
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.join(ROOT, "scripts", "analytics"))
sys.stdout.reconfigure(encoding="utf-8")

import schema_source as S  # noqa: E402

CONTRACT = os.path.join(ROOT, "scripts", "analytics", "event-schema.json")


def fail(msg):
    raise AssertionError(msg)


def check(ok, msg):
    if not ok:
        fail(msg)


saved = json.load(io.open(CONTRACT, encoding="utf-8"))
now = S.contract()

HOWTO = ("고치는 법 — 아래를 그대로 저장하면 된다:\n\n"
         "    python scripts/analytics/schema_source.py > scripts/analytics/event-schema.json\n")

# ── 1) 계약의 모든 값이 같은가 ──
# 지문만 보면 안 된다: 지문은 **데이터의 뜻**만 묶으므로 한글 문구가 바뀌어도 그대로다.
# 문구가 바뀐 것은 데이터를 가르지는 않지만 사람이 확인해야 한다 — 순서가 뒤바뀐
# 것이라면 `BLOCKER_CODES` 매핑이 통째로 밀린다.
diff = []
for key in ("floor_state", "parts", "blocker_codes", "blocker_messages"):
    a, b = saved.get(key, []), now[key]
    if a == b:
        continue
    diff.append(f"  [{key}]")
    for x in a:
        if x not in b:
            diff.append(f"    - 없어짐  {x}")
    for x in b:
        if x not in a:
            diff.append(f"    + 새로    {x}")
    if sorted(a) == sorted(b) and a != b:
        diff.append("    ! 순서가 바뀌었다 — 숫자의 뜻과 코드 매핑이 달라진다")
    diff.append("")

if diff:
    split = ("**옛 데이터의 뜻이 달라진다.** 버리라는 말이 아니라 지문으로 나눠 보라는 말이다."
             if saved.get("fingerprint") != now["fingerprint"] else
             "지문은 그대로다 — 문구만 바뀌었으므로 **데이터는 안 갈린다.** 확인만 하면 된다.")
    fail("\n".join(["이벤트가 기대는 게임 코드의 정의가 바뀌었다.", ""] + diff
                   + [split, "", HOWTO, "새 계약:", json.dumps(now, ensure_ascii=False, indent=2)]))

check(saved.get("fingerprint") == now["fingerprint"],
      f"값은 같은데 지문이 다르다 — 계약이 손으로 고쳐졌다.\n\n{HOWTO}")
print(f"  지문 {now['fingerprint']} — 코드와 계약이 같다 ✅")

# ── 2) 지문이 뜻만 묶는가 ──
# 한글 문구가 지문에 섞이면 오타 하나로 데이터가 갈린다.
check("blocker_messages" not in S.MEANING,
      "지문에 한글 문구가 들어 있다 — 오타를 고치기만 해도 옛 데이터와 비교할 수 없게 된다")
check(set(S.MEANING) == {"floor_state", "parts", "blocker_codes"}, f"MEANING 이 바뀌었다: {S.MEANING}")
print("  지문은 뜻만 묶는다 (문구는 뺀다) ✅")

# ── 3) 뽑기가 조용히 실패하지 않았는가 ──
# 정규식이 표기 변화로 절반만 잡아도 지문이 바뀌어 1) 이 잡는다. 다만 사람이 시키는
# 대로 계약만 다시 만들면 그때는 아무도 안 본다 — 그래서 개수를 계약과 맞춰 본다.
for key in ("floor_state", "parts", "blocker_messages"):
    check(len(now[key]) == len(saved.get(key, [])),
          f"{key} 의 개수가 계약과 다르다 ({len(saved.get(key, []))} → {len(now[key])}) — "
          "뽑기가 깨졌는지 먼저 확인한다")
print(f"  층 {len(now['floor_state'])}종 · 파츠 {len(now['parts'])}개 · "
      f"막힘 {len(now['blocker_messages'])}가지 ✅")

# ── 4) 막힘 코드와 문구의 개수가 맞는가 ──
# 코드는 손으로 적고 문구는 코드에서 뽑는다. 한쪽만 늘면 매핑이 밀린다 —
# 그러면 `tilted` 로 집계한 것이 사실은 `off_pad` 다.
check(len(now["blocker_codes"]) == len(now["blocker_messages"]),
      "막힘 코드와 문구의 개수가 다르다 — 매핑이 밀린다\n"
      f"  코드 {len(now['blocker_codes'])}개: {now['blocker_codes']}\n"
      f"  문구 {len(now['blocker_messages'])}개: {now['blocker_messages']}\n"
      "  `Evaluate()` 에 막는 자리가 늘었다면 schema_source.BLOCKER_CODES 에도 더한다.")
print("  막힘 코드와 문구의 개수가 같다 ✅")

# ── 5) 쉼표 없는 마지막 값도 잡는가 ──
# 실제로 뚫려 있던 자리다(`!461` AI 리뷰). C# 관행대로 맨 끝에 쉼표 없이 값을 더하면
# 전에는 그 값이 통째로 빠졌고, 지문이 그대로라 검사가 통과했다.
sample = "\n".join(["    A = 0,", "    B,", "    /// <summary>주석</summary>", "    C", "}"])
got = [m for m in (__import__("re").match(r"\s*(\w+)", c) for c in
                   S.strip_comments(sample[:sample.index("}")]).split(",")) if m]
check([m.group(1) for m in got] == ["A", "B", "C"],
      f"쉼표 없는 마지막 값을 놓친다: {[m.group(1) for m in got]}")
print("  쉼표 없는 마지막 값도 잡는다 ✅")

# ── 6) 계약이 사람이 읽을 수 있는가 ──
check(bool(saved.get("note")), "계약에 note 가 없다 — 이 파일이 무엇인지 적혀 있어야 한다")
print("  계약에 설명이 있다 ✅")

print("OK  이벤트 스키마 — 코드와 계약이 어긋나지 않았다")
