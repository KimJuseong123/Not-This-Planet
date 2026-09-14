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

    /**
     * 입장 요청의 처리 결과 (S15P21B202-413).
     *
     * <p>실패 이유를 나눠 두는 이유는 <b>사용자가 해야 할 일이 다르기 때문</b>이다 —
     * 꽉 찬 방은 기다리면 자리가 나고, 이미 시작된 방은 다른 방을 찾아야 한다.
     */
    public enum JoinResult { OK, ROOM_PLAYING, ROOM_FULL }

    public static final int MAX_PLAYERS = 4;   // TODO: 기획 확정 후 조정

    /**
     * 맵을 고르지 않은 방이 서는 자리 (2맵 준비).
     *
     * <p>맵이 하나뿐이던 동안에는 서버가 스테이지를 몰라도 됐다 — 클라이언트가 씬
     * 이름을 상수로 들고 있었기 때문이다. 맵이 둘이 되면 <b>같은 방의 네 사람이 같은
     * 맵을 열어야 한다</b>는 것을 보증할 수 있는 곳은 서버뿐이다.
     */
    public static final String DEFAULT_STAGE_ID = "stage1";

    private final String roomId;
    private volatile Long hostUserId;
    private volatile Status status = Status.WAITING;

    // 이 방이 어느 맵으로 시작하는지. 시작하면 GameState 로 넘어가 그 판의 사실이 된다
    private volatile String stageId = DEFAULT_STAGE_ID;

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

    /**
     * 입장을 시도하고 <b>왜 안 되는지까지</b> 알려준다 (S15P21B202-413).
     *
     * <p>예전에는 {@link #join} 이 {@code boolean} 만 돌려줬고, 그래서 클라이언트에는
     * 이유를 가리지 않고 {@code "cannot join room"} 하나만 갔다. 방이 꽉 찬 것과 이미
     * 게임이 시작된 것은 사용자가 <b>해야 할 일이 다르다</b> — 앞은 기다리면 되고 뒤는
     * 다른 방을 찾아야 한다.
     *
     * <p><b>판단은 여기 한 곳에서만 한다.</b> 부르는 쪽에서 {@code getStatus()} 와
     * 인원수를 다시 보고 이유를 짐작하면, 그 조건이 이 메서드와 갈리는 날이 온다.
     */
    public synchronized JoinResult tryJoin(Long userId, String nickname) {
        if (status != Status.WAITING) {
            return JoinResult.ROOM_PLAYING;
        }
        if (players.size() >= MAX_PLAYERS) {
            return JoinResult.ROOM_FULL;
        }
        players.put(userId, new RoomPlayer(nickname));
        return JoinResult.OK;
    }

    /**
     * 들어갔는지만 알면 되는 곳에서 쓴다. 이유가 필요하면 {@link #tryJoin} 을 쓴다.
     */
    public synchronized boolean join(Long userId, String nickname) {
        return tryJoin(userId, nickname) == JoinResult.OK;
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
            hostUserId = pickHost(userId);
        }
    }

    /**
     * 방장 자리를 붙어 있는 다른 사람에게 넘긴다. <b>자리는 건드리지 않는다</b>
     * (S15P21B202-389).
     *
     * <p>방장의 연결이 끊기면 {@code afterConnectionClosed} 는 자리를 30초 유예로 남긴다.
     * 그 사람이 돌아올 수 있게 하려는 것이고 그것은 유지해야 한다 — 하지만 <b>방장 자리는
     * 기다릴 수 없다.</b> 방장의 PC 가 곧 게임 서버라(-145) 30초를 기다리면 그동안 아무도
     * 게임을 할 수 없다. 그래서 자리는 남기고 방장만 즉시 넘긴다.
     *
     * <p><b>끊긴 사람은 고르지 않는다.</b> 게임 서버가 될 수 없기 때문이다 — 유예를
     * 기다리는 중인 사람에게 넘기면 판이 그대로 멈춘다.
     *
     * @return 새 방장. 넘길 사람이 없으면 {@code null} 이고 <b>방장은 그대로 남는다</b> —
     *         null 로 비우면 {@link #rejoin} 이 아무나 방장으로 만든다
     */
    public synchronized Long passHostToConnected() {
        Long next = pickHost(hostUserId);
        if (next != null) {
            hostUserId = next;
        }
        return next;
    }

    /**
     * 방장이 될 사람을 고른다. <b>붙어 있는 사람을 먼저 본다</b> (S15P21B202-389).
     *
     * <p>예전에는 {@code players.keySet().stream().findFirst()} 였다. 그러면 게임 중에
     * 끊겨 유예를 기다리는 사람이 방장이 될 수 있고, 그 사람은 게임 서버가 될 수 없으므로
     * 판이 멈춘다.
     *
     * <p>아무도 붙어 있지 않으면 그때는 끊긴 사람이라도 고른다 — 방이 아직 있으므로
     * 방장이 비어 있는 것보다 낫다.
     *
     * @param except 제외할 사람(나가는 사람 또는 지금 방장)
     */
    private Long pickHost(Long except) {
        return players.keySet().stream()
                .filter(id -> !id.equals(except))
                .filter(id -> !disconnected.contains(id))
                .findFirst()
                .orElseGet(() -> players.keySet().stream()
                        .filter(id -> !id.equals(except))
                        .findFirst()
                        .orElse(null));
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

    /**
     * 시작 전에 맵을 고른다 (2맵 준비).
     *
     * <p><b>시작한 뒤에는 바꿀 수 없다.</b> 판이 도는 중에 방의 스테이지가 바뀌면,
     * 그 뒤에 재입장한 사람만 다른 맵을 열게 된다 — 같은 방에 두 개의 맵이 생긴다.
     * 판이 시작되는 순간 스테이지는 {@link GameState} 로 넘어가 그 판의 사실이 되고,
     * 여기 값은 다음 판을 위한 설정으로만 남는다.
     *
     * @return 바꿨으면 {@code true}. 이미 시작했거나 값이 비었으면 {@code false}
     */
    public synchronized boolean setStageId(String stageId) {
        if (status != Status.WAITING || stageId == null || stageId.isBlank()) {
            return false;
        }
        this.stageId = stageId;
        return true;
    }

    public synchronized boolean start() {
        if (status != Status.WAITING || !allReady()) {
            return false;
        }
        status = Status.PLAYING;
        participants.addAll(players.keySet());
        gameState = new GameState(stageId);
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
