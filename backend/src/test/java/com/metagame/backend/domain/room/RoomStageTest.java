package com.metagame.backend.domain.room;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

/**
 * 방이 어느 맵의 판인지를 서버가 안다 (2맵 준비).
 *
 * <p>맵이 하나뿐이던 동안에는 클라이언트가 씬 이름을 상수로 들고 있었다. 맵이 둘이
 * 되면 같은 방의 네 사람이 같은 맵을 여는 것을 보증할 수 있는 곳은 서버뿐이다.
 */
class RoomStageTest {

    @Test
    void roomStandsOnTheDefaultStageWhenNobodyChoosesOne() {
        Room room = new Room("room-1", 1L, "철수");

        assertThat(room.getStageId()).isEqualTo(Room.DEFAULT_STAGE_ID);
    }

    @Test
    void hostCanChooseTheStageBeforeTheGameStarts() {
        Room room = new Room("room-1", 1L, "철수");

        assertThat(room.setStageId("stage2")).isTrue();
        assertThat(room.getStageId()).isEqualTo("stage2");
    }

    @Test
    void blankStageIsRejectedSoTheRoomNeverLosesItsMap() {
        Room room = new Room("room-1", 1L, "철수");

        assertThat(room.setStageId(null)).isFalse();
        assertThat(room.setStageId("   ")).isFalse();
        assertThat(room.getStageId()).isEqualTo(Room.DEFAULT_STAGE_ID);
    }

    @Test
    void startedGameCarriesTheStageItBeganOn() {
        Room room = new Room("room-1", 1L, "철수");
        room.setStageId("stage2");
        room.setReady(1L, true);

        assertThat(room.start()).isTrue();
        assertThat(room.getGameState().getStageId()).isEqualTo("stage2");
    }

    /**
     * 진행 중에 맵을 바꿀 수 있으면, 그 뒤에 재입장한 사람만 다른 맵을 연다 —
     * 한 판에 두 개의 맵이 생긴다.
     */
    @Test
    void stageCannotChangeWhileTheGameIsRunning() {
        Room room = new Room("room-1", 1L, "철수");
        room.setReady(1L, true);
        room.start();

        assertThat(room.setStageId("stage2")).isFalse();
        assertThat(room.getStageId()).isEqualTo(Room.DEFAULT_STAGE_ID);
        assertThat(room.getGameState().getStageId()).isEqualTo(Room.DEFAULT_STAGE_ID);
    }

    /**
     * 판이 끝나 대기로 돌아오면 다음 판의 맵을 다시 고를 수 있다 (S15P21B202-316의
     * {@link Room#end()} 가 방을 WAITING 으로 되돌린다).
     */
    @Test
    void stageCanBeChosenAgainAfterTheRoomReturnsToWaiting() {
        Room room = new Room("room-1", 1L, "철수");
        room.setReady(1L, true);
        room.start();
        room.end();

        assertThat(room.setStageId("stage2")).isTrue();
        assertThat(room.getStageId()).isEqualTo("stage2");
    }
}
