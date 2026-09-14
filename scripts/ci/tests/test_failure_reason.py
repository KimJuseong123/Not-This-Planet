"""외부 호출이 4xx 로 실패했을 때 **이유가 남는지** (S15P21B202-484).

실행: python scripts/ci/tests/test_failure_reason.py
네트워크 없이 http() 를 가짜로 바꿔 돌린다.

같은 실패가 세 번 났는데 세 번 다 숫자만 남았다 — Jira 릴리스 웹훅 400 두 번,
MM 배포 카드 400 한 번. 그중 MM 400 때문에 🔴 배포 무응답 카드가 채널에 안 떨어져서
개발 배포가 멈춘 것을 1일 15시간 동안 아무도 몰랐다(`-483`).

**여기서 조용히 틀릴 수 있는 것은 방향이다.** 사유를 카드·코멘트에까지 실으면
잡 로그와 달리 아무도 가려 주지 않아 비밀이 채널로 나간다(`-335` 에서 정한 규칙).
그래서 "남는다" 와 "안 나간다" 를 **둘 다** 검사한다.
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

os.environ.update({"CI_PROJECT_ID": "1", "GITLAB_TOKEN": "x"})

import ci_common  # noqa: E402
from ci_common import why, redact  # noqa: E402


class Cap:
    """print 를 가로챈다 — 잡 로그로 나가는 것이 이 검사의 대상이다."""

    def __enter__(self):
        self.buf = io.StringIO()
        self._out = sys.stdout
        sys.stdout = self.buf
        return self

    def __exit__(self, *a):
        sys.stdout = self._out

    @property
    def text(self) -> str:
        return self.buf.getvalue()


def main() -> int:
    # ── ① 사유를 읽을 수 있는 꼴로 만든다 ────────────────────────────────
    assert why({"error": "rule not found"}) == '{"error": "rule not found"}', why({"error": "rule not found"})
    assert why("줄1\n줄2") == "줄1 줄2", why("줄1\n줄2")      # 로그 한 줄로 접는다
    assert why(None) == "(본문 없음)" and why("") == "(본문 없음)"
    long = why("가" * 500)
    assert len(long) == 301 and long.endswith("…"), len(long)   # 로그를 덮지 않게 자른다
    print("  본문 → 한 줄 · 없으면 '(본문 없음)' · 길면 자른다")

    # ── ② **비밀은 가린다** ──────────────────────────────────────────────
    os.environ["MM_WEBHOOK_URL"] = "https://mm.example/hooks/abcdef1234567890"
    got = redact("bad url: https://mm.example/hooks/abcdef1234567890 rejected")
    assert "abcdef1234567890" not in got, got
    assert "<MM_WEBHOOK_URL>" in got, got
    # 짧은 값은 건드리지 않는다 — 두세 글자를 지우면 멀쩡한 문장이 걸레가 된다
    os.environ["JIRA_USER"] = "abc"
    assert redact("abc 는 그대로") == "abc 는 그대로"
    # **URL 인코딩해 되비추는 서버가 있다.** 글자 그대로만 찾으면 그때 새어 나간다.
    from urllib.parse import quote
    enc = quote("https://mm.example/hooks/abcdef1234567890", safe="")
    assert enc != "https://mm.example/hooks/abcdef1234567890"       # 실제로 다른 꼴이다
    got = redact("rejected: " + enc)
    assert "abcdef1234567890" not in got, got
    assert "<MM_WEBHOOK_URL>" in got, got
    print("  비밀 값은 이름으로 · URL 인코딩 꼴도 · 짧은 값은 그대로 둔다")

    # ── ③ Jira 릴리스 웹훅 — 실패면 사유가 **잡 로그에** 남는다 ──────────
    import publish_release as P

    os.environ["JIRA_RELEASE_WEBHOOK"] = "https://ssafy.example/automation/hook/zzzzzzzzzzzz"

    def fake(status, payload):
        def http(method, url, headers=None, body=None, timeout=30):
            return status, payload
        ci_common.http = http
        P.http = http


    # **본문이 요청 주소를 되비춘다.** 실제 서버가 흔히 그렇게 한다 —
    # 이 줄이 없으면 가릴 것이 없어서 "안 샜다" 가 공짜로 참이 된다.
    fake(400, {"code": "NO_RULE", "message": "webhook rule is disabled",
               "url": "https://ssafy.example/automation/hook/zzzzzzzzzzzz"})
    with Cap() as c:
        line = P.jira_release_webhook("v1.1.0", ["S15P21B202-1"], "https://lab/r/v1.1.0")
    assert "사유" in c.text and "webhook rule is disabled" in c.text, c.text
    # **카드에는 안 실린다.** 돌려주는 문구가 MM 으로 나간다.
    assert line and "400" in line, line
    assert "webhook rule is disabled" not in line, line
    assert "zzzzzzzzzzzz" not in line and "zzzzzzzzzzzz" not in c.text, "웹훅 주소가 샜다"
    assert "<JIRA_RELEASE_WEBHOOK>" in c.text, "가려진 흔적이 없다: %s" % c.text
    print("  Jira 웹훅 실패 — 사유는 로그에, 카드 문구에는 숫자만")

    # 성공이면 아무것도 더 찍지 않는다 — 로그가 길어지면 아무도 안 읽는다
    fake(200, {"ok": True})
    with Cap() as c:
        line = P.jira_release_webhook("v1.1.0", ["S15P21B202-1"], "https://lab/r/v1.1.0")
    assert line is None and "사유" not in c.text, (line, c.text)
    print("  성공하면 사유를 찍지 않는다")

    # ── ④ MM 전송 — 실패면 사유가 남는다 ────────────────────────────────
    import mm_notify as M

    os.environ["MM_WEBHOOK_URL"] = "https://mm.example/hooks/abcdef1234567890"

    def fake_mm(status, payload):
        def http(method, url, headers=None, body=None, timeout=30):
            return status, payload
        ci_common.http = http
        M.http = http

    fake_mm(400, "Unable to parse incoming data for https://mm.example/hooks/abcdef1234567890")
    with Cap() as c:
        M.send("🔴 배포 무응답")
    assert "MM 전송 → 400" in c.text and "Unable to parse incoming data" in c.text, c.text
    assert "abcdef1234567890" not in c.text, "웹훅 주소가 샜다"
    assert "<MM_WEBHOOK_URL>" in c.text, "가려진 흔적이 없다: %s" % c.text
    print("  MM 실패 — 사유가 로그에 남는다")

    fake_mm(200, "ok")
    with Cap() as c:
        M.send("평범한 카드")
    assert "사유" not in c.text, c.text
    print("  MM 성공 — 사유를 찍지 않는다")

    print("TEST_FAILURE_REASON OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
