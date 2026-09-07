package com.metagame.backend.websocket;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.metagame.backend.domain.auth.dto.GuestLoginRequest;
import com.metagame.backend.domain.auth.dto.GuestLoginResponse;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.web.socket.CloseStatus;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketSession;
import org.springframework.web.socket.client.standard.StandardWebSocketClient;
import org.springframework.web.socket.handler.TextWebSocketHandler;
import tools.jackson.databind.JsonNode;
import tools.jackson.databind.ObjectMapper;

/**
 * 실제 서버를 띄우고 WebSocket으로 붙어 방 흐름 전체를 확인한다
 * (S15P21B202-46 방 시스템, S15P21B202-48 복귀, S15P21B202-76 방 코드).
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
class GameWebSocketIntegrationTest {

    private static final long TIMEOUT_SECONDS = 5;

    @LocalServerPort
    private int port;

    @Autowired
    private ObjectMapper objectMapper;

    private final HttpClient httpClient = HttpClient.newHttpClient();

    private final List<TestClient> clients = new ArrayList<>();

    @AfterEach
    void closeClients() {
        clients.forEach(TestClient::close);
        clients.clear();
    }

    // ---------- 인증 ----------

    @Test
    void handshakeIsRejectedWithoutToken() {
        assertThatThrownBy(() -> connectRaw("ws://localhost:" + port + "/ws/game"))
                .isInstanceOf(Exception.class);
    }

    @Test
    void handshakeIsRejectedWithForgedToken() {
        assertThatThrownBy(() -> connectRaw("ws://localhost:" + port + "/ws/game?token=not.a.jwt"))
                .isInstanceOf(Exception.class);
    }

    @Test
    void handshakeSucceedsWithIssuedToken() {
        TestClient client = connect("철수", "device-a");

        assertThat(client.session.isOpen()).isTrue();
    }

    // ---------- 방 생성·입장 ----------

    @Test
    void roomCodeIsHumanReadableAndJoinIsCaseInsensitive() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");

        host.send("CREATE_ROOM", Map.of());
        String code = host.await("ROOM_CREATED").get("roomId").asString();
        assertThat(code).hasSize(6).matches("[2-9A-HJ-NP-Z]{6}");
        host.await("ROOM_STATE");

        guest.send("JOIN_ROOM", Map.of("roomId", code.toLowerCase()));
        JsonNode state = guest.await("ROOM_STATE");

        assertThat(state.get("roomId").asString()).isEqualTo(code);
        assertThat(state.get("players").properties()).hasSize(2);
    }

    @Test
    void roomStateCarriesNicknameAndReadyStateForEachPlayer() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = createRoom(host);
        guest.send("JOIN_ROOM", Map.of("roomId", code));
        host.await("ROOM_STATE");
        guest.await("ROOM_STATE");

        guest.send("READY", Map.of("ready", true));
        JsonNode players = host.await("ROOM_STATE").get("players");

        JsonNode hostEntry = players.get(String.valueOf(host.userId));
        JsonNode guestEntry = players.get(String.valueOf(guest.userId));
        assertThat(hostEntry.get("nickname").asString()).isEqualTo("철수");
        assertThat(hostEntry.get("ready").asBoolean()).isFalse();
        assertThat(guestEntry.get("nickname").asString()).isEqualTo("영희");
        assertThat(guestEntry.get("ready").asBoolean()).isTrue();
    }

    @Test
    void rejoiningPlayerStillShowsNickname() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest);
        long guestId = guest.userId;
        guest.close();
        host.await("ROOM_STATE");

        TestClient returning = connect("영희", "device-b");
        returning.send("JOIN_ROOM", Map.of("roomId", code));
        returning.await("STATE_SYNC");

        JsonNode players = host.await("ROOM_STATE").get("players");
        assertThat(players.get(String.valueOf(guestId)).get("nickname").asString()).isEqualTo("영희");
    }

    /**
     * 소문자 방 코드로 재입장한 뒤 유예 시간을 넘겨도 자리가 유지된다 (S15P21B202-313).
     *
     * <p><b>이 테스트가 무엇을 보증하고 무엇을 보증하지 않는지 분명히 해 둔다.</b>
     *
     * <p>보증하는 것 — 방 코드를 소문자로 보내 재입장한 사람이 유예 만료 시점을 지난 뒤에도
     * 방의 방송을 계속 받는다. {@code broadcastExcept} 는 {@code room.playerIds()} 를 돌기
     * 때문에, 자리에서 지워졌다면 이 방송을 받을 수 없다.
     *
     * <p><b>보증하지 않는 것 — 원래 결함의 경쟁 조건 자체.</b> `-313` ① 은 만료 태스크가
     * {@code isDisconnected} 를 읽은 <b>직후</b> 재입장이 끼어드는 인터리빙에서만 터진다.
     * 실측으로 확인했다: ①② 를 둘 다 되돌리고 이 테스트를 3회 돌렸으나 모두 통과했다.
     * 재입장이 유예 창 안에서 일어나므로 만료 태스크가 아직 시작하지 않았고, 나중에 시작할
     * 때는 이미 {@code isDisconnected} 가 false 라 그냥 반환한다.
     *
     * <p>즉 <b>그 경쟁 조건에는 자동 테스트가 없다.</b> 코드 상의 논증(판정과 제거가 한
     * 임계구역이어야 한다)과 리뷰로만 지킨다. 재현 가능한 테스트를 만들려면 만료 태스크에
     * 시험용 훅을 넣어야 하는데, 프로덕션 코드에 시험 전용 구멍을 내는 값이 크지 않다고 봤다.
     *
     * <p>유예는 테스트에서 1초다({@code src/test/resources/application.yml}).
     */
    @Test
    void rejoiningWithLowercaseCodeKeepsTheSeatPastGrace() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest);
        long guestId = guest.userId;

        guest.close();
        host.await("ROOM_STATE");                     // 끊김이 방송된다

        // **소문자로** 재입장한다. RoomManager.find 는 대소문자를 무시하므로 입장 자체는 된다.
        TestClient returning = connect("영희", "device-b");
        returning.send("JOIN_ROOM", Map.of("roomId", code.toLowerCase()));
        returning.await("STATE_SYNC");
        host.await("ROOM_STATE");

        // 유예(1초)를 넘겨 기다린다. 취소가 빗나갔으면 이 사이에 자리에서 지워진다.
        Thread.sleep(1_600);

        // **아직 방에 있어야 한다.** 이 단정이 결정적인 이유: broadcastExcept 는
        // room.playerIds() 를 돌기 때문에, 유예 만료가 이 사람을 자리에서 지웠다면
        // 이 방송을 **받을 수 없다.** 그것이 정확히 유령 상태다.
        host.send("PLAYER_MOVE", Map.of("x", 1.5, "y", 0.0, "z", 3.25, "yaw", 90.0));
        JsonNode moved = returning.await("PLAYER_MOVED");
        assertThat(moved.get("userId").asLong()).isEqualTo(host.userId);
        assertThat(moved.get("x").asDouble()).isEqualTo(1.5);

        // 반대 방향도 — 이 사람이 움직이면 남이 본다(방 소속이 양쪽으로 살아 있다).
        returning.send("PLAYER_MOVE", Map.of("x", 3.0, "y", 0.0, "z", 4.0));
        assertThat(host.await("PLAYER_MOVED").get("userId").asLong()).isEqualTo(guestId);
    }

    @Test
    void stateChangesAreBroadcastToEveryoneInRoom() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = createRoom(host);

        guest.send("JOIN_ROOM", Map.of("roomId", code));

        assertThat(host.await("ROOM_STATE").get("players").properties()).hasSize(2);
        assertThat(guest.await("ROOM_STATE").get("players").properties()).hasSize(2);
    }

    @Test
    void joiningUnknownRoomReturnsError() throws Exception {
        TestClient client = connect("철수", "device-a");

        client.send("JOIN_ROOM", Map.of("roomId", "ZZZZZZ"));

        assertThat(client.await("ERROR").get("message").asString()).isNotBlank();
    }

    @Test
    void unknownMessageTypeReturnsErrorInsteadOfDroppingConnection() throws Exception {
        TestClient client = connect("철수", "device-a");

        client.sendRaw("{\"type\":\"NOT_A_REAL_TYPE\",\"data\":{}}");

        assertThat(client.await("ERROR").get("message").asString()).isNotBlank();
        assertThat(client.session.isOpen()).isTrue();
    }

    @Test
    void malformedPayloadReturnsErrorInsteadOfDroppingConnection() throws Exception {
        TestClient client = connect("철수", "device-a");

        client.sendRaw("this is not json");

        assertThat(client.await("ERROR").get("message").asString()).isNotBlank();
        assertThat(client.session.isOpen()).isTrue();
    }

    @Test
    void messageMissingRequiredFieldReturnsErrorInsteadOfDroppingConnection() throws Exception {
        TestClient client = connect("철수", "device-a");

        client.sendRaw("{\"type\":\"JOIN_ROOM\",\"data\":{}}");

        assertThat(client.await("ERROR").get("message").asString()).isNotBlank();
        assertThat(client.session.isOpen()).isTrue();
    }

    @Test
    void connectionStaysUsableAfterBadMessage() throws Exception {
        TestClient client = connect("철수", "device-a");
        client.sendRaw("{\"type\":\"NOT_A_REAL_TYPE\",\"data\":{}}");
        client.await("ERROR");

        client.send("CREATE_ROOM", Map.of());

        assertThat(client.await("ROOM_CREATED").get("roomId").asString()).hasSize(6);
    }

    // ---------- 시작 규칙 (서버 권위) ----------

    @Test
    void onlyHostCanStartAndOnlyWhenEveryoneIsReady() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = createRoom(host);
        guest.send("JOIN_ROOM", Map.of("roomId", code));
        host.await("ROOM_STATE");
        guest.await("ROOM_STATE");

        guest.send("START_GAME", Map.of());
        assertThat(guest.await("ERROR").get("message").asString()).isNotBlank();

        host.send("START_GAME", Map.of());
        assertThat(host.await("ERROR").get("message").asString()).isNotBlank();

        host.send("READY", Map.of("ready", true));
        host.await("ROOM_STATE");
        guest.await("ROOM_STATE");
        guest.send("READY", Map.of("ready", true));
        host.await("ROOM_STATE");
        guest.await("ROOM_STATE");

        host.send("START_GAME", Map.of());
        assertThat(host.await("GAME_STARTED").get("roomId").asString()).isEqualTo(code);
        assertThat(guest.await("GAME_STARTED").get("roomId").asString()).isEqualTo(code);
    }

    // ---------- 인게임 위치 중계 ----------

    @Test
    void positionIsRelayedToOthersButNotBackToSender() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        startedRoom(host, guest);

        host.send("PLAYER_MOVE", Map.of("x", 1.5, "y", 0.0, "z", 3.25, "yaw", 90.0));

        JsonNode moved = guest.await("PLAYER_MOVED");
        assertThat(moved.get("userId").asLong()).isEqualTo(host.userId);
        assertThat(moved.get("x").asDouble()).isEqualTo(1.5);
        assertThat(moved.get("z").asDouble()).isEqualTo(3.25);
        assertThat(moved.get("yaw").asDouble()).isEqualTo(90.0);
        assertThat(host.messageWithin(400)).isNull();
    }

    @Test
    void positionIsIgnoredWhileRoomIsStillWaiting() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = createRoom(host);
        guest.send("JOIN_ROOM", Map.of("roomId", code));
        host.await("ROOM_STATE");
        guest.await("ROOM_STATE");

        host.send("PLAYER_MOVE", Map.of("x", 1.0, "y", 0.0, "z", 1.0));

        assertThat(guest.messageWithin(400)).isNull();
    }

    @Test
    void lastPositionSurvivesDisconnectAndComesBackOnRejoin() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest);
        long guestId = guest.userId;

        guest.send("PLAYER_MOVE", Map.of("x", 7.5, "y", 1.0, "z", -2.5, "yaw", 45.0));
        host.await("PLAYER_MOVED");
        guest.close();
        host.await("ROOM_STATE");

        TestClient returning = connect("영희", "device-b");
        returning.send("JOIN_ROOM", Map.of("roomId", code));

        JsonNode state = returning.await("STATE_SYNC").get("gameState")
                .get("players").get(String.valueOf(guestId));
        assertThat(state.get("x").asDouble()).isEqualTo(7.5);
        assertThat(state.get("z").asDouble()).isEqualTo(-2.5);
        assertThat(state.get("downed").asBoolean()).isTrue();
    }

    // ---------- 끊김과 복귀 ----------

    @Test
    void disconnectDuringPlayKeepsSeatAndGameKeepsRunning() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest);
        long guestId = guest.userId;

        guest.close();

        JsonNode state = host.await("ROOM_STATE");
        assertThat(state.get("status").asString()).isEqualTo("PLAYING");
        assertThat(state.get("players").properties()).hasSize(2);
        assertThat(state.get("disconnected").valueStream()
                .map(JsonNode::asLong).toList()).contains(guestId);
    }

    @Test
    void rejoiningPlayerReceivesGameStateAndComesBackDowned() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest);
        long guestId = guest.userId;
        guest.close();
        host.await("ROOM_STATE");

        TestClient returning = connect("영희", "device-b");
        returning.send("JOIN_ROOM", Map.of("roomId", code));

        JsonNode sync = returning.await("STATE_SYNC");
        assertThat(sync.get("roomId").asString()).isEqualTo(code);
        assertThat(sync.get("gameState").get("players").get(String.valueOf(guestId))
                .get("downed").asBoolean()).isTrue();
        assertThat(host.await("ROOM_STATE").get("disconnected")).isEmpty();
    }

    @Test
    void outsiderCannotJoinRoomThatIsAlreadyPlaying() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest);

        TestClient outsider = connect("민수", "device-c");
        outsider.send("JOIN_ROOM", Map.of("roomId", code));

        assertThat(outsider.await("ERROR").get("message").asString()).isNotBlank();
    }

    @Test
    void leavingHostDelegatesHostToRemainingPlayerWhileWaiting() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = createRoom(host);
        guest.send("JOIN_ROOM", Map.of("roomId", code));
        host.await("ROOM_STATE");
        guest.await("ROOM_STATE");

        host.send("LEAVE_ROOM", Map.of());

        JsonNode state = guest.await("ROOM_STATE");
        assertThat(state.get("hostUserId").asLong()).isEqualTo(guest.userId);
        assertThat(state.get("players").properties()).hasSize(1);
    }

    // ---------- 판 종료 (S15P21B202-316) ----------

    @Test
    void hostEndsGameAndRoomReturnsToWaiting() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        startedRoom(host, guest);

        host.send("GAME_END", Map.of());
        JsonNode state = guest.await("ROOM_STATE");

        assertThat(state.get("status").asString()).isEqualTo("WAITING");
        assertThat(state.get("players").properties()).hasSize(2);
        assertThat(state.get("players").get(String.valueOf(host.userId))
                .get("ready").asBoolean()).isFalse();
    }

    @Test
    void guestCannotEndGame() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        startedRoom(host, guest);

        guest.send("GAME_END", Map.of());

        assertThat(guest.await("ERROR").get("message").asString()).isEqualTo("cannot end game");
    }

    @Test
    void newPlayerCanJoinAfterGameEnds() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest);
        host.send("GAME_END", Map.of());
        host.await("ROOM_STATE");

        TestClient latecomer = connect("민수", "device-c");
        latecomer.send("JOIN_ROOM", Map.of("roomId", code));

        assertThat(latecomer.await("ROOM_STATE").get("players").properties()).hasSize(3);
    }

    // ---------- 도우미 ----------

    private String createRoom(TestClient host) throws Exception {
        host.send("CREATE_ROOM", Map.of());
        String code = host.await("ROOM_CREATED").get("roomId").asString();
        host.await("ROOM_STATE");
        return code;
    }

    /** 두 명이 들어와 준비를 마치고 게임이 시작된 방을 만든다. */
    private String startedRoom(TestClient host, TestClient guest) throws Exception {
        String code = createRoom(host);
        guest.send("JOIN_ROOM", Map.of("roomId", code));
        host.await("ROOM_STATE");
        guest.await("ROOM_STATE");
        host.send("READY", Map.of("ready", true));
        host.await("ROOM_STATE");
        guest.await("ROOM_STATE");
        guest.send("READY", Map.of("ready", true));
        host.await("ROOM_STATE");
        guest.await("ROOM_STATE");
        host.send("START_GAME", Map.of());
        host.await("GAME_STARTED");
        guest.await("GAME_STARTED");
        return code;
    }

    private TestClient connect(String nickname, String deviceId) {
        GuestLoginResponse login = guestLogin(nickname, deviceId);
        TestClient client = new TestClient(login.userId(),
                connectRaw("ws://localhost:" + port + "/ws/game?token=" + login.accessToken()));
        clients.add(client);
        return client;
    }

    private GuestLoginResponse guestLogin(String nickname, String deviceId) {
        try {
            String body = objectMapper.writeValueAsString(new GuestLoginRequest(nickname, deviceId));
            HttpResponse<String> response = httpClient.send(
                    HttpRequest.newBuilder(URI.create("http://localhost:" + port + "/auth/guest"))
                            .header("Content-Type", "application/json")
                            .POST(HttpRequest.BodyPublishers.ofString(body))
                            .build(),
                    HttpResponse.BodyHandlers.ofString());
            assertThat(response.statusCode()).isEqualTo(200);
            return objectMapper.readValue(response.body(), GuestLoginResponse.class);
        } catch (Exception e) {
            throw new IllegalStateException("guest login failed: " + e.getMessage(), e);
        }
    }

    private WebSocketSession connectRaw(String url) {
        BlockingQueue<String> inbox = new LinkedBlockingQueue<>();
        try {
            WebSocketSession session = new StandardWebSocketClient()
                    .execute(new CollectingHandler(inbox), null, URI.create(url))
                    .get(TIMEOUT_SECONDS, TimeUnit.SECONDS);
            session.getAttributes().put("inbox", inbox);
            return session;
        } catch (Exception e) {
            throw new IllegalStateException("handshake failed: " + e.getMessage(), e);
        }
    }

    private static class CollectingHandler extends TextWebSocketHandler {
        private final BlockingQueue<String> inbox;

        CollectingHandler(BlockingQueue<String> inbox) {
            this.inbox = inbox;
        }

        @Override
        protected void handleTextMessage(WebSocketSession session, TextMessage message) {
            inbox.add(message.getPayload());
        }

        @Override
        public void afterConnectionClosed(WebSocketSession session, CloseStatus status) {
            // 테스트에서는 별도 처리 없음
        }
    }

    private class TestClient {
        private final long userId;
        private final WebSocketSession session;

        @SuppressWarnings("unchecked")
        TestClient(long userId, WebSocketSession session) {
            this.userId = userId;
            this.session = session;
        }

        void send(String type, Map<String, Object> data) throws Exception {
            sendRaw(objectMapper.writeValueAsString(Map.of("type", type, "data", data)));
        }

        void sendRaw(String payload) throws Exception {
            session.sendMessage(new TextMessage(payload));
        }

        /** 해당 type의 메시지가 올 때까지 기다렸다가 data를 돌려준다. */
        @SuppressWarnings("unchecked")
        JsonNode await(String type) throws Exception {
            BlockingQueue<String> inbox = (BlockingQueue<String>) session.getAttributes().get("inbox");
            long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(TIMEOUT_SECONDS);
            while (System.nanoTime() < deadline) {
                String raw = inbox.poll(TIMEOUT_SECONDS, TimeUnit.SECONDS);
                if (raw == null) {
                    break;
                }
                JsonNode node = objectMapper.readTree(raw);
                if (type.equals(node.get("type").asString())) {
                    return node.get("data");
                }
            }
            throw new AssertionError(type + " 메시지를 받지 못했습니다");
        }

        /** 주어진 시간 동안 도착한 메시지. 아무것도 안 오면 null (실패 시 내용을 보여주기 위해 문자열을 돌려준다). */
        @SuppressWarnings("unchecked")
        String messageWithin(long ms) throws Exception {
            BlockingQueue<String> inbox = (BlockingQueue<String>) session.getAttributes().get("inbox");
            return inbox.poll(ms, TimeUnit.MILLISECONDS);
        }

        void close() {
            try {
                session.close();
            } catch (Exception ignored) {
                // 이미 닫힌 경우
            }
        }
    }
}
