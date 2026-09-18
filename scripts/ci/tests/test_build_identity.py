"""빌드마다 신원을 박는가 — 이벤트의 `build` 가 빈칸이던 길 (S15P21B202-597).

실행: python scripts/ci/tests/test_build_identity.py
네트워크·Unity 없이 저장소의 `.gitlab-ci.yml` 과 소스를 읽는다.

**무엇이 있었나.** 이벤트 머리의 `build` 는 스탬프의 `버전=` 줄에서 왔다. 그 줄은
**서버 게이트**(`-404`)가 읽는 값이라 `vX.Y.Z` 여야 하고, 그래서 `build-devbuild` 는
`SPACE_RELEASE_VERSION` 을 **일부러 넘기지 않는다**. 그런데 QA 는 개발 빌드로 한다 —
결과적으로 쌓아 둔 이벤트 대부분이 `build: ""` 였다.

그래서 신원용 환경변수를 따로 둔다(`SPACE_BUILD_ID` → 스탬프의 `빌드=` 줄). 이 파일은
**그 둘이 다시 붙지 않는지**를 못박는다: 개발 빌드가 신원을 잃거나, 개발 빌드가 게이트용
버전을 얻거나(그러면 접속이 거절된다) 하는 두 방향 모두 여기서 깨진다.

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 `assert` 는 통째로
사라져 이 파일이 아무것도 안 하면서 성공으로 끝난다.
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))


def read(rel):
    with io.open(os.path.join(ROOT, *rel.split("/")), encoding="utf-8") as f:
        return f.read()


CI = read(".gitlab-ci.yml")
STAMP = read("METAGAME/Assets/_Project/Scripts/Editor/BuildStamp.cs")

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


def job(name):
    m = re.search(r"(?ms)^" + re.escape(name) + r":.*?(?=^[a-z][a-z0-9-]*:|^# ──|\Z)", CI)
    if not m:
        raise SystemExit(f"❌ 잡을 못 찾음: {name} — 이름이 바뀌었으면 이 검사를 따라 고쳐야 한다")
    return m.group(0)


def without_comments(text):
    """**주석을 떼고 본다** (`!505` AI 리뷰).

    두 방향으로 거짓말을 막는다. ① 주석이 실행 줄 흉내를 낸다 — 아래 순서 검사가
    `publish_devbuild.py version` 을 주석에서 찾아, 명령을 지워도 조용히 통과했다.
    ② 주석이 오탐을 낸다 — "릴리스 버전은 넘기지 않는다" 를 설명하려고
    `SPACE_RELEASE_VERSION` 을 적기만 해도 배선과 무관하게 빨개진다.

    실행 줄만 보는 정규식(`^\\s*-\\s`)으로는 ②만 풀린다 — `- |` 블록 **안**의 줄은
    `-` 로 시작하지 않아서, 거기 숨긴 `export` 를 놓친다. 그래서 줄 모양이 아니라
    주석 여부로 가른다.
    """
    return "\n".join(l for l in text.splitlines() if not l.lstrip().startswith("#"))


DEV = without_comments(job("build-devbuild"))
REL = without_comments(job("release-windows"))

print()
print("== 빌드 신원 ==")


# ── ① 개발·QA 빌드가 신원을 박는다 ──────────────────────────────────────
#
#     이것이 이 이슈가 고친 것이다. 값은 `publish_devbuild.py version` 이 이미 정해 둔
#     이름(`qa-498-…` · `dev-20260917-…`)이어야 한다 — 배치된 파일 이름과 이벤트의
#     이름이 같은 문자열이어야 "이 판은 그 빌드" 를 이을 수 있다.
check(re.search(r'export SPACE_BUILD_ID="\$\(cat release-version\.txt\)"', DEV) is not None,
      "build-devbuild 가 SPACE_BUILD_ID 를 넘기지 않는다 — 개발 빌드 이벤트의 build 가 다시 빈칸이 된다")

#     **순서가 있다.** 이름을 쓰는 `publish_devbuild.py version` 이 먼저 돌아야
#     `cat` 이 읽을 것이 있다. 뒤로 가면 빈 값이 조용히 박힌다(빈 값은 줄 자체가 없다).
#
#     `DEV` 는 주석을 뗀 것이다(`without_comments`) — 그러지 않으면 바로 위 주석의
#     같은 글자가 명령의 자리를 대신해 이 검사가 조용히 통과한다.
i_version = DEV.find("publish_devbuild.py version")
i_export = DEV.find("export SPACE_BUILD_ID")
i_unity = DEV.find("unity-editor")
check(-1 < i_version < i_export < i_unity,
      "SPACE_BUILD_ID 를 정하는 순서가 어긋났다 — 이름을 만들기 전이나 빌드 뒤에 넘긴다 "
      f"(version {i_version} · export {i_export} · 빌드 {i_unity})")


# ── ② 개발·QA 빌드는 게이트용 버전을 **받지 않는다** (-404) ─────────────
#
#     반대 방향의 회귀다. `qa-498-…` 을 `버전=` 으로 박으면 서버가 버전을 비교할 수
#     없어 접속이 거절된다. 그래서 두 변수는 뜻이 다르고 섞이면 안 된다.
check("SPACE_RELEASE_VERSION" not in DEV,
      "build-devbuild 가 릴리스 버전을 박는다 — 개발 빌드가 게이트에서 거절된다(-404)")


# ── ③ 릴리스는 두 줄이 같은 값을 가리킨다 ───────────────────────────────
#
#     릴리스 잡은 `SPACE_BUILD_ID` 를 따로 주지 않는다. 대신 스탬프가 릴리스 버전을
#     신원으로도 쓴다 — **같은 빌드를 두 이름으로 부르는 것**이 가장 헷갈리는 결과다.
#     그 폴백이 사라지면 릴리스 빌드의 이벤트가 이름을 잃으므로 여기서 잡는다.
check(re.search(r'export SPACE_RELEASE_VERSION="\$\(cat release-version\.txt\)"', REL) is not None,
      "release-windows 가 릴리스 버전을 넘기지 않는다 — 서버 게이트가 뜻을 잃는다(-404)")

ident = re.search(r"(?ms)public static string IdentityOf\([^)]*\)(.*?\n        \})", STAMP)
check(ident is not None, "BuildStamp.IdentityOf 가 없다 — 신원 판정을 검사할 수 없다")
if ident:
    # **서명은 빼고 본다.** 매개변수 이름에 `version` 이 들어 있어서, 서명까지 넣고
    # 찾으면 폴백을 지워도 초록이다 — 실제로 그렇게 새는 것을 확인했다.
    check("version" in ident.group(1),
          "신원 폴백이 릴리스 버전을 보지 않는다 — 릴리스 빌드의 이벤트가 이름을 잃는다")


# ── ④ 런타임이 그 줄을 읽는다 ───────────────────────────────────────────
#
#     스탬프에 쓰기만 하고 읽지 않으면 아무 일도 일어나지 않는다. 머리표는 양쪽이
#     **같은 상수**를 봐야 한다 — 예전에 파일 이름을 양쪽에 적어 두고 어긋난 적이 있다.
INFO = read("METAGAME/Assets/_Project/Scripts/Runtime/Core/BuildInfo.cs")
check('BuildIdPrefix = "빌드="' in INFO, "런타임에 빌드= 머리표가 없다")
check("BuildIdPrefix" in STAMP, "스탬프가 공용 머리표 상수를 쓰지 않는다")
check(re.search(r"line\.StartsWith\(BuildIdPrefix\)", INFO) is not None,
      "런타임이 빌드= 줄을 파싱하지 않는다 — 스탬프에만 적히고 아무도 읽지 않는다")


if fails:
    print()
    for f in fails:
        print("❌ " + f)
    raise SystemExit(1)

print("  개발 빌드가 신원을 박고, 게이트용 버전은 여전히 릴리스만 받는다 ✅")
print("  릴리스는 버전을 신원으로도 쓴다(두 줄이 어긋나지 않는다) ✅")
