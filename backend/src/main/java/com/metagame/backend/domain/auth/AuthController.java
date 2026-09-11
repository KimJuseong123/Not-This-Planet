package com.metagame.backend.domain.auth;

import com.metagame.backend.domain.auth.dto.GuestLoginRequest;
import com.metagame.backend.domain.auth.dto.GuestLoginResponse;
import com.metagame.backend.domain.auth.dto.MatchVerifyRequest;
import com.metagame.backend.domain.auth.dto.MatchVerifyResponse;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/auth")
@RequiredArgsConstructor
public class AuthController {

    private final AuthService authService;
    private final MatchAuthService matchAuthService;

    @PostMapping("/guest")
    public GuestLoginResponse guestLogin(@Valid @RequestBody GuestLoginRequest request) {
        return authService.guestLogin(request);
    }

    /**
     * 게임 서버가 접속해 온 플레이어를 확인한다. 사람이 부르는 API 가 아니다.
     * 통과하면 userId·닉네임, 아니면 401 — 사유는 구분하지 않는다.
     */
    @PostMapping("/verify")
    public ResponseEntity<MatchVerifyResponse> verifyMatchPlayer(
            @Valid @RequestBody MatchVerifyRequest request) {
        MatchVerifyResponse verified = matchAuthService.verify(request);
        return verified == null
                ? ResponseEntity.status(401).build()
                : ResponseEntity.ok(verified);
    }
}
