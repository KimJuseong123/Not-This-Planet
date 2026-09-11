package com.metagame.backend.domain.room;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

/**
 * 방장 자리를 넘기는 규칙 (S15P21B202-389).
 *
 * <p><b>왜 따로 두나.</b> 방장의 PC 가 곧 게임 서버다({@code -145} 호스트 권위). 그래서
 * "누구를 방장으로 고르는가" 는 방 관리가 아니라 <b>누가 게임을 이어갈 수 있는가</b> 의
 * 문제다. 끊겨 유예를 기다리는 사람에게 넘기면 판이 그대로 멈춘다.
 *
 * <p>예전 {@code leave()} 는 {@code players.keySet().stream().findFirst()} 라 그것을
 * 가리지 않았다.
 */
class RoomHostPassTest {

    /**
     * 자리는 남기고 방장만 넘긴다.
     *
     * <p>끊긴 사람이 30초 안에 돌아올 수 있어야 하므로({@code -150}) 자리를 지우면 안 된다.
     * 하지만 방장 자리는 기다릴 수 없다.
     */
    @Test
    void passingHostKeepsEveryoneSeated() {
        Room room = playing();
        room.markDisconnected(1L);

        assertThat(room.passHostToConnected()).isEqualTo(2L);

        assertThat(room.getHostUserId()).isEqualTo(2L);
        assertThat(room.getPlayers()).containsOnlyKeys(1L, 2L, 3L);
        assertThat(room.isDisconnected(1L)).isTrue();
        assertThat(room.getStatus()).isEqualTo(Room.Status.PLAYING);
    }

    /** 끊긴 사람은 고르지 않는다 — 게임 서버가 될 수 없다. */
    @Test
    void aDisconnectedPlayerIsSkipped() {
        Room room = playing();
        room.markDisconnected(1L);
        room.markDisconnected(2L);

        assertThat(room.passHostToConnected()).isEqualTo(3L);
        assertThat(room.getHostUserId()).isEqualTo(3L);
    }

    /**
     * 넘길 사람이 없으면 방장은 그대로 남는다.
     *
     * <p>{@code null} 로 비우면 {@link Room#rejoin} 이 "방장이 없다" 를 보고 돌아온
     * 아무나 방장으로 만든다.
     */
    @Test
    void hostStaysWhenThereIsNobodyElse() {
        Room room = new Room("room-1", 1L, "철수");
        room.setReady(1L, true);
        assertThat(room.start()).isTrue();

        assertThat(room.passHostToConnected()).isNull();
        assertThat(room.getHostUserId()).isEqualTo(1L);
    }

    /** 아무도 붙어 있지 않으면 끊긴 사람이라도 고른다 — 방장이 비어 있는 것보다 낫다. */
    @Test
    void aDisconnectedPlayerIsBetterThanNoHost() {
        Room room = playing();
        room.markDisconnected(2L);
        room.markDisconnected(3L);

        Long next = room.passHostToConnected();

        assertThat(next).isIn(2L, 3L);
        assertThat(room.getHostUserId()).isEqualTo(next);
    }

    /** 나가는 경로도 같은 규칙을 쓴다 — 끊긴 사람에게 방장을 떠넘기지 않는다. */
    @Test
    void leavingHostSkipsDisconnectedPlayersToo() {
        Room room = playing();
        room.markDisconnected(2L);

        room.leave(1L);

        assertThat(room.getHostUserId()).isEqualTo(3L);
        assertThat(room.getPlayers()).containsOnlyKeys(2L, 3L);
    }

    /** 아무도 남지 않으면 방장이 비고, 그것이 "이어받을 사람이 없다" 의 표시가 된다. */
    @Test
    void theLastPlayerLeavingEmptiesTheHost() {
        Room room = new Room("room-1", 1L, "철수");
        room.setReady(1L, true);
        assertThat(room.start()).isTrue();

        room.leave(1L);

        assertThat(room.getHostUserId()).isNull();
    }

    private Room playing() {
        Room room = new Room("room-1", 1L, "철수");
        assertThat(room.join(2L, "영희")).isTrue();
        assertThat(room.join(3L, "민수")).isTrue();
        room.setReady(1L, true);
        room.setReady(2L, true);
        room.setReady(3L, true);
        assertThat(room.start()).isTrue();
        return room;
    }
}
