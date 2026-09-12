#!/usr/bin/env bash
# 배포 러너에 **릴리스 산출물·사이트 폴더**를 마운트한다 (S15P21B202-405).
#
# 이것이 있어야 `publish-builds` · `publish-site` 잡이 nginx 가 내보내는 자리에
# 쓸 수 있다. 없으면 두 잡이 "마운트가 없다" 고 사유를 말하고 멈춘다.
#
# **왜 공용 러너가 아니라 배포 러너인가.** 공용 러너에 `/var/www` 를 rw 로 주면
# **아무 MR 잡이나 배포 폴더에 쓸 수 있다** — `-252` 가 스풀을 배포 전용 러너로
# 뺀 이유와 같다. 배포 러너는 `ref_protected` 라 master·develop 의 잡만 집는다.
#
# **rw 마운트에는 데이터만 둔다**(`-324`). nginx 는 그것을 정적으로 내보내고
# 실행하지 않는다.
#
# ── 컨테이너 안 경로를 `/builds` 로 두면 안 된다 (S15P21B202-465) ───────────
#
# **`/builds` 는 GitLab Runner 가 저장소를 클론하는 기본 작업 디렉터리다.**
# `config.toml` 에 `builds_dir` 이 없으면 도커 executor 는 컨테이너 안
# `/builds/<러너해시>/<번호>/<그룹>/<프로젝트>` 에 소스를 푼다.
#
# 처음에 `"/var/www/metagame-builds:/builds:rw"` 로 물렸더니, 배포 태그가 붙은 잡이
# 돌 때마다 **저장소 전체가 공개 다운로드 폴더에 떨어졌다.** 2026-09-12 에
# `https://j15b202.p.ssafy.io/builds/<해시>/0/…/README.md` 가 HTTP 200 으로
# 받아지는 것을 실측했다(835 MB).
#
# 그래서 **러너가 쓰지 않는 자리**(`/publish/...`)로 옮긴다. 잡은 `BUILDS_DIR` ·
# `SITE_DIR` 로 그 경로를 받는다.
#
# 실행: sudo bash scripts/deploy/host/mount-release-dirs.sh
# 여러 번 돌려도 안전하다.
set -euo pipefail

RUNNER_CFG=/etc/gitlab-runner/config.toml
BUILDS=/var/www/metagame-builds
SITE=/var/www/metagame-site

# 컨테이너 안에서 보일 자리. **러너가 쓰는 경로와 겹치면 안 된다**(아래 RESERVED).
BUILDS_AT="${BUILDS_AT:-/publish/builds}"
SITE_AT="${SITE_AT:-/publish/site}"

# 러너·도커가 이미 쓰는 자리. 여기에 물리면 잡의 작업 파일이 우리 폴더로 쏟아진다.
RESERVED="/builds /cache /tmp /var /etc /usr /root /home"

[ "$(id -u)" -eq 0 ] || { echo "root 로 실행하세요 (sudo)"; exit 1; }

# ── 겹침 검사 — 이것이 이 스크립트에서 가장 중요한 줄이다 ────────────────────
#
# 여기서 막지 않으면 **공개 폴더가 러너 작업 디렉터리가 되고**, 저장소가 무인증으로
# 새어 나간다(S15P21B202-465 에서 실제로 일어났다). 사람이 경로를 바꿀 때 이 검사가
# 마지막 방어선이다.
for at in "$BUILDS_AT" "$SITE_AT"; do
  for bad in $RESERVED; do
    if [ "$at" = "$bad" ] || case "$at" in "$bad"/*) true ;; *) false ;; esac; then
      echo "❌ 컨테이너 경로 '$at' 는 러너·시스템이 쓰는 '$bad' 와 겹칩니다."
      echo "   그대로 두면 잡이 그 폴더에 저장소를 풀고, 공개 주소로 새어 나갑니다."
      echo "   (S15P21B202-465 — 실제로 저장소 835 MB 가 무인증으로 받아졌습니다)"
      echo "   BUILDS_AT · SITE_AT 를 /publish/... 같은 자리로 두세요."
      exit 1
    fi
  done
done

echo "[1/3] 폴더 확인"
# `install-dev.sh` 가 이미 만들었을 수 있다 — 같은 소유·권한으로 맞춘다.
install -d -o ubuntu -g www-data -m 2775 "$BUILDS" "$SITE"
ls -ld "$BUILDS" "$SITE"

echo "[2/3] 배포 러너 volumes 에 추가"
if [ ! -f "$RUNNER_CFG" ]; then
  echo "   러너 설정이 없다 → 건너뜀"
elif grep -q "$BUILDS:$BUILDS_AT" "$RUNNER_CFG" && grep -q "$SITE:$SITE_AT" "$RUNNER_CFG" \
     && ! grep -qE "$BUILDS:/builds:|$SITE:/site:" "$RUNNER_CFG"; then
  # **옛 마운트가 같이 남아 있으면 "이미 있음" 이 아니다** (S15P21B202-465 AI 리뷰).
  # 새 경로만 보고 여기서 빠져나가면 옛 `:/builds:rw` 가 그대로 남아 **계속 샌다.**
  # 둘 다 있는 상태는 앞선 실행이 중간에 끊겼을 때 생긴다.
  echo "   이미 마운트돼 있음"
else
  python3 - "$RUNNER_CFG" "$BUILDS:$BUILDS_AT" "$SITE:$SITE_AT" <<'PY'
import re
import sys

cfg, builds_mount, site_mount = sys.argv[1], sys.argv[2], sys.argv[3]
builds_at = builds_mount.split(":", 1)[1]
site_at = site_mount.split(":", 1)[1]
text = open(cfg).read()

# **러너를 이름으로 찾는다.** `install.sh` 는 첫 `volumes` 줄을 고치는데
# config.toml 에는 공용 러너가 먼저 온다 — 그 줄을 고치면 아무 MR 잡이나
# 배포 폴더를 보게 되어 -252 가 막은 구멍이 다시 열린다.
blocks = []
for m in re.finditer(r'^\[\[runners\]\]', text, re.M):
    start = m.start()
    nxt = re.search(r'^\[\[runners\]\]', text[start + 1:], re.M)
    end = start + 1 + nxt.start() if nxt else len(text)
    blocks.append((start, end))

targets = [(s, e) for s, e in blocks
           if re.search(r'name\s*=\s*"[^"]*-deploy-[^"]*"', text[s:e])]
if len(targets) != 1:
    print(f"   ❌ '-deploy-' 러너를 하나로 특정하지 못했다 ({len(targets)}개) — 손으로 고쳐라")
    sys.exit(1)

s, e = targets[0]
block = text[s:e]
m = re.search(r'^(\s*)volumes\s*=\s*\[(.*)\]\s*$', block, re.M)
if not m:
    print("   ❌ 그 러너에 volumes 줄이 없다 — 손으로 고쳐라")
    sys.exit(1)

indent, inner = m.group(1), m.group(2)
add = []
if builds_at not in inner:
    add.append(f'"{builds_mount}:rw"')
if site_at not in inner:
    add.append(f'"{site_mount}:rw"')

# **옛 `/builds` 마운트가 남아 있으면 지운다** (S15P21B202-465). 남겨 두면 러너가
# 계속 그 자리에 저장소를 푼다 — 새 마운트를 더하는 것만으로는 새는 곳이 안 막힌다.
stale = [x.strip() for x in inner.split(",")
         if x.strip().strip('"').endswith((":/builds:rw", ":/site:rw"))]
if stale:
    kept = [x.strip() for x in inner.split(",") if x.strip() and x.strip() not in stale]
    inner = ", ".join(kept)
    print("   옛 마운트 제거: " + " · ".join(stale))
if not add and not stale:
    print("   이미 있음")
    sys.exit(0)

items = [x.strip() for x in inner.split(",") if x.strip()] + add
new_line = f'{indent}volumes = [' + ", ".join(items) + ']'
text = text[:s] + block.replace(m.group(0), new_line, 1) + text[e:]
open(cfg, "w").write(text)
print("   배포 러너 volumes 에 " + " · ".join(add) + " 추가")
PY
  if [ $? -eq 0 ]; then
    # **도는 잡이 없을 때 돌려라.** 재시작은 진행 중인 잡을 죽인다.
    systemctl restart gitlab-runner
    echo "   러너 재시작"
  fi
fi

echo "[3/3] 확인"
grep -A1 '\-deploy\-' "$RUNNER_CFG" 2>/dev/null | grep volumes || true
echo
echo "남은 것 — 사람이 확인한다"
echo "  · 도는 CI 잡이 0개일 때 돌렸는지 (재시작이 잡을 죽인다)"
echo "  · publish-builds / publish-site 잡이 '마운트가 없다' 로 멈추지 않는지"
echo "  · 배포 잡이 한 번 돈 뒤 $BUILDS 에 **산출물만** 남는지 —"
echo "    러너 작업 디렉터리 모양(<해시>/<번호>/<그룹>/)이 생기면 경로가 또 겹친 것이다"
