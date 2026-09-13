package com.metagame.backend.domain.auth.dto;

/**
 * 구버전이라 로그인을 거절할 때 주는 본문 (S15P21B202-404).
 *
 * <p><b>받을 곳을 함께 준다.</b> 로그만 남기거나 "버전이 낮습니다" 로 끝내면 사용자는
 * 무엇을 해야 하는지 모른다. 클라이언트가 {@code message} 와 {@code downloadUrl} 을
 * 그대로 화면에 띄운다.
 *
 * <p>필드 이름은 유니티 {@code JsonUtility} 가 그대로 읽는다 — 평평한 구조여야 한다.
 */
public record ClientOutdatedResponse(
        String message,
        String downloadUrl,
        String minVersion
) {
}
