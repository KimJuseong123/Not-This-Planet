package com.metagame.backend.domain.room;

import lombok.Getter;

/**
 * 방에 들어와 있는 한 사람. 로비 화면에 이름과 준비 상태를 띄우기 위해 함께 담는다.
 * 닉네임은 WebSocket 연결 시 한 번 조회한 값을 그대로 들고 있는다 (브로드캐스트마다 DB를 보지 않는다).
 */
@Getter
public class RoomPlayer {

    private final String nickname;
    private volatile boolean ready;

    public RoomPlayer(String nickname) {
        this.nickname = nickname;
    }

    void setReady(boolean ready) {
        this.ready = ready;
    }
}
