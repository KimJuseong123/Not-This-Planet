#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Edgegap 릴레이 핸드셰이크를 Unity 없이 확인한다 (S15P21B202-298 · -279 진단용).

**왜 이 도구가 있나.** 릴레이가 안 되는데 Unity 로만 확인하려면 빌드 두 개·기계 두 대·다른 망이
필요하고, 실패해도 `[RelayWatch] 서버=Checking` 한 줄만 남는다. 그 한 줄로는
**"답이 안 온다"** 와 **"릴레이가 Checking 이라고 답한다"** 를 구분할 수 없다 — 그 구분이 없어서
원인 6개를 헛짚었다. 핸드셰이크는 UDP 9바이트라 그냥 흉내 내면 된다.

    핑:  userId(uint32 LE) + sessionId(uint32 LE) + 0x01        (9바이트)
    답:  messageType(1) + state(1)                              (2바이트)
    state: 0 Disconnected · 1 Checking · 2 Valid · 3 Invalid · 4 SessionTimeout · 5 Error

`EdgegapKcpServer.SendPing()` / `RawReceiveFrom()` 과 같은 형식이다. 다른 망은 **필요 없다** —
릴레이 경로는 양쪽이 밖으로 나가는 연결이라 같은 기계에서도 핸드셰이크가 동일하다.

사용법 (EC2 에서 실행하면 공인 IP 가 고정이라 NAT 변수가 없다)

    export EDGEGAP_RELAY_TOKEN=…            # 또는 --env-file ~/metagame/.env
    python3 scripts/diagnostics/relay_probe.py once            # 서버·클라이언트 30초
    python3 scripts/diagnostics/relay_probe.py soak --seconds 300
    python3 scripts/diagnostics/relay_probe.py formats         # 핑 형식 5종
    python3 scripts/diagnostics/relay_probe.py nodes --rounds 8
    python3 scripts/diagnostics/relay_probe.py raw             # 세션 응답 전문(토큰 제외)

**토큰 값은 절대 출력하지 않는다** — 자릿수와 uint32 범위 여부만 찍는다.
세션은 매번 만들고 끝나면 지운다(`DELETE /v1/relays/sessions/{id}`).
"""
import argparse
import json
import os
import re
import socket
import struct
import sys
import threading
import time
import urllib.error
import urllib.request

BASE = "https://api.edgegap.com"
STATE = {0: "Disconnected", 1: "Checking", 2: "Valid",
         3: "Invalid", 4: "SessionTimeout", 5: "Error"}
UINT32_MAX = 0xFFFFFFFF
SECRETS = {"authorization_token"}
IP_KEYS = ("ip_address", "ip", "user_ip")


# ── Edgegap API ────────────────────────────────────────────────────────────────

def load_token(env_file: str | None) -> str:
    """토큰을 환경변수 또는 .env 에서 읽는다. 대시보드 복사본은 'token ' 접두어가 딸려 온다."""
    raw = os.environ.get("EDGEGAP_RELAY_TOKEN", "")
    if not raw and env_file:
        try:
            with open(env_file, encoding="utf-8") as f:
                for line in f:
                    if line.startswith("EDGEGAP_RELAY_TOKEN="):
                        raw = line.split("=", 1)[1].strip().strip('"').strip("'")
        except OSError as e:
            sys.exit(f"{env_file} 을 읽지 못했다: {e}")
    if not raw:
        sys.exit("EDGEGAP_RELAY_TOKEN 이 없다 — 환경변수나 --env-file 로 넘겨라")
    return "token " + re.sub(r"(?i)^token\s+", "", raw)


class Api:
    def __init__(self, auth: str):
        self.auth = auth

    def call(self, method: str, path: str, body=None):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(BASE + path, data=data, method=method)
        req.add_header("Authorization", self.auth)
        if data:
            req.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(req, timeout=15) as r:
                text = r.read().decode("utf-8", "replace")
                return r.status, (json.loads(text) if text.strip() else {})
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode("utf-8", "replace")[:400]
        except Exception as e:  # noqa: BLE001
            return 0, str(e)


def redact(o):
    """토큰만 자릿수로 바꾼다. 로그에 값이 남지 않게."""
    if isinstance(o, dict):
        return {k: (f"<{len(str(v))}자리>" if k in SECRETS else redact(v)) for k, v in o.items()}
    if isinstance(o, list):
        return [redact(x) for x in o]
    return o


def token_shape(v) -> str:
    if not isinstance(v, int):
        return f"숫자 아님({type(v).__name__})"
    return f"{len(str(v))}자리 · uint32 {'안' if 0 < v <= UINT32_MAX else '**밖(잘림 위험)**'}"


def user_ip(u: dict) -> str:
    for k in IP_KEYS:
        if u.get(k):
            return str(u[k])
    return ""


class Session:
    """만들고 ready 를 기다린 릴레이 세션 한 벌."""

    def __init__(self, api: Api, ips: list[str], me: str):
        self.api, self.sid, self.raw = api, None, None
        st, body = api.call("POST", "/v1/relays/sessions", {"users": [{"ip": i} for i in ips]})
        if st not in (200, 201) or not isinstance(body, dict) or not body.get("session_id"):
            raise RuntimeError(f"세션 생성 실패 HTTP {st}: {str(body)[:200]}")
        self.sid = body["session_id"]
        t0 = time.time()
        for _ in range(40):
            st, b = api.call("GET", f"/v1/relays/sessions/{self.sid}")
            if st == 200 and isinstance(b, dict) and b.get("ready"):
                self.raw, self.ready_secs = b, round(time.time() - t0, 1)
                break
            time.sleep(0.5)
        if self.raw is None:
            self.close()
            raise RuntimeError("세션이 ready 가 되지 않았다")

        relay = self.raw.get("relay") or {}
        ports = relay.get("ports") or {}
        self.ip = relay.get("ip", "")
        self.host = str(relay.get("host", ""))
        self.server_port = int((ports.get("server") or {}).get("port") or 0)
        self.client_port = int((ports.get("client") or {}).get("port") or 0)
        self.session_token = int(self.raw.get("authorization_token") or 0)
        self.users = self.raw.get("session_users") or []
        # **순서에 기대지 않고 IP 로 우리 토큰을 고른다.** 응답 순서가 요청 순서와 같다는 보장이 없다.
        self.my_token = next((int(u.get("authorization_token") or 0)
                              for u in self.users if user_ip(u) == me), 0)
        if not self.my_token and self.users:
            self.my_token = int(self.users[0].get("authorization_token") or 0)
        self.other_token = next((int(u.get("authorization_token") or 0)
                                 for u in self.users if user_ip(u) != me), self.my_token)

    def describe(self) -> None:
        print(f"  세션 {self.sid} · ready {self.ready_secs}초")
        print(f"  relay {self.ip} ({self.host}) · server {self.server_port} · client {self.client_port}")
        print(f"  session 토큰 {token_shape(self.session_token)} · 사람 {len(self.users)}명")
        for i, u in enumerate(self.users):
            print(f"    user[{i}] {user_ip(u) or 'IP 없음'} · {token_shape(u.get('authorization_token'))}")

    def close(self) -> None:
        if self.sid:
            self.api.call("DELETE", f"/v1/relays/sessions/{self.sid}")
            self.sid = None


# ── UDP 핑 ─────────────────────────────────────────────────────────────────────

FORMATS = {
    "mirror": ("Mirror 그대로 LE(user,session)", lambda u, s: struct.pack("<IIB", u, s, 1)),
    "swap": ("순서 바꿈 LE(session,user)", lambda u, s: struct.pack("<IIB", s, u, 1)),
    "be": ("빅엔디언 BE(user,session)", lambda u, s: struct.pack(">IIB", u, s, 1)),
    "be-swap": ("빅엔디언+바꿈 BE(session,user)", lambda u, s: struct.pack(">IIB", s, u, 1)),
    "pad13": ("13바이트 LE+conn=0", lambda u, s: struct.pack("<IIBI", u, s, 1, 0)),
}


class Pinger(threading.Thread):
    """한 포트를 계속 핑하며 상태 변화를 시각과 함께 남긴다.

    **비연결 소켓**을 쓴다(`sendto`/`recvfrom`). Mirror 의 서버는 `Connect()` + `Receive` 라
    다른 출발 포트에서 온 답을 커널이 버리는데, 그 가능성을 가르기 위해서다.
    """

    def __init__(self, ip, port, uid, sid, label, t0, fmt="mirror", verbose=True):
        super().__init__(daemon=True)
        self.ip, self.port, self.uid, self.sid = ip, port, uid, sid
        self.label, self.t0, self.verbose = label, t0, verbose
        self.build = FORMATS[fmt][1]
        self.hist: list[tuple[float, str]] = []
        self.replies, self.foreign, self.last = 0, 0, None
        self.stop = threading.Event()

    def run(self):
        msg = self.build(self.uid & UINT32_MAX, self.sid & UINT32_MAX)
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.settimeout(0.4)
        while not self.stop.is_set():
            try:
                s.sendto(msg, (self.ip, self.port))
                data, src = s.recvfrom(2048)
            except socket.timeout:
                continue
            except OSError:
                break
            if src[0] != self.ip or src[1] != self.port:
                self.foreign += 1      # Mirror 의 연결형 소켓이라면 버려질 답이다
            if len(data) >= 2 and data[0] == 1:
                self.replies += 1
                st = STATE.get(data[1], data[1])
                self.last = st
                if not self.hist or self.hist[-1][1] != st:
                    t = round(time.time() - self.t0, 1)
                    self.hist.append((t, st))
                    if self.verbose:
                        print(f"    {t:7.1f}초 [{self.label}] → {st}")
            time.sleep(0.35)
        s.close()

    def valid_at(self):
        return next((t for t, st in self.hist if st == "Valid"), None)

    def line(self) -> str:
        h = " → ".join(f"{st}@{t}s" for t, st in self.hist) or "무응답"
        extra = f" · 다른 출발지 {self.foreign}회" if self.foreign else ""
        return f"[{self.label}] 답 {self.replies}회{extra} · {h}"


def both_roles(ses: Session, seconds: int, fmt="mirror", verbose=True):
    """실제 매치처럼 서버·클라이언트를 동시에 핑한다."""
    t0 = time.time()
    srv = Pinger(ses.ip, ses.server_port, ses.my_token, ses.session_token, "서버", t0, fmt, verbose)
    cli = Pinger(ses.ip, ses.client_port, ses.other_token, ses.session_token, "클라이언트", t0, fmt, verbose)
    srv.start()
    cli.start()
    return srv, cli, t0


def finish(*pingers):
    for p in pingers:
        p.stop.set()
    for p in pingers:
        p.join(2)


# ── 명령 ───────────────────────────────────────────────────────────────────────

def cmd_once(api, args):
    ses = Session(api, [args.ip, args.other], args.ip)
    ses.describe()
    print(f"\n  {args.seconds}초 동안 서버·클라이언트 동시 핑")
    srv, cli, _ = both_roles(ses, args.seconds)
    time.sleep(args.seconds)
    finish(srv, cli)
    print(f"\n  {srv.line()}\n  {cli.line()}")
    verdict(srv)
    ses.close()


def cmd_soak(api, args):
    ses = Session(api, [args.ip, args.other], args.ip)
    ses.describe()
    print(f"\n  {args.seconds}초 담금 — 15초마다 API 도 되묻는다")
    srv, cli, t0 = both_roles(ses, args.seconds)
    seen = None
    while time.time() - t0 < args.seconds and srv.last != "Valid":
        time.sleep(15)
        st, b = api.call("GET", f"/v1/relays/sessions/{ses.sid}")
        cur = ((b.get("status"), b.get("ready"), b.get("linked"), b.get("error"))
               if isinstance(b, dict) else b)
        if cur != seen:
            print(f"    {time.time()-t0:7.1f}초 [API] {cur}")
            seen = cur
    finish(srv, cli)
    print(f"\n  {srv.line()}\n  {cli.line()}")
    verdict(srv)
    ses.close()


def cmd_formats(api, args):
    """핑 형식을 바꿔 본다. **경우마다 새 세션** — 한 번 Valid 가 되면 세션에 남아 섞인다."""
    print("  핑 형식 5종 · 경우마다 새 세션")
    hit = []
    for key, (label, _b) in FORMATS.items():
        try:
            ses = Session(api, [args.ip, args.other], args.ip)
        except RuntimeError as e:
            print(f"    {label}: {e}")
            continue
        t0 = time.time()
        p = Pinger(ses.ip, ses.server_port, ses.my_token, ses.session_token,
                   "server", t0, key, verbose=False)
        p.start()
        while time.time() - t0 < args.seconds and p.last != "Valid":
            time.sleep(0.3)
        finish(p)
        v = p.valid_at()
        print(f"    {label:<34} {p.line()}")
        if v is not None:
            hit.append(label)
        ses.close()
    print("\n  " + ("통한 형식: " + ", ".join(hit) if hit
                    else "어느 형식도 Valid 에 못 갔다 → 형식 문제가 아니다"))


def cmd_nodes(api, args):
    """릴레이 노드에 따라 갈리는지. 세션을 여러 번 만들어 노드별로 센다."""
    print(f"  세션 {args.rounds}회 · 각 {args.seconds}초")
    agg: dict[str, dict] = {}
    for k in range(1, args.rounds + 1):
        try:
            ses = Session(api, [args.ip, args.other], args.ip)
        except RuntimeError as e:
            print(f"    {k}: {e}")
            continue
        t0 = time.time()
        p = Pinger(ses.ip, ses.server_port, ses.my_token, ses.session_token,
                   "server", t0, verbose=False)
        p.start()
        while time.time() - t0 < args.seconds and p.last != "Valid":
            time.sleep(0.3)
        finish(p)
        ok = p.valid_at() is not None
        a = agg.setdefault(ses.ip, {"host": ses.host[:24], "ok": 0, "no": 0})
        a["ok" if ok else "no"] += 1
        print(f"    {k:<3} {ses.ip:<17} {'**Valid**' if ok else (p.last or '무응답')}")
        ses.close()
    print("\n  노드별")
    for ip, a in sorted(agg.items()):
        print(f"    {ip:<17} {a['host']:<26} Valid {a['ok']} · 실패 {a['no']}")


def cmd_raw(api, args):
    """세션 응답 전문을 본다(토큰 제외). 필드가 바뀌었는지 확인할 때."""
    st, body = api.call("POST", "/v1/relays/sessions", {"users": [{"ip": args.ip}, {"ip": args.other}]})
    print(f"  POST → HTTP {st}")
    print("  " + json.dumps(redact(body), ensure_ascii=False, indent=2).replace("\n", "\n  "))
    if not (isinstance(body, dict) and body.get("session_id")):
        return
    sid = body["session_id"]
    for _ in range(40):
        st, b = api.call("GET", f"/v1/relays/sessions/{sid}")
        if st == 200 and isinstance(b, dict) and b.get("ready"):
            print("\n  ready 된 뒤:")
            print("  " + json.dumps(redact(b), ensure_ascii=False, indent=2).replace("\n", "\n  "))
            break
        time.sleep(0.5)
    api.call("DELETE", f"/v1/relays/sessions/{sid}")


def verdict(srv: Pinger) -> None:
    print("\n  ── 읽는 법 ──")
    if srv.replies == 0:
        print("  답이 0회 → 릴레이까지 UDP 가 못 간다. 방화벽·주소·포트를 본다.")
    elif srv.valid_at() is not None:
        print(f"  Valid @ {srv.valid_at()}초 → 릴레이가 우리를 서버로 인정했다. 릴레이 경로는 정상.")
    else:
        print(f"  답은 {srv.replies}회 오는데 계속 {srv.last} → **릴레이가 답은 하지만 서버를 인정하지 않는다.**")
        print("  Mirror 화면에서는 '무응답' 과 구분되지 않는다(초기값이 Checking 이라서).")
        print("  형식·노드·시간을 이미 지웠다면 Edgegap 쪽 문제다 — S15P21B202-298 기록 참조.")
    if srv.foreign:
        print(f"  다른 출발지에서 온 답 {srv.foreign}회 → Mirror 의 연결형 소켓이라면 버려진다(고칠 거리).")


def main() -> int:
    p = argparse.ArgumentParser(description="Edgegap 릴레이 핸드셰이크 진단")
    p.add_argument("--env-file", default="/home/ubuntu/metagame/.env")
    p.add_argument("--ip", default=os.environ.get("PROBE_IP", ""),
                   help="우리 공인 IP (비우면 조회한다)")
    p.add_argument("--other", default=os.environ.get("OTHER_IP", "1.1.1.1"),
                   help="상대로 등록할 IP. 여기서 보내지는 않는다")
    sub = p.add_subparsers(dest="cmd", required=True)
    for name, fn, secs in (("once", cmd_once, 30), ("soak", cmd_soak, 300),
                           ("formats", cmd_formats, 15), ("nodes", cmd_nodes, 25),
                           ("raw", cmd_raw, 0)):
        s = sub.add_parser(name)
        s.add_argument("--seconds", type=int, default=secs)
        if name == "nodes":
            s.add_argument("--rounds", type=int, default=8)
        s.set_defaults(fn=fn)
    args = p.parse_args()

    api = Api(load_token(args.env_file))
    if not args.ip:
        st, d = api.call("GET", "/v1/ip")
        args.ip = (d or {}).get("public_ip", "") if isinstance(d, dict) else ""
        if not args.ip:
            sys.exit("공인 IP 를 못 알아냈다 — --ip 로 넘겨라")
    print(f"우리 IP={args.ip} · 상대 등록 IP={args.other}\n")
    try:
        args.fn(api, args)
    except RuntimeError as e:
        print(f"  {e}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
