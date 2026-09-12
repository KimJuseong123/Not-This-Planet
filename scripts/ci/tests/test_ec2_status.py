"""서버 상태 판정 (S15P21B202-458).

실행: python scripts/ci/tests/test_ec2_status.py
네트워크·토큰·서버 없이 돈다.

**왜 이 검사가 있나.** 이 판정은 서버가 나빠진 아침에만 돈다 — 우리가 평소에 볼 일이
없다. 조용히 틀리면 **나쁜 날에 아무 말도 안 하고**, 그것이 바로 이 기능이 없던
상태다. 그래서 세 경우를 전부 잠근다: 임계를 넘음 · 안 넘음 · 아예 못 읽음.
"""
import json
import os
import sys
import tempfile
from datetime import datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, os.path.dirname(HERE))
os.chdir(ROOT)

import ec2_status as E  # noqa: E402

print()
print("== 서버 상태 ==")

NOW = datetime(2026, 9, 12, 9, 30, tzinfo=E.KST)


def healthy(**over) -> dict:
    """2026-09-11 실측을 그대로 옮긴 정상값. 여기서 출발해 한 항목씩 망가뜨린다."""
    d = {
        "at": (NOW - timedelta(minutes=2)).isoformat(),
        "host": "ip-172-26-13-34",
        "uptime_days": 11,
        "disk": {"used_pct": 20, "free_gb": 250},
        "mem": {"total_gb": 15, "available_gb": 12},
        "containers": [
            {"name": "metagame-backend", "running": True, "restarts": 0},
            {"name": "metagame-mysql", "running": True, "restarts": 0},
            {"name": "metagame-dev-backend", "running": True, "restarts": 1,
             "uptime_secs": 7 * 3600},
            {"name": "metagame-dev-mysql", "running": True, "restarts": 0},
        ],
        "backends": [
            {"name": "prod", "port": 8080, "code": "403"},
            {"name": "dev", "port": 8081, "code": "403"},
        ],
        "units": {
            "gitlab-runner": "active", "metagame-relay": "active",
            "metagame-deploy.path": "active", "metagame-deploy-dev.path": "active",
            "metagame.service": "inactive",
        },
        "cert_days_left": 62,
        "sizes": {"docker_gb": 33, "builds_mb": 157, "site_mb": 96, "runlogs_mb": 0},
    }
    d.update(over)
    return d


# ── ① 멀쩡하면 아무 말도 안 한다 ─────────────────────────────────────────
#
#     이 카드의 규칙이다. 정상일 때 한 줄이라도 붙으면 매일 울리고, 그러면 사람이
#     카드를 안 본다 — 정작 나쁜 날에 못 읽는 것과 같아진다.
assert E.problems(healthy(), NOW) == [], E.problems(healthy(), NOW)
assert E.section(healthy(), NOW) == "", "정상인데 절이 붙는다"
print("  멀쩡하면 조용하다 ✅")


# ── ①b 403 을 죽은 것으로 보지 않는다 ────────────────────────────────────
#
#     `/actuator/health` 는 인증에 막혀 403 을 준다. 그것을 실패로 읽으면 **매일
#     아침 두 줄이 뜬다.** 앱이 죽으면 curl 이 000(연결 거부)을 주고, 그것이
#     우리가 잡으려는 값이다.
_p = E.problems(healthy(backends=[{"name": "prod", "port": 8080, "code": "000"}]), NOW)
assert len(_p) == 1 and "응답하지 않는다" in _p[0], _p
assert E.problems(healthy(backends=[{"name": "prod", "port": 8080, "code": "503"}]), NOW), \
    "5xx 는 잡아야 한다"
assert E.problems(healthy(backends=[{"name": "prod", "port": 8080, "code": "200"}]), NOW) == []
print("  403 은 살아 있는 것, 000·5xx 는 죽은 것 ✅")


# ── ② 임계를 넘으면 숫자와 함께 말한다 ───────────────────────────────────
#
#     "디스크 부족" 은 사람을 서버에 들어가 다시 재게 만든다. 숫자가 있으면 그러지
#     않아도 된다.
_p = E.problems(healthy(disk={"used_pct": 84, "free_gb": 49}), NOW)
assert len(_p) == 1 and "84" in _p[0] and "49" in _p[0], _p

_p = E.problems(healthy(mem={"total_gb": 15, "available_gb": 1}), NOW)
assert len(_p) == 1 and "메모리" in _p[0], _p

_p = E.problems(healthy(cert_days_left=12), NOW)
assert len(_p) == 1 and "12일" in _p[0], _p

#     경계 바로 아래는 울리지 않는다 — 79% 로 매일 울리면 80% 라는 선이 뜻을 잃는다.
assert E.problems(healthy(disk={"used_pct": 79, "free_gb": 60}), NOW) == []
assert E.problems(healthy(disk={"used_pct": 80, "free_gb": 60}), NOW) != []
print("  임계를 넘으면 숫자를 싣고, 바로 아래에서는 조용하다 ✅")


# ── ③ 떠 있어도 죽고 있는 것을 잡는다 ────────────────────────────────────
#
#     2026-09-11 dev 백엔드가 JWT_SECRET 때문에 재시작 루프였다. `docker ps` 만 보면
#     "Up 3 seconds" 라 멀쩡해 보인다 — **재시작 횟수를 봐야 보인다.**
_c = healthy()["containers"]
_c[2] = {"name": "metagame-dev-backend", "running": True, "restarts": 12, "uptime_secs": 40}
_p = E.problems(healthy(containers=_c), NOW)
assert len(_p) == 1 and "재시작" in _p[0] and "12" in _p[0], _p

_c[2] = {"name": "metagame-dev-backend", "running": False, "restarts": 0, "uptime_secs": -1}
_p = E.problems(healthy(containers=_c), NOW)
assert len(_p) == 1 and "떠 있지 않다" in _p[0], _p
print("  재시작 루프와 죽은 컨테이너를 가른다 ✅")


# ── ③b 누적 재시작만으로 매일 울리지 않는다 ──────────────────────────────
#
#     `RestartCount` 는 컨테이너를 만든 뒤의 **누적값**이다. 지난주에 열두 번
#     죽었다가 지금 멀쩡한 것도 그 숫자를 영원히 달고 있다. 그것만 보고 울리면
#     **매일 아침 같은 줄이 붙고**, 그러면 사람이 카드를 안 본다 — 이 MR 자신이
#     내세운 전제와 정면으로 어긋난다.
_c = healthy()["containers"]
_c[2] = {"name": "metagame-dev-backend", "running": True, "restarts": 12,
         "uptime_secs": 3 * 24 * 3600}          # 사흘째 안정
assert E.problems(healthy(containers=_c), NOW) == [],     "옛 재시작 기록만으로 울린다 — 매일 아침 같은 줄이 붙는다"

#     값이 없으면(-1, 옛 호스트 스크립트) 울리지 않는다.
_c[2] = {"name": "metagame-dev-backend", "running": True, "restarts": 12, "uptime_secs": -1}
assert E.problems(healthy(containers=_c), NOW) == []

#     **값이 아예 없으면 0 이 아니라 "못 읽었다" 로 말한다.** `_num` 의 기본값이 0 이라
#     호스트가 필드 이름을 바꾸면 디스크 0%·재시작 0회로 읽혀 **영원히 조용해진다** —
#     이 파일이 막으려던 바로 그 상태다. 검사는 필드 *이름*만 잠그므로 값이 비는
#     경우는 판정 쪽에서 잡아야 한다.
_p = E.problems(healthy(disk={}), NOW)
assert any("없다" in r for r in _p), f"디스크 값이 없는데 조용하다: {_p}"
_p = E.problems(healthy(mem={"total_gb": 15}), NOW)
assert any("메모리 여유" in r and "없다" in r for r in _p), f"메모리 값이 없는데 조용하다: {_p}"
#     0 은 정상값이다 — 없는 것과 구별해야 한다.
assert E.problems(healthy(disk={"used_pct": 0, "free_gb": 300}), NOW) == []

#     호스트가 실제로 그 값을 보내는지 — 한쪽만 고치면 영원히 조용해진다.
_sh_up = open(os.path.join(ROOT, "scripts/deploy/host/ec2-status.sh"), encoding="utf-8").read()
assert '"uptime_secs"' in _sh_up, "호스트가 uptime_secs 를 안 보낸다"
print("  옛 재시작 기록만으로는 울리지 않는다 ✅")


# ── ④ 꺼져 있어야 하는 것이 살아난 것도 잡는다 ───────────────────────────
#
#     이슈가 콕 집어 요구한 항목이다. `metagame.service` 는 데디케이티드 서버 시절
#     유닛이고, 살아나도 지금은 아무도 모른다.
_u = dict(healthy()["units"], **{"metagame.service": "active"})
_p = E.problems(healthy(units=_u), NOW)
assert len(_p) == 1 and "살아났다" in _p[0], _p

_u = dict(healthy()["units"], **{"gitlab-runner": "failed"})
_p = E.problems(healthy(units=_u), NOW)
assert len(_p) == 1 and "gitlab-runner" in _p[0], _p
print("  살아나면 안 되는 것과 죽으면 안 되는 것을 둘 다 본다 ✅")


# ── ⑤ 못 읽는 것도 알려야 할 상태다 ──────────────────────────────────────
#
#     **조용한 것과 못 읽은 것을 구별하지 않으면** 재는 쪽이 멈춰도 "서버가 멀쩡하다"
#     로 읽힌다. 그것이 이 기능이 없던 상태와 같다.
_p = E.problems(None, NOW)
assert len(_p) == 1 and "읽지 못했다" in _p[0], _p
assert E.section(None, NOW) != "", "못 읽었는데 절이 안 붙는다"

#     오래된 값도 마찬가지다. 그리고 **오래된 값으로 나머지를 판정하지 않는다** —
#     이미 고쳐진 것을 계속 말하게 된다.
_stale = healthy(at=(NOW - timedelta(hours=3)).isoformat(),
                 disk={"used_pct": 99, "free_gb": 1})
_p = E.problems(_stale, NOW)
assert len(_p) == 1 and "180분 전" in _p[0], _p

_p = E.problems(healthy(at="어제쯤"), NOW)
assert len(_p) >= 1 and "잰 시각" in _p[0], _p
print("  못 읽음·오래됨을 조용함과 구별한다 ✅")


# ── ⑤b 만료된 인증서를 "못 읽음" 으로 말하지 않는다 ──────────────────────
#
#     둘을 묶으면 **가장 심각한 상태(이미 만료돼 접속이 전부 막힌 상태)가 가장 약한
#     문구로** 나간다. 호스트는 못 읽으면 null, 만료됐으면 음수를 보낸다.
_p = E.problems(healthy(cert_days_left=None), NOW)
assert len(_p) == 1 and "읽지 못했다" in _p[0], _p

_p = E.problems(healthy(cert_days_left=-3), NOW)
assert len(_p) == 1 and "만료됐다" in _p[0] and "3" in _p[0], _p
assert "읽지 못했다" not in _p[0], "만료된 것을 못 읽음으로 말한다"

#     호스트가 실제로 그렇게 보내는지도 본다 — 한쪽만 고치면 어긋난다.
_sh_cert = open(os.path.join(ROOT, "scripts/deploy/host/ec2-status.sh"), encoding="utf-8").read()
assert "maybe_num" in _sh_cert, "호스트가 못 읽음을 null 로 보내지 않는다"
print("  만료된 인증서와 못 읽은 인증서를 가른다 ✅")



# ── ⑥ 파일을 찾는 순서 ───────────────────────────────────────────────────
#
#     `mm-digest` 는 공용 러너라 `/spool` 이 안 보인다. `ec2-status` 잡이 아티팩트로
#     넘겨 주고, 그 이름으로 먼저 찾아야 한다.
#     **아침 카드가 잡을 기다리지 않아야 한다.** 한때 배포 스풀에 두고 중간 잡이
#     아티팩트로 넘겼는데, 그러면 그 잡이 대기하는 동안 카드가 통째로 늦어진다 —
#     `allow_failure` 도 `timeout` 도 **대기 중인 잡에는 듣지 않는다.**
assert E.candidates()[-1] == E.MOUNT_PATH
assert E.MOUNT_PATH.startswith("/ec2-status"), "모든 러너에 물리는 자리가 아니다"

_ci_needs = open(os.path.join(ROOT, ".gitlab-ci.yml"), encoding="utf-8").read()
_digest = _ci_needs.split("mm-digest:", 1)[1].split("script:", 1)[0]
assert "ec2-status" not in _digest,     "아침 카드가 다른 잡을 기다린다 — 그 잡이 대기하면 카드가 통째로 늦는다"

with tempfile.TemporaryDirectory() as td:
    _f = os.path.join(td, "s.json")
    with open(_f, "w", encoding="utf-8") as fh:
        json.dump(healthy(), fh)
    assert E.load(_f) is not None
    assert E.load(os.path.join(td, "없는파일.json")) is None

    #     **깨진 파일을 정상으로 읽으면 안 된다.** 호스트가 쓰는 도중이면 반쪽이다.
    with open(_f, "w", encoding="utf-8") as fh:
        fh.write('{"at": "2026-09-12T09:2')
    assert E.load(_f) is None, "반쪽짜리 JSON 을 읽었다"
print("  아티팩트 → 스풀 순서로 찾고, 깨진 파일은 안 읽는다 ✅")


# ── ⑦ 호스트 스크립트와 판정이 같은 것을 본다 ────────────────────────────
#
#     재는 쪽과 읽는 쪽이 어긋나면 **영원히 조용하다.** 필드 이름이 하나만 달라도
#     `.get()` 이 None 을 돌려주고 아무 줄도 안 붙는다 — 그것을 사람이 알아챌 길이 없다.
_sh = open(os.path.join(ROOT, "scripts/deploy/host/ec2-status.sh"), encoding="utf-8").read()
for key in ("used_pct", "free_gb", "available_gb", "restarts", "cert_days_left",
            "running", "code", "units", "at"):
    assert f'"{key}"' in _sh, f"호스트가 {key} 를 안 쓴다 — 판정이 영원히 조용해진다"

#     **`/auth/guest` 를 5분마다 부르면 게스트 사용자가 계속 쌓인다.** 주석에서 그
#     이유를 설명하는 것은 괜찮으므로, 실제로 도는 줄만 본다.
_code = chr(10).join(ln for ln in _sh.splitlines() if not ln.lstrip().startswith("#"))
assert "/auth/guest" not in _code, "상태 재기가 게스트를 만든다 — 5분마다 사용자가 쌓인다"

#     실행되는 코드는 스풀 밖에 있어야 한다(-324). 유닛이 스풀 경로를 부르면 안 된다.
_unit = open(os.path.join(ROOT, "scripts/deploy/host/metagame-status.service"), encoding="utf-8").read()
assert "/opt/metagame-deploy/" in _unit, "유닛이 스풀 밖 코드를 부르지 않는다"
assert "/home/ubuntu/metagame/deploy/ec2-status.sh" not in _unit, \
    "스풀 안의 스크립트를 root 로 실행한다 — 잡이 덮어쓸 수 있다(-324)"
print("  재는 쪽과 읽는 쪽이 같은 필드를 본다 ✅")


# ── ⑧ 아침 카드가 이 절을 실제로 붙인다 ──────────────────────────────────
_dig = open(os.path.join(ROOT, "scripts/ci/mm_digest.py"), encoding="utf-8").read()
assert "ec2_status.section" in _dig, "아침 카드가 서버 절을 안 붙인다"

#     설치가 **모든 러너에** 읽기 전용으로 물리는지. 배포 러너에만 물리면 공용
#     러너에서 도는 아침 카드가 서버를 못 본다. 쓰기를 열면 잡이 상태를 위조한다.
_inst = open(os.path.join(ROOT, "scripts/deploy/host/install-status.sh"), encoding="utf-8").read()
assert ":/ec2-status:ro" in _inst, "읽기 전용이 아니다 — 잡이 가짜 상태를 써 넣을 수 있다"

#     **빈 `volumes = []` 를 깨뜨리면 안 된다.** 예전 판은 `volumes = [, "…"]` 를
#     만들었고, 그 상태로 곧바로 러너를 재시작해 **CI 가 통째로 멈출** 수 있었다.
assert 'x.strip() for x in inner.split' in _inst, "빈 배열에서 쉼표가 앞에 붙는다"
assert "tomllib" in _inst, "고친 설정을 파싱해 보지 않고 러너를 재시작한다"
assert ".bak-" in _inst, "되돌릴 백업을 남기지 않는다"

#     **러너 하나라도 빠지면 실패로 끝낸다.** `volumes` 가 여러 줄인 항목은 정규식에
#     안 걸려 조용히 빠지고, 그 러너에서 도는 아침 카드는 매일 "못 읽었다" 만 붙인다.
assert "added + have != runners" in _inst, "손댄 러너 수를 러너 수와 대조하지 않는다"

#     재시작 뒤 러너가 살아났는지도 본다.
assert "is-active --quiet gitlab-runner" in _inst, "재시작 뒤 러너가 살아났는지 안 본다"
print("  모든 러너에 읽기 전용으로 물리고, 설정을 깨뜨리지 않는다 ✅")

print("== 통과 ==")
