package com.metagame.backend.domain.auth.dto;

import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

public record GuestLoginRequest(
        @NotBlank @Size(max = 12) String nickname,
        @NotBlank @Size(max = 64) String deviceId,

        /**
         * 클라이언트의 릴리스 버전({@code v1.0.0}) — 구버전 차단에 쓴다 (S15P21B202-404).
         *
         * <p><b>일부러 {@code @NotBlank} 를 붙이지 않는다.</b> 이 값을 안 보내는 것은
         * 스탬프가 없던 시절 빌드이고 그것이 바로 막아야 할 대상인데, 검증으로 막으면
         * 400 에 스프링 기본 본문이 나가 <b>사용자가 "받을 곳" 을 못 본다.</b>
         * {@link com.metagame.backend.domain.auth.ClientVersionGate} 가 판정하고
         * 받을 주소까지 담아 거절한다.
         */
        @Size(max = 32) String clientVersion
) {
    /** 버전을 보내지 않는 호출부(테스트·옛 클라이언트)를 위한 형태. */
    public GuestLoginRequest(String nickname, String deviceId) {
        this(nickname, deviceId, null);
    }
}
