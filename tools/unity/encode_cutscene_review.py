"""Encode timestamped Unity camera captures into review GIFs (requires Pillow)."""
import csv
import sys
from pathlib import Path
from PIL import Image

source, destination = map(Path, sys.argv[1:3])
destination.mkdir(parents=True, exist_ok=True)
with (source / "frames.csv").open(encoding="utf-8") as stream:
    rows = list(csv.reader(stream))
for name in ("entry", "assault"):
    selected = [(path, float(age)) for kind, path, age in rows if kind == name]
    if len(selected) < 2:
        raise RuntimeError(f"Missing continuous capture: {name}")
    frames = []
    for path, _ in selected:
        with Image.open(source / path) as image:
            frames.append(image.convert("RGB").quantize(colors=128))
    durations = [max(20, round((b[1] - a[1]) * 1000)) for a, b in zip(selected, selected[1:])]
    durations.append(100)
    target = destination / f"{name}.gif"
    frames[0].save(target, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=False)
    print(f"{name}: {len(frames)} frames, {sum(durations) / 1000:.2f}s, {target.stat().st_size} bytes")
