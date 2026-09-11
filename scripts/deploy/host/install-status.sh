#!/usr/bin/env bash
# 상태 재기를 EC2 호스트에 설치한다 (S15P21B202-458)
#   sudo bash scripts/deploy/host/install-status.sh
#
# `install.sh` 를 고치지 않고 따로 둔 이유: 배포 장치 설치와 이쪽은 고칠 이유가
# 다르다. 상태 판정 하나 바꾸려고 배포 경로를 건드리게 하지 않는다.
#
# 경계는 `install.sh` 와 같다 — **실행되는 코드는 마운트 밖, root 소유**(-324).
# 결과 파일만 러너에 **읽기 전용**으로 보인다. 그래서 잡이 상태를 위조할 수 없다.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
CODE=/opt/metagame-deploy
STATUS_DIR=/var/lib/metagame-status
RUNNER_CFG=/etc/gitlab-runner/config.toml
MOUNT="$STATUS_DIR:/ec2-status:ro"

echo "[1/4] 재기 스크립트 — 마운트 밖, root 소유"
install -d -o root -g root -m 755 "$CODE" "$STATUS_DIR"
install -o root -g root -m 755 "$HERE/ec2-status.sh" "$CODE/ec2-status.sh"
echo "   $(ls -l "$CODE/ec2-status.sh")"

echo "[2/4] systemd 타이머"
install -m 644 "$HERE/metagame-status.service" /etc/systemd/system/
install -m 644 "$HERE/metagame-status.timer" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now metagame-status.timer
systemctl start metagame-status.service
sleep 2
if [ -r "$STATUS_DIR/EC2-STATUS" ]; then
  echo "   $(ls -l "$STATUS_DIR/EC2-STATUS")"
else
  echo "   ❌ $STATUS_DIR/EC2-STATUS 가 없다 — journalctl -u metagame-status.service 를 보세요"
  exit 1
fi

# ── 러너 마운트 ──────────────────────────────────────────────────────────────
#
# **모든 러너에 건다.** 아침 카드는 공용 러너에서 도므로, 배포 러너에만 걸면 카드가
# 서버 상태를 못 본다. 한때 배포 러너에만 걸고 중간 잡이 아티팩트로 넘기게 했는데,
# 그러면 **아침 카드가 그 잡을 기다린다** — 배포 러너가 잡을 늦게 집으면 카드가
# 통째로 늦어지고, `allow_failure` 도 `timeout` 도 대기 중인 잡에는 듣지 않는다.
#
# **읽기 전용이다.** 잡은 읽기만 하면 되고, 쓰기를 열면 잡이 가짜 상태를 써 넣어
# 아침 카드를 속일 수 있다(`-252` 가 스풀에서 겪은 것과 같은 종류의 구멍이다).
echo "[3/4] 러너에 읽기 전용 마운트"
if [ ! -f "$RUNNER_CFG" ]; then
  echo "   러너 설정이 없다 → 건너뜀"
else
  BEFORE=$(grep -c "$STATUS_DIR:/ec2-status" "$RUNNER_CFG" || true)
  python3 - "$RUNNER_CFG" "$MOUNT" <<'PY'
import re
import sys

cfg, mount = sys.argv[1], sys.argv[2]
s = open(cfg, encoding="utf-8").read()
added = 0


def add(m):
    global added
    indent, inner = m.group(1), m.group(2)
    if "/ec2-status" in inner:
        return m.group(0)
    added += 1
    return f'{indent}volumes = [{inner}, "{mount}"]'


s2 = re.sub(r'^(\s*)volumes\s*=\s*\[(.*)\]\s*$', add, s, flags=re.M)
if added:
    open(cfg, "w", encoding="utf-8").write(s2)
print(f"   러너 {added}곳에 추가" if added else "   이미 다 있음")
PY
  AFTER=$(grep -c "$STATUS_DIR:/ec2-status" "$RUNNER_CFG" || true)
  if [ "$AFTER" -gt "$BEFORE" ]; then
    echo "   gitlab-runner 재시작 — 도는 잡이 있으면 끊깁니다"
    systemctl restart gitlab-runner
  fi
  grep -n "volumes" "$RUNNER_CFG" | sed 's/^/   /'
fi

echo "[4/4] 확인"
python3 -m json.tool "$STATUS_DIR/EC2-STATUS" | head -12
echo "   …"
echo ""
echo "완료. 다음 실행: $(systemctl list-timers metagame-status.timer --no-pager | sed -n 2p)"
echo "CI 잡에서는 /ec2-status/EC2-STATUS 로 보입니다(읽기 전용)."
