#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""METAGAME 릴레이 — 방장과 참가자 사이의 UDP 중계 (S15P21B202-299).

**왜 우리가 만드나.** `-145` 의 원래 결정이 EC2 릴레이였다. 그 자리에 Edgegap 이 들어왔는데
`-298` 에서 그쪽 서버 검증이 우리 세션에서 동작하지 않고 **우리가 고칠 수 없다**는 것을 확정했다.
프로토콜은 우리 리포의 `EdgegapKcpServer.cs`·`EdgegapKcpClient.cs` 에 전부 드러나 있으므로,
같은 형식으로 답하는 중계기를 두면 **Unity 쪽은 한 줄도 바뀌지 않는다.**

**이것은 netcode 가 아니다.** 재전송·순서 보장·혼잡 제어(KCP)는 Mirror 가 양쪽 끝에서 그대로
한다. 이 프로세스는 내용을 모르는 채 바이트를 옮기고, 누가 서버인지만 기억한다.

## 와이어 형식 (모든 정수 리틀엔디언)

    클라이언트 → 릴레이   userId(u32) sessionId(u32) 0x01                      핑
                          userId      sessionId      0x02 payload…             데이터
    릴레이 → 클라이언트   0x01 state(u8)
                          0x02 payload…

    서버 → 릴레이         userId      sessionId      0x01                      핑
                          userId      sessionId      0x02 connId(i32) payload…
    릴레이 → 서버         0x01 state(u8)
                          0x02 connId(i32) payload…

    state: 0 Disconnected · 1 Checking · 2 Valid · 3 Invalid · 4 SessionTimeout · 5 Error

**포트 하나로 된다.** 프로토콜은 역할을 포트로 구분하지만 토큰을 우리가 발급하므로
"이 토큰은 방장" 을 알 수 있다. `MatchTransport` 에 서버·클라이언트 포트를 같은 값으로 주면 된다.

## 세션 등록

제어 API 는 **127.0.0.1 에만** 연다(Spring 이 같은 호스트에서 부른다).

    POST /sessions   {"users": [{"userId": 7, "host": true}, {"userId": 9}]}
      → {"sessionId": "...", "sessionToken": 123, "port": 17778,
         "users": {"7": 111, "9": 222}}          # userId → userToken
    DELETE /sessions/<sessionId>
    GET /sessions                                 # 진단용. 토큰은 자릿수만

## 실행

    python3 scripts/relay/metagame_relay.py --port 17778 --control 17779

`sudo ufw allow 17778/udp` 가 선행이다(SSAFY 안내대로 우리가 직접 연다).
"""
import argparse
import json
import logging
import os
import secrets
import socket
import struct
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PING, DATA = 1, 2
DISCONNECTED, CHECKING, VALID, INVALID, SESSION_TIMEOUT, ERROR = range(6)
HEADER = struct.Struct("<IIB")          # userId, sessionId, type
CONN = struct.Struct("<i")              # connectionId
# 양쪽 다 0.5초마다 핑한다(킵얼라이브). 그보다 넉넉히 잡아 잠깐의 유실로 끊지 않는다.
IDLE_TIMEOUT = float(os.environ.get("RELAY_IDLE_TIMEOUT", "8"))
SWEEP_INTERVAL = 2.0

log = logging.getLogger("relay")


class Session:
    """방 하나. 서버는 한 명, 참가자는 여럿."""

    def __init__(self, sid: str, session_token: int, host_user: int, tokens: dict[int, int]):
        self.sid = sid
        self.session_token = session_token
        self.host_user = host_user
        self.by_token = {tok: uid for uid, tok in tokens.items()}
        self.host_token = tokens[host_user]
        self.server_addr: tuple[str, int] | None = None
        self.server_seen = 0.0
        self.conn_of: dict[tuple[str, int], int] = {}
        self.addr_of: dict[int, tuple[str, int]] = {}
        self.seen: dict[tuple[str, int], float] = {}
        self.next_conn = 1
        self.created = time.time()

    def client_conn(self, addr) -> int:
        """참가자에게 connectionId 를 배정한다. 같은 주소면 같은 번호를 유지한다."""
        cid = self.conn_of.get(addr)
        if cid is None:
            cid = self.next_conn
            self.next_conn += 1
            self.conn_of[addr] = cid
            self.addr_of[cid] = addr
            log.info("[%s] 참가자 붙음 %s:%d → connId=%d", self.sid, addr[0], addr[1], cid)
        return cid

    def drop_client(self, addr) -> None:
        cid = self.conn_of.pop(addr, None)
        self.seen.pop(addr, None)
        if cid is not None:
            self.addr_of.pop(cid, None)
            log.info("[%s] 참가자 나감 connId=%d (%s:%d)", self.sid, cid, addr[0], addr[1])

    def snapshot(self) -> dict:
        return {
            "sessionId": self.sid,
            "hostUserId": self.host_user,
            "sessionTokenDigits": len(str(self.session_token)),
            "serverConnected": self.server_addr is not None,
            "clients": len(self.conn_of),
            "ageSeconds": round(time.time() - self.created, 1),
        }


class Relay:
    def __init__(self, port: int):
        self.port = port
        self.sessions: dict[int, Session] = {}      # sessionToken → Session
        self.by_id: dict[str, Session] = {}         # sessionId → Session
        self.lock = threading.Lock()
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 1 << 20)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 1 << 20)
        self.sock.bind(("0.0.0.0", port))
        self.stats = {"in": 0, "out": 0, "unknown": 0, "dropped": 0}

    # ── 세션 관리 (제어 API 가 부른다) ────────────────────────────────────────

    def open_session(self, users: list[dict]) -> dict:
        """users: [{"userId": n, "host": true?}] · 토큰을 발급하고 세션을 만든다."""
        if not users:
            raise ValueError("users 가 비었다")
        host = next((u["userId"] for u in users if u.get("host")), users[0]["userId"])
        # uint32 안에서 뽑는다. 0 은 쓰지 않는다 — Unity 쪽 IsUsable 이 0 을 '없음' 으로 본다.
        def tok() -> int:
            return secrets.randbelow(0xFFFFFF00) + 1
        tokens = {u["userId"]: tok() for u in users}
        session_token = tok()
        sid = secrets.token_hex(6)
        s = Session(sid, session_token, host, tokens)
        with self.lock:
            self.sessions[session_token] = s
            self.by_id[sid] = s
        log.info("[%s] 세션 열림 · 방장 userId=%s · 참가자 %d명", sid, host, len(users) - 1)
        return {"sessionId": sid, "sessionToken": session_token, "port": self.port,
                "hostUserId": host, "users": {str(u): t for u, t in tokens.items()}}

    def close_session(self, sid: str) -> bool:
        with self.lock:
            s = self.by_id.pop(sid, None)
            if s is None:
                return False
            self.sessions.pop(s.session_token, None)
        log.info("[%s] 세션 닫힘 · %.1f초 유지", sid, time.time() - s.created)
        return True

    def list_sessions(self) -> list[dict]:
        with self.lock:
            return [s.snapshot() for s in self.by_id.values()]

    # ── UDP 중계 ─────────────────────────────────────────────────────────────

    def send(self, addr, payload: bytes) -> None:
        try:
            self.sock.sendto(payload, addr)
            self.stats["out"] += 1
        except OSError as e:
            self.stats["dropped"] += 1
            log.debug("송신 실패 %s: %s", addr, e)

    def reply_state(self, addr, state: int) -> None:
        self.send(addr, bytes((PING, state)))

    def handle(self, data: bytes, addr) -> None:
        self.stats["in"] += 1
        if len(data) < HEADER.size:
            return                                  # 헤더도 안 되는 것은 조용히 버린다
        user_token, session_token, msg_type = HEADER.unpack_from(data, 0)
        body = data[HEADER.size:]

        with self.lock:
            s = self.sessions.get(session_token)
        if s is None:
            self.stats["unknown"] += 1
            # 모르는 세션에는 **Invalid 로 분명히 답한다.** Edgegap 은 이 경우 Checking 을 돌려줘서
            # "답이 없다" 와 구분되지 않았고, 그것 때문에 원인을 여섯 번 헛짚었다(-298).
            self.reply_state(addr, INVALID)
            return
        if user_token not in s.by_token:
            self.reply_state(addr, INVALID)
            return

        now = time.time()
        is_host = user_token == s.host_token
        # **핑이든 데이터든 첫 유효 패킷에 등록한다.** 클라이언트는 kcp 핸드셰이크가 끝난 뒤에야
        # 릴레이 핑을 보내므로(TickOutgoing 의 `if (connected)`), 데이터가 먼저 올 수 있다.
        if is_host:
            if s.server_addr != addr:
                s.server_addr = addr
                log.info("[%s] 방장(서버) 등록 %s:%d", s.sid, addr[0], addr[1])
            s.server_seen = now
        else:
            s.client_conn(addr)
            s.seen[addr] = now

        if msg_type == PING:
            if is_host:
                self.reply_state(addr, VALID)
            else:
                # 서버가 아직 안 붙었으면 Mirror 의 정의대로 SessionTimeout(세션 주인 무응답)
                self.reply_state(addr, VALID if s.server_addr else SESSION_TIMEOUT)
            return

        if msg_type != DATA:
            return

        if is_host:
            # 서버 → 특정 참가자. connId 를 떼고 payload 만 넘긴다.
            if len(body) < CONN.size:
                return
            (cid,) = CONN.unpack_from(body, 0)
            target = s.addr_of.get(cid)
            if target is None:
                self.stats["dropped"] += 1
                return
            self.send(target, bytes((DATA,)) + body[CONN.size:])
        else:
            # 참가자 → 서버. connId 를 끼워 넣는다.
            if s.server_addr is None:
                self.stats["dropped"] += 1
                return
            cid = s.conn_of[addr]
            self.send(s.server_addr, bytes((DATA,)) + CONN.pack(cid) + body)

    def sweep(self) -> None:
        """오래 조용한 참가자·서버를 정리한다. 양쪽 다 0.5초마다 핑하므로 8초면 넉넉하다."""
        while True:
            time.sleep(SWEEP_INTERVAL)
            cut = time.time() - IDLE_TIMEOUT
            with self.lock:
                items = list(self.by_id.values())
            for s in items:
                for addr in [a for a, t in s.seen.items() if t < cut]:
                    s.drop_client(addr)
                if s.server_addr and s.server_seen < cut:
                    log.info("[%s] 방장이 조용해짐 — 서버 등록 해제", s.sid)
                    s.server_addr = None

    def serve(self) -> None:
        log.info("릴레이 UDP %d 대기 · idle timeout %.0f초", self.port, IDLE_TIMEOUT)
        threading.Thread(target=self.sweep, daemon=True).start()
        while True:
            try:
                data, addr = self.sock.recvfrom(2048)
            except OSError as e:
                log.warning("수신 오류: %s", e)
                continue
            try:
                self.handle(data, addr)
            except Exception:            # noqa: BLE001 — 한 패킷이 릴레이를 죽이지 않게
                log.exception("패킷 처리 실패 %s", addr)


# ── 제어 API (127.0.0.1 전용) ─────────────────────────────────────────────────

def control_server(relay: Relay, port: int) -> ThreadingHTTPServer:
    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def _json(self, code: int, obj) -> None:
            body = json.dumps(obj, ensure_ascii=False).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if self.path == "/sessions":
                self._json(200, {"sessions": relay.list_sessions(), "stats": relay.stats})
            elif self.path == "/health":
                self._json(200, {"ok": True, "port": relay.port,
                                 "sessions": len(relay.by_id), "stats": relay.stats})
            else:
                self._json(404, {"error": "not found"})

        def do_POST(self):
            if self.path != "/sessions":
                self._json(404, {"error": "not found"})
                return
            try:
                n = int(self.headers.get("Content-Length", "0"))
                payload = json.loads(self.rfile.read(n) or b"{}")
                self._json(201, relay.open_session(payload.get("users") or []))
            except (ValueError, KeyError, TypeError) as e:
                self._json(400, {"error": str(e)})

        def do_DELETE(self):
            if not self.path.startswith("/sessions/"):
                self._json(404, {"error": "not found"})
                return
            sid = self.path.rsplit("/", 1)[-1]
            self._json(200 if relay.close_session(sid) else 404, {"closed": sid})

        def log_message(self, fmt, *args):
            log.debug("control %s", fmt % args)

    srv = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    log.info("제어 API http://127.0.0.1:%d (외부에 열지 않는다)", port)
    return srv


def main() -> int:
    p = argparse.ArgumentParser(description="METAGAME 릴레이")
    p.add_argument("--port", type=int, default=int(os.environ.get("RELAY_PORT", "17778")))
    p.add_argument("--control", type=int, default=int(os.environ.get("RELAY_CONTROL_PORT", "17779")))
    p.add_argument("--verbose", action="store_true")
    a = p.parse_args()
    logging.basicConfig(level=logging.DEBUG if a.verbose else logging.INFO,
                        format="%(asctime)s %(levelname)-5s %(message)s", datefmt="%H:%M:%S")
    relay = Relay(a.port)
    control_server(relay, a.control)
    try:
        relay.serve()
    except KeyboardInterrupt:
        log.info("종료")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
