"""녹음 파일을 STT 평가 케이스(fixtures/stt_cases.jsonl)로 등록한다. 인식·분류를 수행하지 않는다.

모드:
  list          현재 등록 상태와 미등록 음성 파일을 보여준다.
  align         캡처와 대본을 자동 정렬해 사람이 검토할 TSV를 만든다 (녹음이 빠져도 안전).
  script-order  캡처를 eval_stt_script.json 대본 순서대로 매핑한다 (빠짐이 없을 때만).
  by-name       fixtures/의 <대본id>.<확장자> 파일을 그 대본 문장에 매핑한다.
  make-tsv      정답 대본을 사람이 적어 넣을 TSV를 만든다 (자유 발화 캡처용).
  apply-tsv     사람이 채운 TSV를 stt_cases.jsonl에 반영한다.

정답 대본은 사람이 확정한다. Whisper가 들은 문장을 정답으로 그대로 승격하지 않는다.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from engine import ROOT

FIXTURES = ROOT / "fixtures"
CASES_PATH = FIXTURES / "stt_cases.jsonl"
CAPTURE_DIR = FIXTURES / "captures"
SCRIPT_PATH = ROOT / "eval_stt_script.json"
TSV_PATH = FIXTURES / "stt_transcripts.tsv"
AUDIO_EXT = {".wav", ".m4a", ".mp3", ".webm", ".ogg", ".flac"}
TSV_HEADER = ["wav", "whisper_초안_참고용", "실제로_말한_내용", "대본id"]
# 이 점수 아래로는 짝짓지 않고 건너뛴다. 녹음이 빠지거나 대본 밖의 말이 섞여도 밀리지 않게 한다.
MATCH_FLOOR = 0.25


def rel(path):
    """stt_cases.jsonl의 wav 값은 fixtures/ 기준 상대 경로다."""
    return path.relative_to(FIXTURES).as_posix()


def load_cases():
    if not CASES_PATH.exists():
        return []
    rows = []
    for line in CASES_PATH.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            rows.append(json.loads(line))
    return rows


def write_cases(rows):
    CASES_PATH.parent.mkdir(parents=True, exist_ok=True)
    CASES_PATH.write_text("".join(json.dumps(r, ensure_ascii=False) + "\n" for r in rows),
                          encoding="utf-8")


def merge(existing, new_rows):
    """같은 wav가 이미 있으면 기존 정답 대본을 유지한다. 사람이 적은 값을 덮어쓰지 않는다."""
    index = {r["wav"]: r for r in existing}
    added = kept = 0
    for row in new_rows:
        if row["wav"] in index:
            kept += 1
            continue
        existing.append(row)
        index[row["wav"]] = row
        added += 1
    return added, kept


def audio_files(folder):
    if not folder.exists():
        return []
    return sorted(p for p in folder.iterdir() if p.suffix.lower() in AUDIO_EXT)


SCRIPT_OVERRIDE = None


def load_script():
    path = Path(SCRIPT_OVERRIDE) if SCRIPT_OVERRIDE else SCRIPT_PATH
    if not path.exists():
        raise SystemExit(f"대본이 없습니다: {path}")
    return json.loads(path.read_text(encoding="utf-8"))["lines"]


def sidecar_text(wav):
    """서버 캡처가 남긴 Whisper 초안. 참고용이며 정답이 아니다."""
    path = wav.with_suffix(".json")
    if not path.exists():
        return ""
    try:
        return json.loads(path.read_text(encoding="utf-8")).get("stt_text", "")
    except (json.JSONDecodeError, OSError):
        return ""


def cmd_list(_args):
    cases = load_cases()
    registered = {r["wav"] for r in cases}
    filled = sum(1 for r in cases if r.get("text", "").strip())
    print(f"등록된 케이스 {len(cases)}개 (정답 대본 채워짐 {filled}개)")
    for kind in sorted({r.get("kind", "real") for r in cases}):
        print(f"  kind={kind}: {sum(1 for r in cases if r.get('kind', 'real') == kind)}개")
    loose = [p for p in audio_files(FIXTURES) + audio_files(CAPTURE_DIR)
             if rel(p) not in registered]
    print(f"\n미등록 음성 파일 {len(loose)}개")
    for path in loose[:40]:
        print(f"  {rel(path)}   초안: {sidecar_text(path) or '(없음)'}")
    if len(loose) > 40:
        print(f"  ... 외 {len(loose) - 40}개")
    print(f"\n대본: {SCRIPT_PATH.name} · 케이스: {CASES_PATH}")


def cmd_script_order(args):
    """낭독 세트: 캡처를 녹음 시각 순서로 대본 문장에 1:1 매핑한다."""
    lines = load_script()
    cases = load_cases()
    registered = {r["wav"] for r in cases}
    files = [p for p in audio_files(CAPTURE_DIR) if rel(p) not in registered]
    if not files:
        raise SystemExit("매핑할 미등록 캡처가 없습니다. 먼저 캡처를 켜고 대본을 읽어 주세요.")
    start = args.start
    usable = lines[start:start + len(files)]
    if len(files) != len(usable):
        print(f"주의: 캡처 {len(files)}개 · 남은 대본 {len(usable)}개. 적은 쪽까지만 매핑합니다.")
    pairs = list(zip(files, usable))
    print("아래 매핑을 확인하세요. 순서가 어긋나면 --start로 조정하거나 잘못된 캡처를 지우세요.\n")
    for path, line in pairs:
        print(f"  {rel(path):<40} <- [{line['id']}] {line['text']}")
        print(f"  {'':40}    Whisper 초안: {sidecar_text(path) or '(없음)'}")
    if not args.apply:
        print("\n확인 후 --apply 를 붙여 다시 실행하면 등록합니다.")
        return
    added, kept = merge(cases, [{"wav": rel(p), "text": ln["text"],
                                 "expected_action": ln.get("expected_action"),
                                 "kind": args.kind or "read", "script_id": ln["id"]}
                                for p, ln in pairs])
    write_cases(cases)
    print(f"\n등록 {added}개 · 기존 유지 {kept}개 -> {CASES_PATH}")


def cmd_by_name(args):
    """fixtures/pin01.m4a 처럼 대본 id로 이름 붙인 파일을 매핑한다."""
    lines = {ln["id"]: ln for ln in load_script()}
    cases = load_cases()
    registered = {r["wav"] for r in cases}
    new_rows, unknown = [], []
    for path in audio_files(FIXTURES) + audio_files(CAPTURE_DIR):
        if rel(path) in registered:
            continue
        line = lines.get(path.stem)
        if line is None:
            unknown.append(path)
            continue
        new_rows.append({"wav": rel(path), "text": line["text"],
                         "expected_action": line.get("expected_action"),
                         "kind": args.kind or "read", "script_id": line["id"]})
    for row in new_rows:
        print(f"  {row['wav']:<40} <- [{row['script_id']}] {row['text']}")
    if unknown:
        print(f"\n대본 id와 이름이 맞지 않아 건너뛴 파일 {len(unknown)}개:")
        for path in unknown[:20]:
            print(f"  {rel(path)}")
        print("  (자유 발화라면 make-tsv 로 정답 대본을 적어 주세요.)")
    if not new_rows:
        return
    if not args.apply:
        print("\n확인 후 --apply 를 붙여 다시 실행하면 등록합니다.")
        return
    added, kept = merge(cases, new_rows)
    write_cases(cases)
    print(f"\n등록 {added}개 · 기존 유지 {kept}개 -> {CASES_PATH}")


def align_captures(files, lines, floor=MATCH_FLOOR):
    """녹음 순서를 유지한 채 대본과 짝짓는다. 양쪽 모두 건너뛸 수 있다.

    Whisper 초안과 대본 문장의 문자 유사도를 쓴다. 초안은 정답이 아니라 순서를 찾는 단서일 뿐이며,
    짝지어진 정답 대본은 언제나 사람이 최종 확인한다.
    """
    from eval_stt import similarity

    drafts = [sidecar_text(p) for p in files]
    n, m = len(files), len(lines)
    score = [[0.0] * (m + 1) for _ in range(n + 1)]
    back = [[None] * (m + 1) for _ in range(n + 1)]
    for i in range(1, n + 1):
        for j in range(1, m + 1):
            gain = similarity(drafts[i - 1], lines[j - 1]["text"]) - floor
            options = [(score[i - 1][j - 1] + gain, "match"),
                       (score[i - 1][j], "skip_capture"),
                       (score[i][j - 1], "skip_line")]
            score[i][j], back[i][j] = max(options)
    for i in range(1, n + 1):
        score[i][0], back[i][0] = score[i - 1][0], "skip_capture"
    for j in range(1, m + 1):
        score[0][j], back[0][j] = score[0][j - 1], "skip_line"

    pairs, i, j = [], n, m
    while i > 0 or j > 0:
        move = back[i][j]
        if move == "match":
            pairs.append((files[i - 1], lines[j - 1],
                          similarity(drafts[i - 1], lines[j - 1]["text"])))
            i, j = i - 1, j - 1
        elif move == "skip_capture":
            pairs.append((files[i - 1], None, 0.0))
            i -= 1
        else:
            pairs.append((None, lines[j - 1], 0.0))
            j -= 1
    return list(reversed(pairs))


def cmd_align(args):
    """캡처와 대본을 자동 정렬해 검토용 TSV를 만든다. 등록은 apply-tsv가 한다."""
    lines = load_script()
    cases = load_cases()
    registered = {r["wav"] for r in cases}
    files = [p for p in audio_files(CAPTURE_DIR) if rel(p) not in registered]
    if not files:
        raise SystemExit("정렬할 미등록 캡처가 없습니다.")

    pairs = align_captures(files, lines, args.floor)
    rows, missing, extra, weak = [], [], [], 0
    print(f"캡처 {len(files)}개 · 대본 {len(lines)}문장 자동 정렬\n")
    for path, line, sim in pairs:
        if path is None:
            missing.append(line)
            print(f"  [녹음 없음]  [{line['id']}] {line['text']}")
            continue
        draft = sidecar_text(path) or "(없음)"
        if line is None:
            extra.append(path)
            rows.append({"wav": rel(path), "draft": draft, "text": "", "script_id": ""})
            print(f"  [대본 밖]    {path.name}  초안: {draft}")
            continue
        flag = ""
        if sim < 0.45:
            weak += 1
            flag = "   <-- 확인 필요"
        rows.append({"wav": rel(path), "draft": draft, "text": line["text"],
                     "script_id": line["id"]})
        print(f"  {sim:.2f} [{line['id']}] {line['text']}{flag}\n"
              f"       {path.name}  초안: {draft}")

    body = ["# 자동 정렬 결과입니다. 3번째 칸이 '실제로 말한 내용'으로 맞는지 확인하고 틀리면 고치세요.",
            "# 2번째 칸은 Whisper 초안이며 정답이 아닙니다. 3번째 칸을 비우면 그 줄은 등록되지 않습니다.",
            "# 대본 밖의 말(예: 수고하셨습니다)은 3번째 칸을 채우면 자유 발화로 등록됩니다.",
            "\t".join(TSV_HEADER)]
    body += [f"{r['wav']}\t{r['draft']}\t{r['text']}\t{r['script_id']}" for r in rows]
    TSV_PATH.write_text("\n".join(body) + "\n", encoding="utf-8")

    print(f"\n짝지어짐 {len(rows) - len(extra)}개 · 대본 밖 캡처 {len(extra)}개 · "
          f"녹음 없는 대본 {len(missing)}개 · 유사도 낮음 {weak}개")
    if missing:
        print("다시 읽어야 할 대본: " + ", ".join(f"{ln['id']}" for ln in missing))
    print(f"\n검토 파일: {TSV_PATH}\n확인·수정 후: eval_stt_register.py apply-tsv --apply")


def cmd_make_tsv(_args):
    """자유 발화 캡처용. 3번째 칸에 실제로 말한 내용을 적는다."""
    cases = load_cases()
    registered = {r["wav"] for r in cases}
    loose = [p for p in audio_files(FIXTURES) + audio_files(CAPTURE_DIR)
             if rel(p) not in registered]
    if not loose:
        raise SystemExit("정답 대본이 필요한 미등록 파일이 없습니다.")
    body = ["# 3번째 칸에 '실제로 말한 내용'을 적으세요. 2번째 칸은 Whisper 초안이며 정답이 아닙니다.",
            "# 초안이 맞으면 그대로 복사해도 됩니다. 비워 두면 그 줄은 등록되지 않습니다.",
            "\t".join(TSV_HEADER)]
    body += [f"{rel(p)}\t{sidecar_text(p)}\t\t" for p in loose]
    TSV_PATH.write_text("\n".join(body) + "\n", encoding="utf-8")
    print(f"{len(loose)}줄을 만들었습니다: {TSV_PATH}\n"
          "메모장이나 엑셀로 열어 3번째 칸을 채운 뒤 apply-tsv 를 실행하세요.")


def cmd_apply_tsv(args):
    if not TSV_PATH.exists():
        raise SystemExit(f"TSV가 없습니다: {TSV_PATH} (먼저 make-tsv 를 실행하세요)")
    cases = load_cases()
    script = {ln["id"]: ln for ln in load_script()}
    new_rows, blank = [], 0
    for line in TSV_PATH.read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#") or line.startswith(TSV_HEADER[0] + "\t"):
            continue
        parts = line.split("\t")
        wav, truth = parts[0].strip(), (parts[2].strip() if len(parts) > 2 else "")
        script_id = parts[3].strip() if len(parts) > 3 else ""
        if not truth:
            blank += 1
            continue
        found = script.get(script_id)
        new_rows.append({"wav": wav, "text": truth,
                         "expected_action": (found or {}).get("expected_action"),
                         "kind": args.kind or ("read" if found else "free"),
                         **({"script_id": script_id} if found else {})})
    print(f"정답 대본이 채워진 줄 {len(new_rows)}개 · 비어 있어 건너뛴 줄 {blank}개")
    for row in new_rows[:20]:
        print(f"  {row['wav']:<40} <- {row['text']}")
    if not new_rows:
        return
    if not args.apply:
        print("\n확인 후 --apply 를 붙여 다시 실행하면 등록합니다.")
        return
    added, kept = merge(cases, new_rows)
    write_cases(cases)
    print(f"\n등록 {added}개 · 기존 유지 {kept}개 -> {CASES_PATH}")


COMMANDS = {"list": cmd_list, "align": cmd_align, "script-order": cmd_script_order,
            "by-name": cmd_by_name, "make-tsv": cmd_make_tsv, "apply-tsv": cmd_apply_tsv}


def main():
    parser = argparse.ArgumentParser(description="녹음 파일을 STT 평가 케이스로 등록")
    parser.add_argument("mode", choices=list(COMMANDS))
    parser.add_argument("--apply", action="store_true", help="실제로 파일에 기록한다 (기본은 미리보기)")
    parser.add_argument("--start", type=int, default=0, help="script-order에서 시작할 대본 순번")
    parser.add_argument("--kind", default="", help="등록할 kind 이름 (기본: read/free 자동)")
    parser.add_argument("--script", default="", help="다른 대본 파일 (예: eval_word_audition.json)")
    parser.add_argument("--floor", type=float, default=MATCH_FLOOR,
                        help="align에서 짝지을 최소 유사도. 낮출수록 순서를 더 믿는다")
    args = parser.parse_args()
    if args.script:
        global SCRIPT_OVERRIDE
        SCRIPT_OVERRIDE = args.script
    COMMANDS[args.mode](args)


if __name__ == "__main__":
    main()
