"""게임 용어 단어 오디션. 어떤 표현이 음성인식에서 살아남는지 잰다.

문장 전체가 아니라 **목표 단어 하나**가 인식 결과에 남았는지만 본다.
같은 문장에서 단어만 바꾼 최소 대립쌍이므로, 문장 난이도가 아니라 단어 차이를 본다.

  .\\.venv\\Scripts\\python.exe eval_word_audition.py --model whisper-small-ko-zeroth

녹음은 watch_captures.py --script eval_word_audition.json 으로 모은 뒤
eval_stt_register.py 로 kind=audition 등록한다. 같은 단어를 여러 번 읽으면 평균을 낸다.
"""
from __future__ import annotations

import argparse
import json
import re
import statistics
import time
import unicodedata
from collections import defaultdict
from datetime import datetime
from pathlib import Path

from engine import ROOT, THREADS
from stt_metrics import tight, edit_distance, load_cases, NEGATION

SCRIPT_PATH = ROOT / "eval_word_audition.json"
CASES_PATH = ROOT / "fixtures" / "stt_cases.jsonl"


def jamo(text):
    return re.sub(r"[^\w]", "", unicodedata.normalize("NFD", tight(text)))


def best_window_score(target, hypothesis):
    """인식 결과 안에서 목표 단어와 가장 닮은 구간을 찾아 0~1로 점수를 낸다.

    단순 포함 검사는 '야구방망이'가 '야구 방맹이'로 나오면 0점이 되어 너무 가혹하다.
    반대로 문장 전체 유사도는 나머지 단어에 희석된다. 그래서 구간 최대값을 쓴다.
    """
    t, h = jamo(target), jamo(hypothesis)
    if not t:
        return 0.0
    if not h:
        return 0.0
    best = 0.0
    for width in range(max(1, len(t) - 2), len(t) + 3):
        for start in range(0, max(1, len(h) - width + 1)):
            window = h[start:start + width]
            if not window:
                continue
            score = 1.0 - edit_distance(t, window) / max(len(t), len(window))
            best = max(best, score)
    return max(0.0, best)


def main():
    parser = argparse.ArgumentParser(description="게임 용어 단어 오디션")
    parser.add_argument("--cases", default=str(CASES_PATH))
    parser.add_argument("--kind", default="audition")
    parser.add_argument("--model", default="whisper-small-ko-zeroth")
    parser.add_argument("--compute", default="int8")
    parser.add_argument("--survive", type=float, default=0.75,
                        help="이 점수 이상이면 '살아남았다'로 센다")
    parser.add_argument("--script", default="", help="다른 대본 (예: eval_grammar_audition.json)")
    args = parser.parse_args()

    script_path = Path(args.script) if args.script else SCRIPT_PATH
    script = {l["id"]: l for l in json.loads(script_path.read_text(encoding="utf-8"))["lines"]}
    cases = [c for c in load_cases(args.cases, args.kind) if c.get("script_id") in script]
    if not cases:
        raise SystemExit(f"kind={args.kind} 로 등록된 오디션 녹음이 없습니다. "
                         "watch_captures.py 로 녹음하고 eval_stt_register.py 로 등록하세요.")
    model_dir = ROOT / "models" / args.model
    if not (model_dir / "model.bin").exists():
        raise SystemExit(f"모델이 없습니다: {model_dir}")

    from faster_whisper import WhisperModel
    from faster_whisper.audio import decode_audio

    print(f"녹음 {len(cases)}개 · 모델 {args.model} · 생존 기준 {args.survive}\n", flush=True)
    model = WhisperModel(str(model_dir), device="cpu", compute_type=args.compute,
                         cpu_threads=THREADS, num_workers=1, local_files_only=True)

    scored = defaultdict(list)
    rows = []
    for case in cases:
        wav = Path(args.cases).parent / case["wav"]
        if not wav.exists():
            print(f"  [파일없음] {case['wav']}", flush=True)
            continue
        line = script[case["script_id"]]
        audio = decode_audio(str(wav), sampling_rate=16000)
        started = time.perf_counter()
        segments, _ = model.transcribe(audio, language="ko", beam_size=1, vad_filter=True,
                                       condition_on_previous_text=False)
        hyp = " ".join(s.text.strip() for s in segments).strip()
        score = best_window_score(line["target"], hyp)
        # 부정 명령은 긍정으로 뒤집히면 AI가 정반대로 움직인다. 단어 점수와 별개로 따로 센다.
        wants_stop = line.get("expected_action") == "cancel"
        kept_negation = any(n in tight(hyp) for n in NEGATION)
        flipped = wants_stop and not kept_negation
        scored[(line["group"], line["target"], bool(line.get("current")))].append(score)
        rows.append({"script_id": line["id"], "group": line["group"], "target": line["target"],
                     "current": bool(line.get("current")), "text": line["text"], "hyp": hyp,
                     "score": round(score, 3), "survived": score >= args.survive,
                     "expected_action": line.get("expected_action"),
                     "negation_lost": flipped,
                     "asr_ms": round((time.perf_counter() - started) * 1000, 1)})
        mark = "OK" if score >= args.survive else ("~ " if score >= 0.5 else "X ")
        warn = "   [!] 부정 소실 — 정반대 행동 위험" if flipped else ""
        print(f"  {mark} {score:.2f}  {line['target']:<10} | {line['text']:<16} -> {hyp}{warn}",
              flush=True)

    print("\n" + "=" * 66)
    print("그룹별 결과 (점수 높은 순, ★=지금 쓰는 말)")
    print("=" * 66)
    by_group = defaultdict(list)
    for (group, target, current), values in scored.items():
        by_group[group].append((statistics.mean(values), target, current, len(values),
                                sum(v >= args.survive for v in values)))
    winners = {}
    for group in by_group:
        entries = sorted(by_group[group], reverse=True)
        print(f"\n[{group}]")
        for mean, target, current, n, alive in entries:
            star = "★" if current else " "
            bar = "#" * int(mean * 20)
            print(f"  {star} {mean:.2f}  생존 {alive}/{n}  {target:<10} {bar}")
        best = entries[0]
        now = max((e for e in entries if e[2]), default=None)
        if now and best[1] != now[1] and best[0] - now[0] >= 0.15:
            winners[group] = (now[1], best[1], now[0], best[0])

    flips = [r for r in rows if r.get("negation_lost")]
    if flips:
        print("\n" + "=" * 66)
        print(f"부정이 사라진 발화 {len(flips)}개 — 정반대 행동 위험, 이 표현은 쓰지 않습니다")
        for r in flips:
            print(f"  {r['text']:<20} -> {r['hyp']}")

    print("\n" + "=" * 66)
    if winners:
        print("바꿀 만한 후보 (지금 말보다 0.15 이상 높음)")
        for group, (old, new, a, b) in winners.items():
            print(f"  {group:<10} {old} ({a:.2f})  ->  {new} ({b:.2f})")
    else:
        print("지금 쓰는 말보다 뚜렷하게 나은 후보가 없습니다.")
    print("표본이 작으면 같은 대본을 한 번 더 읽어 평균을 늘리세요.")

    out = ROOT / "results" / f"{script_path.stem}-{datetime.now():%Y%m%d-%H%M%S}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(
        {"generated": datetime.now().isoformat(timespec="seconds"),
         "scope": "단어 단위 음성인식 생존율. 문장 정확도나 행동 정확도가 아님.",
         "model": f"{args.model} / {args.compute}", "survive_threshold": args.survive,
         "recordings": len(rows), "negation_lost": len(flips), "rows": rows,
         "winners": {g: {"current": o, "candidate": n, "current_score": round(a, 3),
                         "candidate_score": round(b, 3)} for g, (o, n, a, b) in winners.items()}},
        ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n저장: {out}")


if __name__ == "__main__":
    main()
