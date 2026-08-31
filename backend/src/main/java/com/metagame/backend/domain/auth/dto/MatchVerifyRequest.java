package com.metagame.backend.domain.auth.dto;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

/**
 * 게임 서버가 접속해 온 플레이어를 확인할 때 보내는 요청.
 * 플레이어가 로비에서 받은 토큰과, 들어가려는 방을 함께 보낸다.
 */
public record MatchVerifyRequest(
        @NotBlank String token,
        @NotBlank @Size(max = 16) String roomId
) {
}
