"""이벤트 정의가 코드와 어긋나면 잡는다 (S15P21B202-552).

실행: python scripts/ci/tests/test_event_schema.py
네트워크·Unity 없이 저장소의 실제 소스를 읽는다.

**왜 이 검사가 있나.** 수집할 이벤트는 게임 코드의 값에 얹혀 있다. `floors: [2,1,0,0]`
에서 2가 `Welded` 라는 것은 `FloorState` 의 **순서**가 정한다. 거기에 값이 하나
끼어들면 어제까지 쌓인 데이터의 뜻이 **조용히** 달라진다 — 숫자는 그대로 들어오고
질의도 그대로 돌아가며, 틀린 답만 나온다.

기획과 게임 구성이 빠르게 바뀌는 동안 그것을 사람이 기억하기를 기대할 수 없다.
`-292`(문서가 없어진 잡을 설명) · `-544`(AI 리뷰 목록에 빠진 디렉터리)와 같은 모양이다.

**지문이 바뀌는 것 자체는 잘못이 아니다.** 기획이 바뀌면 당연히 바뀐다. 이 검사가
막는 것은 **모르는 채 바뀌는 것**이다.
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

saved = json.load(io.open(CONTRACT, encoding="utf-8"))
now = S.contract()

# ── 1) 지문이 같은가 ──
if saved.get("fingerprint") != now["fingerprint"]:
    lines = ["이벤트가 기대는 게임 코드의 정의가 바뀌었다.", ""]
    for key in ("floor_state", "parts", "blocker_codes", "blocker_messages"):
        a, b = saved.get(key, []), now[key]
        if a == b:
            continue
        lines.append(f"  [{key}]")
        for x in a:
            if x not in b:
                lines.append(f"    - 없어짐  {x}")
        for x in b:
            if x not in a:
                lines.append(f"    + 새로    {x}")
        if sorted(a) == sorted(b) and a != b:
            lines.append("    ! 순서가 바뀌었다 — 숫자의 뜻이 달라진다")
        lines.append("")
    lines += [
        "**옛 데이터의 뜻이 달라진다.** 버리라는 말이 아니라 지문으로 나눠 보라는 말이다.",
        "",
        "고치는 법 — 아래를 그대로 저장하면 된다:",
        "",
        "    python scripts/analytics/schema_source.py > scripts/analytics/event-schema.json",
        "",
        "새 계약:",
        json.dumps(now, ensure_ascii=False, indent=2),
    ]
    raise AssertionError("\n".join(lines))

print(f"  지문 {now['fingerprint']} — 코드와 계약이 같다 ✅")

# ── 2) 계약이 사람이 읽을 수 있는가 ──
# 이 파일은 이벤트를 해석하는 근거다. 값만 있고 설명이 없으면 반년 뒤에 못 읽는다.
assert saved.get("note"), "계약에 note 가 없다 — 이 파일이 무엇인지 적혀 있어야 한다"
print("  계약에 설명이 있다 ✅")

# ── 3) 층과 사유가 비어 있지 않은가 ──
# 뽑기가 조용히 실패하면 빈 목록이 나오고, 그러면 지문이 우연히 맞아 버릴 수 있다.
assert len(now["floor_state"]) >= 3, now["floor_state"]
assert len(now["parts"]) >= 1, now["parts"]
assert len(now["blocker_messages"]) >= 1, now["blocker_messages"]
print(f"  층 {len(now['floor_state'])}종 · 파츠 {len(now['parts'])}개 · "
      f"막힘 {len(now['blocker_messages'])}가지 ✅")

# ── 4) 막힘 코드와 문구의 개수가 맞는가 ──
# 코드는 손으로 적고 문구는 코드에서 뽑는다. 한쪽만 늘면 매핑이 밀린다 —
# 그러면 `tilted` 로 집계한 것이 사실은 `off_pad` 다.
assert len(now["blocker_codes"]) == len(now["blocker_messages"]), (
    "막힘 코드와 문구의 개수가 다르다 — 매핑이 밀린다\n"
    f"  코드 {len(now['blocker_codes'])}개: {now['blocker_codes']}\n"
    f"  문구 {len(now['blocker_messages'])}개: {now['blocker_messages']}\n"
    "  `Evaluate()` 에 막는 자리가 늘었다면 schema_source.BLOCKER_CODES 에도 더한다.")
print("  막힘 코드와 문구의 개수가 같다 ✅")

print("OK  이벤트 스키마 — 코드와 계약이 어긋나지 않았다")
