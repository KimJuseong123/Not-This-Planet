package com.metagame.backend.domain.room;

import java.util.Collection;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import org.springframework.stereotype.Component;

/**
 * 서버 메모리에서 방 목록을 관리한다.
 * 서버 1대 전제 — 다중 서버로 확장 시 Redis로 이전 (지침 문서 1장).
 */
@Component
public class RoomManager {

    private final Map<String, Room> rooms = new ConcurrentHashMap<>();

    public Room createRoom(Long hostUserId) {
        String roomId = UUID.randomUUID().toString().substring(0, 8);
        Room room = new Room(roomId, hostUserId);
        rooms.put(roomId, room);
        return room;
    }

    public Room find(String roomId) {
        return rooms.get(roomId);
    }

    public Collection<Room> findAll() {
        return rooms.values();
    }

    public void removeIfEmpty(String roomId) {
        Room room = rooms.get(roomId);
        if (room != null && room.isEmpty()) {
            rooms.remove(roomId);
        }
    }
}
