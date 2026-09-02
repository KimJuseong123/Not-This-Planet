package com.metagame.backend.websocket;

import tools.jackson.databind.JsonNode;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.MediaType;
import org.springframework.stereotype.Service;
import org.springframework.web.client.RestClient;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Edgegap 릴레이 세션을 만든다 (S15P21B202-264).
 *
 * <p><b>왜 서버가 만드나.</b> 세션을 열려면 참가자 <b>전원의 공인 IP</b> 가 필요한데,
 * 클라이언트는 남의 IP 를 모른다. 로비 WebSocket 으로 모두가 여기에 붙어 있으므로
 * 서버만 그 목록을 갖고 있다.
 *
 * <p><b>없어도 게임은 굴러간다.</b> 토큰이 없거나 호출이 실패하면 아무것도 돌려주지 않고,
 * 그때는 같은 네트워크 직접 접속으로 간다(-263). 릴레이 하나 때문에 시연이 멈추면 안 된다.
 *
 * <p>흐름은 {@code -145 §3-8-1} 에 표로 남겼다.
 */
@Slf4j
@Service
public class EdgegapRelayService {

    private static final String BASE_URL = "https://api.edgegap.com";

    /** ready 가 될 때까지 기다리는 시간. 넘기면 포기하고 직접 접속으로 간다. */
    private static final long READY_TIMEOUT_MS = 8_000;
    private static final long POLL_INTERVAL_MS = 500;

    private final RestClient client;
    private final String apiToken;

    public EdgegapRelayService(@Value("${relay.edgegap.token:}") String apiToken) {
        this.apiToken = apiToken == null ? "" : apiToken.trim();
        this.client = RestClient.builder().baseUrl(BASE_URL).build();
    }

    public boolean isEnabled() {
        return !apiToken.isBlank();
    }

    /**
     * 세션을 만들고 준비될 때까지 기다린다.
     *
     * @param userIps userId → 공인 IP. 순서가 응답의 session_users 와 짝이 된다.
     * @return 쓸 수 있는 세션, 또는 실패 시 null (그때는 직접 접속)
     */
    public RelaySession open(LinkedHashMap<Long, String> userIps) {
        if (!isEnabled() || userIps == null || userIps.isEmpty()) {
            return null;
        }

        try {
            String sessionId = create(userIps.values());
            if (sessionId == null) {
                return null;
            }
            return waitUntilReady(sessionId, new ArrayList<>(userIps.keySet()));
        } catch (RuntimeException e) {
            log.warn("relay session failed, falling back to direct connect: {}", e.toString());
            return null;
        }
    }

    /** 세션을 닫는다. 실패해도 무시한다 — 방치해도 Edgegap 이 스스로 정리한다. */
    public void close(String sessionId) {
        if (!isEnabled() || sessionId == null || sessionId.isBlank()) {
            return;
        }
        try {
            client.delete()
                    .uri("/v1/relays/sessions/{id}", sessionId)
                    .header("Authorization", "Token " + apiToken)
                    .retrieve()
                    .toBodilessEntity();
        } catch (RuntimeException e) {
            log.debug("relay session close failed: {}", e.toString());
        }
    }

    private String create(Iterable<String> ips) {
        List<Map<String, String>> users = new ArrayList<>();
        for (String ip : ips) {
            if (ip != null && !ip.isBlank()) {
                users.add(Map.of("ip", ip));
            }
        }
        if (users.isEmpty()) {
            return null;
        }

        JsonNode body = client.post()
                .uri("/v1/relays/sessions")
                .header("Authorization", "Token " + apiToken)
                .contentType(MediaType.APPLICATION_JSON)
                .body(Map.of("users", users))
                .retrieve()
                .body(JsonNode.class);

        if (body == null || !body.hasNonNull("session_id")) {
            log.warn("relay create returned no session_id");
            return null;
        }
        return body.get("session_id").asText();
    }

    /**
     * 만든 직후에는 relay 주소와 사람별 토큰이 비어 있다("Initializing").
     * ready 가 될 때까지 짧게 되물어본다.
     */
    private RelaySession waitUntilReady(String sessionId, List<Long> userIds) {
        long deadline = System.currentTimeMillis() + READY_TIMEOUT_MS;

        while (System.currentTimeMillis() < deadline) {
            JsonNode body = client.get()
                    .uri("/v1/relays/sessions/{id}", sessionId)
                    .header("Authorization", "Token " + apiToken)
                    .retrieve()
                    .body(JsonNode.class);

            if (body != null && body.path("ready").asBoolean(false)) {
                return toSession(body, userIds);
            }

            try {
                Thread.sleep(POLL_INTERVAL_MS);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                return null;
            }
        }

        log.warn("relay session {} not ready in {}ms", sessionId, READY_TIMEOUT_MS);
        close(sessionId);
        return null;
    }

    private RelaySession toSession(JsonNode body, List<Long> userIds) {
        JsonNode relay = body.path("relay");
        String address = relay.path("ip").asText("");
        int serverPort = relay.path("ports").path("server").path("port").asInt();
        int clientPort = relay.path("ports").path("client").path("port").asInt();

        // Edgegap 의 session_id 는 문자열이고, 전송에 넣는 값은 세션 단위
        // authorization_token(숫자)이다. 둘은 다른 것이다(-145 §3-8-1).
        long sessionToken = body.path("authorization_token").asLong(0);

        JsonNode users = body.path("session_users");
        Map<Long, Long> userTokens = new HashMap<>();
        for (int i = 0; i < users.size() && i < userIds.size(); i++) {
            long token = users.get(i).path("authorization_token").asLong(0);
            if (token > 0) {
                userTokens.put(userIds.get(i), token);
            }
        }

        if (address.isBlank() || serverPort <= 0 || clientPort <= 0
                || sessionToken <= 0 || userTokens.size() < userIds.size()) {
            log.warn("relay session ready but incomplete — falling back to direct connect");
            return null;
        }

        return new RelaySession(address, serverPort, clientPort, sessionToken, userTokens);
    }
}
