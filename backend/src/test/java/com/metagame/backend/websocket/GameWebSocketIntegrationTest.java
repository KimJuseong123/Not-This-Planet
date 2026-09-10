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

    /**
     * 재입장한 사람이 <b>어디로 붙어야 하는지</b> 다시 듣는다 (S15P21B202-150).
     *
     * <p>이것이 없어서 재접속이 성립하지 않았다 — 방에는 들어오는데 매치 주소를 못 들어
     * 로비에 갇혔고, 게임이 이미 {@code PLAYING} 이라 시작 버튼도 비활성이었다.
     */
    @Test
    void rejoiningPlayerGetsGameStartedSoItCanEnterTheMatch() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest, "192.168.0.7:7777");
        guest.close();
        host.await("ROOM_STATE");

        TestClient returning = connect("영희", "device-b");
        returning.send("JOIN_ROOM", Map.of("roomId", code));

        JsonNode started = returning.await("GAME_STARTED");
        assertThat(started.get("roomId").asString()).isEqualTo(code);
        assertThat(started.get("hostAddress").asString()).isEqualTo("192.168.0.7:7777");
    }

    /** 대기 중인 방에 일반 입장한 사람에게는 매치 접속 정보를 보내지 않는다. */
    @Test
    void joiningAWaitingRoomDoesNotSendGameStarted() throws Exception {
        TestClient host = connect("철수", "device-a");
        String code = createRoom(host);

        TestClient guest = connect("영희", "device-b");
        guest.send("JOIN_ROOM", Map.of("roomId", code));

        assertThat(guest.await("ROOM_STATE").get("status").asString()).isEqualTo("WAITING");
        assertThatThrownBy(() -> guest.await("GAME_STARTED"))
                .isInstanceOf(AssertionError.class);
    }

    // ---------- 방장 이탈 (S15P21B202-385 · 이전은 -389) ----------

    /**
     * 방장이 게임 중에 나가면 <b>판은 이어지고</b> 남은 사람이 방장이 된다.
     *
     * <p><b>S15P21B202-385 에서 뜻이 바뀐 자리다.</b> 그때는 "방장이 빠지면 판을 끝낸다"
     * 였다 — 방장의 PC 가 곧 게임 서버라(-145) 아무도 이어갈 수 없었기 때문이다. 그리고
     * 상태를 {@code PLAYING} 으로 남기면 로비의 시작 버튼이 비활성이라({@code room.IsWaiting}
     * 조건) 다시 시작할 길조차 없었다.
     *
     * <p>-389 가 이어받는 길을 만들었으므로 이제는 끝내지 않는다. 남은 사람 중 하나가
     * 서버가 되어 그 판을 계속한다. 이어받기가 실패하면 그 사람의 클라이언트가
     * {@code GAME_END} 를 보내 방을 풀어 준다 — 갇히는 길은 그렇게 막혀 있다.
     */
    @Test
    void hostLeavingMidMatchPassesHostAndKeepsTheMatch() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        startedRoom(host, guest);

        host.send("LEAVE_ROOM", Map.of());

        JsonNode state = guest.await("ROOM_STATE");
        assertThat(state.get("status").asString()).isEqualTo("PLAYING");
        assertThat(state.get("hostUserId").asLong()).isEqualTo(guest.userId);
    }

    /** 아무도 남지 않으면 판을 끝낸다 — 이어받을 사람이 없다. */
    @Test
    void hostLeavingAloneEndsTheMatch() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest);

        // 참가자를 먼저 내보내 방장 혼자 남긴다.
        guest.send("LEAVE_ROOM", Map.of());
        host.await("ROOM_STATE");

        host.send("LEAVE_ROOM", Map.of());

        // 방이 사라졌으므로 새로 들어가려면 같은 코드가 없어야 한다.
        TestClient latecomer = connect("민수", "device-c");
        latecomer.send("JOIN_ROOM", Map.of("roomId", code));
        assertThat(latecomer.await("ERROR").get("message").asString()).isNotBlank();
    }

    /** 새 방장이 다시 시작할 길은 남아 있다 — 이어받기가 실패한 경우다. */
    @Test
    void theNewHostCanEndTheMatchAndStartAgain() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest);
        host.send("LEAVE_ROOM", Map.of());
        guest.await("ROOM_STATE");

        // 이어받지 못한 새 방장이 판을 풀어 준다.
        guest.send("GAME_END", Map.of());
        assertThat(guest.await("ROOM_STATE").get("status").asString()).isEqualTo("WAITING");

        guest.send("READY", Map.of("ready", true));
        guest.await("ROOM_STATE");
        guest.send("START_GAME", Map.of());

        assertThat(guest.await("GAME_STARTED").get("roomId").asString()).isEqualTo(code);
    }

    // ---------- 호스트 이전 (S15P21B202-389) ----------

    /**
     * 방장의 연결이 끊기면 <b>유예를 기다리지 않고</b> 방장을 넘긴다.
     *
     * <p>자리는 30초 유예로 남는다(-150) — 그 사람이 돌아올 수 있어야 한다. 하지만 방장
     * 자리는 기다릴 수 없다: 방장의 PC 가 곧 게임 서버라 유예가 끝날 때까지 아무도 게임을
     * 할 수 없다.
     */
    @Test
    void hostDisconnectPassesHostImmediatelyWithoutWaitingForGrace() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        startedRoom(host, guest);

        host.close();

        JsonNode state = guest.await("ROOM_STATE");
        assertThat(state.get("hostUserId").asLong()).isEqualTo(guest.userId);
        assertThat(state.get("status").asString()).isEqualTo("PLAYING");
        // 자리는 남아 있다 — 끊김으로 표시될 뿐이다.
        assertThat(state.get("players").properties()).hasSize(2);
        assertThat(state.get("disconnected").valueStream()
                .anyMatch(id -> id.asLong() == host.userId)).isTrue();
    }

    /** 새 방장이 이어받겠다고 하면 전원이 새 접속 정보를 받는다. */
    @Test
    void hostTakeoverSendsFreshConnectionInfoToEveryone() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        TestClient third = connect("민수", "device-c");
        String code = startedRoomOfThree(host, guest, third);

        host.close();
        guest.await("ROOM_STATE");
        third.await("ROOM_STATE");

        guest.send("HOST_TAKEOVER", Map.of("hostAddress", "192.168.0.9"));

        assertThat(guest.await("GAME_STARTED").get("hostAddress").asString()).isEqualTo("192.168.0.9");
        JsonNode forThird = third.await("GAME_STARTED");
        assertThat(forThird.get("roomId").asString()).isEqualTo(code);
        assertThat(forThird.get("hostAddress").asString()).isEqualTo("192.168.0.9");
    }

    /**
     * 방장이 아닌 사람이 이어받겠다고 해도 아무 일도 일어나지 않는다.
     *
     * <p>막지 않으면 남의 매치를 자기 PC 로 끌어갈 수 있다 — 참가자 전원이 그 사람에게
     * 붙게 된다.
     */
    @Test
    void aNonHostCannotTakeOverTheMatch() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        startedRoom(host, guest, "192.168.0.7");

        guest.send("HOST_TAKEOVER", Map.of("hostAddress", "10.0.0.66"));

        assertThatThrownBy(() -> guest.await("GAME_STARTED"))
                .isInstanceOf(AssertionError.class);
    }

    /** 대기 중인 방에는 이어받을 매치가 없다. */
    @Test
    void takeoverIsIgnoredWhileTheRoomIsWaiting() throws Exception {
        TestClient host = connect("철수", "device-a");
        createRoom(host);

        host.send("HOST_TAKEOVER", Map.of("hostAddress", "192.168.0.9"));

        assertThatThrownBy(() -> host.await("GAME_STARTED"))
                .isInstanceOf(AssertionError.class);
    }

    /**
     * 끊겨 유예를 기다리는 사람은 방장이 되지 않는다.
     *
     * <p>그 사람은 게임 서버가 될 수 없으므로 넘겨도 판이 멈춘다. 예전 {@code leave()} 는
     * {@code players.keySet().findFirst()} 라 그것을 가리지 않았다.
     */
    @Test
    void aDisconnectedPlayerIsNotChosenAsTheNewHost() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        TestClient third = connect("민수", "device-c");
        startedRoomOfThree(host, guest, third);

        // 참가자 하나가 먼저 끊긴다 — 자리는 유예로 남는다.
        guest.close();
        host.await("ROOM_STATE");
        third.await("ROOM_STATE");

        host.close();

        JsonNode state = third.await("ROOM_STATE");
        assertThat(state.get("hostUserId").asLong())
                .as("끊긴 사람이 아니라 붙어 있는 사람이 방장이 되어야 한다")
                .isEqualTo(third.userId);
    }

    /**
     * 방장이 끊기면 <b>옛 접속 정보를 버린다</b> — 돌아온 사람에게 죽은 서버를 알려 주지
     * 않는다 (S15P21B202-389).
     *
     * <p><b>실측으로 드러난 결함이다.</b> 2026-09-10 팀 테스트에서 "호스트가 나갔다가 다시
     * 들어올 때 로딩창에서 연결이 끊긴다" 가 나왔다. 옛 정보를 그대로 주면 두 가지가
     * 깨진다.
     *
     * <p>① 돌아온 옛 방장은 그 릴레이 세션에서 <b>서버 토큰의 주인</b>이다. 우리 릴레이는
     * 누가 서버인지를 토큰으로 가르므로, 참가자로 붙으려 해도 릴레이가 서버로 등록해
     * 버린다 — KCP 핸드셰이크가 끝나지 않아 로딩창에서 멈춘다.
     *
     * <p>② 클라이언트는 {@code GAME_STARTED} 를 받으면 주소가 없어도 매치로 들어간다
     * ({@code LobbyBootstrap} 이 로비 주소나 {@code 127.0.0.1} 로 대신 붙는다). 그래서
     * 방 번호만 보내는 것도 안 된다 — 아무것도 보내지 않아야 로비에 남는다.
     */
    @Test
    void afterHostPassesTheOldConnectionInfoIsDropped() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest, "192.168.0.7");

        host.close();
        guest.await("ROOM_STATE");                 // 방장이 넘어갔다

        // 옛 방장이 돌아온다. 같은 deviceId 라 같은 userId 다.
        TestClient returned = connect("철수", "device-a");
        returned.send("JOIN_ROOM", Map.of("roomId", code));

        returned.await("STATE_SYNC");              // 방에는 들어간다
        assertThatThrownBy(() -> returned.await("GAME_STARTED"))
                .as("죽은 서버의 접속 정보를 주면 로딩창에서 멈춘다")
                .isInstanceOf(AssertionError.class);
    }

    /**
     * 새 방장이 이어받으면 <b>그 사이 돌아온 사람까지</b> 새 정보를 받는다 — 스스로 낫는다.
     *
     * <p>위 시험이 "아무것도 안 보낸다" 를 못 박았으므로, 그 사람이 영원히 로비에 갇히지
     * 않는다는 것도 함께 못 박아야 한다.
     */
    @Test
    void theTakeoverAlsoReachesSomeoneWhoCameBackMeanwhile() throws Exception {
        TestClient host = connect("철수", "device-a");
        TestClient guest = connect("영희", "device-b");
        String code = startedRoom(host, guest, "192.168.0.7");

        host.close();
        guest.await("ROOM_STATE");

        TestClient returned = connect("철수", "device-a");
        returned.send("JOIN_ROOM", Map.of("roomId", code));
        returned.await("STATE_SYNC");

        // 새 방장이 이어받는다.
        guest.send("HOST_TAKEOVER", Map.of("hostAddress", "192.168.0.9"));

        assertThat(guest.await("GAME_STARTED").get("hostAddress").asString()).isEqualTo("192.168.0.9");
        assertThat(returned.await("GAME_STARTED").get("hostAddress").asString())
                .as("그 사이 돌아온 사람도 새 방장에게 붙을 수 있어야 한다")
                .isEqualTo("192.168.0.9");
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
        return startedRoom(host, guest, "");
    }

    /**
     * 방장 주소를 실어 시작한다. 재입장 때 그 값이 되돌아오는지 보려면 필요하다
     * (S15P21B202-150).
     */
    private String startedRoom(TestClient host, TestClient guest, String hostAddress)
            throws Exception {
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
        host.send("START_GAME", hostAddress.isBlank()
                ? Map.of()
                : Map.of("hostAddress", hostAddress));
        host.await("GAME_STARTED");
        guest.await("GAME_STARTED");
        return code;
    }

    /**
     * 셋이 들어간 판을 시작한다 (S15P21B202-389).
     *
     * <p>이전은 <b>셋 이상이어야 제대로 보인다</b>. 둘이면 남는 사람이 하나뿐이라
     * "끊긴 사람을 고르지 않는다" 나 "나머지 전원에게 새 정보가 간다" 를 확인할 수 없다.
     */
    private String startedRoomOfThree(TestClient host, TestClient guest, TestClient third)
            throws Exception {
        String code = createRoom(host);

        guest.send("JOIN_ROOM", Map.of("roomId", code));
        drainRoomState(host, guest);

        third.send("JOIN_ROOM", Map.of("roomId", code));
        drainRoomState(host, guest, third);

        for (TestClient client : new TestClient[] { host, guest, third }) {
            client.send("READY", Map.of("ready", true));
            drainRoomState(host, guest, third);
        }

        host.send("START_GAME", Map.of("hostAddress", "192.168.0.7"));
        host.await("GAME_STARTED");
        guest.await("GAME_STARTED");
        third.await("GAME_STARTED");
        return code;
    }

    /** 방 상태 방송을 사람 수만큼 받아 비운다 — 남겨 두면 다음 await 가 옛 것을 집는다. */
    private void drainRoomState(TestClient... clients) throws Exception {
        for (TestClient client : clients) {
            client.await("ROOM_STATE");
        }
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

    /**
     * 한 접속의 테스트 쪽 끝. 서버가 보낸 메시지를 큐에 모아 두고 타입으로 꺼내 쓴다.
     *
     * <p><b>받은 메시지를 버리지 않는다</b>(S15P21B202-302). 예전 {@code await(type)} 는
     * 큐에서 꺼낸 메시지가 찾던 타입이 아니면 <b>그대로 버렸다.</b> 서버는 한 사람에게
     * 여러 종류를 보내고 그 순서가 매 실행마다 같지 않으므로, 앞선 {@code await} 가
     * 뒤에 필요한 메시지를 먹어 버리면 그 {@code await} 가 타임아웃으로 떨어진다 —
     * 그래서 <b>같은 코드가 실행마다 다른 테스트에서</b> 실패했다(2026-09-04 실측:
     * 3회 실행에서 실패한 테스트가 매번 달랐고 떨어지는 줄은 언제나 {@code await} 였다).
     *
     * <p>이제 찾던 타입이 아닌 메시지는 {@link #held} 에 남겨 두고, 다음 {@code await}
     * 가 거기서 먼저 찾는다. 그래서 <b>서버가 보내는 순서와 무관</b>해진다 —
     * 대기 시간을 늘리는 것과 달리 이건 경계를 미루는 것이 아니라 없애는 것이다.
     */
    private class TestClient {
        private final long userId;
        private final WebSocketSession session;

        /** {@code await} 가 찾던 타입이 아니어서 아직 쓰이지 않은 메시지. 버리지 않는다. */
        private final List<String> held = new ArrayList<>();

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

        @SuppressWarnings("unchecked")
        private BlockingQueue<String> inbox() {
            return (BlockingQueue<String>) session.getAttributes().get("inbox");
        }

        /**
         * payload 의 {@code type}. 없으면 자리표시자를 준다.
         *
         * <p>{@code get("type")} 이 null 일 때 NPE 를 던지면, 특히 실패 메시지를 만드는
         * 루프에서 터져 <b>원래의 AssertionError(무엇을 기다렸는지)가 무관한 NPE 로
         * 바뀐다</b> — 진단이 더 어려워지는 쪽이다.
         */
        private String typeOf(String raw) throws Exception {
            JsonNode ty = objectMapper.readTree(raw).get("type");
            return ty == null ? "<타입 없음>" : ty.asString();
        }

        /**
         * 해당 type의 메시지가 올 때까지 기다렸다가 data를 돌려준다.
         *
         * <p>이미 받아 둔 것({@link #held})에서 먼저 찾고, 없으면 <b>남은 예산만큼</b>
         * 기다린다. 예전에는 반복마다 {@code poll} 에 전체 타임아웃을 줘서 총 대기가
         * 타임아웃의 몇 배까지 늘 수 있었다. 찾던 타입이 아닌 것은 버리지 않고 남긴다.
         */
        JsonNode await(String type) throws Exception {
            for (int i = 0; i < held.size(); i++) {
                if (type.equals(typeOf(held.get(i)))) {
                    return objectMapper.readTree(held.remove(i)).get("data");
                }
            }
            long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(TIMEOUT_SECONDS);
            for (long left = deadline - System.nanoTime(); left > 0; left = deadline - System.nanoTime()) {
                String raw = inbox().poll(left, TimeUnit.NANOSECONDS);
                if (raw == null) {
                    break;
                }
                if (type.equals(typeOf(raw))) {
                    return objectMapper.readTree(raw).get("data");
                }
                held.add(raw);
            }
            // 무엇을 받았는지 함께 남긴다 — 없으면 실패 로그만 보고는 원인을 알 수 없다.
            List<String> seen = new ArrayList<>();
            for (String raw : held) {
                seen.add(typeOf(raw));
            }
            throw new AssertionError(type + " 메시지를 " + TIMEOUT_SECONDS
                    + "초 안에 받지 못했습니다 — 그동안 받은 것: " + seen);
        }

        /**
         * 주어진 시간 동안 <b>새로</b> 도착한 메시지. 아무것도 안 오면 null
         * (실패 시 내용을 보여주기 위해 문자열을 돌려준다).
         *
         * <p><b>{@link #held} 는 보지 않는다.</b> 한때 held 를 먼저 돌려주게 했는데
         * 되돌렸다 — 이 메서드는 "아무것도 오지 않아야 한다" 단정에만 쓰이고, 그 단정
         * 바로 앞에 같은 클라이언트의 {@code await} 가 있다. 앞선 {@code await} 가
         * 기대되던 다른 메시지를 남겨 뒀을 뿐인데도 단정이 <b>거짓으로 실패</b>하게 된다
         * (플래키의 방향만 뒤집는 것이다).
         *
         * <p>대신 남는 구멍을 적어 둔다: 서버가 <b>예상 밖</b> 메시지를 보냈고 앞선
         * {@code await} 가 그것을 held 에 담았으면 이 단정은 통과한다. 이 구멍은
         * S15P21B202-302 이전에도 같았다 — 그때는 {@code await} 가 아예 버렸으므로
         * 결과가 같다. 예상 밖 메시지를 잡는 것은 이 단정의 목적이 아니고, 필요하면
         * 별 단정으로 held 를 직접 보는 것이 맞다.
         */
        String messageWithin(long ms) throws Exception {
            return inbox().poll(ms, TimeUnit.MILLISECONDS);
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
