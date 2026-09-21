package com.metagame.backend.domain.leaderboard.dto;

import jakarta.validation.constraints.NotEmpty;
import jakarta.validation.constraints.Positive;
import jakarta.validation.constraints.Size;
import java.util.List;

/**
 * 클리어 한 판을 올린다 (S15P21B202-656).
 *
 * <p><b>모드를 보내지 않는다.</b> 닉네임 수가 곧 인원이고 인원이 곧 모드다
 * ({@link com.metagame.backend.domain.leaderboard.RunMode#ofCrew}). 따로 받으면
 * 둘이 어긋날 수 있고, 그때 무엇을 믿을지 정하는 규칙이 또 필요해진다.</p>
 */
public record SubmitRunRequest(

        /** 클리어에 걸린 시간(밀리초). 범위 검사는 서비스가 한다. */
        @Positive long clearMillis,

        /**
         * 함께한 사람의 닉네임. 순서는 표에 보이는 순서다.
         *
         * <p>{@code @Size} 로 개수만 막고 내용은 서비스가 다듬는다 — 제어문자나 구분자가
         * 섞인 닉네임 하나 때문에 판 전체를 거절하면 안 된다.</p>
         */
        @NotEmpty @Size(max = 4) List<@Size(max = 20) String> crewNames,

        /** 어느 빌드에서 나왔는가. 없어도 받는다 — 옛 빌드가 보낼 수 있다. */
        @Size(max = 80) String build
) {
}
