package com.metagame.backend.websocket;

import tools.jackson.databind.JsonNode;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
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
@ConditionalOnProperty(name = "relay.provider", havingValue = "edgegap")
public class EdgegapRelayService implements RelayProvider {

    private static final String BASE_URL = "https://api.edgegap.com";

    /** ready 가 될 때까지 기다리는 시간. 넘기면 포기하고 직접 접속으로 간다. */
    private static final long READY_TIMEOUT_MS = 8_000;
    private static final long POLL_INTERVAL_MS = 500;

    /**
     * Edgegap 이 요구하는 형식 — 소문자 {@code token} 접두어를 반드시 붙인다.
     * 문서가 "'token' 부분을 삭제하지 마세요" 라고 못박는다. 대문자로 보내면 거절될 수 있다.
     */
    private static final String AUTH_PREFIX = "token ";

    private final RestClient client;
    private final String apiToken;

    public EdgegapRelayService(@Value("${relay.edgegap.token:}") String apiToken) {
        this.apiToken = clean(apiToken);
        this.client = RestClient.builder().baseUrl(BASE_URL).build();
    }

    /**
     * 대시보드에서 토큰을 복사하면 "token 12345..." 처럼 접두어까지 딸려 온다.
     * 그대로 넣으면 헤더가 "token token 12345..." 가 되어 거절당하고, 우리 코드는
     * 그때 조용히 직접 접속으로 되돌아가므로 원인을 찾기 어렵다. 여기서 한 번 걷어낸다.
     */
    private static String clean(String raw) {
        if (raw == null) {
            return "";
        }
        String value = raw.trim();
        if (value.regionMatches(true, 0, AUTH_PREFIX, 0, AUTH_PREFIX.length())) {
            value = value.substring(AUTH_PREFIX.length()).trim();
        }
        return value;
    }

    @Override
    public boolean isEnabled() {
        return !apiToken.isBlank();
    }

    /**
     * 세션을 만들고 준비될 때까지 기다린다.
     *
     * @param userIps userId → 공인 IP. 순서가 응답의 session_users 와 짝이 된다.
     * @return 쓸 수 있는 세션, 또는 실패 시 null (그때는 직접 접속)
     */
    @Override
    public RelaySession open(LinkedHashMap<Long, String> userIps, Long hostUserId) {
        // hostUserId 는 쓰지 않는다 — Edgegap 은 세션 안에서 서버를 지정하는 방법이 없고,
        // 서버 포트로 핑한 쪽이 서버가 된다. userIps 의 순서(방장 먼저)만 의미가 있었다.
        if (!isEnabled() || userIps == null || userIps.isEmpty()) {
            return null;
        }

        try {
            String sessionId = create(userIps.values());
            if (sessionId == null) {
                return null;
            }
            return waitUntilReady(sessionId, userIps);
        } catch (RuntimeException e) {
            log.warn("relay session failed, falling back to direct connect: {}", e.toString());
            return null;
        }
    }

    /** 세션을 닫는다. 실패해도 무시한다 — 방치해도 Edgegap 이 스스로 정리한다. */
    @Override
    public void close(String sessionId) {
        if (!isEnabled() || sessionId == null || sessionId.isBlank()) {
            return;
        }
        try {
            client.delete()
                    .uri("/v1/relays/sessions/{id}", sessionId)
                    .header("Authorization", AUTH_PREFIX + apiToken)
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
                .header("Authorization", AUTH_PREFIX + apiToken)
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
    private RelaySession waitUntilReady(String sessionId, LinkedHashMap<Long, String> userIps) {
        long deadline = System.currentTimeMillis() + READY_TIMEOUT_MS;

        while (System.currentTimeMillis() < deadline) {
            JsonNode body = client.get()
                    .uri("/v1/relays/sessions/{id}", sessionId)
                    .header("Authorization", AUTH_PREFIX + apiToken)
                    .retrieve()
                    .body(JsonNode.class);

            if (body != null && body.path("ready").asBoolean(false)) {
                return toSession(sessionId, body, userIps);
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

    /** 응답의 사용자 IP. Edgegap 이 어느 이름으로 주든 읽는다. */
    private static String userIp(JsonNode user) {
        for (String field : new String[] { "ip_address", "ip", "user_ip" }) {
            String value = user.path(field).asText("");
            if (!value.isBlank()) {
                return value.trim();
            }
        }
        return "";
    }

    /** 이 IP 로 참가한 사람이 누구인지. 같은 IP 가 둘이면 짝지을 수 없어 null 이다. */
    private static Long ownerOf(String ip, Map<Long, String> userIps) {
        Long found = null;
        for (Map.Entry<Long, String> entry : userIps.entrySet()) {
            if (ip.equals(entry.getValue())) {
                if (found != null) {
                    return null;          // 같은 IP 가 여럿 — 순서로 짝지어야 한다
                }
                found = entry.getKey();
            }
        }
        return found;
    }

    private RelaySession toSession(String sessionId, JsonNode body,
                                   LinkedHashMap<Long, String> userIps) {
        List<Long> userIds = new ArrayList<>(userIps.keySet());
        JsonNode relay = body.path("relay");
        String address = relay.path("ip").asText("");
        int serverPort = relay.path("ports").path("server").path("port").asInt();
        int clientPort = relay.path("ports").path("client").path("port").asInt();

        // Edgegap 의 session_id 는 문자열이고, 전송에 넣는 값은 세션 단위
        // authorization_token(숫자)이다. 둘은 다른 것이다(-145 §3-8-1).
        long sessionToken = body.path("authorization_token").asLong(0);

        // 사람별 토큰을 IP 로 짝짓는다. 순서(index)로 짝지으면 Edgegap 이 다른 순서로
        // 돌려줬을 때 방장과 참가자의 토큰이 뒤바뀌고, 릴레이가 방장을 세션 주인으로
        // 인정하지 않는다 — 그러면 참가자는 SessionTimeout 을 받는다(2026-09-02 증상).
        // IP 를 못 읽으면 그때만 순서로 되돌아간다.
        JsonNode users = body.path("session_users");
        Map<Long, Long> userTokens = new HashMap<>();
        for (int i = 0; i < users.size() && i < userIds.size(); i++) {
            JsonNode user = users.get(i);
            long token = user.path("authorization_token").asLong(0);
            if (token <= 0) {
                continue;
            }

            String ip = userIp(user);
            Long owner = ip.isBlank() ? null : ownerOf(ip, userIps);
            if (owner == null) {
                owner = userIds.get(i);
                log.warn("relay user[{}] 를 IP 로 짝짓지 못해 순서로 짝짓는다 · 응답 IP={} · 필드={}",
                        i, ip.isBlank() ? "없음" : ip, user.propertyNames());
            }
            userTokens.put(owner, token);

            log.info("relay user[{}] → userId={} · IP {}", i, owner, ip.isBlank() ? "없음" : ip);
        }

        if (address.isBlank() || serverPort <= 0 || clientPort <= 0
                || sessionToken <= 0 || userTokens.size() < userIds.size()) {
            // 무엇이 비었는지 찍는다. 토큰 값은 남기지 않는다.
            log.warn("relay incomplete — ip={} serverPort={} clientPort={} sessionToken={} "
                            + "userTokens={}/{} · 응답 필드: {}",
                    address.isBlank() ? "없음" : "있음", serverPort, clientPort,
                    sessionToken > 0 ? "있음" : "없음",
                    userTokens.size(), userIds.size(),
                    body.propertyNames());

            // 쓸 수 없는 세션은 붙잡고 있을 이유가 없다 — 두면 동시접속 한도만 먹는다.
            close(sessionId);
            return null;
        }

        log.info("relay ready: {} (server {} / client {}) for {} users",
                address, serverPort, clientPort, userTokens.size());
        return new RelaySession(sessionId, address, serverPort, clientPort, sessionToken, userTokens);
    }
}
