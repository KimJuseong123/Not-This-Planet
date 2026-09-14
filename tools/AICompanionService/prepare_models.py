"""Download free model files only. No inference API, account, or payment."""
import os
import subprocess
from pathlib import Path
import httpx
from huggingface_hub import snapshot_download

ROOT = Path(__file__).resolve().parent
if __name__ == "__main__":
    from engine import OLLAMA, MODELS
    print("First start app.py, then run this preparation script.", flush=True)
    with httpx.Client(timeout=1800, trust_env=False) as client:
        client.get(OLLAMA + "/api/version").raise_for_status()
        for model in MODELS:
            print("Downloading", model, "to the private lab model directory...", flush=True)
            response = client.post(OLLAMA + "/api/pull", json={"model": model, "stream": False})
            response.raise_for_status()
            if "error" in response.json():
                raise RuntimeError(response.json()["error"])
            print(model, response.json(), flush=True)
    print("Downloading multilingual Whisper base (CPU speech recognition)...", flush=True)
    os.environ["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1"
    snapshot_download("Systran/faster-whisper-base", local_dir=ROOT / "models" / "whisper-base",
                      allow_patterns=["config.json", "model.bin", "tokenizer.json", "vocabulary.*", "preprocessor_config.json"])
    print("Ready. All inference now works locally.", flush=True)
