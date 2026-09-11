package com.metagame.backend.global.config;

import static org.assertj.core.api.Assertions.assertThat;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.server.LocalServerPort;

/**
 * 오류 응답의 상태 코드가 덮이지 않고 그대로 나가는지 확인한다 (S15P21B202-210).
 *
 * <p>Spring Boot 는 오류를 {@code /error} 로 <b>포워드</b>해서 응답 본문을 만든다. 그
 * 포워드도 시큐리티 필터를 다시 지나므로, {@code anyRequest().authenticated()} 가 그것까지
 * 막으면 앱이 이미 만든 400·405 가 <b>403 으로 덮여</b> 나간다. 그러면 클라이언트는
 * "내 요청이 잘못됐다" 와 "권한이 없다" 를 구분할 수 없다.
 *
 * <p>2026-08-31 운영 서버(j15b202.p.ssafy.io)에서 실측된 증상이고, 이 테스트가 그것을
 * 로컬에 고정한다. 고치기 전에는 아래 400·405 가 둘 다 403 이었다.
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
class ErrorStatusCodeTest {

    @LocalServerPort
    private int port;

    private final HttpClient httpClient = HttpClient.newHttpClient();

    @Test
    void validGuestLoginStillReturns200() throws Exception {
        HttpResponse<String> response =
                post("/auth/guest", "{\"nickname\":\"철수\",\"deviceId\":\"device-210\"}");

        assertThat(response.statusCode()).isEqualTo(200);
    }

    @Test
    void missingRequiredFieldsReturn400() throws Exception {
        HttpResponse<String> response = post("/auth/guest", "{}");

        assertThat(response.statusCode()).isEqualTo(400);
    }

    @Test
    void wrongHttpMethodReturns405() throws Exception {
        HttpResponse<String> response = get("/auth/guest");

        assertThat(response.statusCode()).isEqualTo(405);
    }

    /**
     * 오류 포워드를 열어도 밖에서 {@code /error} 를 직접 부르는 것은 막혀야 한다.
     *
     * <p>이것이 경로({@code requestMatchers("/error")})가 아니라 디스패치 종류로 허용한
     * 이유다. 경로로 열면 이 문까지 열린다.
     */
    @Test
    void directRequestToErrorPathIsStillDenied() throws Exception {
        HttpResponse<String> response = get("/error");

        assertThat(response.statusCode()).isEqualTo(403);
    }

    /**
     * 매핑이 없는 경로는 <b>404 가 아니라 403</b> 이다. 의도한 동작이다.
     *
     * <p>{@code anyRequest().authenticated()} 가 인증 없는 요청을 라우팅 전에 막으므로
     * "그런 경로가 없다" 까지 가지 않는다. 404 로 바꾸려면 매핑 없는 경로를 통과시켜야
     * 하는데, 그러면 <b>어떤 경로가 있고 없는지가 밖에서 드러난다.</b>
     *
     * <p>이 저장소는 이미 반대 원칙을 택했다 — {@code backend/docs/PROTOCOL.md} 의 매치
     * 진입 확인은 "실패 사유(토큰이 틀렸는지, 방이 없는지)를 구분해서 알려 주지 않는다.
     * 알려 주면 남의 방을 더듬어 볼 수 있다" 고 적어 두었다. 같은 이유로 여기서도
     * 403 을 유지한다.
     *
     * <p>{@code -210} 의 완료 조건은 이 줄을 404 로 적었다. 그 판단은 보류하고 현재
     * 동작을 시험으로 고정한다 — 바꾸려면 인가 정책을 바꾸는 별 이슈여야 한다.
     */
    @Test
    void unmappedPathIsDeniedWithoutDisclosingWhetherItExists() throws Exception {
        HttpResponse<String> response = get("/no-such-path");

        assertThat(response.statusCode()).isEqualTo(403);
    }

    private HttpResponse<String> post(String path, String body) throws Exception {
        return httpClient.send(
                HttpRequest.newBuilder(URI.create("http://localhost:" + port + path))
                        .header("Content-Type", "application/json")
                        .POST(HttpRequest.BodyPublishers.ofString(body))
                        .build(),
                HttpResponse.BodyHandlers.ofString());
    }

    private HttpResponse<String> get(String path) throws Exception {
        return httpClient.send(
                HttpRequest.newBuilder(URI.create("http://localhost:" + port + path))
                        .GET().build(),
                HttpResponse.BodyHandlers.ofString());
    }
}
