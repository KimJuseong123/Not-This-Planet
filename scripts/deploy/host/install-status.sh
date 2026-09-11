#!/usr/bin/env bash
# 상태 재기를 EC2 호스트에 설치한다 (S15P21B202-458)
#   sudo bash scripts/deploy/host/install-status.sh
#
# `install.sh` 를 고치지 않고 따로 둔 이유: 배포 장치 설치는 러너를 재시작한다.
# 이쪽은 **아무것도 재시작하지 않아도 되는** 추가라, 섞어 두면 상태 하나 고치려고
# 배포 러너를 내리게 된다.
#
# 경계는 `install.sh` 와 같다 — **실행되는 코드는 스풀 밖, root 소유**(-324).
# 스풀은 배포 러너에 rw 로 보이므로 안에 둔 스크립트는 잡이 덮어쓸 수 있다.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
CODE=/opt/metagame-deploy
SPOOL=/home/ubuntu/metagame/deploy

echo "[1/3] 재기 스크립트 — 스풀 밖, root 소유"
install -d -o root -g root -m 755 "$CODE"
install -o root -g root -m 755 "$HERE/ec2-status.sh" "$CODE/ec2-status.sh"
echo "   $(ls -l "$CODE/ec2-status.sh")"

echo "[2/3] systemd 타이머"
install -m 644 "$HERE/metagame-status.service" /etc/systemd/system/
install -m 644 "$HERE/metagame-status.timer" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now metagame-status.timer

echo "[3/3] 지금 한 번 재서 확인"
systemctl start metagame-status.service
sleep 2
if [ -r "$SPOOL/EC2-STATUS" ]; then
  python3 -m json.tool "$SPOOL/EC2-STATUS" | head -20
  echo "   …"
  echo "   파일: $(ls -l "$SPOOL/EC2-STATUS")"
else
  echo "   ❌ $SPOOL/EC2-STATUS 가 없다 — journalctl -u metagame-status.service 를 보세요"
  exit 1
fi

echo ""
echo "완료. 다음 실행: $(systemctl list-timers metagame-status.timer --no-pager | sed -n 2p)"
echo "CI 쪽에서는 /spool/EC2-STATUS 로 보인다 (배포 러너에 이미 마운트돼 있다)."
