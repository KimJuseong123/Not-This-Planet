"""녹음이 서버에 도착했는지 실시간으로 알려준다. 인식 정확도를 판정하지 않는다.

대본을 읽는 동안 옆 터미널에 띄워 둔다. 새 캡처가 생기면 바로 한 줄로 알려주므로,
도착하지 않은 문장을 그 자리에서 다시 읽을 수 있다.

재녹음 기준은 '도착했는가'이지 'Whisper가 맞췄는가'가 아니다.
초안이 엉뚱해도 그대로 둔다. 잘 인식된 발화만 남기면 정확도가 부풀려져 측정이 망가진다.
"""
from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

from engine import ROOT

CAPTURE_DIR = ROOT / "fixtures" / "captures"
SCRIPT_PATH = ROOT / "eval_stt_script.json"
TSV_PATH = ROOT / "fixtures" / "stt_transcripts.tsv"
TSV_HEADER = ["wav", "whisper_초안_참고용", "실제로_말한_내용", "대본id"]
SHORT_SECONDS = 0.8


def load_script(path=None):
    return json.loads(Path(path or SCRIPT_PATH).read_text(encoding="utf-8"))["lines"]


def read_sidecar(wav, timeout=3.0):
    """곁파일은 WAV보다 조금 늦게 쓰일 수 있다."""
    path = wav.with_suffix(".json")
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if path.exists():
            try:
                return json.loads(path.read_text(encoding="utf-8"))
            except (json.JSONDecodeError, OSError):
                pass
        time.sleep(0.15)
    return {}


def write_tsv(rows):
    body = ["# watch_captures.py가 실시간으로 쓴 매핑입니다. 3번째 칸이 맞는지 확인하고 틀리면 고치세요.",
            "# 2번째 칸은 Whisper 초안이며 정답이 아닙니다. 비우면 그 줄은 등록되지 않습니다.",
            "\t".join(TSV_HEADER)]
    body += [f"{r['wav']}\t{r['draft']}\t{r['text']}\t{r['script_id']}" for r in rows]
    TSV_PATH.write_text("\n".join(body) + "\n", encoding="utf-8")


def describe(index, total, line):
    tag = f" ({line['group']}/{line['target']})" if "target" in line else ""
    return f"[{index + 1}/{total}] {line['id']}{tag}  {line['text']}"


def main():
    parser = argparse.ArgumentParser(description="캡처 도착을 실시간으로 확인")
    parser.add_argument("--start", type=int, default=0, help="시작할 대본 순번 (0부터)")
    parser.add_argument("--interval", type=float, default=0.4)
    parser.add_argument("--ids", default="", help="쉼표로 구분한 대본 id만 사용 (예: pin01,move01)")
    parser.add_argument("--script", default="", help="다른 대본 파일 (예: eval_word_audition.json)")
    args = parser.parse_args()

    lines = load_script(args.script or None)
    if args.ids:
        wanted = [i.strip() for i in args.ids.split(",")]
        index = {ln["id"]: ln for ln in lines}
        missing = [i for i in wanted if i not in index]
        if missing:
            raise SystemExit(f"대본에 없는 id: {missing}")
        lines = [index[i] for i in wanted]
    total = len(lines)
    CAPTURE_DIR.mkdir(parents=True, exist_ok=True)
    if not (CAPTURE_DIR / "ENABLED").exists():
        print("주의: fixtures/captures/ENABLED 가 없어 서버가 음성을 보관하지 않습니다.")
        print("      켜려면: Set-Content fixtures\\captures\\ENABLED 'on'\n")

    seen = {p.name for p in CAPTURE_DIR.glob("*.wav")}
    print(f"이미 있는 캡처 {len(seen)}개는 건너뜁니다. 새로 녹음하는 것만 봅니다.")
    print(f"대본 {total}문장 · {args.start + 1}번째부터 시작\n")
    print("=" * 62)
    print(f"지금 읽을 문장 → {describe(args.start, total, lines[args.start])}")
    print("=" * 62, flush=True)

    from eval_stt import similarity

    rows, position, arrived, skipped_empty = [], args.start, 0, 0
    try:
        while position < total:
            fresh = sorted(p for p in CAPTURE_DIR.glob("*.wav") if p.name not in seen)
            for wav in fresh:
                seen.add(wav.name)
                meta = read_sidecar(wav)
                draft = (meta.get("stt_text") or "").strip()
                seconds = meta.get("asr", {}).get("audio_seconds")
                line = lines[position]

                if not draft:
                    skipped_empty += 1
                    print(f"\n  X  도착했지만 인식 결과가 비어 있습니다 "
                          f"({seconds if seconds is not None else '?'}초)")
                    print(f"     {wav.name} — 자동 제외했습니다.")
                    print(f"  >> 같은 문장을 다시 읽어 주세요: {describe(position, total, line)}",
                          flush=True)
                    continue

                arrived += 1
                mark = "OK "
                notes = []
                if seconds is not None and seconds < SHORT_SECONDS:
                    mark = "!  "
                    notes.append(f"{seconds}초로 짧습니다. V를 조금 더 길게 눌러 보세요")
                if position > args.start:
                    previous = lines[position - 1]
                    if similarity(draft, previous["text"]) > similarity(draft, line["text"]) + 0.15:
                        notes.append(f"앞 문장({previous['id']}) 재녹음으로 보입니다. "
                                     f"맞다면 이 파일을 지우고 이어가세요: {wav.name}")

                print(f"\n  {mark}도착 {arrived}개째 · "
                      f"{seconds if seconds is not None else '?'}초 · {describe(position, total, line)}")
                print(f"     초안: {draft}")
                for note in notes:
                    print(f"     - {note}")

                rows.append({"wav": f"captures/{wav.name}", "draft": draft,
                             "text": line["text"], "script_id": line["id"]})
                write_tsv(rows)
                position += 1
                if position < total:
                    print(f"  >> 다음: {describe(position, total, lines[position])}", flush=True)
            time.sleep(args.interval)
        print("\n대본을 끝까지 마쳤습니다.")
    except KeyboardInterrupt:
        print("\n중단했습니다.")

    print("=" * 62)
    print(f"도착 {arrived}개 · 빈 인식으로 제외 {skipped_empty}개 · "
          f"남은 대본 {max(0, total - position)}문장")
    if rows:
        print(f"매핑 기록: {TSV_PATH}")
        print("확인·수정 후 등록: eval_stt_register.py apply-tsv --apply")
    if position < total:
        print(f"이어서 하려면: watch_captures.py --start {position}")


if __name__ == "__main__":
    main()
