#!/usr/bin/env bash
# EC2 호스트에 배포 장치를 설치한다 (S15P21B202-141 · 위치 정정 -324)
#   sudo bash scripts/deploy/host/install.sh
# 하는 일: 스풀(데이터) 생성 · deploy.sh 를 **스풀 밖**에 배치 · systemd path/service 등록 ·
#          러너 config.toml 에 스풀 마운트 추가(있으면 건너뜀)
#
# 경계: 스풀은 배포 러너 컨테이너에 rw 로 마운트된다(잡이 jar·RELEASE 를 놓는 자리). 그래서
#   **실행되는 코드는 스풀 안에 두지 않는다** — 안에 두면 그 러너에서 도는 잡이 deploy.sh 를
#   덮어쓸 수 있고 호스트가 그것을 docker 권한으로 실행한다(-324). 컨테이너는 데이터만 쓰고,
#   해석해 실행하는 코드는 root 소유로 마운트 밖에 있다.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
APP=/home/ubuntu/metagame
SPOOL=$APP/deploy
CODE=/opt/metagame-deploy
RUNNER_CFG=/etc/gitlab-runner/config.toml

echo "[1/5] 스풀 디렉터리 (데이터만 — 컨테이너에 rw 로 보인다)"
install -d -o ubuntu -g ubuntu "$SPOOL" "$SPOOL/previous"

echo "[2/5] 배포 스크립트 — 스풀 밖, root 소유 (-324)"
install -d -o root -g root -m 755 "$CODE"
install -o root -g root -m 755 "$HERE/deploy.sh" "$CODE/deploy.sh"
if [ -e "$SPOOL/deploy.sh" ]; then
  rm -f "$SPOOL/deploy.sh"
  echo "   옛 위치 제거: $SPOOL/deploy.sh (컨테이너가 덮어쓸 수 있는 자리였다)"
fi
echo "   $(ls -l "$CODE/deploy.sh")"

echo "[3/5] systemd 유닛"
install -m 644 "$HERE/metagame-deploy.service" /etc/systemd/system/
install -m 644 "$HERE/metagame-deploy.path" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now metagame-deploy.path
systemctl status metagame-deploy.path --no-pager | head -4

echo "[4/5] 러너에 스풀 마운트"
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

echo "[5/5] 설치 확인 (컨테이너를 건드리지 않는 dry-run)"
sudo -u ubuntu bash -c "printf '{\"sha\":\"install-check\"}' > $SPOOL/RELEASE"
sleep 8
tail -6 "$SPOOL/deploy.log" 2>/dev/null || echo "   로그 없음"
echo "   STATUS: $(cat "$SPOOL/STATUS" 2>/dev/null || echo '없음')"
echo ""
echo "완료. dry-run 으로 확인하려면: sudo -u ubuntu env DRY_RUN=1 $CODE/deploy.sh"
echo "격리 확인: 스풀에 실행 파일이 없어야 한다 →  ls -l $SPOOL"
