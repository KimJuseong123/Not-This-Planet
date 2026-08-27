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

    /** 진행 중인 게임 복귀 요청의 처리 결과. */
    public enum RejoinResult { OK, DENIED, FULL }

    public static final int MAX_PLAYERS = 4;   // TODO: 기획 확정 후 조정

    private final String roomId;
    private volatile Long hostUserId;
    private volatile Status status = Status.WAITING;

    // userId → 준비 여부. 현재 방에 속한 사람만 담긴다
    private final Map<Long, Boolean> players = new ConcurrentHashMap<>();

    // 이 판(PLAYING)에 참가했던 전체 userId. 유예를 넘겨 제외된 뒤에도 남아 재입장 자격 판정에 쓰인다
    private final Set<Long> participants = ConcurrentHashMap.newKeySet();

    // 연결이 끊겨 유예 시간을 기다리는 중인 userId. players에는 아직 자리가 남아 있다
    private final Set<Long> disconnected = ConcurrentHashMap.newKeySet();

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

    /**
     * 진행 중인 게임에 복귀시킨다. 이 판에 참가했던 유저만 허용해 무관한 유저의 난입을 막는다.
     * 유예 중이면 자리가 남아 있으므로 끊김 표시만 지우고, 이미 제외되었으면 자리를 다시 만든다.
     */
    public synchronized RejoinResult rejoin(Long userId) {
        if (status != Status.PLAYING || !participants.contains(userId)) {
            return RejoinResult.DENIED;
        }
        if (!players.containsKey(userId) && players.size() >= MAX_PLAYERS) {
            return RejoinResult.FULL;
        }
        disconnected.remove(userId);
        players.putIfAbsent(userId, true);
        if (hostUserId == null) {
            hostUserId = userId;
        }
        return RejoinResult.OK;
    }

    public synchronized void leave(Long userId) {
        players.remove(userId);
        disconnected.remove(userId);
        // 방장이 나가면 남은 사람에게 위임
        if (userId.equals(hostUserId)) {
            hostUserId = players.keySet().stream().findFirst().orElse(null);
        }
    }

    /** 연결이 끊겼음을 표시한다. 자리는 유예 시간 동안 유지된다. */
    public void markDisconnected(Long userId) {
        if (players.containsKey(userId)) {
            disconnected.add(userId);
        }
    }

    public boolean isDisconnected(Long userId) {
        return disconnected.contains(userId);
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
        participants.addAll(players.keySet());
        gameState = new GameState();
        players.keySet().forEach(gameState::addPlayer);
        return true;
    }

    public boolean isEmpty() {
        return players.isEmpty();
    }

    public Set<Long> playerIds() {
        return players.keySet();
    }
}
