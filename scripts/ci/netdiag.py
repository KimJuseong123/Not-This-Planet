"""호스트와 참가자의 Player.log 를 대조해 세계가 실제로 같았는지 판정한다 (S15P21B202-352).

실행:
    python scripts/ci/netdiag.py <호스트 Player.log> <참가자 Player.log>

**왜 이 도구가 있는가.** 2026-09-08 1차 2인 실측을 화면 오버레이를 읽어 손으로 적었더니
느리고 빠뜨렸다 — kcp 통계 박스는 HUD 에 가려 아예 못 봤고, RTT 는 외부 `ping` 으로
어림했고, FPS 는 빈칸이었다. 무엇보다 **두 화면을 동시에 볼 수 없어** 호스트와 참가자가
같은 상태인지 확인할 방법이 없었다.

그래서 `NetDiagnostics` 가 양쪽에 같은 형식으로 남기고 이 도구가 기계로 견준다.
핵심은 `poses` 체크섬이다 — 같은 시각의 그 값이 같으면 **세계가 같았다는 증거**이고,
다르면 그 시각에 갈렸다는 증거다. 좌표를 눈으로 비교할 필요가 없다.

로그 위치(윈도우):
    %USERPROFILE%\\AppData\\LocalLow\\SpaceFlop\\둥실둥실 우주 대탈출\\Player.log
    (직전 실행은 Player-prev.log)
"""
import io
import re
import sys

DIAG = re.compile(r"\[NETDIAG\]\s+(t=.*)$")
NAMES = re.compile(r"\[NETDIAG\]\s+tracked\((\d+)\)\s*=\s*(.*)$")
KCP = re.compile(r"kcp (SERVER|CLIENT) @ time: ([0-9.]+)")
KCPVAL = re.compile(r"^\s+(\w+)(?:\s*\(avg\))?:\s*(.+?)\s*$")


def rows(path):
    """[NETDIAG] 줄을 dict 목록으로. 파싱 못 한 줄은 조용히 버리지 않고 세어 둔다."""
    out, names, bad = [], None, 0
    with io.open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.rstrip("\r\n")
            m = NAMES.search(line)
            if m:
                names = (int(m.group(1)), m.group(2))
                continue
            m = DIAG.search(line)
            if not m:
                continue
            d = {}
            for tok in m.group(1).split():
                if "=" not in tok:
                    bad += 1
                    continue
                k, v = tok.split("=", 1)
                d[k] = v
            if "t" in d:
                out.append(d)
            else:
                bad += 1
    return out, names, bad


def kcp_rows(path):
    """kcp 자신이 남기는 통계 블록. SendQueue·MaxSendRate 는 여기 있다."""
    out = []
    cur = None
    with io.open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.rstrip("\r\n")
            m = KCP.search(line)
            if m:
                if cur:
                    out.append(cur)
                cur = {"side": m.group(1), "t": float(m.group(2))}
                continue
            if cur is not None:
                m = KCPVAL.match(line)
                if m:
                    cur[m.group(1)] = m.group(2)
                elif line.strip() == "":
                    out.append(cur)
                    cur = None
    if cur:
        out.append(cur)
    return out


def num(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def span(rs, key):
    vals = [num(r.get(key)) for r in rs]
    vals = [v for v in vals if v is not None]
    if not vals:
        return "-"
    vals.sort()
    mid = vals[len(vals) // 2]
    return f"{vals[0]:.1f} ~ {vals[-1]:.1f} (중간 {mid:.1f})"


def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 2
    host_path, client_path = argv

    host, host_names, host_bad = rows(host_path)
    client, client_names, client_bad = rows(client_path)

    print("===== netdiag =====")
    print(f"호스트 로그 : {host_path}")
    print(f"참가자 로그 : {client_path}")
    print(f"[NETDIAG] 줄 : 호스트 {len(host)} · 참가자 {len(client)}"
          + (f"  (파싱 실패 {host_bad}/{client_bad})" if host_bad or client_bad else ""))
    if not host or not client:
        print("\n❌ 한쪽에 진단 줄이 없다. `-netdiag` 인자나 METAGAME_NETDIAG=1 을 주고 "
              "다시 실행할 것. 두 창 모두에 줘야 한다.")
        return 1

    print(f"\n역할 : 호스트 로그={host[-1].get('role')} · 참가자 로그={client[-1].get('role')}")
    if host[-1].get("role") == client[-1].get("role"):
        print("  ⚠ 두 로그의 역할이 같다 — 같은 기계의 로그를 두 번 준 것이 아닌지 확인할 것")

    # ── 나르는 목록이 같은가. 여기가 갈리면 자세가 하나도 적용되지 않는다
    print("\n--- 나르는 목록 ---")
    if host_names and client_names:
        print(f"호스트 tracked({host_names[0]}) : {host_names[1]}")
        print(f"참가자 tracked({client_names[0]}) : {client_names[1]}")
        if host_names[0] != client_names[0]:
            print("  ❌ **개수가 다르다** — 이 상태에서는 WorldSync 가 자세를 하나도 "
                  "적용하지 않는다. 두 창이 같은 빌드인지 먼저 확인할 것")
        elif host_names[1] != client_names[1]:
            print("  ❌ 개수는 같은데 **순서·이름이 다르다** — 남의 자리에 값이 들어간다")
        else:
            print("  ✅ 목록과 순서가 같다")
    else:
        print("  (한쪽에 tracked 목록 줄이 없다)")

    # ── 세계가 실제로 같았는가. 이 도구의 핵심
    print("\n--- 세계 일치 (poses 체크섬) ---")
    by_t = {}
    for r in client:
        by_t[r["t"]] = r
    pairs = [(r, by_t[r["t"]]) for r in host if r["t"] in by_t]
    if not pairs:
        print("  ❌ 같은 t 를 가진 줄이 없다 — 두 로그가 겹치는 구간이 없다")
    else:
        same = [p for p in pairs if p[0].get("poses") == p[1].get("poses")]
        print(f"  겹치는 시각 {len(pairs)}개 중 체크섬 일치 {len(same)}개 "
              f"({100.0 * len(same) / len(pairs):.0f}%)")
        if len(same) == len(pairs):
            print("  ✅ 겹치는 모든 시각에서 세계가 같았다")
        else:
            print("  ❌ 갈린 시각 (처음 10개):")
            shown = 0
            for h, c in pairs:
                if h.get("poses") == c.get("poses"):
                    continue
                print(f"    t={h['t']}  호스트 {h.get('poses')} tracked={h.get('tracked')}"
                      f"  ·  참가자 {c.get('poses')} tracked={c.get('tracked')}")
                shown += 1
                if shown >= 10:
                    break

    # ── 개수가 갈린 항목
    print("\n--- 개수 대조 (겹치는 시각에서 다른 것만) ---")
    keys = ("tracked", "clamps", "pending", "live", "scars", "crew")
    diffs = {k: 0 for k in keys}
    for h, c in pairs:
        for k in keys:
            if h.get(k) != c.get(k):
                diffs[k] += 1
    any_diff = False
    for k in keys:
        if diffs[k]:
            any_diff = True
            ex = next((f"t={h['t']} 호스트={h.get(k)} 참가자={c.get(k)}"
                       for h, c in pairs if h.get(k) != c.get(k)), "")
            print(f"  ❌ {k}: {diffs[k]}/{len(pairs)} 시각에서 다르다   예) {ex}")
    if not any_diff and pairs:
        print("  ✅ 겹치는 모든 시각에서 개수가 같았다")

    # ── 참가자 쪽 지연 값 — 가설 ② 를 직접 답한다
    print("\n--- 참가자 쪽 지연 (예산표 검증) ---")
    print(f"  RTT(ms)          : {span(client, 'rtt')}")
    print(f"  BTM              : {span(client, 'btm')}")
    print(f"  buftime(ms)      : {span(client, 'buftime')}   ← -328 예산표의 보간 버퍼 항")
    print(f"  buffer(스냅샷)   : {span(client, 'buf')}")
    print(f"  FPS 호스트/참가자: {span(host, 'fps')}  /  {span(client, 'fps')}")

    # ── kcp 통계 — 가설 ③④ 를 직접 답한다
    print("\n--- kcp 전송 통계 (가설 ③ 적체 · ④ sendRate 여유) ---")
    for tag, path in (("호스트", host_path), ("참가자", client_path)):
        ks = kcp_rows(path)
        if not ks:
            print(f"  {tag}: 통계 줄이 없다 (statisticsLog 가 안 켜졌거나 kcp 가 아니다)")
            continue
        for side in ("SERVER", "CLIENT"):
            rs = [k for k in ks if k["side"] == side]
            if not rs:
                continue
            def sp(key):
                vs = [num(r.get(key)) for r in rs]
                vs = [v for v in vs if v is not None]
                return f"{min(vs):.0f} ~ {max(vs):.0f}" if vs else "-"
            rates = [r.get("MaxSendRate", "-") for r in rs]
            print(f"  {tag} {side}: SendQueue {sp('SendQueue')} · "
                  f"SendBuffer {sp('SendBuffer')} · MaxSendRate {rates[-1] if rates else '-'} "
                  f"({len(rs)}개 표본)")

    print("\n판정 기준")
    print("  · poses 가 겹치는 시각 전부에서 같으면 **세계가 같았다** — 이게 이 검증의 결론이다")
    print("  · tracked 가 다르면 그 순간 자세가 하나도 적용되지 않는다. 빌드가 같은지 먼저 본다")
    print("  · SendQueue 가 계속 커지면 적체다(가설 ③). 0 근처면 -328 수정이 유효하다")
    print("  · buftime 이 -328 예산표의 항과 맞는지 본다(예측 67ms → 1차 실측 83ms)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
