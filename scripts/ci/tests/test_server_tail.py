"""아침 점검 꼬리의 **서버 자원 한 줄** (S15P21B202-486).

실행: python scripts/ci/tests/test_server_tail.py
네트워크·마운트 없이 돈다 — 상태 dict 를 글자로 넣어 본다.

`section()` 은 문제가 있을 때만 말한다. 그래서 화면이 조용할 때 "서버가 멀쩡하다" 와
"재는 쪽이 죽었는데 그것도 못 알아챘다" 가 구별되지 않았다. 꼬리 한 줄이 그것을 가른다.

**여기서 조용히 틀릴 수 있는 것은 `0` 과 `없음` 의 구별이다.** `_num` 의 기본값이 0 이라
필드가 사라지면 "디스크 0%" 라는 **없는 사실**이 실린다 — `-467` 이 같은 자리에서
겪은 것이다. 그래서 둘을 따로 검사한다.
"""
import os
import sys
from datetime import datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x"})

import ci_common  # noqa: E402
import ec2_status as E  # noqa: E402

NOW = datetime(2026, 9, 14, 9, 33, tzinfo=E.KST)


def data(**over) -> dict:
    d = {
        "at": (NOW - timedelta(minutes=3)).isoformat(),
        "disk": {"used_pct": 20, "free_gb": 249},
        "mem": {"total_gb": 15, "available_gb": 12},
        "sizes": {"docker_gb": 36, "builds_mb": 501},
        "cert_days_left": 76,
    }
    d.update(over)
    return d


def main() -> int:
    # ── ① 정상일 때도 숫자가 나온다 ──────────────────────────────────────
    line = E.tail(data(), NOW)
    for must in ("🖥 서버:", "디스크 20%", "여유 249GB", "메모리 여유 12/15GB",
                 "도커 36GB", "인증서 76일", "3분 전"):
        assert must in line, f"{must!r} 이 빠졌다: {line}"
    assert E.section(data(), NOW) == "", "정상인데 블록이 붙었다"
    print("  정상 — 블록은 없고 꼬리에 자원이 나온다")
    print("   ", line)

    # ── ② 못 읽었으면 꼬리는 비운다 (블록이 대신 말한다) ────────────────
    assert E.tail(None, NOW) == "", E.tail(None, NOW)
    assert "읽지 못했다" in E.section(None, NOW), E.section(None, NOW)
    print("  못 읽음 — 꼬리는 비고 블록이 말한다(같은 말을 두 번 하지 않는다)")

    # ── ③ **0 과 없음을 구별한다** ──────────────────────────────────────
    #    없는 필드를 0 으로 읽으면 "디스크 0%" 라는 없는 사실이 실린다.
    d = data()
    del d["disk"]
    line = E.tail(d, NOW)
    assert "디스크" not in line, f"없는 디스크를 실었다: {line}"
    assert "메모리 여유 12/15GB" in line, line          # 나머지는 그대로
    print("  디스크 필드가 없으면 — 그 조각만 빠지고 0% 로 읽지 않는다")

    #    진짜 0 은 실어야 한다. 디스크가 0% 인 것과 못 잰 것은 다르다.
    line = E.tail(data(disk={"used_pct": 0, "free_gb": 0}), NOW)
    assert "디스크 0%" in line and "여유 0GB" in line, f"진짜 0 을 지웠다: {line}"
    print("  진짜 0 은 싣는다 — 0 과 없음은 다르다")

    # ── ④ 숫자가 하나도 없으면 꼬리를 만들지 않는다 ─────────────────────
    assert E.tail({"at": NOW.isoformat()}, NOW) == ""
    print("  숫자가 없으면 꼬리도 없다")

    # ── ⑤ 잰 시각이 이상해도 줄은 나온다 ────────────────────────────────
    line = E.tail(data(at="깨진값"), NOW)
    assert "잰 시각 불명" in line and "디스크 20%" in line, line
    print("  잰 시각이 깨져도 숫자는 싣는다")

    # ── ⑥ 카드에서 **블록이 아니라 꼬리**로 나간다 ──────────────────────
    #    블록으로 붙이면 "항목 없으면 보내지 않는다" 규칙이 깨져 매일 카드가 나간다.
    import mm_digest as D

    os.environ.update({"MM_WEBHOOK_URL": "https://mm.example/hook",
                       "JIRA_USER": "u", "JIRA_API_TOKEN": "t"})
    os.environ.pop("GITLAB_TOKEN", None)
    sent = []

    def http(method, url, headers=None, body=None, timeout=30):
        if url.endswith("/search/jql"):
            return 200, {"issues": [{"key": "S15P21B202-1",
                                     "fields": {"summary": "뭔가", "assignee": None, "status": {}}}]}
        if url.startswith("https://mm.example/"):
            sent.append((body or {}).get("text", ""))
        return 200, {}

    ci_common.http = http
    D.http = http
    # **여기서는 진짜 지금 시각을 쓴다.** main() 은 now 를 못 받아 실제 시각으로
    # 재므로, 고정 NOW 로 만든 픽스처는 20분 상한에 걸려 "낡음" 블록이 붙는다.
    fresh = data(at=(datetime.now(E.KST) - timedelta(minutes=3)).isoformat())
    D.ec2_status.load = lambda path=None: fresh
    assert D.main() == 0
    text = sent[0]
    assert "🖥 서버: 디스크 20%" in text, text[-400:]
    assert "**🖥 서버**" not in text, "꼬리가 아니라 블록으로 붙었다"
    # 꼬리는 각주 바로 위, 블록들 뒤에 온다
    assert text.index("🖥 서버:") > text.index("|---|"), "표보다 앞에 왔다"
    print("  카드에서 꼬리 자리에 붙는다 (블록 아님)")

    print("TEST_SERVER_TAIL OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
