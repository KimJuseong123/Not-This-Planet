#!/usr/bin/env python3
"""러너 호스트의 Claude 자격을 20분마다 확인하고, 죽었으면 알린다 (S15P21B202-310).

**왜 필요한가.** `ai-review` 는 `allow_failure` 라서 자격이 죽어도 **파이프라인은 초록**이다.
MR 코멘트만 소리 없이 사라진다. 2026-09-06 에 `!229` 가 401 로 죽었는데 아무도 몰랐고,
잡 로그를 뒤져서야 찾았다. 아침 점검 카드에도 `ai-review` 항목이 없다.

**왜 새 타이머를 만들지 않나.** `-304`·`-309` 로 이미 20분마다 `claude -p` 를 부르고 있다
(만료된 토큰을 갈아 주려고). 그 결과를 **판정하고 알리기만** 하면 된다. 갱신과 점검은
같은 호출로 동시에 된다.

하는 일
  1. `ai-review` 와 **같은 플래그로** `claude -p` 를 부른다 — 점검이 실제 CI 경로를
     대표해야 의미가 있다. `--bare` 는 쓰지 않는다(keychain 미사용 → `Not logged in`).
  2. 실패를 분류한다(`authentication`/`rate_limit`/`server_error`/`network`/…).
     `scripts/ci/ai_review.py` 의 `classify_claude_failure` 와 같은 규칙이고,
     `scripts/ci/tests/test_claude_auth_check.py` 가 **둘이 어긋나지 않는지 본다.**
  3. 상태 파일을 보고 정책대로 MM 에 알린다. 시끄러우면 무시당하므로 문턱을 둔다.
  4. 리프레시 토큰 만료가 3일 안이면 미리 경고한다 — 그날이 오면 갱신 자체가 불가능하고
     사람이 러너 호스트에서 재로그인해야 한다.
  5. **알릴 수 있는 상태인지 매 회차 말한다.** 알릴 일이 없어도 말한다 — 웹훅이 비어 있으면
     자격이 죽어도 아무도 못 듣는데, 그때 로그가 `정상` 만 찍으면 점검 장치가 자기 침묵을
     감추는 셈이다. 자격은 멀쩡한데 알릴 수단이 없으면 종료 코드 2 로 끝난다.

**자격 값은 어디에도 남기지 않는다.** 만료 시각(숫자)만 읽고, `claude` 의 stdout/stderr 는
분류에만 쓰고 로그·알림에 싣지 않는다.

로컬 실행(알림 없이 판정만):
    MM_WEBHOOK_URL= python3 scripts/runner/claude_auth_check.py --dry-run
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time
import urllib.request

STATE_PATH = os.environ.get("CLAUDE_AUTH_STATE", "/var/lib/metagame/claude-auth.json")
CREDS_PATH = os.environ.get("CLAUDE_CREDENTIALS",
                            os.path.expanduser("~/.claude/.credentials.json"))
CLAUDE = os.environ.get("CLAUDE_BIN", "/usr/local/bin/claude")
MODEL = os.environ.get("CLAUDE_CHECK_MODEL", "claude-opus-5")
MENTION = os.environ.get("ALERT_MENTION", "rmawnsdud")      # team.json 의 infra
CALL_TIMEOUT = int(os.environ.get("CLAUDE_CHECK_TIMEOUT", "120"))
ALERT_ENV = os.environ.get("CLAUDE_ALERT_ENV", "/etc/metagame/alert.env")   # 안내 문구용 경로

# 몇 번 연속 실패해야 알리나. 저절로 낫는 것과 사람이 손대야 하는 것을 가른다.
# 20분 주기이므로 2회 ≈ 40분, 3회 ≈ 1시간, 6회 ≈ 2시간.
THRESHOLD = {
    "authentication": 2,     # 저절로 낫지 않는다. 사람이 재로그인해야 한다
    "invalid_request": 2,    # 옵션·모델명이 틀렸다. 역시 사람 일
    "network": 3,            # 대개 저절로 낫는다
    "server_error": 3,
    "unknown": 3,
    "rate_limit": 6,         # 구독 한도는 정상 동작의 일부다
}
DEFAULT_THRESHOLD = 3
REALERT_SECONDS = 6 * 3600           # 계속 실패해도 6시간에 한 번만
REFRESH_WARN_DAYS = 3                # 리프레시 토큰 만료 며칠 전부터 경고하나
REFRESH_WARN_SECONDS = 24 * 3600     # 그 경고는 하루 한 번

HINT = {
    "authentication": "러너 호스트에서 `claude` 재로그인이 필요합니다 — "
                      "`ssh ubuntu@j15b202.p.ssafy.io` 후 `claude` 로 로그인하세요.",
    "rate_limit": "구독 사용량 한도입니다. 대개 시간이 지나면 풀립니다.",
    "server_error": "Anthropic 쪽 오류입니다. 대개 저절로 낫습니다.",
    "network": "러너 호스트의 네트워크 문제일 수 있습니다.",
    "invalid_request": "모델명·옵션을 확인하세요 (CLAUDE_CHECK_MODEL).",
}


# ── Claude 호출 ──────────────────────────────────────────────────────────────
def call_claude() -> tuple[int, str, str]:
    """(returncode, stdout, stderr). 실행 자체가 안 되면 returncode 127."""
    cmd = [CLAUDE, "-p", "--output-format", "text", "--model", MODEL,
           "--tools", "", "--strict-mcp-config", "--setting-sources", "user",
           "--disable-slash-commands", "--permission-mode", "dontAsk",
           "--no-session-persistence"]
    env = dict(os.environ)
    for k in ("CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT"):
        env.pop(k, None)
    env.setdefault("PYTHONUTF8", "1")
    try:
        r = subprocess.run(cmd, input="PONG", capture_output=True, text=True,
                           encoding="utf-8", errors="replace", env=env, timeout=CALL_TIMEOUT)
        return r.returncode, r.stdout or "", r.stderr or ""
    except FileNotFoundError:
        return 127, "", f"claude 실행 파일을 찾을 수 없다: {CLAUDE}"
    except subprocess.TimeoutExpired:
        return 124, "", f"claude 가 {CALL_TIMEOUT}초 안에 끝나지 않았다 (timed out)"


def classify(stdout: str, stderr: str) -> tuple[str, int | None]:
    """`ai_review.classify_claude_failure` 와 **같은 규칙**이어야 한다.

    여기서 따로 구현하는 이유는 이 스크립트가 러너 호스트의 `/opt` 에서 도는 독립 파일이라
    `scripts/ci/` 를 임포트할 수 없어서다. 대신 오프라인 테스트가 두 판정을 같은 입력으로
    비교해서, 한쪽만 고치면 실패한다."""
    evidence = (stderr[-16000:] + "\n" + stdout[-16000:]).lower()
    m = re.search(r"(?:api error|http(?: status)?|status|error_status)[^\n]{0,80}?"
                  r"(401|403|408|409|429|500|502|503|504)(?!\d)", evidence)
    http_status = int(m.group(1)) if m else None
    rules = [
        ("authentication", {401, 403}, ("authentication_failed", "authentication error",
                                        "invalid authentication", "not logged in", "login expired",
                                        "oauth token revoked", "oauth token has expired",
                                        "oauth_org_not_allowed")),
        ("rate_limit", {429}, ("rate_limit", "rate limit", "temporarily limiting requests",
                               "session limit", "weekly limit", "usage limit")),
        ("server_error", {500, 502, 503, 504}, ("server_error", "server error",
                                                "internal server error", "overloaded",
                                                "temporarily unavailable")),
        ("network", {408, 409}, ("connection error", "connection reset", "unable to connect",
                                 "network error", "socket is closed", "timed out", "timeout",
                                 "econnreset", "etimedout")),
        ("invalid_request", set(), ("invalid_request", "invalid request", "model_not_found",
                                    "max_output_tokens", "unknown option", "invalid effort")),
    ]
    for cat, statuses, patterns in rules:
        if http_status in statuses or any(p in evidence for p in patterns):
            return cat, http_status
    return "unknown", http_status


# ── 상태 ─────────────────────────────────────────────────────────────────────
def load_state() -> dict:
    try:
        with open(STATE_PATH, encoding="utf-8") as f:
            d = json.load(f)
        return d if isinstance(d, dict) else {}
    except (OSError, ValueError):
        # 상태를 못 읽으면 '처음' 으로 본다. 알림 하나를 놓칠 수는 있어도 죽지는 않는다.
        return {}


def save_state(state: dict) -> None:
    try:
        os.makedirs(os.path.dirname(STATE_PATH) or ".", exist_ok=True)
        tmp = STATE_PATH + ".part"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(state, f, ensure_ascii=False, indent=2)
        os.replace(tmp, STATE_PATH)          # 중간에 죽어도 반쯤 쓰인 파일이 남지 않는다
    except OSError as e:
        print(f"[warn] 상태 파일을 쓰지 못했다({e}) — 다음 회차가 '처음'으로 보인다")


# ── 알림 ─────────────────────────────────────────────────────────────────────
def alert_channel() -> tuple[bool, str]:
    """(보낼 수 있나, 사람이 읽을 설명).

    **왜 매 회차마다 확인하나.** 2026-09-07 에 웹훅을 넣었는데 값이 비어 들어갔다
    (`sh -c` 안에서 `read -rsp` 를 썼는데 dash 에는 `-s` 가 없다). 그동안 로그는
    `claude 종료 0 · 정상` 만 찍었고, 알림이 나갈 수 없는 상태인 줄 아무도 몰랐다.
    **점검 장치가 자기 침묵을 감지하지 못하는 것**은 이 이슈가 고치려던 것과 같은 결함이다.
    그래서 실제로 알릴 일이 없어도 알릴 수 있는 상태인지는 매번 말한다."""
    url = os.environ.get("MM_WEBHOOK_URL", "").strip()
    if not url:
        return False, ("알림 경로 **없음** — MM_WEBHOOK_URL 이 비어 있다 "
                       f"({ALERT_ENV} 확인). 자격이 죽어도 알림이 가지 않는다")
    if not url.startswith("https://"):
        # 값이 잘못 들어간 것을 '설정됨' 으로 세면 없는 것보다 나쁘다 — 안심시키기 때문이다
        return False, (f"알림 경로 **깨짐** — MM_WEBHOOK_URL 이 https:// 로 시작하지 않는다 "
                       f"(길이 {len(url)}). {ALERT_ENV} 를 다시 넣어야 한다")
    return True, "알림 경로 OK (MM 웹훅)"


def notify(text: str, dry_run: bool = False) -> bool:
    """MM 으로 보낸다. URL 이 없거나 실패해도 **예외를 내지 않는다** — 알림 때문에
    점검 자체가 죽으면 본말이 전도된다."""
    print(text)
    can, why = alert_channel()
    url = os.environ.get("MM_WEBHOOK_URL", "").strip()
    if dry_run or not can:
        print(f"MM 전송 생략 ({'--dry-run' if dry_run else why})")
        return False
    try:
        req = urllib.request.Request(
            url, data=json.dumps({"text": text, "username": "METAGAME 봇"}).encode("utf-8"),
            headers={"Content-Type": "application/json"}, method="POST")
        with urllib.request.urlopen(req, timeout=15) as r:
            print(f"MM 전송 → {r.status}")
            return 200 <= r.status < 300
    except Exception as e:                                   # noqa: BLE001
        print(f"[warn] MM 전송 실패: {type(e).__name__}")     # 응답 본문은 싣지 않는다
        return False


# ── 리프레시 토큰 만료 ────────────────────────────────────────────────────────
def refresh_expires_at() -> float | None:
    """리프레시 토큰 만료(epoch 초). 못 읽으면 None. **토큰 값은 읽지 않는다.**"""
    try:
        with open(CREDS_PATH, encoding="utf-8") as f:
            d = json.load(f)
        ms = (d.get("claudeAiOauth") or {}).get("refreshTokenExpiresAt")
        return float(ms) / 1000.0 if isinstance(ms, (int, float)) and ms > 0 else None
    except (OSError, ValueError, TypeError):
        return None


def check_refresh_expiry(state: dict, now: float, dry_run: bool) -> None:
    exp = refresh_expires_at()
    if exp is None:
        return
    days = (exp - now) / 86400.0
    if days > REFRESH_WARN_DAYS:
        state.pop("refresh_warned_at", None)
        return
    if now - float(state.get("refresh_warned_at") or 0) < REFRESH_WARN_SECONDS:
        return                                   # 하루 한 번이면 충분하다
    state["refresh_warned_at"] = now
    when = time.strftime("%Y-%m-%d %H:%M", time.localtime(exp))
    if days <= 0:
        head = "🔴 **Claude 리프레시 토큰이 만료됐습니다**"
        tail = "지금은 갱신 자체가 불가능합니다. `ai-review` 가 곧 멈춥니다."
    else:
        head = f"🟠 **Claude 리프레시 토큰 만료 {days:.1f}일 전**"
        tail = "만료되면 자동 갱신이 불가능해지고 `ai-review` 가 멈춥니다."
    notify(f"{head} (만료 {when})\n\n{tail}\n"
           f"러너 호스트에서 재로그인해 주세요 → @{MENTION}\n"
           f"<sub>S15P21B202-310 · claude-auth-check</sub>", dry_run)


# ── 배송 시험 ────────────────────────────────────────────────────────────────
TEST_MARK = "🧪 **[테스트]**"


def test_alert(dry_run: bool) -> int:
    """웹훅을 넣은 뒤 **실제로 배송되는지** 한 번 확인한다.

    정책·판정은 오프라인 테스트가 다 보지만 HTTP 전송만은 진짜 URL 이 있어야 확인된다.
    그래서 사람이 웹훅을 넣은 직후 한 번 부르는 용도다.

    두 가지를 지킨다.
      · **상태 파일을 건드리지 않는다** — 시험이 진짜 장애 카운터를 오염시키면 안 된다
      · 팀 채널에 나가는 글이므로 **테스트임이 한눈에 보여야 한다.** 진짜 알림 모양을
        인용으로 같이 실어서, 앞에 🧪 가 붙는지로 구분할 수 있게 한다
    """
    can, why = alert_channel()
    if not can and not dry_run:
        print(f"[error] {why}")
        return 2
    # 진짜 알림이 어떻게 생겼는지 그대로 만들어 보여 준다(상태는 저장하지 않는다)
    _, sample = decide({"fails": 1, "category": "authentication"}, False, "authentication", time.time())
    quoted = "> " + (sample or "").replace("\n", "\n> ")
    text = (f"{TEST_MARK} Claude 자격 점검 — **알림 배송 시험**입니다. 실제 장애가 아닙니다.\n\n"
            f"이 메시지가 보이면 러너 호스트의 알림 경로가 정상입니다. "
            f"20분마다 Claude 자격을 점검하고, 죽으면 여기로 알립니다(`S15P21B202-310`).\n\n"
            f"진짜 장애는 이런 모양으로 옵니다 — 앞에 {TEST_MARK} 가 **붙지 않습니다**:\n\n"
            f"{quoted}\n\n"
            f"<sub>`claude_auth_check.py --test-alert` · 상태 파일은 건드리지 않았습니다</sub>")
    ok = notify(text, dry_run)
    if dry_run:
        print("(--dry-run 이라 실제로 보내지 않았다)")
        return 0
    print("배송 성공" if ok else "배송 실패 — 웹훅 URL·네트워크 확인")
    return 0 if ok else 1


# ── 본체 ─────────────────────────────────────────────────────────────────────
def decide(state: dict, ok: bool, category: str | None, now: float) -> tuple[dict, str | None]:
    """(새 상태, 보낼 알림 본문 또는 None).

    순수 함수다 — 테스트가 시간을 주입해서 정책 전체를 확인한다."""
    new = dict(state)
    if ok:
        fails = int(state.get("fails") or 0)
        new.update({"fails": 0, "category": None, "last_ok": now})
        new.pop("alerted_at", None)
        if state.get("alerted_at"):
            # 알렸던 장애가 끝났다. "이제 괜찮다" 를 말해야 다음 실패가 신호가 된다.
            return new, (f"🟢 **Claude 자격 복구됨** — `ai-review` 가 다시 동작합니다\n"
                         f"(연속 실패 {fails}회 뒤 복구)\n"
                         f"<sub>S15P21B202-310 · claude-auth-check</sub>")
        return new, None

    cat = category or "unknown"
    # 원인이 바뀌면 연속 횟수를 다시 센다 — 다른 장애를 같은 것으로 묶으면 문턱이 무의미해진다
    fails = int(state.get("fails") or 0) + 1 if state.get("category") == cat else 1
    new.update({"fails": fails, "category": cat, "last_fail": now})

    need = THRESHOLD.get(cat, DEFAULT_THRESHOLD)
    if fails < need:
        return new, None                        # 아직 일시적일 수 있다
    last = float(state.get("alerted_at") or 0)
    if state.get("alerted_at") and now - last < REALERT_SECONDS:
        return new, None                        # 도배 방지
    new["alerted_at"] = now
    again = " (계속 실패 중)" if state.get("alerted_at") else ""
    return new, (f"🔴 **Claude 가 응답하지 않습니다**{again} — 원인 `{cat}`\n\n"
                 f"연속 실패 **{fails}회** (20분 주기). {HINT.get(cat, '')}\n"
                 f"이 동안 `ai-review` 는 **조용히 건너뜁니다** — 파이프라인은 초록이지만 "
                 f"MR 리뷰 코멘트가 달리지 않습니다. → @{MENTION}\n"
                 f"<sub>S15P21B202-310 · claude-auth-check</sub>")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="러너 호스트의 Claude 자격 점검 (S15P21B202-310)")
    ap.add_argument("--dry-run", action="store_true", help="MM 전송 없이 판정만 출력")
    ap.add_argument("--test-alert", action="store_true",
                    help="테스트 알림을 한 번 보내 배송을 확인한다 (상태 파일은 건드리지 않는다)")
    a = ap.parse_args(argv)

    if a.test_alert:
        return test_alert(a.dry_run)

    now = time.time()
    can_alert, why = alert_channel()
    print(why if can_alert else f"[warn] {why}")

    rc, out, err = call_claude()
    ok = rc == 0
    cat = None if ok else classify(out, err)[0]
    # 원문은 싣지 않는다 — 자격 관련 문자열이 섞일 여지를 주지 않는다
    print(f"claude 종료 {rc} · {'정상' if ok else f'실패({cat})'}")

    state = load_state()
    state, message = decide(state, ok, cat, now)
    if message:
        notify(message, a.dry_run)
    else:
        print("알림 조건 아님 — 상태만 갱신")

    check_refresh_expiry(state, now, a.dry_run)
    save_state(state)
    # 실패면 0 이 아닌 값으로 끝낸다 — `systemctl status` 와 journal 이 사실을 말하게 한다.
    # oneshot 이 실패해도 타이머는 계속 돈다.
    if not ok:
        return 1
    # 자격은 멀쩡한데 알릴 수단이 없는 상태도 **정상이 아니다.** 이걸 초록으로 두면
    # `systemctl status` 가 "잘 돌고 있다" 고 말하는데 실제로는 아무도 못 듣는다.
    if not (can_alert or a.dry_run):
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
