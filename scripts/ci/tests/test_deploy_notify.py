"""배포 결과 알림과 아침 점검의 배포 한 줄 (S15P21B202-144).

실행: python scripts/ci/tests/test_deploy_notify.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

왜 이 알림이 있는가 — 배포는 **사람이 눌러서** 도는 수동 잡이다(-139). 누른 사람은 잡 로그를
보고 있지만 같은 서버로 테스트하던 팀원은 재시작을 모른다. 재시작은 진행 중인 WebSocket 세션을
전부 끊으므로 **성공도** 알려야 한다.

핵심 구분: `live`(지금 서버에 올라간 것) 와 `attempt`(마지막 시도) 는 다르다. 마지막 시도가
실패했어도 서버는 그 전 성공분으로 돌고 있다. 섞으면 실패한 커밋이 배포된 것처럼 읽힌다.
"""
import contextlib
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"
os.environ["GITLAB_TOKEN"] = "x"
os.environ["MM_WEBHOOK_URL"] = "https://mm.example/hooks/test"
os.environ.pop("CI_MERGE_REQUEST_IID", None)

import ci_common  # noqa: E402

sent: list[str] = []          # MM 으로 보낸 본문
PIPELINES: list[dict] = []    # develop 파이프라인 (최신 우선)
JOBS: dict[int, list[dict]] = {}


def fake_http(method, url, headers=None, body=None, timeout=30):
    if url.startswith("https://mm.example/"):
        sent.append((body or {}).get("text", ""))
        return 200, {}
    if "/pipelines/" in url and url.endswith("/jobs?per_page=100"):
        pid = int(url.split("/pipelines/")[1].split("/")[0])
        return 200, JOBS.get(pid, [])
    if "/pipelines?ref=" in url:
        per = int(url.split("per_page=")[1].split("&")[0])
        return 200, PIPELINES[:per]
    raise AssertionError(f"예상 못한 호출: {method} {url}")


ci_common.http = fake_http

import mm_digest as D  # noqa: E402
import mm_notify as N  # noqa: E402

N.http = fake_http     # 모듈이 이름을 직접 import 했으므로 그쪽도 바꿔 준다
D.http = fake_http
INFRA_MM = N.BY_GITLAB[N.INFRA]["mm"]


def notify(result, detail="", **env):
    """카드를 한 번 보낸다. 반환 (종료코드, 보낸 본문).

    mm_notify.send 는 보낸 문구를 그대로 출력한다(CI 로그에서 확인하려고). 테스트에서는
    카드 여섯 장이 로그를 덮으므로 삼킨다 — 단정은 반환한 본문으로 한다."""
    sent.clear()
    keep = {}
    for k in ("CI_COMMIT_SHA", "CI_COMMIT_TITLE", "CI_PIPELINE_URL", "CI_JOB_URL", "GITLAB_USER_LOGIN"):
        keep[k] = os.environ.pop(k, None)
    os.environ.update({k: v for k, v in env.items() if v is not None})
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            rc = N.deploy(result, detail)
    finally:
        for k in env:
            os.environ.pop(k, None)
        os.environ.update({k: v for k, v in keep.items() if v is not None})
    return rc, sent[0] if sent else ""


# ── ① 성공 카드 — 🟢 · 멘션 없음 · 세션 끊김 경고
rc, t = notify("ok", "시작 확인까지 42초", CI_COMMIT_SHA="abc1234def567", CI_COMMIT_TITLE="[Backend] feat: x")
assert rc == 0, rc
assert t.startswith("🟢 **배포 완료**"), t
assert f"@{INFRA_MM}" not in t, "성공에 Infra 를 부르면 알림이 소음이 된다"
assert "`abc1234d`" in t and "[Backend] feat: x" in t, t
assert "다시 접속" in t, "재시작으로 접속이 끊긴다는 것을 알려야 한다"
assert "시작 확인까지 42초" in t
print("  성공 카드 ✅")

# ── ② 롤백 — 🔴 · Infra 멘션 · '지금 서버는 직전 jar'
rc, t = notify("rollback", "헬스체크 실패", CI_COMMIT_SHA="deadbeef1234")
assert rc == 0
assert t.startswith("🔴 **배포 롤백**") and f"@{INFRA_MM}" in t, t
assert "직전 jar" in t and "반영되지 않았" in t, t
assert "deploy.log" in t, "볼 곳이 있어야 한다"
print("  롤백 카드 ✅")

# ── ③ 무응답(타임아웃) — 서버 상태를 단정하지 않는다
rc, t = notify("timeout", "420초 안에 호스트가 결과를 남기지 않았다")
assert t.startswith("🔴 **배포 무응답**") and f"@{INFRA_MM}" in t, t
assert "확인해야" in t, "무응답이면 서버가 바뀌었는지도 알 수 없다 — 단정하면 안 된다"
print("  무응답 카드 ✅")

# ── ④ 모르는 결과값도 실패로 다룬다(조용히 넘기지 않는다)
rc, t = notify("무슨값", "")
assert "🔴" in t and f"@{INFRA_MM}" in t and "불명" in t, t
print("  결과값 불명 → 실패 취급 ✅")

# ── ⑤ 누른 사람 — 팀 명단에 있으면 넣고, 없으면 넣지 않는다
_, t = notify("ok", "", GITLAB_USER_LOGIN=N.INFRA)
assert f"@{INFRA_MM}" in t and "누른 사람" in t, t
_, t = notify("ok", "", GITLAB_USER_LOGIN="not-a-member")
assert "누른 사람" not in t, t
print("  누른 사람 표시 ✅")

# ── ⑥ 링크는 있는 것만 — CI 밖에서 부를 때 빈 링크가 남으면 안 된다
_, t = notify("ok", "")
assert "링크" not in t, t
_, t = notify("ok", "", CI_JOB_URL="https://lab/jobs/9")
assert "[배포 잡 로그](https://lab/jobs/9)" in t and "파이프라인]" not in t, t
print("  링크 선택적 ✅")


# ── 아침 점검의 배포 상태 ──────────────────────────────────
def job(jid, status, sha, finished="2026-09-01T12:10:33.000Z"):
    return {"id": jid, "name": "deploy-backend", "status": status,
            "finished_at": finished, "web_url": f"https://lab/jobs/{jid}",
            "commit": {"short_id": sha, "id": sha + "0" * 33}}


def setup(pipes):
    """pipes: [(pipeline_id, [잡...]), ...] 최신 우선."""
    PIPELINES.clear(); JOBS.clear()
    for pid, js in pipes:
        PIPELINES.append({"id": pid})
        JOBS[pid] = js


gl = ci_common.GitLab()

# ⑦ 배포 이력 없음 — 파이프라인은 있지만 배포 잡이 없다(backend 를 안 건드린 커밋들)
setup([(300, [{"id": 1, "name": "ci-tests", "status": "success"}]), (299, [])])
st = D.deploy_state(gl)
assert st == {"live": None, "attempt": None, "pending": 0, "complete": True}, st
assert "이력 없음" in D.deploy_line(st)
print("  배포 잡이 없는 이력 ✅")

# ⑧ 정상 — 마지막 성공이 곧 지금 서버
setup([(300, [job(11, "success", "abc1234")]), (299, [job(10, "success", "0000111")])])
st = D.deploy_state(gl)
assert st["live"]["id"] == 11 and st["attempt"]["id"] == 11 and st["pending"] == 0, st
line = D.deploy_line(st)
assert "`abc1234`" in line and "09/01 21:10" in line and "대기" not in line, line
print("  마지막 성공 = 지금 서버 ✅")

# ⑨ 눌리지 않은 배포가 위에 쌓였다 → 배포 대기 건수
setup([(302, [job(13, "manual", "cccdddd")]),
       (301, [job(12, "manual", "bbbcccc")]),
       (300, [job(11, "success", "abc1234")])])
st = D.deploy_state(gl)
assert st["pending"] == 2 and st["live"]["id"] == 11 and st["attempt"]["id"] == 11, st
assert "2건 배포 대기" in D.deploy_line(st)
print("  배포 대기 건수 ✅")

# ⑩ 마지막 시도가 실패 — 서버는 그 전 성공분으로 돈다(둘을 섞으면 안 된다)
setup([(301, [job(12, "failed", "bbbcccc")]), (300, [job(11, "success", "abc1234")])])
st = D.deploy_state(gl)
assert st["attempt"]["id"] == 12 and st["live"]["id"] == 11, st
assert "`abc1234`" in D.deploy_line(st), "실패한 커밋을 배포된 것처럼 적으면 안 된다"
print("  실패 시도 vs 실제 서버 분리 ✅")

# ⑪ 재시도가 있으면 그 파이프라인의 마지막 잡을 본다
setup([(300, [job(11, "failed", "abc1234"), job(14, "success", "abc1234")])])
st = D.deploy_state(gl)
assert st["attempt"]["id"] == 14 and st["live"]["id"] == 14, st
print("  재시도는 마지막 잡 ✅")

# ⑫ 상한까지 꽉 찼으면 '전량 확인' 이라고 하지 않는다
setup([(400 - i, []) for i in range(D.DEPLOY_SCAN_PIPELINES)])
st = D.deploy_state(gl)
assert st["complete"] is False, st
assert f"{D.DEPLOY_SCAN_PIPELINES}건 안에서" in D.deploy_line(st), D.deploy_line(st)
print("  탐색 상한 정직하게 표기 ✅")

# ⑬ 시각 변환 — 못 읽는 값은 물음표. GitLab 은 두 형태를 다 준다:
#    잡 API 는 `+09:00` 오프셋(2026-09-01 실측), 다른 곳은 `Z`. 둘 다 같은 KST 로 나와야 한다.
assert D.kst("2026-09-01T12:10:33.000Z") == "09/01 21:10"
assert D.kst("2026-09-01T21:10:33.381+09:00") == "09/01 21:10"
assert D.kst("깨진값") == "?" and D.kst(None) == "?" and D.kst("") == "?"
print("  시각 변환 ✅")

# ⑭ 알림은 **실제 배포 잡에서만** 나간다 (2026-09-01 사고의 회귀 검사)
#    ci-tests 잡에도 MM_WEBHOOK_URL 이 있어서, 이 조건이 없으면 테스트가 팀 채널로 진짜 카드를 보낸다.
import deploy_backend as B  # noqa: E402

for jobname, expect_send in (("", False), ("ci-tests", False), ("mm-digest", False), ("deploy-backend", True)):
    sent.clear()
    if jobname:                              # 위의 job() 헬퍼를 덮지 않게 이름을 달리 둔다
        os.environ["CI_JOB_NAME"] = jobname
    else:
        os.environ.pop("CI_JOB_NAME", None)
    with contextlib.redirect_stdout(io.StringIO()):
        B.announce("ok", "회귀 검사")
    assert bool(sent) is expect_send, f"CI_JOB_NAME={jobname!r} 에서 전송={bool(sent)} (기대 {expect_send})"
os.environ.pop("CI_JOB_NAME", None)
print("  배포 잡에서만 알림 ✅")

print("TEST144 OK")
