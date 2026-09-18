import json
import math
import platform
import time
from pathlib import Path
import httpx
from engine import ROOT, MODELS, PROMPT_VERSION

def percentile(values, p):
    values = sorted(values)
    return round(values[max(0, math.ceil(len(values) * p) - 1)], 1) if values else 0

def summarize(rows):
    out = []
    for model in MODELS:
        selected = [r for r in rows if r["model"] == model]
        if not selected:
            continue
        latencies = [r["resources"]["wall_ms"] for r in selected if "resources" in r]
        out.append({"model": model, "total": len(selected), "correct": sum(r.get("correct", False) for r in selected),
                    "errors": sum("error" in r for r in selected),
                    "accuracy_percent": round(sum(r.get("correct", False) for r in selected) / len(selected) * 100, 1),
                    "p50_ms": percentile(latencies, .5), "p95_ms": percentile(latencies, .95),
                    "peak_rss_mb": max([r["resources"]["process_tree_peak_rss_mb"] for r in selected if "resources" in r] or [0]),
                    "mismatches": [{"text": r["text"], "expected": r["expected"], "actual": r.get("action"), "error": r.get("error")} for r in selected if not r.get("correct")]})
    return out

def run(call, progress=lambda *args: None):
    cases = json.loads((ROOT / "eval_cases.json").read_text(encoding="utf-8"))
    rows, warmups = [], []
    total = len(cases) * len(MODELS)
    for model in MODELS:
        progress(len(rows), total, model + " 초기 로딩")
        warmups.append(call({"text": "내 쪽으로 와줘", "model": model}))
        for case in cases:
            try:
                row = call({**case, "model": model})
            except Exception as e:
                row = {**case, "model": model, "error": str(e), "correct": False}
            rows.append(row)
            progress(len(rows), total, model)
    result = {"timestamp": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "prompt_version": PROMPT_VERSION,
              "evaluation": "Authored synthetic text set; not held-out human speech; sequential warm requests",
              "platform": platform.platform(), "cpu": platform.processor(), "warmups": warmups,
              "summary": summarize(rows), "rows": rows}
    folder = ROOT / "results"
    folder.mkdir(exist_ok=True)
    path = folder / ("benchmark-" + time.strftime("%Y%m%d-%H%M%S") + ".json")
    path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    return result

if __name__ == "__main__":
    def call(data):
        with httpx.Client(timeout=180, trust_env=False) as client:
            r = client.post("http://127.0.0.1:8765/api/classify", json=data)
            r.raise_for_status()
            return r.json()
    result = run(call, lambda n, total, current: print(f"{n}/{total} {current}", flush=True))
    print(json.dumps(result["summary"], ensure_ascii=False, indent=2))
