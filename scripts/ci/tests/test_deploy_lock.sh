#!/usr/bin/env bash
# 배포 스크립트가 **권한 오류를 정상적인 건너뜀으로 둔갑시키지 않는지** (S15P21B202-483).
#
# 실행: bash scripts/ci/tests/test_deploy_lock.sh
# 도커·네트워크·root 없이 돈다.
#
# 2026-09-12 실측 사고. 설치 스크립트가 `deploy.sh` 를 root 로 한 번 돌리면서
# `.lock` 과 `deploy.log` 를 root 소유로 만들어 두었고, 그 뒤 `User=ubuntu` 로 도는
# 서비스는 두 파일에 쓸 수 없었다. 그런데 스크립트가 그 실패를
# **"다른 배포가 진행 중"** 으로 읽고 **exit 0** 했다. systemd 는 성공으로 보고,
# `PathExists` 가 RELEASE 를 다시 물어 재시작을 되풀이했고, start-limit-hit 로
# **path 유닛까지 죽어** 개발 배포가 1일 15시간 멈췄다.
#
# **못 여는 파일을 이식성 있게 만드는 법**: 디렉터리로 만든다. `: >>dir` 은 어디서나
# 실패한다 — root 소유 흉내를 내려면 이 테스트 자체가 root 여야 해서 쓸 수 없다.
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
SH="$ROOT/scripts/deploy/host/deploy.sh"
[ -f "$SH" ] || { echo "deploy.sh 를 찾을 수 없다: $SH"; exit 1; }

PASS=0
die() { echo "  ❌ $1"; exit 1; }
ok()  { echo "  ✅ $1"; PASS=$((PASS + 1)); }

# 사고를 재현한다. $1 = 못 쓰게 만들 파일 이름. 출력과 로그를 합쳐 돌려준다.
run_blocked() {
  local blocked="$1" t app spool out rc log
  t=$(mktemp -d); app="$t/app"; spool="$app/deploy"
  mkdir -p "$spool"
  printf '{"sha":"testsha"}' > "$spool/RELEASE"
  mkdir "$spool/$blocked"                      # ← 열 수 없는 파일
  out=$(APP_DIR="$app" DRY_RUN=1 HEALTH_TRIES=1 bash "$SH" 2>&1); rc=$?
  log=$(cat "$spool/deploy.log" 2>/dev/null || true)   # 디렉터리면 빈 문자열
  OUT="$out
$log"
  RC=$rc
  SPOOL="$spool"
}

check_common() {   # $1 = 사람이 읽을 이름
  # ① **0 으로 끝나면 안 된다.** 0 은 systemd 에게 "잘 됐다" 이고, 그러면 아무 흔적도
  #    남지 않는다. 1 이어야 유닛이 failed 로 남아 `systemctl status` 에 보인다.
  [ "$RC" -ne 0 ] || die "$1: 종료 코드가 0 이다 — systemd 가 성공으로 읽는다"
  # ② **이 문구가 나오면 안 된다.** 사고의 핵심이 권한 오류를 이것으로 읽은 것이다.
  case "$OUT" in
    *"다른 배포가 진행 중"*) die "$1: 권한 오류를 '다른 배포가 진행 중' 으로 읽었다" ;;
  esac
  # ③ CI 가 420초를 기다리지 않게 **STATUS 에 사유가 남아야 한다.**
  [ -f "$SPOOL/STATUS" ] || die "$1: STATUS 가 없다 — CI 는 시간 초과까지 기다리게 된다"
  grep -q '"result":"error"' "$SPOOL/STATUS" || die "$1: STATUS 가 error 가 아니다: $(cat "$SPOOL/STATUS")"
  # ④ **RELEASE 를 지워야 되풀이가 끊긴다.** 남기면 PathExists 가 즉시 다시 띄운다.
  [ ! -f "$SPOOL/RELEASE" ] || die "$1: RELEASE 가 남아 있다 — path 유닛이 되풀이한다"
}

echo "== deploy.sh 권한 실패 =="

# ── ① 자물쇠 파일을 열 수 없다 ──────────────────────────────────────────
run_blocked ".lock"
check_common "자물쇠"
case "$OUT" in
  *"자물쇠 파일에 쓸 수 없다"*) ;;
  *) die "자물쇠: 사유가 안 적혔다: $OUT" ;;
esac
ok "자물쇠를 못 열면 — exit≠0 · STATUS=error · RELEASE 삭제 · '다른 배포' 아님"

# ── ② 로그 파일을 열 수 없다 ────────────────────────────────────────────
#    이쪽이 더 고약하다. 못 연 채 진행하면 이후 출력이 전부 journald 로만 가고,
#    사람은 deploy.log 를 열어 보며 "아무 일도 없었다" 고 읽는다.
run_blocked "deploy.log"
check_common "로그"
case "$OUT" in
  *"배포 로그에 쓸 수 없다"*) ;;
  *) die "로그: 사유가 안 적혔다: $OUT" ;;
esac
ok "로그를 못 열면 — 진행하지 않고 같은 방식으로 멈춘다"

# ── ③ 안내가 환경을 구분한다 (deploy_backend.py) ────────────────────────
#    무응답일 때 prod 경로를 하드코딩해 알려 주면, dev 가 멈췄을 때 엉뚱한 파일을 본다.
cd "$ROOT" || exit 1
CI_JOB_NAME=deploy-dev  python -c "import sys; sys.path.insert(0,'scripts/ci'); import deploy_backend as d; assert d.host_unit()=='metagame-deploy-dev.path', d.host_unit(); assert 'metagame-dev' in d.host_log(), d.host_log()" || die "dev 안내가 prod 를 가리킨다"
CI_JOB_NAME=deploy-prod python -c "import sys; sys.path.insert(0,'scripts/ci'); import deploy_backend as d; assert d.host_unit()=='metagame-deploy.path', d.host_unit(); assert 'metagame-dev' not in d.host_log(), d.host_log()" || die "prod 안내가 dev 를 가리킨다"
ok "무응답 안내가 잡 이름대로 dev/prod 를 가른다"

# ── ④ 자물쇠를 남이 쥐고 있어도 **건너뛰고 끝내지 않는다** ──────────────
#    건너뛰면 RELEASE 가 남은 채 즉시 끝나고 PathExists 가 다시 문다 —
#    권한 오류로 죽은 것과 똑같은 되풀이다.
if command -v flock >/dev/null 2>&1; then
  T=$(mktemp -d); APP="$T/app"; SPOOL="$APP/deploy"
  mkdir -p "$SPOOL"
  printf '{"sha":"testsha"}' > "$SPOOL/RELEASE"
  : > "$SPOOL/.lock"
  # 다른 배포가 쥔 것처럼 자물쇠를 잡고 있는다
  ( exec 9>>"$SPOOL/.lock"; flock 9; sleep 4 ) &
  HOLDER=$!
  sleep 1
  START=$(date +%s)
  out=$(APP_DIR="$APP" DRY_RUN=1 HEALTH_TRIES=1 LOCK_WAIT=10 bash "$SH" 2>&1)
  ELAPSED=$(( $(date +%s) - START ))
  wait "$HOLDER" 2>/dev/null
  [ "$ELAPSED" -ge 2 ] || die "경합: 기다리지 않고 곧바로 끝났다 (${ELAPSED}초)"
  case "$out$(cat "$SPOOL/deploy.log" 2>/dev/null)" in
    *"다른 배포가 진행 중"*) die "경합: 건너뛰고 끝냈다 — RELEASE 가 남아 되풀이한다" ;;
  esac
  ok "자물쇠를 남이 쥐면 — 건너뛰지 않고 기다린다 (${ELAPSED}초)"
elif [ "$(uname -s)" = "Linux" ]; then
  # **리눅스에서는 건너뛰지 않는다.** `run_all.py` 는 통과한 테스트의 내부 출력을
  # 감추므로, 건너뛴 것도 ✅ 로 보인다 — 검사가 조용히 빠지는 바로 그 모양이다
  # (`-133` 에서 겪은 것). CI 러너는 리눅스이므로 여기서 멈추면 사람이 본다.
  die "리눅스인데 flock 이 없다 — 경합 검사가 조용히 빠진다 (util-linux 확인)"
else
  # 로컬 Git Bash 에는 flock 이 없다. 위 elif 덕분에 **CI 에서는 반드시 돈다.**
  echo "  ⏭ flock 이 없어 경합 검사는 건너뜀 ($(uname -s)). 리눅스 러너에서 돈다"
fi
echo "TEST_DEPLOY_LOCK OK ($PASS)"
