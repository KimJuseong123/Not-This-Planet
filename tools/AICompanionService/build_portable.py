"""Build a Windows CPU preview pack from explicit local inputs. Never downloads models."""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parent


def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(4 * 1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def copy_llm(source, target):
    rel = Path("manifests/registry.ollama.ai/library/qwen3/1.7b")
    manifest = json.loads((source / rel).read_text(encoding="utf-8"))
    (target / rel).parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source / rel, target / rel)
    for item in [manifest["config"], *manifest["layers"]]:
        digest = item["digest"]
        if len(digest) != 71 or not digest.startswith("sha256:") or any(c not in "0123456789abcdef" for c in digest[7:]):
            raise ValueError("Invalid Ollama digest")
        blob = source / "blobs" / digest.replace(":", "-")
        if blob.stat().st_size != item["size"] or sha256(blob) != digest[7:]:
            raise ValueError("Ollama blob integrity failure")
        dest = target / "blobs" / blob.name
        dest.parent.mkdir(parents=True, exist_ok=True)
        if not dest.exists():
            shutil.copy2(blob, dest)


def copy_asr(source, target, tokenizer):
    target.mkdir(parents=True)
    for name in ("model.bin", "config.json", "preprocessor_config.json", "vocabulary.json", "vocabulary.txt", "tokenizer_config.json"):
        if (source / name).is_file():
            shutil.copy2(source / name, target / name)
    tokenizer = source / "tokenizer.json" if (source / "tokenizer.json").is_file() else tokenizer
    if tokenizer is None or not tokenizer.is_file():
        raise ValueError("Explicit offline tokenizer.json required")
    # A fallback tokenizer must have exactly the model's ID-to-token vocabulary.
    if (source / "vocabulary.json").is_file():
        vocab = json.loads((source / "vocabulary.json").read_text(encoding="utf-8"))
        from tokenizers import Tokenizer
        tok = Tokenizer.from_file(str(tokenizer))
        # faster-whisper omits 1501 timestamp tokens from tokenizer.json and handles them numerically.
        count = tok.get_vocab_size()
        expected_timestamps = [f"<|{i * 0.02:.2f}|>" for i in range(1501)]
        if (not isinstance(vocab, list) or any(tok.id_to_token(i) != token for i, token in enumerate(vocab[:count]))
                or (len(vocab) > count and (tok.token_to_id("<|notimestamps|>") != count - 1
                    or vocab[count:] != expected_timestamps))):
            raise ValueError("Tokenizer vocabulary does not match ASR model")
    shutil.copy2(tokenizer, target / "tokenizer.json")


def inventory(root):
    return [{"path": p.relative_to(root).as_posix(), "bytes": p.stat().st_size, "sha256": sha256(p)}
            for p in sorted(root.rglob("*")) if p.is_file()]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--ollama-dir", type=Path, required=True)
    p.add_argument("--model-root", type=Path, required=True)
    p.add_argument("--tokenizer", type=Path)
    p.add_argument("--skip-freeze", action="store_true", help="Reuse this build's existing frozen runtime")
    p.add_argument("--frozen-runtime", type=Path, help="Reuse a previously frozen runtime in a NEW output directory")
    args = p.parse_args()
    out = args.output.resolve()
    if out.exists() and not args.skip_freeze:
        raise SystemExit("Output already exists; choose a new output folder. Nothing deleted.")
    out.mkdir(parents=True, exist_ok=True)
    runtime = out / "runtime/ntp-ai-worker"
    models = out / "models"
    if args.frozen_runtime:
        shutil.copytree(args.frozen_runtime, runtime)
    elif not args.skip_freeze:
        command = [sys.executable, "-m", "PyInstaller", "--onedir", "--noupx", "--name", "ntp-ai-worker",
            "--distpath", str(out / "runtime"), "--workpath", str(out / "build"), "--specpath", str(out / "spec"),
            "--collect-all", "faster_whisper", "--collect-all", "ctranslate2", "--collect-binaries", "onnxruntime",
            "--collect-all", "tokenizers", "--collect-all", "av", "--collect-submodules", "uvicorn",
            str(ROOT / "portable_worker.py")]
        subprocess.run(command, check=True, cwd=ROOT)
    if not (runtime / "ntp-ai-worker.exe").is_file():
        raise SystemExit("Frozen worker missing")
    ollama = runtime / "ollama"
    ollama.mkdir(exist_ok=True)
    shutil.copy2(args.ollama_dir / "ollama.exe", ollama / "ollama.exe")
    cpu = ollama / "lib/ollama"
    cpu.mkdir(parents=True, exist_ok=True)
    # Installed CPU libraries/notices only, no installer/uninstaller, app, GPU directories or user config.
    for item in (args.ollama_dir / "lib/ollama").iterdir():
        if item.is_file():
            shutil.copy2(item, cpu / item.name)
    if models.exists():
        raise SystemExit("Model destination already exists; use a fresh output. Nothing overwritten.")
    copy_asr(args.model_root / "whisper-small-ko-zeroth", models / "whisper-small-ko-zeroth", args.tokenizer)
    copy_llm(args.model_root / "ollama", models / "ollama")
    notices = runtime / "THIRD_PARTY"
    notices.mkdir(exist_ok=True)
    dependencies = []
    for dist in importlib.metadata.distributions():
        name = dist.metadata["Name"]
        dependencies.append({"name": name, "version": dist.version, "license": dist.metadata.get("License-Expression", dist.metadata.get("License", ""))})
        for file in dist.files or []:
            if any(word in file.name.lower() for word in ("license", "copying", "notice")):
                dest = notices / name / str(file).replace("/", "_").replace("\\", "_")
                dest.parent.mkdir(parents=True, exist_ok=True)
                source = Path(dist.locate_file(file))
                if source.is_file():
                    shutil.copy2(source, dest)
    (notices / "dependency-inventory.json").write_text(json.dumps(dependencies, indent=2), encoding="utf-8")
    manifest = {"schema": 1, "version": "0.1.0-preview", "platform": "windows-x64-cpu", "protocol": "companion-v4",
                "release_approved": False, "release_blockers": ["clean-PC validation", "model/runtime license review", "game integration"],
                "models": ["qwen3:1.7b", "whisper-small-ko-zeroth"], "archives": []}
    for name, folder in (("runtime", runtime), ("models", models)):
        files = inventory(folder)
        (folder / "files.json").write_text(json.dumps(files, indent=2), encoding="utf-8")
        archive = out / (name + ".zip")
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=1) as z:
            for file in folder.rglob("*"):
                if file.is_file():
                    z.write(file, file.relative_to(folder))
        manifest["archives"].append({"name": archive.name, "bytes": archive.stat().st_size,
                                     "installed_bytes": sum(x["bytes"] for x in files), "sha256": sha256(archive)})
    (out / "pack-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
