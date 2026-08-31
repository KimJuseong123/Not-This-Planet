package com.metagame.backend.domain.room;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

/**
 * 연결 끊김·복귀 규칙 (S15P21B202-48).
 * 유예를 넘겨 제외되어도 게임은 계속되고, 그 판에 참가했던 유저만 다시 들어올 수 있다.
 */
class RoomReconnectTest {

    private Room playingRoomWith(Long... userIds) {
        Room room = new Room("ROOM01", userIds[0], "유저" + userIds[0]);
        for (int i = 1; i < userIds.length; i++) {
            room.join(userIds[i], "유저" + userIds[i]);
        }
        for (Long id : userIds) {
            room.setReady(id, true);
        }
        room.start();
        return room;
    }

    @Test
    void disconnectedPlayerKeepsSeatUntilRemoved() {
        Room room = playingRoomWith(1L, 2L);

        room.markDisconnected(2L);

        assertThat(room.isDisconnected(2L)).isTrue();
        assertThat(room.getPlayers()).containsOnlyKeys(1L, 2L);
        assertThat(room.getStatus()).isEqualTo(Room.Status.PLAYING);
    }

    @Test
    void gameKeepsRunningAfterRemovedPlayerLeaves() {
        Room room = playingRoomWith(1L, 2L);
        room.markDisconnected(2L);

        room.leave(2L);

        assertThat(room.getPlayers()).containsOnlyKeys(1L);
        assertThat(room.isDisconnected(2L)).isFalse();
        assertThat(room.getStatus()).isEqualTo(Room.Status.PLAYING);
        assertThat(room.isEmpty()).isFalse();
    }

    @Test
    void participantCanRejoinWhileStillWithinGrace() {
        Room room = playingRoomWith(1L, 2L);
        room.markDisconnected(2L);

        assertThat(room.rejoin(2L, "복귀자")).isEqualTo(Room.RejoinResult.OK);
        assertThat(room.isDisconnected(2L)).isFalse();
        assertThat(room.getPlayers()).containsOnlyKeys(1L, 2L);
    }

    @Test
    void participantCanRejoinEvenAfterBeingRemoved() {
        Room room = playingRoomWith(1L, 2L);
        room.leave(2L);
        assertThat(room.getPlayers()).containsOnlyKeys(1L);

        assertThat(room.rejoin(2L, "복귀자")).isEqualTo(Room.RejoinResult.OK);
        assertThat(room.getPlayers()).containsOnlyKeys(1L, 2L);
    }

    @Test
    void outsiderCannotJoinGameInProgress() {
        Room room = playingRoomWith(1L, 2L);

        assertThat(room.rejoin(99L, "복귀자")).isEqualTo(Room.RejoinResult.DENIED);
        assertThat(room.join(99L, "난입자")).isFalse();
        assertThat(room.getPlayers()).containsOnlyKeys(1L, 2L);
    }

    /**
     * 참가자 전원이 나갔다 돌아와도 정원을 넘지 않는다.
     * 복귀는 그 판의 참가자에게만 허용되고 참가자 수는 시작 시점에 고정되므로,
     * 복귀로 정원이 초과되는 상황은 현재 규칙에서 발생하지 않는다
     * (RejoinResult.FULL 은 규칙이 바뀔 때를 대비한 방어 분기).
     */
    @Test
    void rejoinNeverPushesRoomOverCapacity() {
        Room room = playingRoomWith(1L, 2L, 3L, 4L);

        for (long id = 1; id <= 4; id++) {
            room.leave(id);
        }
        for (long id = 1; id <= 4; id++) {
            assertThat(room.rejoin(id, "복귀자")).isEqualTo(Room.RejoinResult.OK);
        }

        assertThat(room.getPlayers()).hasSize(Room.MAX_PLAYERS);
    }

    @Test
    void rejoiningPlayerBecomesHostWhenEveryoneElseLeft() {
        Room room = playingRoomWith(1L, 2L);
        room.leave(1L);
        room.leave(2L);
        assertThat(room.getHostUserId()).isNull();

        assertThat(room.rejoin(2L, "복귀자")).isEqualTo(Room.RejoinResult.OK);
        assertThat(room.getHostUserId()).isEqualTo(2L);
    }

    @Test
    void rejoinIsDeniedWhileRoomIsStillWaiting() {
        Room room = new Room("ROOM01", 1L, "철수");
        room.join(2L, "영희");

        assertThat(room.rejoin(2L, "복귀자")).isEqualTo(Room.RejoinResult.DENIED);
    }

    @Test
    void markingUnknownUserAsDisconnectedDoesNothing() {
        Room room = playingRoomWith(1L, 2L);

        room.markDisconnected(99L);

        assertThat(room.isDisconnected(99L)).isFalse();
        assertThat(room.getDisconnected()).isEmpty();
    }

    @Test
    void everyPlayerAtStartIsRecordedAsParticipant() {
        Room room = playingRoomWith(1L, 2L, 3L);

        assertThat(room.getParticipants()).containsExactlyInAnyOrder(1L, 2L, 3L);
        assertThat(room.getGameState().getPlayers()).containsOnlyKeys(1L, 2L, 3L);
    }
}
