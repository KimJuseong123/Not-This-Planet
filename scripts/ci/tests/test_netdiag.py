"""호스트/참가자 로그 대조 분석기 (S15P21B202-352).

실행: python scripts/ci/tests/test_netdiag.py

왜 이 테스트가 있는가 — **분석기가 틀리면 검증이 의미가 없다.** 1차 2인 실측을 화면
오버레이를 읽어 손으로 적었더니 느리고 빠뜨렸고(kcp 박스는 HUD 에 가려 아예 못 봤다),
무엇보다 두 화면을 동시에 볼 수 없어 "같은 상태인가" 를 확인할 방법이 없었다. 그래서
로그 대조로 바꿨는데, 그 판정이 조용히 틀리면 예전보다 나쁘다 — 틀린 초록을 믿게 된다.

그래서 **불일치를 일부러 심어 두고 분석기가 그것을 잡는지** 본다.

체크섬은 **묶음 번호(seq)로 짝짓는다.** 시각으로 짝지으면 참가자 쪽이 보간해 가는
중이라 움직이는 것이 항상 갈려 보인다 — 거짓 빨강만 나온다. 자세한 계산은
`WorldSync.BundleSeq` 의 주석에 있다.
"""
import io
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.join(os.path.dirname(HERE), "netdiag.py")


def line(t, role, poses, tracked=6, live=1, seq=None, **kw):
    """진단 한 줄. seq 를 안 주면 시각에서 뽑아(초 × 30) 양쪽이 자연히 짝지어진다."""
    if seq is None:
        seq = int(float(t)) * 30

    d = {"t": t, "role": role, "rtt": "-", "fps": "120", "btm": "-", "buftime": "-",
         "buf": "-", "tracked": tracked, "clamps": 16, "tools": 2, "drops": 2,
         "pending": 2, "live": live, "scars": 3, "crew": 2, "seq": seq, "poses": poses}
    d.update(kw)
    return "[NETDIAG] " + " ".join(f"{k}={v}" for k, v in d.items())


def run(host_lines, client_lines):
    with tempfile.TemporaryDirectory() as d:
        h = os.path.join(d, "host.log")
        c = os.path.join(d, "client.log")
        io.open(h, "w", encoding="utf-8").write("\n".join(host_lines) + "\n")
        io.open(c, "w", encoding="utf-8").write("\n".join(client_lines) + "\n")
        r = subprocess.run([sys.executable, TOOL, h, c], capture_output=True, text=True,
                           encoding="utf-8", errors="replace",
                           env={**os.environ, "PYTHONIOENCODING": "utf-8"})
        return r.returncode, (r.stdout or "") + (r.stderr or "")


NAMES = "[NETDIAG] tracked(6) = Part_1 | Part_2 | Part_3 | Part_4 | Bat | WeldGun"

# **표본을 최소치 위로 둔다** (S15P21B202-685). 도구가 이제 겹치는 묶음이
# `MIN_SHARED` 개 미만이면 판정을 내지 않는다 — 몇 개 우연히 겹친 것으로 높은
# 일치율을 내던 것이 실전에서 틀린 답을 만들었다. 이 파일의 뜻은 *불일치를 잡는가*
# 이고 표본 수는 그 뜻과 무관하므로, 한 곳에서 정해 전부 같이 쓴다.
N = 24

# ── ① 완전히 일치하면 초록이라고 말한다
h = [NAMES] + [line(f"{10+k}.0", "host", "aabbccdd") for k in range(N)]
c = [NAMES] + [line(f"{10+k}.0", "client", "aabbccdd") for k in range(N)]
rc, out = run(h, c)
assert rc == 0, rc
assert "주고받은 모든 묶음에서 세계가 같았다" in out, out
assert "목록과 순서가 같다" in out, out
assert "겹치는 모든 시각에서 개수가 같았다" in out, out
print("  완전 일치 → 초록 ✅")

# ── ② 체크섬이 한 묶음만 갈려도 잡고, **그 번호를 지목**한다
c2 = [NAMES] + [line(f"{10+k}.0", "client", "aabbccdd" if k != 2 else "11223344")
                for k in range(N)]
rc, out = run(h, c2)
assert "갈린 묶음" in out, out
assert "seq=360" in out, "갈린 묶음 번호를 지목해야 한다: " + out
assert f"일치 {N - 1}개" in out, out
assert "주고받은 모든 묶음에서 세계가 같았다" not in out, out
print("  체크섬 1회 갈림 → 그 묶음을 지목 ✅")

# ── ③ **보간 지연을 갈림으로 세지 않는다** — 이 도구가 처음에 틀렸던 그것
#     같은 번호의 묶음은 양쪽에서 바이트가 같으므로, 참가자 화면이 뒤처져 있어도
#     체크섬은 일치해야 한다. 시각이 다른 줄에 실려 와도 번호로 짝지어야 한다.
h3 = [NAMES] + [line(f"{10+k}.0", "host", f"cafe000{k}") for k in range(N)]
c3 = [NAMES] + [line(f"{11+k}.0", "client", f"cafe000{k}", seq=(10 + k) * 30)
                for k in range(N)]
rc, out = run(h3, c3)
assert "주고받은 모든 묶음에서 세계가 같았다" in out, \
    "시각이 밀려도 번호가 같으면 일치로 봐야 한다: " + out
print("  시각이 밀려도 번호로 짝지어 일치 ✅")

# ── ④ tracked 개수가 다르면 **가장 무거운 경고**를 낸다
c4 = ["[NETDIAG] tracked(4) = Part_1 | Part_2 | Part_3 | Part_4"] + \
     [line(f"{10+k}.0", "client", "aabbccdd", tracked=4) for k in range(N)]
rc, out = run(h, c4)
assert "개수가 다르다" in out, out
assert "자세를 하나도 적용하지 않는다" in out, "왜 심각한지 말해야 한다: " + out
assert "같은 빌드인지" in out, "무엇을 먼저 볼지 알려 줘야 한다: " + out
print("  tracked 개수 갈림 → 원인 후보까지 안내 ✅")

# ── ⑤ 개수는 같은데 순서가 다른 것도 잡는다
#     묶음 체크섬으로는 안 잡히는 사고다(양쪽이 같은 바이트를 받았으니 값이 같다).
#     그래서 목록 줄을 그대로 비교하는 검사가 따로 있어야 한다.
c5 = ["[NETDIAG] tracked(6) = Part_2 | Part_1 | Part_3 | Part_4 | Bat | WeldGun"] + \
     [line(f"{10+k}.0", "client", "aabbccdd") for k in range(N)]
rc, out = run(h, c5)
assert "순서·이름이 다르다" in out, out
assert "남의 자리에 값이 들어간다" in out, out
print("  개수 같고 순서 다름 → 목록 비교가 잡는다 ✅")

# ── ⑥ 세계 상태 개수 차이를 항목별로 세고 예시를 준다
#
#     **±1 은 지연으로 적는다** (S15P21B202-685 · `-654` Q0). 참가자는 ~100ms
#     늦게 받으므로 그 순간 날아가는 운석 하나가 엇갈린다 — 그것을 ❌ 로 찍어 온
#     탓에 2026-09-22 QA 에서 "개수가 갈렸다" 로 읽혔고, 같은 판의 체크섬은 121개
#     전부 일치였다. 개수는 참고이고 **판정은 체크섬이 한다.**
c6 = [NAMES] + [line(f"{10+k}.0", "client", "aabbccdd", live=0) for k in range(N)]
rc, out = run(h, c6)
assert "live:" in out and f"{N}/{N}" in out, out
assert "호스트=1 참가자=0" in out, "예시를 줘야 한다: " + out
print("  세계 상태 개수 차이 → 항목·횟수·예시 ✅")

# ── ⑦ 도구 개수가 갈리면 잡는다 (휴대 비트 배열의 길이 검사가 여기 걸린다)
h7 = [NAMES] + [line(f"{10+k}.0", "host", "aabbccdd", tools=2) for k in range(N)]
c7 = [NAMES] + [line(f"{10+k}.0", "client", "aabbccdd", tools=1) for k in range(N)]
rc, out = run(h7, c7)
assert "tools:" in out and f"{N}/{N}" in out, out
assert "호스트=2 참가자=1" in out, out
print("  도구 개수 갈림 → 잡는다 ✅")

# ── ⑧ 진단 줄이 없으면 **조용히 초록이라고 하지 않는다**
rc, out = run(h, ["그냥 로그 한 줄"])
assert rc == 1, f"실패로 끝내야 한다 — 종료 {rc}"
assert "진단 줄이 없다" in out, out
assert "netdiag" in out, "무엇을 켜야 하는지 알려 줘야 한다: " + out
print("  한쪽에 진단 줄 없음 → 종료 1 + 안내 ✅")

# ── ⑨ 겹치는 묶음이 없으면 그렇게 말한다 (조용히 0% 로 넘기지 않는다)
#
#     **이제 판 고르기 단계에서 먼저 걸린다** (S15P21B202-685). 뜻은 그대로다 —
#     조용히 초록이 되지 않아야 한다. 다만 도구가 "0% 일치" 를 내는 대신
#     **견줄 판을 고르지 못했다** 고 말하고 멈춘다. 틀린 수치보다 "모른다" 가 낫다.
c9 = [NAMES] + [line(f"{50+k}.0", "client", "aabbccdd") for k in range(N)]
rc, out = run(h, c9)
assert rc == 1, f"겹치는 묶음이 없는데 판정했다 — 종료 {rc}"
assert "견줄 판을 고르지 못했다" in out, out
assert "주고받은 모든 묶음에서 세계가 같았다" not in out, out
print("  겹치는 묶음 없음 → 판정하지 않는다 ✅")

# ── ⑩ 묶음 번호가 아예 없는 로그(옛 빌드)를 초록으로 넘기지 않는다
#
#     **이제 판 고르기 단계에서 먼저 걸린다** (S15P21B202-685). 뜻은 그대로다 —
#     조용히 초록이 되지 않아야 한다. 다만 도구가 "0% 일치" 를 내는 대신
#     **견줄 판을 고르지 못했다** 고 말하고 멈춘다. 틀린 수치보다 "모른다" 가 낫다.
c10 = [NAMES] + [line(f"{10+k}.0", "client", "aabbccdd", seq="-") for k in range(N)]
rc, out = run(h, c10)
assert rc == 1, f"묶음 번호가 없는 로그에 판정했다 — 종료 {rc}"
assert "견줄 판을 고르지 못했다" in out, out
assert "주고받은 모든 묶음에서 세계가 같았다" not in out, out
print("  묶음 번호 없는 로그 → 판정하지 않는다 ✅")

# ── ⑪ 거짓 초록을 만들지 않는다 — 겹치는 표본이 적으면 **판정하지 않는다**
#
#     예전에는 "표본이 적다" 고 경고하면서 일치율은 그대로 냈다. 2026-09-22 에
#     그 몇 개로 "97% 일치" 가 나와 서로 다른 판을 견준 답이 신뢰를 얻었다.
#     그래서 지금은 `MIN_SHARED` 미만이면 수치를 아예 내지 않는다(-685).
h11 = [NAMES] + [line(f"{10+k}.0", "host", "aabbccdd") for k in range(N)]
c11 = [NAMES] \
    + [line(f"{10+k}.0", "client", "aabbccdd") for k in range(2)] \
    + [line(f"{30+k}.0", "client", "aabbccdd") for k in range(8)]
rc, out = run(h11, c11)
assert rc == 1, f"표본이 최소치개 미만인데 판정했다 — 종료 {rc}"
assert "견줄 판을 고르지 못했다" in out, out
assert "주고받은 모든 묶음에서 세계가 같았다" not in out, out
print("  겹치는 표본이 적으면 판정하지 않는다 ✅")

# ── ⑫ 한쪽에만 있는 번호는 **갈림이 아니라 표본 없음**으로 센다
#     신뢰 없는 채널이라 참가자가 표시된 묶음을 놓칠 수 있다. 그것을 갈림으로 세면
#     정상 유실이 빨강이 된다.
h12 = [NAMES] + [line(f"{10+k}.0", "host", "aabbccdd") for k in range(N)]
c12 = [NAMES] + [line(f"{10+k}.0", "client", "aabbccdd") for k in range(N - 1)]
rc, out = run(h12, c12)
assert "한쪽에만 있는 번호" in out, out
assert "갈림이 아니다" in out, out
assert f"일치 {N - 1}개" in out, out
print("  한쪽에만 있는 번호 → 갈림과 구분 ✅")

# ── ⑬ 같은 기계의 로그를 두 번 주면 **판정하지 않는다** (S15P21B202-685)
#
#     예전에는 "역할이 같다" 경고 한 줄만 내고 판정은 그대로 냈다. 2026-09-22 에
#     그게 서로 다른 판을 견주어 97% 라는 틀린 답을 냈다 — **경고를 내고 판정을
#     그대로 내면 아무도 그 경고를 읽지 않는다.**
rc, out = run(h, h)
assert rc == 1, f"같은 로그 두 개에 판정을 냈다 — 종료 {rc}"
assert "견줄 판을 고르지 못했다" in out, out
assert "주고받은 모든 묶음에서 세계가 같았다" not in out, out
print("  같은 로그 두 개 → 판정하지 않는다 ✅")

# ── ⑭ kcp 통계 블록을 읽는다 (가설 ③④ 를 답하는 값)
hk = h + ["kcp SERVER @ time: 12.0", "  connections: 1",
          "  MaxSendRate (avg): 60.00 KB/s", "  SendQueue: 0", "  SendBuffer: 2", ""]
ck = c + ["kcp CLIENT @ time: 12.0", "  MaxSendRate: 60.00 KB/s",
          "  SendQueue: 7", "  SendBuffer: 1", ""]
rc, out = run(hk, ck)
assert "SendQueue 0 ~ 0" in out, out
assert "SendQueue 7 ~ 7" in out, out
assert "60.00 KB/s" in out, out
print("  kcp 통계 파싱 (SendQueue·MaxSendRate) ✅")

print("TEST352 OK")
