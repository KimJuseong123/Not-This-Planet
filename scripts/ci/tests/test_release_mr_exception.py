"""릴리즈 MR(develop→master) 검사 예외 · 그 밖의 MR 은 그대로 (S15P21B202-122).

실행: python scripts/ci/tests/test_release_mr_exception.py
네트워크·토큰 없이 ci_common.http 를 가짜로 바꿔 돌린다.

릴리즈 MR 의 내용은 develop 으로 들어올 때 이미 MR 단위로 검사를 통과했다. 그래서 형식·템플릿
검사를 건너뛴다 — 다만 **예외를 좁게** 둔다: hotfix/*→master 는 develop 을 거치지 않은 새 코드라
전부 검사하고, 릴리즈 MR 이라도 Draft 는 실패다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "1"})
for k in ("JIRA_USER", "JIRA_API_TOKEN"):   # Jira 조회 없이 형식 검사만 본다
    os.environ.pop(k, None)

import ci_common

MR = {}


def http(method, url, headers=None, body=None, timeout=30):
    return 200, MR


ci_common.http = http
import mr_lint, repo_hygiene
mr_lint.http = repo_hygiene.http = http


def lint(source, target, title, desc="", draft=False):
    MR.clear()
    MR.update({"title": title, "source_branch": source, "target_branch": target,
               "description": desc, "draft": draft, "author": {"username": "rmawnsdud"}})
    return mr_lint.main()


# ① 릴리즈 MR — 템플릿도 이슈 키도 없지만 통과
assert lint("develop", "master", "[Infra] release: 2026-08-28 정렬") == 0
print("  릴리즈 MR develop→master: 통과 ✅")

# ② hotfix/* → master 는 예외가 아니다
assert lint("hotfix/login-crash", "master", "hotfix: 로그인 크래시") == 1
print("  hotfix/*→master: 검사 유지(예외 아님) ✅")

# ③ 릴리즈 MR 이라도 Draft 는 실패
assert lint("develop", "master", "Draft: [Infra] release", draft=True) == 1
print("  릴리즈 MR + Draft: 실패 유지 ✅")

# ④ 작업 브랜치 → master 는 실패(대상 규칙)
assert lint("S15P21B202-1-x", "master", "[Infra] docs: x (S15P21B202-1)") == 1
print("  작업 브랜치→master: 실패 유지 ✅")

# ⑤ 평범한 develop 대상 MR 은 종전대로 템플릿 검사까지 간다(템플릿이 없으니 실패)
assert lint("S15P21B202-1-x", "develop", "[Infra] docs: x (S15P21B202-1)") == 1
print("  일반 MR→develop: 기존 검사 유지 ✅")

# ⑥ repo-hygiene — 릴리즈 MR 이면 git 을 부르지 않고 건너뛴다
os.environ["CI_MERGE_REQUEST_SOURCE_BRANCH_NAME"] = "develop"
os.environ["CI_MERGE_REQUEST_TARGET_BRANCH_NAME"] = "master"
repo_hygiene.git = lambda *a, **k: (_ for _ in ()).throw(AssertionError("git 을 부르면 안 됨"))
assert repo_hygiene.main() == 0
print("  repo-hygiene 릴리즈 MR: 건너뜀 ✅")

# ⑦ Unity 가 임포트하지 않는 경로는 .meta 검사 대상이 아니다
u = repo_hygiene.unity_ignores
assert u("METAGAME/Assets/_Project/.gitkeep")
assert u("METAGAME/Assets/_Project/Docs~/note.txt")
assert not u("METAGAME/Assets/_Project/Art/Rock.fbx")
print("  .gitkeep · Docs~ 는 .meta 검사 제외 ✅")

print("TEST122 OK")
