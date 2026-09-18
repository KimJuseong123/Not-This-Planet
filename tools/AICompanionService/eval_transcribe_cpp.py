"""transcribe.cpp 런타임으로 GGUF 음성 모델을 평가한다. Nemotron/Parakeet, Qwen3-ASR 등에 쓴다.

실험용 .venv-eval 로 실행한다. 서버 .venv 에는 transcribe-cpp 를 설치하지 않는다.
  .\.venv-eval\Scripts\python.exe eval_transcribe_cpp.py --kind read --backend cpu
  .\.venv-eval\Scripts\python.exe eval_transcribe_cpp.py --model models/qwen3-asr/Qwen3-ASR-1.7B-Q5_K_M.gguf --language ko

언어 코드는 모델마다 다르다 (nemotron=ko-KR, qwen3-asr=ko). model.capabilities.languages로 확인한다.
--sweep 의 r<n> 은 Parakeet 계열 전용 우측 컨텍스트이며, 지원하지 않는 모델에서는 건너뛴다.

--backend 로 CPU와 GPU를 같은 녹음으로 비교한다. 이 PC에는 GPU가 있지만, 게임이 GPU를 쓰므로
GPU 사용 여부는 성능만이 아니라 게임 프레임과의 경합까지 보고 사용자가 결정할 문제다.

모델 라이선스를 배포 전에 확인해야 한다 (nemotron은 OpenMDW-1.1).
"""
from __future__ import annotations

import argparse
import array
import time
import wave
from pathlib import Path

import stt_metrics as M

ROOT = Path(__file__).resolve().parent
CASES_PATH = ROOT / "fixtures" / "stt_cases.jsonl"
DEFAULT_MODEL = "models/nemotron-asr/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf"


def read_pcm(path):
    """런타임은 PCM 바이트를 float32로 해석한다. int16을 그대로 넘기면 길이가 절반인 잡음이 된다."""
    with wave.open(str(path), "rb") as w:
        if w.getnchannels() != 1 or w.getsampwidth() != 2 or w.getframerate() != 16000:
            raise SystemExit(f"16kHz 1채널 16bit WAV만 지원합니다: {path}")
        raw = w.readframes(w.getnframes())
        seconds = w.getnframes() / 16000.0
    samples = array.array("h")
    samples.frombytes(raw)
    floats = array.array("f", (s / 32768.0 for s in samples))
    return floats.tobytes(), seconds


def result_text(result):
    """Result / StreamText 어느 쪽이든 최종 문자열을 꺼낸다."""
    if isinstance(result, str):
        return result.strip()
    for attr in ("text", "transcript", "final", "committed"):
        value = getattr(result, attr, None)
        if isinstance(value, str):
            return value.strip()
    return str(result).strip()


def main():
    parser = argparse.ArgumentParser(description="transcribe.cpp GGUF 음성 모델 평가")
    parser.add_argument("--cases", default=str(CASES_PATH))
    parser.add_argument("--kind", default="read")
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--backend", default="cpu", help="cpu / auto (auto면 GPU를 쓸 수 있다)")
    parser.add_argument("--threads", type=int, default=4, help="서버와 같은 4스레드가 기본")
    parser.add_argument("--language", default="ko-KR", help="이 런타임은 ko-KR 형식을 쓴다")
    parser.add_argument("--sweep", default="oneshot",
                        help="쉼표로 구분. oneshot 또는 r<우측컨텍스트> (예: r0,r4,r13)")
    args = parser.parse_args()

    model_path = ROOT / args.model if not Path(args.model).is_absolute() else Path(args.model)
    if not model_path.exists():
        raise SystemExit(f"GGUF 모델이 없습니다: {model_path}")
    cases = M.load_cases(args.cases, args.kind)
    if not cases:
        raise SystemExit("평가할 케이스가 없습니다.")

    import transcribe_cpp as tc
    print(f"케이스 {len(cases)}개 · {model_path.name} · backend={args.backend} "
          f"· {args.threads}스레드 · 언어 {args.language}", flush=True)
    started = time.perf_counter()
    model = tc.Model(str(model_path), backend=args.backend)
    print(f"모델 로드 {round((time.perf_counter() - started) * 1000)}ms "
          f"· arch={getattr(model, 'arch', '?')} · device={getattr(model, 'device', '?')}\n", flush=True)

    session = model.session(n_threads=args.threads)
    presets, names = {}, []
    for spec in [x.strip() for x in args.sweep.split(",")]:
        if spec == "oneshot":
            family, label = None, "oneshot"
        else:
            # 이 모델이 받는 확장은 ParakeetStreamOptions 하나뿐이다 (model.accepts로 확인).
            family = tc.ParakeetStreamOptions(att_context_right=int(spec.lstrip("r")))
            label = f"att_right={spec.lstrip('r')}"
        print(f"=== {label} ===", flush=True)
        rows, failed = [], None
        for case in cases:
            wav = Path(args.cases).parent / case["wav"]
            if not wav.exists():
                print(f"  [파일없음] {case['wav']}", flush=True)
                continue
            pcm, seconds = read_pcm(wav)
            started = time.perf_counter()
            try:
                if family is None:
                    hyp = result_text(session.run(pcm, language=args.language))
                else:
                    # 스트리밍 슬롯: 한 발화를 넣고 확정한다. 발화마다 새 스트림을 연다.
                    with session.stream(language=args.language, family=family) as stream:
                        stream.feed(pcm)
                        stream.finalize()
                        hyp = result_text(stream.text())
            except Exception as exc:
                # 지원하지 않는 우측 컨텍스트 값 등은 설정 단위로 건너뛴다.
                failed = f"{type(exc).__name__}: {exc}"
                break
            ms = (time.perf_counter() - started) * 1000
            row = M.build_row(case, hyp, ms, seconds)
            rows.append(row)
            M.print_row(row)
        if failed:
            print(f"  건너뜀 — {failed}\n", flush=True)
            continue
        summary = M.summarize(rows)
        presets[label] = {"config": {"backend": args.backend, "threads": args.threads,
                                     "stream": spec}, "summary": summary, "rows": rows}
        names.append(label)
        M.print_summary(label, summary)

    tag = f"{model_path.stem.split(chr(46))[0][:28]}-{args.backend}"
    M.print_table(presets, names)
    out = M.save_report(ROOT / "results", tag,
                        f"{model_path.name} / transcribe.cpp {args.backend}", args.cases, presets)
    print(f"\n저장: {out}")


if __name__ == "__main__":
    main()
