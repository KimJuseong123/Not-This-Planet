"""STT 평가 공통 지표와 결과 형식. 표준 라이브러리만 쓴다.

서버 .venv와 실험용 .venv-eval 양쪽에서 import되므로 httpx/psutil 같은 서버 의존성을 넣지 않는다.
어느 엔진(Whisper / Vosk / wav2vec2)으로 인식했든 같은 잣대로 비교하기 위한 모듈이다.
"""
from __future__ import annotations

import json
import re
import unicodedata
from datetime import datetime
from pathlib import Path

# 행동을 가르는 단어. 이 단어가 깨지면 LLM이 아무리 정확해도 행동이 틀어진다.
VOCAB = [
    "고정못", "나사못", "나사", "고정핀", "핀", "못",
    "몽둥이", "방망이", "배트", "용접건", "용접기", "용접", "플라즈마", "총", "도구",
    "운석", "공", "파츠",
    "따라", "기다려", "대기", "가만히", "멈춰", "그만", "취소",
    "뽑", "빼", "제거", "주워", "집어", "가져", "내려놔", "놓", "때려", "쳐내", "막아", "던져",
    "전부", "모두", "하나", "근처", "주변", "가까운",
]
# 부정 표지. '뽑아줘'와 '뽑지 마'를 뒤집으면 가장 치명적인 오류다.
NEGATION = ["지마", "하지마", "말고", "마세요", "그만", "멈춰", "취소", "아니"]

PUNCT = re.compile(r"[\s.,!?~…·\"'’“”\-—()\[\]]+")


def tight(text):
    """비교용 정규화: NFC + 공백/문장부호 제거. 원문 그대로의 일치도 따로 함께 보고한다."""
    return PUNCT.sub("", unicodedata.normalize("NFC", text or ""))


def edit_distance(a, b):
    prev = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        cur = [i]
        for j, y in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (x != y)))
        prev = cur
    return prev[-1]


def cer(ref, hyp):
    """문자 오류율. 편집거리 / 정답 길이. 삽입이 많으면 1.0을 넘을 수 있다.

    참고: 정답이 2~3글자인 짧은 명령에서는 분모가 작아 환각 한 번에 값이 크게 튄다.
    짧은 명령 비교에는 keyword_report와 최종 행동 정확도를 함께 본다.
    """
    ref, hyp = tight(ref), tight(hyp)
    if not ref:
        return 0.0 if not hyp else 1.0
    return edit_distance(ref, hyp) / len(ref)


def similarity(a, b):
    """0~1 문자 유사도. 길이 차이에 대칭이라 정렬 후보 점수로 쓴다."""
    a_t, b_t = tight(a), tight(b)
    if not a_t and not b_t:
        return 1.0
    longest = max(len(a_t), len(b_t))
    return max(0.0, 1.0 - edit_distance(a_t, b_t) / longest) if longest else 0.0


def keyword_report(ref, hyp):
    """정답에 있던 결정적 단어 중 인식 결과에도 남은 비율."""
    ref_t, hyp_t = tight(ref), tight(hyp)
    wanted = [w for w in VOCAB if w in ref_t]
    kept = [w for w in wanted if w in hyp_t]
    ref_neg = any(n in ref_t for n in NEGATION)
    hyp_neg = any(n in hyp_t for n in NEGATION)
    return {"wanted": wanted, "lost": [w for w in wanted if w not in hyp_t],
            "kept_ratio": (len(kept) / len(wanted)) if wanted else None,
            "negation_ref": ref_neg, "negation_hyp": hyp_neg,
            "negation_flip": ref_neg != hyp_neg}


def build_row(case, hyp, ms, audio_seconds):
    ref = case["text"]
    return {"wav": case["wav"], "kind": case.get("kind", "read"),
            "expected_action": case.get("expected_action"),
            "ref": ref, "hyp": hyp,
            "exact": ref.strip() == hyp.strip(), "exact_norm": tight(ref) == tight(hyp),
            "cer": round(cer(ref, hyp), 4), "asr_ms": round(ms, 1),
            "audio_seconds": round(audio_seconds, 2), **keyword_report(ref, hyp)}


def summarize(rows):
    n = len(rows)
    if not n:
        return {"cases": 0}
    ratios = [r["kept_ratio"] for r in rows if r["kept_ratio"] is not None]
    total_audio = sum(r["audio_seconds"] for r in rows)
    total_ms = sum(r["asr_ms"] for r in rows)
    return {"cases": n,
            "exact_norm": sum(r["exact_norm"] for r in rows),
            "exact_raw": sum(r["exact"] for r in rows),
            "cer_mean": round(sum(r["cer"] for r in rows) / n, 4),
            "keyword_kept_mean": round(sum(ratios) / len(ratios), 4) if ratios else None,
            "negation_flips": sum(r["negation_flip"] for r in rows),
            "asr_ms_mean": round(total_ms / n, 1),
            "realtime_factor": round((total_ms / 1000) / total_audio, 2) if total_audio else None}


def print_row(row):
    mark = "OK" if row["exact_norm"] else ("~ " if row["cer"] <= 0.2 else "X ")
    lost = f"  [소실 {','.join(row['lost'])}]" if row["lost"] else ""
    flip = "  [부정뒤집힘]" if row["negation_flip"] else ""
    print(f"  {mark} CER {row['cer']:.2f}  정답: {row['ref']}\n"
          f"       인식: {row['hyp']}{lost}{flip}", flush=True)


def print_summary(name, summary):
    print(f"  -> 완전일치 {summary['exact_norm']}/{summary['cases']} · "
          f"CER평균 {summary['cer_mean']} · 키워드보존 {summary['keyword_kept_mean']} · "
          f"부정뒤집힘 {summary['negation_flips']} · 평균 {summary['asr_ms_mean']}ms "
          f"(실시간계수 {summary['realtime_factor']})\n", flush=True)


def load_cases(path, kind=""):
    path = Path(path)
    if not path.exists():
        raise SystemExit(f"케이스 파일이 없습니다: {path}")
    rows = []
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            row = json.loads(line)
            if not kind or row.get("kind") == kind:
                rows.append(row)
    return rows


def save_report(results_dir, tag, model_label, cases_file, presets):
    """eval_llm.py --stt-result 가 읽는 것과 같은 형식으로 저장한다."""
    out = Path(results_dir) / f"stt-eval-{tag}-{datetime.now():%Y%m%d-%H%M%S}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(
        {"generated": datetime.now().isoformat(timespec="seconds"),
         "scope": "STT 단독 인식 정확도. LLM 분류·게임 행동 검사 아님.",
         "model": model_label, "cases_file": str(cases_file), "presets": presets},
        ensure_ascii=False, indent=2), encoding="utf-8")
    return out


def print_table(presets, names):
    print("표 요약")
    print(f"  {'preset':<16}{'일치':>9}{'CER':>9}{'키워드':>9}{'부정뒤집':>10}{'평균ms':>10}")
    for name in names:
        s = presets[name]["summary"]
        print(f"  {name:<16}{str(s['exact_norm']) + '/' + str(s['cases']):>9}{s['cer_mean']:>9}"
              f"{str(s['keyword_kept_mean']):>9}{s['negation_flips']:>10}{s['asr_ms_mean']:>10}")
