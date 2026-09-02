"""배포 잡의 스풀 프로토콜 (S15P21B202-141).

실행: python scripts/ci/tests/test_deploy_backend.py
임시 디렉터리를 스풀로 쓰고, 호스트(deploy.sh)는 sleep 훅에서 흉내 낸다. 네트워크·도커 없이 돈다.

지키려는 것
  · jar 은 **원자적으로** 놓인다 — 반쯤 쓰인 jar 이 배포되면 안 된다(.part 가 남지 않아야 한다)
  · **이전 실행의 STATUS 를 내 결과로 착각하지 않는다** (RELEASE 를 쓰기 전에 지운다)
  · ok / rollback / fail / 시간 초과가 각각 다른 종료 코드·문구로 구분된다
"""
import json
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ["CI_PROJECT_ID"] = "1443653"
os.environ.pop("MM_WEBHOOK_URL", None)      # 테스트가 팀 채널로 나가지 않게
os.environ.pop("CI_JOB_NAME", None)         # 배포 잡이 아니므로 알림 경로가 열려선 안 된다

import ci_common
import deploy_backend as D


def no_http(method, url, headers=None, body=None, timeout=30):
    """이 테스트에서 네트워크가 열리면 그 자체가 결함이다.

    2026-09-01 사고 — 배포 결과 알림을 붙였더니 이 테스트의 4경우가 `ci-tests` 잡의 실제
    MM_WEBHOOK_URL 로 진짜 카드 4장을 팀 채널에 보냈다. 다시 새면 여기서 잡는다."""
    raise AssertionError(f"테스트가 밖으로 나갔다: {method} {url}")


ci_common.http = no_http

BIG = D.MIN_JAR_BYTES + 1024


def make_jar(path, size=BIG):
    with open(path, "wb") as f:
        f.write(b"\0" * size)
    return path


def host_writes(spool, result, detail="테스트", sha=None):
    """deploy.sh 가 STATUS 를 쓰는 것을 흉내 낸다."""
    def _hook(_seconds):
        rel = os.path.join(spool, "RELEASE")
        if not os.path.exists(rel):
            return
        req = json.loads(open(rel, encoding="utf-8").read())
        with open(os.path.join(spool, "STATUS"), "w", encoding="utf-8") as f:
            json.dump({"sha": sha or req["sha"], "result": result, "detail": detail}, f)
        os.remove(rel)          # 호스트는 다 끝나면 RELEASE 를 지운다
    return _hook


with tempfile.TemporaryDirectory() as tmp:
    spool = os.path.join(tmp, "spool"); os.makedirs(spool)
    jar = make_jar(os.path.join(tmp, "backend.jar"))

    # ── ① 원자적으로 놓이고 RELEASE 가 생긴다
    D.put(spool, jar, "abc123", "999")
    assert os.path.isfile(os.path.join(spool, "incoming.jar")), os.listdir(spool)
    assert not os.path.exists(os.path.join(spool, "incoming.jar.part")), ".part 가 남았다"
    assert os.path.getsize(os.path.join(spool, "incoming.jar")) == BIG
    rel = json.loads(open(os.path.join(spool, "RELEASE"), encoding="utf-8").read())
    assert rel == {"sha": "abc123", "pipeline": "999"}, rel
    print("  jar 원자적 배치 + RELEASE ✅")

    # ── ② 이전 실행의 STATUS 는 지워진다
    with open(os.path.join(spool, "STATUS"), "w", encoding="utf-8") as f:
        json.dump({"sha": "옛것", "result": "ok"}, f)
    D.put(spool, jar, "def456", "1000")
    assert not os.path.exists(os.path.join(spool, "STATUS")), "옛 STATUS 가 남았다"
    print("  옛 STATUS 제거 ✅")

    # ── ③ 남의 sha 가 찍혀 있으면 기다린다(내 것만 인정)
    with open(os.path.join(spool, "STATUS"), "w", encoding="utf-8") as f:
        json.dump({"sha": "남의것", "result": "ok"}, f)
    st = D.wait_status(spool, "def456", timeout=10, interval=5, sleep=lambda _s: None)
    assert st is None, st
    print("  남의 sha 는 무시하고 시간 초과 ✅")

    # ── ④ 성공
    os.remove(os.path.join(spool, "STATUS"))
    D.put(spool, jar, "sha-ok", "1")
    st = D.wait_status(spool, "sha-ok", timeout=30, interval=5, sleep=host_writes(spool, "ok", "배포 완료"))
    assert st and st["result"] == "ok", st
    print("  성공 판정 ✅")

    # ── ⑤ main(): 성공 → 0
    os.environ["DEPLOY_SPOOL"] = spool
    os.environ["CI_COMMIT_SHA"] = "sha-main-ok"
    os.environ["CI_PIPELINE_ID"] = "42"
    real_sleep = D.time.sleep
    D.time.sleep = host_writes(spool, "ok", "배포 완료")
    assert D.main([jar]) == 0
    print("  main() 성공 → 0 ✅")

    # ── ⑥ main(): 롤백 → 1 (서비스는 살아 있지만 이 커밋은 반영 안 됨)
    os.environ["CI_COMMIT_SHA"] = "sha-main-rb"
    D.time.sleep = host_writes(spool, "rollback", "새 jar 이 안 떠서 직전 jar 로 복귀")
    assert D.main([jar]) == 1
    print("  main() 롤백 → 1 ✅")

    # ── ⑦ main(): 실패 → 1
    os.environ["CI_COMMIT_SHA"] = "sha-main-fail"
    D.time.sleep = host_writes(spool, "fail", "롤백 후에도 준비 확인 실패")
    assert D.main([jar]) == 1
    print("  main() 실패 → 1 ✅")

    # ── ⑧ main(): 호스트가 응답 없음(시간 초과) → 1
    os.environ["CI_COMMIT_SHA"] = "sha-main-timeout"
    os.environ["DEPLOY_TIMEOUT"] = "10"
    D.TIMEOUT = 10
    D.time.sleep = lambda _s: None          # 호스트가 아무것도 안 쓴다
    assert D.main([jar]) == 1
    print("  main() 시간 초과 → 1 ✅")
    D.time.sleep = real_sleep

    # ── ⑨ 작은 jar 은 거부
    small = make_jar(os.path.join(tmp, "small.jar"), 1024)
    assert D.main([small]) == 1
    print("  비정상 크기 jar 거부 ✅")

    # ── ⑩ 스풀이 없으면 안내하고 실패
    os.environ["DEPLOY_SPOOL"] = os.path.join(tmp, "없는곳")
    assert D.main([jar]) == 1
    print("  스풀 없음 안내 ✅")

print("TEST141 OK")
