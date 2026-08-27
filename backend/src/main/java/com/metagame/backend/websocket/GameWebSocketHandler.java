package com.metagame.backend.websocket;

import com.metagame.backend.domain.room.Room;
import com.metagame.backend.domain.room.RoomManager;
import java.io.IOException;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;
import org.springframework.web.socket.CloseStatus;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketSession;
import org.springframework.web.socket.handler.TextWebSocketHandler;
import tools.jackson.databind.ObjectMapper;

@Slf4j
@Component
@RequiredArgsConstructor
public class GameWebSocketHandler extends TextWebSocketHandler {

    private static final String ROOM_ID_ATTR = "roomId";

    private final RoomManager roomManager;
    private final ObjectMapper objectMapper;

    // userId → 세션. 브로드캐스트 대상 조회용
    private final Map<Long, WebSocketSession> sessions = new ConcurrentHashMap<>();

    @Override
    public void afterConnectionEstablished(WebSocketSession session) {
        Long userId = userId(session);
        sessions.put(userId, session);
        log.info("connected: userId={}", userId);
    }

    @Override
    protected void handleTextMessage(WebSocketSession session, TextMessage message) {
        Long userId = userId(session);
        WsMessage msg = objectMapper.readValue(message.getPayload(), WsMessage.class);

        switch (WsMessage.Type.valueOf(msg.type())) {
            case CREATE_ROOM -> createRoom(session, userId);
            case JOIN_ROOM -> joinRoom(session, userId, msg.data().get("roomId").asString());
            case LEAVE_ROOM -> leaveRoom(session, userId);
            case READY -> ready(userId, msg.data().get("ready").asBoolean());
            case START_GAME -> startGame(userId);
            default -> send(session, WsMessage.Type.ERROR, Map.of("message", "unknown type"));
        }
    }

    @Override
    public void afterConnectionClosed(WebSocketSession session, CloseStatus status) {
        Long userId = userId(session);
        leaveRoom(session, userId);
        sessions.remove(userId);
        log.info("disconnected: userId={}", userId);
        // TODO: 재접속 유예 처리 — PLAYING 중이면 일정 시간 자리 유지 (지침 문서 6장)
    }

    private void createRoom(WebSocketSession session, Long userId) {
        Room room = roomManager.createRoom(userId);
        session.getAttributes().put(ROOM_ID_ATTR, room.getRoomId());
        send(session, WsMessage.Type.ROOM_CREATED, Map.of("roomId", room.getRoomId()));
        broadcastRoomState(room);
    }

    private void joinRoom(WebSocketSession session, Long userId, String roomId) {
        Room room = roomManager.find(roomId);
        if (room == null || !room.join(userId)) {
            send(session, WsMessage.Type.ERROR, Map.of("message", "cannot join room"));
            return;
        }
        session.getAttributes().put(ROOM_ID_ATTR, roomId);
        broadcastRoomState(room);
    }

    private void leaveRoom(WebSocketSession session, Long userId) {
        String roomId = (String) session.getAttributes().remove(ROOM_ID_ATTR);
        if (roomId == null) {
            return;
        }
        Room room = roomManager.find(roomId);
        if (room != null) {
            room.leave(userId);
            roomManager.removeIfEmpty(roomId);
            broadcastRoomState(room);
        }
    }

    private void ready(Long userId, boolean ready) {
        Room room = roomOf(userId);
        if (room != null) {
            room.setReady(userId, ready);
            broadcastRoomState(room);
        }
    }

    private void startGame(Long userId) {
        Room room = roomOf(userId);
        if (room == null || !userId.equals(room.getHostUserId()) || !room.start()) {
            WebSocketSession session = sessions.get(userId);
            if (session != null) {
                send(session, WsMessage.Type.ERROR, Map.of("message", "cannot start game"));
            }
            return;
        }
        broadcast(room, WsMessage.Type.GAME_STARTED, Map.of("roomId", room.getRoomId()));
    }

    private Room roomOf(Long userId) {
        WebSocketSession session = sessions.get(userId);
        if (session == null) {
            return null;
        }
        String roomId = (String) session.getAttributes().get(ROOM_ID_ATTR);
        return roomId == null ? null : roomManager.find(roomId);
    }

    private void broadcastRoomState(Room room) {
        Map<String, Object> data = new LinkedHashMap<>();
        data.put("roomId", room.getRoomId());
        data.put("hostUserId", room.getHostUserId());
        data.put("status", room.getStatus().name());
        data.put("players", room.getPlayers());
        broadcast(room, WsMessage.Type.ROOM_STATE, data);
    }

    private void broadcast(Room room, WsMessage.Type type, Map<String, Object> data) {
        for (Long playerId : room.playerIds()) {
            WebSocketSession session = sessions.get(playerId);
            if (session != null && session.isOpen()) {
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
}
