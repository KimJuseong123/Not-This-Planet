package com.metagame.backend.domain.auth;

import com.metagame.backend.domain.auth.dto.GuestLoginRequest;
import com.metagame.backend.domain.auth.dto.GuestLoginResponse;
import com.metagame.backend.domain.user.User;
import com.metagame.backend.domain.user.UserRepository;
import com.metagame.backend.global.auth.JwtProvider;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
@RequiredArgsConstructor
public class AuthService {

    private final UserRepository userRepository;
    private final JwtProvider jwtProvider;

    /**
     * 게스트 로그인: deviceId로 기존 유저를 찾고, 없으면 새로 만든다.
     * ID/비밀번호 없음. Steam 배포 시 /auth/steam 엔드포인트만 추가하면 되는 구조.
     */
    @Transactional
    public GuestLoginResponse guestLogin(GuestLoginRequest request) {
        User user = userRepository.findByDeviceId(request.deviceId())
                .orElseGet(() -> userRepository.save(User.builder()
                        .nickname(request.nickname())
                        .deviceId(request.deviceId())
                        .build()));
        user.login(request.nickname());

        String token = jwtProvider.createToken(user.getId());
        return new GuestLoginResponse(user.getId(), user.getNickname(), token);
    }
}
