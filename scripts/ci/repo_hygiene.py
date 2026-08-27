"""repo-hygiene — MR 변경 파일 위생 검사. 위반 시 exit 1.

1. 금지 파일: Unity 생성물(Library/Temp/Logs/obj/Build*), IDE 파일(.csproj/.sln/.vs/.idea), OS 잡파일
2. .meta 짝: Assets/ 아래 추가 파일·폴더에는 .meta 가 함께 있어야 하고, .meta 만 남거나(고아) 파일만 지우면 실패
3. 에셋 위치: Assets/ 바로 아래 새 최상위 폴더 금지(대상 브랜치에 이미 있는 폴더 또는 _Project/ 만)
4. LFS: .gitattributes 로 LFS 추적되는 파일은 포인터여야 함(실 바이너리 커밋 = git lfs install 누락). 비-LFS 5MB 초과 실패
5. 씬/프리팹: .unity/.prefab 변경이 있는데 MR 본문 '씬 / 프리팹 변경' 이 '없음' 이면 실패

로컬 실행: python scripts/ci/repo_hygiene.py [base-ref] [head-ref]  (기본 origin/develop 과의 merge-base .. HEAD)
"""
import os
import re
import subprocess
import sys

from ci_common import GitLab, Report, banner, env, find_section, sections

FORBIDDEN = [
    (re.compile(r"^METAGAME/(Library|Temp|Logs|obj|UserSettings|Build|Builds|\.vs|\.idea)/"), "Unity/IDE 생성물"),
    (re.compile(r"\.(csproj|sln|suo|user|pidb|booproj|svd|pdb|mdb|opendb|VC\.db)$", re.I), "IDE/빌드 산출물"),
    (re.compile(r"(^|/)(Thumbs\.db|\.DS_Store|desktop\.ini)$", re.I), "OS 잡파일"),
    (re.compile(r"^(\.vs|\.idea)/"), "IDE 설정"),
    (re.compile(r"\.(apk|aab|ipa|exe|msi|zip|7z|rar)$", re.I), "빌드/압축 산출물"),
]
ASSETS = "METAGAME/Assets/"
MAX_NON_LFS = 5 * 1024 * 1024


def git(*args, check=True) -> str:
    r = subprocess.run(["git", *args], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if check and r.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} 실패: {r.stderr.strip()}")
    return r.stdout


def tree_has(ref: str, path: str) -> bool:
    return subprocess.run(["git", "cat-file", "-e", f"{ref}:{path}"], capture_output=True).returncode == 0


def blob_size(ref: str, path: str) -> int:
    out = git("cat-file", "-s", f"{ref}:{path}", check=False).strip()
    return int(out) if out.isdigit() else 0


def is_lfs_pointer(ref: str, path: str) -> bool:
    r = subprocess.run(["git", "cat-file", "-p", f"{ref}:{path}"], capture_output=True)
    return r.stdout[:60].startswith(b"version https://git-lfs.github.com/spec/v1")


def lfs_tracked(path: str) -> bool:
    out = git("check-attr", "filter", "--", path, check=False)
    return out.strip().endswith(": lfs")


def main() -> int:
    banner("repo-hygiene")
    rep = Report("repo-hygiene")
    head = sys.argv[2] if len(sys.argv) > 2 else "HEAD"
    base = sys.argv[1] if len(sys.argv) > 1 else env("CI_MERGE_REQUEST_DIFF_BASE_SHA")
    if not base:
        target = env("CI_MERGE_REQUEST_TARGET_BRANCH_NAME", "develop")
        git("fetch", "-q", "origin", target, check=False)
        base = git("merge-base", head, f"origin/{target}").strip()
    else:
        # 얕은 클론이면 base 가 없을 수 있음
        if subprocess.run(["git", "cat-file", "-e", base], capture_output=True).returncode != 0:
            git("fetch", "-q", "origin", base, check=False)
    print(f"비교: {base[:12]}..HEAD")
    raw = git("diff", "--name-status", "-M", "--no-renames", base, head)
    changes = []  # (status, path)
    for line in raw.splitlines():
        if not line.strip():
            continue
        st, path = line.split("\t", 1)
        changes.append((st[0], path))
    added = [p for s, p in changes if s in ("A",)]
    modified = [p for s, p in changes if s in ("M",)]
    deleted = [p for s, p in changes if s in ("D",)]
    print(f"변경 {len(changes)}개 (추가 {len(added)} · 수정 {len(modified)} · 삭제 {len(deleted)})")

    # 1) 금지 파일
    bad = [(p, why) for s, p in changes if s != "D" for rx, why in FORBIDDEN if rx.search(p)]
    for p, why in bad:
        rep.fail(f"금지 파일({why}): {p} — .gitignore 확인, 커밋에서 제외")
    if not bad:
        rep.ok("금지 파일 없음")

    # 2) .meta 짝  (Assets 아래)
    meta_problems = 0
    asset_added = [p for p in added if p.startswith(ASSETS)]
    for p in asset_added:
        if p.endswith(".meta"):
            target = p[:-5]
            if not tree_has(head, target):
                rep.fail(f"고아 .meta: {p} (대상 {target} 없음)"); meta_problems += 1
            continue
        if not tree_has(head, p + ".meta"):
            rep.fail(f".meta 누락: {p} — Unity 에디터에서 임포트된 상태로 .meta 까지 커밋"); meta_problems += 1
        # 상위 폴더 .meta
        d = os.path.dirname(p)
        while d.startswith(ASSETS.rstrip("/")) and d != ASSETS.rstrip("/"):
            if not tree_has(head, d + ".meta"):
                rep.fail(f"폴더 .meta 누락: {d}.meta"); meta_problems += 1
            d = os.path.dirname(d)
    for p in deleted:
        if p.startswith(ASSETS) and not p.endswith(".meta") and tree_has(head, p + ".meta"):
            rep.fail(f"파일만 삭제되고 .meta 가 남음: {p}.meta"); meta_problems += 1
    if asset_added and not meta_problems:
        rep.ok(f"Assets 추가 {len(asset_added)}개 .meta 짝 OK")

    # 3) 에셋 위치 — 새 최상위 폴더 금지
    base_top = set(git("ls-tree", "--name-only", f"{base}:{ASSETS}", check=False).split())
    for p in asset_added:
        rel = p[len(ASSETS):]
        top = rel.split("/", 1)[0]
        if "/" not in rel:
            if not rel.endswith(".meta") or rel[:-5] not in base_top:
                rep.fail(f"Assets/ 바로 아래 파일 금지: {p} — Assets/_Project/ 아래로")
        elif top not in base_top and top != "_Project" and top + ".meta" not in base_top:
            rep.fail(f"Assets/ 에 새 최상위 폴더 '{top}' 금지 — Assets/_Project/ 아래로 (기존 폴더: {', '.join(sorted(t for t in base_top if not t.endswith('.meta')))})")
    if asset_added and not any(f.startswith("Assets/") for f in rep.fails):
        rep.ok("에셋 위치 OK")

    # 4) LFS / 용량
    lfs_bad = 0
    for p in added + modified:
        if not tree_has(head, p):
            continue
        tracked = lfs_tracked(p)
        if tracked and not is_lfs_pointer(head, p):
            rep.fail(f"LFS 추적 대상인데 실제 바이너리가 커밋됨: {p} — `git lfs install` 후 `git add --renormalize {p}` 로 다시 커밋"); lfs_bad += 1
        elif not tracked:
            sz = blob_size(head, p)
            if sz > MAX_NON_LFS:
                rep.fail(f"{sz/1048576:.1f}MB 비-LFS 파일: {p} — .gitattributes 에 LFS 패턴 추가"); lfs_bad += 1
    if not lfs_bad:
        rep.ok("LFS 포인터·용량 OK")

    # 5) 씬/프리팹 선언
    scene_like = [p for s, p in changes if p.lower().endswith((".unity", ".prefab"))]
    desc = GitLab().get_mr().get("description") or ""
    sec = find_section(sections(desc), "씬 / 프리팹 변경") or ""
    declared_none = re.search(r"변경한 씬·프리팹\s*:\s*없음", sec) is not None
    if scene_like:
        print("  씬/프리팹 변경: " + ", ".join(scene_like))
        if declared_none:
            rep.fail(f"씬/프리팹 {len(scene_like)}개가 바뀌었는데 MR 본문은 '변경한 씬·프리팹: 없음' — 본문 갱신 + 사전 선언 링크")
        elif not sec:
            rep.warn("MR 본문에 '씬 / 프리팹 변경' 섹션이 없어 선언 일치 확인 불가")
        else:
            rep.ok("씬/프리팹 변경이 본문에 선언됨")
    else:
        rep.ok("씬/프리팹 변경 없음")

    return rep.finish()


if __name__ == "__main__":
    raise SystemExit(main())
