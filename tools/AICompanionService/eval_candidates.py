"""후보 매칭이 값어치를 하는지 측정한다. STT 결과와 발음이 가까운 등록 명령을 LLM에 함께 준다.

행동 선택은 여전히 LLM이 한다. 매칭기는 후보를 좁힐 뿐이며, 유사도가 문턱보다 낮으면
후보를 주지 않고 원문만 넘긴다. 등록되지 않은 표현이 매칭기 때문에 왜곡되지 않게 하기 위함이다.

후보는 eval_command_bank.json에서 온다. 평가 대본과 문구가 겹치면 정보가 새므로
--check 로 겹침을 먼저 확인한다.

로컬 Qwen만 호출한다. 게임/음성 테스트와 동시에 돌리지 않는다.
"""
from __future__ import annotations

import argparse
import json
import re
import unicodedata
from collections import Counter
from datetime import datetime
from pathlib import Path

from engine import ROOT
from stt_metrics import tight, edit_distance
import companion_v4

BANK_PATH = ROOT / "eval_command_bank.json"
SCRIPT_PATH = ROOT / "eval_stt_script.json"
CASES_PATH = ROOT / "fixtures" / "stt_cases.jsonl"


def jamo(text):
    """한글을 자모로 분해한다. '몽동이'와 '몽둥이'는 음절로는 다르지만 자모로는 한 글자 차이다."""
    return re.sub(r"[^\w]", "", unicodedata.normalize("NFD", tight(text)))


def similarity(a, b):
    ja, jb = jamo(a), jamo(b)
    if not ja or not jb:
        return 0.0
    return max(0.0, 1.0 - edit_distance(ja, jb) / max(len(ja), len(jb)))


def load_bank():
    data = json.loads(BANK_PATH.read_text(encoding="utf-8"))["commands"]
    return [(phrase, entry["action"]) for entry in data for phrase in entry["phrases"]]


def check_overlap(bank):
    script = {tight(l["text"]) for l in json.loads(SCRIPT_PATH.read_text(encoding="utf-8"))["lines"]}
    return [(p, a) for p, a in bank if tight(p) in script]


def candidates_for(hyp, bank, top, floor):
    """발음이 가까운 순으로 top개. 1등이 문턱 미만이면 후보를 주지 않는다."""
    ranked = sorted(((similarity(hyp, p), p, a) for p, a in bank), reverse=True)
    if not ranked or ranked[0][0] < floor:
        return []
    return ranked[:top]


def classify(hyp, picked):
    """후보가 있으면 SYSTEM에 한 줄 덧붙여 분류한다. 없으면 현재 운영 프롬프트 그대로."""
    if not hyp.strip():
        return "none"
    original = companion_v4.SYSTEM
    try:
        if picked:
            listed = ", ".join(f"'{p}'({a})" for _, p, a in picked)
            companion_v4.SYSTEM = original + (
                "\n이번 입력과 발음이 가까운 등록 명령 후보: " + listed +
                "\n후보는 참고용이다. 발화가 후보 중 하나를 잘못 들은 것으로 보이면 그 행동을 고르고,"
                " 어느 후보와도 맞지 않으면 후보를 무시하고 직접 판단해라.")
        return companion_v4.classify_companion(hyp)["action"]
    finally:
        companion_v4.SYSTEM = original


def load_pairs(kinds, stt_results):
    cases = []
    for line in CASES_PATH.read_text(encoding="utf-8").splitlines():
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
    parser = argparse.ArgumentParser(description="후보 매칭 유무 비교")
    parser.add_argument("--kinds", default="clear")
    parser.add_argument("--stt-result", action="append", required=True,
                        help="eval_stt.py 결과 JSON. '경로#preset' 형식 가능")
    parser.add_argument("--top", type=int, default=3)
    parser.add_argument("--floor", type=float, default=0.35,
                        help="1등이 이 값보다 낮으면 후보를 주지 않는다")
    parser.add_argument("--check", action="store_true", help="겹침만 확인하고 종료")
    args = parser.parse_args()

    bank = load_bank()
    overlap = check_overlap(bank)
    print(f"문장은행 {len(bank)}개 · 평가 대본과 겹치는 표현 {len(overlap)}개", flush=True)
    if overlap:
        print("  경고: 겹치는 표현이 있어 수치가 낙관적으로 나옵니다.")
        for p, a in overlap:
            print(f"    {p} ({a})")
    if args.check:
        return

    pairs = load_pairs(set(k.strip() for k in args.kinds.split(",")), args.stt_result)
    if not pairs:
        raise SystemExit("평가할 케이스가 없습니다.")
    print(f"케이스 {len(pairs)}개 · top {args.top} · 문턱 {args.floor}\n", flush=True)

    rows, no_candidate = [], 0
    for case, hyp in pairs:
        picked = candidates_for(hyp, bank, args.top, args.floor)
        if not picked:
            no_candidate += 1
        plain = classify(hyp, [])
        withcand = classify(hyp, picked)
        expected = case["expected_action"]
        row = {"wav": case["wav"], "ref": case["text"], "hyp": hyp, "expected": expected,
               "plain": plain, "with_candidates": withcand,
               "candidates": [{"phrase": p, "action": a, "score": round(s, 3)} for s, p, a in picked]}
        rows.append(row)
        mark = {(True, True): "==", (True, False): "잃음", (False, True): "얻음",
                (False, False): "  "}[(plain == expected, withcand == expected)]
        top_note = f"1등 '{picked[0][1]}'({picked[0][2]}) {picked[0][0]:.2f}" if picked else "후보 없음"
        print(f"  {mark:4} {expected:>15} | 후보없이 {plain:<15} 후보와 {withcand:<15} | {hyp}")
        print(f"       {top_note}", flush=True)

    a = sum(r["plain"] == r["expected"] for r in rows)
    b = sum(r["with_candidates"] == r["expected"] for r in rows)
    gained = [r for r in rows if r["plain"] != r["expected"] and r["with_candidates"] == r["expected"]]
    lost = [r for r in rows if r["plain"] == r["expected"] and r["with_candidates"] != r["expected"]]
    print(f"\n후보 없이 {a}/{len(rows)}  ->  후보 제시 {b}/{len(rows)}")
    print(f"얻음 {len(gained)} · 잃음 {len(lost)} · 문턱 미달로 후보를 주지 않은 경우 {no_candidate}")
    for r in lost:
        print(f"  잃음: '{r['hyp']}' 기대 {r['expected']} -> {r['with_candidates']}")

    out = ROOT / "results" / f"candidates-{datetime.now():%Y%m%d-%H%M%S}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(
        {"generated": datetime.now().isoformat(timespec="seconds"),
         "scope": "후보 매칭 유무 비교. 행동 선택은 LLM이 한다.",
         "bank_size": len(bank), "overlap_with_script": len(overlap),
         "top": args.top, "floor": args.floor,
         "plain_correct": a, "with_candidates_correct": b, "total": len(rows),
         "gained": len(gained), "lost": len(lost), "rows": rows},
        ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n저장: {out}")


if __name__ == "__main__":
    main()
