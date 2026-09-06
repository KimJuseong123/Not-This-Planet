package com.metagame.backend.websocket;

import static org.assertj.core.api.Assertions.assertThat;

import com.sun.net.httpserver.HttpServer;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;

/**
 * 우리 릴레이 제어 API 를 상대로 {@link LocalRelayService} 가 값을 옳게 주고받는지 (S15P21B202-299).
 *
 * <p>가짜 HTTP 서버를 임의 포트에 띄워 확인한다. 필드 이름·타입이 어긋나면 릴레이가 붙지 않는데,
 * 그 종류의 실수는 실행해 보기 전까지 드러나지 않는다.
 *
 * <p>특히 두 가지를 본다.
 *
 * <ul>
 *   <li><b>방장 표시가 옳은 사람에게 붙는가.</b> 릴레이는 이 표시로 서버 역할을 가른다 —
 *       틀리면 방장이 서버로 인정받지 못하고, 그것이 Edgegap 에서 우리를 막았던 증상이다.
 *   <li><b>쓸 수 없는 응답에는 세션을 남기지 않는가.</b> 토큰이 모자란 채로 돌려주면
 *       클라이언트가 붙지 못하는데 릴레이에는 세션이 남는다.
 * </ul>
 */
class LocalRelayServiceTest {

    private HttpServer server;
    private String baseUrl;
    private final List<String> posted = new ArrayList<>();
    private final List<String> deleted = new ArrayList<>();
    private String response = "";

    @BeforeEach
    void startStub() throws IOException {
        server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/sessions", exchange -> {
            if ("POST".equals(exchange.getRequestMethod())) {
                posted.add(new String(exchange.getRequestBody().readAllBytes(), StandardCharsets.UTF_8));
                byte[] body = response.getBytes(StandardCharsets.UTF_8);
                exchange.getResponseHeaders().add("Content-Type", "application/json");
                exchange.sendResponseHeaders(201, body.length);
                exchange.getResponseBody().write(body);
            } else if ("DELETE".equals(exchange.getRequestMethod())) {
                deleted.add(exchange.getRequestURI().getPath());
                exchange.sendResponseHeaders(200, -1);
            } else {
                exchange.sendResponseHeaders(405, -1);
            }
            exchange.close();
        });
        server.start();
        baseUrl = "http://127.0.0.1:" + server.getAddress().getPort();
    }

    @AfterEach
    void stopStub() {
        server.stop(0);
    }

    private LocalRelayService service() {
        return new LocalRelayService(baseUrl, "j15b202.p.ssafy.io");
    }

    private LinkedHashMap<Long, String> room() {
        LinkedHashMap<Long, String> ips = new LinkedHashMap<>();
        ips.put(7L, "1.2.3.4");
        ips.put(9L, "5.6.7.8");
        return ips;
    }

    @Test
    void openReturnsSessionWithOnePortForBothRoles() {
        response = """
                {"sessionId":"abc123","sessionToken":555,"port":17778,
                 "hostUserId":7,"users":{"7":111,"9":222}}""";
        RelaySession relay = service().open(room(), 7L);

        assertThat(relay).isNotNull();
        assertThat(relay.sessionId()).isEqualTo("abc123");
        assertThat(relay.address()).isEqualTo("j15b202.p.ssafy.io");
        // 포트가 하나다 — 방장과 참가자를 토큰으로 가른다
        assertThat(relay.serverPort()).isEqualTo(17778);
        assertThat(relay.clientPort()).isEqualTo(17778);
        assertThat(relay.sessionToken()).isEqualTo(555L);
        assertThat(relay.userTokens()).containsEntry(7L, 111L).containsEntry(9L, 222L);
    }

    @Test
    void hostFlagGoesToTheHostOnly() {
        response = """
                {"sessionId":"abc123","sessionToken":555,"port":17778,"users":{"7":111,"9":222}}""";
        service().open(room(), 9L);          // 방장을 9 로 준다

        assertThat(posted).hasSize(1);
        String body = posted.get(0);
        // 9 에만 host 표시가 붙어야 한다. 릴레이는 이 표시로 서버 역할을 가른다.
        assertThat(body).contains("\"userId\":9").contains("\"host\":true");
        int hostAt = body.indexOf("\"host\":true");
        int nine = body.indexOf("\"userId\":9");
        int seven = body.indexOf("\"userId\":7");
        assertThat(Math.abs(hostAt - nine)).isLessThan(Math.abs(hostAt - seven));
    }

    @Test
    void payloadForCarriesOnlyThatPersonsToken() {
        response = """
                {"sessionId":"abc123","sessionToken":555,"port":17778,"users":{"7":111,"9":222}}""";
        RelaySession relay = service().open(room(), 7L);

        assertThat(relay.payloadFor(9L))
                .containsEntry("userToken", 222L)
                .containsEntry("sessionToken", 555L)
                .containsEntry("serverPort", 17778)
                .doesNotContainValue(111L);          // 남의 토큰이 섞이지 않는다
        assertThat(relay.payloadFor(999L)).isNull();
        // sessionId 는 우리 쪽 식별자다 — 클라이언트에 보내지 않는다
        assertThat(relay.payloadFor(7L)).doesNotContainKey("sessionId");
    }

    @Test
    void missingTokenMeansNoSessionAndTheStubGetsCleanedUp() {
        response = """
                {"sessionId":"abc123","sessionToken":555,"port":17778,"users":{"7":111}}""";
        assertThat(service().open(room(), 7L)).isNull();     // 9 의 토큰이 없다
        // 쓸 수 없는 세션을 릴레이에 남기지 않는다
        assertThat(deleted).containsExactly("/sessions/abc123");
    }

    @Test
    void brokenResponseFallsBackToDirectConnect() {
        response = "{}";
        assertThat(service().open(room(), 7L)).isNull();
        response = "{\"sessionId\":\"x\",\"port\":0,\"sessionToken\":0,\"users\":{}}";
        assertThat(service().open(room(), 7L)).isNull();
    }

    @Test
    void withoutPublicAddressRelayIsNotUsed() {
        LocalRelayService noAddress = new LocalRelayService(baseUrl, "  ");
        assertThat(noAddress.isEnabled()).isFalse();
        assertThat(noAddress.open(room(), 7L)).isNull();
        assertThat(posted).isEmpty();          // 주소가 없으면 요청조차 하지 않는다
    }

    @Test
    void probeTellsWhetherTheControlApiIsReachable() {
        // /health 를 주는 경로를 붙인다
        server.createContext("/health", exchange -> {
            byte[] body = "{\"ok\":true,\"port\":17778,\"sessions\":0}".getBytes(StandardCharsets.UTF_8);
            exchange.getResponseHeaders().add("Content-Type", "application/json");
            exchange.sendResponseHeaders(200, body.length);
            exchange.getResponseBody().write(body);
            exchange.close();
        });
        assertThat(service().probe()).isTrue();

        // 주소가 없으면 릴레이를 안 쓰므로 물어보지도 않는다
        assertThat(new LocalRelayService(baseUrl, "").probe()).isFalse();

        // 닿지 않는 곳이면 **예외를 내지 않고** false 를 돌려준다 — 릴레이가 없어도 백엔드는 떠야 한다
        assertThat(new LocalRelayService("http://127.0.0.1:1", "j15b202.p.ssafy.io").probe()).isFalse();
    }

    @Test
    void closeSendsDelete() {
        service().close("abc123");
        assertThat(deleted).containsExactly("/sessions/abc123");
        deleted.clear();
        service().close("   ");
        service().close(null);
        assertThat(deleted).isEmpty();
    }
}
