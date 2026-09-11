#!/usr/bin/env python3
"""빌드된 jar 을 스풀에 놓고 호스트의 배포 결과를 기다린다 (S15P21B202-141).

CI 잡은 컨테이너 안에서 돌기 때문에 호스트의 `docker compose` 를 부를 수 없다. 도커 소켓을
잡에 노출하는 것은 백엔드·DB 가 같은 호스트에 있어 받아들일 수 없다(사실상 root). 그래서
**파일 하나로 신호를 준다** — 결정 근거는 docs/records/S15P21B202-139-cd-strategy.md.

이 스크립트가 하는 일
  1. jar 을 스풀에 원자적으로 놓는다(`.part` 로 쓰고 rename — 반쯤 쓰인 jar 이 배포되면 안 된다)
  2. 이전 실행의 STATUS 를 지운다(옛 결과를 내 결과로 착각하면 안 된다)
  3. RELEASE 표시 파일을 쓴다 → 호스트의 systemd path 유닛이 깨어나 deploy.sh 를 돌린다
  4. STATUS 에 **내 sha** 가 찍힐 때까지 기다렸다가 결과를 판정한다

로컬 실행: python scripts/ci/deploy_backend.py [jar경로]   (DEPLOY_SPOOL 로 스풀 지정)
"""
import glob
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from ci_common import GitLab, banner, env  # noqa: E402

DEFAULT_SPOOL = "/spool"
JAR_GLOB = "backend/build/libs/*.jar"
MIN_JAR_BYTES = 10_000_000        # 10MB 미만이면 정상 jar 이 아니다
TIMEOUT = int(env("DEPLOY_TIMEOUT", "420") or 420)
INTERVAL = 5
BACKEND_PREFIX = "backend/"       # 이 접두의 변경만 "백엔드가 달라졌다" 로 센다
DRIFT_SHOWN = 5                   # 경고에 파일 이름을 몇 개까지 늘어놓나
CARD_KINDS = ("warn", "unknown")  # 배포 카드에 싣는 종류 — 나머지는 정상이라 알릴 것이 없다
CARD_ICON = {"warn": "⚠", "unknown": "❔"}


def staleness(sha: str, ref: str = "develop") -> tuple[int | None, list[str] | None]:
    """이 배포가 `ref` 의 끝인가 (S15P21B202-333).

    돌려주는 것: `(ref 가 이 커밋보다 앞선 커밋 수, 그 사이 바뀐 backend/ 파일)`.
    **확인하지 못하면 `(None, None)`** — `0`·`[]` 과 섞지 않는다. `-309` 에서 얻은 교훈이다:
    "확인 못 했다" 를 "이상 없다" 로 접으면 조용한 미탐이 된다.

    왜 필요한가: `deploy-backend` 는 `needs: [build-backend]` 라 **자기 파이프라인의** 아티팩트를
    배포한다. 그래서 옛 파이프라인의 버튼을 누르면 그 시점의 jar 이 올라가는데, 옛 jar 도 정상
    jar 이라 헬스체크를 통과하고 `STATUS ok` 로 끝난다 — 뒤로 갔다는 신호가 어디에도 없었다.

    토큰이 없으면 **네트워크를 열지 않고** 바로 모른다고 답한다(오프라인 테스트가 이 경로를 탄다).
    """
    gl = GitLab()
    if not (gl.token and gl.project) or not sha or sha == "local":
        return None, None
    # **어떤 예외도 배포를 막지 않는다.** ci_common.http 는 4xx/5xx 는 튜플로 주지만
    # URLError(DNS·연결 거부·타임아웃)는 그대로 올린다. 이것은 배포를 돕는 경고일 뿐이므로,
    # 네트워크가 흔들렸다고 배포가 죽으면 고치려던 것보다 나쁜 것을 만든다.
    try:
        head = gl.branch_head(ref)
        if not head:
            return None, None
        if head == sha:
            return 0, []
        behind = gl.behind_count(sha, ref)
        paths = gl.compare_paths(sha, ref)
    except Exception as e:  # noqa: BLE001 — 경고가 배포 판정을 바꾸면 안 된다
        print(f"[warn] develop 비교 실패({type(e).__name__}: {e}) — 최신 여부를 판단하지 않는다")
        return None, None
    drift = None if paths is None else [x for x in paths if x.startswith(BACKEND_PREFIX)]
    return behind, drift


def stale_verdict(behind: int | None, drift: list[str] | None) -> tuple[str, str]:
    """`(종류, 사람이 읽을 한 줄)`. 순수 함수 — 판정이 한 곳에만 있게 한다.

    종류
      · `warn`    최신이 아니고 그 사이 `backend/` 가 바뀌었다 = 정말로 뒤로 간다
      · `same`    뒤처졌지만 `backend/` 변경이 0 = 배포되는 백엔드가 같다. 경고할 것이 없다
      · `tip`     `develop` 의 끝이다
      · `unknown` **확인하지 못했다**

    **`unknown` 을 나머지와 반드시 구분한다.** `behind` 를 읽었어도 변경 목록을 못 읽었으면
    "backend 변경이 없다" 고 말할 근거가 없다 — 처음 이 함수와 호출부에 분기가 갈려 있어
    `drift is None`(못 읽음)이 `same`(변경 없음)으로 떨어졌다(`!252` AI 리뷰가 잡았다).
    `-309` 의 교훈과 같은 자리다: "확인 못 했다" 를 "이상 없다" 로 접으면 조용한 미탐이 된다.
    """
    if behind is None or drift is None:
        return "unknown", ("develop 과 비교하지 못했다 — 최신 여부를 판단하지 않는다"
                           " (GITLAB_TOKEN 이 없거나 조회 실패)")
    if behind == 0:
        return "tip", "develop 의 끝을 배포한다"
    if not drift:
        return "same", (f"develop 보다 {behind}커밋 뒤처졌지만 그 사이 backend/ 변경이 없다"
                        " — 배포되는 백엔드는 같다")
    return "warn", (f"최신이 아닌 커밋을 배포한다 — develop 보다 {behind}커밋 뒤 · "
                    f"그 사이 backend/ 변경 {len(drift)}개")


NOTIFY_JOB = "deploy-backend"        # 이 이름의 잡에서만 알린다


def announce(result: str, detail: str = "") -> None:
    """배포 결과를 MM 에 알린다(S15P21B202-144). **최선 노력** — 알림이 실패해도 배포 결과를 덮지 않는다.

    같은 서버로 테스트하는 팀원은 재시작을 알 방법이 없다. 재시작은 진행 중인 WebSocket 세션을
    전부 끊으므로 성공도 알린다.

    **실제 배포 잡에서만 보낸다.** 2026-09-01 실측 사고 — `ci-tests` 잡에도 `MM_WEBHOOK_URL` 이
    들어 있어서, 이 함수를 붙인 순간 `test_deploy_backend.py` 의 4경우가 진짜 카드 4장을 팀
    채널로 보냈다. 사람이 손으로 돌리는 경우(로컬 확인)에도 팀에 "서버가 재시작됐다" 고 알리면
    거짓이다. 조건을 잡 이름으로 두면 두 경우가 함께 막힌다."""
    job = env("CI_JOB_NAME", "")
    if job != NOTIFY_JOB:
        print(f"[알림 생략] 배포 잡이 아니다(CI_JOB_NAME={job or '없음'}) — 보냈을 결과: {result} {detail}")
        return
    try:
        import mm_notify
        mm_notify.deploy(result, detail)
    except Exception as e:  # noqa: BLE001 — 알림 실패가 배포 판정을 바꾸면 안 된다
        print(f"[warn] 배포 알림 전송 실패({type(e).__name__}: {e}) — 배포 결과는 위 판정이 맞다")


def find_jar(args: list[str]) -> str | None:
    """인자로 받은 경로, 없으면 빌드 산출물에서 찾는다."""
    cands = [a for a in args if a.endswith(".jar")] or sorted(glob.glob(JAR_GLOB))
    for c in cands:
        if os.path.isfile(c) and os.path.getsize(c) >= MIN_JAR_BYTES:
            return c
    return cands[0] if cands else None


def put(spool: str, jar: str, sha: str, pipeline: str) -> None:
    """jar 을 원자적으로 놓고 RELEASE 를 쓴다. STATUS 는 먼저 지운다."""
    part = os.path.join(spool, "incoming.jar.part")
    with open(jar, "rb") as src, open(part, "wb") as dst:
        while chunk := src.read(1 << 20):
            dst.write(chunk)
    os.replace(part, os.path.join(spool, "incoming.jar"))

    status = os.path.join(spool, "STATUS")
    if os.path.exists(status):
        os.remove(status)                     # 옛 결과를 내 결과로 읽지 않도록

    body = json.dumps({"sha": sha, "pipeline": pipeline}, ensure_ascii=False)
    rel = os.path.join(spool, "RELEASE.part")
    with open(rel, "w", encoding="utf-8") as f:
        f.write(body)
    os.replace(rel, os.path.join(spool, "RELEASE"))


def read_status(spool: str) -> dict | None:
    p = os.path.join(spool, "STATUS")
    try:
        with open(p, encoding="utf-8") as f:
            return json.loads(f.read() or "{}")
    except (OSError, json.JSONDecodeError):
        return None


def wait_status(spool: str, sha: str, timeout: int = TIMEOUT, interval: int = INTERVAL,
                sleep=time.sleep) -> dict | None:
    """내 sha 의 STATUS 를 기다린다. 시간 초과면 None."""
    waited = 0
    while True:
        st = read_status(spool)
        if st and st.get("sha") == sha:
            return st
        if waited >= timeout:
            return None
        sleep(interval)
        waited += interval


def main(argv: list[str] | None = None) -> int:
    banner("deploy-backend")
    argv = argv if argv is not None else sys.argv[1:]
    spool = env("DEPLOY_SPOOL", DEFAULT_SPOOL)

    if not os.path.isdir(spool):
        print(f"스풀 디렉터리가 없다: {spool}")
        print("  러너 config.toml 의 volumes 에 호스트 스풀이 마운트돼 있어야 한다"
              " — scripts/deploy/host/install.sh 가 해 준다")
        return 1

    jar = find_jar(argv)
    if not jar:
        print(f"jar 을 찾지 못했다({JAR_GLOB}) — build-backend 잡의 아티팩트가 필요하다"); return 1
    size = os.path.getsize(jar)
    if size < MIN_JAR_BYTES:
        print(f"jar 크기가 비정상이다: {jar} {size}바이트"); return 1

    sha = env("CI_COMMIT_SHA", "local") or "local"
    pipeline = env("CI_PIPELINE_ID", "-") or "-"
    print(f"jar={jar} ({size / 1e6:.1f}MB) sha={sha[:8]} pipeline={pipeline}")

    # 최신 여부를 **배포 전에** 알린다 (S15P21B202-333). 막지는 않는다 — 옛 커밋 배포는
    # 롤백이라는 정당한 용도가 있다. 다만 모르고 뒤로 가는 일이 없게 한다.
    behind, drift = staleness(sha)
    kind, line = stale_verdict(behind, drift)
    # **카드에도 싣는 것은 warn 과 unknown 둘 다다** (S15P21B202-338). 처음에는 warn 만 실었는데,
    # 그러면 카드만 보는 사람이 "최신 확인됨" 과 "확인 못 함" 을 구분할 수 없다 — 토큰이 만료되면
    # 검사가 조용히 꺼진 것과 같아진다. `-333` 이 막으려던 미탐이 카드 경로에 남아 있었다(!252 AI 리뷰).
    # 아이콘으로 가른다: ⚠ 는 "정말 뒤로 간다", ❔ 는 "판단하지 못했다".
    note = f"{CARD_ICON[kind]} {line}" if kind in CARD_KINDS else ""
    if kind == "warn":
        print(f"⚠ {line}")
        for path in (drift or [])[:DRIFT_SHOWN]:
            print(f"     - {path}")
        if drift and len(drift) > DRIFT_SHOWN:
            print(f"     … 그 밖에 {len(drift) - DRIFT_SHOWN}개")
        print("   롤백이면 정상이다. 최신을 올리려면 develop 의 **가장 최신** 파이프라인에서 누른다")
    else:
        print(f"[알림] {line}" if kind == "unknown" else line)

    put(spool, jar, sha, pipeline)
    print(f"스풀에 올렸다 → 호스트가 배포한다. 최대 {TIMEOUT}초 기다린다")

    st = wait_status(spool, sha, sleep=time.sleep)
    if st is None:
        print(f"❌ {TIMEOUT}초 안에 결과가 오지 않았다 — 호스트 쪽을 봐야 한다:")
        print("   systemctl status metagame-deploy.path · tail ~/metagame/deploy/deploy.log")
        announce("timeout", f"{TIMEOUT}초 안에 호스트가 결과를 남기지 않았다")
        return 1

    result, detail = st.get("result"), st.get("detail", "")
    if result == "ok":
        print(f"✅ 배포 완료 — {detail}")
        # 경고는 카드에도 싣는다. 로그를 여는 사람보다 카드를 보는 사람이 많다.
        announce("ok", f"{detail} · {note}" if note else detail)
        return 0
    if result == "rollback":
        print(f"⚠ 롤백됨 — {detail}")
        print("   서비스는 직전 jar 로 돌고 있다. 이 커밋의 변경은 반영되지 않았다")
        announce("rollback", detail)
        return 1
    print(f"❌ 배포 실패 — result={result} detail={detail}")
    announce("fail", detail or f"result={result}")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
