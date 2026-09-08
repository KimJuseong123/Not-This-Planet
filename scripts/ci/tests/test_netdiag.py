"""호스트/참가자 로그 대조 분석기 (S15P21B202-352).

실행: python scripts/ci/tests/test_netdiag.py

왜 이 테스트가 있는가 — **분석기가 틀리면 검증이 의미가 없다.** 1차 2인 실측을 화면
오버레이를 읽어 손으로 적었더니 느리고 빠뜨렸고(kcp 박스는 HUD 에 가려 아예 못 봤다),
무엇보다 두 화면을 동시에 볼 수 없어 "같은 상태인가" 를 확인할 방법이 없었다. 그래서
로그 대조로 바꿨는데, 그 판정이 조용히 틀리면 예전보다 나쁘다 — 틀린 초록을 믿게 된다.

그래서 **불일치를 일부러 심어 두고 분석기가 그것을 잡는지** 본다.
"""
import io
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.join(os.path.dirname(HERE), "netdiag.py")


def line(t, role, poses, tracked=6, live=1, **kw):
    d = {"t": t, "role": role, "rtt": "-", "fps": "120", "btm": "-", "buftime": "-",
         "buf": "-", "tracked": tracked, "clamps": 16, "drops": 2, "pending": 2,
         "live": live, "scars": 3, "crew": 2, "poses": poses}
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

# ── ① 완전히 일치하면 초록이라고 말한다
h = [NAMES] + [line(f"{10+k}.0", "host", "aabbccdd") for k in range(4)]
c = [NAMES] + [line(f"{10+k}.0", "client", "aabbccdd") for k in range(4)]
rc, out = run(h, c)
assert rc == 0, rc
assert "겹치는 모든 시각에서 세계가 같았다" in out, out
assert "목록과 순서가 같다" in out, out
assert "겹치는 모든 시각에서 개수가 같았다" in out, out
print("  완전 일치 → 초록 ✅")

# ── ② 체크섬이 한 시각만 갈려도 잡고, **그 시각을 지목**한다
c2 = [NAMES] + [line(f"{10+k}.0", "client", "aabbccdd" if k != 2 else "11223344")
                for k in range(4)]
rc, out = run(h, c2)
assert "갈린 시각" in out, out
assert "t=12.0" in out, "갈린 시각을 지목해야 한다\n" + out
assert "일치 3개" in out, out
assert "겹치는 모든 시각에서 세계가 같았다" not in out, out
print("  체크섬 1회 갈림 → 그 시각을 지목 ✅")

# ── ③ tracked 개수가 다르면 **가장 무거운 경고**를 낸다
#     이 상태에서는 WorldSync 가 자세를 하나도 적용하지 않는다 — 빌드 불일치의 신호다
c3 = ["[NETDIAG] tracked(4) = Part_1 | Part_2 | Part_3 | Part_4"] + \
     [line(f"{10+k}.0", "client", "aabbccdd", tracked=4) for k in range(4)]
rc, out = run(h, c3)
assert "개수가 다르다" in out, out
assert "자세를 하나도 적용하지 않는다" in out, "왜 심각한지 말해야 한다\n" + out
assert "같은 빌드인지" in out, "무엇을 먼저 볼지 알려 줘야 한다\n" + out
print("  tracked 개수 갈림 → 원인 후보까지 안내 ✅")

# ── ④ 개수는 같은데 순서가 다른 것도 잡는다 (개수 검사로는 못 잡는 사고)
c4 = ["[NETDIAG] tracked(6) = Part_2 | Part_1 | Part_3 | Part_4 | Bat | WeldGun"] + \
     [line(f"{10+k}.0", "client", "aabbccdd") for k in range(4)]
rc, out = run(h, c4)
assert "순서·이름이 다르다" in out, out
assert "남의 자리에 값이 들어간다" in out, out
print("  개수 같고 순서 다름 → 잡는다 ✅")

# ── ⑤ 세계 상태 개수 차이를 항목별로 세고 예시를 준다
c5 = [NAMES] + [line(f"{10+k}.0", "client", "aabbccdd", live=0) for k in range(4)]
rc, out = run(h, c5)
assert "live:" in out and "4/4" in out, out
assert "호스트=1 참가자=0" in out, "예시를 줘야 한다\n" + out
print("  세계 상태 개수 차이 → 항목·횟수·예시 ✅")

# ── ⑥ 진단 줄이 없으면 **조용히 초록이라고 하지 않는다**
rc, out = run(h, ["그냥 로그 한 줄"])
assert rc == 1, f"실패로 끝내야 한다 — 종료 {rc}"
assert "진단 줄이 없다" in out, out
assert "netdiag" in out, "무엇을 켜야 하는지 알려 줘야 한다\n" + out
print("  한쪽에 진단 줄 없음 → 종료 1 + 안내 ✅")

# ── ⑦ 겹치는 시각이 없으면 그렇게 말한다 (조용히 0% 로 넘기지 않는다)
c7 = [NAMES] + [line(f"{50+k}.0", "client", "aabbccdd") for k in range(4)]
rc, out = run(h, c7)
assert "겹치는 구간이 없다" in out, out
print("  겹치는 시각 없음 → 그렇게 말한다 ✅")

# ── ⑧ 같은 기계의 로그를 두 번 준 실수를 경고한다
rc, out = run(h, h)
assert "역할이 같다" in out, out
print("  같은 역할 두 개 → 경고 ✅")

# ── ⑨ kcp 통계 블록을 읽는다 (가설 ③④ 를 답하는 값)
hk = h + ["kcp SERVER @ time: 12.0", "  connections: 1",
          "  MaxSendRate (avg): 60.00 KB/s", "  SendQueue: 0", "  SendBuffer: 2", ""]
ck = c + ["kcp CLIENT @ time: 12.0", "  MaxSendRate: 60.00 KB/s",
          "  SendQueue: 7", "  SendBuffer: 1", ""]
rc, out = run(hk, ck)
assert "SendQueue 0 ~ 0" in out, out
assert "SendQueue 7 ~ 7" in out, out
assert "60.00 KB/s" in out, out
print("  kcp 통계 파싱 (SendQueue·MaxSendRate) ✅")

# ── ⑩ 도구 개수가 갈리면 잡는다 (휴대 비트 배열의 길이 검사가 여기 걸린다)
c10 = [NAMES] + [line(f"{10+k}.0", "client", "aabbccdd", tools=1) for k in range(4)]
h10 = [NAMES] + [line(f"{10+k}.0", "host", "aabbccdd", tools=2) for k in range(4)]
rc, out = run(h10, c10)
assert "tools:" in out and "4/4" in out, out
assert "호스트=2 참가자=1" in out, out
print("  도구 개수 갈림 → 잡는다 ✅")

# ── ⑪ 거짓 초록을 만들지 않는다 — 겹치는 표본이 적으면 그렇게 말한다
#     옛 빌드는 t 를 프로세스별 로컬 시간으로 찍어 겹치는 시각이 거의 없다.
#     그 몇 개로 "일치 100%" 가 나오면 무엇도 증명하지 못하면서 초록으로 보인다.
h11 = [NAMES] + [line(f"{10+k}.0", "host", "aabbccdd") for k in range(10)]
c11 = [NAMES] \
    + [line(f"{10+k}.0", "client", "aabbccdd") for k in range(2)] \
    + [line(f"{30+k}.0", "client", "aabbccdd") for k in range(8)]
rc, out = run(h11, c11)
assert "겹치는 시각이 적다" in out, out
assert "공유 시계" in out, "원인 후보를 짚어 줘야 한다: " + out
assert "적은 표본에만 해당" in out, out
print("  겹치는 표본이 적으면 경고 ✅")

print("TEST352 OK")
