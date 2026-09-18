"""Local synthesized speech integration test, not human accuracy evaluation."""
import io
import json
import wave
import httpx
from engine import ROOT

if __name__ == "__main__":
    rows=[]
    with httpx.Client(timeout=180, trust_env=False) as client:
        for name, expected in [("synthetic-pull-pin.wav","pull_pin"),("synthetic-follow.wav","follow"),("synthetic-negative.wav","cancel")]:
            with (ROOT / "fixtures" / name).open("rb") as f:
                r=client.post("http://127.0.0.1:8765/api/voice",data={"model":"qwen3:1.7b","expected":expected},files={"audio":(name,f,"audio/wav")})
            r.raise_for_status()
            data=r.json(); rows.append(data)
            print(json.dumps({"fixture":name,"text":data.get("text"),"action":data.get("action"),"correct":data.get("correct"),"asr":data.get("asr"),"resources":data.get("resources")},ensure_ascii=False),flush=True)
        silence=io.BytesIO()
        with wave.open(silence,"wb") as wav:
            wav.setnchannels(1);wav.setsampwidth(2);wav.setframerate(16000);wav.writeframes(b"\0\0"*16000)
        r=client.post("http://127.0.0.1:8765/api/voice",data={"model":"qwen3:1.7b"},files={"audio":("silence.wav",silence.getvalue(),"audio/wav")})
        r.raise_for_status()
        assert r.json().get("abstained"), r.text
        print("Silence: abstained without LLM call",flush=True)
        rows.append(r.json())
    (ROOT/"results"/"synthetic-audio-check.json").write_text(json.dumps({"kind":"Windows offline TTS fixtures, NOT user microphone test","rows":rows},ensure_ascii=False,indent=2),encoding="utf-8")
