"""공개 폴더를 러너가 쓰는 자리에 물리지 않는지 (S15P21B202-465).

실행: python scripts/ci/tests/test_mount_paths.py
네트워크·서버 없이 돈다. 저장소의 스크립트·설정을 글자로 읽는다.

**왜 이 검사가 있나.** 2026-09-12 에 공개 다운로드 폴더를 배포 러너의 `/builds` 에
물렸더니, 배포 잡이 돌 때마다 **저장소 전체가 그 폴더에 떨어졌다.**
`https://…/builds/<러너해시>/0/<그룹>/<프로젝트>/README.md` 가 무인증으로 받아졌다
(835 MB). `/builds` 는 GitLab Runner 가 저장소를 클론하는 **기본 작업 디렉터리**다.

접근 로그상 외부에서 받아 간 흔적은 없었다. 그러나 **"아무도 안 왔다" 는 운이지
설계가 아니다.** 같은 실수가 다시 나오지 않게 규칙을 글자로 잠근다.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
os.chdir(ROOT)

NL = chr(10)
MOUNT_SH = "scripts/deploy/host/mount-release-dirs.sh"
NGINX = "scripts/deploy/server/nginx/metagame.conf"

sh = open(MOUNT_SH, encoding="utf-8").read()
code = "\n".join(ln for ln in sh.splitlines() if not ln.lstrip().startswith("#"))

print()
print("== 마운트 경로 ==")


# ── ① 컨테이너 안 경로가 러너가 쓰는 자리와 겹치면 안 된다 ───────────────
#
#     `/builds` 는 러너의 기본 `builds_dir` 이다. 거기에 공개 폴더를 물리면 잡이
#     돌 때마다 저장소가 공개된다 — **이 이슈가 실제로 겪은 일이다.**
#     **항상 참이 되는 단정을 쓰지 않는다.** 첫 판은
#     `bad not in code or "stale" in code` 였는데 `"stale"` 이 늘 들어 있어
#     **무엇을 넣어도 통과**했다(리뷰가 잡았다). 검사할 것은 "옛 경로를
#     **마운트 대상으로** 쓰지 않는다" 이므로, 그 문자열이 나오는 자리가
#     옛 마운트를 걷어 내는 줄뿐인지를 본다.
for bad in (':/builds:rw', ':/site:rw'):
    uses = [ln for ln in code.splitlines()
            if bad in ln and "stale" not in ln and "endswith" not in ln]
    assert not uses, f"컨테이너 경로가 러너가 쓰는 자리다: {bad}" + NL + NL.join(uses)

m = re.search(r'BUILDS_AT="\$\{BUILDS_AT:-([^}]+)\}"', code)
assert m, "BUILDS_AT 를 정하지 않는다"
builds_at = m.group(1)
assert not builds_at.startswith("/builds"), \
    f"BUILDS_AT 가 러너의 작업 디렉터리와 겹친다: {builds_at}"

m = re.search(r'SITE_AT="\$\{SITE_AT:-([^}]+)\}"', code)
assert m and not m.group(1).startswith("/site"), "SITE_AT 가 예전 자리 그대로다"
print(f"  컨테이너 경로가 {builds_at} · {m.group(1)} 로 러너와 갈라져 있다 ✅")


# ── ② 설치 스크립트가 겹치는 경로를 **거부**한다 ─────────────────────────
#
#     기본값만 바꾸면 다음 사람이 `BUILDS_AT=/builds` 로 되돌릴 수 있다.
#     그때 막는 것이 이 검사가 아니라 스크립트 자신이어야 한다.
assert "RESERVED" in code, "예약 경로 목록이 없다"
assert "/builds" in code.split("RESERVED", 1)[1][:200], \
    "예약 목록에 /builds 가 없다 — 이 사고를 낸 바로 그 경로다"
assert "exit 1" in code, "겹쳐도 그냥 진행한다"
print("  겹치는 경로를 설치 스크립트가 거부한다 ✅")


# ── ③ 옛 마운트를 걷어 낸다 ──────────────────────────────────────────────
#
#     새 경로를 더하기만 하면 **옛 `/builds` 마운트가 남아 계속 샌다.**
#     이미 물려 있는 서버에서 이 스크립트를 다시 돌리는 것이 복구 절차다.
assert "stale" in code, "옛 마운트를 걷어 내지 않는다 — 다시 돌려도 안 고쳐진다"
print("  옛 마운트를 걷어 낸다 ✅")


# ── ④ 잡이 쓰는 기본 경로도 같이 옮겨야 한다 ─────────────────────────────
#
#     스크립트 기본값이 `/builds` 로 남아 있으면, 마운트를 옮긴 순간 잡이
#     **마운트가 없다** 며 멈춘다.
pb = open("scripts/ci/publish_builds.py", encoding="utf-8").read()
ps = open("scripts/ci/publish_site.py", encoding="utf-8").read()
assert 'env("BUILDS_DIR", "/builds")' not in pb, "publish_builds 기본값이 옛 자리다"
assert 'env("SITE_DIR", "/site")' not in ps, "publish_site 기본값이 옛 자리다"
assert builds_at in pb, f"publish_builds 기본값이 마운트 자리({builds_at})와 다르다"
print("  잡의 기본 경로가 마운트 자리와 같다 ✅")


# ── ⑤ nginx 가 산출물만 내보낸다 ─────────────────────────────────────────
#
#     마운트를 옮겨도 **누가 그 폴더에 무언가를 풀면 또 샌다.** 내보낼 것을
#     정해 두면 두 곳이 동시에 틀려야 새어 나간다.
#
#     막는 목록이 아니라 **여는 목록**이어야 한다 — 막는 목록은 예상하지 못한
#     모양을 놓친다. 이번 사고의 `<해시>/0/<그룹>/…` 이 바로 그런 모양이었다.
conf = open(NGINX, encoding="utf-8").read()
allow = re.search(r"location ~ \^/builds/\(\?<artifact>([^)]+)\)\$", conf)
assert allow, "nginx 에 여는 목록이 없다 — 폴더 전체가 그대로 나간다"
#     **런처가 받는 것이 전부 들어 있어야 한다.** 하나라도 빠지면 그 경로가 404 가
#     되어 런처가 멈춘다. `versions.json` 은 첫 판에서 빠져 있었다 — 개발 런처가
#     그것을 받는다(`DevMode.cs`).
for must in ("latest", "versions", "launcher", "manifest/", "files/", "zip"):
    assert must in allow.group(1), f"런처가 받는 {must} 가 여는 목록에 없다"

#     **접두 블록이 404 여야 여는 목록이 뜻을 갖는다.** 거기 `alias` 가 남아 있으면
#     정규식에 안 걸린 요청이 그대로 서빙된다 — 첫 판이 그랬고 리뷰가 잡았다.
prefix = conf.split("location /builds/ {", 1)[1].split("}", 1)[0]
assert "return 404" in prefix, \
    "접두 /builds/ 블록이 파일을 서빙한다 — 여는 목록이 아무것도 막지 못한다"
assert "alias" not in prefix, "접두 블록에 alias 가 남아 있다"
print("  nginx 가 산출물만 내보내고 나머지는 404 다 ✅")


# ── ⑥ 목록은 꺼져 있어야 한다 ────────────────────────────────────────────
#
#     `autoindex on` 이 이 사고의 증폭기였다 — 주소를 몰라도 목록에서 눌러
#     들어갈 수 있었다(49분간).
#     목록(`autoindex`)은 접두 블록이 404 가 되면서 같이 사라진다 — 404 가 더 강하다.
assert "autoindex on" not in conf, "어딘가에서 목록을 켜고 있다"
print("  목록을 낼 자리가 없다 ✅")

print("== 통과 ==")
