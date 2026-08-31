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
}
