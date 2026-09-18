"""Opt-in local model regression check; no microphone recording or paid APIs."""
import json
import companion_v4
from verify_live_v3 import CASES as OLD_CASES

CASES = OLD_CASES + [
    ("몽둥이 주워줘", "pickup_bat"), ("방망이 좀 가져와", "pickup_bat"),
    ("배트 집어봐", "pickup_bat"),
    ("용접건 주워줘", "pickup_welder"), ("플라즈마 총 집어줘", "pickup_welder"),
    ("근처 도구 하나 집어", "pickup_tool"),
    ("그거 내려놔", "drop_tool"), ("들고 있는 거 놓아", "drop_tool"),
    ("공 때려줘", "hit_target"), ("저거 한 대 때려봐", "hit_target"),
    ("운석 막아줘", "defend_meteors"), ("운석 쳐내줘", "defend_meteors"),
    ("운석 방어 그만해", "cancel"), ("몽둥이 줍지 마", "cancel"),
    ("용접해줘", "none"), ("도구 던져줘", "none")
]

if __name__ == "__main__":
    passed = 0
    for text, expected in CASES:
        result = companion_v4.classify_companion(text)
        ok = result["action"] == expected
        passed += ok
        print(json.dumps({"text": text, "expected": expected, "actual": result["action"],
                          "ok": ok, "ms": result["llm_wall_ms"]}, ensure_ascii=False), flush=True)
    print(json.dumps({"passed": passed, "total": len(CASES)}))
