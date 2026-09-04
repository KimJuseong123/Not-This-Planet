#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""릴레이가 Mirror 프로토콜대로 동작하는지 확인한다 (S15P21B202-299).

Mirror 의 서버·클라이언트를 **날 UDP 로 흉내 내어** 릴레이만 시험한다. Unity 도 다른 망도
필요 없다. `metagame_relay.py` 를 먼저 띄우고 이 스크립트를 돌린다.

    python3 scripts/relay/metagame_relay.py &            # 다른 창
    python3 scripts/relay/relay_selftest.py

보는 것 (Edgegap 에서 하나하나 막혔던 지점들이다)
  ① 방장 핑 → **Valid** (Edgegap 은 여기서 Checking 에 머물렀다)
  ② 서버가 붙기 전 참가자 핑 → SessionTimeout
  ③ 서버가 붙은 뒤 참가자 핑 → Valid
  ④ 참가자 → 서버 데이터가 **connId 와 함께** 도착
  ⑤ 서버 → 참가자 데이터가 **payload 만** 도착
  ⑥ 참가자 둘이면 connId 가 서로 다르고, 서버가 고른 쪽에만 간다
  ⑦ 모르는 세션·모르는 토큰 → **Invalid** ("답이 없다" 와 구분된다)
  ⑧ 세션을 닫으면 그 뒤 핑은 Invalid
"""
import json
import os
import socket
import struct
import sys
import urllib.error
import urllib.request

HOST = os.environ.get("RELAY_HOST", "127.0.0.1")
PORT = int(os.environ.get("RELAY_PORT", "17778"))
CONTROL = os.environ.get("RELAY_CONTROL", "http://127.0.0.1:17779")
PING, DATA = 1, 2
STATE = {0: "Disconnected", 1: "Checking", 2: "Valid",
         3: "Invalid", 4: "SessionTimeout", 5: "Error"}
HEADER = struct.Struct("<IIB")
CONN = struct.Struct("<i")
fails: list[str] = []


def api(method: str, path: str, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(CONTROL + path, data=data, method=method)
    if data:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=5) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw.strip() else {})
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()[:200]
    except Exception as e:  # noqa: BLE001
        sys.exit(f"제어 API 에 못 붙었다({CONTROL}): {e}\n릴레이를 먼저 띄워라.")


class Peer:
    """Mirror 의 한쪽(서버 또는 참가자)을 흉내 낸다."""

    def __init__(self, token: int, session_token: int, label: str):
        self.token, self.session_token, self.label = token, session_token, label
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.settimeout(1.0)

    def ping(self):
        self.sock.sendto(HEADER.pack(self.token, self.session_token, PING), (HOST, PORT))
        return self.recv_state()

    def send_data(self, payload: bytes, conn_id: int | None = None):
        head = HEADER.pack(self.token, self.session_token, DATA)
        tail = (CONN.pack(conn_id) if conn_id is not None else b"") + payload
        self.sock.sendto(head + tail, (HOST, PORT))

    def recv_state(self):
        try:
            d, _ = self.sock.recvfrom(2048)
        except socket.timeout:
            return None
        return STATE.get(d[1], d[1]) if len(d) >= 2 and d[0] == PING else f"핑 아님({d[:4]!r})"

    def recv_data(self, with_conn: bool):
        """(connId, payload) 또는 (None, payload). 핑 응답은 건너뛴다."""
        for _ in range(12):
            try:
                d, _ = self.sock.recvfrom(2048)
            except socket.timeout:
                return None
            if not d or d[0] != DATA:
                continue                      # 핑 응답이 섞여 온다
            body = d[1:]
            if with_conn:
                if len(body) < CONN.size:
                    return None
                return CONN.unpack_from(body, 0)[0], body[CONN.size:]
            return None, body
        return None

    def close(self):
        self.sock.close()


def check(name: str, got, want) -> None:
    ok = got == want
    print(f"  {'✅' if ok else '❌'} {name}" + ("" if ok else f"  기대 {want!r} · 실제 {got!r}"))
    if not ok:
        fails.append(name)


def main() -> int:
    st, health = api("GET", "/health")
    if st != 200:
        sys.exit(f"릴레이 health 실패: {st} {health}")
    print(f"릴레이 UDP {HOST}:{PORT} · 제어 {CONTROL} · 세션 {health.get('sessions')}개\n")

    st, ses = api("POST", "/sessions", {"users": [{"userId": 7, "host": True},
                                                  {"userId": 9}, {"userId": 11}]})
    if st != 201:
        sys.exit(f"세션 생성 실패: {st} {ses}")
    sid, stok = ses["sessionId"], ses["sessionToken"]
    host_tok = ses["users"]["7"]
    c1_tok, c2_tok = ses["users"]["9"], ses["users"]["11"]
    print(f"세션 {sid} · 방장 userId=7 · 참가자 9·11\n")

    server = Peer(host_tok, stok, "서버")
    c1 = Peer(c1_tok, stok, "참가자9")
    c2 = Peer(c2_tok, stok, "참가자11")

    # ② 서버가 붙기 전 참가자 핑
    check("② 서버 전 참가자 핑 → SessionTimeout", c1.ping(), "SessionTimeout")

    # 앞의 핑으로 c1 이 등록됐다. 서버를 붙인다.
    # ① 방장 핑 → Valid  (Edgegap 은 여기서 Checking 에 머물렀다)
    check("① 방장 핑 → Valid", server.ping(), "Valid")

    # ③ 서버가 붙은 뒤 참가자 핑
    check("③ 서버 후 참가자 핑 → Valid", c1.ping(), "Valid")
    check("③ 두 번째 참가자도 Valid", c2.ping(), "Valid")

    # ④ 참가자 → 서버 (connId 가 붙어 도착해야 한다)
    c1.send_data(b"hello-from-9")
    got = server.recv_data(with_conn=True)
    if got is None:
        check("④ 참가자→서버 전달", None, "(connId, payload)")
        cid1 = None
    else:
        cid1, payload = got
        check("④ 참가자→서버 payload", payload, b"hello-from-9")
        print(f"     connId={cid1}")

    c2.send_data(b"hello-from-11")
    got = server.recv_data(with_conn=True)
    cid2 = got[0] if got else None
    if got:
        check("④ 두 번째 참가자 payload", got[1], b"hello-from-11")

    # ⑥ connId 가 서로 다르다
    check("⑥ connId 가 참가자마다 다르다", cid1 is not None and cid2 is not None and cid1 != cid2, True)

    # ⑤ 서버 → 지정한 참가자에게만 (payload 만 도착)
    if cid1 is not None:
        server.send_data(b"reply-to-9", conn_id=cid1)
        got = c1.recv_data(with_conn=False)
        check("⑤ 서버→참가자9 payload", got[1] if got else None, b"reply-to-9")
        # 다른 참가자에게는 오지 않아야 한다
        c2.sock.settimeout(0.4)
        check("⑥ 다른 참가자에게는 안 간다", c2.recv_data(with_conn=False), None)
        c2.sock.settimeout(1.0)

    # ⑦ 모르는 세션 · 모르는 토큰 → Invalid
    ghost = Peer(host_tok, (stok + 12345) & 0xFFFFFFFF, "유령세션")
    check("⑦ 모르는 세션 → Invalid", ghost.ping(), "Invalid")
    ghost.close()
    wrong = Peer((host_tok + 999) & 0xFFFFFFFF, stok, "틀린토큰")
    check("⑦ 모르는 토큰 → Invalid", wrong.ping(), "Invalid")
    wrong.close()

    # 세션 상태가 제어 API 에 보이는지
    st, listing = api("GET", "/sessions")
    snap = next((x for x in listing.get("sessions", []) if x["sessionId"] == sid), {})
    check("제어 API 가 서버 접속을 안다", snap.get("serverConnected"), True)
    check("제어 API 가 참가자 수를 안다", snap.get("clients"), 2)

    # ⑧ 세션을 닫으면 Invalid
    api("DELETE", f"/sessions/{sid}")
    check("⑧ 닫은 세션 → Invalid", server.ping(), "Invalid")

    for p in (server, c1, c2):
        p.close()

    print("\n════ 결과 ════")
    if fails:
        print(f"  실패 {len(fails)}건: " + ", ".join(fails))
        return 1
    print("  전부 통과 — 릴레이가 Mirror 프로토콜대로 중계한다")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
