"""아침 점검 '검사가 돌지 않은 MR' 오프라인 테스트 (S15P21B202-130).

실행: python scripts/ci/tests/test_digest_unchecked.py
네트워크·토큰 없이 http() 를 가짜로 바꿔 돌린다.
"""
import os
import sys
from datetime import datetime, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x", "MM_WEBHOOK_URL": "https://mm.example/h"})

import ci_common  # noqa: E402
import mm_digest as D  # noqa: E402

NOW = datetime(2026, 8, 28, 15, 0, tzinfo=timezone.utc)
ME = "rmawnsdud"


def mr(iid: int, title: str, author: str = ME) -> dict:
    return {"iid": iid, "title": title, "web_url": f"https://lab/mr/{iid}",
            "author": {"username": author}, "created_at": "2026-08-28T10:00:00.000Z"}


def pipe(status: str, created: str) -> dict:
    return {"id": 1, "status": status, "created_at": created}


def run(mrs: list[dict], pipes_by_iid: dict) -> list[str]:
    def http(method, url, headers=None, body=None, timeout=30):
        if url.endswith("state=opened&per_page=100"):
            return 200, mrs
        for iid, val in pipes_by_iid.items():
            if url.endswith(f"/merge_requests/{iid}/pipelines"):
                return (200, val) if val is not None else (500, {})   # None = 조회 실패
        return 200, {}

    ci_common.http = http
    D.http = http
    return D.unchecked_mrs(ci_common.GitLab(), NOW)


def main() -> int:
    # 1) 파이프라인이 하나도 없는 MR — master 기반 브랜치(2026-08-28 MR !74 실측 사례)
    rows = run([mr(74, "[Infra] docs: master 기반")], {74: []})
    assert len(rows) == 1 and "파이프라인 없음" in rows[0] and f"@{ME}" in rows[0], rows
    print("  파이프라인 없음 → 표에 오름")

    # 2) 오래 대기 — 러너가 꺼져 있다(F1 실측)
    rows = run([mr(70, "[Infra] docs: 대기")], {70: [pipe("pending", "2026-08-28T09:00:00.000Z")]})
    assert len(rows) == 1 and "6시간째 대기" in rows[0], rows
    print("  오래 대기 → 표에 오름(Infra 멘션 포함)")

    # 3) 방금 만들어진 대기는 정상 — 경보하지 않는다
    assert run([mr(71, "방금")], {71: [pipe("pending", "2026-08-28T14:30:00.000Z")]}) == []
    print("  30분 대기 → 조용")

    # 4) 검사가 돈 MR 은 이 항목이 아니다 — 실패는 🔴 카드가 따로 알린다
    assert run([mr(73, "정상")], {73: [pipe("failed", "2026-08-28T14:00:00.000Z")]}) == []
    print("  검사가 돈 MR(실패 포함) → 이 항목 아님")

    # 5) 조회 실패는 경보로 만들지 않는다 — 없는 문제를 매일 띄우는 쪽이 더 나쁘다
    assert run([mr(75, "조회 실패")], {75: None}) == []
    print("  파이프라인 조회 실패 → 경보 없음")

    # 6) 섞여 있어도 해당하는 것만
    rows = run([mr(74, "없음"), mr(73, "정상"), mr(70, "대기")],
               {74: [], 73: [pipe("success", "2026-08-28T14:00:00.000Z")],
                70: [pipe("created", "2026-08-28T09:00:00.000Z")]})
    assert len(rows) == 2, rows
    print("  섞인 경우 해당 건만 2개")

    # 7) 최신 파이프라인 판정이 순서에 의존하지 않는다 — 오래된 pending 이 뒤에 와도 최신(success)을 본다
    rows = run([mr(72, "재실행됨")],
               {72: [pipe("pending", "2026-08-28T09:00:00.000Z") | {"id": 1},
                     pipe("success", "2026-08-28T14:00:00.000Z") | {"id": 2}]})
    assert rows == [], rows
    print("  파이프라인 순서가 뒤섞여도 최신 기준")

    # 8) 임계값 환경변수가 이상해도 아침 점검 전체가 죽지 않는다
    assert D.pending_hours("세시간") == 2.0 and D.pending_hours("4") == 4.0 and D.pending_hours(None) == 2.0
    print("  DIGEST_PENDING_HOURS 가 숫자가 아니어도 기본값으로 계속")

    print("TEST_DIGEST_UNCHECKED OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
