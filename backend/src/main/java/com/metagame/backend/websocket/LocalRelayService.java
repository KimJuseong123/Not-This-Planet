package com.metagame.backend.websocket;

import tools.jackson.databind.JsonNode;
import jakarta.annotation.PostConstruct;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.MediaType;
import org.springframework.stereotype.Service;
import org.springframework.web.client.RestClient;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 우리 EC2 릴레이에 세션을 등록한다 (S15P21B202-299).
 *
 * <p>릴레이는 같은 호스트에서 도는 별 프로세스이고({@code scripts/relay/metagame_relay.py},
 * systemd {@code metagame-relay}), 제어 API 는 <b>{@code 127.0.0.1} 에만</b> 열려 있다.
 *
 * <p><b>왜 Spring 안이 아니라 별 프로세스인가.</b> 패킷 경로에 JVM·GC 가 끼지 않고, 백엔드를
 * 배포·재시작해도 게임이 끊기지 않는다. 그리고 Spring 쪽 변경이 "POST 대상을 바꾸는 것" 뿐이다.
 *
 * <p>Edgegap 과 달라지는 점 셋.
 *
 * <ul>
 *   <li><b>공인 IP 가 필요 없다.</b> 릴레이가 토큰으로 사람을 가른다. Edgegap 은 세션을 만들 때
 *       참가자 전원의 IP 를 요구했고, 교육장이 공인 IP 를 여러 개 돌려써서 그것이 계속 문제였다.
 *   <li><b>포트가 하나다.</b> 프로토콜은 역할을 포트로 구분하지만 토큰을 우리가 발급하므로
 *       "이 토큰은 방장" 을 알 수 있다. 그래서 serverPort 와 clientPort 가 같다.
 *   <li><b>기다릴 것이 없다.</b> Edgegap 은 {@code ready} 가 될 때까지 최대 8초를 폴링했다.
 * </ul>
 *
 * <p><b>공개 주소는 설정에서 받는다.</b> 제어 API 는 자기 포트만 알고 밖에서 어떤 이름으로
 * 보이는지는 모른다. 클라이언트가 붙을 주소이므로 비어 있으면 세션을 만들지 않는다.
 *
 * <p><b>부팅할 때 제어 API 에 한 번 물어본다</b>({@link #probe()}, S15P21B202-303). 그렇게 하는
 * 이유가 있다 — {@code -299} 는 테스트가 통과하고 배포도 성공했는데 <b>backend → relay 경로가
 * 한 번도 동작하지 않았다.</b> 컨테이너 안의 {@code 127.0.0.1} 은 컨테이너 자신이라 기본값으로는
 * 호스트의 릴레이에 닿지 않는데, 그 사실이 <b>첫 게임이 시작될 때까지 드러나지 않았다.</b>
 * 테스트는 같은 JVM 안 stub 을 불러서 컨테이너 경계를 넘지 않았다. 그래서 코드가 아니라
 * <b>기동 로그</b>가 알려 주게 한다.
 */
@Slf4j
@Service
@ConditionalOnProperty(name = "relay.provider", havingValue = "local", matchIfMissing = true)
public class LocalRelayService implements RelayProvider {

    private final RestClient client;
    private final String publicAddress;

    public LocalRelayService(
            @Value("${relay.local.base-url:http://127.0.0.1:17779}") String baseUrl,
            @Value("${relay.local.public-address:}") String publicAddress) {
        this.client = RestClient.builder().baseUrl(baseUrl).build();
        this.publicAddress = publicAddress == null ? "" : publicAddress.trim();
        if (this.publicAddress.isEmpty()) {
            log.warn("relay.local.public-address 가 비어 있다 — 릴레이를 쓰지 않고 직접 접속으로 간다");
        } else {
            log.info("relay provider=local · 제어 {} · 공개 주소 {}", baseUrl, this.publicAddress);
        }
    }

    @Override
    public boolean isEnabled() {
        return !publicAddress.isEmpty();
    }

    /**
     * 제어 API 에 실제로 닿는지 한 번 확인하고 로그에 남긴다. 반환은 도달 여부.
     *
     * <p><b>절대 예외를 밖으로 내지 않는다.</b> 릴레이가 아직 안 떴다고 백엔드가 못 뜨면 안 된다 —
     * 릴레이 없이도 같은 네트워크 직접 접속은 굴러가야 한다.
     */
    @PostConstruct
    public boolean probe() {
        if (!isEnabled()) {
            return false;
        }
        try {
            JsonNode health = client.get().uri("/health").retrieve().body(JsonNode.class);
            boolean ok = health != null && health.path("ok").asBoolean(false);
            if (ok) {
                log.info("relay 제어 API 도달 OK · UDP 포트 {} · 세션 {}개",
                        health.path("port").asInt(), health.path("sessions").asInt());
            } else {
                log.warn("relay 제어 API 가 응답했지만 ok 가 아니다 — 릴레이 상태를 확인하라");
            }
            return ok;
        } catch (RuntimeException e) {
            log.warn("relay 제어 API 에 닿지 못한다 — 다른 망 플레이가 직접 접속으로 떨어진다. "
                    + "컨테이너의 127.0.0.1 은 컨테이너 자신이다(S15P21B202-303): {}", e.toString());
            return false;
        }
    }

    @Override
    public RelaySession open(LinkedHashMap<Long, String> userIps, Long hostUserId) {
        if (!isEnabled() || userIps == null || userIps.isEmpty()) {
            return null;
        }
        // 방장을 명시로 받는다. 못 받았으면 첫 사람 — publicIpsOf 가 방장을 앞에 놓는다.
        Long host = hostUserId != null && userIps.containsKey(hostUserId)
                ? hostUserId
                : userIps.keySet().iterator().next();

        List<Map<String, Object>> users = new ArrayList<>();
        for (Long userId : userIps.keySet()) {
            users.add(userId.equals(host)
                    ? Map.of("userId", userId, "host", true)
                    : Map.of("userId", userId));
        }

        try {
            JsonNode body = client.post()
                    .uri("/sessions")
                    .contentType(MediaType.APPLICATION_JSON)
                    .body(Map.of("users", users))
                    .retrieve()
                    .body(JsonNode.class);
            return toSession(body, userIps.keySet());
        } catch (RuntimeException e) {
            // 릴레이가 죽어 있어도 같은 네트워크 직접 접속은 살아 있어야 한다
            log.warn("local relay open failed, falling back to direct connect: {}", e.toString());
            return null;
        }
    }

    private RelaySession toSession(JsonNode body, Iterable<Long> userIds) {
        if (body == null || !body.hasNonNull("sessionId")) {
            log.warn("local relay returned no sessionId");
            return null;
        }
        String sessionId = body.get("sessionId").asText();
        int port = body.path("port").asInt();
        long sessionToken = body.path("sessionToken").asLong(0);

        JsonNode users = body.path("users");
        Map<Long, Long> userTokens = new LinkedHashMap<>();
        int expected = 0;
        for (Long userId : userIds) {
            expected++;
            long token = users.path(String.valueOf(userId)).asLong(0);
            if (token > 0) {
                userTokens.put(userId, token);
            }
        }

        if (port <= 0 || sessionToken <= 0 || userTokens.size() < expected) {
            // 무엇이 비었는지만 남긴다. 토큰 값은 로그에 넣지 않는다.
            log.warn("local relay session unusable — port={} sessionToken={} 토큰 {}/{}",
                    port, sessionToken > 0 ? "있음" : "없음", userTokens.size(), expected);
            close(sessionId);                 // 쓸 수 없는 세션을 남기지 않는다
            return null;
        }

        log.info("local relay session {} · {}:{} · 사람 {}명",
                sessionId, publicAddress, port, userTokens.size());
        // 포트가 하나다 — 방장과 참가자를 토큰으로 가른다
        return new RelaySession(sessionId, publicAddress, port, port, sessionToken, userTokens);
    }

    @Override
    public void close(String sessionId) {
        if (sessionId == null || sessionId.isBlank()) {
            return;
        }
        try {
            client.delete().uri("/sessions/{id}", sessionId).retrieve().toBodilessEntity();
            log.info("local relay session {} 닫음", sessionId);
        } catch (RuntimeException e) {
            // 못 닫아도 릴레이가 유휴 세션을 스스로 정리한다
            log.debug("local relay close failed: {}", e.toString());
        }
    }
}
