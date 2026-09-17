"""잡음 섞인 STT 텍스트에 대한 프롬프트 변형을 비교한다. 서버의 SYSTEM은 수정하지 않는다.

현재 프롬프트는 깨끗한 명령을 전제로 쓰여 있어, 음성인식 오타가 섞이면 none/clarify로 물러난다.
'입력이 음성인식 결과'임을 알려주는 변형이 실제로 복구를 하는지 같은 발화로 비교한다.

로컬 Qwen만 호출한다. 게임/음성 테스트와 동시에 돌리지 않는다.
"""
from __future__ import annotations

import argparse
import json
from collections import Counter
from datetime import datetime
from pathlib import Path

from engine import ROOT
import companion_v4

NOISE_NOTE = """
중요: user의 utterance는 음성인식(STT)을 거친 결과라 오타와 잘못 들린 단어가 섞여 있다.
글자 그대로 해석하지 말고 발음이 비슷한 말로 보정해서 의도를 판단해라.
예: '주어져'/'조어줘'는 '주워줘', '몽동이'는 '몽둥이', '주만해'는 '그만해', '궁'은 '공'일 수 있다.
행동을 가리키는 단어가 하나라도 남아 있으면 그 행동을 선택한다.
none과 clarify는 정말 근거가 없을 때만 쓴다. 발음이 닮은 후보가 있으면 그쪽을 택해라.
"""

# 발음 보정만 하고 clarify/none 판단 기준은 건드리지 않는다.
# 불명확한 명령을 억지로 행동으로 밀면 게임에서 엉뚱한 행동을 하게 되므로 분리해서 측정한다.
PHONETIC_ONLY = """
중요: user의 utterance는 음성인식(STT)을 거친 결과라 오타와 잘못 들린 단어가 섞여 있다.
글자 그대로만 보지 말고 발음이 비슷한 말로 보정해서 의도를 판단해라.
예: '주어져'/'조어줘'는 '주워줘', '몽동이'는 '몽둥이', '주만해'는 '그만해', '궁'은 '공'일 수 있다.
다만 대상이나 행동이 정말 불분명하면 기존 기준대로 clarify, 지원하지 않는 일이면 none을 유지한다.
"""

VARIANTS = {
    "current": "",          # 서버가 지금 쓰는 프롬프트 그대로
    "noise_aware": NOISE_NOTE,
    "phonetic_only": PHONETIC_ONLY,
}


def classify_with(system_suffix, text):
    """companion_v4.SYSTEM을 잠시 바꿔서 같은 경로로 분류한다."""
    original = companion_v4.SYSTEM
    try:
        companion_v4.SYSTEM = original + system_suffix
        return companion_v4.classify_companion(text)
    finally:
        companion_v4.SYSTEM = original


def load_pairs(cases_path, kinds, stt_results):
    cases = []
    for line in Path(cases_path).read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            row = json.loads(line)
            if row.get("expected_action") and row.get("kind") in kinds:
                cases.append(row)
    hyps = {}
    for spec in stt_results:
        path, _, preset = spec.partition("#")
        data = json.loads(Path(path).read_text(encoding="utf-8"))
        for row in data["presets"][preset or "current"]["rows"]:
            hyps[row["wav"]] = row["hyp"]
    return [(c, hyps[c["wav"]]) for c in cases if c["wav"] in hyps]


def main():
    parser = argparse.ArgumentParser(description="잡음 입력용 프롬프트 변형 비교")
    parser.add_argument("--cases", default=str(ROOT / "fixtures" / "stt_cases.jsonl"))
    parser.add_argument("--kinds", default="clear", help="쉼표로 구분 (예: clear,read)")
    parser.add_argument("--stt-result", action="append", required=True,
                        help="eval_stt.py 결과 JSON. '경로#preset' 형식도 가능. 여러 번 지정 가능")
    parser.add_argument("--variants", default="current,noise_aware")
    args = parser.parse_args()

    pairs = load_pairs(args.cases, set(k.strip() for k in args.kinds.split(",")), args.stt_result)
    if not pairs:
        raise SystemExit("평가할 케이스가 없습니다. --kinds 와 --stt-result 를 확인하세요.")
    names = [v.strip() for v in args.variants.split(",")]
    unknown = [n for n in names if n not in VARIANTS]
    if unknown:
        raise SystemExit(f"모르는 변형: {unknown}")

    print(f"케이스 {len(pairs)}개 · 변형 {names}\n", flush=True)
    results, report = {}, {"generated": datetime.now().isoformat(timespec="seconds"),
                           "scope": "잡음 입력에 대한 LLM 프롬프트 변형 비교. STT 인식률 검사 아님.",
                           "cases": len(pairs), "variants": {}}
    for name in names:
        rows = []
        print(f"=== {name} ===", flush=True)
        for case, hyp in pairs:
            expected = case["expected_action"]
            actual = (classify_with(VARIANTS[name], hyp)["action"]
                      if hyp.strip() else "none")
            ok = actual == expected
            rows.append({"wav": case["wav"], "ref": case["text"], "hyp": hyp,
                         "expected": expected, "actual": actual, "ok": ok})
            print(f"  {'OK' if ok else 'X '} {expected:>15} -> {actual:<15} {hyp}", flush=True)
        results[name] = rows
        correct = sum(r["ok"] for r in rows)
        report["variants"][name] = {"correct": correct, "total": len(rows), "rows": rows}
        print(f"  -> {correct}/{len(rows)}\n", flush=True)

    if len(names) == 2:
        a, b = results[names[0]], results[names[1]]
        gained = [x for x, y in zip(a, b) if not x["ok"] and y["ok"]]
        lost = [x for x, y in zip(a, b) if x["ok"] and not y["ok"]]
        print(f"{names[1]} 가 새로 맞춘 것 {len(gained)}개")
        for r in gained:
            print(f"  + {r['ref']}  <-  {r['hyp']}")
        print(f"{names[1]} 가 새로 틀린 것 {len(lost)}개")
        for r in lost:
            print(f"  - {r['ref']}  <-  {r['hyp']}")
        report["gained"] = len(gained)
        report["lost"] = len(lost)

    out = ROOT / "results" / f"llm-prompt-{datetime.now():%Y%m%d-%H%M%S}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n저장: {out}")


if __name__ == "__main__":
    main()
