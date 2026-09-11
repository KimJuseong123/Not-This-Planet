package com.metagame.backend.domain.auth;

import static org.assertj.core.api.Assertions.assertThat;

import com.metagame.backend.domain.auth.dto.MatchVerifyRequest;
import com.metagame.backend.domain.auth.dto.MatchVerifyResponse;
import com.metagame.backend.domain.room.Room;
import com.metagame.backend.domain.room.RoomManager;
import com.metagame.backend.global.auth.JwtProvider;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.transaction.annotation.Transactional;

/**
 * 게임 서버가 접속해 온 플레이어를 확인하는 경로.
 * 토큰만 맞으면 남의 방에 들어갈 수 있으므로 방 참가자인지까지 본다.
 */
@SpringBootTest
@Transactional
class MatchAuthServiceTest {

    @Autowired
    private MatchAuthService matchAuthService;

    @Autowired
    private RoomManager roomManager;

    @Autowired
    private JwtProvider jwtProvider;

    @Test
    void roomMemberIsVerifiedWithNickname() {
        Room room = roomManager.createRoom(1L, "철수");
        String token = jwtProvider.createToken(1L);

        MatchVerifyResponse verified =
                matchAuthService.verify(new MatchVerifyRequest(token, room.getRoomId()));

        assertThat(verified).isNotNull();
        assertThat(verified.userId()).isEqualTo(1L);
        assertThat(verified.nickname()).isEqualTo("철수");
    }

    @Test
    void joinedPlayerIsAlsoVerified() {
        Room room = roomManager.createRoom(1L, "철수");
        room.join(2L, "영희");

        MatchVerifyResponse verified = matchAuthService.verify(
                new MatchVerifyRequest(jwtProvider.createToken(2L), room.getRoomId()));

        assertThat(verified).isNotNull();
        assertThat(verified.nickname()).isEqualTo("영희");
    }

    @Test
    void tokenOfSomeoneNotInThatRoomIsRejected() {
        Room room = roomManager.createRoom(1L, "철수");
        String outsiderToken = jwtProvider.createToken(99L);

        assertThat(matchAuthService.verify(new MatchVerifyRequest(outsiderToken, room.getRoomId())))
                .isNull();
    }

    @Test
    void unknownRoomIsRejected() {
        String token = jwtProvider.createToken(1L);

        assertThat(matchAuthService.verify(new MatchVerifyRequest(token, "NOPE12")))
                .isNull();
    }

    @Test
    void brokenTokenIsRejected() {
        Room room = roomManager.createRoom(1L, "철수");

        assertThat(matchAuthService.verify(new MatchVerifyRequest("이건-토큰이-아니다", room.getRoomId())))
                .isNull();
    }
}
