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
# 실행: sudo bash scripts/deploy/host/mount-release-dirs.sh
# 여러 번 돌려도 안전하다.
set -euo pipefail

RUNNER_CFG=/etc/gitlab-runner/config.toml
BUILDS=/var/www/metagame-builds
SITE=/var/www/metagame-site

[ "$(id -u)" -eq 0 ] || { echo "root 로 실행하세요 (sudo)"; exit 1; }

echo "[1/3] 폴더 확인"
# `install-dev.sh` 가 이미 만들었을 수 있다 — 같은 소유·권한으로 맞춘다.
install -d -o ubuntu -g www-data -m 2775 "$BUILDS" "$SITE"
ls -ld "$BUILDS" "$SITE"

echo "[2/3] 배포 러너 volumes 에 추가"
if [ ! -f "$RUNNER_CFG" ]; then
  echo "   러너 설정이 없다 → 건너뜀"
elif grep -q "$BUILDS:/builds" "$RUNNER_CFG" && grep -q "$SITE:/site" "$RUNNER_CFG"; then
  echo "   이미 마운트돼 있음"
else
  python3 - "$RUNNER_CFG" "$BUILDS" "$SITE" <<'PY'
import re
import sys

cfg, builds, site = sys.argv[1], sys.argv[2], sys.argv[3]
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
if "/builds" not in inner:
    add.append(f'"{builds}:/builds:rw"')
if "/site" not in inner:
    add.append(f'"{site}:/site:rw"')
if not add:
    print("   이미 있음")
    sys.exit(0)

new_line = f'{indent}volumes = [{inner}, ' + ", ".join(add) + ']'
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
