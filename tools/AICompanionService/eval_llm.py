"""LLM 단독 행동 분류 정확도와 STT/LLM 책임 분리. 게임 행동 성공 여부는 포함하지 않는다.

깨끗한 정답 대본으로 분류하면 LLM의 상한이 나오고, 같은 발화의 STT 결과로 분류하면 실제 성능이 나온다.
두 결과를 겹치면 틀린 원인이 STT인지 LLM인지 갈린다.

로컬 Qwen만 호출한다. 유료 API를 쓰지 않는다. 모델 자원을 쓰므로 게임/음성 테스트와 동시에 돌리지 않는다.
"""
from __future__ import annotations

import argparse
import json
import statistics
from collections import Counter
from datetime import datetime
from pathlib import Path

from engine import ROOT
import companion_v4

CASES_PATH = ROOT / "fixtures" / "stt_cases.jsonl"


def load_cases(path, kind):
    rows = []
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            row = json.loads(line)
            if row.get("expected_action") and (not kind or row.get("kind") == kind):
                rows.append(row)
    return rows


def classify_all(pairs, label):
    """pairs: (표시용 발화, 분류에 넣을 텍스트, 기대 행동)"""
    rows = []
    print(f"=== {label} ===", flush=True)
    for shown, text, expected in pairs:
        if not text or not text.strip():
            # 빈 텍스트는 서버와 같은 규칙으로 none 처리하며 모델을 부르지 않는다.
            actual, ms = "none", 0.0
        else:
            result = companion_v4.classify_companion(text)
            actual, ms = result["action"], result["llm_wall_ms"]
        ok = actual == expected
        rows.append({"utterance": shown, "input": text, "expected": expected,
                     "actual": actual, "ok": ok, "llm_wall_ms": ms})
        print(f"  {'OK' if ok else 'X '} {expected:>18} -> {actual:<18} {text}", flush=True)
    return rows


def summarize(rows):
    latencies = [r["llm_wall_ms"] for r in rows if r["llm_wall_ms"]]
    return {"cases": len(rows), "correct": sum(r["ok"] for r in rows),
            "accuracy": round(sum(r["ok"] for r in rows) / len(rows), 4) if rows else None,
            "llm_ms_median": round(statistics.median(latencies), 1) if latencies else None,
            "llm_ms_p95": round(sorted(latencies)[int(len(latencies) * 0.95) - 1], 1)
                          if len(latencies) >= 2 else None}


def print_confusion(rows, title):
    wrong = Counter((r["expected"], r["actual"]) for r in rows if not r["ok"])
    if not wrong:
        print(f"\n{title}: 오분류 없음")
        return
    print(f"\n{title} (기대 -> 실제, 횟수)")
    for (expected, actual), count in wrong.most_common():
        print(f"  {expected:>18} -> {actual:<18} {count}")


def stt_outputs(result_path, preset):
    data = json.loads(Path(result_path).read_text(encoding="utf-8"))
    if preset not in data["presets"]:
        raise SystemExit(f"{result_path} 에 preset '{preset}' 이 없습니다. "
                         f"있는 것: {list(data['presets'])}")
    return data["model"], {r["wav"]: r["hyp"] for r in data["presets"][preset]["rows"]}


def main():
    parser = argparse.ArgumentParser(description="LLM 분류 정확도와 STT/LLM 책임 분리")
    parser.add_argument("--cases", default=str(CASES_PATH))
    parser.add_argument("--kind", default="read")
    parser.add_argument("--stt-result", help="eval_stt.py가 저장한 JSON. 주면 책임 분리까지 한다")
    parser.add_argument("--preset", default="current", help="STT 결과에서 사용할 preset")
    args = parser.parse_args()

    cases = load_cases(args.cases, args.kind)
    if not cases:
        raise SystemExit("expected_action이 있는 케이스가 없습니다.")

    clean = classify_all([(c["text"], c["text"], c["expected_action"]) for c in cases],
                         f"정답 대본으로 분류 ({len(cases)}개) — LLM 상한")
    report = {"generated": datetime.now().isoformat(timespec="seconds"),
              "scope": "LLM 행동 분류 정확도. STT 인식률·게임 행동 검사 아님.",
              "model": "qwen3:1.7b", "prompt_version": "companion-v4",
              "clean": {"summary": summarize(clean), "rows": clean}}
    print_confusion(clean, "정답 대본 기준 오분류")

    if args.stt_result:
        stt_model, hyps = stt_outputs(args.stt_result, args.preset)
        missing = [c["wav"] for c in cases if c["wav"] not in hyps]
        if missing:
            print(f"\n주의: STT 결과에 없는 케이스 {len(missing)}개는 제외합니다.")
        paired = [c for c in cases if c["wav"] in hyps]
        noisy = classify_all([(c["text"], hyps[c["wav"]], c["expected_action"]) for c in paired],
                             f"STT 결과로 분류 ({len(paired)}개, {stt_model} / {args.preset}) — 실제 성능")
        report["noisy"] = {"stt_model": stt_model, "preset": args.preset,
                           "summary": summarize(noisy), "rows": noisy}
        print_confusion(noisy, "STT 결과 기준 오분류")

        from eval_stt import tight
        grid = Counter()
        both_wrong = []
        for case, clean_row, noisy_row in zip(paired, clean, noisy):
            stt_ok = tight(hyps[case["wav"]]) == tight(case["text"])
            grid[(stt_ok, noisy_row["ok"])] += 1
            if not noisy_row["ok"]:
                both_wrong.append((case, clean_row, noisy_row))

        llm_fault = [n for c, cl, n in both_wrong if cl["ok"] is False]
        stt_fault = [n for c, cl, n in both_wrong if cl["ok"] is True]
        report["split"] = {
            "total": len(paired),
            "end_to_end_correct": sum(r["ok"] for r in noisy),
            "llm_ceiling_correct": sum(r["ok"] for r in clean),
            "wrong_but_llm_handles_clean_text": len(stt_fault),
            "wrong_even_on_clean_text": len(llm_fault),
        }
        print("\n" + "=" * 58)
        print("책임 분리")
        print("=" * 58)
        print(f"  전체 {len(paired)}개")
        print(f"  실제 파이프라인 정답        {sum(r['ok'] for r in noisy):>3}개")
        print(f"  정답 대본이었다면 정답      {sum(r['ok'] for r in clean):>3}개  <- LLM 상한")
        print(f"  틀렸지만 깨끗한 글은 맞춤   {len(stt_fault):>3}개  <- STT 책임")
        print(f"  깨끗한 글에도 틀림          {len(llm_fault):>3}개  <- LLM 책임")

    out = ROOT / "results" / f"llm-eval-{datetime.now():%Y%m%d-%H%M%S}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    clean_summary = report["clean"]["summary"]
    print(f"\n정답 대본 정확도 {clean_summary['correct']}/{clean_summary['cases']} "
          f"· 중앙 {clean_summary['llm_ms_median']}ms · p95 {clean_summary['llm_ms_p95']}ms")
    print(f"저장: {out}")


if __name__ == "__main__":
    main()
