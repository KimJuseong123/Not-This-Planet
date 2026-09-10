#!/usr/bin/env python3
"""`[PERF]` 줄을 읽어 **인원수별로 프레임이 어디서 꺾이는지** 표로 만든다 (S15P21B202-376).

사용법:
    python scripts/ci/perflog.py <Player.log> [<Player.log> ...]

왜 필요한가. 2026-09-10 4인 QA 에서 "여러 명이 같이 뭔가 하면 프레임이 떨어진다"
(S15P21B202-374)가 나왔는데 재는 수단이 없었다. `PerfLog` 가 남긴 줄을 사람이 눈으로
훑으면 수백 줄이라 경향이 안 보인다 — 인원수로 묶어야 "몇 명부터 꺾이나" 가 나온다.

**평균만 보지 않는다.** 끊기는 것은 최저와 95백분위에 나타난다. 평균 60fps 인데
95백분위가 100ms 면 그것이 체감되는 그 현상이다.

파이썬 표준 라이브러리만 쓴다(팀 규칙 — 러너 이미지에 pip 없이 돌고 오프라인 테스트가
어디서나 돈다).
"""
import io
import os
import re
import sys

# PerfLog.Line 이 만드는 형식. 그쪽이 바뀌면 이 정규식도 바뀌어야 하고,
# METAGAME 쪽 PerfLogTests 가 그 형식을 지킨다.
LINE = re.compile(
    r"\[PERF\]\s+t=(?P<t>[\d.]+)\s+fps=(?P<fps>[\d.]+)\s+min=(?P<min>[\d.]+)\s+"
    r"p95=(?P<p95>[\d.]+)\s+frames=(?P<frames>\d+)\s+players=(?P<players>\d+)\s+"
    r"props=(?P<props>\d+)")

HEAD = re.compile(r"\[PERF\]\s+(시작|값)\s+(?P<rest>.*)")
BUILD = re.compile(r"\[빌드\]\s+(?P<rest>.*)")


def samples(text):
    """`[PERF]` 표본을 dict 목록으로. 못 읽는 줄은 조용히 건너뛴다 —
    로그에는 스택 트레이스가 섞이므로 엄격하게 굴면 아무것도 못 읽는다."""
    out = []
    for m in LINE.finditer(text):
        d = m.groupdict()
        out.append({
            "t": float(d["t"]), "fps": float(d["fps"]), "min": float(d["min"]),
            "p95": float(d["p95"]), "frames": int(d["frames"]),
            "players": int(d["players"]), "props": int(d["props"]),
        })
    return out


def context(text):
    """빌드 표시와 `[PERF] 시작·값` 줄. **무엇을 재고 있었는지**가 여기 있다."""
    lines = []
    for m in BUILD.finditer(text):
        s = " ".join(m.group("rest").split())
        if s and s not in lines:
            lines.append(s)
    for m in HEAD.finditer(text):
        s = " ".join(m.group("rest").split())
        if s and s not in lines:
            lines.append(s)
    return lines


def by_players(rows):
    """인원수 → 표본 목록. 이것이 이 도구의 본론이다."""
    groups = {}
    for r in rows:
        groups.setdefault(r["players"], []).append(r)
    return dict(sorted(groups.items()))


def worst(rows, key):
    return min(r[key] for r in rows) if rows else 0.0


def mean(rows, key):
    return sum(r[key] for r in rows) / len(rows) if rows else 0.0


def report(path, out=sys.stdout):
    text = io.open(path, encoding="utf-8", errors="replace").read()
    rows = samples(text)

    print(f"\n=== {os.path.basename(path)} ===", file=out)
    for line in context(text):
        print(f"  {line}", file=out)

    if not rows:
        # 조용히 0건으로 끝내지 않는다 — 왜 없는지가 곧 다음 할 일이다.
        print("  [PERF] 표본이 없다. 실험 라벨이 박힌 빌드인지 확인하라 "
              "(SPACE_EXPERIMENT 없이 빌드하면 이 로그는 남지 않는다).", file=out)
        return rows

    print(f"  표본 {len(rows)}건 · {rows[0]['t']:.0f}~{rows[-1]['t']:.0f}초", file=out)
    print("\n  인원 | 표본 | 평균fps | 최저fps | 최악 p95(ms) | 최대 소품", file=out)
    print("  -----|------|---------|---------|--------------|---------", file=out)
    for players, group in by_players(rows).items():
        print(f"  {players:>4} | {len(group):>4} | {mean(group, 'fps'):>7.1f} | "
              f"{worst(group, 'min'):>7.1f} | {max(r['p95'] for r in group):>12.1f} | "
              f"{max(r['props'] for r in group):>8}", file=out)

    # 가장 나쁜 순간을 따로 짚는다 — 표는 경향을 보여 주고 이 줄은 재현 지점을 준다.
    low = min(rows, key=lambda r: r["min"])
    print(f"\n  가장 끊긴 순간: t={low['t']:.0f}초 · 최저 {low['min']:.1f}fps · "
          f"p95 {low['p95']:.1f}ms · {low['players']}명 · 소품 {low['props']}개", file=out)
    return rows


def main(argv):
    if len(argv) < 2:
        print(__doc__.strip())
        return 2

    total = 0
    for path in argv[1:]:
        if not os.path.exists(path):
            print(f"파일이 없다: {path}")
            continue
        total += len(report(path))

    if total == 0:
        print("\n어느 로그에도 [PERF] 표본이 없다.")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
