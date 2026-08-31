package com.metagame.backend.domain.auth.dto;

/**
 * 확인 결과. 게임 서버는 이 값을 그대로 그 연결의 신원으로 쓴다.
 * 닉네임을 함께 돌려주는 이유는, 게임 서버가 DB 를 보지 않게 하기 위해서다.
 */
public record MatchVerifyResponse(Long userId, String nickname) {
}
