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

APP="${APP_DIR:-/home/ubuntu/metagame}"
SPOOL="${SPOOL_DIR:-$APP/deploy}"
JAR_DEST="$APP/build/libs/backend-0.0.1-SNAPSHOT.jar"
KEEP="${KEEP:-3}"
HEALTH_TRIES="${HEALTH_TRIES:-30}"        # 3초 간격 → 최대 90초
MIN_JAR_BYTES="${MIN_JAR_BYTES:-10000000}"   # 10MB 미만이면 정상 jar 이 아니다

mkdir -p "$SPOOL/previous"
exec >>"$SPOOL/deploy.log" 2>&1

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

LAST_CODE=""
# 준비 확인: 게스트 로그인이 200 인가. deviceId 가 고정이라 사용자 행은 최대 1개만 생긴다
# (AuthService 는 findByDeviceId().orElseGet(save) 로 멱등하다). DB·JWT·시큐리티 경로를 한 번에 본다.
#
# **nickname 은 12자 이하**여야 한다. 넘으면 400 인데 지금 서버는 그것을 403 으로 가려 보내서
# (S15P21B202-210) 원인 모를 실패가 된다 — 2026-09-01 에 'deploy-health'(13자)로 90초를 헛돌았다.
# 그래서 실패하면 마지막 응답 코드와 백엔드 로그 꼬리를 남긴다.
ready() {
  LAST_CODE=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 \
    -X POST http://127.0.0.1:8080/auth/guest \
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
exec 9>"$SPOOL/.lock"
if ! flock -n 9; then
  echo "[$(now)] 다른 배포가 진행 중 → 이번 실행은 건너뜀"
  exit 0
fi

echo ""
echo "===== [$(now)] 배포 시작 ====="

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
