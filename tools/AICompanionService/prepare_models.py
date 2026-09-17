"""무료로 공개된 모델 파일만 내려받는다. 유료 API, 계정, 결제를 쓰지 않는다.

서버가 실제로 쓰는 모델만 받는다. app.py 의 ASR_MODEL 기본값과 어긋나면
서버가 "음성 모델이 없습니다" 로 멈추므로, 이 목록이 그 기본값과 같아야 한다.
"""
import os
import subprocess
from pathlib import Path
import httpx
from huggingface_hub import snapshot_download

ROOT = Path(__file__).resolve().parent

# 서버 기본값(app.py ASR_MODEL)과 같아야 한다. 한국어 파인튜닝 모델이 기본이다 —
# 측정에서 whisper-base 대비 CER 0.86 -> 0.61 로 내려갔고 속도는 비슷하다.
WHISPER_MODELS = {
    "whisper-small-ko-zeroth": "ghost613/faster-whisper-small_korean-zeroth",
    # 비교용 기준선. 더 작고 빠르지만 한국어 인식률이 낮다.
    "whisper-base": "Systran/faster-whisper-base",
}
WHISPER_FILES = ["config.json", "model.bin", "tokenizer.json", "vocabulary.*",
                 "preprocessor_config.json"]

if __name__ == "__main__":
    from engine import OLLAMA, MODELS
    print("먼저 START.bat 으로 서버를 띄운 뒤 이 스크립트를 실행하세요.", flush=True)
    with httpx.Client(timeout=1800, trust_env=False) as client:
        client.get(OLLAMA + "/api/version").raise_for_status()
        for model in MODELS:
            print(f"{model} 내려받는 중 (전용 모델 폴더)...", flush=True)
            response = client.post(OLLAMA + "/api/pull", json={"model": model, "stream": False})
            response.raise_for_status()
            if "error" in response.json():
                raise RuntimeError(response.json()["error"])
            print(model, response.json(), flush=True)

    os.environ["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1"
    for folder, repo in WHISPER_MODELS.items():
        target = ROOT / "models" / folder
        if (target / "model.bin").exists():
            print(f"{folder} 는 이미 있습니다. 건너뜁니다.", flush=True)
            continue
        print(f"{folder} 내려받는 중 ({repo})...", flush=True)
        snapshot_download(repo, local_dir=target, allow_patterns=WHISPER_FILES)

    print("준비 끝. 모든 추론이 이 PC 안에서 돕니다.", flush=True)
