package com.metagame.backend.domain.room;

import com.metagame.backend.domain.game.GameState;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import lombok.Getter;

/**
 * 방 하나 = 게임 한 판. 휘발성 데이터이므로 DB에 저장하지 않는다 (지침 문서 3-2).
 */
@Getter
public class Room {

    public enum Status { WAITING, PLAYING }

    public static final int MAX_PLAYERS = 4;   // TODO: 기획 확정 후 조정

    private final String roomId;
    private volatile Long hostUserId;
    private volatile Status status = Status.WAITING;

    // userId → 준비 여부
    private final Map<Long, Boolean> players = new ConcurrentHashMap<>();

    // 게임 상태는 전부 GameState 하나에 모은다 (지침 문서 4장)
    private volatile GameState gameState;
    private volatile GameState lastCheckpoint;

    public Room(String roomId, Long hostUserId) {
        this.roomId = roomId;
        this.hostUserId = hostUserId;
        this.players.put(hostUserId, false);
    }

    public synchronized boolean join(Long userId) {
        if (status != Status.WAITING || players.size() >= MAX_PLAYERS) {
            return false;
        }
        players.put(userId, false);
        return true;
    }

    public synchronized void leave(Long userId) {
        players.remove(userId);
        // 방장이 나가면 남은 사람에게 위임
        if (userId.equals(hostUserId)) {
            hostUserId = players.keySet().stream().findFirst().orElse(null);
        }
    }

    public void setReady(Long userId, boolean ready) {
        players.computeIfPresent(userId, (id, r) -> ready);
    }

    public boolean allReady() {
        return !players.isEmpty() && players.values().stream().allMatch(Boolean::booleanValue);
    }

    public synchronized boolean start() {
        if (status != Status.WAITING || !allReady()) {
            return false;
        }
        status = Status.PLAYING;
        gameState = new GameState();
        return true;
    }

    public boolean isEmpty() {
        return players.isEmpty();
    }

    public Set<Long> playerIds() {
        return players.keySet();
    }
}
