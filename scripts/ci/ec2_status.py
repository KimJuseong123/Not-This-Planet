"""EC2 상태를 아침 카드에 실을 줄로 바꾼다 (S15P21B202-458).

**왜 필요한가.** 아침 점검(`mm-digest`)은 Jira 와 GitLab 만 본다. 서버는 아무도
안 본다 — 그래서 조용히 나빠진다. 2026-09-11 에 개발 백엔드가 `JWT_SECRET` 때문에
재시작 루프였는데, 사람이 `docker ps` 를 쳐 봤기에 알았다. 그러지 않았으면
그날 테스트가 통째로 막혔을 것이다.

**어떻게 보나.** 잡은 도커 컨테이너 안이라 호스트를 직접 못 본다. 호스트의
`ec2-status.sh` 가 5분마다 재서 JSON 한 장을 `/var/lib/metagame-status` 에 놓고,
그 폴더가 **모든 러너에 읽기 전용으로 마운트**돼 있어 아침 카드 잡이 직접 읽는다.
`deploy.sh` 의 `STATUS` 와 같은 방식이고 이유도 같다 — **도커 소켓도 SSH 키도 CI 에
주지 않는다**(-139 · -252 · -324).

중간 잡을 두어 아티팩트로 넘기는 구조를 한 번 썼다가 버렸다. **그 잡이 대기하는
동안 아침 카드가 통째로 늦어진다** — `allow_failure` 는 실패에만, `timeout` 은
실행이 시작된 뒤에만 듣는다.

**조용하면 아무 말도 안 한다.** `mm-digest` 규칙 그대로다. 서버가 멀쩡하면 줄이
없어야 하고, 그래야 줄이 있을 때 사람이 본다.
"""
import hashlib
import json
import os
from datetime import datetime, timedelta, timezone

KST = timezone(timedelta(hours=9))

# ── 임계값 ───────────────────────────────────────────────────────────────────
#
# 2026-09-11 실측을 기준으로 잡았다: 디스크 20%(여유 250 GB) · 메모리 여유 12 GiB ·
# 컨테이너 4개 Up · 인증서 여유 넉넉. **지금 값에서 한참 떨어진 곳**에 선을 둔다 —
# 실측값 바로 옆에 두면 평소에도 울려서 사람이 카드를 안 보게 된다.
DISK_PCT_WARN = 80        # 차면 빌드·배포가 동시에 죽는다
MEM_FREE_GB_WARN = 2      # 15 GiB 중 백엔드 2벌 + MySQL 2벌
CERT_DAYS_WARN = 30       # certbot 이 멈춰도 한 달 전에 안다
RESTART_WARN = 3          # 재시작 루프. 떠 있어도 이 숫자가 오르면 죽고 있는 것이다
# **누적 재시작만으로는 울리지 않는다.** `RestartCount` 는 컨테이너를 만든 뒤의
# 누적값이라, 지난주에 다섯 번 죽었다 지금 멀쩡한 것도 그 숫자를 영원히 달고 있다.
# "지금 죽고 있는 중" 은 **재시작이 많고 + 방금 떴다** 로 본다.
FRESH_RESTART_SECS = 30 * 60
STALE_MINUTES = 20        # 5분 주기가 네 번 빠지면 재는 쪽이 멈춘 것이다

# 꺼져 있어야 하는 것. 살아나면 알린다 — 지금은 확인하는 장치가 없다.
MUST_BE_INACTIVE = ("metagame.service",)

# 찾는 자리 — 앞에서부터 처음 읽히는 것을 쓴다.
#   ① 환경 변수  검사·손으로 확인할 때
#   ② 마운트      호스트가 5분마다 쓰는 파일. **모든 러너에 읽기 전용으로 물려 있어**
#                 아침 카드 잡이 그대로 읽는다
#
# 한때는 배포 스풀에 두고 `ec2-status` 잡이 아티팩트로 넘기게 했는데, 그러면
# **아침 카드가 그 잡을 기다린다.** 배포 러너가 잡을 늦게 집으면 카드가 통째로
# 늦어지고, `allow_failure` 도 `timeout` 도 **대기 중인 잡에는 듣지 않는다**.
# 읽기 전용으로 물리면 잡도 의존도 필요 없다.
MOUNT_PATH = "/ec2-status/EC2-STATUS"


def candidates() -> list[str]:
    env_path = os.environ.get("EC2_STATUS_PATH")
    return ([env_path] if env_path else []) + [MOUNT_PATH]


def load(path: str = None) -> dict | None:
    """상태 파일. 없거나 깨졌으면 None — **그것도 알려야 할 상태다.**"""
    for p in ([path] if path else candidates()):
        try:
            with open(p, encoding="utf-8") as f:
                data = json.load(f)
        except (OSError, ValueError):
            continue
        if isinstance(data, dict):
            return data
    return None


def _age_minutes(at: str, now: datetime) -> float | None:
    try:
        return (now - datetime.fromisoformat(at)).total_seconds() / 60.0
    except (TypeError, ValueError):
        return None


def problems(data: dict | None, now: datetime | None = None) -> list[str]:
    """카드에 실을 줄. **정상이면 빈 목록**이다.

    숫자를 반드시 싣는다 — "디스크 부족" 은 사람이 서버에 들어가 다시 재게 만들고,
    "디스크 84% (여유 49 GB)" 는 그러지 않아도 되게 한다.
    """
    now = now or datetime.now(KST)

    if data is None:
        return [f"🖥 **서버 상태를 읽지 못했다** — `{MOUNT_PATH}` 가 없거나 깨졌다. "
                "호스트의 `metagame-status.timer` 와 러너 마운트를 확인하세요"]

    out: list[str] = []

    age = _age_minutes(data.get("at", ""), now)
    if age is None:
        out.append("🖥 **서버 상태에 잰 시각이 없다** — `ec2-status.sh` 가 옛 판일 수 있다")
    elif age > STALE_MINUTES:
        out.append(f"🖥 **서버 상태가 {int(age)}분 전 것이다** (5분마다 재야 한다) — "
                   "`metagame-status.timer` 가 멈췄는지 보세요")
        # 오래된 값으로 나머지를 판정하지 않는다. 그러면 이미 고쳐진 것을 계속 말한다.
        return out

    # **없는 값을 0 으로 읽지 않는다.** `_num` 의 기본값이 0 이라, 호스트가 필드
    # 이름을 바꾸면 디스크 0%·재시작 0회로 읽혀 **영원히 조용해진다** — 이 파일이
    # 막으려던 바로 그 상태다. 검사는 필드 *이름*만 잠그므로 값이 비는 경우는
    # 여기서 잡는다.
    absent = [label for label, path in (
        ("디스크 사용률", ("disk", "used_pct")),
        ("디스크 여유", ("disk", "free_gb")),
        ("메모리 여유", ("mem", "available_gb")),
    ) if _reach(data, path) is None]
    if absent:
        out.append(f"🖥 **상태 파일에 {' · '.join(absent)} 이(가) 없다** — "
                   "재는 쪽과 읽는 쪽이 어긋났다. `ec2-status.sh` 를 다시 설치하세요")

    # **없는 값은 여기서 다시 말하지 않는다** (S15P21B202-467, `!374` 리뷰). `_num` 의
    # 기본값이 0 이라 `available_gb` 가 없으면 `0 < 3` 이 참이 되어 **"메모리 여유 0 GB"**
    # 라는 없는 장애를 바로 위 "값이 없다" 줄 뒤에 한 번 더 붙였다. 카드를 보는 사람은
    # 두 줄을 서로 다른 문제로 읽는다.
    used_pct = _reach(data, ("disk", "used_pct"))
    if used_pct is not None and used_pct >= DISK_PCT_WARN:
        free_gb = _reach(data, ("disk", "free_gb"))
        free = f"{free_gb:.0f} GB" if free_gb is not None else "?"
        out.append(f"💽 **디스크 {used_pct:.0f}%** (여유 {free}) — "
                   f"{DISK_PCT_WARN}% 를 넘었다. 차면 빌드와 배포가 같이 죽는다")

    avail_gb = _reach(data, ("mem", "available_gb"))
    if avail_gb is not None and avail_gb < MEM_FREE_GB_WARN:
        total_gb = _reach(data, ("mem", "total_gb"))
        total = f"{total_gb:.0f} GB" if total_gb is not None else "?"
        out.append(f"🧠 **메모리 여유 {avail_gb:.0f} GB** "
                   f"(전체 {total}) — {MEM_FREE_GB_WARN} GB 밑이다")

    for c in data.get("containers") or []:
        name = c.get("name", "?")
        if not c.get("running"):
            out.append(f"📦 **{name} 가 떠 있지 않다**")
        elif _num(c.get("restarts")) >= RESTART_WARN and _recently_up(c):
            out.append(f"📦 **{name} 가 {_num(c.get('restarts')):.0f}번 재시작했고 "
                       f"{_num(c.get('uptime_secs')) / 60:.0f}분 전에 떴다** — "
                       "떠 있어도 죽고 있는 중이다")

    for b in data.get("backends") or []:
        code = str(b.get("code") or "000")
        # **403 은 정상이다.** `/actuator/health` 가 인증에 막혀서 그렇고, 그래도
        # 스프링이 응답했다는 뜻이다. 앱이 죽으면 curl 이 `000`(연결 거부)을 준다.
        if code == "000" or code.startswith("5"):
            out.append(f"🔌 **{b.get('name')} 백엔드가 응답하지 않는다** "
                       f"(:{b.get('port')} → {code})")

    units = data.get("units") or {}
    for name, state in units.items():
        if name in MUST_BE_INACTIVE:
            if state == "active":
                out.append(f"⚠ **{name} 가 살아났다** — 꺼져 있어야 하는 것이다")
        elif state != "active":
            out.append(f"⚙ **{name} 가 {state or '알 수 없음'}** — active 여야 한다")

    # **만료된 것과 못 읽은 것을 가른다.** 둘을 묶으면 가장 심각한 상태(이미 만료돼
    # 접속이 전부 막힌 상태)가 가장 약한 문구로 나간다.
    # **넓은 검사가 있으면 그것만 쓴다** (S15P21B202-620). 둘 다 돌리면 `deploy.sh` 가
    # 두 번 보고된다 — 같은 사실이 두 줄이면 읽는 사람이 둘인 줄 안다.
    if data.get("host_files"):
        out.extend(host_files_drift(data))
    else:
        drift = deploy_sh_drift(data)
        if drift:
            out.append(drift)

    raw_days = data.get("cert_days_left")
    if raw_days is None:
        out.append("🔐 **인증서 만료일을 읽지 못했다** — `/etc/letsencrypt` 를 볼 수 있는지 확인하세요")
    else:
        days = _num(raw_days)
        if days < 0:
            out.append(f"🔐 **인증서가 {-days:.0f}일 전에 만료됐다** — "
                       "지금 접속이 전부 막혀 있다. certbot 을 즉시 확인하세요")
        elif days <= CERT_DAYS_WARN:
            out.append(f"🔐 **인증서가 {days:.0f}일 남았다** — certbot 자동 갱신을 확인하세요")

    return out


# 저장소의 배포 스크립트. **`__file__` 기준 절대 경로다** (`!426` 리뷰).
# 상대 경로로 열면 잡의 작업 디렉터리가 저장소 루트가 아닐 때 조용히 못 읽고,
# 그러면 **드리프트가 있어도 아무 말을 안 한다** — 이 검사가 막으려던 바로 그 모양이다.
REPO_DEPLOY_SH = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "scripts", "deploy", "host", "deploy.sh")


#: 저장소에 **정본이 있는** 호스트 파일들 (S15P21B202-620). `(호스트 경로, 저장소 경로)`.
#:
#: **이 목록은 `scripts/deploy/host/ec2-status.sh` 의 `WATCHED` 와 같아야 한다** —
#: 호스트는 저장소를 갖고 있지 않아 "무엇을 잴지" 를 스스로 알아야 하고, 여기는
#: "무엇과 견줄지" 를 안다. 한쪽만 늘면 그 파일은 조용히 감시 밖이 된다.
#: `test_ec2_status.py` 가 둘을 대조한다.
#:
#: **왜 이만큼 넓히나.** `deploy.sh` 하나만 보던 동안 호스트 파일이 **네 번** 조용히
#: 낡았다: `deploy.sh` 7일(`-485`) · `docker-compose.yml` 4일 · `claude-token-refresh.*`
#: 5일 · `ec2-cleanup.sh` **8일**(2026-09-19 발견). 마지막 것은 `-467` 의 떠돌이 이미지
#: 보호가 통째로 빠진 상태였고 — Unity 이미지 17.9GB 를 지킬 장치가 없었다 —
#: 그동안 아무것도 빨갛지 않았다.
#:
#: **`/etc/gitlab-runner/config.toml` 은 넣지 않는다.** 저장소에 정본이 없다(토큰이
#: 들어 있어 둘 수 없다). 견줄 기준이 없는 것을 이 목록에 넣으면 매일 "다르다" 가 난다.
HOST_FILES = (
    ("/opt/metagame-deploy/deploy.sh", "scripts/deploy/host/deploy.sh"),
    ("/opt/metagame-deploy/ec2-status.sh", "scripts/deploy/host/ec2-status.sh"),
    ("/opt/metagame-deploy/ec2-cleanup.sh", "scripts/deploy/host/ec2-cleanup.sh"),
    ("/opt/metagame-runner/claude_auth_check.py", "scripts/runner/claude_auth_check.py"),
    ("/etc/systemd/system/metagame-deploy.service", "scripts/deploy/host/metagame-deploy.service"),
    ("/etc/systemd/system/metagame-deploy.path", "scripts/deploy/host/metagame-deploy.path"),
    ("/etc/systemd/system/metagame-deploy-dev.service", "scripts/deploy/host/metagame-deploy-dev.service"),
    ("/etc/systemd/system/metagame-deploy-dev.path", "scripts/deploy/host/metagame-deploy-dev.path"),
    ("/etc/systemd/system/metagame-status.service", "scripts/deploy/host/metagame-status.service"),
    ("/etc/systemd/system/metagame-status.timer", "scripts/deploy/host/metagame-status.timer"),
    ("/etc/systemd/system/metagame-cleanup.service", "scripts/deploy/host/metagame-cleanup.service"),
    ("/etc/systemd/system/metagame-cleanup.timer", "scripts/deploy/host/metagame-cleanup.timer"),
    ("/etc/systemd/system/claude-token-refresh.service", "scripts/runner/claude-token-refresh.service"),
    ("/etc/systemd/system/claude-token-refresh.timer", "scripts/runner/claude-token-refresh.timer"),
    ("/home/ubuntu/metagame/docker-compose.yml", "scripts/deploy/server/compose.prod.yml"),
    ("/home/ubuntu/metagame-dev/docker-compose.yml", "scripts/deploy/server/compose.dev.yml"),
    ("/etc/nginx/conf.d/upgrade-map.conf", "scripts/deploy/server/nginx/upgrade-map.conf"),
    ("/etc/nginx/conf.d/limits.conf", "scripts/deploy/server/nginx/limits.conf"),
)

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

#: 한 번에 몇 줄까지 말할까. 설치를 통째로 빠뜨리면 18줄이 카드를 덮는다 — 그러면
#: 아무도 안 읽는다(`-590`). 넘으면 수만 말하고 나머지는 잡 로그에 남긴다.
MAX_DRIFT_LINES = 4


def repo_sha(rel: str) -> str | None:
    """저장소본의 해시. 못 읽으면 None — **같다고 읽지 않는다.**"""
    try:
        with open(os.path.join(REPO_ROOT, *rel.split("/")), "rb") as f:
            return hashlib.sha256(f.read().replace(b"\r", b"")).hexdigest()
    except OSError:
        return None


def host_files_drift(data: dict) -> list[str]:
    """호스트 파일들이 저장소본과 같은가 (S15P21B202-620).

    **세 가지를 가른다** — 뭉치면 가장 심한 것이 가장 약한 문구로 나간다.

      · 다르다      — 손으로 고쳤거나 설치를 빠뜨렸다. 이것이 네 번 났다
      · 호스트에 없다 — 설치 자체가 안 됐다
      · 못 견줬다    — 저장소본을 못 읽었다. 검사가 죽은 것이지 괜찮은 것이 아니다
    """
    live = data.get("host_files") or {}
    changed, missing, unknown = [], [], []
    for host_path, rel in HOST_FILES:
        got = (live.get(host_path) or "").strip()
        want = repo_sha(rel)
        if want is None:
            unknown.append(host_path)
        elif not got:
            missing.append(host_path)
        elif got != want:
            changed.append((host_path, got, want))

    out = []
    if changed:
        head = (f"🖥 **호스트 파일 {len(changed)}개가 저장소본과 다르다** — "
                f"낡은 채로 돌면 **고쳐 둔 것이 안 돌고도 아무것도 빨갛지 않다**(`-485`·`-620`)")
        out.append(head)
        for host_path, got, want in changed[:MAX_DRIFT_LINES]:
            out.append(f"   · `{host_path}` — 호스트 `{got[:8]}` · 저장소 `{want[:8]}`")
        if len(changed) > MAX_DRIFT_LINES:
            out.append(f"   · … 그 밖 {len(changed) - MAX_DRIFT_LINES}개(잡 로그 참고)")
    if missing:
        out.append(f"🖥 **호스트에 없는 파일 {len(missing)}개** — 설치가 빠졌다: "
                   + " · ".join(f"`{p}`" for p in missing[:MAX_DRIFT_LINES]))
    if unknown:
        out.append(f"🖥 **저장소본을 못 읽어 {len(unknown)}개를 견주지 못했다** — "
                   f"드리프트가 있어도 안 보이는 상태다: "
                   + " · ".join(f"`{p}`" for p in unknown[:MAX_DRIFT_LINES]))
    return out


def deploy_sh_drift(data: dict) -> str | None:
    """호스트의 배포 스크립트가 **저장소본과 다른가** (S15P21B202-485).

    2026-09-14 발견: `/opt/metagame-deploy/deploy.sh` 가 7일 동안 낡아 있었다.
    `-284` 의 dev 지원이 호스트에 없어 **dev 배포가 prod 포트로 헬스체크**했고,
    그러면 dev 가 기동에 실패해도 `배포 완료` 로 끝나고 롤백도 안 한다. 같은
    디렉터리의 다른 스크립트는 갱신돼 있었는데 이것만 빠졌고, **아무것도
    빨갛지 않았다.**

    `-508`(런처)과 같은 방식이다 — 고쳐 주지는 않고 **하루 안에 보이게** 한다.
    """
    live = data.get("deploy_sh_sha256")
    if not live:
        # **재지 못한 것과 같은 것은 다르다.** 호스트 스크립트가 옛 판이면 이 값을
        # 아예 안 보내므로, 조용히 넘기면 그 상태가 영원히 안 보인다.
        return ("🖥 **배포 스크립트 해시가 상태 파일에 없다** — 호스트의 "
                "`ec2-status.sh` 가 옛 판이다. 다시 설치하세요")
    try:
        with open(REPO_DEPLOY_SH, "rb") as f:
            want = hashlib.sha256(f.read().replace(b"\r", b"")).hexdigest()
    except OSError as e:
        # **조용히 넘어가지 않는다** (`!426` 리뷰). 못 읽은 것과 같은 것은 다르다 —
        # 여기서 None 을 돌려주면 이 검사가 통째로 죽어도 화면이 똑같다.
        return (f"🖥 **저장소의 배포 스크립트를 못 읽었다** — `{REPO_DEPLOY_SH}` "
                f"({type(e).__name__}). 견줄 기준이 없어 드리프트를 판정하지 못했다")
    if live == want:
        return None
    return (f"🖥 **호스트의 배포 스크립트가 저장소본과 다르다** — 호스트 `{live[:8]}` · "
            f"저장소 `{want[:8]}`. 낡은 채로 돌면 **배포가 성공한 척할 수 있다**"
            f"(`-485`). `/opt/metagame-deploy/deploy.sh` 를 다시 올리세요")


def _reach(data: dict, path: tuple):
    """중첩 키를 따라간다. 없거나 숫자가 아니면 None — **0 과 구별한다.**"""
    cur = data
    for k in path:
        if not isinstance(cur, dict):
            return None
        cur = cur.get(k)
    return cur if isinstance(cur, (int, float)) and not isinstance(cur, bool) else None


def _recently_up(c: dict) -> bool:
    """방금 뜬 컨테이너인가. 값이 없으면(`-1`) **울리지 않는다** — 옛 호스트
    스크립트가 이 필드를 안 보내던 때가 있고, 그때 매일 울리게 하면 안 된다."""
    secs = _num(c.get("uptime_secs"), default=-1)
    return 0 <= secs <= FRESH_RESTART_SECS


def _num(v, default: float = 0.0) -> float:
    try:
        return float(v)
    except (TypeError, ValueError):
        return default


def section(data: dict | None, now: datetime | None = None) -> str:
    """카드에 붙일 절. 정상이면 **빈 문자열**이라 아무것도 안 붙는다."""
    rows = problems(data, now)
    if not rows:
        return ""
    head = f"**🖥 서버** ({len(rows)})"
    return head + chr(10) * 2 + chr(10).join(f"- {r}" for r in rows)


def tail(data: dict | None, now: datetime | None = None) -> str:
    """꼬리에 붙일 **한 줄** — 지금 자원이 얼마나 쓰이고 있는지 (S15P21B202-486).

    `section()` 은 **문제가 있을 때만** 말한다. 그래서 화면이 조용할 때
    "서버가 멀쩡하다" 와 "재는 쪽이 죽었는데 그것도 못 알아챘다" 가 구별되지 않는다.
    2026-09-12 에 개발 배포가 1일 15시간 조용히 멈춰 있던 것(`-483`)이 그 모양이었다.
    `-458` 이 EC2 를 카드에 넣은 이유가 "조용히 나빠져도 아무도 모른다" 였는데,
    정작 **나빠지기 전에는 아무 숫자도 안 보였다.**

    **블록이 아니라 꼬리다** — 조치 항목이 아니므로 이것만으로 카드를 보내지
    않는다(`-144` 의 🚀 와 같은 결).

    못 읽었으면 **빈 문자열**이다. 그 경우는 `section()` 이 블록으로 크게 말하고,
    꼬리에까지 적으면 같은 말이 카드에 두 번 나온다.
    """
    if data is None:
        return ""

    parts: list[str] = []

    # **없는 값은 그 조각만 뺀다.** 0 으로 읽으면 "디스크 0%" 같은 없는 사실이
    # 실린다 — `-467` 에서 정한 규칙이다. `_reach` 가 None 과 0 을 구별한다.
    used = _reach(data, ("disk", "used_pct"))
    free = _reach(data, ("disk", "free_gb"))
    if used is not None:
        parts.append(f"디스크 {used:.0f}%" + (f" (여유 {free:.0f}GB)" if free is not None else ""))
    elif free is not None:
        parts.append(f"디스크 여유 {free:.0f}GB")

    avail = _reach(data, ("mem", "available_gb"))
    total = _reach(data, ("mem", "total_gb"))
    if avail is not None:
        parts.append(f"메모리 여유 {avail:.0f}" + (f"/{total:.0f}GB" if total is not None else "GB"))

    docker = _reach(data, ("sizes", "docker_gb"))
    if docker is not None:
        parts.append(f"도커 {docker:.0f}GB")

    cert = _reach(data, ("cert_days_left",))
    if cert is not None:
        parts.append(f"인증서 {cert:.0f}일")

    if not parts:
        return ""          # 숫자가 하나도 없으면 꼬리를 만들지 않는다

    # **잰 시각을 함께 싣는다.** 숫자만 있으면 그것이 방금 잰 것인지 어제 것인지
    # 알 수 없다. 20분을 넘기면 `problems()` 가 블록으로 따로 말한다.
    age = _age_minutes(data.get("at", ""), now or datetime.now(KST))
    when = f"{int(age)}분 전" if age is not None and age >= 0 else "잰 시각 불명"
    return "🖥 서버: " + " · ".join(parts) + f" · {when}"


if __name__ == "__main__":  # 손으로 확인할 때
    d = load()
    print("===== ec2-status =====")
    if d is None:
        print("  상태 파일을 읽지 못했다:", MOUNT_PATH)
    else:
        print(f"  잰 시각 {d.get('at')} · 디스크 {(d.get('disk') or {}).get('used_pct')}% · "
              f"메모리 여유 {(d.get('mem') or {}).get('available_gb')} GB · "
              f"인증서 {d.get('cert_days_left')}일")
    rows = problems(d)
    for r in rows:
        print("  ⚠ " + r)
    if not rows:
        print("  ✅ 알릴 것 없음")
    # **여기서 실패로 끝내지 않는다.** 이 잡이 빨개지면 아침 카드가 통째로 안 나간다.
