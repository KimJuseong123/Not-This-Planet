"""릴리스 산출물 배치 (S15P21B202-405).

실행: python scripts/ci/tests/test_publish_builds.py
네트워크 없이 돈다.

**무엇을 막는 검증인가.** `promote` 가 `latest.json` 을 옮기는 순간 **모든 사용자의
런처가 그 버전으로 간다.** 그 버전의 파일이 하나라도 저장소에 없으면 받는 사람이
중간에 멈추고, 그때는 이미 늦다 — 옮기기 **전에** 확인해야 한다.
"""
import io
import json
import os
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "scripts", "launcher"))
sys.stdout.reconfigure(encoding="utf-8")

os.environ.setdefault("CI_PROJECT_ID", "1443653")
os.environ.setdefault("GITLAB_TOKEN", "x")

import make_manifest as M  # noqa: E402
import publish_builds as P  # noqa: E402

NL = chr(10)


def load(path):
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


tmp = tempfile.mkdtemp()
cwd = os.getcwd()
try:
    builds = os.path.join(tmp, "builds")
    os.makedirs(builds)
    P.BUILDS = builds

    # 작은 "빌드" 두 개를 실제로 올려 둔다.
    for i, v in enumerate(("v1.0.0", "v1.0.1")):
        b = os.path.join(tmp, f"b{i}")
        os.makedirs(b)
        for rel, data in {"Game.exe": f"exe-{i}".encode(), "d/a.dat": b"A" * 50}.items():
            p = os.path.join(b, rel.replace("/", os.sep))
            os.makedirs(os.path.dirname(p), exist_ok=True)
            io.open(p, "wb").write(data)
        assert M.main([b, v, "--out", builds, "--no-latest"]) == 0

    # ── ① --no-latest 로 올렸으므로 아직 아무도 그 버전을 받지 않는다 ────
    assert not os.path.exists(os.path.join(builds, "latest.json")), \
        "올리기만 했는데 latest.json 이 생겼다 — 사람이 누르기 전에 배포된다"
    print("  ① 올려 둬도 latest.json 은 안 생긴다 ✅")

    # ── ② 옮길 버전을 모르면 멈춘다 ──────────────────────────────────────
    os.chdir(tmp)
    os.environ.pop("PROMOTE_VERSION", None)
    assert P.promote() == 1, "버전을 모르는데 옮겼다"

    # ── ③ 없는 버전은 거절한다 ───────────────────────────────────────────
    os.environ["PROMOTE_VERSION"] = "v9.9.9"
    assert P.promote() == 1, "없는 버전을 가리켰다"
    assert not os.path.exists(os.path.join(builds, "latest.json"))
    print("  ② 버전을 모르거나 없으면 옮기지 않는다 ✅")

    # ── ④ 정상 경로 ──────────────────────────────────────────────────────
    os.environ["PROMOTE_VERSION"] = "v1.0.0"
    assert P.promote() == 0
    assert load(os.path.join(builds, "latest.json"))["version"] == "v1.0.0"
    print("  ③ 정상 전환 ✅")

    # ── ⑤ **파일이 빠진 버전은 가리키지 않는다** ─────────────────────────
    # 저장소 회수(-406 --keep)나 올리다 만 경우에 생긴다. 옮기고 나서 알면
    # 받는 사람이 중간에 멈추고, 되돌릴 때까지 아무도 받지 못한다.
    m = load(os.path.join(builds, "manifest", "v1.0.1.json"))
    victim = m["files"][0]["sha256"]
    os.remove(os.path.join(builds, "files", victim + ".gz"))
    os.environ["PROMOTE_VERSION"] = "v1.0.1"
    assert P.promote() == 1, "파일이 빠졌는데 가리켰다"
    assert load(os.path.join(builds, "latest.json"))["version"] == "v1.0.0", \
        "거절했는데 latest.json 이 바뀌었다"
    print("  ④ 파일이 빠진 버전은 가리키지 않는다(=옛 버전 그대로) ✅")

    # ── ⑥ 되돌리기가 같은 경로다 ─────────────────────────────────────────
    b2 = os.path.join(tmp, "b1")
    assert M.main([b2, "v1.0.1", "--out", builds, "--no-latest"]) == 0   # 빠진 파일 복구
    os.environ["PROMOTE_VERSION"] = "v1.0.1"
    assert P.promote() == 0
    assert load(os.path.join(builds, "latest.json"))["version"] == "v1.0.1"
    os.environ["PROMOTE_VERSION"] = "v1.0.0"          # 되돌린다
    assert P.promote() == 0
    assert load(os.path.join(builds, "latest.json"))["version"] == "v1.0.0"
    print("  ⑤ 되돌리기가 같은 잡이다(PROMOTE_VERSION) ✅")

    # ── ⑦ 배포 폴더가 없으면 사유를 말하고 멈춘다 ────────────────────────
    P.BUILDS = os.path.join(tmp, "없는곳")
    assert P.promote() == 1
    assert P.stage() == 1
    P.BUILDS = builds
    print("  ⑥ 마운트가 없으면 사유를 말하고 멈춘다 ✅")

    # ── ⑧ 아티팩트가 안 넘어오면 멈춘다 ──────────────────────────────────
    for f in (P.VERSION_FILE, P.PACKAGE_FILE):
        if os.path.exists(f):
            os.remove(f)
    assert P.stage() == 1, "release-version.txt 가 없는데 진행했다"
    print("  ⑦ 릴리스 아티팩트가 없으면 멈춘다 ✅")

    # ── ⑨ 패키지 주소가 버전·파일명을 인코딩한다 ─────────────────────────
    os.environ["CI_API_V4_URL"] = "https://lab.example/api/v4"
    url = P.package_url("v1.0.0", "a b.zip")
    assert url.endswith("/packages/generic/metagame/v1.0.0/a%20b.zip"), url
    print("  ⑧ 패키지 주소 인코딩 ✅")

    # ── ⑩ PROMOTE_VERSION 은 사람이 손으로 적는 값이다 (S15P21B202-467) ──
    #     그대로 `manifest/<값>.json` 경로가 되고 그대로 `latest.json` 에 실린다.
    #     `../` 가 섞이면 배포 폴더 **밖**을 읽는다.
    for bad in ("../../etc/passwd", "v1.0.0/../../x", "latest", "v1.0", "", "v1.0.0-rehearsal"):
        assert not P.VERSION_RE.fullmatch(bad), f"형식 검사를 통과하면 안 되는 값: {bad!r}"
    for good in ("v1.0.0", "1.0.0", "v12.3.45"):
        assert P.VERSION_RE.fullmatch(good), f"정상 버전을 막는다: {good!r}"

    #     검사가 **경로를 만들기 전에** 있어야 의미가 있다.
    src = io.open(os.path.join(ROOT, "scripts", "ci", "publish_builds.py"),
                  encoding="utf-8").read()
    body = src.split("def promote(", 1)[1]
    assert body.index("VERSION_RE.fullmatch") < body.index("manifest"), \
        "형식 검사가 경로를 만든 뒤에 있다 — 그때는 이미 늦다"
    print("  ⑨ PROMOTE_VERSION 형식 검사가 경로보다 앞에 있다 ✅")

    print()
    print("TEST405 OK")
finally:
    os.chdir(cwd)
    shutil.rmtree(tmp, ignore_errors=True)
