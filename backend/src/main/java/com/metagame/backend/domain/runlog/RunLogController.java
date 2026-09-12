package com.metagame.backend.domain.runlog;

import com.metagame.backend.global.auth.JwtProvider;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 배포판이 보내는 플레이 로그를 받는다 (S15P21B202-443).
 *
 * <p><b>왜 필요한가.</b> 로그가 각자 기계에만 남으면, 배포한 뒤에는 버그 신고를 받아도
 * 고칠 수 없다 — 실유저는 로그를 찾아 보내 주지 않는다. 특히 <b>참가자 화면에서만
 * 나는 문제</b>는 그 사람의 로그가 있어야 판정된다.</p>
 *
 * <p><b>왜 nginx 업로드가 아니라 여기인가.</b> nginx 로 받으면 인증이 없어
 * <b>누구나 우리 서버에 파일을 부어넣을 수 있다.</b> 공개 서버이므로 택할 수 없다.
 * 여기서는 게스트 토큰을 요구한다.</p>
 *
 * <p><b>상한 없는 업로드 엔드포인트는 디스크를 채우는 길이다.</b> 그래서 크기와
 * 빈도를 둘 다 막고, 보관 기간이 지난 것은 회수한다.</p>
 */
@RestController
@RequestMapping("/logs")
public class RunLogController {

    private static final Logger log = LoggerFactory.getLogger(RunLogController.class);

    private final JwtProvider jwtProvider;
    private final RunLogStore store;

    public RunLogController(JwtProvider jwtProvider, RunLogStore store) {
        this.jwtProvider = jwtProvider;
        this.store = store;
    }

    /**
     * 로그 한 판을 받는다.
     *
     * <p>본문은 <b>평문 로그</b>다. JSON 으로 감싸지 않는 이유: 클라이언트가 파일을
     * 그대로 올리면 되고, 감싸면 8 MB 를 문자열로 이스케이프하느라 메모리를 두 배로
     * 쓴다. 부가 정보는 헤더로 온다.</p>
     */
    @PostMapping(consumes = "text/plain")
    public ResponseEntity<String> upload(
            @RequestHeader(value = "Authorization", required = false) String authorization,
            @RequestHeader(value = "X-Run-File", required = false) String fileName,
            @RequestHeader(value = "X-Run-Build", required = false) String build,
            @RequestHeader(value = "X-Run-Backend", required = false) String backend,
            @RequestBody byte[] body) {

        Long userId = userIdOf(authorization);
        if (userId == null) {
            // 토큰이 없거나 못 읽는다. **무엇이 틀렸는지 자세히 말하지 않는다** —
            // 밖에서 오는 요청이라 토큰 판정의 단서를 주지 않는다.
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED).body("인증이 필요합니다.");
        }

        if (body == null || body.length == 0) {
            return ResponseEntity.badRequest().body("본문이 비어 있습니다.");
        }
        if (body.length > RunLogStore.MAX_BYTES) {
            // 클라이언트가 이미 잘라서 보내므로 여기 걸리는 것은 옛 빌드이거나 우리가
            // 만든 것이 아닌 요청이다.
            return ResponseEntity.status(HttpStatus.PAYLOAD_TOO_LARGE)
                    .body("로그가 너무 큽니다.");
        }

        // **상한 셋을 한 자리에서 본다** (S15P21B202-467).
        //
        // 사용자별 상한만으로는 아무것도 막지 못한다 — `/auth/guest` 가 `permitAll` 이라
        // **토큰을 새로 받을 때마다 새 userId** 이고, 그러면 선을 원하는 만큼 넘는다.
        // 공개 서버에서 그것은 디스크를 채우는 길이다(리뷰가 잡았다).
        RunLogStore.Admission admission = store.admit(userId, body.length);
        if (admission != RunLogStore.Admission.OK) {
            if (admission == RunLogStore.Admission.DISK_FULL) {
                // **507 로 답한다.** 이것은 "잠시 후 다시" 가 아니라 사람이 고쳐야 하는
                // 상태다. 클라이언트는 어차피 다음 실행에서 다시 보낸다.
                log.warn("[runlog] 디스크 여유 부족으로 거절 user={}", userId);
                return ResponseEntity.status(HttpStatus.INSUFFICIENT_STORAGE)
                        .body("서버에 공간이 없습니다.");
            }
            if (admission == RunLogStore.Admission.GLOBAL_RATE) {
                // 전역 상한에 닿았다는 것은 **평소와 다른 일이 일어나고 있다**는 뜻이다.
                log.warn("[runlog] 전역 상한으로 거절 user={} bytes={}", userId, body.length);
            }
            return ResponseEntity.status(HttpStatus.TOO_MANY_REQUESTS)
                    .body("잠시 후 다시 시도해 주세요.");
        }

        try {
            String saved = store.save(userId, fileName, build, backend, body);
            log.info("[runlog] 저장 user={} build={} backend={} bytes={} file={}",
                    userId, safe(build), safe(backend), body.length, saved);
            return ResponseEntity.accepted().body(saved);
        } catch (Exception e) {
            // 받는 쪽이 실패해도 게임에는 아무 영향이 없다. 다만 우리가 알아야 한다.
            log.warn("[runlog] 저장 실패 user={} : {}", userId, e.toString());
            return ResponseEntity.status(HttpStatus.INTERNAL_SERVER_ERROR)
                    .body("저장에 실패했습니다.");
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

    /** 로그에 남길 때 줄바꿈·제어문자를 없앤다 — 헤더 값은 남이 준 것이다. */
    private String safe(String s) {
        if (s == null) return "-";
        String t = s.replaceAll("[\\p{Cntrl}]", "");
        return t.length() > 80 ? t.substring(0, 80) : t;
    }
}
