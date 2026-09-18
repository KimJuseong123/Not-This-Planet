from __future__ import annotations

import asyncio
import hashlib
import io
import json
import os
import shutil
import socket
import subprocess
import threading
import time
from contextlib import asynccontextmanager

import httpx
import psutil
from fastapi import FastAPI, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

from engine import ROOT, OLLAMA, MODELS, THREADS, SKILLS, Monitor, classify, sample_resources, append_result

LOCK = threading.Lock()
ASR = None
# 한국어 파인튜닝 모델이 기본. 측정: CER 0.86(whisper-base) -> 0.61, 속도는 비슷하다.
# 다른 모델로 시험하려면 COMPANION_ASR_MODEL 환경변수에 models/ 아래 폴더 이름을 넣는다.
ASR_MODEL = os.environ.get("COMPANION_ASR_MODEL", "whisper-small-ko-zeroth")
ollama_process = None
SERVER = None
SOURCE_HASHES = {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()
                 for name in ("app.py", "companion_v3.py", "companion_v4.py")}
BENCH = {"running": False, "finished": False, "progress": 0, "total": 80, "current": ""}


@asynccontextmanager
async def lifespan(app):
    global ollama_process
    (ROOT / "logs").mkdir(exist_ok=True)
    with socket.socket() as sock:
        if sock.connect_ex(("127.0.0.1", 11439)) == 0:
            raise RuntimeError("테스트 전용 11439 포트가 이미 사용 중입니다. 다른 서버를 덮어쓰지 않습니다.")
    executable = shutil.which("ollama")
    if not executable:
        raise RuntimeError("Ollama 설치가 필요합니다.")
    env = os.environ.copy()
    env.update({"OLLAMA_HOST": "127.0.0.1:11439", "OLLAMA_MODELS": str(ROOT / "models" / "ollama"),
                "OLLAMA_NUM_PARALLEL": "1", "OLLAMA_MAX_LOADED_MODELS": "1", "OLLAMA_CONTEXT_LENGTH": "2048",
                "OLLAMA_NO_CLOUD": "1"})
    with (ROOT / "logs" / "ollama.log").open("a", encoding="utf-8") as log:
        ollama_process = subprocess.Popen([executable, "serve"], env=env, stdout=log, stderr=log,
                                          creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        try:
            async with httpx.AsyncClient(trust_env=False, timeout=1) as client:
                for _ in range(40):
                    if ollama_process.poll() is not None:
                        raise RuntimeError("테스트 Ollama 서버 시작 실패. logs/ollama.log 확인")
                    try:
                        r = await client.get(OLLAMA + "/api/version")
                        r.raise_for_status()
                        break
                    except httpx.HTTPError:
                        await asyncio.sleep(0.25)
                else:
                    raise RuntimeError("Ollama 서버 준비 시간 초과")
            yield
        finally:
            # Only terminate the private Ollama tree created by this app.
            try:
                for child in psutil.Process(ollama_process.pid).children(recursive=True):
                    child.terminate()
            except psutil.Error:
                pass
            ollama_process.terminate()
            try:
                ollama_process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                ollama_process.kill()


app = FastAPI(lifespan=lifespan)


@app.middleware("http")
async def local_only(request: Request, call_next):
    if request.method != "GET":
        origin = request.headers.get("origin")
        if origin and origin not in {"http://127.0.0.1:8765", "http://localhost:8765"}:
            return JSONResponse({"detail": "외부 사이트 요청 차단"}, status_code=403)
    if request.headers.get("host", "").split(":")[0] not in {"127.0.0.1", "localhost"}:
        return JSONResponse({"detail": "로컬 접속만 허용"}, status_code=403)
    return await call_next(request)


@app.get("/")
def index():
    return FileResponse(ROOT / "static" / "index.html")


@app.get("/api/health")
def health():
    # Do not call Ollama or load models here: startup checks must stay cheap.
    from companion_v4 import ACTIONS
    return {"service": "ai-companion-lab", "service_root": str(ROOT),
            "pid": os.getpid(), "protocol": "companion-v4", "actions": ACTIONS,
            "busy": LOCK.locked(), "can_shutdown": SERVER is not None,
            "source_hashes": SOURCE_HASHES}


@app.get("/api/status")
def status():
    with httpx.Client(timeout=3, trust_env=False) as client:
        installed = client.get(OLLAMA + "/api/tags").json().get("models", [])
        running = client.get(OLLAMA + "/api/ps").json().get("models", [])
    return {"models": [{"name": m["name"], "size": m["size"], "digest": m.get("digest"),
                        "quantization": m.get("details", {}).get("quantization_level")} for m in installed],
            "allowed_models": MODELS, "skills": [{"id": k, "label": v[0]} for k, v in SKILLS.items()],
            "resources": sample_resources(), "logical_cpus": os.cpu_count(), "threads": THREADS,
            "asr_model": ASR_MODEL,
            "asr_downloaded": (ROOT / "models" / ASR_MODEL / "model.bin").exists(),
            "asr_loaded": ASR is not None, "busy": LOCK.locked(), "running_models": running,
            "cloud_calls": False, "service_root": str(ROOT)}


class TextInput(BaseModel):
    text: str = Field(min_length=1, max_length=1000)
    model: str = "qwen3:0.6b"
    context: str = Field(default="", max_length=1000)
    expected: str | None = None


def run_text(data, source="text", extra=None):
    try:
        result = classify(data.text, data.model, data.context)
    except Exception as e:
        raise HTTPException(503, f"로컬 모델 처리 실패: {e}") from e
    result.update({"text": data.text, "context": data.context, "source": source,
                   "timestamp": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "expected": data.expected})
    if data.expected is not None:
        if data.expected not in SKILLS:
            raise HTTPException(400, "알 수 없는 정답 행동")
        result["correct"] = result["action"] == data.expected
    if extra:
        result.update(extra)
    return result


@app.post("/api/classify")
def text_endpoint(data: TextInput):
    if data.model not in MODELS:
        raise HTTPException(400, "테스트 모델만 허용됩니다.")
    if not LOCK.acquire(blocking=False):
        raise HTTPException(409, "다른 시험이 진행 중입니다. 완료 후 다시 시도해 주세요.")
    try:
        with Monitor() as monitor:
            result = run_text(data)
        result["resources"] = monitor.result
        append_result(result)
        return result
    finally:
        LOCK.release()


def transcribe(raw):
    global ASR
    from faster_whisper import WhisperModel
    from faster_whisper.audio import decode_audio
    model_path = ROOT / "models" / ASR_MODEL
    if not (model_path / "model.bin").exists():
        raise ValueError(f"음성 모델이 없습니다: {model_path.name}. COMPANION_ASR_MODEL 설정을 확인하세요.")
    start = time.perf_counter()
    cold = ASR is None
    if cold:
        ASR = WhisperModel(str(model_path), device="cpu", compute_type="int8", cpu_threads=THREADS,
                           num_workers=1, local_files_only=True)
    load_ms = (time.perf_counter() - start) * 1000
    audio = decode_audio(io.BytesIO(raw), sampling_rate=16000)
    if len(audio) > 16000 * 30:
        raise ValueError("음성은 30초 이하로 입력해 주세요.")
    segments, info = ASR.transcribe(audio, language="ko", beam_size=1, vad_filter=True,
                                    condition_on_previous_text=False)
    text = " ".join(s.text.strip() for s in segments).strip()
    elapsed = (time.perf_counter() - start) * 1000
    return text, {"asr_ms": round(elapsed, 1), "asr_load_ms": round(load_ms, 1), "asr_cold": cold,
                  "audio_seconds": round(len(audio) / 16000, 2), "asr_model": f"{ASR_MODEL} / CPU int8"}


@app.post("/api/voice")
def voice_endpoint(audio: UploadFile = File(...), model: str = Form("qwen3:0.6b"),
                   context: str = Form(""), expected: str = Form("")):
    if model not in MODELS or len(context) > 1000 or (expected and expected not in SKILLS):
        raise HTTPException(400, "잘못된 모델 또는 입력")
    raw = audio.file.read(10 * 1024 * 1024 + 1)
    if len(raw) > 10 * 1024 * 1024:
        raise HTTPException(413, "파일은 10MB 이하로 입력해 주세요.")
    if not LOCK.acquire(blocking=False):
        raise HTTPException(409, "다른 시험이 진행 중입니다.")
    try:
        with Monitor() as monitor:
            try:
                text, asr = transcribe(raw)
            except Exception as e:
                raise HTTPException(422, f"음성 인식 실패: {e}") from e
            if not text:
                return {"action": None, "label": "음성을 인식하지 못했어요", "text": "", "asr": asr,
                        "abstained": True, "source": "voice"}
            result = run_text(TextInput(text=text, model=model, context=context, expected=expected or None),
                              source="voice", extra={"asr": asr})
        result["resources"] = monitor.result
        append_result(result)
        return result
    finally:
        LOCK.release()


@app.get("/api/results")
def results():
    path = ROOT / "results" / "sessions.jsonl"
    if not path.exists():
        return {"rows": []}
    lines = path.read_text(encoding="utf-8").splitlines()[-100:]
    return {"rows": [json.loads(line) for line in lines]}


@app.get("/api/download")
def download():
    path = ROOT / "results" / "sessions.jsonl"
    if not path.exists():
        raise HTTPException(404, "아직 기록이 없습니다.")
    return FileResponse(path, filename="ai-intent-results.jsonl", media_type="application/x-ndjson")


@app.get("/api/benchmark")
def benchmark_status():
    return BENCH


@app.post("/api/benchmark")
def start_benchmark():
    if not LOCK.acquire(blocking=False):
        raise HTTPException(409, "다른 시험이 진행 중입니다.")
    BENCH.update(running=True, finished=False, progress=0, summary=None, error=None)
    def work():
        from benchmark import run
        try:
            def call(data):
                with Monitor() as monitor:
                    result = run_text(TextInput(**data), source="synthetic_benchmark")
                result["resources"] = monitor.result
                return result
            def progress(n, total, current):
                BENCH.update(progress=n, total=total, current=current)
            result = run(call, progress)
            BENCH["summary"] = result["summary"]
        except Exception as e:
            BENCH["error"] = str(e)
        finally:
            BENCH.update(running=False, finished=True)
            LOCK.release()
    threading.Thread(target=work, daemon=True).start()
    return {"started": True}


@app.post("/api/shutdown")
def shutdown():
    if LOCK.locked():
        raise HTTPException(409, "처리 중입니다. 완료 후 종료해 주세요.")
    if SERVER is None:
        raise HTTPException(503, "종료 제어가 없는 실행 방식입니다. 서버 실행 창에서 종료해 주세요.")
    SERVER.should_exit = True
    return {"stopping": True}


app.mount("/static", StaticFiles(directory=ROOT / "static"), name="static")

from guided import router as guided_router
app.include_router(guided_router)
from companion import router as companion_router
app.include_router(companion_router)
from companion_v2 import router as companion_v2_router
app.include_router(companion_v2_router)
from companion_v3 import router as companion_v3_router
app.include_router(companion_v3_router)
from companion_v4 import router as companion_v4_router
app.include_router(companion_v4_router)

if __name__ == "__main__":
    # Routes importing app share the running process lock instead of a second module instance.
    import sys
    sys.modules['app'] = sys.modules[__name__]
    import uvicorn
    SERVER = uvicorn.Server(uvicorn.Config(app, host="127.0.0.1", port=8765, log_level="info"))
    SERVER.run()
