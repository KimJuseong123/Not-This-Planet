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

    // userId → 참가자(닉네임·준비 여부). 현재 방에 속한 사람만 담긴다
    private final Map<Long, RoomPlayer> players = new ConcurrentHashMap<>();

    // 이 판(PLAYING)에 참가했던 전체 userId. 유예를 넘겨 제외된 뒤에도 남아 재입장 자격 판정에 쓰인다
    private final Set<Long> participants = ConcurrentHashMap.newKeySet();

    // 연결이 끊겨 유예 시간을 기다리는 중인 userId. players에는 아직 자리가 남아 있다
    private final Set<Long> disconnected = ConcurrentHashMap.newKeySet();

    // 게임 상태는 전부 GameState 하나에 모은다 (지침 문서 4장)
    private volatile GameState gameState;
    private volatile GameState lastCheckpoint;

    public Room(String roomId, Long hostUserId, String hostNickname) {
        this.roomId = roomId;
        this.hostUserId = hostUserId;
        this.players.put(hostUserId, new RoomPlayer(hostNickname));
    }

    public synchronized boolean join(Long userId, String nickname) {
        if (status != Status.WAITING || players.size() >= MAX_PLAYERS) {
            return false;
        }
        players.put(userId, new RoomPlayer(nickname));
        return true;
    }

    /**
     * 진행 중인 게임에 복귀시킨다. 이 판에 참가했던 유저만 허용해 무관한 유저의 난입을 막는다.
     * 유예 중이면 자리가 남아 있으므로 끊김 표시만 지우고, 이미 제외되었으면 자리를 다시 만든다.
     */
    public synchronized RejoinResult rejoin(Long userId, String nickname) {
        if (status != Status.PLAYING || !participants.contains(userId)) {
            return RejoinResult.DENIED;
        }
        if (!players.containsKey(userId) && players.size() >= MAX_PLAYERS) {
            return RejoinResult.FULL;
        }
        disconnected.remove(userId);
        players.computeIfAbsent(userId, id -> {
            RoomPlayer returning = new RoomPlayer(nickname);
            returning.setReady(true);
            return returning;
        });
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
        RoomPlayer player = players.get(userId);
        if (player != null) {
            player.setReady(ready);
        }
    }

    public boolean allReady() {
        return !players.isEmpty() && players.values().stream().allMatch(RoomPlayer::isReady);
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

    /**
     * 판을 끝내고 방을 대기 상태로 되돌린다 (S15P21B202-316).
     *
     * <p>이것이 없어서 한 번 시작한 방은 영구히 {@code PLAYING} 이었다.
     * {@link #join} 은 {@code WAITING} 만 받으므로 끝난 방에는 아무도 들어올 수 없었고,
     * 다시 하려면 방을 새로 만들어 코드를 다시 공유해야 했다.
     *
     * <p><b>준비 상태를 전부 내린다.</b> 그러지 않으면 판이 끝난 직후 방장이 시작을
     * 한 번 더 누르는 것만으로 다음 판이 곧바로 시작된다 — 결과 화면을 보고 있을
     * 사람들이 영문도 모르고 다음 판에 들어간다.
     *
     * <p><b>참가자 명단({@code participants})도 비운다.</b> 이 목록은 "이 판에 참가했던
     * 사람" 이고 {@link #rejoin} 의 자격 판정에만 쓰인다. 비우지 않으면 1라운드에 있다가
     * 나간 사람이 2라운드 명단에 남아, 참가하지도 않은 판에 복귀할 수 있게 된다.
     *
     * <p><b>끊김 표시({@code disconnected})는 건드리지 않는다.</b> 유예 타이머가 아직
     * 돌고 있으므로 그대로 두면 만료 시점에 자리가 정리된다. 여기서 지우면 만료
     * 처리가 그 사람을 못 찾아 빈 자리가 영구히 남는다.
     *
     * @return 되돌렸으면 true. {@code PLAYING} 이 아니었으면 false
     */
    public synchronized boolean end() {
        if (status != Status.PLAYING) {
            return false;
        }
        status = Status.WAITING;
        participants.clear();
        players.values().forEach(player -> player.setReady(false));
        gameState = null;
        return true;
    }

    public boolean isEmpty() {
        return players.isEmpty();
    }

    public Set<Long> playerIds() {
        return players.keySet();
    }
}
