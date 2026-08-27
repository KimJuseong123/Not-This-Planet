package com.metagame.backend.domain.auth.dto;

public record GuestLoginResponse(
        Long userId,
        String nickname,
        String accessToken
) {
}
