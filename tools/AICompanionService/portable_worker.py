"""Private, offline-only deployment entry point. No lab UI, captures or result logs."""
from __future__ import annotations

import argparse
import asyncio
import ctypes
import hashlib
import hmac
import io
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import threading
import time
import wave
from contextlib import asynccontextmanager

import httpx
import psutil
import uvicorn
from fastapi import FastAPI, File, HTTPException, Request, UploadFile
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field

import companion_v4
import companion_social
import companion_plan

# 모델 이름과 받아쓰기 힌트는 engine.py 한 곳에 있다 — 개발 서버(app.py)와 **같은 값을
# 본다**. 갈리면 시험한 것과 게임에 나가는 것이 달라지고, 시험 쪽이 늘 더 좋아 보인다.
from engine import ASR_CONTEXT, ASR_HOTWORDS, ASR_MODEL_NAME

VERSION = "0.1.0-preview"
ASR_NAME = ASR_MODEL_NAME
LLM_NAME = "qwen3:1.7b"


def check_models(root: Path):
    """Presence check only; Ready additionally requires real inference warmup."""
    asr = root / ASR_NAME
    for name in ("model.bin", "config.json", "tokenizer.json"):
        if not (asr / name).is_file():
            raise ValueError("missing_asr_files")
    manifest = root / "ollama/manifests/registry.ollama.ai/library/qwen3/1.7b"
    data = json.loads(manifest.read_text(encoding="utf-8"))
    for item in [data["config"], *data["layers"]]:
        digest = item["digest"]
        if not isinstance(digest, str) or len(digest) != 71 or not digest.startswith("sha256:"):
            raise ValueError("invalid_model_digest")
        if any(c not in "0123456789abcdef" for c in digest[7:]):
            raise ValueError("invalid_model_digest")
        blob = root / "ollama/blobs" / digest.replace(":", "-")
        if not blob.is_file() or blob.stat().st_size != item["size"]:
            raise ValueError("missing_llm_blob")


class OwnedJob:
    """Windows job: worker/Ollama descendants die even if the game kills the worker."""
    def __init__(self):
        from ctypes import wintypes as w
        class Basic(ctypes.Structure):
            _fields_ = [("process_time", ctypes.c_int64), ("job_time", ctypes.c_int64),
                        ("flags", w.DWORD), ("min_ws", ctypes.c_size_t), ("max_ws", ctypes.c_size_t),
                        ("active", w.DWORD), ("affinity", ctypes.c_size_t), ("priority", w.DWORD), ("scheduling", w.DWORD)]
        class IO(ctypes.Structure):
            _fields_ = [(name, ctypes.c_uint64) for name in ("ro", "wo", "oo", "rb", "wb", "ob")]
        class Extended(ctypes.Structure):
            _fields_ = [("basic", Basic), ("io", IO), ("pm", ctypes.c_size_t),
                        ("jm", ctypes.c_size_t), ("pp", ctypes.c_size_t), ("pj", ctypes.c_size_t)]
        self.api = ctypes.WinDLL("kernel32", use_last_error=True)
        self.api.CreateJobObjectW.argtypes = [ctypes.c_void_p, w.LPCWSTR]
        self.api.CreateJobObjectW.restype = w.HANDLE
        self.api.SetInformationJobObject.argtypes = [w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD]
        self.api.AssignProcessToJobObject.argtypes = [w.HANDLE, w.HANDLE]
        self.api.GetCurrentProcess.restype = w.HANDLE
        self.api.CloseHandle.argtypes = [w.HANDLE]
        self.handle = self.api.CreateJobObjectW(None, None)
        info = Extended()
        info.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not self.handle or not self.api.SetInformationJobObject(self.handle, 9, ctypes.byref(info), ctypes.sizeof(info)):
            raise ctypes.WinError(ctypes.get_last_error())
        if not self.api.AssignProcessToJobObject(self.handle, self.api.GetCurrentProcess()):
            self.api.CloseHandle(self.handle)
            raise ctypes.WinError(ctypes.get_last_error())
        # Do not CloseHandle during normal Python execution: this worker also belongs to the job.
        # OS closes this non-inherited handle on exit; owned children then terminate.


class Worker:
    def __init__(self, model_root, ollama_exe, parent_pid, parent_started, token, ollama_port):
        if len(token) < 32 or any(c not in "0123456789abcdef" for c in token):
            raise ValueError("invalid_session_token")
        self.models = Path(model_root).resolve()
        self.ollama_exe = Path(ollama_exe).resolve()
        self.parent_pid, self.parent_started = parent_pid, parent_started
        self.token, self.ollama_port = token, ollama_port
        self.state, self.error = "loading", None
        self.process = self.server = self.asr = None
        self.lock = threading.Lock()
        self.stopping = threading.Event()
        self.url = f"http://127.0.0.1:{ollama_port}"

    def parent_alive(self):
        try:
            return abs(psutil.Process(self.parent_pid).create_time() - self.parent_started) < 0.01
        except psutil.Error:
            return False

    def warmup(self):
        try:
            check_models(self.models)
            if not self.ollama_exe.is_file():
                raise ValueError("missing_ollama_runtime")
            with socket.socket() as sock:
                sock.bind(("127.0.0.1", self.ollama_port))  # refuse a pre-existing service
            env = os.environ.copy()
            env.update(OLLAMA_HOST=f"127.0.0.1:{self.ollama_port}",
                       OLLAMA_MODELS=str(self.models / "ollama"), OLLAMA_NO_CLOUD="1",
                       OLLAMA_NUM_PARALLEL="1", OLLAMA_MAX_LOADED_MODELS="1",
                       OLLAMA_CONTEXT_LENGTH="2048", HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1")
            self.process = subprocess.Popen([str(self.ollama_exe), "serve"], env=env,
                cwd=str(self.ollama_exe.parent), stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            with httpx.Client(timeout=1, trust_env=False) as client:
                for _ in range(120):
                    if self.stopping.is_set() or self.process.poll() is not None:
                        raise ValueError("ollama_stopped")
                    try:
                        client.get(self.url + "/api/version").raise_for_status()
                        break
                    except httpx.HTTPError:
                        self.stopping.wait(0.25)
                else:
                    raise ValueError("ollama_start_timeout")
            from faster_whisper import WhisperModel
            self.asr = WhisperModel(str(self.models / ASR_NAME), device="cpu", compute_type="int8",
                                    cpu_threads=4, num_workers=1, local_files_only=True)
            # Decode and infer synthetic silence, never a saved user recording.
            buffer = io.BytesIO()
            with wave.open(buffer, "wb") as wav:
                wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(16000)
                wav.writeframes(b"\0" * 32000)
            self.transcribe(buffer.getvalue())
            companion_v4.OLLAMA = self.url
            result = companion_v4.classify_companion("준비 검사입니다")
            if result["action"] not in companion_v4.ACTIONS:
                raise ValueError("invalid_warmup_result")
            if not self.stopping.is_set():
                self.state = "ready"
        except Exception:
            self.state, self.error = "failed", "runtime_preparation_failed"
            self.stop_ollama()

    def stop_ollama(self):
        process = self.process
        if process is not None and process.poll() is None:
            # Only the child started by this worker. Never kill by port or process name.
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)

    def transcribe(self, raw):
        from faster_whisper.audio import decode_audio
        audio = decode_audio(io.BytesIO(raw), sampling_rate=16000)
        if len(audio) > 16000 * 30:
            raise ValueError("audio_too_long")
        segments, _ = self.asr.transcribe(audio, language="ko", beam_size=1,
                                         vad_filter=True, condition_on_previous_text=False,
                                         initial_prompt=ASR_CONTEXT, hotwords=ASR_HOTWORDS)
        return " ".join(s.text.strip() for s in segments).strip()

    def execute(self, text=None, raw=None, social=False, plan=False):
        if self.state != "ready" or self.stopping.is_set():
            raise HTTPException(503, "runtime_not_ready")
        if not self.lock.acquire(blocking=False):
            raise HTTPException(409, "busy")
        # 아래 `except` 도 이 값을 쓴다 — try 안에 두면 "실패한 경로에서는 안 잡혀 있을 수도
        # 있나" 를 매번 다시 확인해야 한다.
        started = time.perf_counter()
        try:
            if raw is not None:
                text = self.transcribe(raw)
            result = (companion_plan.classify(text, self.url) if plan else companion_social.classify(text, self.url) if social else companion_v4.classify_companion(text)) if text and text.strip() else ({"status": "none", "orders": [], "text": text or ""} if plan else {"action": "none", "text": ""})
            result["elapsed_ms"] = round((time.perf_counter() - started) * 1000)
            return result  # response only; never written to disk
        except (ValueError, TypeError, KeyError):
            # Malformed model output is a rejected command, not a permanently dead AI runtime.
            #
            # **받아쓴 말은 돌려준다** (S15P21B202-727). 전에는 `text` 없이 돌려줘서, 게임의
            # `[AI] 들음:` 로그에 아무것도 안 남았다 — 밖에서 보면 **"못 알아들었다" 와
            # "듣고 버렸다" 가 똑같아 보인다.** 2026-09-23 측정에서 그 때문에 받아쓰기를
            # 애먼 곳에서 의심했다(70% 로 보였지만 실제로는 77%였다).
            #
            # `elapsed_ms` 도 실제 값으로 준다. `0` 이면 "아무 일도 안 했다" 로 읽힌다.
            elapsed = round((time.perf_counter() - started) * 1000)
            return ({"status": "none", "orders": [], "text": text or "", "elapsed_ms": elapsed} if plan
                    else {"action": "none", "text": text or "", "elapsed_ms": elapsed})
        except Exception:
            self.state, self.error = "failed", "inference_failed"
            raise HTTPException(503, "inference_failed") from None
        finally:
            self.lock.release()


class TextInput(BaseModel):
    text: str = Field(min_length=1, max_length=300)


def make_app(worker):
    @asynccontextmanager
    async def lifespan(app):
        thread = threading.Thread(target=worker.warmup, daemon=True)
        thread.start()
        async def watchdog():
            while not worker.stopping.is_set():
                if not worker.parent_alive():
                    worker.stopping.set()
                    if worker.server is not None:
                        worker.server.should_exit = True
                    break
                if worker.state == "ready" and worker.process.poll() is not None:
                    worker.state, worker.error = "failed", "ollama_stopped"
                await asyncio.sleep(0.5)
        task = asyncio.create_task(watchdog())
        try:
            yield
        finally:
            worker.stopping.set()
            task.cancel()
            worker.stop_ollama()

    app = FastAPI(lifespan=lifespan, docs_url=None, redoc_url=None, openapi_url=None)

    @app.middleware("http")
    async def authorize(request: Request, call_next):
        if request.headers.get("origin") or request.client.host not in {"127.0.0.1", "testclient"}:
            return JSONResponse({"detail": "local_client_only"}, status_code=403)
        if not hmac.compare_digest(request.headers.get("authorization", ""), "Bearer " + worker.token):
            return JSONResponse({"detail": "unauthorized"}, status_code=401)
        length = request.headers.get("content-length", "0")
        if not length.isdigit() or int(length) > 1100000:
            return JSONResponse({"detail": "request_too_large"}, status_code=413)
        return await call_next(request)

    @app.get("/api/health")
    def health():
        return {"service": "ntp-ai-worker", "version": VERSION, "protocol": "companion-v4",
                "state": worker.state, "error": worker.error, "busy": worker.lock.locked(),
                "actions": companion_v4.ACTIONS, "command_schema": "work-v1", "voice_plan": True, "plan_actions": companion_plan.ACTIONS, "game_actions_ready": False}

    @app.post("/api/companion-plan/voice")
    def plan_voice(audio: UploadFile = File(...)):
        raw = audio.file.read(256045)
        if len(raw) > 256044:
            raise HTTPException(413, "audio_too_large")
        if len(raw) < 44 or raw[:4] != b"RIFF" or raw[8:12] != b"WAVE":
            raise HTTPException(400, "wav_required")
        return worker.execute(raw=raw, plan=True)

    @app.post("/api/companion-plan/text")
    def plan_text(data: TextInput):
        return worker.execute(text=data.text, plan=True)

    @app.post("/api/companion-social/text")
    def social_text(data: TextInput):
        return worker.execute(text=data.text, social=True)

    @app.post("/api/companion-v4/text")
    def text(data: TextInput):
        return worker.execute(text=data.text)

    @app.post("/api/companion-v4/voice")
    def voice(audio: UploadFile = File(...)):
        raw = audio.file.read(1024 * 1024 + 1)
        if len(raw) > 1024 * 1024:
            raise HTTPException(413, "audio_too_large")
        return worker.execute(raw=raw)

    @app.post("/api/shutdown")
    def shutdown():
        worker.stopping.set()
        if worker.server is not None:
            worker.server.should_exit = True
        return {"stopping": True}
    return app


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--model-root")
    parser.add_argument("--ollama-exe")
    parser.add_argument("--parent-pid", type=int)
    parser.add_argument("--parent-started", type=float)
    parser.add_argument("--port", type=int, default=18765)
    parser.add_argument("--ollama-port", type=int, default=11439)
    args = parser.parse_args()
    if args.self_test:
        import faster_whisper, ctranslate2, av, onnxruntime
        print(json.dumps({"self_test": "dependencies_imported", "version": VERSION, "models_tested": False}))
        return
    if not all((args.model_root, args.parent_pid, args.parent_started)):
        parser.error("model-root, parent-pid and parent-started are required")
    if not 1024 <= args.port <= 65535 or not 1024 <= args.ollama_port <= 65535 or args.port == args.ollama_port:
        parser.error("invalid ports")
    # Game sends a per-launch secret via redirected stdin, never command line or disk.
    token = sys.stdin.readline(256).strip()
    pack = Path(sys.executable).parent if getattr(sys, "frozen", False) else Path(__file__).parent
    worker = Worker(args.model_root, args.ollama_exe or pack / "ollama/ollama.exe",
                    args.parent_pid, args.parent_started, token, args.ollama_port)
    if not worker.parent_alive():
        raise RuntimeError("parent_not_running")
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", args.port))
    job = OwnedJob() if os.name == "nt" else None
    os.environ.update(HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1")
    worker.server = uvicorn.Server(uvicorn.Config(make_app(worker), host="127.0.0.1", port=args.port,
        access_log=False, log_level="critical", timeout_graceful_shutdown=5, ws="none"))
    try:
        worker.server.run()
    finally:
        worker.stopping.set()
        worker.stop_ollama()


if __name__ == "__main__":
    main()
