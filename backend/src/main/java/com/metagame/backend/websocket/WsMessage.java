package com.metagame.backend.websocket;

import tools.jackson.databind.JsonNode;

/**
 * 모든 실시간 메시지의 공통 형식 (지침 문서 2-2).
 * { "type": "JOIN_ROOM", "data": { ... } }
 */
public record WsMessage(String type, JsonNode data) {

    public enum Type {
        // Client → Server
        CREATE_ROOM, JOIN_ROOM, LEAVE_ROOM, READY, START_GAME, GAME_END, PLAYER_MOVE,
        // Server → Client
        ROOM_CREATED, ROOM_STATE, GAME_STARTED, STATE_SYNC, PLAYER_MOVED, ERROR
        // TODO: 소생·체크포인트 메시지는 기획 확정 후 추가 (S15P21B202-55)
    }
}
