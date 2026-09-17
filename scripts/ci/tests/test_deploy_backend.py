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

# ── 최신 아님 경고 (S15P21B202-333) ─────────────────────────────
# stale_verdict 는 순수 함수다 — 네 종류를 그대로 넣어 본다.
# 지키려는 것: **"확인 못 했다"(unknown) 를 "이상 없다" 로 접지 않는다.** 섞으면 조용한 미탐이 된다.

assert D.stale_verdict(0, []) == ("tip", "develop 의 끝을 배포한다")
print("  최신 → tip ✅")

kind, line = D.stale_verdict(3, ["backend/src/A.java", "backend/build.gradle"])
assert kind == "warn" and "3커밋 뒤" in line and "2개" in line, (kind, line)
print(f"  뒤처짐 + backend 변경 → warn ✅ ({line})")

kind, line = D.stale_verdict(3, [])
assert kind == "same", (kind, line)
print("  뒤처졌지만 backend 변경 0 → same(경고 없음) ✅")

# **!252 AI 리뷰가 잡은 미탐**: behind 는 읽었지만 변경 목록을 못 읽은 경우.
# 예전에는 판정이 함수와 호출부로 갈려 있어 이 경우가 "backend 변경이 없다" 로 단정됐다.
for args in [(None, None), (3, None), (None, [])]:
    kind, line = D.stale_verdict(*args)
    assert kind == "unknown", f"{args} 를 {kind} 로 단정했다: {line}"
assert "판단하지 않는다" in D.stale_verdict(3, None)[1]
print("  확인 불가(behind 만 읽힌 경우 포함) → unknown ✅")

# 토큰이 없으면 **네트워크를 열지 않고** 모른다고 답한다.
os.environ.pop("GITLAB_TOKEN", None)
assert D.staleness("deadbeef") == (None, None), "토큰 없이 판단했다"
print("  토큰 없음 → 네트워크 없이 (None, None) ✅")

# **경고가 배포를 죽여선 안 된다.** 이 파일의 ci_common.http 는 부르면 AssertionError 를 던진다
# (URLError 처럼 http() 가 잡지 않는 예외의 대역이다). 토큰이 있으면 조회를 시도하므로 그 경로를 탄다.
os.environ["GITLAB_TOKEN"] = "테스트용-가짜"
try:
    assert D.staleness("deadbeef") == (None, None), "예외를 삼키지 않았다"
finally:
    os.environ.pop("GITLAB_TOKEN", None)
print("  조회가 예외를 던져도 (None, None) — 배포를 막지 않는다 ✅")

# ── 카드에 실리는 종류 (S15P21B202-338) ─────────────────────────
# warn 만 싣던 동안에는 "최신 확인됨" 과 "확인 못 함" 이 카드에서 구분되지 않았다.
# 토큰이 만료되면 검사가 조용히 꺼진 것과 같아지는데, 그것이 -333 이 막으려던 미탐이다.
assert set(D.CARD_KINDS) == {"warn", "unknown"}, D.CARD_KINDS
assert D.CARD_ICON["warn"] != D.CARD_ICON["unknown"], "두 종류가 같은 아이콘이면 구분하려던 뜻이 사라진다"
print(f"  카드에 싣는 종류 = warn·unknown, 아이콘 구분 {D.CARD_ICON['warn']}/{D.CARD_ICON['unknown']} ✅")

# ── 서버만 고치는 길이 실제로 만들어진다 (S15P21B202-464) ────────────────
#
#     `master` 머지는 빌드 릴리스이지 서버 배포가 아니다. 그런데 `deploy-prod` 버튼은
#     **릴리스 파이프라인에만** 있어서, 서버 코드 한 줄을 고치려면 90분짜리 Unity
#     빌드를 다시 태우는 것 말고 길이 없었다.
_ci = open(".gitlab-ci.yml", encoding="utf-8").read()


def _rules(job: str) -> list[str]:
    """그 잡의 `rules:` 에서 **주석을 뺀** 줄만. 주석에 같은 말이 적혀 있다."""
    blk = _ci.split(chr(10) + job + ":", 1)[1]
    blk = blk.split(chr(10) + "  script:", 1)[0].split("  rules:", 1)[1]
    out = []
    for ln in blk.splitlines():
        s = ln.strip()
        if not s or s.startswith("#"):
            continue
        if not ln.startswith("    "):      # 다음 키(예: variables:)로 넘어갔다
            break
        out.append(s)
    return out


def _rule_blocks(job: str) -> list[list[str]]:
    """규칙을 **`- if:` 단위로 묶는다** (`!379` 리뷰).

    줄 단위로 보면 `all(("when: manual" in r) or ("if:" in r))` 같은 검사가 **`if` 줄
    하나만으로 늘 통과한다** — `when` 이 빠진 자동 실행 규칙을 못 잡는다. 이 검사가
    지키려던 것이 바로 그것이라 무의미한 검사였다(내가 쓴 것이고, 리뷰가 잡았다).
    """
    blocks: list[list[str]] = []
    for line in _rules(job):
        if line.startswith("- "):
            blocks.append([line])
        elif blocks:
            blocks[-1].append(line)
    return blocks


_dp = _rule_blocks("deploy-prod")
assert any(any("$DEPLOY_PROD" in l for l in b) for b in _dp), \
    "deploy-prod 에 서버 전용 경로가 없다 — 서버만 고치려면 90분 빌드를 다시 태워야 한다: " + str(_dp)

#     **규칙 하나하나가 수동이어야 한다.** prod 재시작은 진행 중인 세션을 끊는다 —
#     자동으로 도는 규칙이 하나라도 생기면 게임 중인 사람이 튕긴다.
for _b in _dp:
    assert any("when:" in l for l in _b), \
        "deploy-prod 에 when 이 없는 규칙이 있다 — 자동으로 돌면 게임 중인 사람이 튕긴다: " + str(_b)
    assert any(("when: manual" in l) or ("when: never" in l) for l in _b), \
        "deploy-prod 규칙이 수동도 never 도 아니다: " + str(_b)

#     **값까지 본다.** `$DEPLOY_PROD` 만 쓰면 있기만 하면 참이라 `DEPLOY_PROD=0` 에도 걸린다.
for _b in _dp:
    for _l in _b:
        if "$DEPLOY_PROD" in _l:
            assert '$DEPLOY_PROD == "1"' in _l, \
                "DEPLOY_PROD 를 존재성으로만 본다 — 끄려고 0 을 넣은 사람이 켜는 결과를 얻는다: " + _l

#     `needs: [build-backend]` 라 그 잡도 같은 조건에서 생겨야 한다. 아니면 GitLab 이
#     **파이프라인 생성 자체를 거절한다**(`-467` 이 release-prod 에서 겪은 자리).
_bb = _rules("build-backend")
assert any('$DEPLOY_PROD == "1"' in r for r in _bb), \
    "build-backend 가 서버 전용 파이프라인에서 안 생긴다 — deploy-prod 의 needs 가 없는 잡을 가리킨다"

#     그리고 그 파이프라인에 90분짜리 Unity 빌드가 딸려 오면 길을 낸 이유가 사라진다.
#     **막는 조건이 너무 넓으면 리허설이 조용히 사라진다.** 브랜치와 값까지 봐야 한다 —
#     리허설은 "릴리스 전에 안다" 가 목적이라 안 도는 것이 가장 나쁜 실패다.
#     never 규칙은 둘이다 — 되돌리기(`-467`, `$PROMOTE_VERSION`)와 서버 전용 배포
#     (`$DEPLOY_PROD`). **둘 다** 브랜치까지 좁혀져 있어야 한다.
_rw = [b for b in _rule_blocks("release-windows") if any("when: never" in l for l in b)]
assert len(_rw) >= 1, "release-windows 에 never 규칙이 없다"
for _b in _rw:
    _cond = _b[0]
    for _need in ('$CI_PIPELINE_SOURCE == "web"', '$CI_COMMIT_BRANCH == "master"'):
        assert _need in _cond, \
            "release-windows 를 막는 조건이 너무 넓다 — 다른 브랜치의 리허설까지 사라진다: " + _cond

assert any('$DEPLOY_PROD == "1"' in b[0] for b in _rw), \
    "서버 전용 파이프라인에서 release-windows 가 여전히 돈다 — 서버 한 줄 고치는 데 90분이 걸린다"
assert any("$PROMOTE_VERSION" in b[0] for b in _rw), \
    "되돌리기 파이프라인에서 release-windows 가 여전히 돈다 (-467)"
print("  서버만 고치는 길이 만들어지고, 90분 빌드를 안 끌고 온다 ✅")


# ── 비교 기준은 그 파이프라인의 브랜치다 (S15P21B202-464) ─────────────────
#
#     `develop` 을 못 박으면 `deploy-prod`(master) 가 릴리스 직후에도 "develop 보다
#     N커밋 뒤" 로 읽혀 **정상 배포에 매번 경고가 붙는다.** 늘 붙는 경고는 안 읽힌다.
_old = os.environ.get("CI_COMMIT_BRANCH")
try:
    os.environ["CI_COMMIT_BRANCH"] = "master"
    assert D.deploy_ref() == "master", D.deploy_ref()
    assert "master 의 끝" in D.stale_verdict(0, [], D.deploy_ref())[1]
    os.environ.pop("CI_COMMIT_BRANCH", None)
    os.environ.pop("CI_COMMIT_REF_NAME", None)
    assert D.deploy_ref() == "develop", D.deploy_ref()
finally:
    if _old is None:
        os.environ.pop("CI_COMMIT_BRANCH", None)
    else:
        os.environ["CI_COMMIT_BRANCH"] = _old
print("  최신 여부를 그 파이프라인의 브랜치와 비교한다 ✅")

print("TEST141 OK")
