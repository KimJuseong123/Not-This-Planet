"""claude_auth_check 의 알림 정책과 분류 (S15P21B202-310).

  1. 실패 분류가 `ai_review.classify_claude_failure` 와 **어긋나지 않는다**
     — 호스트 스크립트는 /opt 에서 도는 독립 파일이라 scripts/ci 를 임포트할 수 없어
       규칙이 두 벌 있다. 한쪽만 고치면 여기서 잡힌다.
  2. 알림 정책 — 문턱·재알림·복구·원인 변경
  3. 리프레시 토큰 만료 경고
  4. 자격 값이 로그·알림에 새지 않는다

네트워크·`claude` 실행 없이 돈다.
"""
import json
import os
import sys
import tempfile
from pathlib import Path

HERE = os.path.dirname(os.path.abspath(__file__))
CI = os.path.dirname(HERE)                      # .../scripts/ci
SCRIPTS = os.path.dirname(CI)                   # .../scripts
sys.path.insert(0, CI)
sys.path.insert(0, os.path.join(SCRIPTS, "runner"))

import ai_review                      # noqa: E402
import claude_auth_check as A         # noqa: E402


# ── ① 두 분류기가 같은 답을 낸다 ─────────────────────────────────────────────
SAMPLES = [
    ("", "API Error: 401 authentication_failed", "authentication"),
    ("", "Invalid authentication credentials", "authentication"),
    ("", "Not logged in", "authentication"),
    ("", "OAuth token has expired", "authentication"),
    ("", "http status 403 forbidden", "authentication"),
    ("", "API Error: 429 rate_limit_error", "rate_limit"),
    ("Claude usage limit reached", "", "rate_limit"),
    ("", "weekly limit exceeded", "rate_limit"),
    ("", "API Error: 500 internal server error", "server_error"),
    ("", "status 503 overloaded_error", "server_error"),
    ("", "Connection error: ECONNRESET", "network"),
    ("", "request timed out", "network"),
    ("", "unable to connect to api.anthropic.com", "network"),
    ("", "error: unknown option '--nope'", "invalid_request"),
    ("", "model_not_found", "invalid_request"),
    ("hello", "", "unknown"),
    ("", "", "unknown"),
]

for out, err, expected in SAMPLES:
    mine = A.classify(out, err)
    theirs = ai_review.classify_claude_failure(out, err)
    assert mine == theirs, f"두 분류기가 갈렸다 {out!r}/{err!r}: 호스트={mine} CI={theirs}"
    assert mine[0] == expected, f"{out!r}/{err!r} → {mine[0]}, 기대 {expected}"
print(f"  분류 {len(SAMPLES)}가지 · CI 판정과 일치 ✅")

# 문턱 표의 모든 원인이 실제로 나올 수 있는 값이어야 한다(오타 방지)
known = {c for c, _, _ in [("authentication", 0, 0), ("rate_limit", 0, 0), ("server_error", 0, 0),
                           ("network", 0, 0), ("invalid_request", 0, 0), ("unknown", 0, 0)]}
assert set(A.THRESHOLD) <= known, f"문턱 표에 없는 원인: {set(A.THRESHOLD) - known}"
print("  문턱 표의 원인 이름이 전부 실재 ✅")


# ── ② 알림 정책 ──────────────────────────────────────────────────────────────
T0 = 1_788_000_000.0        # 고정 시각 — 테스트가 오늘 날짜에 의존하지 않게 한다(-281)


def run(state, ok, cat, at):
    return A.decide(state, ok, cat, at)


# 인증 실패: 1회는 조용, 2회째에 알린다
st, msg = run({}, False, "authentication", T0)
assert st["fails"] == 1 and msg is None, (st, msg)
st, msg = run(st, False, "authentication", T0 + 1200)
assert st["fails"] == 2 and msg, "연속 2회면 알려야 한다"
assert "authentication" in msg and "@" in msg, msg
assert "재로그인" in msg, "무엇을 하라는지 적어야 한다"
print("  인증 실패 2회째 알림 ✅")

# 계속 실패해도 6시간 안에는 다시 알리지 않는다
st2, msg2 = run(st, False, "authentication", T0 + 2400)
assert msg2 is None and st2["fails"] == 3, (st2, msg2)
st3, msg3 = run(st2, False, "authentication", T0 + 6 * 3600 + 1200)
assert msg3 and "계속 실패 중" in msg3, msg3
print("  6시간마다 한 번만 재알림 ✅")

# 복구되면 즉시 한 번
st4, msg4 = run(st3, True, None, T0 + 7 * 3600)
assert msg4 and "복구" in msg4, msg4
assert st4["fails"] == 0 and "alerted_at" not in st4, st4
# 두 번 연속 성공이면 아무 말도 하지 않는다
st5, msg5 = run(st4, True, None, T0 + 8 * 3600)
assert msg5 is None, msg5
print("  복구 알림은 한 번만 ✅")

# 알린 적 없이 회복하면 조용하다 — 1회 실패는 애초에 알리지 않았으므로
st6, _ = run({}, False, "network", T0)
st7, msg7 = run(st6, True, None, T0 + 1200)
assert msg7 is None, "알린 적 없는 실패의 회복은 알리지 않는다"
print("  알린 적 없는 실패의 회복은 조용 ✅")

# 원인이 바뀌면 연속 횟수를 다시 센다
st8, _ = run({}, False, "network", T0)
st9, msg9 = run(st8, False, "authentication", T0 + 1200)
assert st9["fails"] == 1 and msg9 is None, f"다른 장애를 이어 세면 안 된다: {st9}"
print("  원인이 바뀌면 다시 셈 ✅")

# 한도는 문턱이 높다 — 2회로는 알리지 않는다
s = {}
for i in range(5):
    s, m = run(s, False, "rate_limit", T0 + 1200 * i)
    assert m is None, f"{i+1}회째에 알리면 안 된다"
s, m = run(s, False, "rate_limit", T0 + 1200 * 5)
assert m and "rate_limit" in m, m
print("  사용량 한도는 6회째에 알림 ✅")

# 네트워크·서버 오류는 3회
s = {}
for i in range(2):
    s, m = run(s, False, "server_error", T0 + 1200 * i)
    assert m is None
s, m = run(s, False, "server_error", T0 + 2400)
assert m, "3회째에는 알려야 한다"
print("  네트워크·서버 오류는 3회째 ✅")


# ── ③ 리프레시 토큰 만료 경고 ────────────────────────────────────────────────
def creds(days_left, tmp):
    p = os.path.join(tmp, "creds.json")
    with open(p, "w", encoding="utf-8") as f:
        json.dump({"claudeAiOauth": {
            "accessToken": "SHOULD-NEVER-APPEAR",
            "refreshToken": "SHOULD-NEVER-APPEAR",
            "refreshTokenExpiresAt": int((T0 + days_left * 86400) * 1000)}}, f)
    return p


sent = []
REAL_NOTIFY = A.notify              # ⑥ 에서 진짜 함수를 봐야 하므로 덮어쓰기 전에 잡아 둔다
A.notify = lambda text, dry_run=False: sent.append(text)          # noqa: E731

with tempfile.TemporaryDirectory() as tmp:
    A.CREDS_PATH = creds(30, tmp)
    s = {}
    A.check_refresh_expiry(s, T0, True)
    assert not sent, "30일 남았으면 조용해야 한다"

    A.CREDS_PATH = creds(2, tmp)
    A.check_refresh_expiry(s, T0, True)
    assert len(sent) == 1 and "2.0일 전" in sent[0], sent
    assert "SHOULD-NEVER-APPEAR" not in sent[0], "토큰 값이 알림에 새면 안 된다"

    # 하루 안에는 다시 경고하지 않는다
    A.check_refresh_expiry(s, T0 + 3600, True)
    assert len(sent) == 1, sent
    A.check_refresh_expiry(s, T0 + 25 * 3600, True)
    assert len(sent) == 2, "하루 지나면 다시 경고"

    # 이미 만료됐으면 더 센 문구
    sent.clear(); s = {}
    A.CREDS_PATH = creds(-1, tmp)
    A.check_refresh_expiry(s, T0, True)
    assert len(sent) == 1 and "만료됐습니다" in sent[0], sent

    # 파일이 없으면 조용히 넘어간다 — 경고 하나 놓치는 것보다 죽는 게 나쁘다
    sent.clear()
    A.CREDS_PATH = os.path.join(tmp, "없는파일.json")
    A.check_refresh_expiry({}, T0, True)
    assert not sent, sent
print("  리프레시 토큰 만료 경고 ✅")


# ── ④ 상태 파일 왕복 · 못 써도 죽지 않는다 ────────────────────────────────────
with tempfile.TemporaryDirectory() as tmp:
    A.STATE_PATH = os.path.join(tmp, "sub", "claude-auth.json")
    A.save_state({"fails": 2, "category": "authentication"})
    assert A.load_state()["fails"] == 2
    assert not os.path.exists(A.STATE_PATH + ".part"), "임시 파일이 남으면 안 된다"

    # 깨진 파일은 '처음' 으로 읽는다
    with open(A.STATE_PATH, "w", encoding="utf-8") as f:
        f.write("{깨짐")
    assert A.load_state() == {}

    # 쓸 수 없는 경로여도 예외를 내지 않는다
    A.STATE_PATH = os.path.join(tmp, "sub", "claude-auth.json", "더깊이", "x.json")
    A.save_state({"fails": 1})
print("  상태 파일 왕복 · 실패해도 안 죽음 ✅")


# ── ⑤ 알림 문구에 자격 관련 원문이 실리지 않는다 ──────────────────────────────
_, msg = run({"fails": 1, "category": "authentication"}, False, "authentication", T0)
assert msg and "sk-" not in msg and "Bearer" not in msg, msg
# decide() 는 애초에 stdout/stderr 를 받지 않는다 — 실을 방법이 없다
import inspect                                                    # noqa: E402
params = set(inspect.signature(A.decide).parameters)
assert params == {"state", "ok", "category", "now"}, f"decide 가 원문을 받으면 안 된다: {params}"
print("  알림에 자격 원문이 실릴 경로 없음 ✅")

# ── ⑥ 알림 경로 점검 — 점검 장치가 자기 침묵을 감지해야 한다 (2026-09-07)
#    웹훅을 넣었는데 값이 비어 들어간 일이 실제로 있었다(`sh -c` 안의 `read -rsp`;
#    dash 에는 -s 가 없다). 그동안 로그는 `claude 종료 0 · 정상` 만 찍어서,
#    자격이 죽어도 아무도 못 듣는 상태인 줄 몰랐다.
CASES = [
    ("", False, "없음"),                              # 미설정
    ("   ", False, "없음"),                           # 공백만
    ("http://mm.example/hooks/x", False, "깨짐"),      # https 아님
    ("mm.example/hooks/x", False, "깨짐"),             # 스킴 없음
    ("https://mm.example/hooks/abc", True, "OK"),
]
_saved = os.environ.get("MM_WEBHOOK_URL")
try:
    for value, expect_can, expect_word in CASES:
        os.environ["MM_WEBHOOK_URL"] = value
        can, why = A.alert_channel()
        assert can is expect_can, f"{value!r} → {can}, 기대 {expect_can}"
        assert expect_word in why, f"{value!r} → {why}"
        # 사람이 어디를 고쳐야 하는지 알 수 있어야 한다
        if not can:
            assert A.ALERT_ENV in why, why
        # URL 자체는 절대 로그에 싣지 않는다 — 웹훅 URL 은 그 자체가 발송 권한이다
        assert value.strip() not in why or not value.strip(), f"URL 이 문구에 실렸다: {why}"
    print(f"  알림 경로 판정 {len(CASES)}가지 ✅")

    # 잘못된 값이면 notify 가 보내지 않는다 (전송 시도 자체를 안 한다)
    os.environ["MM_WEBHOOK_URL"] = "http://mm.example/hooks/x"
    assert REAL_NOTIFY("테스트", dry_run=False) is False, "https 가 아니면 보내면 안 된다"
    print("  깨진 URL 로는 전송 시도조차 안 함 ✅")
finally:
    if _saved is None:
        os.environ.pop("MM_WEBHOOK_URL", None)
    else:
        os.environ["MM_WEBHOOK_URL"] = _saved

# ── ⑦ --test-alert — 팀 채널에 나가는 글이므로 테스트임이 보여야 한다
sent.clear()
A.notify = lambda text, dry_run=False: (sent.append(text), True)[1]   # noqa: E731
os.environ["MM_WEBHOOK_URL"] = "https://mm.example/hooks/abc"

with tempfile.TemporaryDirectory() as tmp:
    A.STATE_PATH = os.path.join(tmp, "claude-auth.json")
    A.save_state({"fails": 4, "category": "authentication", "alerted_at": T0})
    before = Path(A.STATE_PATH).read_text(encoding="utf-8")

    rc = A.test_alert(dry_run=False)
    assert rc == 0, rc
    assert len(sent) == 1, sent
    body = sent[0]
    assert body.startswith(A.TEST_MARK), "첫 글자부터 테스트임이 보여야 한다"
    assert "실제 장애가 아닙니다" in body, body
    # 진짜 알림 모양을 인용으로 같이 실어서 구분법을 알려 준다
    assert "> 🔴" in body and "붙지 않습니다" in body, body
    # 웹훅 URL 은 본문에 없어야 한다 — URL 이 곧 발송 권한이다
    assert "mm.example" not in body and "hooks/abc" not in body, body

    # **진짜 장애 카운터를 건드리지 않는다** — 시험이 상태를 오염시키면 다음 알림이 어긋난다
    assert Path(A.STATE_PATH).read_text(encoding="utf-8") == before, "상태 파일이 바뀌었다"
print("  --test-alert 표시·상태 무개입 ✅")

# 웹훅이 없으면 보내지 않고 2 로 끝난다 (사람이 넣기 전에 성공했다고 오해하지 않게)
sent.clear()
os.environ["MM_WEBHOOK_URL"] = ""
assert A.test_alert(dry_run=False) == 2 and not sent
print("  웹훅 없으면 시험이 실패로 끝남 ✅")

A.notify = REAL_NOTIFY
os.environ.pop("MM_WEBHOOK_URL", None)

print("TEST310 OK")
