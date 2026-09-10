"""릴리스 브랜치 규칙 — release/<키>-vX.Y.Z → master (S15P21B202-325).

실행: python scripts/ci/tests/test_release_branch_rules.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다. compare 응답으로 '자기 커밋 수' 를 흉내 내고,
PUT 기록으로 squash 자동 해제를 본다.

규칙: 릴리스 브랜치는 develop 의 스냅샷이라 (1) squash 가 꺼져 있어야 하고 (2) 자기 커밋이 없어야 한다.
(3) 형식이 틀린 이름(버전 없음)은 릴리즈 MR 이 아니라 일반 검사로 가서 대상 규칙에 걸린다.
(4) 위생 검사는 릴리즈 MR 을 건너뛴다 (5) unity 기준선은 master 대상이면 develop 을 쓴다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "1"})
for k in ("JIRA_USER", "JIRA_API_TOKEN"):   # Jira 조회 없이 규칙만 본다
    os.environ.pop(k, None)

import ci_common

MR: dict = {}
COMPARE: dict = {"commits": []}
PUTS: list = []


def http(method, url, headers=None, body=None, timeout=30):
    if "/repository/compare" in url:
        return 200, COMPARE
    if method == "PUT":
        PUTS.append(dict(body or {}))
        return 200, MR
    return 200, MR


ci_common.http = http
import mr_lint, mr_title, repo_hygiene, unity_report   # noqa: E402
mr_lint.http = repo_hygiene.http = http


def set_mr(source, target, title, squash=False, draft=False, desc=""):
    MR.clear()
    MR.update({"title": title, "source_branch": source, "target_branch": target, "description": desc,
               "draft": draft, "squash": squash, "author": {"username": "suho6347"}})


REL = "release/S15P21B202-318-v0.1.0"

# ── 0) 도우미 ──
assert ci_common.key_from_branch(REL) == "S15P21B202-318"
assert ci_common.release_version(REL) == "v0.1.0"
assert ci_common.release_version("release/S15P21B202-318") is None
assert ci_common.is_release_mr(REL, "master") and ci_common.is_release_mr("develop", "master")
assert not ci_common.is_release_mr("release/S15P21B202-318", "master"), "버전 없는 이름은 릴리즈 MR 이 아니다"
assert not ci_common.is_release_mr(REL, "develop") and not ci_common.is_release_mr("hotfix/S15P21B202-40-x", "master")
print("  0) key_from_branch · release_version · is_release_mr ✅")

# ── 1) 정상 릴리스 MR: squash 꺼짐 · 자기 커밋 0 → 통과 (템플릿·키 없어도) ──
set_mr(REL, "master", "release v0.1.0")
COMPARE["commits"] = []
assert mr_lint.main() == 0
print("  1) release/…-v0.1.0 → master · squash 해제 · 자기 커밋 0 → 통과 ✅")

# ── 2) squash 켜짐 → 실패 ──
set_mr(REL, "master", "release v0.1.0", squash=True)
assert mr_lint.main() == 1
set_mr("develop", "master", "release", squash=True)
assert mr_lint.main() == 1, "develop → master 도 squash 규칙은 같다"
print("  2) squash 켜진 릴리즈 MR → 실패 ✅")

# ── 3) 자기 커밋이 있다 → 실패 ──
set_mr(REL, "master", "release v0.1.0")
COMPARE["commits"] = [{"id": "a"}, {"id": "b"}]
assert mr_lint.main() == 1
COMPARE["commits"] = []
print("  3) 릴리스 브랜치에 develop 밖 커밋 2개 → 실패 ✅")

# ── 3b) 조회 실패는 경고만 — 억울하게 막지 않는다 ──
COMPARE_BACKUP = dict(COMPARE)
def http_fail(method, url, headers=None, body=None, timeout=30):
    if "/repository/compare" in url:
        return 500, {}
    return http(method, url, headers, body, timeout)
ci_common.http = mr_lint.http = http_fail
set_mr(REL, "master", "release v0.1.0")
assert mr_lint.main() == 0
ci_common.http = mr_lint.http = http
print("  3b) 자기 커밋 조회 실패 → 경고만, 통과 ✅")

# ── 4) 버전 없는 release/ 이름 → 일반 검사로 가서 실패 ──
set_mr("release/S15P21B202-318", "master", "[Plan] scene: Build (S15P21B202-318)")
assert mr_lint.main() == 1
print("  4) release/<키>(버전 없음) → master : 실패 ✅")

# ── 5) repo-hygiene — 릴리스 브랜치 MR 이면 git 을 부르지 않고 건너뛴다 ──
os.environ["CI_MERGE_REQUEST_SOURCE_BRANCH_NAME"] = REL
os.environ["CI_MERGE_REQUEST_TARGET_BRANCH_NAME"] = "master"
repo_hygiene.git = lambda *a, **k: (_ for _ in ()).throw(AssertionError("git 을 부르면 안 됨"))
assert repo_hygiene.main() == 0
print("  5) repo-hygiene 릴리스 브랜치 MR: 건너뜀 ✅")

# ── 6) mr-title 이 **모든** MR 의 squash 를 API 로 끈다 (S15P21B202-417) ──
#
# 예전에는 master 대상만 껐다(-325) — 그때는 스택 MR 이 없었다. 지금은 스택이 일상이고,
# squash 는 **스택의 부모**에서 문제가 된다(부모의 커밋이 사라져 자식이 그것을 다시
# 짊어진다). 껴야 하는 쪽이 부모라서 "스택만 해제" 로는 못 막는다.
PUTS.clear()
set_mr("develop", "master", "S15p21b202 release", squash=True)
assert mr_title.main() == 0
assert any(p.get("squash") is False for p in PUTS), PUTS

PUTS.clear()
set_mr(REL, "master", "release v0.1.0", squash=False)
mr_title.main()
assert not any("squash" in p for p in PUTS), "이미 꺼져 있으면 건드리지 않는다"

PUTS.clear()
set_mr("S15P21B202-1-x", "develop", "[Infra] docs: x (S15P21B202-1)", squash=True)
mr_title.main()
assert any(p.get("squash") is False for p in PUTS),     "develop 대상도 꺼야 한다 — 이 MR 이 스택의 부모이면 자식이 부모의 변경을 다시 짊어진다"

# 작업 브랜치를 대상으로 하는 스택 MR 도 같다.
PUTS.clear()
set_mr("S15P21B202-2-y", "S15P21B202-1-x", "[Infra] docs: y (S15P21B202-2)", squash=True)
mr_title.main()
assert any(p.get("squash") is False for p in PUTS), "스택 MR 도 꺼야 한다"

PUTS.clear()
set_mr("S15P21B202-1-x", "develop", "[Infra] docs: x (S15P21B202-1)", squash=False)
mr_title.main()
assert not any("squash" in p for p in PUTS), "develop 대상도 이미 꺼져 있으면 건드리지 않는다"
print("  6) mr-title: 대상 무관 + squash 켜짐 → squash=false PUT · 꺼져 있으면 무동작 ✅")

# ── 7) unity 기준선: master 대상은 develop 을 본다 ──
assert unity_report.baseline_ref("master") == "develop"
assert unity_report.baseline_ref("") == "develop" and unity_report.baseline_ref(None) == "develop"
assert unity_report.baseline_ref("develop") == "develop"
assert unity_report.baseline_ref("S15P21B202-1-x") == "S15P21B202-1-x"
print("  7) unity 기준선: master → develop ✅")

# ── 8) hygiene 금지 목록에 빌드 폴더 ──
assert any(p.match("METAGAME/Release/v0000.20260907.1257/NotThisPlanet.exe") for p, _ in repo_hygiene.FORBIDDEN), \
    "METAGAME/Release/ 가 금지 경로여야 한다"
print("  8) repo-hygiene: METAGAME/Release/ 금지 ✅")

print("TEST325 OK")
