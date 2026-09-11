package com.metagame.backend.domain.room;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

class RoomTest {

    @Test
    void roomStartsOnlyWhenEveryPlayerIsReady() {
        Room room = new Room("room-1", 1L, "철수");
        assertThat(room.start()).isFalse();

        room.setReady(1L, true);
        assertThat(room.start()).isTrue();
        assertThat(room.getStatus()).isEqualTo(Room.Status.PLAYING);
        assertThat(room.getGameState()).isNotNull();
    }

    @Test
    void playerCannotJoinAfterRoomStarts() {
        Room room = new Room("room-1", 1L, "철수");
        room.setReady(1L, true);
        assertThat(room.start()).isTrue();

        assertThat(room.join(2L, "영희")).isFalse();
        assertThat(room.getPlayers()).containsOnlyKeys(1L);
    }

    @Test
    void playersCarryNicknameAndReadyState() {
        Room room = new Room("room-1", 1L, "철수");
        room.join(2L, "영희");

        room.setReady(2L, true);

        assertThat(room.getPlayers().get(1L).getNickname()).isEqualTo("철수");
        assertThat(room.getPlayers().get(1L).isReady()).isFalse();
        assertThat(room.getPlayers().get(2L).getNickname()).isEqualTo("영희");
        assertThat(room.getPlayers().get(2L).isReady()).isTrue();
    }

    @Test
    void rejoiningPlayerKeepsNicknameVisible() {
        Room room = new Room("room-1", 1L, "철수");
        room.join(2L, "영희");
        room.setReady(1L, true);
        room.setReady(2L, true);
        room.start();
        room.leave(2L);

        room.rejoin(2L, "영희");

        assertThat(room.getPlayers().get(2L).getNickname()).isEqualTo("영희");
    }

    @Test
    void leavingHostDelegatesHostToRemainingPlayer() {
        Room room = new Room("room-1", 1L, "철수");
        assertThat(room.join(2L, "영희")).isTrue();

        room.leave(1L);

        assertThat(room.getHostUserId()).isEqualTo(2L);
        assertThat(room.getPlayers()).containsOnlyKeys(2L);
    }

    // ---------- 판 종료 (S15P21B202-316) ----------

    @Test
    void endingGameReturnsRoomToWaitingAndClearsReady() {
        Room room = new Room("room-1", 1L, "철수");
        room.join(2L, "영희");
        room.setReady(1L, true);
        room.setReady(2L, true);
        assertThat(room.start()).isTrue();

        assertThat(room.end()).isTrue();

        assertThat(room.getStatus()).isEqualTo(Room.Status.WAITING);
        assertThat(room.getPlayers().get(1L).isReady()).isFalse();
        assertThat(room.getPlayers().get(2L).isReady()).isFalse();
        assertThat(room.getGameState()).isNull();
    }

    @Test
    void endingIsRejectedWhenRoomIsNotPlaying() {
        Room room = new Room("room-1", 1L, "철수");

        assertThat(room.end()).isFalse();
        assertThat(room.getStatus()).isEqualTo(Room.Status.WAITING);
    }

    @Test
    void playerCanJoinAfterGameEnds() {
        Room room = new Room("room-1", 1L, "철수");
        room.setReady(1L, true);
        room.start();
        room.end();

        assertThat(room.join(2L, "영희")).isTrue();
        assertThat(room.getPlayers()).containsOnlyKeys(1L, 2L);
    }

    @Test
    void roomCanStartAgainAfterGameEnds() {
        Room room = new Room("room-1", 1L, "철수");
        room.setReady(1L, true);
        room.start();
        room.end();

        // 준비가 풀렸으므로 곧바로는 시작되지 않는다
        assertThat(room.start()).isFalse();

        room.setReady(1L, true);
        assertThat(room.start()).isTrue();
        assertThat(room.getStatus()).isEqualTo(Room.Status.PLAYING);
    }

    /**
     * 판이 끝날 때 참가자 명단을 비우지 않으면, 지난 판에 있었던 사람이 참가하지도
     * 않은 다음 판에 복귀할 수 있다.
     */
    @Test
    void participantOfPreviousRoundCannotRejoinTheNextRound() {
        Room room = new Room("room-1", 1L, "철수");
        room.join(2L, "영희");
        room.setReady(1L, true);
        room.setReady(2L, true);
        room.start();
        room.end();

        // 영희는 로비에서 나가고, 방장 혼자 다음 판을 시작한다
        room.leave(2L);
        room.setReady(1L, true);
        assertThat(room.start()).isTrue();

        assertThat(room.rejoin(2L, "영희")).isEqualTo(Room.RejoinResult.DENIED);
    }
}
