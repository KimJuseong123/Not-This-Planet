"""MR 빌드가 개발 런처까지 가는가 (S15P21B202-582).

실행: python scripts/ci/tests/test_trigger_stage.py
네트워크·토큰 없이 돈다 — 트리거는 `--dry-run` 으로만 부른다.

**무엇을 막는 검사인가.** 이 길은 세 조각이 서로 짝이 맞아야 돈다.

① `trigger_stage.needs_stage()` 와 `publish-devbuild` 의 `rules` — 한쪽이 새벽까지
   넘기면 **같은 빌드를 두 번 배치**하고, 한쪽이 MR 을 빼면 **아무도 배치하지 않는다.**
② `workflow.rules` — 트리거를 허용하지 않으면 파이프라인이 아예 안 만들어진다.
   그런데 토큰은 MR 에서 읽혀야 해서 Protected 를 걸 수 없으므로, 그 토큰으로 할 수
   있는 일을 여기서 좁혀 둔 것도 같이 지킨다.
③ qa 가 `dev-latest.json` 을 가져가지 않는 것 — 가져가면 누가 MR 버튼을 누를 때마다
   **팀 전체 개발 런처의 기본값**이 그 미머지 브랜치가 된다.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
os.chdir(ROOT)
sys.stdout.reconfigure(encoding="utf-8")

os.environ.setdefault("CI_PROJECT_ID", "1")
os.environ.setdefault("CI_API_V4_URL", "https://lab.example/api/v4")

import publish_builds as P  # noqa: E402
import trigger_stage as T  # noqa: E402

sys.path.insert(0, os.path.join(ROOT, "scripts", "launcher"))
import make_manifest as M  # noqa: E402

fails = []
passes = 0

KEYS = ("CI_PIPELINE_SOURCE", "CI_MERGE_REQUEST_IID", "CI_COMMIT_REF_NAME",
        "CI_MERGE_REQUEST_SOURCE_BRANCH_NAME", "DEVBUILD_TRIGGER_TOKEN", "STAGE_BRANCH")


def check(ok, msg):
    global passes
    print(("  ✅ " if ok else "  ❌ ") + msg)
    if ok:
        passes += 1
    else:
        fails.append(msg)


def at(**kw):
    """트리거 환경을 통째로 갈아 끼운다 — 이전 경우가 새는 것을 막는다."""
    for k in KEYS:
        os.environ.pop(k, None)
    os.environ.update({k: v for k, v in kw.items() if v is not None})


MR = dict(CI_PIPELINE_SOURCE="merge_request_event", CI_MERGE_REQUEST_IID="490",
          CI_MERGE_REQUEST_SOURCE_BRANCH_NAME="S15P21B202-578-stage2-plan")
NIGHTLY = dict(CI_PIPELINE_SOURCE="schedule", CI_COMMIT_REF_NAME="develop")

QA_V, DEV_V = "qa-490-99c0fb28", "dev-20260917-9ae7220d"

print()
print("== 누가 배치를 넘기나 ==")

at(**MR)
check(T.needs_stage(QA_V) is True, "MR 의 qa 빌드는 넘긴다")

# ① 새벽은 **넘기면 안 된다** — 그 파이프라인에는 publish-devbuild 가 이미 있다.
at(**NIGHTLY)
check(T.needs_stage(DEV_V) is False, "새벽 dev 빌드는 안 넘긴다 — 두 번 배치된다")
check(T.needs_stage(QA_V) is False, "새벽에서 qa 이름이 와도 안 넘긴다")

at(**MR)
check(T.needs_stage(DEV_V) is False, "MR 에서 dev 이름이 와도 안 넘긴다")
for bad in ("", "v1.2.0", "qa-490", "qa--99c0fb28", "qa-490-zzz",
            "qa-490-99c0fb28/../x", " qa-490-99c0fb28"):
    check(T.needs_stage(bad) is False, f"이름이 어긋나면 안 넘긴다: {bad!r}")

print()
print("== 무엇을 넘기나 ==")

at(**MR)
f = dict(T.form(QA_V))
check(f["ref"] == "develop", "ref 는 develop 이다 — 배포 러너가 집는 곳")
check(f["variables[STAGE_VERSION]"] == QA_V, "버전 이름을 넘긴다")
# **브랜치를 넘기지 않으면 목록이 거짓말을 한다.** 받는 쪽의 CI_COMMIT_REF_NAME 은
# develop 이라 `develop @ …` 이 찍힌다.
check(f["variables[STAGE_BRANCH]"] == "S15P21B202-578-stage2-plan",
      "MR 의 소스 브랜치를 명시해서 넘긴다 (develop 이 아니다)")
check(f["variables[STAGE_MR]"] == "490", "어느 MR 에서 왔는지 넘긴다")
check("token" not in f, "토큰은 form() 에 없다 — 부르는 쪽에서 붙인다")
check(T.STAGE_REF == "develop" and "STAGE_REF" not in os.environ,
      "보낼 ref 는 코드에 박혀 있다 — 변수로 바꿀 수 없다")

print()
print("== 토큰이 없으면 조용히 넘어가지 않는다 ==")

at(**MR)
check(T.main(["--version", QA_V, "--dry-run"]) == 1,
      "토큰이 비어 있으면 실패로 끝낸다 (빌드는 초록인데 배치가 없는 상태를 막는다)")

at(**MR, DEVBUILD_TRIGGER_TOKEN="glptt-0000000000000000000000000000000000000000")
check(T.main(["--version", QA_V, "--dry-run"]) == 0, "토큰이 있으면 dry-run 이 통과한다")

# 새벽은 토큰이 없어도 **성공**이어야 한다 — 넘길 것이 없는 것은 실패가 아니다.
at(**NIGHTLY)
check(T.main(["--version", DEV_V, "--dry-run"]) == 0,
      "새벽 빌드는 토큰 없이도 0 으로 끝난다 — 넘길 것이 없는 것은 실패가 아니다")

check(T.TOKEN_ENV in __import__("ci_common").SECRET_ENVS,
      "트리거 토큰이 씻는 목록에 있다 — 응답에 되비쳐도 안 찍힌다")

print()
print("== 어느 채널이 개발 런처 기본값을 가져가나 ==")

# ③ 이것이 이 이슈에서 같이 정한 것이다.
rel = P.manifest_args("/g", "v1.2.0", M.RELEASE, "")
dev = P.manifest_args("/g", DEV_V, M.DEV, "")
qa = P.manifest_args("/g", QA_V, M.QA, QA_V)

check("--no-latest" in rel, "릴리스는 latest 를 안 가져간다 — promote-builds 가 옮긴다")
check("--no-latest" not in dev, "새벽 dev 는 latest 를 가져간다 — 기본값이 되는 것이 맞다")
check("--no-latest" in qa,
      "qa 는 latest 를 **안** 가져간다 — 미머지 브랜치가 팀 기본값이 되면 안 된다")
check("--branch" in qa and "--commit" in qa, "qa 도 브랜치·커밋은 싣는다 — 목록에서 고른다")
check(M.CHANNEL_FILES[M.QA] == M.CHANNEL_FILES[M.DEV],
      "dev·qa 가 같은 목록 파일을 쓴다 — 그래서 latest 를 나눠 두는 것이 뜻이 있다")

print()
print("== .gitlab-ci.yml 과 짝이 맞나 ==")

with open(os.path.join(ROOT, ".gitlab-ci.yml"), encoding="utf-8") as fh:
    CI = fh.read().split("\n")

HEAD = re.compile(r"^([A-Za-z_][A-Za-z0-9_.-]*):")
_starts = [(i, HEAD.match(l).group(1)) for i, l in enumerate(CI) if HEAD.match(l)]
BLOCKS = {}
for (i, name), nxt in zip(_starts, [s[0] for s in _starts[1:]] + [len(CI)]):
    BLOCKS[name] = CI[i:nxt]


def rule_lines(block):
    return [l for l in block if l.lstrip().startswith("- if:")]


wf = rule_lines(BLOCKS.get("workflow", []))
trig = [l for l in wf if 'CI_PIPELINE_SOURCE == "trigger"' in l]
check(len(trig) == 1, f"workflow 가 트리거를 한 줄로 허용한다 (찾은 것 {len(trig)})")
if trig:
    # 토큰에 Protected 를 걸 수 없으므로 **여기서 좁힌다.** 셋 중 하나라도 빠지면
    # 그 토큰으로 다른 ref·다른 잡을 돌릴 수 있다.
    for need in ('$CI_COMMIT_BRANCH == "develop"', "$STAGE_VERSION"):
        check(need in trig[0], f"트리거 허용 조건에 {need} 이 있다")

pub = rule_lines(BLOCKS.get("publish-devbuild", []))
auto = [l for l in pub if 'CI_PIPELINE_SOURCE == "trigger"' in l]
check(len(auto) == 1, "publish-devbuild 가 트리거를 받는다")
if auto:
    i = BLOCKS["publish-devbuild"].index(auto[0])
    nxt = BLOCKS["publish-devbuild"][i + 1] if i + 1 < len(BLOCKS["publish-devbuild"]) else ""
    # **자동이어야 한다.** 버튼으로 두면 그 버튼도 Maintainer 만 누를 수 있어
    # (develop 은 보호 브랜치라) 고치려던 것이 그대로 남는다.
    check("when: manual" not in nxt, "트리거로 온 배치는 자동이다 — 또 누르게 하지 않는다")
check(any('CI_PIPELINE_SOURCE == "web"' in l for l in pub),
      "손으로 배치하는 길은 남아 있다 — 트리거가 안 될 때 돌아갈 자리")

# **다른 잡이 딸려 오면 안 된다.** 트리거 파이프라인은 배치 하나만을 위한 것이다.
others = [n for n, b in BLOCKS.items()
          if n not in ("workflow", "publish-devbuild")
          and any('CI_PIPELINE_SOURCE == "trigger"' in l for l in rule_lines(b))]
check(not others, "트리거에 걸리는 잡은 publish-devbuild 뿐이다" + (f" — {others}" if others else ""))

dev_blk = BLOCKS.get("build-devbuild", [])
check(any("trigger_stage.py" in l and l.lstrip().startswith("-") for l in dev_blk),
      "build-devbuild 가 마지막에 배치를 넘긴다")
# 여기서 `if` 로 가르면 needs_stage() 와 두 곳이 되어 어긋난다.
check(not any("trigger_stage.py" in l and ("if " in l or "||" in l) for l in dev_blk),
      "넘길지 말지는 스크립트가 정한다 — yml 에서 가르지 않는다")

print()
print(f"== 배치 넘기기: 통과 {passes} · 실패 {len(fails)} ==")
if fails:
    print("   ❌ " + " / ".join(fails))
raise SystemExit(1 if fails else 0)
