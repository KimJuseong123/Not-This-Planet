package com.metagame.backend.domain.leaderboard;

import com.metagame.backend.domain.leaderboard.dto.LeaderboardEntry;
import com.metagame.backend.domain.leaderboard.dto.SubmitRunRequest;
import java.time.Duration;
import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.List;
import org.springframework.data.domain.PageRequest;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * 클리어 기록을 받고 돌려준다 (S15P21B202-656).
 *
 * <p><b>클라이언트가 보낸 값을 믿지 않는다.</b> 공개 서버이고 본문은 누구나 만들 수
 * 있다 — 0초 클리어나 99명짜리 기록이 표 맨 위에 박히면 리더보드가 죽는다. 검사를
 * 컨트롤러가 아니라 여기 두는 이유는 <b>검증이 닿게</b> 하기 위해서다. 컨트롤러
 * 안의 if 는 HTTP 를 세워야 시험할 수 있다.</p>
 */
@Service
public class LeaderboardService {

    /**
     * 인정하는 가장 빠른 클리어(밀리초).
     *
     * <p>기획 §6.1 의 입장 연출만 15~20초이고 §6.2 의 학습 흐름(고정못 뽑기 → 15m
     * 운반 → 조립)이 뒤따른다. 30초는 <b>물리적으로 불가능한 선</b>이지 목표 기록이
     * 아니다 — 잘한 판을 걸러내지 않도록 넉넉히 아래에 둔다.</p>
     */
    public static final long MIN_CLEAR_MILLIS = 30_000L;

    /**
     * 인정하는 가장 느린 클리어(밀리초). 세 시간.
     *
     * <p>이 값이 필요한 이유는 <b>기록이 아니라 자료형</b>이다. 상한이 없으면 실수나
     * 장난으로 들어온 거대한 값이 그대로 저장되고, 표 맨 아래에 "9999시간" 이 남는다.</p>
     */
    public static final long MAX_CLEAR_MILLIS = 3 * 60 * 60 * 1000L;

    /** 표에 한 번에 보여 주는 줄 수의 상한. */
    public static final int MAX_LIMIT = 50;

    /** 한 사람이 이 시간 안에 넣을 수 있는 판 수. */
    public static final int MAX_SUBMITS_PER_WINDOW = 20;

    public static final Duration SUBMIT_WINDOW = Duration.ofHours(1);

    /** 닉네임 사이 구분자. 표에 그대로 보인다. */
    public static final String NAME_SEPARATOR = " · ";

    private final RunRecordRepository repository;

    public LeaderboardService(RunRecordRepository repository) {
        this.repository = repository;
    }

    /**
     * 한 판을 기록한다.
     *
     * @throws IllegalArgumentException 값이 범위를 벗어나면
     * @throws TooManySubmitsException 같은 사람이 너무 자주 넣으면
     */
    @Transactional
    public RunRecord submit(Long userId, SubmitRunRequest request) {
        long millis = request.clearMillis();
        if (millis < MIN_CLEAR_MILLIS || millis > MAX_CLEAR_MILLIS) {
            throw new IllegalArgumentException(
                    "클리어 시간이 범위를 벗어납니다: " + millis + "ms");
        }

        List<String> names = cleanNames(request.crewNames());
        if (names.isEmpty()) {
            throw new IllegalArgumentException("닉네임이 하나도 없습니다.");
        }

        // **인원 수는 닉네임 수로 정한다.** 따로 보내게 하면 둘이 어긋날 수 있고,
        // 그때 무엇을 믿을지 정하는 규칙이 또 필요해진다. 보내는 값을 하나로 줄인다.
        RunMode mode = RunMode.ofCrew(names.size());

        long recent = repository.countBySubmitterUserIdAndCreatedAtAfter(
                userId, LocalDateTime.now().minus(SUBMIT_WINDOW));
        if (recent >= MAX_SUBMITS_PER_WINDOW) {
            throw new TooManySubmitsException(
                    "최근 " + SUBMIT_WINDOW.toHours() + "시간 동안 너무 많이 올렸습니다.");
        }

        return repository.save(RunRecord.builder()
                .mode(mode)
                .clearMillis(millis)
                .crewSize(names.size())
                .crewNames(String.join(NAME_SEPARATOR, names))
                .submitterUserId(userId)
                .build(trim(request.build(), 80))
                .build());
    }

    /** 그 판의 상위 기록. 1위부터 번호를 매겨 돌려준다. */
    @Transactional(readOnly = true)
    public List<LeaderboardEntry> top(RunMode mode, int limit) {
        int size = Math.max(1, Math.min(limit, MAX_LIMIT));
        List<RunRecord> records =
                repository.findByModeOrderByClearMillisAscCreatedAtAsc(mode, PageRequest.of(0, size));

        List<LeaderboardEntry> entries = new ArrayList<>(records.size());
        for (int i = 0; i < records.size(); i++) {
            RunRecord r = records.get(i);
            entries.add(new LeaderboardEntry(
                    i + 1, r.getCrewNames(), r.getClearMillis(), r.getCrewSize(), r.getCreatedAt()));
        }
        return entries;
    }

    /**
     * 닉네임 목록을 쓸 수 있는 꼴로 만든다.
     *
     * <p><b>구분자를 이름에서 없앤다.</b> 저장은 한 문자열이므로, 닉네임 안에
     * {@link #NAME_SEPARATOR} 가 들어 있으면 읽는 쪽에서 사람이 하나 더 있는 것처럼
     * 보인다. 제어문자도 같은 이유로 턴다 — 표가 깨진다.</p>
     *
     * <p><b>버리지 않고 다듬는다.</b> 이상한 닉네임은 이상한 이름이지 없는 이름이
     * 아니다. 통째로 거절하면 팀원 하나의 닉네임 때문에 판 전체가 사라진다.</p>
     */
    static List<String> cleanNames(List<String> raw) {
        List<String> out = new ArrayList<>();
        if (raw == null) return out;

        for (String name : raw) {
            if (name == null) continue;
            String cleaned = name.replaceAll("[\\p{Cntrl}]", "")
                                 .replace(NAME_SEPARATOR.trim(), "")
                                 .trim();
            if (cleaned.isEmpty()) continue;
            out.add(trim(cleaned, 20));
            if (out.size() >= RunMode.MAX_CREW) break;
        }

        // 나열 전체가 칸을 넘지 않는지 마지막으로 본다. 넘으면 뒤에서 덜어낸다 —
        // 저장에서 잘리면 DataException 으로 판이 통째로 사라진다.
        while (out.size() > 1
                && String.join(NAME_SEPARATOR, out).length() > RunRecord.MAX_CREW_TEXT) {
            out.remove(out.size() - 1);
        }
        return out;
    }

    static String trim(String value, int max) {
        if (value == null) return null;
        String t = value.replaceAll("[\\p{Cntrl}]", "").trim();
        return t.length() > max ? t.substring(0, max) : t;
    }

    /** 도배를 막았다는 뜻. 컨트롤러가 429 로 옮긴다. */
    public static class TooManySubmitsException extends RuntimeException {
        public TooManySubmitsException(String message) {
            super(message);
        }
    }
}
