"""wav2vec2 CTC(ONNX) 한국어 인식 평가. 자기회귀 디코더가 없어 구조적으로 환각이 불가능하다.

서버 .venv 로 실행한다. onnxruntime·numpy는 이미 설치돼 있어 새 패키지가 필요 없다.
  .\.venv\Scripts\python.exe eval_wav2vec2_onnx.py --kind read

Whisper와 달리 30초 창으로 패딩하지 않고 입력 길이만큼만 계산하므로 짧은 명령에 유리할 수 있다.
"""
from __future__ import annotations

import argparse
import json
import time
import wave
from pathlib import Path

import numpy as np

import stt_metrics as M

ROOT = Path(__file__).resolve().parent
CASES_PATH = ROOT / "fixtures" / "stt_cases.jsonl"
DEFAULT_MODEL = "wav2vec2-ko-onnx"


def load_vocab(model_dir):
    tok = json.loads((model_dir / "tokenizer.json").read_text(encoding="utf-8"))
    vocab = tok["model"]["vocab"]
    table = {i: t for t, i in vocab.items()}
    return table, vocab.get("[PAD]"), vocab.get("|")


def read_audio(path):
    with wave.open(str(path), "rb") as w:
        if w.getnchannels() != 1 or w.getsampwidth() != 2 or w.getframerate() != 16000:
            raise SystemExit(f"16kHz 1채널 16bit WAV만 지원합니다: {path}")
        raw = w.readframes(w.getnframes())
    audio = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0
    # wav2vec2 기본 전처리: 평균 0, 분산 1 정규화.
    return (audio - audio.mean()) / (audio.std() + 1e-7), len(audio) / 16000.0


def ctc_greedy(logits, table, blank_id, delim_id):
    """반복 축약 후 blank 제거. 단어 구분자 '|'는 공백으로 바꾼다."""
    ids = logits.argmax(-1)
    out, previous = [], -1
    for token_id in ids:
        if token_id != previous and token_id != blank_id:
            out.append(" " if token_id == delim_id else table.get(int(token_id), ""))
        previous = token_id
    return "".join(out).strip()


def main():
    parser = argparse.ArgumentParser(description="wav2vec2 CTC(ONNX) 한국어 인식 평가")
    parser.add_argument("--cases", default=str(CASES_PATH))
    parser.add_argument("--kind", default="read")
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--threads", type=int, default=4, help="서버와 같은 4스레드가 기본")
    args = parser.parse_args()

    model_dir = ROOT / "models" / args.model
    if not (model_dir / "model.onnx").exists():
        raise SystemExit(f"ONNX 모델이 없습니다: {model_dir}")
    cases = M.load_cases(args.cases, args.kind)
    if not cases:
        raise SystemExit("평가할 케이스가 없습니다.")

    import onnxruntime as ort
    options = ort.SessionOptions()
    options.intra_op_num_threads = args.threads
    options.inter_op_num_threads = 1
    print(f"케이스 {len(cases)}개 · 모델 {args.model} · {args.threads}스레드", flush=True)
    started = time.perf_counter()
    session = ort.InferenceSession(str(model_dir / "model.onnx"), options,
                                   providers=["CPUExecutionProvider"])
    table, blank_id, delim_id = load_vocab(model_dir)
    print(f"모델 로드 {round((time.perf_counter() - started) * 1000)}ms · "
          f"vocab {len(table)} · blank {blank_id}\n", flush=True)

    rows = []
    for case in cases:
        wav = Path(args.cases).parent / case["wav"]
        if not wav.exists():
            print(f"  [파일없음] {case['wav']}", flush=True)
            continue
        audio, seconds = read_audio(wav)
        started = time.perf_counter()
        logits = session.run(["logits"], {"input_values": audio[None, :]})[0][0]
        hyp = ctc_greedy(logits, table, blank_id, delim_id)
        ms = (time.perf_counter() - started) * 1000
        row = M.build_row(case, hyp, ms, seconds)
        rows.append(row)
        M.print_row(row)

    summary = M.summarize(rows)
    presets = {"ctc_greedy": {"config": {"decoder": "greedy", "threads": args.threads},
                              "summary": summary, "rows": rows}}
    M.print_summary("ctc_greedy", summary)
    M.print_table(presets, ["ctc_greedy"])
    out = M.save_report(ROOT / "results", args.model, f"{args.model} / wav2vec2 CTC ONNX",
                        args.cases, presets)
    print(f"\n저장: {out}")


if __name__ == "__main__":
    main()
