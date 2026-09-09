package com.metagame.backend.websocket;

import com.metagame.backend.domain.room.Room;
import com.metagame.backend.domain.room.RoomManager;
import com.metagame.backend.domain.user.UserRepository;
import java.io.IOException;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;
import org.springframework.web.socket.CloseStatus;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketSession;
import org.springframework.web.socket.handler.ConcurrentWebSocketSessionDecorator;
import org.springframework.web.socket.handler.WebSocketSessionDecorator;
import org.springframework.web.socket.handler.TextWebSocketHandler;
import tools.jackson.databind.JsonNode;
import tools.jackson.databind.ObjectMapper;

@Slf4j
@Component
@RequiredArgsConstructor
public class GameWebSocketHandler extends TextWebSocketHandler {

    private static final String ROOM_ID_ATTR = "roomId";
    private static final String NICKNAME_ATTR = "nickname";

    /**
     * 이 사람이 알려 준 자기 랜 주소(예 192.168.31.66).
     *
     * <p>공인 IP 만으로는 같은 망인지 알 수 없다. 규모가 큰 망은 공인 IP 를 여러 개
     * 돌려쓰기 때문에, 같은 WiFi 에 앉아 있어도 서로 다른 공인 IP 로 보인다
     * (2026-09-02 SSAFY 교육장 실측 — 같은 WiFi 인데 릴레이를 탔다).
     */
    private static final String LOCAL_IP_ATTR = "localIp";
    private static final String UNKNOWN_NICKNAME = "비행사";

    private final RoomManager roomManager;
    private final ReconnectGraceManager graceManager;
    private final UserRepository userRepository;
    private final ObjectMapper objectMapper;
    private final RelayProvider relayService;

    // userId → 세션. 브로드캐스트 대상 조회용.
    // **여기 담기는 것은 동시 쓰기를 직렬화하도록 감싼 세션이다**(S15P21B202-313 ③).
    private final Map<Long, WebSocketSession> sessions = new ConcurrentHashMap<>();

    /** 한 세션의 쓰기가 이만큼 밀리면 그 세션을 닫는다. 느린 클라이언트가 방 전체를 붙잡지 않게. */
    private static final int SEND_TIME_LIMIT_MS = 5_000;
    /** 밀린 메시지 버퍼 상한. 우리 메시지는 작아서 이 정도면 한참 밀려도 여유가 있다. */
    private static final int SEND_BUFFER_LIMIT_BYTES = 256 * 1024;
    /**
     * roomId → 그 매치의 접속 정보 (S15P21B202-150).
     *
     * <p>예전에는 릴레이 <b>세션 id 만</b> 담았다(방이 사라질 때 닫으려고 — -299).
     * 그래서 재입장한 사람에게 다시 줄 것이 남아 있지 않았다. 방장 주소와 릴레이 세션을
     * 함께 들고 있으면 {@code GAME_STARTED} 를 그 사람 몫으로 다시 만들 수 있다.
     */
    private final Map<String, MatchEntry> matches = new ConcurrentHashMap<>();

    /**
     * 한 매치에 붙는 데 필요한 것. {@code relay} 는 같은 망이면 null 이다(직접 접속).
     *
     * @param hostAddress 방장이 알려 준 접속 주소. 서버가 보는 공인 IP 로는 안 된다(-263)
     * @param relay       릴레이 세션. 사람별 토큰이 여기 있다
     */
    private record MatchEntry(String hostAddress, RelaySession relay) {
    }

    /** 닉네임은 연결 시 한 번만 조회해 세션에 보관한다 (브로드캐스트마다 DB를 보지 않기 위해). */
    @Override
    public void afterConnectionEstablished(WebSocketSession session) {
        Long userId = userId(session);
        // **세션당 쓰기를 직렬화한다**(S15P21B202-313 ③). Spring 의 WebSocketSession 은
        // 스레드 안전하지 않은데 같은 세션에 쓰는 주체가 여럿이다 — 다른 플레이어들의
        // PLAYER_MOVED 중계(입력 주기마다), 접속 종료 처리, 그리고 **별 스레드**인 유예 스케줄러.
        //
        // 2026-09-07 EC2 실측에서 Tomcat 이 던지는 것을 현장에서 잡았다:
        //   IllegalStateException: The remote endpoint was in state [TEXT_PARTIAL_WRITING]
        // 그 메시지는 그대로 유실됐고, 받아야 할 클라이언트는 5초를 굶고 실패했다.
        sessions.put(userId, new ConcurrentWebSocketSessionDecorator(
                session, SEND_TIME_LIMIT_MS, SEND_BUFFER_LIMIT_BYTES));
        session.getAttributes().put(NICKNAME_ATTR, userRepository.findById(userId)
                .map(user -> user.getNickname())
                .orElse(UNKNOWN_NICKNAME));
        log.info("connected: userId={}", userId);
    }

    /**
     * 잘못된 메시지 하나로 연결이 끊기지 않도록, 해석·처리 실패는 ERROR 응답으로 돌려준다.
     * (모르는 type은 enum 변환에서, 빠진 필드는 처리 중에 예외가 난다)
     */
    @Override
    protected void handleTextMessage(WebSocketSession session, TextMessage message) {
        Long userId = userId(session);

        WsMessage msg;
        WsMessage.Type type;
        try {
            msg = objectMapper.readValue(message.getPayload(), WsMessage.class);
            type = WsMessage.Type.valueOf(msg.type());
        } catch (RuntimeException e) {
            send(session, WsMessage.Type.ERROR, Map.of("message", "unknown type"));
            return;
        }

        try {
            // 방을 만들거나 들어올 때 자기 랜 주소를 함께 보낸다. 없으면 없는 대로 둔다 —
            // 옛 클라이언트도 계속 붙을 수 있어야 한다.
            rememberLocalIp(session, msg.data());

            switch (type) {
                case CREATE_ROOM -> createRoom(session, userId);
                case JOIN_ROOM -> joinRoom(session, userId, msg.data().get("roomId").asString());
                case LEAVE_ROOM -> leaveRoom(session, userId);
                case READY -> ready(userId, msg.data().get("ready").asBoolean());
                case START_GAME -> startGame(userId, msg.data());
                case GAME_END -> endGame(userId);
                case PLAYER_MOVE -> playerMove(userId, msg.data());
                default -> send(session, WsMessage.Type.ERROR, Map.of("message", "unknown type"));
            }
        } catch (RuntimeException e) {
            log.warn("message handling failed: userId={} type={} : {}", userId, type, e.toString());
            send(session, WsMessage.Type.ERROR, Map.of("message", "invalid message"));
        }
    }

    /**
     * WAITING 상태에서는 즉시 퇴장시킨다.
     * PLAYING 상태에서는 유예 시간 동안 자리를 유지하고, 넘기면 그 사람만 제외한 뒤
     * 남은 인원으로 게임을 계속한다 (지침 문서 5-1).
     */
    @Override
    public void afterConnectionClosed(WebSocketSession session, CloseStatus status) {
        Long userId = userId(session);
        // 재접속으로 이미 새 세션이 등록된 뒤 옛 세션이 닫히는 경우가 있어, 같은 세션일 때만 정리한다.
        // 저장된 것은 감싼 세션이므로 **객체 동일성으로 비교할 수 없다** — 감싼 대상을 벗겨서 본다.
        // remove(k, v) 로 원자적으로 지우고, 그 사이 새 세션이 들어왔으면 정리하지 않는다.
        WebSocketSession stored = sessions.get(userId);
        if (stored == null
                || WebSocketSessionDecorator.unwrap(stored) != WebSocketSessionDecorator.unwrap(session)
                || !sessions.remove(userId, stored)) {
            log.info("stale session closed: userId={}", userId);
            return;
        }

        String roomId = (String) session.getAttributes().get(ROOM_ID_ATTR);
        Room room = roomId == null ? null : roomManager.find(roomId);
        if (room == null) {
            log.info("disconnected: userId={}", userId);
            return;
        }

        if (room.getStatus() == Room.Status.PLAYING) {
            room.markDisconnected(userId);
            broadcastRoomState(room);
            graceManager.schedule(roomId, userId, () -> onGraceExpired(roomId, userId));
            log.info("disconnected (grace): userId={} room={}", userId, roomId);
        } else {
            removeFromRoom(room, userId);
            log.info("disconnected (left): userId={} room={}", userId, roomId);
        }
    }

    /** 유예 시간 만료 — 참가자 목록에서 제외한다. 게임은 남은 인원으로 계속된다. */
    private void onGraceExpired(String roomId, Long userId) {
        Room room = roomManager.find(roomId);
        if (room == null) {
            return;   // 방이 사라졌다
        }
        // **판정과 제거가 한 임계구역이어야 한다** (S15P21B202-313 ①). 둘 사이에 재입장이
        // 끼어들면 막 돌아온 사람을 지우게 된다. 방송·릴레이 호출은 락 밖에서 한다.
        synchronized (room) {
            if (!room.isDisconnected(userId)) {
                return;   // 이미 복귀했다
            }
            room.leave(userId);
        }
        afterLeave(room);
        log.info("grace expired, removed: userId={} room={}", userId, roomId);
    }

    private void removeFromRoom(Room room, Long userId) {
        synchronized (room) {
            room.leave(userId);
        }
        afterLeave(room);
    }

    /** 자리에서 빠진 뒤의 정리. **락 밖에서** 한다 — 방송과 릴레이 호출이 들어 있다. */
    private void afterLeave(Room room) {
        String roomId = room.getRoomId();
        roomManager.removeIfEmpty(roomId);
        // 방이 실제로 사라졌으면 릴레이 세션도 닫는다. removeIfEmpty 는 void 라 조회로 확인한다.
        if (roomManager.find(roomId) == null) {
            closeRelay(roomId);
        }
        broadcastRoomState(room);
    }

    /** 그 방의 릴레이 세션을 닫고 접속 정보를 버린다. 없으면 아무 것도 하지 않는다. */
    private void closeRelay(String roomId) {
        MatchEntry entry = matches.remove(roomId);
        if (entry != null && entry.relay() != null) {
            relayService.close(entry.relay().sessionId());
        }
    }

    private void createRoom(WebSocketSession session, Long userId) {
        Room room = roomManager.createRoom(userId, nickname(session));
        session.getAttributes().put(ROOM_ID_ATTR, room.getRoomId());
        send(session, WsMessage.Type.ROOM_CREATED, Map.of("roomId", room.getRoomId()));
        broadcastRoomState(room);
    }

    /**
     * 대기 중인 방에는 일반 입장, 진행 중인 방에는 복귀(재입장)로 처리한다.
     * 복귀는 그 판에 참가했던 유저만 허용되며, 기획에 따라 기절 상태로 돌아온다.
     */
    private void joinRoom(WebSocketSession session, Long userId, String roomId) {
        Room room = roomManager.find(roomId);
        if (room == null) {
            send(session, WsMessage.Type.ERROR, Map.of("message", "cannot join room"));
            return;
        }

        if (room.getStatus() == Room.Status.PLAYING) {
            // 재입장 판정과 유예 취소를 **한 임계구역**에서 한다 (S15P21B202-313 ①).
            // 예전에는 유예 만료 스레드가 isDisconnected 를 읽은 직후 여기서 재입장이 성공하면,
            // 그 스레드가 **막 돌아온 사람을 방에서 지웠다.** 그 사람은 playerIds() 에서 빠져
            // ROOM_STATE·PLAYER_MOVED 를 하나도 못 받는 유령이 됐다.
            // cancel 로는 막을 수 없다 — 만료 태스크가 먼저 pending 에서 자기를 지우기 때문에
            // cancel 이 키를 찾지도 못한다(ReconnectGraceManager 참고).
            Room.RejoinResult result;
            synchronized (room) {
                result = room.rejoin(userId, nickname(session));
                if (result == Room.RejoinResult.OK) {
                    // **정규화된** 방 코드로 취소한다 (S15P21B202-313 ②). 예전에는 클라이언트가
                    // 보낸 원본 문자열을 썼는데 타이머는 정규화된 코드로 걸려 있어,
                    // 소문자 코드로 재입장하면 취소가 **반드시** 빗나갔다.
                    //
                    // 취소할 것이 없으면 경고한다 — 여기 도달했다는 것은 PLAYING 인 방에
                    // 끊겨 있던 사람이 돌아왔다는 뜻이고, 그러면 타이머가 있어야 정상이다.
                    // 없다면 키가 어긋났거나 유예가 이미 만료된 것이다. 조용히 넘기면
                    // ② 같은 실수가 또 눈에 안 보인다.
                    if (!graceManager.cancel(room.getRoomId(), userId)) {
                        log.warn("rejoin: 취소할 유예 타이머가 없다 — 키 불일치 또는 이미 만료. "
                                + "room={} user={}", room.getRoomId(), userId);
                    }
                }
            }
            if (result != Room.RejoinResult.OK) {
                send(session, WsMessage.Type.ERROR, Map.of("message",
                        result == Room.RejoinResult.FULL ? "room is full" : "cannot join room"));
                return;
            }
            session.getAttributes().put(ROOM_ID_ATTR, room.getRoomId());   // 클라이언트가 보낸 대소문자가 아닌 정규화된 코드를 보관
            if (room.getGameState() != null) {
                room.getGameState().setDowned(userId, true);   // 재입장 시 기절 상태로 복귀
            }
            sendStateSync(session, room);
            broadcastRoomState(room);
            // 어디로 붙어야 하는지 다시 알려 준다 (S15P21B202-150).
            //
            // 이것이 없어서 재접속이 성립하지 않았다 — 방에는 들어오는데 매치 주소와
            // 사람별 릴레이 토큰을 못 들어 로비에 갇혔다. 게임이 이미 PLAYING 이라
            // 시작 버튼도 비활성이므로(LobbyHud 의 room.IsWaiting 조건) 들어갈 길이 없었다.
            //
            // 클라이언트는 GAME_STARTED 를 받으면 알아서 매치로 들어간다
            // (NetworkSession 이 주소를 담고 LobbyBootstrap.EnterMatch 가 씬을 넘긴다).
            sendGameStarted(session, room, userId);
            log.info("rejoined: userId={} room={}", userId, roomId);
            return;
        }

        if (!room.join(userId, nickname(session))) {
            send(session, WsMessage.Type.ERROR, Map.of("message", "cannot join room"));
            return;
        }
        session.getAttributes().put(ROOM_ID_ATTR, room.getRoomId());   // 클라이언트가 보낸 대소문자가 아닌 정규화된 코드를 보관
        broadcastRoomState(room);
    }

    private void leaveRoom(WebSocketSession session, Long userId) {
        String roomId = (String) session.getAttributes().remove(ROOM_ID_ATTR);
        if (roomId == null) {
            return;
        }
        graceManager.cancel(roomId, userId);
        Room room = roomManager.find(roomId);
        if (room != null) {
            removeFromRoom(room, userId);
        }
    }

    private void ready(Long userId, boolean ready) {
        Room room = roomOf(userId);
        if (room != null) {
            room.setReady(userId, ready);
            broadcastRoomState(room);
        }
    }

    private void startGame(Long userId, JsonNode data) {
        Room room = roomOf(userId);
        if (room == null || !userId.equals(room.getHostUserId()) || !room.start()) {
            WebSocketSession session = sessions.get(userId);
            if (session != null) {
                send(session, WsMessage.Type.ERROR, Map.of("message", "cannot start game"));
            }
            return;
        }

        // 방장의 클라이언트가 곧 게임 서버다(S15P21B202-263). 참가자들이 어디로 붙을지는
        // 방장만 알 수 있으므로 START_GAME 에 실려 온 값을 그대로 전달한다.
        // 서버가 보는 방장의 주소(공인 IP)로는 안 된다 — 공유기가 밖에서 들어오는
        // 연결을 막기 때문에 같은 네트워크 안에서 쓰는 사설 주소여야 한다.
        String hostAddress = data != null && data.hasNonNull("hostAddress")
                ? data.get("hostAddress").asText("")
                : "";

        // 서로 다른 네트워크에서도 붙으려면 릴레이를 거쳐야 한다(-264).
        // 세션을 열려면 참가자 전원의 공인 IP 가 필요한데 그 목록은 서버만 갖고 있다.
        // 실패하면 null 이고, 그때는 위의 직접 접속 주소로 간다.
        LinkedHashMap<Long, String> publicIps = publicIpsOf(room);
        boolean same = sameNetwork(room, publicIps);
        RelaySession relay = same ? null : relayService.open(publicIps, room.getHostUserId());

        // 재입장한 사람에게 다시 보낼 수 있게 방마다 기억한다 (S15P21B202-150).
        // **릴레이가 null 이어도 보관한다** — 같은 망 직접 접속도 주소를 다시 줘야 한다.
        matches.put(room.getRoomId(), new MatchEntry(hostAddress, relay));

        // 무엇을 골랐는지 남긴다. 릴레이가 안 붙었을 때 어디서 갈렸는지 알아야 한다 —
        // 실패하면 조용히 직접 접속으로 되돌아가므로 로그가 없으면 원인을 못 찾는다.
        // IP 는 개인정보라 개수만 센다.
        long localCount = publicIps.values().stream().filter(GameWebSocketHandler::isLocalIp).count();
        log.info("game start: room={} players={} distinctIps={} localIps={} decision={}",
                room.getRoomId(), publicIps.size(),
                publicIps.values().stream().distinct().count(),
                localCount,
                same ? "direct(same network)" : (relay != null ? "relay" : "direct(relay failed)"));

        // 같은 망 판별이 왜 그렇게 나왔는지 남긴다. 같은 WiFi 인데 릴레이를 타면
        // 여기서 갈린 것이다 — 공인 IP 는 다른데 랜 주소를 아무도 안 알려 준 경우다.
        log.info("same network 판별: 공인IP종류={} · 같은랜대역={}",
                publicIps.values().stream().distinct().count(), sameLocalSubnet(room));

        if (localCount > 0) {
            log.warn("공인 IP 를 못 읽었다 — nginx 가 X-Forwarded-For 를 넘기는지 확인할 것. "
                    + "이대로면 모두 같은 주소로 보여 릴레이가 켜지지 않는다(-264).");
        }

        for (Long playerId : room.playerIds()) {
            WebSocketSession target = sessions.get(playerId);
            if (target == null || !target.isOpen()
                    || !room.getRoomId().equals(target.getAttributes().get(ROOM_ID_ATTR))) {
                continue;
            }

            sendGameStarted(target, room, playerId);
        }
    }

    /**
     * 이 사람 몫의 {@code GAME_STARTED} 를 보낸다 — 매치에 붙는 데 필요한 것 전부.
     *
     * <p><b>게임 시작과 재입장이 같은 코드를 쓴다</b>(S15P21B202-150). 예전에는 시작
     * 경로에만 있었고, 그래서 재입장한 사람은 방에는 들어오지만 <b>어디로 붙어야 하는지를
     * 못 들었다.</b> 로비에 갇히고, 게임이 이미 {@code PLAYING} 이라 시작 버튼도 비활성이라
     * 들어갈 길이 없었다.
     *
     * <p>사람마다 {@code userToken} 이 다르므로 방송하지 않고 각자에게 따로 보낸다.
     */
    private void sendGameStarted(WebSocketSession target, Room room, Long playerId) {
        MatchEntry entry = matches.get(room.getRoomId());

        Map<String, Object> payload = new LinkedHashMap<>();
        payload.put("roomId", room.getRoomId());

        if (entry == null) {
            // 접속 정보가 없다 — 서버가 재시작된 뒤 진행 중인 방이 남은 경우다.
            // 방 번호만이라도 보내면 클라이언트가 옛 주소로 시도하지 않고 멈춘다.
            log.warn("no match entry: room={} userId={} — 접속 정보 없이 GAME_STARTED 를 보낸다",
                    room.getRoomId(), playerId);
            send(target, WsMessage.Type.GAME_STARTED, payload);
            return;
        }

        if (!entry.hostAddress().isBlank()) {
            payload.put("hostAddress", entry.hostAddress());
        }

        if (entry.relay() != null) {
            Map<String, Object> mine = entry.relay().payloadFor(playerId);
            if (mine != null) {
                payload.put("relay", mine);
            }
        }

        send(target, WsMessage.Type.GAME_STARTED, payload);
    }

    /**
     * 판이 끝났음을 방장이 알린다 — 방을 대기 상태로 되돌린다 (S15P21B202-316).
     *
     * <p><b>서버는 판정하지 않는다.</b> 클리어 여부는 호스트가 판단한다(-145 호스트
     * 권위). 서버는 그 결과만 받아 방 상태를 되돌리는 일만 한다.
     *
     * <p><b>새 서버→클라이언트 메시지를 만들지 않았다.</b> {@code ROOM_STATE} 가 이미
     * {@code status} 를 실어 보내고 클라이언트에도 핸들러가 있으므로, {@code WAITING}
     * 으로 바뀐 {@code ROOM_STATE} 하나가 곧 "로비로 돌아가라" 다. 결과 화면에 필요한
     * 클리어 여부·경과 시간은 호스트가 SyncVar 로 내리는 몫이다(-54).
     *
     * <p><b>릴레이 세션은 닫지 않는다.</b> 같은 방에서 바로 한 판 더 하는 것이 흔하고,
     * 그때 세션을 다시 열면 주소와 토큰이 새로 발급돼 전원이 다시 붙어야 한다. 세션은
     * 방이 실제로 사라질 때({@link #removeFromRoom}) 닫히고, 백엔드가 죽어 그마저
     * 못 하면 릴레이의 30분 TTL 이 걷어낸다(-299).
     *
     * <p>유예 중인 사람의 타이머도 취소하지 않는다. 취소하면 이미 연결이 끊긴 그
     * 사람의 자리가 영구히 남는다 — 만료되도록 두면 자리가 정리된다.
     */
    private void endGame(Long userId) {
        Room room = roomOf(userId);
        if (room == null || !userId.equals(room.getHostUserId()) || !room.end()) {
            // 저장된 세션은 동시 쓰기를 직렬화하도록 감싼 것이다(-313). 원본으로 보내면 그 보호를 잃는다.
            WebSocketSession session = sessions.get(userId);
            if (session != null) {
                send(session, WsMessage.Type.ERROR, Map.of("message", "cannot end game"));
            }
            return;
        }

        broadcastRoomState(room);
        log.info("game end: room={} by userId={}", room.getRoomId(), userId);
    }

    /**
     * 모두가 같은 공유기 뒤에 있는가 — 공인 IP 가 하나뿐이면 그렇다(S15P21B202-264).
     *
     * <p>그럴 때는 사설 주소로 서로 바로 닿으므로 릴레이가 필요 없다. 굳이 태우면
     * 지연만 늘고 릴레이 동시접속 한도(과금 기준)를 쓴다. 교육장처럼 다 같이 있는
     * 자리가 정확히 이 경우다.
     *
     * <p>같은 공인 IP 라도 공유기가 기기끼리의 통신을 막아 두면(AP isolation) 못 붙는다.
     * 그때는 핫스팟으로 우회한다 — {@code -145 §4-3} 에 적어 둔 그대로다.
     */
    /**
     * 다 같은 망에 있는가 — 그렇다면 릴레이 없이 서로 바로 닿는다.
     *
     * <p>둘 중 하나만 맞으면 같은 망으로 본다.
     * <ul>
     *   <li>공인 IP 가 전부 같다 — 한 공유기 뒤에 있다는 뜻</li>
     *   <li>각자 알려 준 <b>랜 주소가 같은 대역</b>이다 — 공인 IP 를 여러 개 돌려쓰는
     *       망에서도 이건 맞는다. 이것이 없어서 같은 WiFi 인데 릴레이를 탔다(2026-09-02)</li>
     * </ul>
     */
    private boolean sameNetwork(Room room, LinkedHashMap<Long, String> publicIps) {
        if (publicIps.size() < 2) {
            return true;
        }
        if (publicIps.values().stream().distinct().count() == 1) {
            return true;
        }
        return sameLocalSubnet(room);
    }

    /** 방 전원이 같은 사설 대역(앞 세 자리)에 있는가. 한 명이라도 안 알려 줬으면 거짓. */
    private boolean sameLocalSubnet(Room room) {
        String shared = null;
        for (Long playerId : room.playerIds()) {
            WebSocketSession session = sessions.get(playerId);
            if (session == null) {
                return false;
            }
            String local = (String) session.getAttributes().get(LOCAL_IP_ATTR);
            String subnet = subnetOf(local);
            if (subnet == null) {
                return false;                       // 안 알려 줬거나 사설 주소가 아니다
            }
            if (shared == null) {
                shared = subnet;
            } else if (!shared.equals(subnet)) {
                return false;
            }
        }
        return shared != null;
    }

    /** 사설 주소의 앞 세 자리(192.168.31). 사설이 아니거나 형식이 아니면 null. */
    private static String subnetOf(String ip) {
        if (ip == null || ip.isBlank() || !isLocalIp(ip)) {
            return null;
        }
        int last = ip.lastIndexOf('.');
        return last > 0 ? ip.substring(0, last) : null;
    }

    /** 클라이언트가 알려 준 자기 랜 주소를 세션에 적어 둔다. */
    private static void rememberLocalIp(WebSocketSession session, JsonNode data) {
        if (data == null || !data.has("localAddress")) {
            return;
        }
        String local = data.get("localAddress").asString();
        if (local != null && !local.isBlank()) {
            session.getAttributes().put(LOCAL_IP_ATTR, local.trim());
        }
    }

    /**
     * 방 사람들의 공인 IP 를 로비 연결에서 읽는다(-264). 릴레이 세션을 열 때 쓴다.
     * 순서가 응답의 session_users 와 짝이 되므로 넣은 차례를 지키는 맵을 쓴다.
     */
    /**
     * userId → 공인 IP. <b>방장을 맨 앞에 둔다.</b>
     *
     * <p>릴레이 세션에서 방장은 게임 서버 자리에 앉는다. Edgegap 이 목록 순서를 어떻게
     * 해석하는지 문서에 분명치 않은데, 첫 번째를 서버로 보는 구현이라면 순서가 뒤바뀐 순간
     * 방장이 세션 주인으로 인정받지 못하고 참가자는 SessionTimeout 을 받는다.
     * 방장을 앞에 두는 것은 공짜이므로 그 위험을 없앤다(2026-09-02).
     */
    private LinkedHashMap<Long, String> publicIpsOf(Room room) {
        LinkedHashMap<Long, String> ips = new LinkedHashMap<>();

        List<Long> ordered = new ArrayList<>(room.playerIds());
        Long host = room.getHostUserId();
        if (host != null && ordered.remove(host)) {
            ordered.add(0, host);
        }

        for (Long playerId : ordered) {
            WebSocketSession session = sessions.get(playerId);
            String ip = clientIp(session);
            if (ip != null && !ip.isBlank()) {
                ips.put(playerId, ip);
            }
        }
        return ips;
    }

    /**
     * 이 사람이 밖에서 보이는 주소(S15P21B202-264).
     *
     * <p>우리 서버는 nginx 뒤에 있다. 소켓이 보는 상대는 nginx 자신(127.0.0.1)이므로
     * 그것만 보면 <b>모두가 같은 주소로 보이고</b>, 그러면 릴레이가 영원히 안 켜진다.
     * 게다가 그 값을 Edgegap 에 보내면 세션이 엉뚱하게 만들어진다.
     *
     * <p>그래서 핸드셰이크의 {@code X-Forwarded-For} 를 먼저 본다. nginx 가 그 헤더를
     * 채워 주도록 설정돼 있어야 한다. 값이 여러 개면 맨 앞이 실제 사용자다.
     */
    private String clientIp(WebSocketSession session) {
        if (session == null) {
            return null;
        }

        String forwarded = session.getHandshakeHeaders().getFirst("X-Forwarded-For");
        if (forwarded != null && !forwarded.isBlank()) {
            return forwarded.split(",")[0].trim();
        }

        return session.getRemoteAddress() == null
                ? null
                : session.getRemoteAddress().getAddress().getHostAddress();
    }

    /** 사설·루프백 주소인가. 릴레이에 넘기면 안 되는 값이라 로그로 알린다. */
    private static boolean isLocalIp(String ip) {
        return ip == null || ip.startsWith("127.") || ip.startsWith("10.")
                || ip.startsWith("192.168.") || ip.startsWith("172.16.")
                || ip.equals("::1") || ip.equals("0:0:0:0:0:0:0:1");
    }

    /**
     * 자기 위치를 같은 방의 다른 사람들에게 전달한다. 보낸 사람에게는 되돌려 보내지 않는다.
     * 서버는 물리를 계산하지 않고 중계만 하며, 마지막 위치는 GameState에 남겨
     * 복귀한 플레이어가 끊긴 자리에서 이어갈 수 있게 한다.
     */
    private void playerMove(Long userId, JsonNode data) {
        Room room = roomOf(userId);
        if (room == null || room.getStatus() != Room.Status.PLAYING || room.getGameState() == null) {
            return;
        }

        float x = (float) data.get("x").asDouble();
        float y = (float) data.get("y").asDouble();
        float z = (float) data.get("z").asDouble();
        float yaw = data.has("yaw") ? (float) data.get("yaw").asDouble() : 0f;
        room.getGameState().setPosition(userId, x, y, z, yaw);

        Map<String, Object> moved = new LinkedHashMap<>();
        moved.put("userId", userId);
        moved.put("x", x);
        moved.put("y", y);
        moved.put("z", z);
        moved.put("yaw", yaw);
        broadcastExcept(room, userId, WsMessage.Type.PLAYER_MOVED, moved);
    }

    private Room roomOf(Long userId) {
        WebSocketSession session = sessions.get(userId);
        if (session == null) {
            return null;
        }
        String roomId = (String) session.getAttributes().get(ROOM_ID_ATTR);
        return roomId == null ? null : roomManager.find(roomId);
    }

    /** 복귀한 플레이어에게 현재 게임 상태 전체를 보낸다 (지침 문서 4-2). */
    private void sendStateSync(WebSocketSession session, Room room) {
        Map<String, Object> data = new LinkedHashMap<>();
        data.put("roomId", room.getRoomId());
        data.put("gameState", room.getGameState());
        send(session, WsMessage.Type.STATE_SYNC, data);
    }

    private void broadcastRoomState(Room room) {
        Map<String, Object> data = new LinkedHashMap<>();
        data.put("roomId", room.getRoomId());
        data.put("hostUserId", room.getHostUserId());
        data.put("status", room.getStatus().name());
        data.put("players", room.getPlayers());
        data.put("disconnected", room.getDisconnected());
        broadcast(room, WsMessage.Type.ROOM_STATE, data);
    }

    private void broadcast(Room room, WsMessage.Type type, Map<String, Object> data) {
        broadcastExcept(room, null, type, data);
    }

    /**
     * 세션은 userId로만 찾으므로, 재접속으로 세션이 바뀐 뒤 옛 방의 브로드캐스트가
     * 새 세션으로 새어 나갈 수 있다. 세션이 실제로 그 방에 속해 있는지 확인하고 보낸다.
     */
    private void broadcastExcept(Room room, Long excludedUserId, WsMessage.Type type,
                                 Map<String, Object> data) {
        for (Long playerId : room.playerIds()) {
            if (playerId.equals(excludedUserId)) {
                continue;
            }
            WebSocketSession session = sessions.get(playerId);
            // 왜 빠졌는지 반드시 남긴다 (S15P21B202-313 ④). 예전에는 조용히 continue 해서
            // 메시지 유실이 서버 로그에 아무 흔적도 남기지 않았다 — 진단이 불가능했다.
            if (session == null) {
                // 유예 중인 사람은 세션이 없는 것이 **정상**이다(끊겼지만 자리는 유지). 그때는 조용히
                // 넘어가고, 끊김 표시도 없는데 세션이 없으면 그것만 경고한다 — 그것이 이상한 경우다.
                if (room.isDisconnected(playerId)) {
                    log.debug("broadcast skip: type={} user={} room={} 이유=유예중(세션없음)",
                            type, playerId, room.getRoomId());
                } else {
                    log.warn("broadcast skip: type={} user={} room={} 이유=세션없음(끊김 표시도 없다)",
                            type, playerId, room.getRoomId());
                }
                continue;
            }
            if (!session.isOpen()) {
                log.warn("broadcast skip: type={} user={} room={} 이유=세션닫힘", type, playerId, room.getRoomId());
                continue;
            }
            Object attr = session.getAttributes().get(ROOM_ID_ATTR);
            if (!room.getRoomId().equals(attr)) {
                log.warn("broadcast skip: type={} user={} room={} 이유=방불일치(attr={})",
                        type, playerId, room.getRoomId(), attr);
                continue;
            }
            send(session, type, data);
        }
    }

    /**
     * 한 세션에 한 건 보낸다.
     *
     * <p><b>예외를 여기서 끝낸다</b>(S15P21B202-313 ③). 예전에는 {@code IOException} 만 잡았는데,
     * Tomcat 은 동시 쓰기·닫힌 엔드포인트에 <b>{@code IllegalStateException}</b>(unchecked)을 던진다.
     * 그것이 {@code send} 를 뚫고 나가면 팬아웃 루프가 그 자리에서 끝나 <b>뒤 순번의 사람들이
     * 그 메시지를 영구히 잃었다.</b> 한 명에게 못 보낸 것이 나머지 전원의 손실로 번지면 안 된다.
     *
     * <p>대상은 {@link #writeTarget}(감싼 세션)으로 바꾼다 — 호출부가 스프링이 준 원본 세션을
     * 넘기더라도 실제 쓰기는 직렬화된 쪽으로 나간다.
     */
    private void send(WebSocketSession session, WsMessage.Type type, Map<String, Object> data) {
        WebSocketSession target = writeTarget(session);
        try {
            String payload = objectMapper.writeValueAsString(
                    Map.of("type", type.name(), "data", data));
            target.sendMessage(new TextMessage(payload));
        } catch (IOException e) {
            log.warn("send failed: type={} sid={} : {}", type, target.getId(), e.getMessage());
        } catch (RuntimeException e) {
            // 여기서 멈춘다. 세션 하나의 문제가 방 전체의 손실이 되지 않게.
            log.warn("send threw: type={} sid={} {}: {}",
                    type, target.getId(), e.getClass().getSimpleName(), e.getMessage());
        }
    }

    /** 이 세션에 쓸 때 실제로 써야 하는 대상 — 등록돼 있으면 감싼 세션, 아니면 그대로. */
    private WebSocketSession writeTarget(WebSocketSession session) {
        Long userId = userId(session);
        WebSocketSession stored = userId == null ? null : sessions.get(userId);
        if (stored != null
                && WebSocketSessionDecorator.unwrap(stored) == WebSocketSessionDecorator.unwrap(session)) {
            return stored;
        }
        return session;
    }

    private Long userId(WebSocketSession session) {
        return (Long) session.getAttributes().get(JwtHandshakeInterceptor.USER_ID_ATTR);
    }

    private String nickname(WebSocketSession session) {
        Object cached = session.getAttributes().get(NICKNAME_ATTR);
        return cached == null ? UNKNOWN_NICKNAME : (String) cached;
    }
}
