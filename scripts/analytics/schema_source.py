"""이벤트가 기대는 정의를 **게임 코드에서** 뽑는다 (S15P21B202-552).

실행: python scripts/analytics/schema_source.py        지금 값을 계약 모양으로 찍는다

수집할 이벤트는 게임 코드의 값에 얹혀 있다. `floors: [2,1,0,0]` 에서 2가 `Welded`
라는 것은 **`FloorState` 의 순서**가 정한다. 그 순서가 바뀌면 어제까지 쌓인 데이터의
뜻이 조용히 달라진다 — 숫자는 그대로 들어오고 질의도 그대로 돌아가며, 틀린 답만 나온다.

그래서 정의를 한 곳에서 뽑아 **지문**으로 묶는다. 검사(`test_event_schema.py`)가 이 값을
계약 파일과 대조하고, 이벤트는 그 지문을 실어 옛 데이터와 섞이지 않게 한다.

**지문이 바뀌는 것 자체는 잘못이 아니다** — 기획이 바뀌면 당연히 바뀐다.
**조용히 바뀌는 것**이 잘못이다.
"""
import hashlib
import io
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

ASSEMBLY = "METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/ShipAssembly.cs"
PARTSPEC = "METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartSpec.cs"

# `Blocker` 는 사람이 읽는 한글 문장이라 그대로 싣지 않는다 — 문구가 바뀌면 집계가
# 갈라진다. 대신 `Evaluate()` 의 **순서**에 이 코드를 맞춘다. 조기 탈출이라 순서가
# 곧 우선순위다(먼저 걸린 것이 그 순간의 발목).
BLOCKER_CODES = ["no_engine", "incomplete", "wrong_order", "off_pad", "tilted"]


def read(rel):
    return io.open(os.path.join(ROOT, rel), encoding="utf-8").read()


def floor_states():
    """조립 층의 상태 목록. **순서가 곧 이벤트의 숫자**다."""
    t = read(ASSEMBLY)
    body = t[t.index("enum FloorState"):]
    body = body[body.index("{") + 1:body.index("}")]
    return [m.group(1) for m in re.finditer(r"^\s*(\w+)(?:\s*=\s*\d+)?\s*,", body, re.M)]


def parts():
    """조립 순서. 인덱스가 곧 층이고, 길이가 곧 `floors` 배열의 길이다."""
    return [m.group(1) for m in re.finditer(r'new\("(Part_\d+)",', read(PARTSPEC))]


def blockers():
    """`Evaluate()` 가 막는 사유. 빈 문자열(막히지 않음)은 뺀다."""
    t = read(ASSEMBLY)
    ev = t[t.index("public void Evaluate()"):]
    ev = ev[:ev.index("/// <summary>")]
    out = []
    for m in re.finditer(r'Blocker\s*=\s*[$]?"([^"]*)"', ev):
        s = " ".join(m.group(1).split())     # 공백 차이로는 안 깨지게
        if s:
            out.append(s)
    return out


def snapshot():
    """계약에 실리는 값 전부. 여기 든 것이 바뀌면 지문이 바뀐다."""
    return {
        "floor_state": floor_states(),
        "parts": parts(),
        "blocker_codes": BLOCKER_CODES,
        "blocker_messages": blockers(),
    }


def fingerprint(snap):
    """지문. 값이 같으면 같은 지문이 나온다 — 키 순서·공백에 안 흔들린다."""
    canon = json.dumps(snap, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(canon.encode("utf-8")).hexdigest()[:12]


def contract():
    snap = snapshot()
    return {
        "fingerprint": fingerprint(snap),
        "note": "이벤트가 기대는 게임 코드의 정의 (S15P21B202-552). "
                "손으로 고치지 말 것 — `python scripts/analytics/schema_source.py` 가 찍어 준다.",
        **snap,
    }


if __name__ == "__main__":
    import sys
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(contract(), ensure_ascii=False, indent=2))
