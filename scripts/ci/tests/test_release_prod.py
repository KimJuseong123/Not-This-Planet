"""릴리스 버튼이 하나인가, 그리고 순서를 지키는가 (S15P21B202-592).

실행: python scripts/ci/tests/test_release_prod.py
네트워크·스풀 없이 돈다 — 백엔드 배포와 latest 이동을 대역으로 바꿔 **호출 순서**만 본다.

**무엇을 막는 검사인가.** 전에는 버튼이 둘이었고 서로를 몰랐다 — `deploy-prod`(서버에
jar 적용)와 `promote-builds`(`latest.json` 이동). v1.1.0 릴리스에서 **promote 만 눌려**
배포 백엔드가 나흘째 옛 jar 이었고(`-570` 이 prod 에 없었다), 한쪽만 눌러도 파이프라인이
초록이라 **아무도 몰랐다.**

그래서 세 가지를 못 박는다.

① 릴리스 경로에 **누를 버튼이 하나**다
② 그 하나가 **백엔드 → latest** 순으로 하고, 백엔드가 실패하면 latest 는 안 움직인다
③ **되돌리기는 백엔드를 건드리지 않는다** — 급할 때 쓰는 길이라 서버까지 되돌리면
   그 시간이 곧 장애 시간이다
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
os.chdir(ROOT)
sys.stdout.reconfigure(encoding="utf-8")

import release_prod as R  # noqa: E402

fails = []
passes = 0


def check(ok, msg):
    global passes
    print(("  ✅ " if ok else "  ❌ ") + msg)
    if ok:
        passes += 1
    else:
        fails.append(msg)


def run(promote_version=None, jar="backend/build/libs/app.jar",
        deploy_rc=0, promote_rc=0):
    """`main()` 을 대역으로 돌린다. 돌려주는 것: (종료코드, 부른 순서)."""
    calls = []
    real = (R.D.find_jar, R.D.main, R.P.promote)

    def fake_find(argv):
        calls.append("find_jar")
        return jar

    def fake_deploy(argv=None):
        calls.append("deploy_backend")
        return deploy_rc

    def fake_promote():
        calls.append("promote")
        return promote_rc

    R.D.find_jar, R.D.main, R.P.promote = fake_find, fake_deploy, fake_promote
    os.environ.pop("PROMOTE_VERSION", None)
    if promote_version:
        os.environ["PROMOTE_VERSION"] = promote_version
    try:
        rc = R.main([])
    finally:
        R.D.find_jar, R.D.main, R.P.promote = real
        os.environ.pop("PROMOTE_VERSION", None)
    return rc, calls


print()
print("== 평범한 릴리스: 백엔드 먼저, 그다음 latest ==")

rc, calls = run()
check(rc == 0, f"성공으로 끝난다 (종료 {rc})")
check(calls == ["find_jar", "deploy_backend", "promote"],
      f"백엔드 → latest 순이다 (실제 {calls})")

print()
print("== 백엔드가 실패하면 latest 는 안 움직인다 ==")

# 여기서 promote 가 불리면, 서버는 옛것인데 사용자만 새 클라이언트를 받는다.
rc, calls = run(deploy_rc=1)
check(rc == 1, f"실패를 그대로 돌려준다 (종료 {rc})")
check("promote" not in calls, f"latest 를 옮기지 않는다 (실제 {calls})")

print()
print("== 되돌리기는 백엔드를 건드리지 않는다 ==")

rc, calls = run(promote_version="v1.0.0")
check(rc == 0, f"성공으로 끝난다 (종료 {rc})")
check(calls == ["promote"], f"latest 만 옮긴다 (실제 {calls})")
check("deploy_backend" not in calls, "서버를 되돌리지 않는다 — 장애 시간을 늘린다")

print()
print("== 백엔드가 안 바뀐 릴리스 ==")

# `build-backend` 잡이 아예 없으면 jar 이 없다. 실패가 아니라 건너뛸 일이다.
rc, calls = run(jar=None)
check(rc == 0, f"성공으로 끝난다 (종료 {rc})")
check(calls == ["find_jar", "promote"], f"배포를 건너뛰고 latest 는 옮긴다 (실제 {calls})")

print()
print("== 릴리스 파이프라인에 버튼이 하나인가 ==")

with open(os.path.join(ROOT, ".gitlab-ci.yml"), encoding="utf-8") as f:
    CI = f.read().split("\n")

HEAD = re.compile(r"^([A-Za-z_][A-Za-z0-9_.-]*):")
_starts = [(i, HEAD.match(l).group(1)) for i, l in enumerate(CI) if HEAD.match(l)]
BLOCKS = {}
for (i, name), nxt in zip(_starts, [s[0] for s in _starts[1:]] + [len(CI)]):
    BLOCKS[name] = CI[i:nxt]

RELEASE_IF = re.compile(r'^\s*- if:.*CI_PIPELINE_SOURCE == "push".*CI_COMMIT_BRANCH == "master"')

buttons = []
for name, blk in BLOCKS.items():
    if name in ("stages", "default", "workflow", "variables"):
        continue
    for i, line in enumerate(blk):
        if RELEASE_IF.match(line) and "when: manual" in (blk[i + 1] if i + 1 < len(blk) else ""):
            buttons.append(name)

check(buttons == ["release-prod"],
      f"master 머지 파이프라인에서 누를 버튼은 release-prod 하나다 (찾은 것 {buttons})")

dp = BLOCKS.get("deploy-prod", [])
check(dp, "deploy-prod 잡은 남아 있다")
check(not any(RELEASE_IF.match(l) for l in dp),
      "deploy-prod 가 릴리스 파이프라인에는 없다 — 버튼이 나란히 둘이면 하나만 눌린다")
check(any('CI_PIPELINE_SOURCE == "web"' in l and "DEPLOY_PROD" in l for l in dp),
      "서버만 고치는 길(web + DEPLOY_PROD)은 살아 있다 — 설정 한 줄에 릴리스를 내지 않는다")

rp = BLOCKS.get("release-prod", [])
check(any("release_prod.py" in l and l.lstrip().startswith("-") for l in rp),
      "release-prod 가 묶는 스크립트를 부른다")
check(not any("publish_builds.py promote" in l for l in rp),
      "latest 만 옮기는 옛 호출이 남아 있지 않다 — 그러면 백엔드를 건너뛴다")
# jar 이 넘어와야 백엔드를 배포할 수 있다. 백엔드가 안 바뀐 릴리스도 있으므로 optional.
i = next((n for n, l in enumerate(rp) if "job: build-backend" in l), None)
check(i is not None, "release-prod 가 build-backend 를 needs 로 받는다")
if i is not None:
    tail = " ".join(rp[i:i + 3])
    check("optional: true" in tail, "그 needs 는 optional 이다 — 백엔드가 안 바뀐 릴리스도 있다")
    check("artifacts: true" in tail, "jar 아티팩트를 받는다")

print()
print(f"== 릴리스 버튼: 통과 {passes} · 실패 {len(fails)} ==")
if fails:
    print("   ❌ " + " / ".join(fails))
raise SystemExit(1 if fails else 0)
