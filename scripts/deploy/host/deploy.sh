#!/usr/bin/env bash
# METAGAME 백엔드 배포 — EC2 호스트에서 도는 쪽 (S15P21B202-141)
#
# CI 잡은 컨테이너 안에서 돌기 때문에 호스트의 docker compose 를 부를 수 없다. 그래서
# 잡은 **스풀 디렉터리에 jar 과 RELEASE 표시 파일만** 놓고, systemd path 유닛이 그것을 보고
# 이 스크립트를 호스트 권한으로 실행한다. 도커 소켓을 잡에 노출하지 않기 위한 구조다
# (백엔드·DB 가 같은 호스트에 있어 소켓 = 사실상 root). 결정 근거: docs/records/S15P21B202-139-cd-strategy.md
#
# **이 파일은 스풀 밖(/opt/metagame-deploy/deploy.sh, root 소유)에 설치된다**(S15P21B202-324).
# 스풀은 배포 러너 컨테이너에 rw 로 마운트되므로, 스풀 안에 두면 잡이 이 스크립트를 덮어써
# 호스트가 그것을 실행한다 — 컨테이너는 데이터만, 코드는 마운트 밖. 위치와 무관하게 동작한다
# (경로는 아래 APP·SPOOL 로 정해지고 자기 위치를 참조하지 않는다).
#
# 프로토콜 (스풀 = $SPOOL, 잡에서는 /spool 로 마운트)
#   incoming.jar  CI 가 놓는 새 jar (원자적으로: .part 로 쓰고 rename)
#   RELEASE       방아쇠. 한 줄 JSON {"sha":…,"pipeline":…}  — 이 스크립트가 마지막에 지운다
#   STATUS        결과. {"sha":…,"result":"ok|rollback|fail","detail":…,"at":…}
#   previous/     직전 jar 보관($KEEP 개)
#   deploy.log    실행 기록 (누가·어떤 sha·결과)
#
# DRY_RUN=1 로 실행하면 컨테이너를 건드리지 않고 지금 도는 앱의 준비 상태만 확인한다(설치 검증용).
set -uo pipefail

# **환경마다 값이 다르다** (S15P21B202-284). prod 는 기본값이 곧 지금 쓰는 값이므로
# 이 스크립트를 dev 에 그대로 쓰면서 환경변수만 갈아 끼운다 — 복사본을 두면 한쪽이
# 조용히 낡는다(`-433` 에서 겪은 것과 같은 결).
#
#   prod  APP_DIR=/home/ubuntu/metagame       HEALTH_PORT=8080  (기본값)
#   dev   APP_DIR=/home/ubuntu/metagame-dev   HEALTH_PORT=8081
APP="${APP_DIR:-/home/ubuntu/metagame}"
SPOOL="${SPOOL_DIR:-$APP/deploy}"
HEALTH_PORT="${HEALTH_PORT:-8080}"
ENV_NAME="${ENV_NAME:-prod}"
JAR_DEST="$APP/build/libs/backend-0.0.1-SNAPSHOT.jar"
KEEP="${KEEP:-3}"
HEALTH_TRIES="${HEALTH_TRIES:-30}"        # 3초 간격 → 최대 90초
MIN_JAR_BYTES="${MIN_JAR_BYTES:-10000000}"   # 10MB 미만이면 정상 jar 이 아니다

mkdir -p "$SPOOL/previous"

now() { date -Is; }

write_status() {   # result detail
  printf '{"sha":"%s","result":"%s","detail":"%s","at":"%s"}\n' \
    "${SHA:-?}" "$1" "$2" "$(now)" > "$SPOOL/STATUS.part"
  mv "$SPOOL/STATUS.part" "$SPOOL/STATUS"
  echo "[$(now)] 결과=$1 detail=$2"
}

finish() {         # result detail  — RELEASE 를 지워 path 유닛이 다시 무장하게
  write_status "$1" "$2"
  rm -f "$SPOOL/RELEASE"
  exit 0
}

abort() {          # detail  — **되풀이해도 소용없는 오류.** (S15P21B202-483)
  #
  # `finish` 와 둘을 가르는 것은 **종료 코드**다. 0 으로 끝내면 systemd 는 잘 됐다고
  # 보고, CI 는 STATUS 만 보고, 사람은 아무 말도 못 듣는다. 1 로 끝내야 유닛이
  # failed 로 남아 `systemctl status` 에 보인다.
  #
  # RELEASE 는 **지운다.** 남겨 두면 `PathExists` 가 곧바로 다시 트리거하고, 원인이
  # 그대로인 이상 또 실패한다 — 그 되풀이가 2026-09-12 에 start-limit-hit 로
  # 유닛을 죽였고 개발 배포가 1일 15시간 멈췄다.
  write_status "error" "$1"
  rm -f "$SPOOL/RELEASE"
  exit 1
}

# **로그를 열 수 있는지 먼저 본다** (S15P21B202-483).
#
# 못 연 채로 진행하면 이후 출력이 전부 journald 로만 가고, 사람은 `deploy.log` 를
# 열어 보며 "아무 일도 없었다" 고 읽는다. 2026-09-12 에 실제로 그랬다 — 설치
# 스크립트가 root 로 한 번 돌면서 이 파일을 root 소유로 만들어 두었고, 그 뒤
# User=ubuntu 로 도는 서비스는 영영 쓸 수 없었다.
if ! : >>"$SPOOL/deploy.log" 2>/dev/null; then
  abort "배포 로그에 쓸 수 없다: $SPOOL/deploy.log — 소유자·권한을 보라"
fi
exec >>"$SPOOL/deploy.log" 2>&1

LAST_CODE=""
# 준비 확인: 게스트 로그인이 200 인가. deviceId 가 고정이라 사용자 행은 최대 1개만 생긴다
# (AuthService 는 findByDeviceId().orElseGet(save) 로 멱등하다). DB·JWT·시큐리티 경로를 한 번에 본다.
#
# **nickname 은 12자 이하**여야 한다. 넘으면 400 인데 지금 서버는 그것을 403 으로 가려 보내서
# (S15P21B202-210) 원인 모를 실패가 된다 — 2026-09-01 에 'deploy-health'(13자)로 90초를 헛돌았다.
# 그래서 실패하면 마지막 응답 코드와 백엔드 로그 꼬리를 남긴다.
ready() {
  LAST_CODE=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 \
    -X POST "http://127.0.0.1:$HEALTH_PORT/auth/guest" \
    -H 'Content-Type: application/json' \
    -d '{"deviceId":"ci-deploy-health","nickname":"health"}' 2>/dev/null)
  [ "$LAST_CODE" = "200" ]
}

wait_ready() {
  local i
  for i in $(seq 1 "$HEALTH_TRIES"); do
    if ready; then echo "[$(now)] 준비 확인 OK (${i}회 시도)"; return 0; fi
    sleep 3
  done
  echo "[$(now)] 준비 확인 실패 (${HEALTH_TRIES}회, 마지막 응답 코드=${LAST_CODE:-없음})"
  echo "--- 백엔드 로그 꼬리 (원인 파악용) ---"
  (cd "$APP" && docker compose logs --tail 15 backend 2>&1 | tail -15) || true
  echo "--- 끝 ---"
  return 1
}

# ── 동시 실행 방지 ────────────────────────────────────────────────────────
# **자물쇠를 못 여는 것과 남이 쥐고 있는 것은 다르다** (S15P21B202-483).
#
# 2026-09-12 실측: `.lock` 이 root 소유라 `exec 9>` 가 Permission denied 로 실패했는데,
# 그러고도 스크립트가 계속 돌아 `flock -n 9` 가 `Bad file descriptor` 로 떨어졌다.
# 그 실패를 아래 분기가 **"다른 배포가 진행 중"** 으로 읽고 exit 0 했다 —
# 권한 오류가 정상적인 건너뜀으로 둔갑한 것이다.
#
# 그래서 **열 수 있는지 먼저 본다.** 여기서 갈라야 `flock` 의 실패가 오직 한 가지
# 뜻(남이 쥐고 있다)만 갖는다.
if ! : >>"$SPOOL/.lock" 2>/dev/null; then
  abort "자물쇠 파일에 쓸 수 없다: $SPOOL/.lock — 소유자·권한을 보라"
fi
exec 9>>"$SPOOL/.lock"
if ! flock -n 9; then
  echo "[$(now)] 다른 배포가 진행 중 → 이번 실행은 건너뜀"
  exit 0
fi

echo ""
echo "===== [$(now)] 배포 시작 · 환경=$ENV_NAME · 앱=$APP · 포트=$HEALTH_PORT ====="

[ -f "$SPOOL/RELEASE" ] || { echo "RELEASE 없음 → 할 일 없음"; exit 0; }
RELEASE_RAW=$(head -c 2000 "$SPOOL/RELEASE")
SHA=$(printf '%s' "$RELEASE_RAW" | sed -n 's/.*"sha"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
SHA="${SHA:-unknown}"
echo "요청: $RELEASE_RAW"

if [ "${DRY_RUN:-0}" = "1" ]; then
  echo "DRY_RUN — 컨테이너를 건드리지 않고 지금 도는 앱만 확인한다"
  if wait_ready; then finish ok "dry-run: 현재 앱 준비 확인"; else finish fail "dry-run: 현재 앱이 준비되지 않음"; fi
fi

# ── 들어온 jar 검사 ───────────────────────────────────────────────────────
IN="$SPOOL/incoming.jar"
[ -f "$IN" ] || finish fail "incoming.jar 없음"
SIZE=$(stat -c %s "$IN" 2>/dev/null || echo 0)
[ "$SIZE" -ge "$MIN_JAR_BYTES" ] || finish fail "jar 크기 비정상 ${SIZE}바이트"
echo "jar ${SIZE}바이트"

# ── 직전 jar 보관 (롤백용) ────────────────────────────────────────────────
BACKUP=""
if [ -f "$JAR_DEST" ]; then
  BACKUP="$SPOOL/previous/$(date +%s).jar"
  cp -f "$JAR_DEST" "$BACKUP" && echo "직전 jar 보관: $BACKUP"
  # 오래된 것부터 정리 — $KEEP 개만 남긴다
  ls -1t "$SPOOL/previous"/*.jar 2>/dev/null | tail -n +"$((KEEP + 1))" | xargs -r rm -f
fi

# ── 교체 후 기동 ──────────────────────────────────────────────────────────
mkdir -p "$(dirname "$JAR_DEST")"
cp -f "$IN" "$JAR_DEST" || finish fail "jar 복사 실패"
cd "$APP" || finish fail "$APP 로 이동 실패"

echo "docker compose up -d --build backend"
if ! docker compose up -d --build backend; then
  echo "기동 실패 → 롤백 시도"
  if [ -n "$BACKUP" ]; then cp -f "$BACKUP" "$JAR_DEST"; docker compose up -d --build backend; fi
  finish rollback "compose 실패로 직전 jar 복귀"
fi

if wait_ready; then
  finish ok "배포 완료"
fi

# ── 헬스체크 실패 → 롤백 ─────────────────────────────────────────────────
echo "준비 확인 실패 → 롤백"
if [ -z "$BACKUP" ]; then finish fail "준비 확인 실패, 되돌릴 직전 jar 이 없음"; fi
cp -f "$BACKUP" "$JAR_DEST"
docker compose up -d --build backend
if wait_ready; then finish rollback "새 jar 이 안 떠서 직전 jar 로 복귀(서비스 정상)"; fi
finish fail "롤백 후에도 준비 확인 실패 — 사람이 봐야 한다"
