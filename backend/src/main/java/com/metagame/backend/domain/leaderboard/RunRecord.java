package com.metagame.backend.domain.leaderboard;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Index;
import jakarta.persistence.Table;
import java.time.LocalDateTime;
import lombok.AccessLevel;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

/**
 * 클리어 한 판의 기록 (S15P21B202-656).
 *
 * <p><b>한 판이 한 줄이다.</b> 협동으로 넷이 끝내도 줄은 하나이고 닉네임이 나열된다.
 * 사람마다 한 줄로 남기면 같은 판이 표를 네 칸 차지해, 1~4위가 전부 같은 기록인
 * 리더보드가 된다.</p>
 *
 * <p><b>그래서 호스트만 보낸다.</b> 팀원 각자가 올리면 같은 판이 여러 줄이 되는데,
 * 서버가 그것을 "같은 판" 으로 묶을 방법이 없다 — 시각과 시간이 미세하게 다르다.
 * 보내는 쪽을 하나로 정하는 것이 유일하게 확실한 방법이다.</p>
 */
@Entity
@Table(
        name = "run_records",
        indexes = {
            // 리더보드 질의가 언제나 "모드로 거르고 시간으로 정렬" 이다.
            // 복합 인덱스가 없으면 판이 쌓일수록 전체 정렬이 된다.
            @Index(name = "idx_run_records_mode_millis", columnList = "mode, clearMillis")
        })
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class RunRecord {

    /** 닉네임 나열의 최대 길이. 4인 × 닉네임 20자 + 구분자. */
    public static final int MAX_CREW_TEXT = 100;

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 8)
    private RunMode mode;

    /**
     * 클리어에 걸린 시간(밀리초). <b>작을수록 위</b>다.
     *
     * <p>초가 아니라 밀리초로 담는 이유는 동률이다. 초로 자르면 4분 12초짜리가 여럿
     * 나오고, 그때 순위가 저장 순서로 갈린다.</p>
     */
    @Column(nullable = false)
    private long clearMillis;

    /** 함께한 인원(1~4). 모드를 정하고, 표에 "(3인)" 으로 보여 준다. */
    @Column(nullable = false)
    private int crewSize;

    /**
     * 닉네임 나열. 표에 그대로 찍는 문자열이다.
     *
     * <p><b>테이블을 따로 파지 않았다.</b> 정규화하면 <c>run_record_players</c> 가 생기고
     * 조회마다 조인이 붙는데, 이 값으로 하는 일이 "그대로 보여 주기" 하나뿐이다.
     * 사람별 통계를 내게 되면 그때 나눈다.</p>
     */
    @Column(nullable = false, length = MAX_CREW_TEXT)
    private String crewNames;

    /** 보낸 사람. 같은 기기가 반복해 넣는 것을 재는 데 쓴다. */
    @Column(nullable = false)
    private Long submitterUserId;

    /** 어느 빌드에서 나온 기록인가. 규칙이 바뀌면 옛 기록과 섞이면 안 된다. */
    @Column(length = 80)
    private String build;

    @Column(nullable = false)
    private LocalDateTime createdAt;

    @Builder
    private RunRecord(RunMode mode, long clearMillis, int crewSize, String crewNames,
                      Long submitterUserId, String build) {
        this.mode = mode;
        this.clearMillis = clearMillis;
        this.crewSize = crewSize;
        this.crewNames = crewNames;
        this.submitterUserId = submitterUserId;
        this.build = build;
        this.createdAt = LocalDateTime.now();
    }
}
