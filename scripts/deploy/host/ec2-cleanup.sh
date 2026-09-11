#!/usr/bin/env bash
# EC2 에 쌓이는 것을 주기적으로 치운다 (S15P21B202-457)
#
#   실행: 호스트 systemd timer 가 매주 (metagame-cleanup.timer)
#   확인: DRY_RUN=1 로 돌리면 지우지 않고 무엇을 지울지만 말한다
#
# ── 지우지 않는 것이 이 스크립트의 핵심이다 ──────────────────────────────────
#
# `docker system df` 는 **36.7 GB 중 32.5 GB(88%)가 회수 가능**이라고 말한다.
# 그 말을 그대로 믿고 `docker image prune -a` 를 돌리면 Unity 이미지 두 개
# (17.9 GB + 15.1 GB)가 사라진다. 그것은 **컨테이너가 도는 동안에만** 쓰이므로
# 도커가 보기에는 늘 놀고 있는 것이다.
#
# 지우는 순간은 공짜다. 대가는 다음 릴리스에 온다 — **이미지를 다시 받는 데 7분**
# (2026-09-11 실측)이고, 러너 볼륨의 셰이더 캐시까지 날아가면 **빌드가 40분 더**
# 걸린다(-406 실측). 그래서 이 스크립트는 다음 두 가지를 **절대 하지 않는다**:
#
#   docker image prune -a      ← 태그가 붙은 이미지까지 지운다 (Unity 이미지)
#   docker volume prune        ← 러너 Library·셰이더 캐시가 거기 있다
#
# 하는 것은 **되돌려도 싼 것**뿐이다: 빌드 캐시 · 떠돌이(dangling) 이미지 ·
# 끝난 컨테이너 · 오래된 릴리스 zip.
set -uo pipefail

DRY="${DRY_RUN:-0}"
BUILDS_DIR="${BUILDS_DIR:-/var/www/metagame-builds}"
KEEP_ZIPS="${KEEP_ZIPS:-3}"            # 릴리스 zip 은 164 MB 짜리다. 3개면 되돌릴 여지가 남는다
BUILD_CACHE_HOURS="${BUILD_CACHE_HOURS:-168}"   # 7일
CONTAINER_HOURS="${CONTAINER_HOURS:-24}"

say() { echo "[$(date -Is)] $*"; }
# **실패를 삼키지 않는다.** 파이프(`| sed`)를 쓰면 셸은 `sed` 의 종료 코드를 보므로,
# 앞의 명령이 깨져도 성공처럼 보인다. 타이머로 도는 것이라 아무도 화면을 안 보고
# 있고, 그러면 몇 주 동안 아무것도 안 치우면서 초록으로 남는다.
run() {
  if [ "$DRY" = "1" ]; then
    say "  (DRY) $*"
    return 0
  fi
  local out rc
  out=$("$@" 2>&1); rc=$?
  [ -n "$out" ] && printf '%s\n' "$out" | sed 's/^/    /'
  if [ "$rc" -ne 0 ]; then
    say "   ❌ 실패(종료 $rc): $*"
    FAILED=1
  fi
  return 0
}

say "===== 정리 시작 (DRY_RUN=$DRY) ====="
BEFORE=$(df -BG --output=avail / | tail -1 | tr -d ' G')
FAILED=0

# ── CI 잡이 돌고 있으면 도커 정리는 건너뛴다 ────────────────────────────────
#
# 일요일 03:00 은 한가하지만 **비어 있는 것이 보장되지는 않는다** — 새벽 배포
# 스케줄(-455)이나 누가 돌린 릴리스가 겹칠 수 있다. 도커가 쓰는 중인 것을 지우지는
# 않지만, 캐시를 치우는 것이 **그 잡의 빌드를 느리게 만든다.** 한 주 미루는 것이
# 싸다 — 다음 주에 다시 돈다.
#
# zip 정리는 그대로 한다. CI 잡과 아무 상관이 없다.
BUSY=$(docker ps --format '{{.Names}}' 2>/dev/null | grep -c '^runner-' || true)
if [ "${BUSY:-0}" -gt 0 ]; then
  say "⏸ CI 잡 ${BUSY}개가 돌고 있다 — 도커 정리는 건너뛴다(다음 주에 다시 돈다)"
fi

# ── ① 빌드 캐시 ─────────────────────────────────────────────────────────────
# 2026-09-11 실측 1.94 GB. 다시 만들어지는 것이라 지워도 잃는 것이 없다.
say "① 도커 빌드 캐시 (${BUILD_CACHE_HOURS}시간 넘은 것)"
[ "${BUSY:-0}" -eq 0 ] && run docker builder prune -f --filter "until=${BUILD_CACHE_HOURS}h" \
  || say "   건너뜀 (CI 잡이 돈다)"

# ── ② 떠돌이 이미지 ─────────────────────────────────────────────────────────
# **`-a` 를 쓰지 않는다.** 그것 없이는 태그가 없는 것만 지운다 — 백엔드를 새로
# 빌드할 때마다 옛 `metagame-backend:latest` 가 태그를 잃고 남는, 바로 그것이다.
say "② 태그 없는 떠돌이 이미지"
[ "${BUSY:-0}" -eq 0 ] && run docker image prune -f \
  || say "   건너뜀 (CI 잡이 돈다)"

# ── ③ 끝난 컨테이너 ─────────────────────────────────────────────────────────
# 러너가 잡마다 만들고 버린다. 도는 것은 건드리지 않는다.
say "③ ${CONTAINER_HOURS}시간 넘게 끝나 있는 컨테이너"
[ "${BUSY:-0}" -eq 0 ] && run docker container prune -f --filter "until=${CONTAINER_HOURS}h" \
  || say "   건너뜀 (CI 잡이 돈다)"

# ── ④ 오래된 릴리스 zip ─────────────────────────────────────────────────────
# 런처 저장소(`files/`·`manifest/`)는 `make_manifest.py --keep 10` 이 회수한다.
# **zip 은 그 대상이 아니다** — 손으로 올리는 것이라 아무도 치우지 않았다.
say "④ 릴리스 zip — 최신 ${KEEP_ZIPS}개만 남긴다"
if [ -d "$BUILDS_DIR" ]; then
  # 이름에 날짜·시각이 있어 문자열 정렬이 곧 시각 정렬이다(-269 규칙).
  mapfile -t ZIPS < <(ls -1 "$BUILDS_DIR"/*.zip 2>/dev/null | sort -r)
  if [ "${#ZIPS[@]}" -gt "$KEEP_ZIPS" ]; then
    for z in "${ZIPS[@]:$KEEP_ZIPS}"; do
      say "   지운다: $(basename "$z") ($(du -h "$z" | cut -f1))"
      run rm -f "$z"
    done
  else
    say "   ${#ZIPS[@]}개뿐 — 지울 것 없음"
  fi
else
  say "   $BUILDS_DIR 가 없다 — 건너뜀"
fi

# ── ⑤ 지켜야 할 것이 그대로인지 확인한다 ────────────────────────────────────
#
# 위 넷은 안전한 명령만 쓰지만, **누가 나중에 `-a` 를 붙일 수 있다.** 그때 조용히
# 사라지면 다음 릴리스에서야 안다 — 그 릴리스가 40분 넘게 길어진 뒤에.
say "⑤ 지켜야 할 것 확인"
MISSING=0
for img in "unityci/editor:ubuntu-6000.5.4f1-windows-mono-3.2.2" "unityci/editor:ubuntu-6000.5.4f1-base-3.2.2"; do
  if docker image inspect "$img" >/dev/null 2>&1; then
    say "   ✅ $img"
  else
    say "   ❌ $img 가 없다 — 다음 릴리스가 이미지 내려받기로 7분 더 걸린다"
    MISSING=1
  fi
done
VOLS=$(docker volume ls -q 2>/dev/null | grep -c -- '-cache-.*-protected')
say "   보호 러너 캐시 볼륨 ${VOLS}개 (셰이더 캐시가 여기 있다 — 지우면 릴리스가 40분 길어진다)"

AFTER=$(df -BG --output=avail / | tail -1 | tr -d ' G')
say "===== 끝. 여유 ${BEFORE}GB → ${AFTER}GB ====="

# 지켜야 할 것이 사라졌거나 한 단계라도 실패했으면 비0 으로 끝낸다 — `systemctl
# status` 와 저널에 빨갛게 남아야 사람이 본다.
[ "$MISSING" = "1" ] && exit 1
[ "$FAILED" = "1" ] && exit 2
exit 0
