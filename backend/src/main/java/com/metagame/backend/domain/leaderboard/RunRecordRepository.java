package com.metagame.backend.domain.leaderboard;

import java.time.LocalDateTime;
import java.util.List;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;

public interface RunRecordRepository extends JpaRepository<RunRecord, Long> {

    /** 그 판의 상위 기록. 빠른 순이고, 같으면 먼저 세운 쪽이 위다. */
    List<RunRecord> findByModeOrderByClearMillisAscCreatedAtAsc(RunMode mode, Pageable pageable);

    /** 이 사람이 최근에 몇 판을 넣었는가. 도배를 막는 데 쓴다. */
    long countBySubmitterUserIdAndCreatedAtAfter(Long submitterUserId, LocalDateTime after);
}
