"""씬·프리팹에 서버 주소가 박혀 있지 않은가 (S15P21B202-584).

실행: python scripts/ci/tests/test_scene_backend_urls.py

**무엇을 막는 검사인가.** 주소가 두 곳에 있으면 그 둘은 언젠가 어긋나고, 어긋난 날
**한 실행이 두 환경을 본다.**

2026-09-17 실측: `qa-490-99c0fb28`(개발 빌드)로 3인 테스트를 했는데 입장이 안 됐다.
클라이언트 로그는 `[백엔드] …/dev (개발)` 로 **옳게** 찍혔는데 nginx 접근 로그는

    POST /auth/guest       → 배포     ← 방이 배포 쪽 메모리에 생긴다
    POST /dev/auth/verify  → 개발 401  ← 그 방이 없다

였다. `Lobby.unity` 에 `defaultServerAddress: j15b202.p.ssafy.io` 가 저장돼 있었고,
그 값이 `NetworkSession.ResolveServerAddress` 에서 **빌드 채널보다 먼저** 읽혔다.

`-284` 가 `httpBaseUrl`·`webSocketUrl` 을 코드에서 없앴고 `-573` 이 상수를 없앴지만,
**씬에 저장된 값**은 둘 다 살아남아 같은 일을 계속했다. 그래서 코드가 아니라
**씬 파일**을 본다 — 필드를 없애도 YAML 은 남기 때문이다.

주소는 `Metagame.Core.BackendEndpoints` 한 곳에서만 나온다. 다른 서버를 봐야 하면
실행 인자다: `-backend=https://…` · `-metagame-server=127.0.0.1:8080`.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
os.chdir(ROOT)
sys.stdout.reconfigure(encoding="utf-8")

ASSETS = os.path.join(ROOT, "METAGAME", "Assets")

# **씬과 프리팹만 본다.** `.asset` 에는 Unity 가 딸려 보낸 URP 안내문(`Readme.asset`)이
# 있고 그것은 사람이 읽는 링크다 — 게임이 접속하는 주소가 아니다.
SUFFIXES = (".unity", ".prefab")

# 직렬화된 값 한 줄: `  어떤필드: 값`. 값이 주소처럼 보이면 잡는다.
FIELD = re.compile(r"^\s+([A-Za-z_][A-Za-z0-9_]*):\s*(\S.*)$")

# 주소처럼 보이는 것. 좁게 적는다 — 넓히면 멀쩡한 문자열이 걸려 검사가 꺼진다.
LOOKS_LIKE = (
    re.compile(r"\bssafy\.io\b"),                 # 우리 백엔드 호스트
    re.compile(r"\b(?:https?|wss?)://"),          # 완성된 URL
    re.compile(r"\b127\.0\.0\.1\b"),              # 로컬 백엔드 (Main.unity 에 있었다)
    re.compile(r"\b(?:192\.168|10)\.\d+\.\d+\b"), # LAN 주소 — 남의 PC 를 가리킨 채 배포된다
)

fails = []
passes = 0


def check(ok, msg):
    global passes
    print(("  ✅ " if ok else "  ❌ ") + msg)
    if ok:
        passes += 1
    else:
        fails.append(msg)


def scan(path: str) -> list[str]:
    hits = []
    with open(path, encoding="utf-8", errors="replace") as f:
        for n, line in enumerate(f, 1):
            m = FIELD.match(line.rstrip("\r\n"))
            if not m:
                continue
            name, value = m.group(1), m.group(2)
            if any(p.search(value) for p in LOOKS_LIKE):
                hits.append(f"{os.path.relpath(path, ROOT)}:{n}  {name}: {value[:60]}")
    return hits


print()
print("== 씬·프리팹에 박힌 주소 ==")

files = []
for base, _, names in os.walk(ASSETS):
    for name in names:
        if name.endswith(SUFFIXES):
            files.append(os.path.join(base, name))
files.sort()

check(len(files) > 0, f"씬·프리팹을 찾았다 ({len(files)}개)")

found = []
for path in files:
    found += scan(path)

check(not found, "박힌 주소가 없다 — 주소는 BackendEndpoints 한 곳에서만 나온다")
for h in found:
    print("     " + h)

print()
print("== 검사 자신이 도는가 ==")

# **잡을 수 있는 검사인지 확인한다.** 정규식을 잘못 좁히면 아무것도 못 잡으면서
# 통과하고, 그러면 이 파일은 있으나 마나다. `-584` 가 실제로 지운 네 줄을 넣어 본다.
SAMPLES = [
    "  defaultServerAddress: j15b202.p.ssafy.io",
    "  httpBaseUrl: https://j15b202.p.ssafy.io:443",
    "  webSocketUrl: ws://127.0.0.1:8080/ws/game",
    "  address: 192.168.0.15",
]
for s in SAMPLES:
    m = FIELD.match(s)
    hit = bool(m) and any(p.search(m.group(2)) for p in LOOKS_LIKE)
    check(hit, f"되돌리면 잡는다: {s.strip()[:46]}")

# 멀쩡한 값은 잡지 않는다 — 여기서 넓으면 사람이 검사를 꺼 버린다.
for s in ["  defaultNickname: \"\\uBE44\\uD589\\uC0AC\"",
          "  m_Script: {fileID: 11500000, guid: 6a5b4c, type: 3}",
          "  serverPort: 17777",
          "  m_Name: Lobby"]:
    m = FIELD.match(s)
    hit = bool(m) and any(p.search(m.group(2)) for p in LOOKS_LIKE)
    check(not hit, f"멀쩡한 값은 잡지 않는다: {s.strip()[:46]}")

print()
print(f"== 씬 주소: 통과 {passes} · 실패 {len(fails)} ==")
if fails:
    print("   ❌ " + " / ".join(fails))
raise SystemExit(1 if fails else 0)
