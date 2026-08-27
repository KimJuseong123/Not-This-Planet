package com.metagame.backend.websocket;

import tools.jackson.databind.JsonNode;

/**
 * 모든 실시간 메시지의 공통 형식 (지침 문서 2-2).
 * { "type": "JOIN_ROOM", "data": { ... } }
 */
public record WsMessage(String type, JsonNode data) {

    public enum Type {
        // Client → Server
        CREATE_ROOM, JOIN_ROOM, LEAVE_ROOM, READY, START_GAME,
        // Server → Client
        ROOM_CREATED, ROOM_STATE, GAME_STARTED, STATE_SYNC, ERROR
        // TODO: 게임 기획 확정 후 게임플레이 메시지 추가 (PLAYER_MOVE, CHECKPOINT_REACHED 등)
    }
}
