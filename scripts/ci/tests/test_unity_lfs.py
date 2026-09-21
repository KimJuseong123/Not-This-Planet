"""Regression: .JPEG was hydrated on Windows but stayed a pointer on Linux."""
from pathlib import Path
import subprocess
import re
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "scripts/ci"))
from validate_unity_lfs import POINTER_HEADER, asset_problems


class UnityLfsTests(unittest.TestCase):
    def test_jpeg_case_variants_are_lfs_on_case_sensitive_checkouts(self):
        paths = ["METAGAME/Assets/_Project/example." + ext
                 for ext in ("jpeg", "JPEG", "Jpeg", "jPeG", "jpg", "png")]
        result = subprocess.run(
            ["git", "-c", "core.ignorecase=false", "check-attr", "-z", "--stdin", "filter"],
            cwd=ROOT, input="\0".join(paths).encode() + b"\0",
            stdout=subprocess.PIPE, check=True,
        )
        fields = result.stdout.decode().split("\0")[:-1]
        for path, attribute, value in zip(fields[0::3], fields[1::3], fields[2::3]):
            with self.subTest(path=path):
                self.assertEqual(value, "lfs")

    def test_pointer_is_rejected_regardless_of_extension_or_attributes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ("leaf.JPEG", "rock.fbx", "terrain.asset", "unknown.bin"):
                (root / name).write_bytes(POINTER_HEADER + b"\noid sha256:" + b"0" * 64 + b"\nsize 500\n")
                with self.subTest(name=name):
                    self.assertEqual(asset_problems(root, [name]), [f"Unresolved Git LFS pointer: {name}"])

    def test_real_image_and_text_assets_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "leaf.JPEG").write_bytes(b"\xff\xd8\xff\xe0" + b"image data")
            (root / "leaf.JPEG.meta").write_text("fileFormatVersion: 2\nguid: example\n")
            (root / "empty").write_bytes(b"")
            self.assertEqual(asset_problems(root, ["leaf.JPEG", "leaf.JPEG.meta", "empty"]), [])

    def test_missing_asset_fails_instead_of_silently_skipping(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(len(asset_problems(Path(directory), ["missing.JPEG"])), 1)

    def test_all_unity_jobs_check_assets_before_import_or_result_reuse(self):
        config = (ROOT / ".gitlab-ci.yml").read_text(encoding="utf-8")
        for job in ("unity-compile", "release-windows", "build-devbuild"):
            section = re.search(r"(?ms)^" + job + r":.*?(?=^[a-z][\w-]*:|\Z)", config).group()
            script = section.split("\n  script:", 1)[1]
            gate = script.index("- python3 scripts/ci/validate_unity_lfs.py")
            commands = re.findall(r"(?m)^\s+(?:- )?(?:unity-editor|python3 scripts/ci/unity_reuse.py).*$", script)
            with self.subTest(job=job):
                self.assertTrue(commands)
                for command in commands:
                    self.assertLess(gate, script.index(command.strip()))


if __name__ == "__main__":
    unittest.main()
