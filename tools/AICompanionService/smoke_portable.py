"""Exercise the built EXE with an isolated cache/PATH; synthetic inputs only."""
import argparse
import io
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import time
import wave
import httpx
import psutil


def wait_health(client, process, deadline=120):
    end = time.monotonic() + deadline
    while time.monotonic() < end:
        if process.poll() is not None:
            raise RuntimeError("worker exited: " + str(process.returncode))
        try:
            health = client.get("/api/health").json()
            if health.get("state") in {"ready", "failed"}:
                return health
        except (httpx.HTTPError, ValueError):
            pass
        time.sleep(0.25)
    raise TimeoutError("warmup")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("pack", type=Path)
    parser.add_argument("--port", type=int, default=18775)
    parser.add_argument("--ollama-port", type=int, default=11449)
    args = parser.parse_args()
    pack = args.pack.resolve()
    exe = pack / "runtime/ntp-ai-worker/ntp-ai-worker.exe"
    models = pack / "models"
    report = {}
    with tempfile.TemporaryDirectory(prefix="ntp-ai-isolated-") as cache:
        env = os.environ.copy()
        for key in list(env):
            if key.startswith(("PYTHON", "OLLAMA", "CONDA", "HF_", "TRANSFORMERS")):
                env.pop(key)
        env.update(PATH=str(Path(os.environ["SystemRoot"]) / "System32"), HF_HOME=cache,
                   HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", USERPROFILE=cache,
                   HOME=cache, LOCALAPPDATA=cache, APPDATA=cache)
        self_test = subprocess.run([str(exe), "--self-test"], env=env, cwd=cache, capture_output=True,
                                  timeout=40, creationflags=subprocess.CREATE_NO_WINDOW)
        if self_test.returncode:
            raise RuntimeError(self_test.stderr.decode(errors="replace"))
        report["dependency_self_test"] = json.loads(self_test.stdout)
        for scenario in ("normal", "parent_exit", "worker_kill"):
            parent = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(180)"],
                                      creationflags=subprocess.CREATE_NO_WINDOW)
            token = secrets.token_hex(32)
            process = None
            children = []
            try:
                command = [str(exe), "--model-root", str(models), "--parent-pid", str(parent.pid),
                           "--parent-started", str(psutil.Process(parent.pid).create_time()),
                           "--port", str(args.port), "--ollama-port", str(args.ollama_port)]
                process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE, env=env, cwd=cache, creationflags=subprocess.CREATE_NO_WINDOW)
                process.stdin.write((token + "\n").encode()); process.stdin.flush(); process.stdin.close()
                base = f"http://127.0.0.1:{args.port}"
                with httpx.Client(base_url=base, headers={"Authorization": "Bearer " + token},
                                  timeout=35, trust_env=False) as client:
                    started = time.monotonic()
                    health = wait_health(client, process)
                    if health["state"] != "ready":
                        raise RuntimeError(json.dumps(health))
                    children = psutil.Process(process.pid).children(recursive=True)
                    report[scenario] = {"warmup_seconds": round(time.monotonic() - started, 2), "health": health}
                    if scenario == "normal":
                        no_auth = httpx.get(base + "/api/health", timeout=2, trust_env=False)
                        assert no_auth.status_code == 401
                        response = client.post("/api/companion-v4/text", json={"text": "나 따라와"})
                        response.raise_for_status()
                        report[scenario]["action"] = response.json()["action"]
                        assert response.json()["action"] == "follow"
                        wav_bytes = io.BytesIO()
                        with wave.open(wav_bytes, "wb") as wav:
                            wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(16000)
                            wav.writeframes(b"\0" * 32000)
                        voice = client.post("/api/companion-v4/voice", files={"audio": ("silence.wav", wav_bytes.getvalue(), "audio/wav")})
                        voice.raise_for_status()
                        report[scenario]["silence_action"] = voice.json()["action"]
                        assert voice.json()["action"] == "none"
                        client.post("/api/shutdown").raise_for_status()
                    elif scenario == "parent_exit":
                        parent.terminate(); parent.wait(timeout=5)
                    else:
                        process.kill()
                    process.wait(timeout=15)
                    time.sleep(1)
                    alive = [p.pid for p in children if p.is_running()]
                    assert not alive, f"Owned processes still alive: {alive}"
                    report[scenario]["owned_children_stopped"] = True
            finally:
                if process is not None and process.poll() is None:
                    process.kill(); process.wait(timeout=10)
                if parent.poll() is None:
                    parent.terminate(); parent.wait(timeout=5)
        report["clean_pc_test"] = False
        report["scope"] = "same PC, isolated PATH/user cache; synthetic silence + one text command; no game actions"
        (pack / "smoke-result.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
        print(json.dumps(report, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
