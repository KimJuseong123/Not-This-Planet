"""아침 점검 '진행 중인데 스프린트에 없음' 항목 (S15P21B202-528).

실행: python scripts/ci/tests/test_digest_no_sprint.py
네트워크·토큰 없이 http() 를 가짜로 바꿔 `main()` 을 통째로 돌린다.

**왜 이 항목이 필요한가.** 아침 점검의 다른 질의는 대부분 `sprint in openSprints()` 로
거른다 — 담당자 없음도, 포인트 없음도 그렇다. 기한 지남도 기한 자체가 스프린트
종료일에서 오므로 사실상 같다. 그래서 **진행 중인데 스프린트에 없는 이슈는 어느 줄에도
안 잡힌다.** 번다운에도 없다.

**어떻게 그런 이슈가 생기나** (2026-09-14 실측). 스프린트 자동 편입이 브랜치 생성이
아니라 `해야 할 일 → 진행 중` **전이**에 붙어 있다. 해야 할 일이던 이슈에 브랜치를
만들면 3초 안에 편입되지만, **이미 진행 중**인 이슈에 브랜치를 만들면 `has-branch` 만
붙고 스프린트는 빈 채로 남는다. 되돌림을 겪은 이슈가 그 상태가 되기 쉽다 — `완료로
끌기` 는 **진행 중으로** 되돌리므로 그다음 브랜치 생성에는 전이가 없다.

**여기서 조용히 틀릴 수 있는 것이 조건이다.** `sprint is EMPTY` 를 빼먹으면 진행 중인
이슈가 전부 올라와 매일 수십 줄이 되고, 그러면 카드를 아무도 안 읽는다. 그래서 JQL 을
글자로 못 박는다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"MM_WEBHOOK_URL": "https://mm.example/hook",
                   "JIRA_USER": "u", "JIRA_API_TOKEN": "t"})
for gone in ("GITLAB_TOKEN", "CI_PROJECT_ID"):   # GitLab 항목은 이 검사의 관심사가 아니다
    os.environ.pop(gone, None)

import ci_common  # noqa: E402
import mm_digest as D  # noqa: E402

TITLE = "🫥 진행 중인데 스프린트에 없음"
MARK = "sprint is EMPTY"          # 이 질의를 알아보는 표시


def issue(key: str, summary: str) -> dict:
    return {"key": key, "fields": {"summary": summary, "assignee": None, "status": {}}}


def run(hits: list[dict], fail: bool = False) -> tuple[str, list[str]]:
    """(MM 으로 나간 본문, 던져진 JQL 목록). `fail=True` 면 이 조회만 500 을 낸다."""
    jqls: list[str] = []
    sent: list[str] = []

    def http(method, url, headers=None, body=None, timeout=30):
        if url.endswith("/search/jql"):
            jql = (body or {}).get("jql", "")
            jqls.append(jql)
            if MARK in jql:
                return (500, {"message": "boom"}) if fail else (200, {"issues": hits})
            return 200, {"issues": [issue("S15P21B202-1", "다른 항목")]}
        if url.startswith("https://mm.example/"):
            sent.append((body or {}).get("text", ""))
        return 200, {}

    ci_common.http = http
    D.http = http
    assert D.main() == 0
    return (sent[0] if sent else ""), jqls


def main() -> int:
    # 1) 그런 이슈가 있으면 표에 오른다
    text, jqls = run([issue("S15P21B202-526", "[Infra] 되돌림을 겪은 이슈")])
    assert TITLE in text, text[:400]
    assert "S15P21B202-526" in text and "되돌림을 겪은 이슈" in text, text[:400]
    print("  진행 중·스프린트 없음 → 표에 오름")

    # 2) **조건이 좁혀져 있다.** 셋 중 하나라도 빠지면 매일 수십 줄이 되거나,
    #    엉뚱한 것이 올라온다.
    mine = [q for q in jqls if MARK in q]
    assert len(mine) == 1, mine
    q = mine[0]
    for must in ("statusCategory != Done",        # 끝난 일은 볼 것 없다
                 'status = "In Progress"',        # 해야 할 일은 스프린트가 비어도 정상이다
                 "sprint is EMPTY",               # 이 항목의 본질
                 "issuetype != Epic"):            # 에픽은 스프린트에 넣지 않는다
        assert must in q, f"{must!r} 이 빠졌다: {q}"
    print("  JQL 조건 — 진행 중 · 스프린트 없음 · 에픽 제외")

    # 3) **`sprint in openSprints()` 를 쓰면 안 된다.** 그것이 바로 이 이슈들을
    #    놓치던 조건이다 — 같은 필터를 쓰면 이 항목은 영원히 빈 채로 돈다.
    assert "openSprints" not in q, f"이 질의는 열린 스프린트로 거르면 안 된다: {q}"
    print("  openSprints 로 거르지 않는다 — 그것이 놓치던 원인이다")

    # 4) 해당 이슈가 없으면 이 항목은 아예 안 나온다 ("없으면 보내지 않는다" 규칙)
    text, _ = run([])
    assert TITLE not in text, text[:400]
    print("  해당 없음 → 항목 없음")

    # 5) **이 조회가 죽어도 나머지 항목은 나간다.** 한 항목의 실패로 아침 점검이
    #    통째로 조용해지면, 다른 항목이 빠진 것을 아무도 모른다.
    text, _ = run([], fail=True)
    assert TITLE in text and "조회 실패" in text, text[:400]
    assert D.INFRA_MM and f"@{D.INFRA_MM}" in text, "조치할 사람이 안 붙었다"
    assert "다른 항목" in text, "다른 Jira 항목까지 같이 죽었다"
    print("  조회 실패 → 그 항목만 실패로 표시, 나머지는 그대로")

    print("TEST_DIGEST_NO_SPRINT OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
