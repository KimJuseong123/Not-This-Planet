"""Fail before Unity imports unresolved Git LFS pointers as game assets."""
from pathlib import Path
import subprocess
import sys

POINTER_HEADER = b"version https://git-lfs.github.com/spec/v1"


def asset_problems(root: Path, paths: list[str]) -> list[str]:
    problems = []
    for relative in paths:
        path = root / relative
        try:
            with path.open("rb") as stream:
                if stream.read(len(POINTER_HEADER)) == POINTER_HEADER:
                    problems.append(f"Unresolved Git LFS pointer: {relative}")
        except OSError as error:
            problems.append(f"Unreadable asset: {relative} ({error.__class__.__name__})")
    return problems


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    result = subprocess.run(
        ["git", "ls-files", "-z", "--", "METAGAME/Assets"],
        cwd=root, check=True, stdout=subprocess.PIPE,
    )
    paths = [p for p in result.stdout.decode("utf-8").split("\0") if p]
    if not paths:
        print("No tracked Unity assets found; refusing to validate an empty checkout.")
        return 1
    problems = asset_problems(root, paths)
    if problems:
        for problem in problems:
            print(problem)
        print("Restore the assets with git lfs pull and check case-sensitive .gitattributes rules before building.")
        return 1
    print(f"Unity LFS preflight passed: {len(paths)} tracked assets checked.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
