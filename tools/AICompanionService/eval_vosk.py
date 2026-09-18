"""Vosk 한국어 인식 평가. 자유 전사와 문법 제약을 같은 잣대로 비교한다.

실험용 .venv-eval 로 실행한다. 서버 .venv 에는 vosk 를 설치하지 않는다.
  .\.venv-eval\Scripts\python.exe eval_vosk.py --kind read

문법 제약은 인식 어휘를 14개 행동의 표현으로 한정한다. 탐색 공간이 좁아 환각이 구조적으로 줄지만,
대본에 없는 말은 [unk] 로 나온다. 이는 '미지원 명령'이며 통신 실패와 구분해야 한다.
"""
from __future__ import annotations

import argparse
import json
import time
import wave
from pathlib import Path

import stt_metrics as M

ROOT = Path(__file__).resolve().parent
CASES_PATH = ROOT / "fixtures" / "stt_cases.jsonl"
DEFAULT_MODEL = "vosk-model-small-ko-0.22"
SCRIPT_PATH = ROOT / "eval_stt_script.json"


def grammar_phrases():
    """대본 문장을 그대로 문법으로 쓴다. 평가 대상과 같은 문구라 낙관 편향이 있음을 보고한다."""
    lines = json.loads(SCRIPT_PATH.read_text(encoding="utf-8"))["lines"]
    return sorted({ln["text"] for ln in lines})


def read_wav(path):
    with wave.open(str(path), "rb") as w:
        if w.getnchannels() != 1 or w.getsampwidth() != 2 or w.getframerate() != 16000:
            raise SystemExit(f"16kHz 1채널 16bit WAV만 지원합니다: {path}")
        return w.readframes(w.getnframes()), w.getnframes() / 16000.0


def transcribe(model, raw, seconds, grammar):
    from vosk import KaldiRecognizer
    rec = (KaldiRecognizer(model, 16000, json.dumps(grammar, ensure_ascii=False))
           if grammar else KaldiRecognizer(model, 16000))
    started = time.perf_counter()
    rec.AcceptWaveform(raw)
    text = json.loads(rec.FinalResult()).get("text", "")
    return text.strip(), (time.perf_counter() - started) * 1000


def main():
    parser = argparse.ArgumentParser(description="Vosk 한국어 인식 평가")
    parser.add_argument("--cases", default=str(CASES_PATH))
    parser.add_argument("--kind", default="read")
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--modes", default="free,grammar", help="free / grammar")
    args = parser.parse_args()

    model_dir = ROOT / "models" / args.model
    if not model_dir.exists():
        raise SystemExit(f"Vosk 모델이 없습니다: {model_dir}")
    cases = M.load_cases(args.cases, args.kind)
    if not cases:
        raise SystemExit("평가할 케이스가 없습니다.")

    from vosk import Model, SetLogLevel
    SetLogLevel(-1)
    print(f"케이스 {len(cases)}개 · 모델 {args.model}", flush=True)
    started = time.perf_counter()
    model = Model(str(model_dir))
    print(f"모델 로드 {round((time.perf_counter() - started) * 1000)}ms\n", flush=True)

    phrases = grammar_phrases()
    presets, names = {}, [m.strip() for m in args.modes.split(",")]
    for mode in names:
        grammar = (phrases + ["[unk]"]) if mode == "grammar" else None
        print(f"=== {mode} " + (f"(문법 {len(phrases)}구문 + [unk])" if grammar else "(자유 전사)")
              + " ===", flush=True)
        rows = []
        for case in cases:
            wav = Path(args.cases).parent / case["wav"]
            if not wav.exists():
                print(f"  [파일없음] {case['wav']}", flush=True)
                continue
            raw, seconds = read_wav(wav)
            hyp, ms = transcribe(model, raw, seconds, grammar)
            row = M.build_row(case, hyp, ms, seconds)
            rows.append(row)
            M.print_row(row)
        summary = M.summarize(rows)
        presets[mode] = {"config": {"grammar": bool(grammar),
                                    "grammar_size": len(phrases) if grammar else 0},
                         "summary": summary, "rows": rows}
        M.print_summary(mode, summary)

    M.print_table(presets, names)
    out = M.save_report(ROOT / "results", args.model, f"{args.model} / Vosk", args.cases, presets)
    if "grammar" in names:
        print("\n주의: 문법 구문이 낭독 대본과 같은 문장입니다. 실사용보다 낙관적인 값입니다.")
        print("      실제 배포에서는 사용자가 다르게 말하므로 [unk] 비율이 올라갑니다.")
    print(f"\n저장: {out}")


if __name__ == "__main__":
    main()
