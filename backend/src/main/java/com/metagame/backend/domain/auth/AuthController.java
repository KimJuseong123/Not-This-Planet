package com.metagame.backend.domain.auth;

import com.metagame.backend.domain.auth.dto.GuestLoginRequest;
import com.metagame.backend.domain.auth.dto.GuestLoginResponse;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/auth")
@RequiredArgsConstructor
public class AuthController {

    private final AuthService authService;

    @PostMapping("/guest")
    public GuestLoginResponse guestLogin(@Valid @RequestBody GuestLoginRequest request) {
        return authService.guestLogin(request);
    }
}
