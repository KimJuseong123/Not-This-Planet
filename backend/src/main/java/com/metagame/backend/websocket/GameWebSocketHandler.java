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
import org.springframework.web.socket.handler.TextWebSocketHandler;
import tools.jackson.databind.JsonNode;
import tools.jackson.databind.ObjectMapper;

@Slf4j
@Component
@RequiredArgsConstructor
public class GameWebSocketHandler extends TextWebSocketHandler {

    private static final String ROOM_ID_ATTR = "roomId";
    private static final String NICKNAME_ATTR = "nickname";
    private static final String UNKNOWN_NICKNAME = "비행사";

    private final RoomManager roomManager;
    private final ReconnectGraceManager graceManager;
    private final UserRepository userRepository;
    private final ObjectMapper objectMapper;
    private final EdgegapRelayService relayService;

    // userId → 세션. 브로드캐스트 대상 조회용
    private final Map<Long, WebSocketSession> sessions = new ConcurrentHashMap<>();

    /** 닉네임은 연결 시 한 번만 조회해 세션에 보관한다 (브로드캐스트마다 DB를 보지 않기 위해). */
    @Override
    public void afterConnectionEstablished(WebSocketSession session) {
        Long userId = userId(session);
        sessions.put(userId, session);
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
            switch (type) {
                case CREATE_ROOM -> createRoom(session, userId);
                case JOIN_ROOM -> joinRoom(session, userId, msg.data().get("roomId").asString());
                case LEAVE_ROOM -> leaveRoom(session, userId);
                case READY -> ready(userId, msg.data().get("ready").asBoolean());
                case START_GAME -> startGame(userId, msg.data());
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
        // 재접속으로 이미 새 세션이 등록된 뒤 옛 세션이 닫히는 경우가 있어, 같은 세션일 때만 정리한다
        if (!sessions.remove(userId, session)) {
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
        if (room == null || !room.isDisconnected(userId)) {
            return;   // 방이 사라졌거나 이미 복귀함
        }
        removeFromRoom(room, userId);
        log.info("grace expired, removed: userId={} room={}", userId, roomId);
    }

    private void removeFromRoom(Room room, Long userId) {
        room.leave(userId);
        roomManager.removeIfEmpty(room.getRoomId());
        broadcastRoomState(room);
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
            Room.RejoinResult result = room.rejoin(userId, nickname(session));
            if (result != Room.RejoinResult.OK) {
                send(session, WsMessage.Type.ERROR, Map.of("message",
                        result == Room.RejoinResult.FULL ? "room is full" : "cannot join room"));
                return;
            }
            graceManager.cancel(roomId, userId);
            session.getAttributes().put(ROOM_ID_ATTR, room.getRoomId());   // 클라이언트가 보낸 대소문자가 아닌 정규화된 코드를 보관
            if (room.getGameState() != null) {
                room.getGameState().setDowned(userId, true);   // 재입장 시 기절 상태로 복귀
            }
            sendStateSync(session, room);
            broadcastRoomState(room);
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
        boolean same = sameNetwork(publicIps);
        RelaySession relay = same ? null : relayService.open(publicIps);

        // 무엇을 골랐는지 남긴다. 릴레이가 안 붙었을 때 어디서 갈렸는지 알아야 한다 —
        // 실패하면 조용히 직접 접속으로 되돌아가므로 로그가 없으면 원인을 못 찾는다.
        // IP 는 개인정보라 개수만 센다.
        long localCount = publicIps.values().stream().filter(GameWebSocketHandler::isLocalIp).count();
        log.info("game start: room={} players={} distinctIps={} localIps={} decision={}",
                room.getRoomId(), publicIps.size(),
                publicIps.values().stream().distinct().count(),
                localCount,
                same ? "direct(same network)" : (relay != null ? "relay" : "direct(relay failed)"));

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

            Map<String, Object> payload = new LinkedHashMap<>();
            payload.put("roomId", room.getRoomId());
            if (!hostAddress.isBlank()) {
                payload.put("hostAddress", hostAddress);
            }

            // 사람마다 userToken 이 다르므로 방송하지 않고 각자에게 따로 보낸다.
            if (relay != null) {
                Map<String, Object> mine = relay.payloadFor(playerId);
                if (mine != null) {
                    payload.put("relay", mine);
                }
            }

            send(target, WsMessage.Type.GAME_STARTED, payload);
        }
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
    private boolean sameNetwork(LinkedHashMap<Long, String> publicIps) {
        if (publicIps.size() < 2) {
            return true;
        }
        return publicIps.values().stream().distinct().count() == 1;
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
            if (session != null && session.isOpen()
                    && room.getRoomId().equals(session.getAttributes().get(ROOM_ID_ATTR))) {
                send(session, type, data);
            }
        }
    }

    private void send(WebSocketSession session, WsMessage.Type type, Map<String, Object> data) {
        try {
            String payload = objectMapper.writeValueAsString(
                    Map.of("type", type.name(), "data", data));
            session.sendMessage(new TextMessage(payload));
        } catch (IOException e) {
            log.warn("send failed: {}", e.getMessage());
        }
    }

    private Long userId(WebSocketSession session) {
        return (Long) session.getAttributes().get(JwtHandshakeInterceptor.USER_ID_ATTR);
    }

    private String nickname(WebSocketSession session) {
        Object cached = session.getAttributes().get(NICKNAME_ATTR);
        return cached == null ? UNKNOWN_NICKNAME : (String) cached;
    }
}
