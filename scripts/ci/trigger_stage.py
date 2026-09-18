"""MR 에서 만든 qa 빌드를 develop 파이프라인에 넘겨 배치시킨다 (S15P21B202-582).

실행:
    python scripts/ci/trigger_stage.py              # build-devbuild 의 마지막 줄
    python scripts/ci/trigger_stage.py --dry-run    # 보내지 않고 무엇을 보낼지만 찍는다

**왜 파이프라인을 한 번 더 만드나.** 배포 러너는 `ref_protected` 라 **MR ref 의 잡을
아예 집지 않는다**(`-405` 실측). 그래서 MR 파이프라인 안에서는 배치할 수가 없다.
전에는 사람이 `Run pipeline(develop)` 에서 `STAGE_VERSION` 을 주고 눌렀는데, develop 은
보호 브랜치라 **Maintainer 만** 할 수 있었다 — 버튼을 누르는 사람과 배치하는 사람이
달라서, `-460` 이 없애려던 "손으로 빌드해 나눠 주는 일" 이 "손으로 등록해 달라고
부탁하는 일" 로 모양만 바뀌어 남았다.

**보안 경계는 그대로다.** 만들어지는 파이프라인의 ref 는 `develop` 이므로 배치 잡은
**develop 의** `.gitlab-ci.yml` 과 스크립트로 돈다. MR 브랜치에서 넘어가는 것은
레지스트리의 zip 한 개와 아래 변수 세 개뿐이다 — 잡 정의도 스크립트도 넘어가지 않는다.

**새벽 빌드는 트리거하지 않는다.** 그쪽은 같은 파이프라인 안에 `publish-devbuild` 가
이미 있다. 여기서 또 부르면 **같은 빌드를 두 번 배치한다.**

**토큰 값은 찍지 않는다.** 있는지 없는지와 길이만 말한다.
"""
import argparse
import os
import re
import sys
from urllib.parse import urlencode

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.stdout.reconfigure(encoding="utf-8")

import ci_common as C  # noqa: E402

VERSION_FILE = "release-version.txt"

# **develop 으로만 보낸다.** 토큰이 만들 수 있는 ref 를 코드에서 한 곳으로 묶어 둔다 —
# 변수로 받으면 그 변수를 바꾼 브랜치가 다른 ref 에 파이프라인을 만들 수 있다.
STAGE_REF = "develop"

# qa 빌드만 이 길로 간다. dev 는 새벽 파이프라인이 자기 안에서 배치한다.
QA_RE = re.compile(r"^qa-\d+-[0-9a-f]{7,40}$")

TOKEN_ENV = "DEVBUILD_TRIGGER_TOKEN"


def read_version(path: str = VERSION_FILE) -> str:
    """`publish_devbuild.py version` 이 남긴 버전 이름. 없으면 빈 문자열."""
    try:
        with open(path, encoding="utf-8") as f:
            return f.read().strip()
    except OSError:
        return ""


def needs_stage(version: str) -> bool:
    """이 트리거에서 배치를 넘겨야 하는가.

    **`publish-devbuild` 의 rules 와 짝이다.** 그쪽이 새벽(schedule)은 같은 파이프라인
    안에서 직접 돌고, MR 것만 `trigger` 로 받는다. 두 곳이 어긋나면 한쪽은 두 번
    배치하거나 아무도 배치하지 않는다.
    """
    return C.env("CI_PIPELINE_SOURCE") == "merge_request_event" and bool(QA_RE.match(version))


def form(version: str) -> list[tuple[str, str]]:
    """트리거에 실을 것. **토큰은 여기 없다** — 부르는 쪽에서 따로 붙인다.

    브랜치는 `STAGE_BRANCH` 로 **명시해서** 넘긴다. 받는 쪽의 `CI_COMMIT_REF_NAME` 은
    `develop` 이라 그것을 쓰면 목록에 `develop @ …` 이 찍힌다 —
    **출처를 말한다는 것이 곧 틀린 것을 말하는 것**이 된다(`publish_builds.stage_branch`).
    """
    return [
        ("ref", STAGE_REF),
        ("variables[STAGE_VERSION]", version),
        ("variables[STAGE_BRANCH]", C.env("CI_MERGE_REQUEST_SOURCE_BRANCH_NAME")
                                    or C.env("CI_COMMIT_REF_NAME")),
        # 어느 MR 에서 왔는지. 배치 로그만 보고도 거슬러 갈 수 있어야 한다.
        ("variables[STAGE_MR]", C.env("CI_MERGE_REQUEST_IID")),
    ]


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="qa 빌드 배치를 develop 파이프라인에 넘긴다")
    ap.add_argument("--dry-run", action="store_true", help="보내지 않고 찍기만 한다")
    ap.add_argument("--version", default=None, help="버전 이름 (기본: release-version.txt)")
    a = ap.parse_args(argv)

    version = a.version if a.version is not None else read_version()
    print("=== 배치 넘기기 ===")
    print(f"버전: {version or '(없음)'} · 트리거: {C.env('CI_PIPELINE_SOURCE') or '(모름)'}")

    if not needs_stage(version):
        # **실패가 아니다.** 새벽 빌드는 여기로 오지 않는 것이 맞다.
        print("넘길 것이 없다 — 새벽 빌드는 같은 파이프라인이 직접 배치한다")
        return 0

    fields = form(version)
    print("보낼 것: " + " · ".join(f"{k.split('[')[-1].rstrip(']')}={v or '(빈값)'}"
                                  for k, v in fields))

    token = C.env(TOKEN_ENV)
    print(f"{TOKEN_ENV}: " + (f"있음 <{len(token)}자>" if token else "**없음**"))
    if not token:
        # **조용히 넘어가지 않는다.** 여기서 0 을 돌려주면 빌드는 초록인데 배치가 영영
        # 안 되고, 누른 사람은 런처를 열어 보고서야 안다. 이 잡은 MR 에서
        # `allow_failure` 라 릴리스처럼 무엇을 막지는 않고 ⚠ 로 남는다.
        print(f"❌ {TOKEN_ENV} 이 비어 있다 — 배치를 넘기지 못했다")
        print("   CI 변수에 있는지, 그리고 **Protected 가 꺼져 있는지** 보세요:")
        print("   MR 파이프라인은 보호 ref 가 아니라서 Protected 변수는 전달되지 않습니다")
        return 1

    url = f"{C.env('CI_API_V4_URL')}/projects/{C.env('CI_PROJECT_ID')}/trigger/pipeline"
    if a.dry_run:
        print(f"(dry-run) POST {url}")
        return 0

    body = urlencode(fields + [("token", token)])
    st, d = C.http("POST", url, {"Content-Type": "application/x-www-form-urlencoded"}, body)
    if st != 201:
        # 응답 본문은 우리가 만든 것이 아니다 — 씻어서 찍는다(S15P21B202-484).
        print(f"❌ 트리거 실패 {st} — {C.redact(str(d))[:300]}")
        return 1

    made = d if isinstance(d, dict) else {}
    print(f"✅ 배치 파이프라인 {made.get('id')} → {made.get('web_url')}")
    print("   이 파이프라인이 끝나면 개발 런처 목록에 뜬다 "
          "(기본값 `dev-latest.json` 은 바뀌지 않는다 — 목록에서 고른다)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
