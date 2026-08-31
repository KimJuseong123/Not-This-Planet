package com.metagame.backend.domain.room;

import java.security.SecureRandom;
import java.util.Collection;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import org.springframework.stereotype.Component;

/**
 * 서버 메모리에서 방 목록을 관리한다.
 * 서버 1대 전제 — 다중 서버로 확장 시 Redis로 이전 (지침 문서 1장).
 */
@Component
public class RoomManager {

    /** 방 코드에 쓰는 문자. 0/O, 1/I/L 처럼 눈으로 구분이 어려운 문자는 제외한다. */
    static final String CODE_ALPHABET = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    static final int CODE_LENGTH = 6;

    private static final int MAX_GENERATE_ATTEMPTS = 100;

    private final Map<String, Room> rooms = new ConcurrentHashMap<>();
    private final SecureRandom random = new SecureRandom();

    /**
     * 방을 만들고 사람이 읽기 쉬운 코드를 배정한다.
     * 코드는 친구에게 말이나 메시지로 전달하는 값이므로 짧고 헷갈리지 않아야 한다 (S15P21B202-76).
     */
    public Room createRoom(Long hostUserId, String hostNickname) {
        for (int i = 0; i < MAX_GENERATE_ATTEMPTS; i++) {
            String code = generateCode();
            Room room = new Room(code, hostUserId, hostNickname);
            if (rooms.putIfAbsent(code, room) == null) {
                return room;
            }
        }
        throw new IllegalStateException("방 코드를 배정할 수 없습니다. 방이 너무 많습니다.");
    }

    /** 입력된 코드는 대소문자를 구분하지 않는다. 정규화는 서버가 책임진다. */
    public Room find(String roomId) {
        String code = normalize(roomId);
        return code == null ? null : rooms.get(code);
    }

    public Collection<Room> findAll() {
        return rooms.values();
    }

    public void removeIfEmpty(String roomId) {
        String code = normalize(roomId);
        if (code == null) {
            return;
        }
        Room room = rooms.get(code);
        if (room != null && room.isEmpty()) {
            rooms.remove(code);
        }
    }

    private String generateCode() {
        StringBuilder sb = new StringBuilder(CODE_LENGTH);
        for (int i = 0; i < CODE_LENGTH; i++) {
            sb.append(CODE_ALPHABET.charAt(random.nextInt(CODE_ALPHABET.length())));
        }
        return sb.toString();
    }

    private String normalize(String roomId) {
        if (roomId == null) {
            return null;
        }
        String trimmed = roomId.trim();
        return trimmed.isEmpty() ? null : trimmed.toUpperCase(Locale.ROOT);
    }
}
