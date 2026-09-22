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
    %USERPROFILE%\\AppData\\LocalLow\\Not This Planet\\Not This Planet\\Player.log
    (직전 실행은 Player-prev.log)
"""
import io
import os
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


#: 한 판으로 인정할 최소 겹침(묶음 수). 이보다 적으면 **판정하지 않는다**.
#:
#: 옛 빌드는 묶음 번호를 아예 안 찍었고, 번호는 판마다 0 에서 다시 센다. 그래서
#: 서로 다른 판이 몇 개쯤 우연히 겹치는 일이 생긴다 — 그 몇 개로 "97% 일치" 를
#: 내는 것이 2026-09-22 에 실제로 한 일이다(S15P21B202-685).
MIN_SHARED = 20

#: 같은 판이면 두 로그의 `t` 차이가 **일정해야 한다**(각자 켠 시각만큼 밀려 있다).
#: 판이 섞이면 그 차이가 제멋대로 벌어진다 — 번호가 우연히 겹친 것을 가르는 자다.
MAX_OFFSET_SPREAD = 5.0


def sessions(rs):
    """한 파일에 든 여러 판을 가른다. `t` 가 되감기면 새 판이다.

    한 실행에 솔로 → 방 만들기 → 참가가 이어지므로 **한 판 안에서 역할이 바뀐다.**
    그래서 역할로 가르지 않고 시각으로 가르고, 역할은 아래에서 고른다.
    """
    out, cur, prev = [], [], None
    for r in rs:
        try:
            t = float(r.get("t", ""))
        except ValueError:
            continue
        if prev is not None and t < prev:
            out.append(cur)
            cur = []
        cur.append(r)
        prev = t
    if cur:
        out.append(cur)
    return out


def marks_of(rs):
    """그 판에서 실제로 주고받은 묶음: {번호: 줄}."""
    return {r["seq"]: r for r in rs if r.get("seq", "-") not in ("-", "0")}


def offset_spread(hm, cm, shared):
    """같은 번호끼리 `t` 차이가 얼마나 흔들리는가(초). 못 재면 None."""
    offs = []
    for k in shared:
        try:
            offs.append(float(hm[k]["t"]) - float(cm[k]["t"]))
        except (KeyError, ValueError):
            continue
    if not offs:
        return None
    return max(offs) - min(offs)


def pick_pair(host_rows, client_rows):
    """견줄 **같은 판**을 고른다. `(호스트 줄, 참가자 줄, 설명)` 또는 `(None, None, 사유)`.

    **못 고르면 판정하지 않는다.** 서로 다른 판을 견주면 움직이는 것이 전부 갈려
    보이거나, 반대로 몇 개 겹친 것으로 높은 일치율이 나온다 — 둘 다 거짓이다.
    """
    hs = [s for s in sessions(host_rows) if any(r.get("role") == "host" for r in s)]
    cs = [s for s in sessions(client_rows) if any(r.get("role") == "client" for r in s)]
    if not hs:
        return None, None, "첫 로그에 `role=host` 인 판이 없다 — 호스트 로그를 먼저 주었는지 확인할 것"
    if not cs:
        return None, None, "둘째 로그에 `role=client` 인 판이 없다 — 참가자 로그를 주었는지 확인할 것"

    best = None
    for hi, h in enumerate(hs):
        hsub = [r for r in h if r.get("role") == "host"]
        hm = marks_of(hsub)
        for ci, c in enumerate(cs):
            csub = [r for r in c if r.get("role") == "client"]
            cm = marks_of(csub)
            shared = set(hm) & set(cm)
            if not shared:
                continue
            spread = offset_spread(hm, cm, shared)
            if spread is None or spread > MAX_OFFSET_SPREAD:
                continue
            score = len(shared)
            if best is None or score > best[0]:
                best = (score, hi, ci, hsub, csub, spread)

    if best is None:
        return None, None, (f"호스트 판 {len(hs)}개 · 참가자 판 {len(cs)}개 중 "
                            f"**같은 판으로 볼 수 있는 짝이 없다** — 번호가 겹치지 않거나 "
                            f"시각 차이가 일정하지 않다(한계 {MAX_OFFSET_SPREAD:g}초)")
    score, hi, ci, hsub, csub, spread = best
    if score < MIN_SHARED:
        return None, None, (f"가장 잘 맞는 짝도 겹치는 묶음이 {score}개뿐이다"
                            f"(최소 {MIN_SHARED}개) — 이 표본으로는 판정하지 않는다")
    return hsub, csub, (f"호스트 판 {hi + 1}/{len(hs)} ({len(hsub)}줄) · "
                        f"참가자 판 {ci + 1}/{len(cs)} ({len(csub)}줄) · "
                        f"겹치는 묶음 {score}개 · 시각 차이 흔들림 {spread:.2f}초")


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

    # **없는 파일에 스택 추적을 뱉지 않는다** (S15P21B202-685). 로그 경로를 적어 둔
    # 문서 세 곳이 실제로 틀려 있었다 — 그대로 따라온 사람이 처음 만나는 것이
    # 파이썬 역추적이면 도구가 고장난 줄로 읽는다.
    for label, path in (('호스트', host_path), ('참가자', client_path)):
        if not os.path.isfile(path):
            print(f"❌ {label} 로그를 찾지 못했다: {path}")
            print('   윈도우 기본 위치: %USERPROFILE%\\AppData\\LocalLow\\Not This Planet\\Not This Planet\\Player.log')
            print('   (폴더 이름에 띄어쓰기가 있다. 개발 빌드는 빌드 폴더의 logs/ 에도 남는다)')
            return 2
    verdict = []          # 비어 있으면 통과. 채워지면 종료 코드 1
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

    # ── 견줄 **같은 판**을 고른다 (S15P21B202-685)
    #
    # 한 파일에는 한 실행의 여러 판이 들어 있다 — 솔로로 둘러보다 방을 만들고, 나가서
    # 남의 방에 들어간다. 예전에는 `host[-1]` 로 **마지막 줄의 역할**만 보고 전부를
    # 통째로 견줬다. 2026-09-22 첫 실전 QA 에서 그게 서로 다른 판을 견주어
    # `97% 일치 · 갈림 5개` 를 냈고, 같은 판만 골라 다시 재니 `126개 전부 일치` 였다.
    #
    # ⚠ 그때도 "역할이 같다" 경고 한 줄은 나갔다. **경고를 내고 판정을 그대로 내면
    # 아무도 그 경고를 읽지 않는다** — 그래서 지금은 짝을 못 고르면 판정하지 않는다.
    host, client, why = pick_pair(host, client)
    if host is None:
        print(f"\n❌ 견줄 판을 고르지 못했다 — {why}")
        print("   두 로그가 같은 판을 담고 있는지 확인할 것. "
              "**틀린 답을 내지 않으려고 여기서 멈춘다.**")
        return 1
    print(f"\n견줄 판 : {why}")

    # ── 나르는 목록이 같은가. 여기가 갈리면 자세가 하나도 적용되지 않는다
    print("\n--- 나르는 목록 ---")
    if host_names and client_names:
        print(f"호스트 tracked({host_names[0]}) : {host_names[1]}")
        print(f"참가자 tracked({client_names[0]}) : {client_names[1]}")
        # **여기도 판정에 담는다** (`!578` AI 리뷰). 담지 않으면 자세가 하나도
        # 적용되지 않는 상태에서도 마지막 줄이 `✅ 판정` 에 종료 0 이 된다 —
        # 이 파일이 없애려던 "경고만 내고 판정은 초록" 이 이 경로에 남아 있었고,
        # 새로 넣은 최종 판정 줄이 그것을 **더 단정적으로** 만들었다.
        if host_names[0] != client_names[0]:
            verdict.append("나르는 목록의 개수가 다르다")
            print("  ❌ **개수가 다르다** — 이 상태에서는 WorldSync 가 자세를 하나도 "
                  "적용하지 않는다. 두 창이 같은 빌드인지 먼저 확인할 것")
        elif host_names[1] != client_names[1]:
            verdict.append("나르는 목록의 순서·이름이 다르다")
            print("  ❌ 개수는 같은데 **순서·이름이 다르다** — 남의 자리에 값이 들어간다")
        else:
            print("  ✅ 목록과 순서가 같다")
    else:
        print("  (한쪽에 tracked 목록 줄이 없다)")

    # ── 세계가 실제로 같았는가. 이 도구의 핵심
    #
    # **번호로 짝짓는다, 시각이 아니라.** 참가자 쪽 물체는 받은 자세로 보간해 가는
    # 중이라 호스트의 실시간 자세보다 늘 뒤에 있다(초당 2m 로 끌리는 파츠에서 10cm).
    # 시각으로 짝지어 좌표를 견주면 **움직이는 것이 항상 갈려 보인다** — 거짓 빨강이다.
    # 그래서 양쪽이 같은 규칙으로 접어 둔 **실제 주고받은 묶음**을 번호로 짝짓는다.
    print("\n--- 세계 일치 (묶음 체크섬 · seq 로 짝짓는다) ---")
    host_marks = {r["seq"]: r for r in host if r.get("seq", "-") not in ("-", "0")}
    client_marks = {r["seq"]: r for r in client if r.get("seq", "-") not in ("-", "0")}
    shared = sorted(set(host_marks) & set(client_marks), key=lambda v: int(v))

    if not host_marks or not client_marks:
        verdict.append("주고받은 묶음을 짝지을 수 없다")
        print("  ❌ 한쪽에 묶음 번호가 없다 — 자세를 주고받지 못했다("
              "호스트가 안 보냈거나 참가자가 하나도 받지 못했다)")
    elif not shared:
        verdict.append("주고받은 묶음을 짝지을 수 없다")
        print(f"  ❌ 같은 번호의 묶음이 없다 — 호스트 {len(host_marks)}개 · "
              f"참가자 {len(client_marks)}개인데 겹치는 번호가 0이다. "
              "두 로그가 같은 경기인지, 같은 빌드인지 확인할 것")
    else:
        same = [k for k in shared if host_marks[k].get("poses") == client_marks[k].get("poses")]
        print(f"  겹치는 묶음 {len(shared)}개 중 체크섬 일치 {len(same)}개 "
              f"({100.0 * len(same) / len(shared):.0f}%)")

        if len(same) == len(shared):
            print("  ✅ 주고받은 모든 묶음에서 세계가 같았다")
        else:
            # **종료 코드로도 말한다** (S15P21B202-685). 갈림을 찍고 0 으로 끝나면
            # 자동화에 걸 수 없고, 사람이 출력을 훑다 놓치면 통과로 읽힌다.
            verdict.append("체크섬이 갈렸다")
            print("  ❌ 갈린 묶음 (처음 10개):")
            shown = 0
            for k in shared:
                if host_marks[k].get("poses") == client_marks[k].get("poses"):
                    continue
                h, c = host_marks[k], client_marks[k]
                print(f"    seq={k}  호스트 {h.get('poses')} (t={h.get('t')})"
                      f"  ·  참가자 {c.get('poses')} (t={c.get('t')})")
                shown += 1
                if shown >= 10:
                    break

        # 표본이 너무 적으면 **판정 자체를 믿을 수 없다**. 옛 빌드는 묶음 번호를
        # 아예 안 찍었고, 다른 경기의 로그를 섞으면 겹치는 번호가 몇 개뿐이다.
        # 그 몇 개로 "일치 100%" 가 나와도 아무것도 증명하지 못한다.
        smaller = min(len(host_marks), len(client_marks))
        if smaller and len(shared) < smaller * 0.5:
            print(f"  ⚠ 겹치는 묶음이 적다 — {len(shared)}개 / 적은 쪽 {smaller}개. "
                  "두 로그가 같은 경기·같은 빌드인지 확인할 것. "
                  "위 판정은 이 적은 표본에만 해당한다")

        # 한쪽에만 있는 번호는 **갈린 것이 아니라 표본이 없는 것**이다. 신뢰 없는
        # 채널이라 참가자가 그 묶음을 놓치면 다음 표시까지 기다린다 — 흔하지 않지만
        # 일어난다. 갈림과 섞어 세면 판정이 흐려지므로 따로 적는다.
        only_host = len(set(host_marks) - set(client_marks))
        only_client = len(set(client_marks) - set(host_marks))
        if only_host or only_client:
            print(f"  (한쪽에만 있는 번호 — 호스트 {only_host}개 · 참가자 {only_client}개. "
                  "유실이나 기록 시점 차이다. 갈림이 아니다)")

    # ── 개수가 갈린 항목. **여기는 시각으로 짝짓는다** — 개수는 순간값이고,
    #    한쪽이 한 틱 늦게 반영하는 것은 정상이므로 번호로 묶을 이유가 없다.
    by_t = {}
    for r in client:
        by_t[r["t"]] = r
    pairs = [(r, by_t[r["t"]]) for r in host if r["t"] in by_t]

    print("\n--- 개수 대조 (겹치는 시각에서 다른 것만) ---")
    if not pairs:
        print("  (겹치는 시각이 없다 — 두 창이 같은 빌드인지, 공유 시계로 t 를 찍는지 확인할 것)")
    # **±1 은 갈림이 아니라 지연이다** (S15P21B202-685 · `-654` Q0).
    #
    # 참가자는 ~100ms 늦게 받으므로 그 순간 날아가는 운석 하나가 엇갈린다. 그것을
    # ❌ 로 찍어 온 탓에 2026-09-22 QA 에서 "개수가 갈렸다" 로 읽혔는데, 같은 판의
    # 체크섬은 121개 전부 일치였다. **사양으로 정한 값을 빨간불로 내면 사람이 빨간불
    # 전체를 무시한다.** 그래서 ±1 은 ⚠(지연)으로 적고, 2 이상만 ❌(갈림)으로 센다.
    keys = ("tracked", "clamps", "tools", "pending", "live", "scars", "crew")
    LAG_TOLERANCE = 1
    lag = {k: 0 for k in keys}
    gap = {k: 0 for k in keys}

    def delta(h, c, k):
        """|호스트 − 참가자|. 숫자로 못 읽으면 None(같지 않기만 하면 갈림으로 본다)."""
        try:
            return abs(int(h.get(k, "")) - int(c.get(k, "")))
        except ValueError:
            return None

    for h, c in pairs:
        for k in keys:
            if h.get(k) == c.get(k):
                continue
            d = delta(h, c, k)
            if d is not None and d <= LAG_TOLERANCE:
                lag[k] += 1
            else:
                gap[k] += 1

    any_diff = False
    for k in keys:
        if gap[k]:
            any_diff = True
            # **개수로는 판정하지 않는다** — 판정은 체크섬이 한다(아래 판정 기준).
            # 공세 묶음이 한 번에 여섯 개씩 나오는 순간에는 참가자가 둘셋 뒤처지는
            # 것이 정상이다(2026-09-22 실측: t=93 에 호스트 10 · 참가자 13, 그런데
            # 같은 판의 체크섬은 121개 전부 일치였다). 그것을 빨간불로 내면
            # **사양대로 도는 판이 빨갛게 나온다.**
            ex = next((f"t={h['t']} 호스트={h.get(k)} 참가자={c.get(k)}"
                       for h, c in pairs
                       if h.get(k) != c.get(k)
                       and (delta(h, c, k) is None or delta(h, c, k) > LAG_TOLERANCE)), "")
            print(f"  ⚠ {k}: {gap[k]}/{len(pairs)} 시각에서 {LAG_TOLERANCE} 넘게 다르다"
                  f"   예) {ex} — 스폰이 몰린 순간이면 정상이다")
        if lag[k]:
            any_diff = True
            # **예시를 함께 준다.** 사람이 로그를 열지 않고도 무엇이 얼마나
            # 엇갈렸는지 알아야 이 줄이 쓸모가 있다.
            exl = next((f"t={h['t']} 호스트={h.get(k)} 참가자={c.get(k)}"
                        for h, c in pairs
                        if h.get(k) != c.get(k)
                        and delta(h, c, k) is not None
                        and delta(h, c, k) <= LAG_TOLERANCE), "")
            print(f"  ⚠ {k}: {lag[k]}/{len(pairs)} 시각에서 ±{LAG_TOLERANCE}"
                  f"   예) {exl} — 참가자가 늦게 받는 것이다(사양, -654 Q0)")
    if not any_diff and pairs:
        print("  ✅ 겹치는 모든 시각에서 개수가 같았다")
    if any_diff:
        print("  (개수는 참고다 — 갈렸는지는 위의 체크섬이 판정한다)")

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
    print("  · 같은 seq 의 poses 가 전부 같으면 **세계가 같았다** — 이게 이 검증의 결론이다")
    print("  · 나르는 목록의 순서까지 같아야 한다 — 순서가 갈리면 남의 자리에 값이 들어간다")
    print("  · tracked 가 다르면 그 순간 자세가 하나도 적용되지 않는다. 빌드가 같은지 먼저 본다")
    print("  · SendQueue 가 계속 커지면 적체다(가설 ③). 0 근처면 -328 수정이 유효하다")
    print("  · buftime 이 -328 예산표의 항과 맞는지 본다(예측 67ms → 1차 실측 83ms)")

    if verdict:
        print(f"\n❌ 판정: " + " · ".join(verdict))
        return 1
    print("\n✅ 판정: 주고받은 모든 묶음에서 세계가 같았다")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
