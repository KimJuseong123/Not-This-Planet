#!/usr/bin/env bash
# EC2 상태를 한 장의 JSON 으로 적는다 (S15P21B202-458)
#
#   실행: 호스트 systemd timer 가 5분마다 (metagame-status.timer)
#   결과: $OUT  — 배포 러너에 `/spool` 로 마운트돼 있어 CI 잡이 그대로 읽는다
#
# ── 왜 이런 모양인가 ─────────────────────────────────────────────────────────
#
# CI 잡은 도커 컨테이너 안이라 **호스트를 직접 못 본다.** 그래서 호스트가 재서
# 파일로 놓고 잡이 그것을 읽는다. `deploy.sh` 가 `STATUS` 를 쓰고 잡이 읽는 것과
# 같은 방식이고, 그 이유도 같다 — **도커 소켓도 SSH 키도 CI 에 주지 않는다**
# (-139 · -252 · -324).
#
# **실행되는 코드는 마운트 안에 두지 않는다** (-324). 이 스크립트는
# `/opt/metagame-deploy/` 에 있고 **결과만** 마운트되는 폴더에 떨어진다. 그 폴더는
# 러너에 **읽기 전용**으로 보이므로 잡이 상태를 위조할 수도 없다.
#
# ── 판정은 여기서 하지 않는다 ────────────────────────────────────────────────
#
# 임계값·문구는 전부 `scripts/ci/ec2_status.py` 에 있다. 여기는 **재기만 한다.**
# 그래야 임계를 바꿀 때 서버에 들어가지 않아도 되고, 판정 로직을 오프라인
# 테스트로 잠글 수 있다. 호스트 스크립트를 고치려면 설치를 다시 해야 한다.
#
# ── JSON 은 python3 가 만든다 ────────────────────────────────────────────────
#
# 셸에서 손으로 조립하면 따옴표·역슬래시가 한 번만 어긋나도 파일이 깨지고, 그러면
# 읽는 쪽은 **아무 말도 안 하게 된다**(못 읽은 것을 조용한 것과 구별하지 못하면
# 그렇다 — 그래서 읽는 쪽도 그것을 따로 알린다). 값은 줄 단위로 모아 넘긴다.
set -uo pipefail

# **배포 스풀이 아니라 전용 폴더에 쓴다** (S15P21B202-458).
#
# 스풀은 배포 러너에 `rw` 로 보인다 — 거기 두면 ① 배포 태그가 붙은 잡이 가짜 상태를
# 써 넣을 수 있고 ② **배포 러너에서 도는 잡만** 읽을 수 있어, 아침 카드가 그 잡을
# 기다리게 된다. 전용 폴더를 모든 러너에 `ro` 로 물리면 둘 다 사라진다.
OUT="${STATUS_OUT:-/var/lib/metagame-status/EC2-STATUS}"
TMP="$OUT.part"
mkdir -p "$(dirname "$OUT")"
DOMAIN="${CERT_DOMAIN:-j15b202.p.ssafy.io}"

# 있어야 하는 컨테이너 넷.
WANT_CONTAINERS=(metagame-backend metagame-mysql metagame-dev-backend metagame-dev-mysql)

# systemd 는 **꺼져 있어야 하는 것도** 본다. `metagame.service` 는 데디케이티드
# 서버 시절 유닛이라 살아나면 안 된다 — 되살아나도 지금은 아무도 모른다.
WANT_UNITS=(gitlab-runner metagame-relay metagame-deploy.path metagame-deploy-dev.path metagame.service)

# ── 디스크·메모리 ────────────────────────────────────────────────────────────
read -r DISK_PCT DISK_FREE_GB <<<"$(df -BG --output=pcent,avail / | tail -1 | tr -d '%G')"
read -r MEM_TOTAL_GB MEM_AVAIL_GB <<<"$(free -g | awk '/^Mem:/ {print $2, $7}')"
UPTIME_DAYS=$(awk '{printf "%d", $1/86400}' /proc/uptime)

# ── 컨테이너 ─────────────────────────────────────────────────────────────────
# `Up` 인지와 **재시작 횟수**를 함께 싣는다. 떠 있어도 재시작을 반복하면 그것이
# 2026-09-11 dev 백엔드(JWT_SECRET)가 있던 상태다 — `docker ps` 만 보면
# "Up 3 seconds" 라 멀쩡해 보인다.
CONTAINER_LINES=""
for c in "${WANT_CONTAINERS[@]}"; do
  running=$(docker inspect -f '{{.State.Running}}' "$c" 2>/dev/null || echo false)
  restarts=$(docker inspect -f '{{.RestartCount}}' "$c" 2>/dev/null || echo -1)
  # **언제 떴는지도 싣는다.** `RestartCount` 는 컨테이너를 만든 뒤의 **누적값**이라,
  # 지난주에 다섯 번 죽었다가 지금 멀쩡한 것도 영원히 그 숫자를 달고 있다. 그것만
  # 보고 울리면 매일 아침 같은 줄이 붙고, 사람이 카드를 안 보게 된다.
  up_secs=$(docker inspect -f '{{.State.StartedAt}}' "$c" 2>/dev/null | \
            xargs -I{} date -d {} +%s 2>/dev/null || echo 0)
  [ "${up_secs:-0}" -gt 0 ] && up_secs=$(( $(date +%s) - up_secs )) || up_secs=-1
  CONTAINER_LINES+="$c	${running:-false}	${restarts:--1}	${up_secs:--1}"$'\n'
done

# ── 백엔드가 실제로 응답하는가 ───────────────────────────────────────────────
# **`/auth/guest` 를 쓰지 않는다.** `deploy.sh` 의 준비 확인은 그것을 쓰지만 배포마다
# 한 번이다. 5분마다 부르면 **게스트 사용자가 계속 쌓인다.**
#
# `/actuator/health` 는 인증에 막혀 **403** 을 준다. 그래도 판정에 충분하다 — 403 은
# 스프링이 응답했다는 뜻이고, 앱이 죽어 있으면 curl 이 `000`(연결 거부)을 돌려준다.
# 우리가 잡으려는 것이 그 `000` 이다.
BACKEND_LINES=""
for pair in "prod:8080" "dev:8081"; do
  name="${pair%%:*}"; port="${pair##*:}"
  code=$(curl -s -o /dev/null -w '%{http_code}' -m 5 "http://127.0.0.1:$port/actuator/health" 2>/dev/null || echo 000)
  BACKEND_LINES+="$name	$port	${code:-000}"$'\n'
done

# ── systemd ──────────────────────────────────────────────────────────────────
UNIT_LINES=""
for u in "${WANT_UNITS[@]}"; do
  UNIT_LINES+="$u	$(systemctl is-active "$u" 2>/dev/null || true)"$'\n'
done

# ── TLS 인증서 ───────────────────────────────────────────────────────────────
# certbot 자동 갱신이 멈춰도 지금은 만료 당일에야 안다.
# **못 읽은 것과 만료된 것을 구별한다.** 둘 다 음수로 보내면 읽는 쪽이 가장 심각한
# 상태(이미 만료됨)를 가장 약한 문구("만료일을 읽지 못했다")로 말하게 된다.
# 못 읽었으면 빈 값 → JSON 의 null, 만료됐으면 음수 일수.
CERT_DAYS=""
CERT_FILE="/etc/letsencrypt/live/$DOMAIN/cert.pem"
if [ -r "$CERT_FILE" ]; then
  end=$(openssl x509 -enddate -noout -in "$CERT_FILE" 2>/dev/null | cut -d= -f2)
  if [ -n "$end" ]; then
    end_s=$(date -d "$end" +%s 2>/dev/null || echo 0)
    # 만료됐으면 음수가 나온다. 그대로 싣는다 — 읽는 쪽이 그것을 가장 센 문구로 만든다.
    [ "$end_s" -gt 0 ] && CERT_DAYS=$(( (end_s - $(date +%s)) / 86400 ))
  fi
fi

# ── 쌓이는 것 (S15P21B202-457 이 치운다) ─────────────────────────────────────
# `docker system df` 는 1~2초 걸린다. 5분 간격이라 감당할 수 있고, **디스크가 차는
# 이유가 대부분 여기**라 같이 재는 편이 낫다.
DOCKER_GB=$(docker system df --format '{{.Size}}' 2>/dev/null | head -1 | sed 's/[^0-9.]//g' | cut -d. -f1)
BUILDS_MB=$(du -sm /var/www/metagame-builds 2>/dev/null | cut -f1)
SITE_MB=$(du -sm /var/www/metagame-site 2>/dev/null | cut -f1)
RUNLOG_MB=$(du -sm /var/log/metagame-runlogs /var/log/metagame-dev-runlogs 2>/dev/null | awk '{s+=$1} END {print s+0}')

export DISK_PCT DISK_FREE_GB MEM_TOTAL_GB MEM_AVAIL_GB UPTIME_DAYS \
       CERT_DAYS DOCKER_GB BUILDS_MB SITE_MB RUNLOG_MB \
       CONTAINER_LINES BACKEND_LINES UNIT_LINES

python3 > "$TMP" <<'PY'
import json, os, socket, subprocess


def num(name, default=0):
    try:
        return int(float(os.environ.get(name) or default))
    except ValueError:
        return default


def maybe_num(name):
    """빈 값이면 None(→ JSON null). **못 읽은 것과 0/음수를 섞지 않는다.**"""
    raw = (os.environ.get(name) or "").strip()
    if not raw:
        return None
    try:
        return int(float(raw))
    except ValueError:
        return None


def rows(name):
    return [ln.split("\t") for ln in (os.environ.get(name) or "").splitlines() if ln.strip()]


print(json.dumps({
    "at": subprocess.run(["date", "-Is"], capture_output=True, text=True).stdout.strip(),
    "host": socket.gethostname(),
    "uptime_days": num("UPTIME_DAYS"),
    "disk": {"used_pct": num("DISK_PCT"), "free_gb": num("DISK_FREE_GB")},
    "mem": {"total_gb": num("MEM_TOTAL_GB"), "available_gb": num("MEM_AVAIL_GB")},
    "containers": [
        {"name": r[0], "running": r[1] == "true", "restarts": int(r[2]),
         "uptime_secs": int(r[3]) if len(r) > 3 else -1} for r in rows("CONTAINER_LINES")
    ],
    "backends": [
        {"name": r[0], "port": int(r[1]), "code": r[2]} for r in rows("BACKEND_LINES")
    ],
    "units": {r[0]: (r[1] if len(r) > 1 else "") for r in rows("UNIT_LINES")},
    "cert_days_left": maybe_num("CERT_DAYS"),
    "sizes": {
        "docker_gb": num("DOCKER_GB"), "builds_mb": num("BUILDS_MB"),
        "site_mb": num("SITE_MB"), "runlogs_mb": num("RUNLOG_MB"),
    },
}, ensure_ascii=False, indent=2))
PY

# **원자적으로 바꾼다.** 잡이 읽는 도중에 쓰면 반쪽짜리 JSON 을 읽는다.
if ! python3 -c 'import json,sys; json.load(open(sys.argv[1]))' "$TMP" 2>/dev/null; then
  echo "만든 JSON 이 깨졌다 — 옛 파일을 그대로 둔다" >&2
  rm -f "$TMP"
  exit 1
fi
chmod 644 "$TMP"
mv "$TMP" "$OUT"
