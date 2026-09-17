#!/usr/bin/env python3
"""릴리스를 사용자에게 내보낸다 — 백엔드 먼저, 그다음 클라이언트 (S15P21B202-592).

    python scripts/ci/release_prod.py

**왜 하나로 묶나.** 전에는 버튼이 둘이었다 — `deploy-prod`(서버에 jar 적용)와
`promote-builds`(`latest.json` 이동). 그 둘이 **서로를 몰랐다.** 순서도, 눌렀는지도.

그래서 실제로 어긋났다. v1.1.0 릴리스에서 `promote` 는 눌렸고 `deploy-prod` 는 아무도
안 눌러, 배포 백엔드가 **나흘째 `48c61a8b` 옛 jar** 이었다(`/spool/STATUS` 실측).
그동안 `-570`(다른 망을 같은 망으로 보던 것)이 prod 에 없었고, **파이프라인은 초록이라
아무도 몰랐다.**

**순서가 이 방향인 이유.** 반대(새 클라이언트가 옛 백엔드를 만나는 것)가 더 나쁘다.
백엔드는 대체로 옛 클라이언트와 호환되게 나가므로, 서버가 앞서는 창은 견딜 수 있다.
그 창을 **가장 짧게** 만드는 것이 한 버튼으로 묶는 또 하나의 이유다.

다만 새 백엔드가 `CLIENT_MIN_VERSION` 을 올리면 아직 클라이언트를 못 받은 사용자가
그 창에서 막힌다(`-404` 의 버전 게이트). 그때는 창이 짧은 것이 곧 피해가 작은 것이다.

**되돌리기는 백엔드를 건드리지 않는다.** `PROMOTE_VERSION` 이 있으면 `latest.json` 만
옮긴다 — 되돌리기는 급할 때 쓰는 길이고, 그때 서버까지 되돌리면 그 시간이 곧 장애
시간이다(`-467` 이 되돌리기에서 90분 빌드를 뺀 것과 같은 이유).

**백엔드가 안 바뀐 릴리스도 있다.** 그때는 `build-backend` 잡이 아예 생기지 않아
jar 이 없다 — 건너뛰고 그렇다고 말한다. 실패가 아니다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.stdout.reconfigure(encoding="utf-8")

import ci_common as C          # noqa: E402
import deploy_backend as D     # noqa: E402
import publish_builds as P     # noqa: E402
import publish_release as R    # noqa: E402

NL = chr(10)

# `build-backend` 가 도는 조건 (`.gitlab-ci.yml`). 이 릴리스가 여기를 건드렸으면
# **jar 이 있어야 한다** — 없으면 만료·유실이지 "안 바뀐 릴리스" 가 아니다.
BACKEND_PATHS = ("backend/", ".gitlab-ci.yml")


def rollback_version() -> str:
    """되돌리기로 준 버전. 없으면 빈 문자열(= 평범한 릴리스)."""
    return C.env("PROMOTE_VERSION", "").strip()


def backend_changed() -> bool | None:
    """이 릴리스가 백엔드를 건드렸나. 못 알아내면 None.

    **jar 이 없는 이유를 가른다** (`!499` AI 리뷰). 그냥 없으면 건너뛰게 두면,
    `build-backend` 아티팩트가 만료된(3일) **옛 파이프라인의 버튼**을 눌렀을 때
    백엔드를 조용히 건너뛰고 `latest.json` 만 옮긴다 — 이 스크립트가 막으려던
    상태가 그대로 재현된다.
    """
    version = P.read_line(P.VERSION_FILE) if os.path.exists(P.VERSION_FILE) else ""
    if not version:
        return None
    try:
        prev = R.previous_tag(version)
    except Exception:
        return None
    rng = f"{prev}..HEAD" if prev else "HEAD"
    try:
        out = R.git("diff", "--name-only", rng)
    except Exception:
        return None
    if out is None:
        return None
    return any(line.strip().startswith(BACKEND_PATHS)
               for line in out.split(chr(10)) if line.strip())


def main(argv: list[str] | None = None) -> int:
    argv = [] if argv is None else argv
    print("===== release-prod =====")

    staged = rollback_version()
    if staged:
        # **되돌리기.** 서버는 그대로 두고 사용자가 받는 것만 옛 버전으로 돌린다.
        print(f"되돌리기 ({staged}) — 백엔드는 건드리지 않는다")
        return P.promote()

    jar = D.find_jar(argv)
    if not jar:
        # **왜 없는지를 가른다.** 그냥 건너뛰면 아티팩트가 만료된(3일) 옛 파이프라인의
        # 버튼을 눌렀을 때 백엔드를 조용히 지나치고 `latest.json` 만 옮긴다.
        changed = backend_changed()
        if changed:
            print("❌ 백엔드 jar 이 없는데 이번 릴리스는 backend/ 를 바꿨다")
            print("   build-backend 아티팩트가 만료됐을 수 있다(3일). 파이프라인을 다시 돌리거나,")
            print("   서버만 고치는 길로 가세요: Run pipeline · ref master · DEPLOY_PROD=1")
            return 1
        if changed is None:
            # 못 알아내면 **멈춘다.** 모르는 채로 지나가는 것이 이 이슈가 막으려던 것이다.
            print("❌ 백엔드 jar 이 없고, 이번 릴리스가 backend/ 를 바꿨는지 알 수 없다")
            print("   (태그·git 이력을 못 읽었다 — 얕은 클론인지 확인할 것)")
            return 1
        print("백엔드 jar 이 없다 — 이번 릴리스는 backend/ 를 바꾸지 않았다. 배포를 건너뛴다")
    else:
        print(NL + "① 배포 백엔드를 먼저 바꾼다 (세션이 끊긴다)")
        rc = D.main(argv)
        if rc != 0:
            # **여기서 멈춘다.** `latest.json` 은 안 움직인다 — 남는 상태가
            # "서버는 옛것, 사용자도 옛것" 이라 어긋나지 않는다.
            print(f"❌ 백엔드 배포 실패({rc}) — latest.json 은 옮기지 않는다")
            return rc

    print(NL + "② latest.json 을 옮긴다 (사용자가 받기 시작한다)")
    return P.promote()


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
