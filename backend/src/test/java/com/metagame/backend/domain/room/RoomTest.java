package com.metagame.backend.domain.room;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

class RoomTest {

    @Test
    void roomStartsOnlyWhenEveryPlayerIsReady() {
        Room room = new Room("room-1", 1L);
        assertThat(room.start()).isFalse();

        room.setReady(1L, true);
        assertThat(room.start()).isTrue();
        assertThat(room.getStatus()).isEqualTo(Room.Status.PLAYING);
        assertThat(room.getGameState()).isNotNull();
    }

    @Test
    void playerCannotJoinAfterRoomStarts() {
        Room room = new Room("room-1", 1L);
        room.setReady(1L, true);
        assertThat(room.start()).isTrue();

        assertThat(room.join(2L)).isFalse();
        assertThat(room.getPlayers()).containsOnlyKeys(1L);
    }

    @Test
    void leavingHostDelegatesHostToRemainingPlayer() {
        Room room = new Room("room-1", 1L);
        assertThat(room.join(2L)).isTrue();

        room.leave(1L);

        assertThat(room.getHostUserId()).isEqualTo(2L);
        assertThat(room.getPlayers()).containsOnlyKeys(2L);
    }
}
