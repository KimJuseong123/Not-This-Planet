package com.metagame.backend.domain.auth;

import static org.assertj.core.api.Assertions.assertThat;

import com.metagame.backend.domain.auth.dto.GuestLoginRequest;
import com.metagame.backend.domain.auth.dto.GuestLoginResponse;
import com.metagame.backend.domain.user.User;
import com.metagame.backend.domain.user.UserRepository;
import com.metagame.backend.global.auth.JwtProvider;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.transaction.annotation.Transactional;

@SpringBootTest
@Transactional
class AuthServiceTest {

    @Autowired
    private AuthService authService;

    @Autowired
    private UserRepository userRepository;

    @Autowired
    private JwtProvider jwtProvider;

    @Test
    void firstLoginCreatesUserAndIssuesUsableToken() {
        GuestLoginResponse response = authService.guestLogin(new GuestLoginRequest("철수", "device-a"));

        assertThat(response.userId()).isNotNull();
        assertThat(response.nickname()).isEqualTo("철수");
        assertThat(jwtProvider.parseUserId(response.accessToken())).isEqualTo(response.userId());
        assertThat(userRepository.findByDeviceId("device-a")).isPresent();
    }

    @Test
    void sameDeviceReturnsSameUserInsteadOfCreatingAnother() {
        GuestLoginResponse first = authService.guestLogin(new GuestLoginRequest("철수", "device-a"));
        GuestLoginResponse second = authService.guestLogin(new GuestLoginRequest("철수", "device-a"));

        assertThat(second.userId()).isEqualTo(first.userId());
        assertThat(userRepository.count()).isEqualTo(1);
    }

    @Test
    void loggingInAgainUpdatesNickname() {
        GuestLoginResponse first = authService.guestLogin(new GuestLoginRequest("철수", "device-a"));

        GuestLoginResponse renamed = authService.guestLogin(new GuestLoginRequest("영희", "device-a"));

        assertThat(renamed.userId()).isEqualTo(first.userId());
        assertThat(renamed.nickname()).isEqualTo("영희");
        assertThat(userRepository.findByDeviceId("device-a")).get()
                .extracting(User::getNickname).isEqualTo("영희");
    }

    @Test
    void differentDevicesGetDifferentUsers() {
        GuestLoginResponse a = authService.guestLogin(new GuestLoginRequest("철수", "device-a"));
        GuestLoginResponse b = authService.guestLogin(new GuestLoginRequest("영희", "device-b"));

        assertThat(b.userId()).isNotEqualTo(a.userId());
        assertThat(userRepository.count()).isEqualTo(2);
    }

    @Test
    void steamIdStaysNullUntilAccountIsPromoted() {
        GuestLoginResponse response = authService.guestLogin(new GuestLoginRequest("철수", "device-a"));

        assertThat(userRepository.findById(response.userId())).get()
                .extracting(User::getSteamId).isNull();
    }
}
