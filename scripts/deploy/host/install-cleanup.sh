#!/usr/bin/env bash
# 정리를 EC2 호스트에 설치한다 (S15P21B202-457)
#   sudo bash scripts/deploy/host/install-cleanup.sh
#
# `install.sh` 와 따로 둔 이유: 배포 장치 설치는 러너를 재시작한다. 이쪽은
# 재시작이 필요 없는 추가라, 섞어 두면 정리 하나 고치려고 배포 러너를 내리게 된다.
#
# 경계는 같다 — **실행되는 코드는 스풀 밖, root 소유**(-324).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
CODE=/opt/metagame-deploy
JOURNALD=/etc/systemd/journald.conf
JOURNAL_MAX="${JOURNAL_MAX:-500M}"

echo "[1/4] 정리 스크립트 — 스풀 밖, root 소유"
install -d -o root -g root -m 755 "$CODE"
install -o root -g root -m 755 "$HERE/ec2-cleanup.sh" "$CODE/ec2-cleanup.sh"
echo "   $(ls -l "$CODE/ec2-cleanup.sh")"

echo "[2/4] systemd 타이머 (매주 일요일 03:00)"
install -m 644 "$HERE/metagame-cleanup.service" /etc/systemd/system/
install -m 644 "$HERE/metagame-cleanup.timer" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now metagame-cleanup.timer

# ── 저널 상한 ────────────────────────────────────────────────────────────────
#
# 2026-09-11 실측: 저널이 **780 MB** 인데 `SystemMaxUse` 가 설정돼 있지 않다.
# 기본값은 파일 시스템의 10% 라, 309 GB 디스크에서는 **30 GB 까지 자란다.**
# 지금은 여유가 많아 안 보이지만, 디스크가 차기 시작하면 저널이 그 속도를 두 배로
# 만든다. 상한 하나로 끝나는 일이라 여기서 건다.
echo "[3/4] 저널 상한 ($JOURNAL_MAX)"
if grep -qE '^SystemMaxUse=' "$JOURNALD"; then
  echo "   이미 설정돼 있음: $(grep -E '^SystemMaxUse=' "$JOURNALD")"
else
  cp -n "$JOURNALD" "$JOURNALD.bak-$(date +%Y%m%d)" || true
  # 주석 처리된 줄이 있으면 그 자리에, 없으면 [Journal] 절 끝에 붙인다.
  if grep -qE '^#SystemMaxUse=' "$JOURNALD"; then
    sed -i "s/^#SystemMaxUse=.*/SystemMaxUse=$JOURNAL_MAX/" "$JOURNALD"
  else
    printf '\nSystemMaxUse=%s\n' "$JOURNAL_MAX" >> "$JOURNALD"
  fi
  systemctl restart systemd-journald
  journalctl --vacuum-size="$JOURNAL_MAX" 2>&1 | tail -2
  echo "   지금 크기: $(journalctl --disk-usage)"
fi

echo "[4/4] 지우지 않고 한 번 돌려 본다 (DRY_RUN)"
# **종료 코드를 흡수한다.** 정리 스크립트는 지켜야 할 이미지가 없으면 1, 단계가
# 실패하면 2 로 끝난다. `set -e` 아래에서 그대로 두면 **설치가 여기서 죽어**
# 타이머는 이미 등록됐는데 마지막 안내가 안 나온다 — 절반만 끝난 것처럼 보인다.
# 미리 보기가 설치를 실패시킬 이유는 없다.
DRY_RUN=1 "$CODE/ec2-cleanup.sh" 2>&1 | sed 's/^/   /' \
  || echo "   ⚠ DRY 확인에서 경고가 나왔습니다 — 위 출력을 보세요(설치는 끝났습니다)"

echo ""
echo "완료. 다음 실행: $(systemctl list-timers metagame-cleanup.timer --no-pager | sed -n 2p)"
echo "직접 돌리려면: sudo systemctl start metagame-cleanup.service"
echo "무엇을 지울지만 보려면: sudo DRY_RUN=1 $CODE/ec2-cleanup.sh"
