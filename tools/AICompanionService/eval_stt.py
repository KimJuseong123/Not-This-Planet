"""Whisper 단독 정확도 측정. STT만 검사하며 LLM 분류·게임 행동 성공 여부는 포함하지 않는다.

실행 중인 서버와 별개 프로세스에서 모델을 한 번 더 올린다. 게임/음성 테스트와 동시에 실행하지 않는다.
모델은 이미 models/ 에 있는 것만 사용하고 새로 내려받지 않는다.
"""
from __future__ import annotations

import argparse
import json
import time
import unicodedata
from datetime import datetime
from pathlib import Path

from engine import ROOT, THREADS

CASES_PATH = ROOT / "fixtures" / "stt_cases.jsonl"
DEFAULT_MODEL = "whisper-base"

# make_synthetic_audio.ps1이 만든 기계 음성. 사람 목소리 인식률의 근거가 아니라 도구 점검용이다.
SYNTHETIC_SEED = [
    {"wav": "synthetic-pull-pin.wav", "text": "야 이거 고정못 좀 뽑아줘",
     "expected_action": "pull_pin", "kind": "synthetic"},
    {"wav": "synthetic-follow.wav", "text": "나 좀 따라와",
     "expected_action": "follow", "kind": "synthetic"},
    {"wav": "synthetic-negative.wav", "text": "그 못은 뽑지 마",
     "expected_action": "cancel", "kind": "synthetic"},
]

# 지표는 stt_metrics로 일원화한다. 엔진별 러너가 같은 잣대를 쓰게 하기 위함이다.
from stt_metrics import tight, cer, similarity, keyword_report, VOCAB, NEGATION  # noqa: E402

GAME_PROMPT = ("우주선 정비 협동 게임의 한국어 음성 명령. "
               "고정못, 나사, 핀, 몽둥이, 방망이, 배트, 용접건, 플라즈마 총, 도구, 운석, 연습용 공.")

# current는 app.py transcribe()의 현재 서버 설정과 같아야 하는 기준선이다.
PRESETS = {
    "current":      {"beam_size": 1, "vad_filter": True,  "initial_prompt": None},
    "beam5":        {"beam_size": 5, "vad_filter": True,  "initial_prompt": None},
    "prompt":       {"beam_size": 1, "vad_filter": True,  "initial_prompt": GAME_PROMPT},
    "beam5_prompt": {"beam_size": 5, "vad_filter": True,  "initial_prompt": GAME_PROMPT},
    "novad":        {"beam_size": 1, "vad_filter": False, "initial_prompt": None},
}

def load_cases(path):
    if not path.exists():
        raise SystemExit(f"케이스 파일이 없습니다: {path}\n"
                         "--init-synthetic 으로 점검용 시드를 만들거나, "
                         "녹음 파일과 정답 대본을 이 파일에 한 줄씩 추가하세요.")
    rows = []
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            rows.append(json.loads(line))
    return rows


def init_synthetic(path):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        raise SystemExit(f"이미 있습니다: {path} (덮어쓰지 않습니다)")
    path.write_text("".join(json.dumps(r, ensure_ascii=False) + "\n" for r in SYNTHETIC_SEED),
                    encoding="utf-8")
    print(f"점검용 합성 케이스 {len(SYNTHETIC_SEED)}개를 만들었습니다: {path}")


def main():
    parser = argparse.ArgumentParser(description="Whisper 단독 인식 정확도 측정")
    parser.add_argument("--cases", default=str(CASES_PATH))
    parser.add_argument("--presets", default="current",
                        help="쉼표로 구분. all 이면 전부. 사용 가능: " + ", ".join(PRESETS))
    parser.add_argument("--init-synthetic", action="store_true", help="합성 fixture 시드 생성 후 종료")
    parser.add_argument("--kind", default="", help="특정 kind만 평가 (예: read)")
    parser.add_argument("--compute", default="int8",
                        help="CTranslate2 계산 정밀도. int8(기본, 가장 압축) / int8_float32 / float32")
    parser.add_argument("--model", default=DEFAULT_MODEL,
                        help="models/ 아래 Whisper 폴더 이름. 이미 받아둔 것만 쓴다")
    args = parser.parse_args()

    model_dir = ROOT / "models" / args.model
    cases_path = Path(args.cases)
    if args.init_synthetic:
        return init_synthetic(cases_path)

    names = list(PRESETS) if args.presets == "all" else [p.strip() for p in args.presets.split(",")]
    unknown = [n for n in names if n not in PRESETS]
    if unknown:
        raise SystemExit(f"모르는 preset: {unknown}")

    cases = load_cases(cases_path)
    if args.kind:
        cases = [c for c in cases if c.get("kind") == args.kind]
    if not cases:
        raise SystemExit("평가할 케이스가 없습니다.")
    if not (model_dir / "model.bin").exists():
        raise SystemExit(f"로컬 Whisper 모델이 없습니다: {model_dir}. "
                         "새로 내려받지 않습니다. models/ 에 있는 것만 쓸 수 있습니다.")

    from faster_whisper import WhisperModel
    from faster_whisper.audio import decode_audio

    print(f"케이스 {len(cases)}개 · preset {names} · 모델 {model_dir.name} CPU {args.compute} {THREADS}스레드",
          flush=True)
    load_started = time.perf_counter()
    model = WhisperModel(str(model_dir), device="cpu", compute_type=args.compute,
                         cpu_threads=THREADS, num_workers=1, local_files_only=True)
    print(f"모델 로드 {round((time.perf_counter() - load_started) * 1000)}ms\n", flush=True)

    audio_cache = {}
    report = {"generated": datetime.now().isoformat(timespec="seconds"),
              "scope": "STT 단독 인식 정확도. LLM 분류·게임 행동 검사 아님.",
              "model": f"{model_dir.name} / CPU {args.compute}", "threads": THREADS,
              "cases_file": str(cases_path), "presets": {}}

    for name in names:
        cfg = PRESETS[name]
        rows, total_audio, total_ms = [], 0.0, 0.0
        print(f"=== preset {name}  beam={cfg['beam_size']} vad={cfg['vad_filter']} "
              f"prompt={'있음' if cfg['initial_prompt'] else '없음'} ===", flush=True)
        for case in cases:
            wav = cases_path.parent / case["wav"]
            if not wav.exists():
                print(f"  [파일없음] {case['wav']}", flush=True)
                continue
            if wav not in audio_cache:
                audio_cache[wav] = decode_audio(str(wav), sampling_rate=16000)
            audio = audio_cache[wav]
            started = time.perf_counter()
            segments, _info = model.transcribe(
                audio, language="ko", beam_size=cfg["beam_size"], vad_filter=cfg["vad_filter"],
                initial_prompt=cfg["initial_prompt"], condition_on_previous_text=False)
            hyp = " ".join(s.text.strip() for s in segments).strip()
            ms = (time.perf_counter() - started) * 1000
            ref = case["text"]
            kw = keyword_report(ref, hyp)
            row = {"wav": case["wav"], "kind": case.get("kind", "real"),
                   "expected_action": case.get("expected_action"),
                   "ref": ref, "hyp": hyp,
                   "exact": ref.strip() == hyp.strip(), "exact_norm": tight(ref) == tight(hyp),
                   "cer": round(cer(ref, hyp), 4), "asr_ms": round(ms, 1),
                   "audio_seconds": round(len(audio) / 16000, 2), **kw}
            rows.append(row)
            total_audio += row["audio_seconds"]
            total_ms += ms
            mark = "OK" if row["exact_norm"] else ("~ " if row["cer"] <= 0.2 else "X ")
            flip = "  [부정뒤집힘]" if kw["negation_flip"] else ""
            lost = f"  [소실 {','.join(kw['lost'])}]" if kw["lost"] else ""
            print(f"  {mark} CER {row['cer']:.2f}  정답: {ref}\n       인식: {hyp}{lost}{flip}",
                  flush=True)

        n = len(rows)
        ratios = [r["kept_ratio"] for r in rows if r["kept_ratio"] is not None]
        summary = {
            "cases": n,
            "exact_norm": sum(r["exact_norm"] for r in rows),
            "exact_raw": sum(r["exact"] for r in rows),
            "cer_mean": round(sum(r["cer"] for r in rows) / n, 4) if n else None,
            "keyword_kept_mean": round(sum(ratios) / len(ratios), 4) if ratios else None,
            "negation_flips": sum(r["negation_flip"] for r in rows),
            "asr_ms_mean": round(total_ms / n, 1) if n else None,
            "realtime_factor": round((total_ms / 1000) / total_audio, 2) if total_audio else None,
        }
        report["presets"][name] = {
            "config": {k: (bool(v) if k == "initial_prompt" else v) for k, v in cfg.items()},
            "summary": summary, "rows": rows}
        print(f"  -> 완전일치 {summary['exact_norm']}/{n} · CER평균 {summary['cer_mean']} · "
              f"키워드보존 {summary['keyword_kept_mean']} · 부정뒤집힘 {summary['negation_flips']} · "
              f"평균 {summary['asr_ms_mean']}ms (실시간계수 {summary['realtime_factor']})\n", flush=True)

    out = ROOT / "results" / f"stt-eval-{model_dir.name}-{args.compute}-{datetime.now():%Y%m%d-%H%M%S}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("표 요약")
    print(f"  {'preset':<14}{'일치':>9}{'CER':>9}{'키워드':>9}{'부정뒤집':>10}{'평균ms':>10}")
    for name in names:
        s = report["presets"][name]["summary"]
        print(f"  {name:<14}{str(s['exact_norm']) + '/' + str(s['cases']):>9}{s['cer_mean']:>9}"
              f"{str(s['keyword_kept_mean']):>9}{s['negation_flips']:>10}{s['asr_ms_mean']:>10}")
    print(f"\n저장: {out}")


if __name__ == "__main__":
    main()
