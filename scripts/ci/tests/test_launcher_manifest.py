"""런처 매니페스트 도구 (S15P21B202-406).

실행: python scripts/ci/tests/test_launcher_manifest.py
네트워크 없이 돈다 — 임시 폴더에 작은 빌드를 흉내내서 검사한다.

**무엇을 막는 검증인가.** 이 도구의 출력은 **런처가 파일을 쓰는 지시서**다.
경로 하나가 잘못 들어가면 사용자 PC 의 게임 폴더 밖에 파일이 생긴다. 그리고
"바뀐 것만 받는다" 는 이 방식의 존재 이유인데, 해시나 공유 판정이 어긋나면
조용히 통짜 다운로드로 되돌아간다 — 느려지기만 하고 아무 오류도 안 난다.
"""
import gzip
import hashlib
import io
import json
import os
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.join(ROOT, "scripts", "launcher"))
sys.stdout.reconfigure(encoding="utf-8")

import make_manifest as M  # noqa: E402

NL = chr(10)


def build(root, files):
    for rel, data in files.items():
        p = os.path.join(root, rel.replace("/", os.sep))
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "wb") as f:
            f.write(data)


def load(path):
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


tmp = tempfile.mkdtemp()
try:
    # ── ① 경로 검사 — 런처가 이 문자열로 파일을 쓴다 ──────────────────────
    ok = os.path.join(tmp, "b1")
    os.makedirs(ok)
    assert M.safe_relpath(ok, os.path.join(ok, "a", "b.dll")) == "a/b.dll"
    # **구분자를 `/` 로 고정한다.** 섞이면 런처가 같은 파일을 다른 것으로 보고
    # 매번 다시 받는다 — 조용히 통짜 다운로드가 된다.
    assert "\\" not in M.safe_relpath(ok, os.path.join(ok, "a", "b.dll"))
    for bad in [os.path.join(tmp, "밖.dll"), os.path.join(tmp, "b2", "x.dll")]:
        try:
            M.safe_relpath(ok, bad)
            raise AssertionError(f"빌드 폴더 밖인데 통과시켰다: {bad}")
        except ValueError:
            pass
    print("  ① 경로: 구분자 고정 · 폴더 밖 거부 ✅")

    # ── ② 배포하면 안 되는 폴더를 거부한다 ────────────────────────────────
    b = os.path.join(tmp, "dbg")
    build(b, {"Game.exe": b"x", "Game_BurstDebugInformation_DoNotShip/x.txt": b"y"})
    try:
        M.collect(b)
        raise AssertionError("DoNotShip 폴더가 있는데 통과시켰다")
    except SystemExit as e:
        assert "DoNotShip" in str(e), e
    print("  ② Unity 디버그 산출물 거부 ✅")

    # ── ③ 실행 파일을 이름이 아니라 목록에서 정한다 (-402 대비) ────────────
    # 런처가 이름을 하드코딩하면 실행 파일 이름이 바뀔 때 깨진다. 그래서
    # 매니페스트가 이름을 담고, 여기서 그것을 정한다.
    b = os.path.join(tmp, "exe1")
    build(b, {"NotThisPlanet.exe": b"a", "UnityCrashHandler64.exe": b"b", "x/y.dll": b"c"})
    assert M.pick_exe(M.collect(b)) == "NotThisPlanet.exe"

    b = os.path.join(tmp, "exe2")   # 이름이 바뀐 미래 — 크래시 핸들러는 후보가 아니다
    build(b, {"Renamed.exe": b"a", "UnityCrashHandler64.exe": b"b"})
    assert M.pick_exe(M.collect(b)) == "Renamed.exe"

    b = os.path.join(tmp, "exe3")   # 하나로 못 정하면 **멈춘다** — 틀린 것을 켜면 안 된다
    build(b, {"A.exe": b"a", "B.exe": b"b"})
    try:
        M.pick_exe(M.collect(b))
        raise AssertionError("실행 파일이 둘인데 하나를 골랐다")
    except SystemExit:
        pass
    print("  ③ 실행 파일: 힌트·단일 후보·모호하면 멈춤 ✅")

    # ── ④ 해시가 내용을 따른다 (크기가 같아도 다르면 다르다) ──────────────
    b = os.path.join(tmp, "h")
    build(b, {"Game.exe": b"AAAA", "same.dat": b"AAAA", "diff.dat": b"AAAB"})
    files = M.collect(b)
    by = {e["path"]: e["sha256"] for e in files}
    assert by["Game.exe"] == by["same.dat"], "같은 내용인데 해시가 다르다"
    assert by["diff.dat"] != by["Game.exe"], "크기가 같고 내용이 다른데 해시가 같다"
    assert by["Game.exe"] == hashlib.sha256(b"AAAA").hexdigest()
    print("  ④ 해시: 내용 기준 · 크기 같아도 갈린다 ✅")

    # ── ⑤ 버전 간 공유 — 이 방식의 존재 이유 ───────────────────────────────
    out = os.path.join(tmp, "dist")
    v1 = os.path.join(tmp, "v1")
    build(v1, {"Game.exe": b"exe-v1", "big.dat": b"B" * 10000, "gone.dat": b"bye"})
    rc = M.main([v1, "v1.0.0", "--out", out])
    assert rc == 0, rc

    v2 = os.path.join(tmp, "v2")
    build(v2, {"Game.exe": b"exe-v2", "big.dat": b"B" * 10000, "new.dat": b"hi"})
    added, shared, gz, fresh = M.publish(M.collect(v2), v2, out, dry=False)
    # `big.dat` 이 그대로이므로 다시 올리지 않는다. 바뀐 exe 와 새 파일만 새로 생긴다.
    assert added == 2, f"새로 놓은 것 {added}개 (기대 2: 바뀐 exe · 새 파일)"
    assert shared == 1, f"공유 {shared}개 (기대 1: big.dat)"
    assert len(fresh) == 2
    print("  ⑤ 버전 간 공유: 안 바뀐 파일은 다시 올리지 않는다 ✅")

    # ── ⑥ 저장소는 gzip 이고, 풀면 원본과 바이트가 같다 ───────────────────
    # 압축하지 않으면 런처의 **첫 설치가 지금 zip 보다 2배 이상 무거워진다**
    # (v1.0.0 실측: 압축 없이 355.5 MB · zip 156 MB). 그런데 해시는 **푼 뒤**의
    # 바이트로 확인하므로, 굽는 것과 푸는 것이 어긋나면 런처가 전부 거부한다.
    m = load(os.path.join(out, "manifest", "v1.0.0.json"))
    entry = next(e for e in m["files"] if e["path"] == "big.dat")
    stored = os.path.join(out, "files", entry["sha256"] + ".gz")
    assert os.path.exists(stored), "저장소 파일이 .gz 가 아니다"
    raw = gzip.decompress(open(stored, "rb").read())
    assert raw == b"B" * 10000, "풀었더니 원본과 다르다"
    assert hashlib.sha256(raw).hexdigest() == entry["sha256"], "해시가 푼 바이트를 가리키지 않는다"
    assert entry["gz"] == os.path.getsize(stored), "매니페스트의 gz 크기가 실제와 다르다"
    print("  ⑥ gzip 저장 · 해시는 푼 바이트 기준 ✅")

    # ── ⑦ 같은 내용을 두 번 구우면 같은 바이트 (mtime=0) ──────────────────
    # gzip 은 기본으로 현재 시각을 헤더에 넣는다. 그러면 같은 파일을 다시 올릴 때
    # 바이트가 달라져 헛일이 생긴다.
    first = open(stored, "rb").read()
    os.remove(stored)
    M.publish([entry], v1, out, dry=False)
    assert open(stored, "rb").read() == first, "같은 내용인데 압축 결과가 다르다(mtime 이 들어갔다)"
    print("  ⑦ 압축이 결정적이다(mtime=0) ✅")

    # ── ⑧ latest.json 만 바뀐다 = 롤백 수단 ───────────────────────────────
    latest = load(os.path.join(out, "latest.json"))
    assert latest["version"] == "v1.0.0" and latest["manifest"] == "manifest/v1.0.0.json"
    rc = M.main([v2, "v1.0.1", "--out", out, "--no-latest"])
    assert rc == 0
    again = load(os.path.join(out, "latest.json"))
    assert again["version"] == "v1.0.0", "--no-latest 인데 최신 표시가 바뀌었다"
    assert os.path.exists(os.path.join(out, "manifest", "v1.0.1.json")), "매니페스트는 올라가야 한다"
    # 옛 매니페스트는 그대로 있어야 한다 — 롤백은 latest 를 되돌리는 것이다.
    assert os.path.exists(os.path.join(out, "manifest", "v1.0.0.json"))
    print("  ⑧ --no-latest: 올려 두고 전환은 따로 · 옛 버전 보존 ✅")

    # ── ⑨ 버전 모양을 강제한다 ────────────────────────────────────────────
    # 태그·릴리스 노트·게이트가 모두 `vX.Y.Z` 를 쓴다. 여기서만 달라지면
    # 매니페스트 이름과 태그가 어긋나 사람이 찾지 못한다.
    assert M.main([v1, "1.0.0", "--out", out]) == 1, "`v` 없는 버전을 받아들였다"
    print("  ⑨ 버전은 v 로 시작해야 한다 ✅")

    print()
    print("TEST406 OK")
finally:
    shutil.rmtree(tmp, ignore_errors=True)
