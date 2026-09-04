"""아침 점검 카드가 사실을 말하는지 (S15P21B202-282).

실행: python scripts/ci/tests/test_digest_hygiene.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다. 현재 시각에도 의존하지 않는다(-281).

세 가지가 카드에 거짓을 실었다.
  ① 헤더 시각이 UTC — 09:33 발송 카드에 '00:33' 이 사흘 연속 찍혔다(잡 474023·476226·478559)
  ② 🌿 가 develop 에 이미 다 들어간 브랜치와 진짜 유실 후보를 **같은 문구로** 경고했다
     (`-126` 은 0커밋, `-74` 는 Main.unity 126줄이 develop 에 없었다)
  ③ 🚀 가 **시작조차 안 한** canceled 잡을 '배포 시도' 로 셌다. GitLab 이 같은 ref 의 새
     파이프라인을 만들 때 이전 파이프라인의 대기 잡을 자동 취소한다(09-03 09:52 실측)

셋 다 "카드를 못 믿게 만드는" 종류다. 카드가 틀린 것을 섞어 보내면 사람이 안 읽게 된다 —
지금 ⏰·👤 항목이 사흘째 무반응인 상태라 더 그렇다.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"
os.environ["GITLAB_TOKEN"] = "x"

import ci_common  # noqa: E402
import mm_digest as D  # noqa: E402

# ── ① 헤더 시각은 KST — UTC 오프셋이 붙은 현재 시각이어야 한다
from datetime import datetime, timedelta, timezone  # noqa: E402

head = D.now_kst()
assert re.fullmatch(r"\d{2}/\d{2} \d{2}:\d{2}", head), head
utc_now = datetime.now(timezone.utc)
kst_now = utc_now.astimezone(D.KST)
assert head == kst_now.strftime("%m/%d %H:%M"), f"KST 가 아니다: {head} (KST 는 {kst_now:%m/%d %H:%M})"
# naive now() 와 다르면 확실히 tz 를 붙인 것이다. 같은 시간대(UTC+9 머신)에서는 구분되지 않으므로
# 값 자체가 아니라 **함수가 tz-aware 를 쓰는지**를 본다.
assert "datetime.now()" not in open(
    os.path.join(os.path.dirname(HERE), "mm_digest.py"), encoding="utf-8"
).read().replace('naive datetime.now() 를', ''), "naive datetime.now() 가 남아 있다"
assert D.kst("2026-09-03T00:33:00.000Z") == "09/03 09:33", D.kst("2026-09-03T00:33:00.000Z")
print("  헤더·시각 변환 KST ✅")

# ── ② 🌿 — develop 에 없는 커밋 수로 빈 브랜치와 유실 후보를 가른다
MERGED = [
    {"iid": 121, "web_url": "https://lab/mr/121", "source_branch": "S15P21B202-74-map-tile-scene",
     "merged_at": "2026-08-27T09:00:00.000Z", "author": {"username": "suho6347"}},
    {"iid": 96, "web_url": "https://lab/mr/96", "source_branch": "S15P21B202-126-lobby",
     "merged_at": "2026-08-27T09:00:00.000Z", "author": {"username": "suho6347"}},
    {"iid": 60, "web_url": "https://lab/mr/60", "source_branch": "S15P21B202-9-gone",
     "merged_at": "2026-08-27T09:00:00.000Z", "author": {"username": "suho6347"}},
    {"iid": 61, "web_url": "https://lab/mr/61", "source_branch": "S15P21B202-10-unknown",
     "merged_at": "2026-08-27T09:00:00.000Z", "author": {"username": "suho6347"}},
]
AHEAD = {"S15P21B202-74-map-tile-scene": 2, "S15P21B202-126-lobby": 0, "S15P21B202-10-unknown": None}
ALIVE = {"S15P21B202-74-map-tile-scene", "S15P21B202-126-lobby", "S15P21B202-10-unknown"}
NOW = datetime(2026, 8, 28, tzinfo=timezone.utc)


def fake_http(method, url, headers=None, body=None, timeout=30):
    if "state=merged" in url:
        return 200, MERGED
    if "/repository/branches/" in url:
        br = url.rsplit("/", 1)[-1].replace("%2F", "/")
        return (200, {"commit": {"author_name": "김수호"}}) if br in ALIVE else (404, {"message": "404"})
    if "/repository/compare" in url:
        br = url.split("&to=")[1].split("&")[0].replace("%2F", "/")
        a = AHEAD.get(br)
        return (500, {"message": "boom"}) if a is None else (200, {"commits": [{}] * a})
    raise AssertionError(f"예상 못한 호출: {method} {url}")


ci_common.http = fake_http
D.http = fake_http
rows = D.resurrected_branches(ci_common.GitLab(), now=NOW)
assert len(rows) == 3, rows                      # 사라진 -9 는 행이 없다
by = {r.split("`")[1]: r for r in rows}
assert "2개 — develop 에 없음" in by["S15P21B202-74-map-tile-scene"], by
assert "0 — 지우면 끝" in by["S15P21B202-126-lobby"], by
assert "확인 실패" in by["S15P21B202-10-unknown"], by
assert "@suho6347" in by["S15P21B202-74-map-tile-scene"] or "@" in by["S15P21B202-74-map-tile-scene"]
assert all(r.count("|") == 5 for r in rows), f"4칸 표여야 한다: {rows}"
print("  🌿 빈 브랜치 vs 유실 후보 구분 ✅")

# ── ③ 🚀 — 시작 안 한 canceled 는 배포 시도가 아니다
PIPES, JOBS = [], {}


def job(jid, status, sha, started="2026-09-03T00:52:01.000Z", finished="2026-09-03T00:53:06.000Z"):
    return {"id": jid, "name": "deploy-backend", "status": status, "started_at": started,
            "finished_at": finished, "web_url": f"https://lab/jobs/{jid}",
            "commit": {"short_id": sha, "id": sha + "0" * 33}}


def deploy_http(method, url, headers=None, body=None, timeout=30):
    if "/pipelines?ref=" in url:
        return 200, PIPES
    if "/pipelines/" in url and url.endswith("/jobs?per_page=100"):
        return 200, JOBS.get(int(url.split("/pipelines/")[1].split("/")[0]), [])
    raise AssertionError(url)


ci_common.http = deploy_http
D.http = deploy_http
gl = ci_common.GitLab()

# 09-03 실제 입력: manual 대기 + 시작조차 안 한 canceled(중복 파이프라인 자동취소)
PIPES[:] = [{"id": 177702}, {"id": 177699}]
JOBS.clear()
JOBS[177702] = [job(478669, "manual", "b5e3ff5", started=None, finished=None)]
JOBS[177699] = [job(478665, "canceled", "48eb0fa", started=None, finished=None)]
st = D.deploy_state(gl)
assert st["attempt"] is None, f"시작 안 한 취소는 시도가 아니다: {st['attempt']}"
assert st["live"] is None and st["pending"] == 1, st
print("  🚀 시작 안 한 취소 → 시도 아님 ✅")

# 실제로 돌다가 취소된 것은 시도로 센다(뒤처리가 필요하다)
JOBS[177699] = [job(478665, "canceled", "48eb0fa")]      # started_at 있음
st = D.deploy_state(gl)
assert st["attempt"] and st["attempt"]["id"] == 478665, st
print("  🚀 돌다가 취소된 것은 시도로 셈 ✅")

# 성공 이력이 있으면 지금 서버는 그것이다(취소가 그것을 덮지 않는다)
JOBS[177699] = [job(478665, "canceled", "48eb0fa", started=None, finished=None)]
PIPES[:] = [{"id": 177702}, {"id": 177699}, {"id": 177600}]
JOBS[177600] = [job(478000, "success", "aaa1111")]
st = D.deploy_state(gl)
assert st["live"] and st["live"]["id"] == 478000, st
assert st["attempt"] is None or st["attempt"]["id"] == 478000, st
line = D.deploy_line(st)
assert "`aaa1111`" in line, line
print("  🚀 성공 이력 유지 ✅")

print("TEST282 OK")
