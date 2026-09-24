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

    // ---------- AI 자리 (S15P21B202-713) ----------

    /** 범위를 넘는 값은 거절하지 않고 빈 자리 수로 줄인다. 방장 혼자면 AI 는 셋까지다. */
    @Test
    void aiCountIsClampedToTheFreeSeats() {
        Room room = new Room("room-1", 1L, "철수");

        assertThat(room.setAiCount(1L, 9)).isEqualTo(Room.AiCountResult.OK);
        assertThat(room.getAiCount()).isEqualTo(3);

        assertThat(room.setAiCount(1L, -2)).isEqualTo(Room.AiCountResult.OK);
        assertThat(room.getAiCount()).isZero();

        room.join(2L, "영희");
        assertThat(room.setAiCount(1L, 9)).isEqualTo(Room.AiCountResult.OK);
        assertThat(room.getAiCount()).isEqualTo(2);
    }

    @Test
    void onlyTheHostSetsTheAiCount() {
        Room room = new Room("room-1", 1L, "철수");
        room.join(2L, "영희");

        assertThat(room.setAiCount(2L, 1)).isEqualTo(Room.AiCountResult.NOT_HOST);
        assertThat(room.setAiCount(null, 1)).isEqualTo(Room.AiCountResult.NOT_HOST);
        assertThat(room.getAiCount()).isZero();
    }

    /** 판 중에는 바꾸지 못한다 — 그래야 모든 PC 가 판 내내 같은 수를 들고 있다. */
    @Test
    void aiCountCannotChangeWhilePlaying() {
        Room room = new Room("room-1", 1L, "철수");
        room.setAiCount(1L, 2);
        room.setReady(1L, true);
        assertThat(room.start()).isTrue();

        assertThat(room.setAiCount(1L, 1)).isEqualTo(Room.AiCountResult.NOT_WAITING);
        assertThat(room.getAiCount()).isEqualTo(2);
    }

    /** 사람 1 + AI 3 에 사람이 들어오면 입장은 되고 AI 하나가 비켜 준다. */
    @Test
    void aJoiningHumanTakesAnAiSeat() {
        Room room = new Room("room-1", 1L, "철수");
        room.setAiCount(1L, 3);

        assertThat(room.tryJoin(2L, "영희")).isEqualTo(Room.JoinResult.OK);

        assertThat(room.getAiCount()).isEqualTo(2);
        assertThat(room.getPlayers()).containsOnlyKeys(1L, 2L);
    }

    /** 자리가 남으면 AI 는 그대로다 — 넘칠 때만 비켜 준다. */
    @Test
    void aJoiningHumanLeavesTheAiAloneWhileSeatsRemain() {
        Room room = new Room("room-1", 1L, "철수");
        room.setAiCount(1L, 1);

        assertThat(room.tryJoin(2L, "영희")).isEqualTo(Room.JoinResult.OK);

        assertThat(room.getAiCount()).isEqualTo(1);
    }

    /** 정원에는 사람만 센다. AI 가 있어도 사람 넷까지 들어오고, 다섯째에서 막힌다. */
    @Test
    void aiNeverCountsTowardRoomFull() {
        Room room = new Room("room-1", 1L, "철수");
        room.setAiCount(1L, 3);

        for (long id = 2; id <= Room.MAX_PLAYERS; id++) {
            assertThat(room.tryJoin(id, "유저" + id)).isEqualTo(Room.JoinResult.OK);
        }
        assertThat(room.getAiCount()).isZero();

        assertThat(room.tryJoin(99L, "다섯째")).isEqualTo(Room.JoinResult.ROOM_FULL);
    }

    /** 줄인 값은 남는다. 들어왔던 사람이 나가도 AI 가 되살아나지 않는다. */
    @Test
    void aLeavingHumanDoesNotBringTheAiBack() {
        Room room = new Room("room-1", 1L, "철수");
        room.setAiCount(1L, 3);
        room.tryJoin(2L, "영희");

        room.leave(2L);

        assertThat(room.getAiCount()).isEqualTo(2);
    }

    @Test
    void hostKicksAPlayerOut() {
        Room room = new Room("room-1", 1L, "철수");
        room.join(2L, "영희");
        room.setAiCount(1L, 1);

        assertThat(room.kick(1L, 2L)).isEqualTo(Room.KickResult.OK);

        assertThat(room.getPlayers()).containsOnlyKeys(1L);
        assertThat(room.getHostUserId()).isEqualTo(1L);
        assertThat(room.getAiCount()).as("사람이 나가도 AI 수는 그대로다").isEqualTo(1);
    }

    @Test
    void kickIsRefusedForEveryoneButTheWaitingHost() {
        Room room = new Room("room-1", 1L, "철수");
        room.join(2L, "영희");
        room.join(3L, "민수");

        assertThat(room.kick(2L, 3L)).isEqualTo(Room.KickResult.NOT_HOST);
        assertThat(room.kick(1L, 1L)).isEqualTo(Room.KickResult.SELF);
        assertThat(room.kick(1L, 99L)).isEqualTo(Room.KickResult.NOT_IN_ROOM);
        assertThat(room.getPlayers()).containsOnlyKeys(1L, 2L, 3L);

        room.setReady(1L, true);
        room.setReady(2L, true);
        room.setReady(3L, true);
        assertThat(room.start()).isTrue();
        assertThat(room.kick(1L, 2L)).isEqualTo(Room.KickResult.NOT_WAITING);
        assertThat(room.getPlayers()).containsOnlyKeys(1L, 2L, 3L);
    }

    /** 지난 판의 끊김 표시도 지운다 — 유예 만료가 이미 없는 사람을 다시 찾지 않게. */
    @Test
    void kickClearsALeftoverDisconnectMark() {
        Room room = new Room("room-1", 1L, "철수");
        room.join(2L, "영희");
        room.setReady(1L, true);
        room.setReady(2L, true);
        room.start();
        room.markDisconnected(2L);
        room.end();

        assertThat(room.kick(1L, 2L)).isEqualTo(Room.KickResult.OK);

        assertThat(room.isDisconnected(2L)).isFalse();
    }
}
