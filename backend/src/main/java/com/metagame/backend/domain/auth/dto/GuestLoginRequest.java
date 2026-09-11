package com.metagame.backend.domain.auth.dto;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

public record GuestLoginRequest(
        @NotBlank @Size(max = 12) String nickname,
        @NotBlank @Size(max = 64) String deviceId
) {
}
