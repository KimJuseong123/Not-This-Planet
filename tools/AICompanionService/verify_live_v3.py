"""Opt-in local HTTP acceptance test; no microphone capture or paid/cloud calls."""
import io
import json
import time
import wave

import httpx

CASES = [
    ("못 뽑아줘", "pull_pin"), ("못 빼줘", "pull_pin"),
    ("이거 못 빼줘", "pull_pin"), ("못 하나만 빼줄래", "pull_pin"),
    ("주변 못 전부 뽑아줘", "pull_nearby_pins"),
    ("남은 나사 다 제거해", "pull_nearby_pins"),
    ("못들 하나씩 쭉 뽑아줘", "pull_nearby_pins"),
    ("가까운 못으로 가봐", "move_to_pin"),
    ("못 앞으로 가서 기다려", "move_to_pin"),
    ("못 있는 곳으로 가기만 해", "move_to_pin"),
    ("나 따라와", "follow"), ("일로와봐", "follow"),
    ("여기서 기다려", "wait"), ("가만히 있어", "wait"),
    ("못 뽑지 마", "cancel"), ("못 다 빼지 마", "cancel"),
    ("그만해", "cancel"), ("따라오지 마", "cancel"),
    ("용접해줘", "none"), ("그거 해줘", "clarify"),
    # New phrasing, not included verbatim in the prompt.
    ("내 뒤로 따라붙어", "follow"),
    ("잠깐 거기서 대기해", "wait"),
    ("진행 중인 작업 취소해", "cancel"),
    ("근처 고정핀 하나 제거해줄 수 있어?", "pull_pin"),
    ("이 근처에 박힌 나사 전부 빼줄래?", "pull_nearby_pins"),
    ("가장 가까운 고정핀 앞으로 이동해줘", "move_to_pin"),
]


def main():
    rows = []
    with httpx.Client(base_url="http://127.0.0.1:8765", timeout=40, trust_env=False) as client:
        health = client.get("/api/health")
        health.raise_for_status()
        assert health.json()["protocol"] == "companion-v3"
        for text, expected in CASES:
            start = time.perf_counter()
            response = client.post("/api/companion-v3/text", json={"text": text})
            response.raise_for_status()
            result = response.json()
            row = {"text": text, "expected": expected, "actual": result["action"],
                   "ok": result["action"] == expected,
                   "http_ms": round((time.perf_counter() - start) * 1000),
                   "resources": result.get("resources")}
            rows.append(row)
            print(json.dumps(row, ensure_ascii=False), flush=True)
        # Verifies the real WAV -> Whisper -> empty-intent route, NOT spoken-word accuracy.
        wav = io.BytesIO()
        with wave.open(wav, "wb") as output:
            output.setnchannels(1)
            output.setsampwidth(2)
            output.setframerate(16000)
            output.writeframes(b"\0" * 32000)
        response = client.post("/api/companion-v3/voice",
                               files={"audio": ("silence.wav", wav.getvalue(), "audio/wav")})
        response.raise_for_status()
        silent = response.json()
        assert silent["action"] == "none" and not silent["text"].strip(), silent
        print(json.dumps({"silence_voice_route": "passed", "asr": silent.get("asr")}, ensure_ascii=False))
    print(json.dumps({"passed": sum(r["ok"] for r in rows), "total": len(rows),
                      "note": "Development regression set; not a general accuracy benchmark."}))


if __name__ == "__main__":
    main()
