package com.metagame.backend.domain.leaderboard;

import com.metagame.backend.domain.leaderboard.dto.LeaderboardEntry;
import com.metagame.backend.domain.leaderboard.dto.SubmitRunRequest;
import com.metagame.backend.global.auth.JwtProvider;
import jakarta.validation.Valid;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

/**
 * 클리어 기록 리더보드 (S15P21B202-656).
 *
 * <p><b>올리는 데는 토큰이 필요하고 보는 데는 필요 없다.</b> 공개 서버라 인증 없이
 * 쓰기를 열면 아무나 표를 채울 수 있다 — {@code RunLogController} 가 같은 이유로
 * nginx 업로드를 택하지 않았다. 반대로 읽기는 막을 이유가 없고, 로비에서 로그인 전에
 * 보여 주려면 열려 있어야 한다.</p>
 */
@RestController
@RequestMapping("/leaderboard")
public class LeaderboardController {

    private static final Logger log = LoggerFactory.getLogger(LeaderboardController.class);

    /** 조회 기본 줄 수. 로비 표에 한 화면으로 들어가는 정도다. */
    private static final int DEFAULT_LIMIT = 10;

    private final JwtProvider jwtProvider;
    private final LeaderboardService service;

    public LeaderboardController(JwtProvider jwtProvider, LeaderboardService service) {
        this.jwtProvider = jwtProvider;
        this.service = service;
    }

    /**
     * 그 인원 표의 상위 기록. 빠른 순이다.
     *
     * <p><b>표가 넷이다</b> — 1인·2인·3인·4인. 게임이 인원으로 난이도를 조정하므로
     * (기획 §17.6) 인원이 다르면 애초에 다른 판이고, 한 표에 섞으면 비교가 성립하지
     * 않는다.</p>
     */
    @GetMapping
    public ResponseEntity<?> top(
            @RequestParam(defaultValue = "4") int crew,
            @RequestParam(defaultValue = "" + DEFAULT_LIMIT) int limit) {
        try {
            return ResponseEntity.ok(service.top(crew, limit));
        } catch (IllegalArgumentException e) {
            // 없는 표를 물었다. **빈 목록으로 답하지 않는다** — 그러면 오타를
            // "아직 기록이 없다" 로 읽게 된다.
            return ResponseEntity.badRequest().body(e.getMessage());
        }
    }

    /**
     * 클리어 한 판을 올린다.
     *
     * <p><b>호스트만 부른다.</b> 팀원이 각자 올리면 같은 판이 여러 줄이 되는데, 서버가
     * 그것을 묶을 방법이 없다 — 시각도 시간도 미세하게 다르다. 보내는 쪽을 하나로
     * 정하는 것이 유일하게 확실하다.</p>
     */
    @PostMapping
    public ResponseEntity<?> submit(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @Valid @RequestBody SubmitRunRequest request) {

        Long userId = userIdOf(authorization);
        if (userId == null) {
            // 무엇이 틀렸는지 자세히 말하지 않는다 — 밖에서 오는 요청이라 토큰 판정의
            // 단서를 주지 않는다(RunLogController 와 같은 판단).
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED).body("인증이 필요합니다.");
        }

        try {
            RunRecord saved = service.submit(userId, request);
            log.info("[leaderboard] 기록 user={} {}인 {}ms",
                    userId, saved.getCrewSize(), saved.getClearMillis());
            return ResponseEntity.status(HttpStatus.CREATED).body(saved.getId());
        } catch (LeaderboardService.TooManySubmitsException e) {
            return ResponseEntity.status(HttpStatus.TOO_MANY_REQUESTS).body(e.getMessage());
        } catch (IllegalArgumentException e) {
            // 값이 범위를 벗어났다. **이유는 알려 준다** — 우리 클라이언트가 잘못 보내는
            // 경우가 대부분이고, 그때 로그만 보고는 못 고친다.
            log.warn("[leaderboard] 거절 user={} : {}", userId, e.getMessage());
            return ResponseEntity.badRequest().body(e.getMessage());
        }
    }

    private Long userIdOf(String authorization) {
        if (authorization == null || !authorization.startsWith("Bearer ")) return null;
        try {
            return jwtProvider.parseUserId(authorization.substring(7).trim());
        } catch (Exception e) {
            return null;
        }
    }
}
