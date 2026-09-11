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
#
# **고친 설정을 검증하기 전에는 러너를 재시작하지 않는다.** 이 파일이 깨진 채
# 재시작하면 러너가 통째로 기동에 실패해 CI 가 멈춘다 — 백업을 두고, 파싱에
# 성공했을 때만 바꾼다.
echo "[3/4] 러너에 읽기 전용 마운트"
if [ ! -f "$RUNNER_CFG" ]; then
  echo "   러너 설정이 없다 → 건너뜀"
else
  cp -a "$RUNNER_CFG" "$RUNNER_CFG.bak-$(date +%Y%m%d-%H%M%S)"
  if python3 - "$RUNNER_CFG" "$MOUNT" <<'PY'
import os
import re
import sys
import tempfile

cfg, mount = sys.argv[1], sys.argv[2]
src = open(cfg, encoding="utf-8").read()
runners = len(re.findall(r'^\s*\[\[runners\]\]', src, re.M))
added = have = 0


def fix(m):
    """`volumes = [...]` 한 줄을 고친다. **빈 배열을 안전하게 다룬다** — 예전 판은
    `volumes = []` 에서 `volumes = [, "…"]` 를 만들어 파일을 깨뜨렸다."""
    global added, have
    indent, inner = m.group(1), m.group(2)
    items = [x.strip() for x in inner.split(",") if x.strip()]
    if any("/ec2-status" in x for x in items):
        have += 1
        return m.group(0)
    items.append(f'"{mount}"')
    added += 1
    return f'{indent}volumes = [{", ".join(items)}]'


out = re.sub(r'^(\s*)volumes\s*=\s*\[([^\[\]]*)\]\s*$', fix, src, flags=re.M)

# **손댄 러너 수가 러너 수와 맞는지 본다.** `volumes` 가 여러 줄로 쓰였거나 아예
# 없는 러너는 위 정규식에 안 걸려 **조용히 빠진다.** 그러면 그 러너에서 도는 아침
# 카드가 매일 "서버 상태를 읽지 못했다" 만 붙인다.
if added + have != runners:
    print(f"   ❌ 러너 {runners}개 중 {added + have}개만 처리됐다 — "
          "`volumes` 가 여러 줄이거나 없는 항목이 있다. 손으로 넣어 주세요:")
    print(f'      volumes = [..., "{mount}"]')
    sys.exit(1)

if not added:
    print(f"   이미 다 있음 (러너 {runners}개)")
    sys.exit(2)                     # 바꾼 것이 없다 = 재시작할 이유도 없다

# **쓰기 전에 파싱해 본다.** 깨진 파일로 재시작하면 러너가 통째로 죽는다.
try:
    import tomllib
    tomllib.loads(out)
except ModuleNotFoundError:
    pass                            # python <3.11 — 아래 백업으로 되돌릴 수 있다
except Exception as e:              # noqa: BLE001
    print(f"   ❌ 고친 설정이 TOML 로 읽히지 않는다 — 바꾸지 않는다: {e}")
    sys.exit(1)

# **권한과 소유자를 그대로 옮긴다.** 이 파일에는 **러너 등록 토큰**이 들어 있어
# 보통 root 0600 이다. `/tmp` 에 만들어 옮기면 파일 시스템이 달라 복사로 떨어지고,
# 그때 임시 파일의 느슨한 권한이 그대로 남아 토큰이 노출된다. 같은 디렉터리에
# 만들고 원본의 stat 를 복사한 뒤 `os.replace` 로 원자적으로 바꾼다.
st = os.stat(cfg)
with tempfile.NamedTemporaryFile("w", encoding="utf-8", delete=False,
                                 dir=os.path.dirname(cfg)) as f:
    f.write(out)
    tmp = f.name
os.chmod(tmp, st.st_mode & 0o7777)
try:
    os.chown(tmp, st.st_uid, st.st_gid)
except PermissionError:
    pass
os.replace(tmp, cfg)
print(f"   러너 {added}곳에 추가 (전체 {runners}개)")
PY
  then
    echo "   gitlab-runner 재시작 — 도는 잡이 있으면 끊깁니다"
    systemctl restart gitlab-runner
    sleep 2
    systemctl is-active --quiet gitlab-runner \
      || { echo "   ❌ 러너가 살아나지 않았다 — $RUNNER_CFG.bak-* 로 되돌리세요"; exit 1; }
  else
    rc=$?
    [ "$rc" = "2" ] || { echo "   설정을 바꾸지 못했다 (종료 $rc)"; exit "$rc"; }
  fi
  grep -n "ec2-status" "$RUNNER_CFG" | sed 's/^/   /'
fi

echo "[4/4] 확인"
python3 -m json.tool "$STATUS_DIR/EC2-STATUS" | head -12
echo "   …"
echo ""
echo "완료. 다음 실행: $(systemctl list-timers metagame-status.timer --no-pager | sed -n 2p)"
echo "CI 잡에서는 /ec2-status/EC2-STATUS 로 보입니다(읽기 전용)."
