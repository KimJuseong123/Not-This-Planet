#!/usr/bin/env python3
"""Unity 빌드 로그에서 **시간이 어디로 갔는지** 한 화면으로 뽑는다.

사용: python scripts/ci/build_timing.py <unity-build.log>

**왜 필요한가.** 2026-09-11 첫 실전 릴리스(v1.0.0)가 58분 걸렸다. 그 중
**43분이 셰이더 컴파일**이었고 `Local cache hits 0` 이 533건이었다 — d3d11
셰이더 캐시가 완전히 차가웠다는 뜻이다. 그런데 그 사실을 알아내려면 1,500줄
로그를 파야 했다.

다음 릴리스가 **빨라졌는지 느려졌는지, 왜 그런지**를 잡 로그 끝의 몇 줄로
알 수 있어야 한다. 캐시가 warm 하면 `적중` 이 올라가고 시간이 내려간다.
그것이 안 보이면 캐시가 지워진 것이고, 그때 러너 볼륨을 보러 가면 된다.

읽기만 한다 — 빌드 결과에 영향을 주지 않고, 실패해도 릴리스를 막지 않는다
(`|| true` 로 부른다).
"""
from __future__ import annotations

import collections
import re
import sys

# `Pass ForwardLit (fp, d3d11) finished in 611.72 seconds. Local cache hits 0 (…),
#  remote cache hits 0 (…), compiled 192 variants …`
PASS_RE = re.compile(
    r"Pass (\S+) \(([^)]+)\) finished in ([0-9.]+) seconds"
    r".*?Local cache hits (\d+).*?remote cache hits (\d+)"
    r".*?compiled (\d+) variants")

# 그 밖에 오래 걸리는 것들 — 어느 단계가 큰지 알려면 셰이더 밖도 봐야 한다.
OTHER_RE = re.compile(r"^(.*?)\b([0-9]+\.[0-9]+) seconds")
SKIP_UNDER = 20.0            # 20초 미만은 노이즈다


def mins(sec: float) -> str:
    return f"{sec / 60:.1f}분" if sec >= 60 else f"{sec:.0f}초"


def main(argv: list[str]) -> int:
    if len(argv) != 1:
        print("사용: python scripts/ci/build_timing.py <unity-build.log>")
        return 2
    try:
        with open(argv[0], encoding="utf-8", errors="replace") as f:
            text = f.read()
    except OSError as e:
        print(f"빌드 로그를 읽지 못했다: {type(e).__name__}")
        return 1

    print("=== 빌드 시간이 어디로 갔나 ===")

    # ── 셰이더 ────────────────────────────────────────────────────────────
    agg: dict[tuple[str, str], list] = collections.defaultdict(lambda: [0, 0.0, 0, 0, 0])
    for name, api, sec, local, remote, var in PASS_RE.findall(text):
        row = agg[(name, api)]
        row[0] += 1
        row[1] += float(sec)
        row[2] += int(var)
        row[3] += int(local)
        row[4] += int(remote)

    shader_sec = sum(r[1] for r in agg.values())
    hits = sum(r[3] + r[4] for r in agg.values())
    variants = sum(r[2] for r in agg.values())

    if agg:
        print(f"셰이더 컴파일 합계 {mins(shader_sec)} · 변형 {variants}개 · 캐시 적중 {hits}회")
        # **적중이 0 이면 그것이 곧 원인이다.** 크게 말한다.
        if hits == 0:
            print("  🔴 캐시 적중 0 — 셰이더 캐시가 차갑다. 이 시간은 다음 빌드에서 "
                  "사라져야 한다(러너 볼륨의 METAGAME/Library/ShaderCache 가 남는지 확인)")
        elif variants and hits >= variants:
            print("  🟢 캐시가 warm 하다")
        print(f"  {'패스':24} {'API':12} {'횟수':>4} {'시간':>8} {'변형':>7} {'적중':>6}")
        for (n, a), (c, s, v, lo, re_) in sorted(agg.items(), key=lambda x: -x[1][1])[:6]:
            print(f"  {n:24} {a:12} {c:4} {mins(s):>8} {v:7} {lo + re_:6}")
    else:
        print("셰이더 컴파일 줄을 찾지 못했다 — 로그 형식이 바뀌었을 수 있다")

    # ── 셰이더 밖에서 오래 걸린 것 ────────────────────────────────────────
    others: list[tuple[float, str]] = []
    for line in text.split("\n"):
        if "Pass " in line and "finished in" in line:
            continue                       # 위에서 이미 셌다
        m = OTHER_RE.search(line.strip())
        if not m:
            continue
        sec = float(m.group(2))
        if sec < SKIP_UNDER:
            continue
        others.append((sec, line.strip()[:110]))
    others.sort(reverse=True)
    if others:
        print(f"셰이더 밖에서 20초 넘은 것 {len(others)}건 · 합계 {mins(sum(o[0] for o in others))}")
        for sec, line in others[:5]:
            print(f"  {mins(sec):>8}  {line}")

    print(f"→ 셰이더 {mins(shader_sec)} + 그 밖 {mins(sum(o[0] for o in others))}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
