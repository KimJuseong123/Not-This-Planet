"""EC2 상태를 아침 카드에 실을 줄로 바꾼다 (S15P21B202-458).

**왜 필요한가.** 아침 점검(`mm-digest`)은 Jira 와 GitLab 만 본다. 서버는 아무도
안 본다 — 그래서 조용히 나빠진다. 2026-09-11 에 개발 백엔드가 `JWT_SECRET` 때문에
재시작 루프였는데, 사람이 `docker ps` 를 쳐 봤기에 알았다. 그러지 않았으면
그날 테스트가 통째로 막혔을 것이다.

**어떻게 보나.** 잡은 도커 컨테이너 안이라 호스트를 직접 못 본다. 호스트의
`ec2-status.sh` 가 5분마다 재서 JSON 한 장을 배포 스풀에 놓고, 배포 러너에서 도는
잡이 그것을 읽는다. `deploy.sh` 의 `STATUS` 와 같은 방식이고 이유도 같다 —
**도커 소켓도 SSH 키도 CI 에 주지 않는다**(-139 · -252 · -324).

**조용하면 아무 말도 안 한다.** `mm-digest` 규칙 그대로다. 서버가 멀쩡하면 줄이
없어야 하고, 그래야 줄이 있을 때 사람이 본다.
"""
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
STALE_MINUTES = 20        # 5분 주기가 네 번 빠지면 재는 쪽이 멈춘 것이다

# 꺼져 있어야 하는 것. 살아나면 알린다 — 지금은 확인하는 장치가 없다.
MUST_BE_INACTIVE = ("metagame.service",)

# 찾는 자리 — 앞에서부터 처음 읽히는 것을 쓴다.
#   ① 환경 변수    검사·손으로 확인할 때
#   ② 아티팩트     `mm-digest` 는 공용 러너라 `/spool` 이 안 보인다. `ec2-status`
#                  잡(배포 러너)이 스풀에서 꺼내 아티팩트로 넘겨 준다
#   ③ 스풀         `ec2-status` 잡 자신이 읽는 자리
ARTIFACT_NAME = "ec2-status.json"
SPOOL_PATH = "/spool/EC2-STATUS"


def candidates() -> list[str]:
    env_path = os.environ.get("EC2_STATUS_PATH")
    return ([env_path] if env_path else []) + [ARTIFACT_NAME, SPOOL_PATH]


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
        return ["🖥 **서버 상태를 읽지 못했다** — `/spool/EC2-STATUS` 가 없거나 깨졌다. "
                "호스트의 `metagame-status.timer` 를 확인하세요"]

    out: list[str] = []

    age = _age_minutes(data.get("at", ""), now)
    if age is None:
        out.append("🖥 **서버 상태에 잰 시각이 없다** — `ec2-status.sh` 가 옛 판일 수 있다")
    elif age > STALE_MINUTES:
        out.append(f"🖥 **서버 상태가 {int(age)}분 전 것이다** (5분마다 재야 한다) — "
                   "`metagame-status.timer` 가 멈췄는지 보세요")
        # 오래된 값으로 나머지를 판정하지 않는다. 그러면 이미 고쳐진 것을 계속 말한다.
        return out

    disk = data.get("disk") or {}
    if _num(disk.get("used_pct")) >= DISK_PCT_WARN:
        out.append(f"💽 **디스크 {_num(disk.get('used_pct')):.0f}%** (여유 {_num(disk.get('free_gb')):.0f} GB) — "
                   f"{DISK_PCT_WARN}% 를 넘었다. 차면 빌드와 배포가 같이 죽는다")

    mem = data.get("mem") or {}
    if _num(mem.get("available_gb")) < MEM_FREE_GB_WARN:
        out.append(f"🧠 **메모리 여유 {_num(mem.get('available_gb')):.0f} GB** "
                   f"(전체 {_num(mem.get('total_gb')):.0f} GB) — {MEM_FREE_GB_WARN} GB 밑이다")

    for c in data.get("containers") or []:
        name = c.get("name", "?")
        if not c.get("running"):
            out.append(f"📦 **{name} 가 떠 있지 않다**")
        elif _num(c.get("restarts")) >= RESTART_WARN:
            out.append(f"📦 **{name} 가 {_num(c.get('restarts')):.0f}번 재시작했다** — "
                       "떠 있어도 죽고 있는 중일 수 있다")

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

    days = _num(data.get("cert_days_left"), default=-1)
    if days < 0:
        out.append("🔐 **인증서 만료일을 읽지 못했다**")
    elif days <= CERT_DAYS_WARN:
        out.append(f"🔐 **인증서가 {days:.0f}일 남았다** — certbot 자동 갱신을 확인하세요")

    return out


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


if __name__ == "__main__":  # 잡에서 직접 볼 때 (사람이 읽는 출력)
    d = load()
    print("===== ec2-status =====")
    # **스풀에서 읽은 것을 아티팩트로 넘긴다.** `mm-digest` 는 공용 러너에서 돌아
    # `/spool` 이 안 보인다 — 이 잡이 다리 역할을 한다.
    if d is not None:
        try:
            with open(ARTIFACT_NAME, "w", encoding="utf-8") as f:
                json.dump(d, f, ensure_ascii=False, indent=2)
        except OSError as e:
            print(f"  아티팩트를 쓰지 못했다: {e}")
    if d is None:
        print("  상태 파일을 읽지 못했다:", SPOOL_PATH)
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
