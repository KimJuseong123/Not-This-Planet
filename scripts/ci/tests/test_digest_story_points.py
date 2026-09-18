"""아침 점검 '스토리 포인트 없음' 항목 (S15P21B202-482).

실행: python scripts/ci/tests/test_digest_story_points.py
네트워크·토큰 없이 http() 를 가짜로 바꿔 `main()` 을 통째로 돌린다.

**왜 이 항목이 필요한가.** 포인트는 이슈를 만들 때 예측해 넣는 값인데 Jira 가 비어도
막지 않는다 — 생성 화면 스키마에서 `customfield_10031` 은 `required: false` 이고,
이 프로젝트의 필수 필드는 project·issuetype·summary·reporter 넷뿐이다. 그래서
2026-09-14 기준 활성 스프린트에 포인트가 있는 이슈와 없는 이슈가 양쪽 다 50건을 넘었다.

**여기서 조용히 틀릴 수 있는 것이 범위다.** 백로그까지 대상으로 잡으면 매일 수십 줄이
나오고, 그러면 사람이 카드 전체를 안 읽게 된다. 그래서 JQL 을 글자로 못 박는다.

**항목이 둘이다**(S15P21B202-568). 읽는 사람이 할 일이 다르다 — 하나는 지금 채우면 되는
것이고, 다른 하나는 **이미 끝났는데 안 채운 것**이라 스프린트 속도가 그만큼 틀려 있다.
끝난 것을 보는 항목이 없어서 `-544`·`-545` 가 포인트 없이 완료됐는데 **어느 아침에도
목록에 오른 적이 없었다**(2026-09-16 확인).
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

TITLE = "🔢 스토리 포인트 없음"


def issue(key: str, summary: str) -> dict:
    return {"key": key, "fields": {"summary": summary, "assignee": None, "status": {}}}


def run(hits: list[dict], fail: bool = False) -> tuple[str, list[str]]:
    """(MM 으로 나간 본문, 던져진 JQL 목록). `fail=True` 면 포인트 조회만 500 을 낸다."""
    jqls: list[str] = []
    sent: list[str] = []

    def http(method, url, headers=None, body=None, timeout=30):
        if url.endswith("/search/jql"):
            jql = (body or {}).get("jql", "")
            jqls.append(jql)
            if D.STORY_POINTS in jql:
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
    # 1) 포인트 없는 이슈가 있으면 표에 오른다
    text, jqls = run([issue("S15P21B202-480", "[Plan] 빌드 배포 v1.1.0")])
    assert TITLE in text, text[:400]
    assert "S15P21B202-480" in text and "빌드 배포 v1.1.0" in text, text[:400]
    print("  포인트 없는 이슈 → 표에 오름")

    # 2) **두 항목이 있고 범위가 갈려 있다.**
    sp = [q for q in jqls if D.STORY_POINTS in q]
    assert len(sp) == 2, f"포인트 질의가 2개여야 한다(지금 채운다 · 이미 완료됨): {sp}"

    todo = [q for q in sp if "statusCategory != Done" in q]
    done = [q for q in sp if "statusCategory = Done" in q]
    assert len(todo) == 1 and len(done) == 1, sp

    # ② 지금 채울 수 있는 것 — 백로그 전체를 보면 매일 수십 줄이 나와 카드가 안 읽힌다.
    for must in ("sprint in openSprints()",       # 백로그는 아직 매길 때가 아니다
                 'status = "In Progress"',        # 스프린트 밖에서 진행 중인 것도 본다
                 "issuetype != Epic",             # 에픽에는 포인트를 매기지 않는다
                 "cf[10031] is EMPTY"):
        assert must in todo[0], f"{must!r} 이 빠졌다: {todo[0]}"

    # ③ 이미 끝났는데 안 채운 것 — **이 항목은 완료를 봐야 뜻이 있다.**
    #    `statusCategory != Done` 이 붙어 있으면 늘 0건이라 있으나 마나다.
    assert "statusCategory != Done" not in done[0], \
        "완료를 보는 항목인데 미완료로 좁혀져 있다 — 늘 0건이 된다: " + done[0]
    for must in ("sprint in openSprints()",       # 지난 스프린트까지 소급하지 않는다
                 "issuetype != Epic",
                 "cf[10031] is EMPTY"):
        assert must in done[0], f"{must!r} 이 빠졌다: {done[0]}"
    print("  JQL 범위 — 지금 채울 것 · 이미 완료된 것, 둘 다 에픽 제외")

    # 3) 해당 이슈가 없으면 이 항목은 아예 안 나온다 ("없으면 보내지 않는다" 규칙)
    text, _ = run([])
    assert TITLE not in text, text[:400]
    print("  해당 없음 → 항목 없음")

    # 4) **이 조회가 죽어도 나머지 항목은 나간다.** 한 항목의 실패로 아침 점검이
    #    통째로 조용해지면, 다른 항목이 빠진 것을 아무도 모른다.
    text, _ = run([], fail=True)
    assert TITLE in text and "조회 실패" in text, text[:400]
    assert D.INFRA_MM and f"@{D.INFRA_MM}" in text, "조치할 사람이 안 붙었다"
    assert "다른 항목" in text, "다른 Jira 항목까지 같이 죽었다"
    print("  조회 실패 → 그 항목만 실패로 표시, 나머지는 그대로")

    print("TEST_DIGEST_STORY_POINTS OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
