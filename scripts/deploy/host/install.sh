#!/usr/bin/env bash
# EC2 호스트에 배포 장치를 설치한다 (S15P21B202-141)
#   sudo bash scripts/deploy/host/install.sh
# 하는 일: 스풀 디렉터리 생성 · deploy.sh 배치 · systemd path/service 등록 ·
#          러너 config.toml 에 스풀 마운트 추가(있으면 건너뜀)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
APP=/home/ubuntu/metagame
SPOOL=$APP/deploy
RUNNER_CFG=/etc/gitlab-runner/config.toml

echo "[1/4] 스풀 디렉터리"
install -d -o ubuntu -g ubuntu "$SPOOL" "$SPOOL/previous"
install -o ubuntu -g ubuntu -m 755 "$HERE/deploy.sh" "$SPOOL/deploy.sh"

echo "[2/4] systemd 유닛"
install -m 644 "$HERE/metagame-deploy.service" /etc/systemd/system/
install -m 644 "$HERE/metagame-deploy.path" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now metagame-deploy.path
systemctl status metagame-deploy.path --no-pager | head -4

echo "[3/4] 러너에 스풀 마운트"
if [ -f "$RUNNER_CFG" ] && ! grep -q "$SPOOL:/spool" "$RUNNER_CFG"; then
  python3 - "$RUNNER_CFG" "$SPOOL" <<'PY'
import re, sys
cfg, spool = sys.argv[1], sys.argv[2]
s = open(cfg).read()
m = re.search(r'^(\s*)volumes\s*=\s*\[(.*)\]\s*$', s, re.M)
assert m, "volumes 줄을 찾지 못했다"
if "/spool" not in m.group(2):
    new = f'{m.group(1)}volumes = [{m.group(2)}, "{spool}:/spool:rw"]'
    s = s[:m.start()] + new + s[m.end():]
    open(cfg, "w").write(s)
    print("   volumes 에 스풀 추가")
else:
    print("   이미 있음")
PY
  systemctl restart gitlab-runner
else
  echo "   러너 설정이 없거나 이미 마운트돼 있음 → 건너뜀"
fi

echo "[4/4] 설치 확인 (컨테이너를 건드리지 않는 dry-run)"
sudo -u ubuntu bash -c "printf '{\"sha\":\"install-check\"}' > $SPOOL/RELEASE"
sleep 8
tail -6 "$SPOOL/deploy.log" 2>/dev/null || echo "   로그 없음"
echo "   STATUS: $(cat "$SPOOL/STATUS" 2>/dev/null || echo '없음')"
echo ""
echo "완료. dry-run 으로 확인하려면: sudo -u ubuntu env DRY_RUN=1 $SPOOL/deploy.sh"
