"""설정·조회가 어긋나도 검사·알림이 죽지 않는지 (S15P21B202-134).

실행: python scripts/ci/tests/test_hardening.py
AI 리뷰가 지적한 "임포트 시점 사망 · 조용한 오탐" 항목들을 하나씩 확인한다.
"""
import json
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x", "CI_MERGE_REQUEST_IID": "1"})

import ci_common  # noqa: E402


def test_env_int():
    os.environ["X_TEST_INT"] = "세 개"
    assert ci_common.env_int("X_TEST_INT", 25) == 25          # 숫자가 아니면 기본값 — 잡을 죽이지 않는다
    os.environ["X_TEST_INT"] = "7"
    assert ci_common.env_int("X_TEST_INT", 25) == 7
    os.environ["X_TEST_INT"] = ""
    assert ci_common.env_int("X_TEST_INT", 25) == 25
    del os.environ["X_TEST_INT"]
    assert ci_common.env_int("X_TEST_INT", 25) == 25
    print("  env_int: 오설정·빈값·미설정 모두 기본값으로 계속")


def test_load_team_broken(monkey_path: str):
    """team.json 이 깨져 있어도 빈 팀으로 계속한다."""
    real = ci_common.os.path.dirname
    ci_common.os.path.dirname = lambda _p: monkey_path      # team.json 을 임시 폴더에서 찾게 만든다
    try:
        team = ci_common.load_team()
    finally:
        ci_common.os.path.dirname = real
    assert team == {"members": [], "roles": {}}, team
    print("  load_team: 파손된 team.json → 빈 팀으로 계속")


def test_role_member():
    team = {"members": [{"gitlab": "a", "mm": "a-mm"}], "roles": {"lead": "없는사람", "infra": "a"}}
    assert ci_common.role_member(team, "lead") is None       # 예전에는 StopIteration 으로 죽었다
    assert ci_common.role_member(team, "infra")["mm"] == "a-mm"
    assert ci_common.role_member({"members": [], "roles": {}}, "lead") is None
    print("  role_member: roles 와 members 가 어긋나도 예외 없음")


def test_mr_commits_paging():
    pages = {1: [{"id": i} for i in range(100)], 2: [{"id": 100 + i} for i in range(30)]}

    def http(method, url, headers=None, body=None, timeout=30):
        page = int(url.split("page=")[-1])
        return 200, pages.get(page, [])

    ci_common.http = http
    commits = ci_common.GitLab().mr_commits()
    assert len(commits) == 130, len(commits)                 # 예전에는 100개에서 끊겼다
    print("  mr_commits: 100개를 넘겨도 전부 수집(130건)")


def test_target_verdict():
    import mr_lint
    assert mr_lint.target_verdict("S15P21B202-1-x", "develop")[0] == "ok"
    assert mr_lint.target_verdict("develop", "master")[0] == "ok"
    assert mr_lint.target_verdict("S15P21B202-1-x", "master")[0] == "fail"
    assert mr_lint.target_verdict("S15P21B202-2-y", "S15P21B202-1-x")[0] == "warn"
    lvl, msg = mr_lint.target_verdict("S15P21B202-2-y", "hotfix/S15P21B202-1")
    assert lvl == "fail" and "hotfix" in msg, (lvl, msg)      # hotfix 위에 스택 MR 을 쌓지 않는다
    print("  target_verdict: hotfix 대상은 실패, 스택 MR 은 경고")


def test_stale_warn():
    import mr_lint
    os.environ["MR_STALE_WARN_COMMITS"] = "이십오"
    assert mr_lint.stale_warn() == 25                         # 예전에는 임포트 시점 ValueError 로 잡 전체가 죽었다
    os.environ["MR_STALE_WARN_COMMITS"] = "3"
    assert mr_lint.stale_warn() == 3
    del os.environ["MR_STALE_WARN_COMMITS"]
    print("  stale_warn: 오설정이어도 기본값 25 로 계속")


def test_not_done_issues_fallback():
    """키 조회가 거부되면 전량 조회로 폴백한다."""
    import mm_digest
    calls = []

    def search_all(jql, fields, page_size=100, max_pages=20):
        calls.append(jql)
        if "key in (" in jql:
            raise RuntimeError("Jira search → 400: issue key does not exist")
        return [{"key": "S15P21B202-1", "fields": {}}], True

    jira = type("J", (), {"search_all": staticmethod(search_all), "available": True})()
    issues, complete = mm_digest.not_done_issues(jira, ["S15P21B202-1", "S15P21B202-999"])
    assert complete and len(issues) == 1, (issues, complete)
    assert len(calls) == 2 and "key in (" in calls[0] and "key in (" not in calls[1], calls
    print("  not_done_issues: 키 조회 → 거부되면 전량 조회로 폴백")

    calls.clear()
    issues, _ = mm_digest.not_done_issues(
        type("J", (), {"search_all": staticmethod(lambda jql, f, **k: ([], True)), "available": True})(),
        [f"S15P21B202-{i}" for i in range(mm_digest.KEYS_PER_QUERY + 5)])
    print("  not_done_issues: 키가 많으면 곧바로 전량 조회")


def test_ai_review_no_judgement():
    """리뷰어 응답을 하나도 해석하지 못했으면 '발견 사항 없음' 이라고 하지 않는다."""
    import ai_review
    merged = {"verdict": "unknown", "checks": {}, "findings": [], "questions": []}
    text = ai_review.render("cli/test · 커밋 `abc1234`", ["code"], {"code": None}, {}, merged, None, 0, [])
    assert "발견 사항 없음" not in text, text
    assert "판단하지 못했습니다" in text, text
    print("  ai_review: 판단 불가를 '발견 사항 없음' 으로 적지 않는다")



def test_import_with_broken_team():
    """team.json 이 깨져 폴백된 상태에서도 각 스크립트가 임포트된다(MR !78 리뷰 지적).

    임포트 시점에 죽으면 폴백을 넣은 의미가 없다 — mr_lint 는 차단 잡이라 머지가 막히고,
    mm_notify 가 죽으면 실패 자체가 조용해진다."""
    import importlib
    real = ci_common.load_team
    ci_common.load_team = lambda: {"members": [{"gitlab": "a"}], "roles": {}}   # mm/jira/name 없음, roles 비었음
    try:
        import mm_notify
        import mr_lint
        importlib.reload(mm_notify)
        importlib.reload(mr_lint)
        assert mm_notify.LEAD == "" and mm_notify.INFRA == "", (mm_notify.LEAD, mm_notify.INFRA)
        assert mr_lint.JIRA_BY_GITLAB == {"a": ""} and mr_lint.NAME_BY_GITLAB == {"a": ""}
    finally:
        ci_common.load_team = real
        importlib.reload(mm_notify)
        importlib.reload(mr_lint)
    print("  깨진 team.json → mm_notify · mr_lint 모두 임포트됨(KeyError 없음)")


def test_normalize_team():
    t = ci_common.normalize_team({"members": [{"gitlab": "a"}, {"mm": "이름없음"}, "문자열"],
                                  "roles": "잘못된 형식"})
    assert t["roles"] == {} and len(t["members"]) == 1, t
    assert t["members"][0] == {"gitlab": "a", "jira": "", "mm": "", "name": "a"}, t["members"]
    print("  normalize_team: 빈 칸·잘못된 형식을 걸러 키를 보장")

def main() -> int:
    with tempfile.TemporaryDirectory() as d:
        with open(os.path.join(d, "team.json"), "w", encoding="utf-8") as f:
            f.write("{ 깨진 JSON")
        test_env_int()
        test_load_team_broken(d)
    test_role_member()
    test_mr_commits_paging()
    test_target_verdict()
    test_stale_warn()
    test_not_done_issues_fallback()
    test_ai_review_no_judgement()
    test_normalize_team()
    test_import_with_broken_team()
    print("TEST_HARDENING OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
