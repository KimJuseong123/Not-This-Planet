"""모인 이벤트 파일을 **판 한 줄**로 만든다 (S15P21B202-499).

실행:
    python scripts/analytics/sessions.py <폴더|파일…>            요약을 찍는다
    python scripts/analytics/sessions.py <폴더> --json out.json  판 목록을 파일로

`docs/data-pipeline.md` §11 의 **M3 남은 절반**이다 — 파일은 쌓이는데(`-607`) 그것을
판 단위로 보는 수단이 없었다. §10 이 정한 방향("BI 도구를 올리지 않는다 · 질의를
저장소에 둔다")을 그대로 따른다.

**의존성을 늘리지 않는다.** §10 은 DuckDB 로 질의하자고 적었는데, 그러려면 먼저
**질의할 표**가 있어야 한다. 여기서 만드는 JSON·CSV 가 그 표다 — DuckDB 든 엑셀이든
그대로 읽는다. 표준 라이브러리만 쓰므로 CI 에서도, EC2 에서도, 노트북에서도 돈다.

**EC2 는 회수된다**([deploy-selfhosted-ec2-temporary]). 그래서 원본 NDJSON 을 그대로
두고 **읽기만** 한다 — 서버에 새 저장소를 만들지 않는다. 내보내는 것이 곧 백업이다.

## 파일 모양

머리줄은 서버가 붙인다(`RunLogStore`). 본문은 판 하나의 NDJSON 이다.

    # user=55
    # backend=prod
    # build=v1.2.0
    # client-file=20260918-172541.ndjson
    # received=2026-09-18T17:44:26
    # ---
    {"seq":1,"t":0,"type":"session_start","role":"solo","scene":"Stage1_Astra", …}
    {"seq":2,"t":0.1,"type":"phase","name":"cutscene"}
    {"seq":5,"t":225.6,"type":"session_end","result":"abandoned","elapsed":199.4, …}

**머리줄의 `build` 와 본문의 `build` 는 다른 것이다**(`docs/data-pipeline.md` §1).
머리줄은 **올린** 빌드, 본문은 그 판을 **만든** 빌드다. 판을 빌드별로 가를 때는
본문 쪽을 본다 — 여기서는 `build` 가 본문, `uploaded_build` 가 머리줄이다.
"""
import argparse
import io
import json
import os
import sys
from collections import Counter

sys.stdout.reconfigure(encoding="utf-8")

HEADER_MARK = "# ---"

#: 판이 어디까지 갔는지를 한 값으로. `docs/data-pipeline.md` §4 의 `Floors` 그대로다.
#: 인덱스가 곧 층이고, 값이 `FloorState` 의 순서다(`event-schema.json`).
FLOOR_EMPTY = 0


def read_file(path: str) -> dict | None:
    """파일 하나 → 판 한 줄. 읽을 수 없으면 None.

    **한 줄이 깨져도 판을 버리지 않는다.** 전송이 중간에 끊기면 마지막 줄이 반쪽으로
    남는데, 그 판의 앞부분은 멀쩡하다. 깨진 줄 수를 세어 함께 싣는다 — 조용히 버리면
    "왜 판 수가 안 맞지" 로 돌아온다.
    """
    try:
        text = io.open(path, encoding="utf-8", errors="replace").read()
    except OSError as e:
        print(f"[warn] 못 읽음: {path} ({type(e).__name__})", file=sys.stderr)
        return None

    head, _, body = text.partition(HEADER_MARK)
    if not _:
        # 머리줄이 없다 — 서버를 거치지 않은 원본(빌드 폴더의 `events/`)일 수 있다.
        head, body = "", text

    meta = {}
    for line in head.splitlines():
        line = line.strip()
        if not line.startswith("#"):
            continue
        k, sep, v = line.lstrip("# ").partition("=")
        if sep:
            meta[k.strip()] = v.strip()

    events, broken = [], 0
    for line in body.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            events.append(json.loads(line))
        except json.JSONDecodeError:
            broken += 1
    if not events:
        return None

    return summarize(os.path.basename(path), meta, events, broken)


def summarize(name: str, meta: dict, events: list, broken: int) -> dict:
    """판 하나의 요약. **없는 값은 None 으로 둔다** — 0 과 섞지 않는다."""
    first = next((e for e in events if e.get("type") == "session_start"), {})
    last = next((e for e in reversed(events) if e.get("type") == "session_end"), {})
    phases = [e for e in events if e.get("type") == "phase"]

    # `session_end` 가 실을 것을 안 실었으면 마지막 관찰값으로 메운다 — 판이 강제로
    # 끊기면 그 줄이 짧다.
    floors = last.get("floors")
    if floors is None:
        floors = next((e.get("floors") for e in reversed(events)
                       if e.get("type") == "assembly"), None)
    blocked = last.get("blocked")
    if blocked is None:
        blocked = next((e.get("code") for e in reversed(events)
                        if e.get("type") == "blocked"), None)

    seqs = [e["seq"] for e in events if isinstance(e.get("seq"), int)]
    return {
        "file": name,
        "user": meta.get("user"),
        "backend": meta.get("backend") or first.get("backend"),
        # 머리줄은 **올린** 빌드, 본문은 그 판을 **만든** 빌드다(§1).
        "uploaded_build": meta.get("build"),
        "build": first.get("build") or None,
        "session": first.get("session"),
        "role": first.get("role"),
        "scene": first.get("scene"),
        "humans": first.get("humans"),
        "started_at": first.get("at"),
        "result": last.get("result"),
        "cleared": last.get("cleared"),
        "elapsed": last.get("elapsed"),
        "falls": last.get("falls"),
        "floors": floors,
        "blocked": blocked,
        "last_phase": (phases[-1].get("name") if phases else None),
        "last_stage": (phases[-1].get("stage") if phases else None),
        "phase_changes": len(phases),
        "events": len(events),
        # **유실을 숨기지 않는다.** `seq` 는 1부터 이어져야 한다.
        "seq_gaps": (max(seqs) - len(set(seqs)) if seqs else None),
        "broken_lines": broken,
        "ended": bool(last),
    }


def channel_of(row: dict) -> str:
    """이 판이 **어느 채널의 빌드**에서 왔나 (S15P21B202-499).

    릴리스는 `v1.2.0` 처럼 `v` + 숫자로 시작한다(`publish_release.decide_version`).
    QA 는 `qa-<MR>-<sha>`, 새벽 dev 는 `dev-<날짜>-<sha>`(`publish_devbuild`).
    그 밖은 **사람이 자기 자리에서 만든 것**이다 — `BuildStamp` 의 시각·브랜치가 온다.

    이 구분이 필요한 이유는 §11 이 적어 둔 그대로다: 개발 빌드가 배포 서버로 보내면
    **사람 데이터와 팀 테스트가 한 표에 섞인다.**
    """
    b = (row.get("uploaded_build") or "").strip()
    if not b:
        return "(모름)"
    if b.startswith("v") and b[1:2].isdigit():
        return "release"
    if b.startswith("qa-"):
        return "qa"
    if b.startswith("dev-"):
        return "dev"
    return "local"


def floor_reach(floors) -> int | None:
    """몇 층까지 올라갔나. 비어 있지 않은 칸의 수 — "어디서 그만뒀나" 의 한 값."""
    if not isinstance(floors, list):
        return None
    return sum(1 for f in floors if f != FLOOR_EMPTY)


def collect(paths: list) -> list:
    out = []
    for p in paths:
        if os.path.isdir(p):
            for base, _dirs, names in os.walk(p):
                for n in sorted(names):
                    if n.endswith(".ndjson"):
                        row = read_file(os.path.join(base, n))
                        if row:
                            out.append(row)
        elif p.endswith(".ndjson"):
            row = read_file(p)
            if row:
                out.append(row)
    out.sort(key=lambda r: (r.get("started_at") or "", r.get("file") or ""))
    return out


def tally(rows: list, key, label: str) -> None:
    """한 칸의 분포. **값이 없는 것도 센다** — 빼면 합이 안 맞고 그것을 아무도 못 본다."""
    c = Counter(key(r) for r in rows)
    print(f"  {label}")
    for v, n in c.most_common():
        shown = "(없음)" if v is None else v
        print(f"    {n:3d}  {shown}")


def report(rows: list) -> None:
    print()
    print(f"== 판 {len(rows)}건 ==")
    if not rows:
        print("  (없음)")
        return

    # **빌드별로 가른다** (`docs/data-pipeline.md` §10). 지문이 다르면 `floors` 의
    # 숫자가 다른 것을 뜻하므로 한 표에 섞으면 안 된다. 지금 이벤트에는 스키마 지문이
    # 없어 **빌드를 대신 쓴다** — 빌드 하나가 스키마 하나이므로 지금은 같은 뜻이다.
    tally(rows, lambda r: r.get("build"), "만든 빌드")

    # **올린 빌드도 함께 본다.** 문서 §11 이 경고한 섞임이 여기서 드러난다 —
    # *"이벤트는 빌드 채널별로 끄고 켤 수 있어야 합니다(개발·QA 빌드가 배포 서버로
    # 보내면 데이터가 섞입니다)"*. 릴리스 빌드는 `v…` 로 시작하고, 그 밖은 사람이
    # 자기 자리에서 만든 것이다.
    tally(rows, lambda r: channel_of(r), "올린 채널")
    tally(rows, lambda r: r.get("result"), "끝난 이유")
    tally(rows, lambda r: r.get("last_phase"), "마지막 국면")
    tally(rows, lambda r: floor_reach(r.get("floors")), "쌓은 층 수")
    tally(rows, lambda r: r.get("blocked"), "막힌 사유")
    tally(rows, lambda r: r.get("role"), "역할")

    done = [r for r in rows if isinstance(r.get("elapsed"), (int, float))]
    if done:
        secs = sorted(r["elapsed"] for r in done)
        mid = secs[len(secs) // 2]
        print("  길이(초)")
        print(f"    최소 {secs[0]:.0f} · 중앙 {mid:.0f} · 최대 {secs[-1]:.0f}")

    unfinished = [r for r in rows if not r["ended"]]
    if unfinished:
        print(f"  ⚠ 끝 줄이 없는 판 {len(unfinished)}건 — 강제 종료로 마지막 줄을 못 남겼다")
    broken = [r for r in rows if r["broken_lines"]]
    if broken:
        print(f"  ⚠ 깨진 줄이 있는 판 {len(broken)}건 — 전송이 중간에 끊겼을 수 있다")
    gaps = [r for r in rows if r.get("seq_gaps")]
    if gaps:
        print(f"  ⚠ seq 가 비는 판 {len(gaps)}건 — 이벤트가 유실됐다")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="이벤트 파일을 판 단위로 요약한다")
    ap.add_argument("paths", nargs="+", help="폴더 또는 .ndjson 파일")
    ap.add_argument("--json", dest="out", help="판 목록을 이 파일에 JSON 으로 쓴다")
    # **섞인 것을 가려 보는 길** (`docs/data-pipeline.md` §11). 지금 배포 서버에는
    # 팀원이 자기 자리에서 만든 빌드의 판이 함께 들어온다 — 사람 행동을 물을 때는
    # `--channel release` 로 걸러야 답이 뜻을 갖는다.
    ap.add_argument("--channel", choices=("release", "qa", "dev", "local"),
                    help="이 채널의 빌드에서 온 판만 본다")
    args = ap.parse_args(argv)

    rows = collect(args.paths)
    if args.channel:
        before = len(rows)
        rows = [r for r in rows if channel_of(r) == args.channel]
        print(f"채널 {args.channel} 로 걸렀다 — {before}건 중 {len(rows)}건")
    if args.out:
        with io.open(args.out, "w", encoding="utf-8", newline="\n") as f:
            json.dump(rows, f, ensure_ascii=False, indent=2)
        print(f"판 {len(rows)}건 → {args.out}")
    report(rows)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
