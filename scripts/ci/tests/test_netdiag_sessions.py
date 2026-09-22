"""netdiag 가 **같은 판만** 견주고, 못 고르면 판정하지 않는가 (S15P21B202-685).

실행: python scripts/ci/tests/test_netdiag_sessions.py
네트워크·Unity 없이 가짜 로그를 만들어 돌린다.

**왜 이 검사가 있는가.** 2026-09-22 첫 실전 QA 에서 이 도구가 **틀린 답을 냈다.**
한 `Player.log` 에는 한 실행의 여러 판이 들어 있는데(솔로로 둘러보다 방을 만들고,
나가서 남의 방에 들어간다) 도구는 그걸 가르지 않고 `host[-1]` — **마지막 줄의 역할**만
보고 전부를 통째로 견줬다. 그래서

    겹치는 묶음 195개 중 체크섬 일치 190개 (97%)
    ❌ 갈린 묶음: seq=1650 호스트 t=622.0 · 참가자 t=417.0 …

가 나왔다. **서로 다른 판을 견준 수치다.** 같은 판만 골라 다시 재니 **121개 전부
일치**였다. 그 답으로 "세계가 갈렸다" 는 이슈를 팔 뻔했다.

그때도 `⚠ 두 로그의 역할이 같다` 한 줄은 나갔다. **경고를 내고 판정을 그대로 내면
아무도 그 경고를 읽지 않는다** — 그래서 지금은 짝을 못 고르면 멈춘다.

**`assert` 를 쓰지 않는다** (`!461` AI 리뷰). `python -O` 로 돌리면 통째로 사라진다.
"""
import contextlib
import io
import os
import sys
import tempfile

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))

import netdiag as N  # noqa: E402

fails = []


def check(ok, msg):
    if not ok:
        fails.append(msg)


NAMES = "[NETDIAG] tracked(6) = A,B,C,D,E,F"


def row(t, role, seq=None, poses="aaaa", live=0, pending=0, scars=0):
    """한 줄. `seq=None` 이면 주고받은 묶음이 아니다(`-`)."""
    return ("[NETDIAG] t=%.1f role=%s rtt=100.0 fps=100 btm=2.5 buftime=90.0 buf=3 "
            "tracked=6 clamps=16 tools=2 drops=0 pending=%d live=%d scars=%d crew=2 "
            "seq=%s poses=%s" % (t, role, pending, live, scars,
                                 "-" if seq is None else seq, poses))


def write(dirpath, name, lines):
    p = os.path.join(dirpath, name)
    with io.open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(NAMES + "\n")
        f.write("\n".join(lines) + "\n")
    return p


def run(host_lines, client_lines):
    """(종료코드, 출력)."""
    with tempfile.TemporaryDirectory() as d:
        h = write(d, "host.log", host_lines)
        c = write(d, "client.log", client_lines)
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            rc = N.main([h, c])
        return rc, buf.getvalue()


def session(role, t0, seq0, count, poses=lambda i: "same%04d" % i, **kw):
    return [row(t0 + i, role, seq=seq0 + i * 10, poses=poses(i), **kw)
            for i in range(count)]


print()
print("== netdiag 판 고르기 (S15P21B202-685) ==")


# ── ① 여러 판이 든 로그에서 **같은 판만** 고른다 — 이 파일의 핵심
#
#    호스트 로그: 솔로 한 판 → 호스트 한 판
#    참가자 로그: 호스트 한 판(번호가 겹치게 둔다) → 참가자 한 판
#    섞어 보면 체크섬이 갈리고, 제대로 고르면 전부 일치한다.
N_SHARED = N.MIN_SHARED + 5
HOST = session("solo", 10.0, 1000, N_SHARED, poses=lambda i: "solo%04d" % i) \
    + session("host", 500.0, 3000, N_SHARED)
CLIENT = session("host", 10.0, 3000, N_SHARED, poses=lambda i: "other%04d" % i) \
    + session("client", 700.0, 3000, N_SHARED)

rc, out = run(HOST, CLIENT)
check("견줄 판 :" in out, f"어느 판을 골랐는지 말하지 않는다:\n{out[:400]}")
check("주고받은 모든 묶음에서 세계가 같았다" in out,
      "같은 판을 골랐으면 전부 일치여야 한다 — 판을 섞고 있다:\n" + out[:900])
check(rc == 0, f"같은 판이 전부 일치인데 종료 코드가 {rc}")
print("  여러 판 중 같은 판만 골라 견준다 ✅")


# ── ② 짝을 못 고르면 **판정하지 않는다**
#     호스트 자리에 참가자 로그를 주면 `role=host` 판이 없다.
rc, out = run(session("client", 10.0, 3000, N_SHARED),
              session("client", 10.0, 3000, N_SHARED))
check(rc == 1, f"호스트 판이 없는데 종료 코드가 {rc}")
check("견줄 판을 고르지 못했다" in out and "role=host" in out,
      f"무엇이 없어서 멈췄는지 말하지 않는다:\n{out[:400]}")
check("주고받은 모든 묶음에서 세계가 같았다" not in out,
      "판을 못 골랐는데 일치 판정을 냈다")
print("  짝을 못 고르면 판정하지 않는다 ✅")


# ── ③ **시각 차이가 일정하지 않으면** 그 짝을 쓰지 않는다
#     번호는 판마다 0 에서 다시 세므로 서로 다른 판이 우연히 겹친다. 같은 판이면
#     두 로그의 `t` 차이가 각자 켠 시각만큼 **일정하다** — 그것이 가르는 자다.
skewed = [row(10.0 + i * (1 if i % 2 == 0 else 40), "client", seq=3000 + i * 10)
          for i in range(N_SHARED)]
rc, out = run(session("host", 10.0, 3000, N_SHARED), skewed)
check(rc == 1, f"시각 차이가 제멋대로인데 짝으로 썼다 (종료 코드 {rc})")
check("견줄 판을 고르지 못했다" in out, f"멈춘 사유를 말하지 않는다:\n{out[:300]}")
print("  시각 차이가 일정하지 않으면 쓰지 않는다 ✅")


# ── ④ 겹침이 너무 적으면 판정하지 않는다 (몇 개로 높은 일치율을 내지 않는다)
few = N.MIN_SHARED - 1
rc, out = run(session("host", 10.0, 3000, few), session("client", 10.0, 3000, few))
check(rc == 1, f"겹치는 묶음이 {few}개인데 판정했다 (종료 코드 {rc})")
check(str(N.MIN_SHARED) in out, f"최소 표본 수를 말하지 않는다:\n{out[:300]}")
print(f"  겹침이 {N.MIN_SHARED}개 미만이면 판정하지 않는다 ✅")


# ── ⑤ 체크섬이 갈리면 **종료 코드로도** 말한다
#     찍고 0 으로 끝나면 자동화에 걸 수 없고, 사람이 훑다 놓치면 통과로 읽힌다.
rc, out = run(session("host", 10.0, 3000, N_SHARED),
              session("client", 10.0, 3000, N_SHARED,
                      poses=lambda i: "diff%04d" % i))
check(rc == 1, f"체크섬이 전부 갈렸는데 종료 코드가 {rc}")
check("갈린 묶음" in out, "갈린 것을 나열하지 않는다")
print("  체크섬이 갈리면 종료 코드 1 ✅")


# ── ⑥ 개수 차이는 **종료 코드를 바꾸지 않는다**
#     참가자는 ~100ms 늦게 받고, 공세 묶음이 한 번에 여섯 개씩 나오는 순간에는 둘셋
#     뒤처진다(2026-09-22 실측: t=93 에 호스트 10 · 참가자 13, 체크섬은 전부 일치).
#     **사양으로 정한 값을 빨간불로 내면 사람이 빨간불 전체를 무시한다**(-654 Q0).
rc, out = run(session("host", 10.0, 3000, N_SHARED, live=10, pending=10),
              session("client", 10.0, 3000, N_SHARED, live=13, pending=13))
check(rc == 0, f"개수 차이만으로 빨간불이 됐다 (종료 코드 {rc})")
check("개수는 참고다" in out, f"개수가 판정이 아니라는 말이 없다:\n{out[-700:]}")
print("  개수 차이는 판정을 바꾸지 않는다 (판정은 체크섬) ✅")


# ── ⑦ 없는 파일에 역추적을 뱉지 않는다 — 문서 경로가 틀려 실제로 물린 자리다
buf = io.StringIO()
with contextlib.redirect_stdout(buf):
    rc = N.main([os.path.join(ROOT, "없는파일.log"),
                 os.path.join(ROOT, "없는파일2.log")])
out = buf.getvalue()
check(rc == 2, f"없는 파일에 종료 코드가 {rc}")
check("찾지 못했다" in out, f"무엇을 못 찾았는지 말하지 않는다:\n{out[:300]}")
print("  없는 파일 — 역추적 대신 안내 ✅")


# ── ⑧ 적어 둔 로그 경로가 **실제 폴더 이름**과 같다
#
#     실제는 `LocalLow\\Not This Planet\\Not This Planet\\`(띄어쓰기)인데 세 곳이
#     `NotThisPlanet`(붙여 쓴) 으로 적혀 있었다. 그대로 따라온 사람은 파일을 못 찾는다.
#     옛 기록 둘(`-269`·`-352`)은 맞게 적혀 있었으니 **나중에 쓴 쪽이 갈렸다.**
BS = chr(92)
WRONG = "LocalLow" + BS + "NotThisPlanet"
WRONG_ESC = "LocalLow" + BS * 2 + "NotThisPlanet"
RIGHT = "Not This Planet"
WATCHED = (
    "docs/multiplayer-test-guide.md",
    "scripts/ci/netdiag.py",
    "METAGAME/Assets/_Project/Scripts/Runtime/Network/NetDiagnostics.cs",
)
for rel in WATCHED:
    with io.open(os.path.join(ROOT, *rel.split("/")), encoding="utf-8") as f:
        text = f.read()
    check(WRONG not in text and WRONG_ESC not in text,
          f"{rel} 에 붙여 쓴 `NotThisPlanet` 폴더 이름이 남아 있다 — 실제 폴더는 "
          f"`{RIGHT}` 다(띄어쓰기). 그대로 따라가면 파일을 못 찾는다")
    check("LocalLow" in text and RIGHT in text,
          f"{rel} 에 로그 위치 안내가 사라졌다 — 사람이 파일을 찾을 길이 없다")
print(f"  로그 경로가 세 곳에서 실제 폴더 이름과 같다 ({len(WATCHED)}곳) ✅")


if fails:
    print(f"\n❌ 실패 {len(fails)}건")
    for f in fails:
        print(f"  · {f}")
    raise SystemExit(1)
print("TEST685 OK")
