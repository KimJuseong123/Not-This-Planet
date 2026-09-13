package com.metagame.backend.domain.auth;

import com.metagame.backend.domain.auth.dto.ClientOutdatedResponse;
import com.metagame.backend.domain.auth.dto.GuestLoginRequest;
import com.metagame.backend.domain.auth.dto.GuestLoginResponse;
import com.metagame.backend.domain.auth.dto.MatchVerifyRequest;
import com.metagame.backend.domain.auth.dto.MatchVerifyResponse;
import jakarta.validation.Valid;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/auth")
@RequiredArgsConstructor
public class AuthController {

    private final AuthService authService;
    private final MatchAuthService matchAuthService;
    private final ClientVersionGate versionGate;

    /**
     * 게스트 로그인. **여기가 구버전을 막는 자리다** (S15P21B202-404).
     *
     * <p>게임에 들어오려면 반드시 이 요청을 지나므로, 런처를 건너뛰고 압축 푼 폴더의
     * exe 를 직접 실행해도 우회할 수 없다. 거절할 때는 <b>받을 곳까지</b> 담아 준다 —
     * 사유만 주면 사용자가 무엇을 해야 하는지 모른다.
     *
     * <p>사용자를 만들기 <b>전에</b> 본다. 막을 요청으로 행을 만들 이유가 없다.
     */
    @PostMapping("/guest")
    public ResponseEntity<?> guestLogin(@Valid @RequestBody GuestLoginRequest request) {
        ClientVersionGate.Verdict verdict = versionGate.check(request.clientVersion());
        if (verdict != ClientVersionGate.Verdict.OK) {
            // 426 — "그 프로토콜로는 안 되니 올려라". 401(인증 실패)·403(권한 없음)과
            // 뜻이 달라야 클라이언트가 **다시 로그인해 보는 헛수고**를 하지 않는다.
            return ResponseEntity.status(HttpStatus.UPGRADE_REQUIRED)
                    .body(new ClientOutdatedResponse(
                            versionGate.message(verdict),
                            versionGate.downloadUrl(),
                            versionGate.minVersion()));
        }
        return ResponseEntity.ok(authService.guestLogin(request));
    }

    /**
     * 게임 서버가 접속해 온 플레이어를 확인한다. 사람이 부르는 API 가 아니다.
     * 통과하면 userId·닉네임, 아니면 401 — 사유는 구분하지 않는다.
     */
    @PostMapping("/verify")
    public ResponseEntity<MatchVerifyResponse> verifyMatchPlayer(
            @Valid @RequestBody MatchVerifyRequest request) {
        MatchVerifyResponse verified = matchAuthService.verify(request);
        return verified == null
                ? ResponseEntity.status(401).build()
                : ResponseEntity.ok(verified);
    }
}
