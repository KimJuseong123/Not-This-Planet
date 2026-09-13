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

    # ── ⑩ 버전 목록 — 개발 런처가 이것을 보고 고른다 ───────────────────
    # `/builds/` 의 디렉터리 목록을 닫았으므로(-454) **명시적 목록 파일이 없으면
    # 개발 런처가 되돌릴 버전을 알 수 없다.**
    vs = load(os.path.join(out, "versions.json"))
    got = [r["version"] for r in vs["versions"]]
    assert got == ["v1.0.1", "v1.0.0"], f"목록이 새것부터가 아니다: {got}"
    assert all(r["files"] > 0 for r in vs["versions"]), "파일 수가 비어 있다"
    print("  ⑩ versions.json: 새것부터 정렬 ✅")

    # 문자열로 정렬하면 v1.10.0 이 v1.9.0 앞에 온다 — 그러면 개발 런처의 목록이
    # 거짓이 되고, 보관 회수가 **틀린 버전을 지운다.**
    assert M.parse_version("v1.10.0") > M.parse_version("v1.9.0")
    assert M.parse_version("v1.9.0") < M.parse_version("v2.0.0")
    for bad in ["", "v1", "v1.0", "v1.0.0.0", "vX.Y.Z", "v1.0.-1"]:
        assert M.parse_version(bad) is None, f"버전 모양이 아닌데 읽었다: {bad}"
    print("  ⑪ 버전 정렬이 숫자 기준이다(v1.10.0 > v1.9.0) ✅")

    # ── ⑫ 보관 회수 — **되돌릴 수 없으므로** 크게 검사한다 ──────────────
    gc_out = os.path.join(tmp, "gc")
    builds = {}
    for i in range(4):
        b = os.path.join(tmp, f"g{i}")
        # `shared.dat` 은 모든 버전이 같이 쓴다 — 회수가 이것을 지우면 전부 깨진다.
        build(b, {"Game.exe": f"exe-{i}".encode(), "shared.dat": b"S" * 100,
                  f"only{i}.dat": f"only-{i}".encode()})
        builds[f"v1.0.{i}"] = b
        rc = M.main([b, f"v1.0.{i}", "--out", gc_out, "--keep", "0"])
        assert rc == 0, rc

    def store_digests():
        d = os.path.join(gc_out, "files")
        return {n[:-3] for n in os.listdir(d) if n.endswith(".gz")}

    before = store_digests()
    # 런처도 같은 저장소에 있다고 가정한다 — 회수가 이것을 지우면 런처를 못 받는다.
    launcher_digest = "f" * 64
    io.open(os.path.join(gc_out, "files", launcher_digest + ".gz"), "wb").write(b"x")
    io.open(os.path.join(gc_out, "launcher.json"), "w", encoding="utf-8").write(
        '{"schema":1,"version":"v1.0.0","sha256":"' + launcher_digest + '"}')

    dropped, gone, freed = M.collect_garbage(gc_out, M.RELEASE, keep=2, dry=False)
    assert dropped == 2, f"지운 매니페스트 {dropped}개 (기대 2: v1.0.0 · v1.0.1)"
    left = sorted(M.manifest_versions(gc_out))
    assert left == ["v1.0.2", "v1.0.3"], f"남은 버전이 틀렸다: {left}"

    after = store_digests()
    # ① 남은 버전이 쓰는 파일은 **전부 살아 있어야 한다**
    for v in left:
        m = load(os.path.join(gc_out, "manifest", f"{v}.json"))
        for e in m["files"]:
            assert e["sha256"] in after, f"{v} 가 쓰는 파일을 지웠다: {e['path']}"
    # ② 모든 버전이 공유하던 파일도 살아 있어야 한다
    shared = next(e["sha256"] for e in
                  load(os.path.join(gc_out, "manifest", "v1.0.3.json"))["files"]
                  if e["path"] == "shared.dat")
    assert shared in after, "공유 파일을 지웠다 — 전 버전이 깨진다"
    # ③ 런처 파일도 살아 있어야 한다
    assert launcher_digest in after, "런처 파일을 지웠다 — 런처를 못 받는다"
    # ④ 그리고 실제로 **무언가는 지워야** 한다(회수가 동작하는지)
    assert gone > 0 and freed > 0, f"회수가 아무것도 하지 않았다 ({gone}개 {freed}바이트)"
    assert len(after) < len(before) + 1, "회수 뒤에 파일이 줄지 않았다"
    print(f"  ⑫ 보관 회수: 남은 버전·공유 파일·런처를 지키고 {gone}개 회수 ✅")

    # ── ⑬ dry-run 은 아무것도 지우지 않는다 ─────────────────────────────
    snap = store_digests()
    msnap = sorted(M.manifest_versions(gc_out))
    M.collect_garbage(gc_out, M.RELEASE, keep=1, dry=True)
    assert store_digests() == snap, "dry-run 이 파일을 지웠다"
    assert sorted(M.manifest_versions(gc_out)) == msnap, "dry-run 이 매니페스트를 지웠다"
    print("  ⑬ dry-run 은 지우지 않는다 ✅")

    # ── ⑭ 런처 재배포가 최소 버전 강제를 조용히 풀면 안 된다 ────────────
    #
    # `--min v1.2.0` 으로 한 번 잠가 두고 다음에 그냥 올리면, 예전 방식은
    # `minLauncher: ""` 로 덮어써서 **옛 런처가 다시 통과했다.** 아무 오류도 안 난다.
    # (`!364` AI 리뷰가 잡았다.)
    import publish_launcher as PL  # noqa: E402

    pl_out = os.path.join(tmp, "pl")
    fake = os.path.join(tmp, "fake-launcher.exe")
    io.open(fake, "wb").write(b"L" * 500)

    assert PL.main([fake, "v1.0.0", "--out", pl_out]) == 0
    assert load(os.path.join(pl_out, "launcher.json"))["minLauncher"] == ""

    # 잠근다
    assert PL.main([fake, "v1.1.0", "--out", pl_out, "--min", "v1.1.0"]) == 0
    assert load(os.path.join(pl_out, "launcher.json"))["minLauncher"] == "v1.1.0"

    # **그냥 올리면 그대로 있어야 한다** — 여기가 조용히 풀리던 자리다
    assert PL.main([fake, "v1.2.0", "--out", pl_out]) == 0
    j = load(os.path.join(pl_out, "launcher.json"))
    assert j["version"] == "v1.2.0", j["version"]
    assert j["minLauncher"] == "v1.1.0", (
        f"재배포가 최소 버전 강제를 풀었다: {j['minLauncher']!r} — 옛 런처가 다시 통과한다")

    # 내리는 것은 **명시해야** 한다
    assert PL.main([fake, "v1.3.0", "--out", pl_out, "--min-clear"]) == 0
    assert load(os.path.join(pl_out, "launcher.json"))["minLauncher"] == ""

    # 둘을 같이 주면 거절한다 — 무엇을 원하는지 알 수 없다
    assert PL.main([fake, "v1.4.0", "--out", pl_out, "--min", "v1.4.0", "--min-clear"]) == 1

    # 올리는 버전보다 높은 최소 버전은 **방금 올린 런처도 거부한다**
    assert PL.main([fake, "v1.5.0", "--out", pl_out, "--min", "v9.9.9"]) == 1
    print("  ⑭ 런처 재배포가 최소 버전 강제를 풀지 않는다 ✅")

    # ── ⑮ 채널 (S15P21B202-460) ──────────────────────────────────────────
    #
    # QA 는 두 번 일어난다: 머지 뒤(`develop`)와 머지 전(그 MR 브랜치). 그래서 채널을
    # 셋으로 나눈다. **이름이 곧 채널이다** — 두 곳에 두면 어긋나고, 어긋나면 배포
    # 런처가 dev 빌드를 집는다.
    print()
    print("== 채널 ==")

    assert M.channel_of("v1.0.0") == M.RELEASE
    assert M.channel_of("dev-20260914-1a2b3c4") == M.DEV
    assert M.channel_of("qa-401-1a2b3c4") == M.QA
    # `v` 를 빼면 릴리스가 아니다 — 태그와 같은 모양이어야 한다
    assert M.channel_of("1.0.0") is None
    for bad in ("dev20260914", "qa401", "latest", "", "v1.0"):
        assert M.channel_of(bad) is None, bad
    print("  ⑮-a 이름으로 채널을 가른다 (v 없는 릴리스·모양 틀린 것은 거부) ✅")

    ch = os.path.join(tmp, "ch")
    rel_dir = os.path.join(tmp, "ch-rel")
    dev_dir = os.path.join(tmp, "ch-dev")
    # 릴리스와 dev 가 **서로 다른 파일**을 하나씩 갖는다. 공유 파일도 하나 둔다 —
    # 회수가 공유분을 지키는지도 같이 본다.
    build(rel_dir, {"Game.exe": b"exe", "shared.dat": b"same", "only-rel.dat": b"R"})
    build(dev_dir, {"Game.exe": b"exe", "shared.dat": b"same", "only-dev.dat": b"D"})

    assert M.main([rel_dir, "v1.0.0", "--out", ch]) == 0
    assert M.main([dev_dir, "dev-20260914-1a2b3c4", "--out", ch,
                   "--branch", "develop", "--commit", "1a2b3c4"]) == 0

    # **배포 런처가 보는 자리는 바뀌지 않는다.** dev 를 올려도 latest.json 은 릴리스다.
    assert load(os.path.join(ch, "latest.json"))["version"] == "v1.0.0"
    assert load(os.path.join(ch, "dev-latest.json"))["version"] == "dev-20260914-1a2b3c4"
    rel_list = [r["version"] for r in load(os.path.join(ch, "versions.json"))["versions"]]
    dev_list = [r["version"] for r in load(os.path.join(ch, "dev-versions.json"))["versions"]]
    assert rel_list == ["v1.0.0"], rel_list
    assert dev_list == ["dev-20260914-1a2b3c4"], dev_list
    print("  ⑮-b 배포 목록과 개발 목록이 섞이지 않는다 ✅")

    # 무엇으로 만든 빌드인지 목록이 말한다
    row = load(os.path.join(ch, "dev-versions.json"))["versions"][0]
    assert row["branch"] == "develop" and row["commit"] == "1a2b3c4", row
    assert row["channel"] == M.DEV, row
    print("  ⑮-c 목록이 브랜치·커밋을 말한다 ✅")

    # **여기가 이 변경에서 가장 위험한 자리다.** 예전 `collect_garbage` 는 참조 집합을
    # `manifest_versions()`(= semver 만)에서 모았다. 그대로 뒀다면 릴리스를 한 번만 더
    # 올려도 **dev 전용 파일이 전부 회수된다** — 해시가 이름이라 되살릴 수 없다.
    dev_only = M.sha256_of(os.path.join(dev_dir, "only-dev.dat"))[0]
    rel2 = os.path.join(tmp, "ch-rel2")
    build(rel2, {"Game.exe": b"exe2", "shared.dat": b"same", "only-rel.dat": b"R"})
    assert M.main([rel2, "v1.0.1", "--out", ch, "--keep", "1"]) == 0
    assert os.path.exists(os.path.join(ch, "files", dev_only + ".gz")), (
        "릴리스를 올렸더니 dev 파일이 회수됐다 — 되살릴 수 없는 사고다")
    assert os.path.exists(os.path.join(ch, "manifest", "dev-20260914-1a2b3c4.json")), (
        "릴리스의 --keep 이 dev 매니페스트를 지웠다")
    print("  ⑮-d 릴리스 회수가 dev 파일을 지우지 않는다 ✅")

    # 반대 방향도 같다 — dev 를 매일 올려도 릴리스 보관이 밀려나지 않는다
    dev2 = os.path.join(tmp, "ch-dev2")
    build(dev2, {"Game.exe": b"exe3", "only-dev.dat": b"D"})
    assert M.main([dev2, "dev-20260915-2b3c4d5", "--out", ch, "--keep", "1"]) == 0
    assert os.path.exists(os.path.join(ch, "manifest", "v1.0.1.json")), (
        "dev 의 --keep 이 릴리스 매니페스트를 지웠다")
    dev_list = [r["version"] for r in load(os.path.join(ch, "dev-versions.json"))["versions"]]
    assert dev_list[0] == "dev-20260915-2b3c4d5", dev_list
    print("  ⑮-e dev 회수가 릴리스를 밀어내지 않고, 새것이 목록 앞에 온다 ✅")

    # qa 는 dev 와 **같은 목록**에 담긴다 — 개발 런처가 한 화면에서 고른다
    qa_dir = os.path.join(tmp, "ch-qa")
    build(qa_dir, {"Game.exe": b"exe4", "only-qa.dat": b"Q"})
    assert M.main([qa_dir, "qa-401-3c4d5e6", "--out", ch, "--keep", "5",
                   "--branch", "S15P21B202-401-x", "--commit", "3c4d5e6"]) == 0
    rows = load(os.path.join(ch, "dev-versions.json"))["versions"]
    kinds = {r["version"]: r["channel"] for r in rows}
    assert kinds.get("qa-401-3c4d5e6") == M.QA, kinds
    assert any(v.startswith("dev-") for v in kinds), kinds
    assert not any(v.startswith("v") for v in kinds), "개발 목록에 릴리스가 섞였다"
    print("  ⑮-f dev 와 qa 가 한 목록, 릴리스는 섞이지 않는다 ✅")

    print()
    print("TEST406 OK")
finally:
    shutil.rmtree(tmp, ignore_errors=True)
