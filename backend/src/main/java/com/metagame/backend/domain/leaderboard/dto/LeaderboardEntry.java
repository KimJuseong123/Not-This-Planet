package com.metagame.backend.domain.leaderboard.dto;

import java.time.LocalDateTime;

/**
 * 리더보드 한 줄 (S15P21B202-656).
 *
 * <p><b>순위를 서버가 매겨 보낸다.</b> 클라이언트가 목록 인덱스로 세면 되긴 하지만,
 * 나중에 동률 처리(공동 3위)를 넣을 때 화면마다 다르게 세는 일이 생긴다.</p>
 */
public record LeaderboardEntry(

        /** 1부터. */
        int rank,

        /** 표에 그대로 찍는 닉네임 나열. {@code 재현 · 수호 · 주성} */
        String crewNames,

        /** 클리어 시간(밀리초). 화면에 맞춰 분·초로 바꾸는 것은 클라이언트가 한다. */
        long clearMillis,

        /** 함께한 인원(1~4). */
        int crewSize,

        LocalDateTime achievedAt
) {
}
