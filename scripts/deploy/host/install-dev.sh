#!/usr/bin/env bash
# EC2 호스트에 **개발** 배포 장치를 설치한다 (S15P21B202-284)
#   sudo bash scripts/deploy/host/install-dev.sh
#
# prod 를 건드리지 않는다. 하는 일은 덧붙이기뿐이다 —
#   dev 스풀 생성 · dev systemd path/service 등록 · 배포 러너에 dev 스풀 마운트 추가
#
# deploy.sh 는 **복사하지 않는다.** prod 와 같은 파일을 환경변수로 갈아 끼운다
# (dev 유닛이 APP_DIR·HEALTH_PORT·ENV_NAME 을 준다). 복사본을 두면 한쪽이 조용히 낡는다.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
DEV_APP=/home/ubuntu/metagame-dev
DEV_SPOOL=$DEV_APP/deploy
CODE=/opt/metagame-deploy
RUNNER_CFG=/etc/gitlab-runner/config.toml
BUILDS=/var/www/metagame-builds

echo "[1/6] dev 스풀 생성 — $DEV_SPOOL"
install -d -o ubuntu -g ubuntu "$DEV_SPOOL" "$DEV_SPOOL/previous"

echo "[2/6] dev 앱 폴더 — compose 와 .env"
install -d -o ubuntu -g ubuntu "$DEV_APP"
# compose 파일과 Dockerfile·.env 는 prod 쪽에서 가져온다. **같은 비밀을 쓴다** —
# DB 는 볼륨이 갈리므로 데이터는 섞이지 않고, 값을 두 벌 관리하면 한쪽이 낡는다.
if [ ! -f "$DEV_APP/docker-compose.yml" ]; then
  echo "   ⚠ $DEV_APP/docker-compose.yml 이 없다."
  echo "     compose.dev.yml 을 그 자리에 두고, prod 의 Dockerfile·.env 를 함께 복사해야 한다:"
  echo "       cp $HERE/../server/compose.dev.yml $DEV_APP/docker-compose.yml"
  echo "       cp /home/ubuntu/metagame/Dockerfile /home/ubuntu/metagame/.env $DEV_APP/"
  echo "     (이 스크립트는 남의 비밀을 옮기지 않는다 — 사람이 확인하고 복사한다)"
else
  echo "   이미 있음"
fi

echo "[3/6] dev systemd 유닛 등록"
install -m 644 "$HERE/metagame-deploy-dev.service" /etc/systemd/system/
install -m 644 "$HERE/metagame-deploy-dev.path" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now metagame-deploy-dev.path
systemctl status metagame-deploy-dev.path --no-pager | head -4

echo "[4/6] 배포 산출물 폴더 — $BUILDS (S15P21B202-405)"
# nginx 가 정적으로 내보내는 자리. **데이터만** 둔다(-324) — 실행되는 것은 두지 않는다.
install -d -o ubuntu -g www-data -m 2775 "$BUILDS"

echo "[5/6] 배포 러너에 dev 스풀 마운트"
# **러너를 이름으로 찾는다.** install.sh 는 첫 `volumes` 줄을 고치는데, config.toml 에는
# 공용 러너가 먼저 온다 — 그 줄을 고치면 아무 MR 잡이나 스풀을 보게 되어 -252 가 막은
# 구멍이 다시 열린다. 그래서 여기서는 `-deploy-` 가 든 러너 블록만 건드린다.
if [ ! -f "$RUNNER_CFG" ]; then
  echo "   러너 설정이 없다 → 건너뜀"
elif grep -q "$DEV_SPOOL:/spool-dev" "$RUNNER_CFG"; then
  echo "   이미 마운트돼 있음"
else
  python3 - "$RUNNER_CFG" "$DEV_SPOOL" <<'PY'
import re
import sys

cfg, spool = sys.argv[1], sys.argv[2]
text = open(cfg).read()

# [[runners]] 블록으로 자르고, name 에 '-deploy-' 가 든 것만 고른다.
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
if '/spool-dev' in m.group(2):
    print("   이미 있음")
    sys.exit(0)

new_block = (block[:m.start()]
             + f'{m.group(1)}volumes = [{m.group(2)}, "{spool}:/spool-dev:rw"]'
             + block[m.end():])
open(cfg, 'w').write(text[:s] + new_block + text[e:])
print("   배포 러너 volumes 에 /spool-dev 추가")
PY
  if [ $? -eq 0 ]; then systemctl restart gitlab-runner; fi
fi

echo "[6/6] 설치 확인 (컨테이너를 건드리지 않는 dry-run)"
APP_DIR=$DEV_APP HEALTH_PORT=8081 ENV_NAME=dev DRY_RUN=1 \
  bash -c "printf '{\"sha\":\"install-dev\"}' > $DEV_SPOOL/RELEASE; $CODE/deploy.sh" || true
echo "   결과: $(cat "$DEV_SPOOL/STATUS" 2>/dev/null || echo '없음')"
echo
echo "남은 것 — 사람이 확인한다"
echo "  · $DEV_APP 에 docker-compose.yml(compose.dev.yml) · Dockerfile · .env 가 있는지"
echo "  · nginx 설정을 갱신하고 'nginx -t' 뒤 reload — /dev/ 와 /builds/ 가 생긴다"
echo "  · 첫 기동: cd $DEV_APP && docker compose -p metagame-dev up -d --build"
echo "  · 확인: curl -s -o /dev/null -w '%{http_code}\\n' -X POST http://127.0.0.1:8081/auth/guest \\"
echo "            -H 'Content-Type: application/json' -d '{\"deviceId\":\"x\",\"nickname\":\"health\"}'"
