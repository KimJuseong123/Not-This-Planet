"""repo-hygiene — MR 변경 파일 위생 검사. 위반 시 exit 1.

1. 금지 파일: Unity 생성물(Library/Temp/Logs/obj/Build*), IDE 파일(.csproj/.sln/.vs/.idea), OS 잡파일
2. .meta 짝: Assets/ 아래 추가 파일·폴더에는 .meta 가 함께 있어야 하고, .meta 만 남거나(고아) 파일만 지우면 실패
3. 에셋 위치: Assets/ 바로 아래 새 최상위 폴더 금지(대상 브랜치에 이미 있는 폴더 또는 _Project/ 만)
4. LFS: .gitattributes 로 LFS 추적되는 파일은 포인터여야 함(실 바이너리 커밋 = git lfs install 누락). 비-LFS 5MB 초과 실패
5. 씬/프리팹: .unity/.prefab 변경이 있는데 MR 본문 '씬 / 프리팹 변경' 이 '없음' 이면 실패
   (서드파티 패키지에서 새로 들어온 것은 제외 — 임포트마다 수십 개가 딸려 온다)
6. 경로 길이: 리포 상대 경로가 MAX_PATH_LEN 을 넘으면 실패 — Windows 러너에서 checkout 자체가 죽는다

로컬 실행: python scripts/ci/repo_hygiene.py [base-ref] [head-ref]  (기본 origin/develop 과의 merge-base .. HEAD)
"""
import glob
import os
import re
import subprocess
import sys

from ci_common import GitLab, Report, banner, env, find_section, is_release_mr, sections

FORBIDDEN = [
    (re.compile(r"^METAGAME/(Library|Temp|Logs|obj|UserSettings|Build|Builds|\.vs|\.idea)/"), "Unity/IDE 생성물"),
    (re.compile(r"\.(csproj|sln|suo|user|pidb|booproj|svd|pdb|mdb|opendb|VC\.db)$", re.I), "IDE/빌드 산출물"),
    (re.compile(r"(^|/)(Thumbs\.db|\.DS_Store|desktop\.ini)$", re.I), "OS 잡파일"),
    (re.compile(r"^(\.vs|\.idea)/"), "IDE 설정"),
    (re.compile(r"\.(apk|aab|ipa|exe|msi|zip|7z|rar)$", re.I), "빌드/압축 산출물"),
    # 인증키가 리포에 올라가면 되돌릴 수 없다(이력에 남는다). 2026-08-31 EC2 pem 이 작업 폴더에 있던 것을 발견.
    (re.compile(r"\.(pem|ppk|key|p12|pfx|jks|keystore)$", re.I), "인증키·비밀키"),
    (re.compile(r"(^|/)(id_rsa|id_ed25519|id_ecdsa|id_dsa)(\.pub)?$"), "SSH 개인키"),
]
ASSETS = "METAGAME/Assets/"
THIRDPARTY = ASSETS + "_Project/ThirdParty/"
MAX_NON_LFS = 5 * 1024 * 1024
# 리포 상대 경로 상한. Windows 러너의 빌드 경로 접두어가 약 71자이고 MAX_PATH 가 260 이라
# 180 을 넘으면 checkout 이 'Filename too long' 으로 죽는다 — 스크립트가 돌기도 전에
# 파이프라인 전체가 실패한다(2026-08-31 실측: 189자 경로가 develop 에 들어와 10건 전멸).
MAX_PATH_LEN = 180


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


def unity_ignores(path: str) -> bool:
    """Unity 가 임포트하지 않는 경로 — .meta 가 없는 것이 정상.

    점으로 시작하는 파일·폴더(.gitkeep, .DS_Store …), `~` 로 끝나는 폴더, Documentation~ 등.
    https://docs.unity3d.com/Manual/SpecialFolders.html"""
    return any(seg.startswith(".") or seg.endswith("~") for seg in path.split("/"))


def too_long(paths: list[str], limit: int = MAX_PATH_LEN) -> list[tuple[str, int]]:
    """상한을 넘는 경로를 긴 것부터. Windows 러너에서 checkout 자체가 실패하는 원인이다."""
    return sorted(((p, len(p)) for p in paths if len(p) > limit), key=lambda t: -t[1])


def location_problems(asset_added: list[str], base_top: set[str]) -> list[str]:
    """에셋 위치 위반을 **최상위 폴더 단위로 묶어** 돌려준다.

    파일마다 찍으면 서드파티 패키지를 임포트할 때 같은 문구가 수백 줄 나와 정작 다른 위반이 묻힌다
    (2026-08-31 MR !112 Mirror 임포트에서 실측)."""
    direct, folders = [], {}
    for path in asset_added:
        rel = path[len(ASSETS):]
        top = rel.split("/", 1)[0]
        if "/" not in rel:
            if not rel.endswith(".meta") or rel[:-5] not in base_top:
                direct.append(path)
        elif top not in base_top and top != "_Project" and top + ".meta" not in base_top:
            folders.setdefault(top, []).append(path)
    msgs = []
    if direct:
        shown = ", ".join(direct[:5]) + (" …" if len(direct) > 5 else "")
        msgs.append(f"Assets/ 바로 아래 파일 금지 {len(direct)}개: {shown} — Assets/_Project/ 아래로")
    known = ", ".join(sorted(t for t in base_top if not t.endswith(".meta")))
    for top, ps in sorted(folders.items()):
        msgs.append(f"Assets/ 에 새 최상위 폴더 '{top}' 금지 (파일 {len(ps)}개) — "
                    f"Assets/_Project/ 아래로 (기존 폴더: {known})")
    return msgs


# 잡 이름처럼 생긴 것만 본다. 문서 전체를 검사하려 들면 오탐이 많아 아무도 안 보게 된다.
JOB_PREFIXES = ("mr-", "mm-", "ci-", "repo-", "ai-", "unity-", "build-", "deploy-", "stack-", "jira-")
# 없어진 잡을 **역사로** 적는 것은 정당하다("한동안 X 를 뒀지만 없앴습니다"). 그것과 "X 를 쓰세요" 를
# 기계가 가를 수 없으므로 사람이 표시한다. 이 주석이 있는 줄은 검사에서 빠진다.
DRIFT_OK = "<!-- drift-ok -->"


def ci_jobs(path: str = ".gitlab-ci.yml") -> set[str]:
    """`.gitlab-ci.yml` 에 실제로 있는 잡 이름."""
    try:
        s = open(path, encoding="utf-8").read()
    except OSError:
        return set()
    return set(re.findall(r"(?m)^([a-z][a-z0-9-]*):", s)) - {
        "stages", "variables", "default", "workflow", "include"}


def ci_tags(path: str = ".gitlab-ci.yml") -> set[str]:
    """러너 태그(`tags: [linux]` 의 linux). 잡 이름이 아니지만 생김새가 같아 오탐이 된다."""
    try:
        s = open(path, encoding="utf-8").read()
    except OSError:
        return set()
    return {t.strip() for grp in re.findall(r"(?m)^\s+tags:\s*\[([^\]]*)\]", s) for t in grp.split(",") if t.strip()}


def doc_job_drift(jobs: set[str]) -> list[str]:
    """문서가 백틱으로 말하는 CI 잡 이름 중 **실제로 없는 것**.

    문서가 틀리면 사람이 없는 잡을 찾다 시간을 버리고, 더 나쁘게는 "이미 처리된 일" 로 믿고 넘어간다.
    `-139` §6 의 "develop·manual 이 막는다" 가 그랬다 — 실제로는 안 막았고, 그 문장 때문에 `-252` 의
    구멍이 한동안 검토된 위험으로 분류돼 있었다. 이번 주에 문서-사실 불일치를 6곳 찾았는데 전부
    실측하다 우연히 걸린 것이라, 사람이 아니라 CI 가 잡게 한다(-292).

    거르는 것 셋. 손으로 관리하는 예외 목록보다 오래가는 규칙만 쓴다.
      - **문서 파일 이름** — `ci-and-notifications`·`mr-flow` 처럼 접두사가 겹치는 문서 이름
      - **러너 태그** — `unity-win`·`linux`. 잡이 아니지만 생김새가 같다
      - **`docs/records/`** — 기록은 그 시점의 사실이거나 **앞으로 만들 것**을 적는다. `-284` 가
        아직 없는 `deploy-dev`·`deploy-prod` 를 설명하는 것이 정상이다. 지금 유효해야 하는 것은
        살아 있는 안내 문서(`docs/*.md`)뿐이다
      - **`<!-- drift-ok -->` 가 달린 줄** — 없어진 잡을 역사로 적는 것은 정당하다("한동안 X 를 뒀지만
        없앴습니다"). 그것과 "X 를 쓰세요" 는 기계가 가를 수 없으므로 **사람이 표시한다.** 표시가
        없으면 실패하므로, 잡을 지웠는데 문서가 그대로면 반드시 한 번은 눈에 걸린다"""
    if not jobs:
        return []
    docs = sorted(p for p in glob.glob("docs/*.md") if os.path.isfile(p))
    docnames = {os.path.splitext(os.path.basename(p))[0]
                for p in glob.glob("docs/**/*.md", recursive=True)}
    skip = jobs | docnames | ci_tags()
    bad = []
    for path in docs:
        try:
            lines = open(path, encoding="utf-8").read().splitlines()
        except OSError:
            continue
        for i, line in enumerate(lines, 1):
            if DRIFT_OK in line:
                continue
            for name in re.findall(r"`([a-z][a-z0-9-]{2,})`", line):
                if name in skip or not name.startswith(JOB_PREFIXES):
                    continue
                bad.append(f"{path}:{i} `{name}`")
    return bad


def scene_changes(changes: list[tuple[str, str]]) -> tuple[list[str], list[str]]:
    """(선언 검사 대상 씬·프리팹, 서드파티 임포트로 새로 들어와 제외한 것).

    서드파티 패키지를 넣으면 딸려 오는 프리팹이 수십 개다 — 그것까지 MR 본문에 선언하라고 하면
    규칙이 지켜지지 않는다. 이미 있던 서드파티 파일을 **수정**한 경우는 그대로 검사한다."""
    scene = [path for st, path in changes if path.lower().endswith((".unity", ".prefab"))]
    imported = {path for st, path in changes
                if st == "A" and path.startswith(THIRDPARTY) and path.lower().endswith((".unity", ".prefab"))}
    return [path for path in scene if path not in imported], sorted(imported)


def main() -> int:
    banner("repo-hygiene")
    rep = Report("repo-hygiene")
    # 릴리즈 MR(develop → master)은 건너뛴다 — 비교 기준이 master 라 develop 의 기존 파일까지
    # 전부 '추가됨' 으로 잡혀 오탐만 쌓인다. 내용은 develop 에 머지될 때 이미 검사했다 (S15P21B202-122)
    src, tgt = env("CI_MERGE_REQUEST_SOURCE_BRANCH_NAME", ""), env("CI_MERGE_REQUEST_TARGET_BRANCH_NAME", "")
    if is_release_mr(src, tgt):
        rep.ok(f"릴리즈 MR ({src} → {tgt}) — 위생 검사 건너뜀 (내용은 develop 에 머지될 때 이미 검사됨)")
        return rep.finish()

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
    #    점(.)으로 시작하는 파일·폴더는 Unity 가 임포트하지 않아 .meta 가 생기지 않는다(.gitkeep 등) → 검사 대상에서 뺀다.
    #    (2026-08-28 MR !68 에서 .gitkeep 14건이 '.meta 누락' 으로 잡힌 오탐, S15P21B202-122)
    meta_problems = 0
    asset_added = [p for p in added if p.startswith(ASSETS) and not unity_ignores(p)]
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
        if p.startswith(ASSETS) and not p.endswith(".meta") and not unity_ignores(p) and tree_has(head, p + ".meta"):
            rep.fail(f"파일만 삭제되고 .meta 가 남음: {p}.meta"); meta_problems += 1
    if asset_added and not meta_problems:
        rep.ok(f"Assets 추가 {len(asset_added)}개 .meta 짝 OK")

    # 3) 에셋 위치 — 새 최상위 폴더 금지
    # splitlines() — 폴더명에 공백이 있으면 split() 이 쪼개 버린다. 그리고 ls-tree 가 실패하면
    # (base 에 Assets 트리가 없는 경우 등) 빈 집합이 되어 모든 최상위 폴더가 '새 폴더' 로 실패한다(-134).
    base_ls = git("ls-tree", "--name-only", f"{base}:{ASSETS}", check=False)
    base_top = set(base_ls.splitlines())
    loc_checkable = bool(base_top)
    if asset_added and not loc_checkable:
        rep.warn(f"비교 기준({base[:12]})에 {ASSETS} 트리가 없어 에셋 위치 검사를 건너뜁니다")
    loc_msgs = location_problems(asset_added, base_top) if loc_checkable else []
    for m in loc_msgs:
        rep.fail(m)
    loc_bad = len(loc_msgs)
    # 통과 판정을 fail 메시지의 접두사로 하고 있었다 — 문구만 바꿔도 조용히 잘못된 ok 가 찍힌다(-134)
    if asset_added and loc_checkable and not loc_bad:
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

    # 5) 경로 길이 — 추가되는 경로만 본다. 이미 리포에 있던 긴 경로로 그 파일을 스치는 MR 을 막으면 안 된다.
    long_paths = too_long(added)
    for path, ln in long_paths[:5]:
        rep.fail(f"경로가 {ln}자 (상한 {MAX_PATH_LEN}): {path} — Windows 러너에서 checkout 이 'Filename too long' 으로 실패해 파이프라인 전체가 죽는다. 폴더·파일 이름을 줄이세요")
    if len(long_paths) > 5:
        rep.fail(f"…그 밖에 {len(long_paths) - 5}개 더 (가장 긴 것 {long_paths[0][1]}자)")
    if added and not long_paths:
        rep.ok(f"경로 길이 OK (가장 긴 것 {max((len(p) for p in added), default=0)}자 / 상한 {MAX_PATH_LEN})")

    # 6) 문서가 없어진 CI 잡을 설명하는지 (-292). diff 와 무관하게 항상 본다 —
    #    잡을 지운 MR 이 문서를 안 고쳐도, 그 뒤 아무 MR 에서나 걸린다.
    drift = doc_job_drift(ci_jobs())
    if drift:
        for d in drift:
            rep.fail(f"문서가 없는 CI 잡을 말합니다: {d} — 문서를 고치거나, 역사 서술이면 그 줄 끝에 {DRIFT_OK}")
    else:
        rep.ok("문서가 말하는 CI 잡이 모두 실재함")

    # 7) 씬/프리팹 선언
    scene_like, tp_imported = scene_changes(changes)
    if tp_imported:
        rep.ok(f"서드파티 패키지에서 새로 들어온 씬·프리팹 {len(tp_imported)}개는 선언 검사 제외")
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
