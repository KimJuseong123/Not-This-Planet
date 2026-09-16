package com.metagame.backend.websocket;

import com.metagame.backend.domain.game.GameState;
import com.metagame.backend.domain.room.Room;
import com.metagame.backend.domain.room.RoomManager;
import com.metagame.backend.domain.user.UserRepository;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;
import org.springframework.web.socket.CloseStatus;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketSession;
import org.springframework.web.socket.handler.ConcurrentWebSocketSessionDecorator;
import org.springframework.web.socket.handler.WebSocketSessionDecorator;
import org.springframework.web.socket.handler.TextWebSocketHandler;
import tools.jackson.databind.JsonNode;
import tools.jackson.databind.ObjectMapper;

@Slf4j
@Component
@RequiredArgsConstructor
public class GameWebSocketHandler extends TextWebSocketHandler {

    private static final String ROOM_ID_ATTR = "roomId";
    private static final String NICKNAME_ATTR = "nickname";

    /**
     * 이 사람이 알려 준 자기 랜 주소(예 192.168.31.66).
     *
     * <p>공인 IP 만으로는 같은 망인지 알 수 없다. 규모가 큰 망은 공인 IP 를 여러 개
     * 돌려쓰기 때문에, 같은 WiFi 에 앉아 있어도 서로 다른 공인 IP 로 보인다
     * (2026-09-02 SSAFY 교육장 실측 — 같은 WiFi 인데 릴레이를 탔다).
     */
    private static final String LOCAL_IP_ATTR = "localIp";
    private static final String UNKNOWN_NICKNAME = "비행사";

    private final RoomManager roomManager;
    private final ReconnectGraceManager graceManager;
    private final UserRepository userRepository;
    private final ObjectMapper objectMapper;
    private final RelayProvider relayService;

    /**
     * 같은 망이어도 <b>무조건 릴레이를 태운다</b> (S15P21B202-570).
     *
     * <p><b>같은 공유기 뒤라고 서로 닿는 것은 아니다.</b> 공유기가 기기끼리의 통신을 막아
     * 두면(AP isolation) 사설 주소로 못 붙는다 — 교육장 WiFi 가 그럴 수 있고, 그때는
     * 판정이 아무리 맞아도 입장이 안 된다({@code -145 §4-3} 이 핫스팟 우회를 적어 둔 이유).
     *
     * <p>그 자리에서는 이 스위치를 켠다. 지연이 늘고 릴레이 동시접속을 쓰지만 <b>되는 것이
     * 빠른 것보다 먼저다.</b> 기본은 꺼짐이라 배포 서버의 동작은 그대로다.
     */
    @Value("${relay.always:false}")
    private boolean alwaysRelay;

    // userId → 세션. 브로드캐스트 대상 조회용.
    // **여기 담기는 것은 동시 쓰기를 직렬화하도록 감싼 세션이다**(S15P21B202-313 ③).
    private final Map<Long, WebSocketSession> sessions = new ConcurrentHashMap<>();

    /** 한 세션의 쓰기가 이만큼 밀리면 그 세션을 닫는다. 느린 클라이언트가 방 전체를 붙잡지 않게. */
    private static final int SEND_TIME_LIMIT_MS = 5_000;
    /** 밀린 메시지 버퍼 상한. 우리 메시지는 작아서 이 정도면 한참 밀려도 여유가 있다. */
    private static final int SEND_BUFFER_LIMIT_BYTES = 256 * 1024;
    /**
     * roomId → 그 매치의 접속 정보 (S15P21B202-150).
     *
     * <p>예전에는 릴레이 <b>세션 id 만</b> 담았다(방이 사라질 때 닫으려고 — -299).
     * 그래서 재입장한 사람에게 다시 줄 것이 남아 있지 않았다. 방장 주소와 릴레이 세션을
     * 함께 들고 있으면 {@code GAME_STARTED} 를 그 사람 몫으로 다시 만들 수 있다.
     */
    private final Map<String, MatchEntry> matches = new ConcurrentHashMap<>();

    /**
     * 한 매치에 붙는 데 필요한 것. {@code relay} 는 같은 망이면 null 이다(직접 접속).
     *
     * @param hostAddress 방장이 알려 준 접속 주소. 서버가 보는 공인 IP 로는 안 된다(-263)
     * @param relay       릴레이 세션. 사람별 토큰이 여기 있다
     */
    private record MatchEntry(String hostAddress, RelaySession relay) {
    }

    /** 닉네임은 연결 시 한 번만 조회해 세션에 보관한다 (브로드캐스트마다 DB를 보지 않기 위해). */
    @Override
    public void afterConnectionEstablished(WebSocketSession session) {
        Long userId = userId(session);
        // **세션당 쓰기를 직렬화한다**(S15P21B202-313 ③). Spring 의 WebSocketSession 은
        // 스레드 안전하지 않은데 같은 세션에 쓰는 주체가 여럿이다 — 다른 플레이어들의
        // PLAYER_MOVED 중계(입력 주기마다), 접속 종료 처리, 그리고 **별 스레드**인 유예 스케줄러.
        //
        // 2026-09-07 EC2 실측에서 Tomcat 이 던지는 것을 현장에서 잡았다:
        //   IllegalStateException: The remote endpoint was in state [TEXT_PARTIAL_WRITING]
        // 그 메시지는 그대로 유실됐고, 받아야 할 클라이언트는 5초를 굶고 실패했다.
        sessions.put(userId, new ConcurrentWebSocketSessionDecorator(
                session, SEND_TIME_LIMIT_MS, SEND_BUFFER_LIMIT_BYTES));
        session.getAttributes().put(NICKNAME_ATTR, userRepository.findById(userId)
                .map(user -> user.getNickname())
                .orElse(UNKNOWN_NICKNAME));
        log.info("connected: userId={}", userId);
    }

    /**
     * 잘못된 메시지 하나로 연결이 끊기지 않도록, 해석·처리 실패는 ERROR 응답으로 돌려준다.
     * (모르는 type은 enum 변환에서, 빠진 필드는 처리 중에 예외가 난다)
     */
    @Override
    protected void handleTextMessage(WebSocketSession session, TextMessage message) {
        Long userId = userId(session);

        WsMessage msg;
        WsMessage.Type type;
        try {
            msg = objectMapper.readValue(message.getPayload(), WsMessage.class);
            type = WsMessage.Type.valueOf(msg.type());
        } catch (RuntimeException e) {
            send(session, WsMessage.Type.ERROR, Map.of("message", "unknown type"));
            return;
        }

        try {
            // 방을 만들거나 들어올 때 자기 랜 주소를 함께 보낸다. 없으면 없는 대로 둔다 —
            // 옛 클라이언트도 계속 붙을 수 있어야 한다.
            rememberLocalIp(session, msg.data());

            switch (type) {
                case CREATE_ROOM -> createRoom(session, userId, msg.data());
                case JOIN_ROOM -> joinRoom(session, userId, msg.data().get("roomId").asString());
                case LEAVE_ROOM -> leaveRoom(session, userId);
                case READY -> ready(userId, msg.data().get("ready").asBoolean());
                case START_GAME -> startGame(userId, msg.data());
                case GAME_END -> endGame(userId);
                case HOST_TAKEOVER -> hostTakeover(userId, msg.data());
                case PLAYER_MOVE -> playerMove(userId, msg.data());
                default -> send(session, WsMessage.Type.ERROR, Map.of("message", "unknown type"));
            }
        } catch (RuntimeException e) {
            log.warn("message handling failed: userId={} type={} : {}", userId, type, e.toString());
            send(session, WsMessage.Type.ERROR, Map.of("message", "invalid message"));
        }
    }

    /**
     * WAITING 상태에서는 즉시 퇴장시킨다.
     * PLAYING 상태에서는 유예 시간 동안 자리를 유지하고, 넘기면 그 사람만 제외한 뒤
     * 남은 인원으로 게임을 계속한다 (지침 문서 5-1).
     */
    @Override
    public void afterConnectionClosed(WebSocketSession session, CloseStatus status) {
        Long userId = userId(session);
        // 재접속으로 이미 새 세션이 등록된 뒤 옛 세션이 닫히는 경우가 있어, 같은 세션일 때만 정리한다.
        // 저장된 것은 감싼 세션이므로 **객체 동일성으로 비교할 수 없다** — 감싼 대상을 벗겨서 본다.
        // remove(k, v) 로 원자적으로 지우고, 그 사이 새 세션이 들어왔으면 정리하지 않는다.
        WebSocketSession stored = sessions.get(userId);
        if (stored == null
                || WebSocketSessionDecorator.unwrap(stored) != WebSocketSessionDecorator.unwrap(session)
                || !sessions.remove(userId, stored)) {
            log.info("stale session closed: userId={}", userId);
            return;
        }

        String roomId = (String) session.getAttributes().get(ROOM_ID_ATTR);
        Room room = roomId == null ? null : roomManager.find(roomId);
        if (room == null) {
            log.info("disconnected: userId={}", userId);
            return;
        }

        if (room.getStatus() == Room.Status.PLAYING) {
            room.markDisconnected(userId);

            // 방장이 끊겼으면 <b>기다리지 않고</b> 방장만 넘긴다 (S15P21B202-389).
            //
            // 자리는 위에서 유예로 남겼고 그것은 그대로 둔다 — 그 사람이 30초 안에
            // 돌아올 수 있어야 한다(-150). 하지만 방장 자리는 기다릴 수 없다: 방장의
            // PC 가 곧 게임 서버라(-145) 유예가 끝날 때까지 아무도 게임을 할 수 없다.
            //
            // 새 방장에게 따로 알리지 않는다. 바로 아래 broadcastRoomState 의
            // ROOM_STATE 가 이미 hostUserId 를 싣고, 클라이언트는 그것으로
            // IsRoomHost 를 판단한다(NetworkSession) — "게임 중인데 내가 방장이 되었다"
            // 를 보면 스스로 HOST_TAKEOVER 를 보낸다.
            Long newHost = userId.equals(room.getHostUserId())
                    ? room.passHostToConnected()
                    : null;

            // 방장이 넘어갔으면 <b>옛 접속 정보를 버린다</b> (S15P21B202-389).
            //
            // 그 정보는 이제 없는 서버를 가리킨다. 그대로 두면 두 가지가 깨진다.
            //
            // <p>① <b>돌아온 옛 방장이 릴레이에서 서버 토큰의 주인이다.</b> 우리 릴레이는
            // 누가 서버인지를 토큰으로 가르므로({@code metagame_relay.py} 의
            // {@code host_token}), 그 사람이 참가자로 붙으려 해도 릴레이가 <b>서버로
            // 등록</b>해 버린다. KCP 핸드셰이크가 끝나지 않아 로딩창에서 멈춘다.
            //
            // <p>② 남은 사람이 재입장하면 죽은 방장의 주소를 다시 받아 그리로 붙으려 한다.
            //
            // 버리면 {@link #sendGameStarted} 가 접속 정보를 못 만들어 아무것도 보내지
            // 않고, 그 사람은 로비에서 기다린다. 새 방장이 {@code HOST_TAKEOVER} 를 보내는
            // 순간 그 사람까지 포함해 전원에게 새 정보가 간다 — 스스로 낫는다.
            if (newHost != null) {
                closeRelay(roomId);
            }

            broadcastRoomState(room);
            graceManager.schedule(roomId, userId, () -> onGraceExpired(roomId, userId));

            if (newHost != null) {
                log.info("host disconnected, passing host: room={} old={} new={}",
                        roomId, userId, newHost);
            } else {
                log.info("disconnected (grace): userId={} room={}", userId, roomId);
            }
        } else {
            removeFromRoom(room, userId);
            log.info("disconnected (left): userId={} room={}", userId, roomId);
        }
    }

    /** 유예 시간 만료 — 참가자 목록에서 제외한다. 게임은 남은 인원으로 계속된다. */
    private void onGraceExpired(String roomId, Long userId) {
        Room room = roomManager.find(roomId);
        if (room == null) {
            return;   // 방이 사라졌다
        }
        // **판정과 제거가 한 임계구역이어야 한다** (S15P21B202-313 ①). 둘 사이에 재입장이
        // 끼어들면 막 돌아온 사람을 지우게 된다. 방송·릴레이 호출은 락 밖에서 한다.
        synchronized (room) {
            if (!room.isDisconnected(userId)) {
                return;   // 이미 복귀했다
            }
            room.leave(userId);
        }
        afterLeave(room);
        log.info("grace expired, removed: userId={} room={}", userId, roomId);
    }

    private void removeFromRoom(Room room, Long userId) {
        boolean matchEnded;

        synchronized (room) {
            // 방장이 누구였는지는 leave() 앞에서 봐야 한다 — leave() 가 다음 사람에게
            // 방장을 넘기므로 뒤에서 보면 이미 남이다.
            boolean wasHost = userId.equals(room.getHostUserId());
            boolean wasPlaying = room.getStatus() == Room.Status.PLAYING;

            room.leave(userId);

            // 방장이 게임 중에 빠졌고 <b>이어받을 사람이 없으면</b> 판을 끝낸다
            // (S15P21B202-385 · 조건을 -389 에서 좁혔다).
            //
            // 방장의 PC 가 곧 게임 서버다(-145 호스트 권위). 예전에는 방장만 다음 사람에게
            // 넘기고 상태를 PLAYING 으로 남겼고, 그래서 참가자가 로비로 돌아가도 시작
            // 버튼이 비활성이라(LobbyHud 의 room.IsWaiting 조건) 새 방장이 다시 시작할 수
            // 없었다. -385 는 그것을 "방장이 빠지면 판을 끝낸다" 로 고쳤다.
            //
            // <b>-389 가 이어받는 길을 만들면서 그 조건이 너무 넓어졌다.</b> 남은 사람이
            // 있으면 그 중 하나가 서버가 되어 판을 이어갈 수 있으므로, 여기서 끝내면
            // 이어받기가 시작되기도 전에 방이 대기로 돌아간다. 그래서 <b>아무도 남지
            // 않았을 때만</b> 끝낸다 — leave() 가 방장을 못 채우면(null) 그것이 곧
            // "남은 사람이 없다" 다.
            //
            // 잠깐 끊긴 것과는 구분된다. 게임 중 연결이 끊기면 이 함수가 아니라 유예
            // 스케줄러로 가고, 30초 안에 돌아오면 여기 오지 않는다.
            matchEnded = wasHost && wasPlaying && room.getHostUserId() == null && room.end();
        }

        afterLeave(room);

        if (matchEnded) {
            log.info("host left with nobody to take over, room back to WAITING: room={} host={}",
                    room.getRoomId(), userId);
        }
    }

    /** 자리에서 빠진 뒤의 정리. **락 밖에서** 한다 — 방송과 릴레이 호출이 들어 있다. */
    private void afterLeave(Room room) {
        String roomId = room.getRoomId();
        roomManager.removeIfEmpty(roomId);
        // 방이 실제로 사라졌으면 릴레이 세션도 닫는다. removeIfEmpty 는 void 라 조회로 확인한다.
        if (roomManager.find(roomId) == null) {
            closeRelay(roomId);
        }
        broadcastRoomState(room);
    }

    /** 그 방의 릴레이 세션을 닫고 접속 정보를 버린다. 없으면 아무 것도 하지 않는다. */
    private void closeRelay(String roomId) {
        MatchEntry entry = matches.remove(roomId);
        if (entry != null && entry.relay() != null) {
            relayService.close(entry.relay().sessionId());
        }
    }

    /**
     * 방을 만든다. <b>맵을 함께 보내면 그 맵으로</b>, 안 보내면 기본 맵으로 선다.
     *
     * <p>보내지 않아도 되게 둔 이유는 {@code localAddress} 와 같다 — 옛 클라이언트도
     * 그대로 방을 만들 수 있어야 한다. 맵 이름을 모르는 클라이언트는
     * {@link Room#DEFAULT_STAGE_ID} 로 선다.
     */
    private void createRoom(WebSocketSession session, Long userId, JsonNode data) {
        Room room = roomManager.createRoom(userId, nickname(session));
        if (data != null && data.has("stageId")) {
            room.setStageId(data.get("stageId").asString());
        }
        session.getAttributes().put(ROOM_ID_ATTR, room.getRoomId());
        send(session, WsMessage.Type.ROOM_CREATED,
                Map.of("roomId", room.getRoomId(), "stageId", stageIdOf(room)));
        broadcastRoomState(room);
    }

    /**
     * 대기 중인 방에는 일반 입장, 진행 중인 방에는 복귀(재입장)로 처리한다.
     * 복귀는 그 판에 참가했던 유저만 허용되며, 기획에 따라 기절 상태로 돌아온다.
     */
    private void joinRoom(WebSocketSession session, Long userId, String roomId) {
        Room room = roomManager.find(roomId);
        if (room == null) {
            sendJoinError(session, "ROOM_NOT_FOUND", "cannot join room");
            return;
        }

        if (room.getStatus() == Room.Status.PLAYING) {
            // 재입장 판정과 유예 취소를 **한 임계구역**에서 한다 (S15P21B202-313 ①).
            // 예전에는 유예 만료 스레드가 isDisconnected 를 읽은 직후 여기서 재입장이 성공하면,
            // 그 스레드가 **막 돌아온 사람을 방에서 지웠다.** 그 사람은 playerIds() 에서 빠져
            // ROOM_STATE·PLAYER_MOVED 를 하나도 못 받는 유령이 됐다.
            // cancel 로는 막을 수 없다 — 만료 태스크가 먼저 pending 에서 자기를 지우기 때문에
            // cancel 이 키를 찾지도 못한다(ReconnectGraceManager 참고).
            Room.RejoinResult result;
            synchronized (room) {
                result = room.rejoin(userId, nickname(session));
                if (result == Room.RejoinResult.OK) {
                    // **정규화된** 방 코드로 취소한다 (S15P21B202-313 ②). 예전에는 클라이언트가
                    // 보낸 원본 문자열을 썼는데 타이머는 정규화된 코드로 걸려 있어,
                    // 소문자 코드로 재입장하면 취소가 **반드시** 빗나갔다.
                    //
                    // 취소할 것이 없으면 경고한다 — 여기 도달했다는 것은 PLAYING 인 방에
                    // 끊겨 있던 사람이 돌아왔다는 뜻이고, 그러면 타이머가 있어야 정상이다.
                    // 없다면 키가 어긋났거나 유예가 이미 만료된 것이다. 조용히 넘기면
                    // ② 같은 실수가 또 눈에 안 보인다.
                    if (!graceManager.cancel(room.getRoomId(), userId)) {
                        log.warn("rejoin: 취소할 유예 타이머가 없다 — 키 불일치 또는 이미 만료. "
                                + "room={} user={}", room.getRoomId(), userId);
                    }
                }
            }
            if (result != Room.RejoinResult.OK) {
                boolean full = result == Room.RejoinResult.FULL;
                sendJoinError(session,
                        full ? "ROOM_FULL" : "REJOIN_DENIED",
                        full ? "room is full" : "cannot join room");
                return;
            }
            session.getAttributes().put(ROOM_ID_ATTR, room.getRoomId());   // 클라이언트가 보낸 대소문자가 아닌 정규화된 코드를 보관
            if (room.getGameState() != null) {
                room.getGameState().setDowned(userId, true);   // 재입장 시 기절 상태로 복귀
            }
            sendStateSync(session, room);
            broadcastRoomState(room);
            // 어디로 붙어야 하는지 다시 알려 준다 (S15P21B202-150).
            //
            // 이것이 없어서 재접속이 성립하지 않았다 — 방에는 들어오는데 매치 주소와
            // 사람별 릴레이 토큰을 못 들어 로비에 갇혔다. 게임이 이미 PLAYING 이라
            // 시작 버튼도 비활성이므로(LobbyHud 의 room.IsWaiting 조건) 들어갈 길이 없었다.
            //
            // 클라이언트는 GAME_STARTED 를 받으면 알아서 매치로 들어간다
            // (NetworkSession 이 주소를 담고 LobbyBootstrap.EnterMatch 가 씬을 넘긴다).
            sendGameStarted(session, room, userId);
            log.info("rejoined: userId={} room={}", userId, roomId);
            return;
        }

        // 왜 안 되는지까지 받아 그대로 전달한다 (S15P21B202-413). 꽉 찬 방과 이미 시작된
        // 방은 사용자가 해야 할 일이 다르다 — 앞은 기다리면 되고 뒤는 다른 방을 찾아야 한다.
        //
        // 여기까지 왔으면 방은 WAITING 이므로 대개 OK 아니면 ROOM_FULL 이다. ROOM_PLAYING 은
        // 위 검사와 이 호출 사이에 방장이 시작을 누른 경우에만 오는데, tryJoin 이 그것을
        // 스스로 판단하므로 여기서 다시 세지 않는다.
        Room.JoinResult joined = room.tryJoin(userId, nickname(session));
        if (joined != Room.JoinResult.OK) {
            sendJoinError(session,
                    joined == Room.JoinResult.ROOM_FULL ? "ROOM_FULL" : "ROOM_PLAYING",
                    joined == Room.JoinResult.ROOM_FULL ? "room is full" : "cannot join room");
            return;
        }
        session.getAttributes().put(ROOM_ID_ATTR, room.getRoomId());   // 클라이언트가 보낸 대소문자가 아닌 정규화된 코드를 보관
        broadcastRoomState(room);
    }

    private void leaveRoom(WebSocketSession session, Long userId) {
        String roomId = (String) session.getAttributes().remove(ROOM_ID_ATTR);
        if (roomId == null) {
            return;
        }
        graceManager.cancel(roomId, userId);
        Room room = roomManager.find(roomId);
        if (room != null) {
            removeFromRoom(room, userId);
        }
    }

    private void ready(Long userId, boolean ready) {
        Room room = roomOf(userId);
        if (room != null) {
            room.setReady(userId, ready);
            broadcastRoomState(room);
        }
    }

    private void startGame(Long userId, JsonNode data) {
        Room room = roomOf(userId);
        if (room == null || !userId.equals(room.getHostUserId()) || !room.start()) {
            WebSocketSession session = sessions.get(userId);
            if (session != null) {
                send(session, WsMessage.Type.ERROR, Map.of("message", "cannot start game"));
            }
            return;
        }

        // 방장의 클라이언트가 곧 게임 서버다(S15P21B202-263). 참가자들이 어디로 붙을지는
        // 방장만 알 수 있으므로 START_GAME 에 실려 온 값을 그대로 전달한다.
        // 서버가 보는 방장의 주소(공인 IP)로는 안 된다 — 공유기가 밖에서 들어오는
        // 연결을 막기 때문에 같은 네트워크 안에서 쓰는 사설 주소여야 한다.
        String hostAddress = data != null && data.hasNonNull("hostAddress")
                ? data.get("hostAddress").asText("")
                : "";

        // 서로 다른 네트워크에서도 붙으려면 릴레이를 거쳐야 한다(-264).
        // 세션을 열려면 참가자 전원의 공인 IP 가 필요한데 그 목록은 서버만 갖고 있다.
        // 실패하면 null 이고, 그때는 위의 직접 접속 주소로 간다.
        LinkedHashMap<Long, String> publicIps = publicIpsOf(room);
        boolean same = !alwaysRelay && sameNetwork(room, publicIps);
        RelaySession relay = same ? null : relayService.open(publicIps, room.getHostUserId());

        // 재입장한 사람에게 다시 보낼 수 있게 방마다 기억한다 (S15P21B202-150).
        // **릴레이가 null 이어도 보관한다** — 같은 망 직접 접속도 주소를 다시 줘야 한다.
        matches.put(room.getRoomId(), new MatchEntry(hostAddress, relay));

        // 무엇을 골랐는지 남긴다. 릴레이가 안 붙었을 때 어디서 갈렸는지 알아야 한다 —
        // 실패하면 조용히 직접 접속으로 되돌아가므로 로그가 없으면 원인을 못 찾는다.
        // IP 는 개인정보라 개수만 센다.
        long localCount = publicIps.values().stream().filter(GameWebSocketHandler::isLocalIp).count();
        log.info("game start: room={} players={} distinctIps={} localIps={} decision={}",
                room.getRoomId(), publicIps.size(),
                publicIps.values().stream().distinct().count(),
                localCount,
                same ? "direct(same network)" : (relay != null ? "relay" : "direct(relay failed)"));

        // 같은 망 판별이 왜 그렇게 나왔는지 남긴다 — **세 값이 곧 판정 근거다.**
        // IP 는 개인정보라 종류 수만 센다. 공인 대역이 둘 이상이면 서로 다른 망이라
        // 보고 릴레이로 간다(S15P21B202-570): 랜 대역은 망이 달라도 겹치기 때문이다.
        List<String> localIps = localIpsOf(room);
        log.info("same network 판별: 공인IP종류={} · 공인대역종류={} · 랜대역종류={}",
                publicIps.values().stream().distinct().count(),
                publicIps.values().stream().map(GameWebSocketHandler::prefix24).distinct().count(),
                localIps.stream().map(GameWebSocketHandler::subnetOf).distinct().count());

        if (localCount > 0) {
            log.warn("공인 IP 를 못 읽었다 — nginx 가 X-Forwarded-For 를 넘기는지 확인할 것. "
                    + "이대로면 모두 같은 주소로 보여 릴레이가 켜지지 않는다(-264).");
        }

        for (Long playerId : room.playerIds()) {
            WebSocketSession target = sessions.get(playerId);
            if (target == null || !target.isOpen()
                    || !room.getRoomId().equals(target.getAttributes().get(ROOM_ID_ATTR))) {
                continue;
            }

            sendGameStarted(target, room, playerId);
        }
    }

    /**
     * 이 사람 몫의 {@code GAME_STARTED} 를 보낸다 — 매치에 붙는 데 필요한 것 전부.
     *
     * <p><b>게임 시작과 재입장이 같은 코드를 쓴다</b>(S15P21B202-150). 예전에는 시작
     * 경로에만 있었고, 그래서 재입장한 사람은 방에는 들어오지만 <b>어디로 붙어야 하는지를
     * 못 들었다.</b> 로비에 갇히고, 게임이 이미 {@code PLAYING} 이라 시작 버튼도 비활성이라
     * 들어갈 길이 없었다.
     *
     * <p>사람마다 {@code userToken} 이 다르므로 방송하지 않고 각자에게 따로 보낸다.
     */
    private void sendGameStarted(WebSocketSession target, Room room, Long playerId) {
        MatchEntry entry = matches.get(room.getRoomId());

        Map<String, Object> payload = new LinkedHashMap<>();
        payload.put("roomId", room.getRoomId());

        // 어느 맵을 열지는 서버가 말해 준다 (2맵 준비). 클라이언트가 씬 이름을 스스로
        // 고르면 재입장한 사람이 다른 맵을 여는 날이 온다 — 같은 판에 두 개의 맵이 생긴다.
        payload.put("stageId", stageIdOf(room));

        if (entry == null) {
            // 접속 정보가 없다 — 방장이 사라져 옛 정보를 버렸거나(-389), 서버가 재시작된
            // 뒤 진행 중인 방이 남은 경우다.
            //
            // <b>이때는 아무것도 보내지 않는다</b>(S15P21B202-389). 예전에는 방 번호만
            // 실어 보냈는데, 클라이언트는 {@code GAME_STARTED} 를 받으면 <b>주소가 없어도
            // 매치로 들어간다</b> — {@code LobbyBootstrap.StartMirrorMatch} 가 주소를 못
            // 찾으면 로비 서버 주소로, 그것도 없으면 {@code 127.0.0.1} 로 붙는다. 붙을 곳이
            // 없는데 게임 씬을 열어 로딩창에서 멈추는 것이 그래서다.
            //
            // 보내지 않으면 그 사람은 로비에 남는다. 새 방장이 {@code HOST_TAKEOVER} 를
            // 보내는 순간 전원에게 새 정보가 가므로 그때 함께 들어간다 — 스스로 낫는다.
            log.warn("no match entry: room={} userId={} — 접속 정보가 없어 GAME_STARTED 를 보내지 않는다",
                    room.getRoomId(), playerId);
            return;
        }

        if (!entry.hostAddress().isBlank()) {
            payload.put("hostAddress", entry.hostAddress());
        }

        if (entry.relay() != null) {
            Map<String, Object> mine = entry.relay().payloadFor(playerId);
            if (mine != null) {
                payload.put("relay", mine);
            }
        }

        send(target, WsMessage.Type.GAME_STARTED, payload);
    }

    /**
     * 새 방장이 매치를 이어받는다 (S15P21B202-389).
     *
     * <p><b>왜 클라이언트가 먼저 말을 걸어야 하나.</b> 참가자들이 붙을 주소는 방장만 안다 —
     * 서버가 보는 주소는 공인 IP 라 공유기가 밖에서 들어오는 연결을 막는다. {@code START_GAME}
     * 이 {@code hostAddress} 를 싣는 것과 같은 이유이고, 그래서 이전도 같은 모양이다.
     *
     * <p><b>릴레이 세션을 새로 연다.</b> 우리 릴레이는 누가 서버인지를 <b>토큰으로</b> 가른다
     * ({@code metagame_relay.py} 의 {@code host_token}). 방장이 바뀌면 그 토큰의 주인이
     * 달라지므로 세션을 고쳐 쓸 수 없다. 새로 열고 옛 세션은 닫는다 — 닫지 않으면 릴레이에
     * 세션이 쌓인다({@code -298} §5-① 에서 실제로 그랬다).
     *
     * <p><b>방장만 부를 수 있다.</b> 아니면 남이 이 메시지를 보내 남의 매치를 자기 PC 로
     * 끌어갈 수 있다. 방 상태도 확인한다 — 대기 중인 방에는 이어받을 매치가 없다.
     *
     * <p>전원에게 {@code GAME_STARTED} 를 다시 보낸다. <b>새 메시지를 만들지 않았다</b> —
     * 클라이언트가 매치에 붙는 데 필요한 것이 이미 그 메시지에 다 들어 있고, 받는 쪽은
     * "매치 중에 온 {@code GAME_STARTED}" 를 이전으로 읽는다.
     */
    private void hostTakeover(Long userId, JsonNode data) {
        Room room = roomOf(userId);
        if (room == null || room.getStatus() != Room.Status.PLAYING
                || !userId.equals(room.getHostUserId())) {
            log.info("host takeover rejected: userId={} room={}", userId,
                    room == null ? null : room.getRoomId());
            return;
        }

        String hostAddress = data != null && data.hasNonNull("hostAddress")
                ? data.get("hostAddress").asText("")
                : "";

        LinkedHashMap<Long, String> publicIps = publicIpsOf(room);
        boolean same = !alwaysRelay && sameNetwork(room, publicIps);
        RelaySession relay = same ? null : relayService.open(publicIps, room.getHostUserId());

        MatchEntry previous = matches.put(room.getRoomId(), new MatchEntry(hostAddress, relay));
        if (previous != null && previous.relay() != null) {
            relayService.close(previous.relay().sessionId());
        }

        log.info("host takeover: room={} newHost={} players={} decision={}",
                room.getRoomId(), userId, publicIps.size(),
                same ? "direct(same network)" : (relay != null ? "relay" : "direct(relay failed)"));

        for (Long playerId : room.playerIds()) {
            WebSocketSession target = sessions.get(playerId);
            if (target == null || !target.isOpen()
                    || !room.getRoomId().equals(target.getAttributes().get(ROOM_ID_ATTR))) {
                continue;
            }

            sendGameStarted(target, room, playerId);
        }
    }

    /**
     * 판이 끝났음을 방장이 알린다 — 방을 대기 상태로 되돌린다 (S15P21B202-316).
     *
     * <p><b>서버는 판정하지 않는다.</b> 클리어 여부는 호스트가 판단한다(-145 호스트
     * 권위). 서버는 그 결과만 받아 방 상태를 되돌리는 일만 한다.
     *
     * <p><b>새 서버→클라이언트 메시지를 만들지 않았다.</b> {@code ROOM_STATE} 가 이미
     * {@code status} 를 실어 보내고 클라이언트에도 핸들러가 있으므로, {@code WAITING}
     * 으로 바뀐 {@code ROOM_STATE} 하나가 곧 "로비로 돌아가라" 다. 결과 화면에 필요한
     * 클리어 여부·경과 시간은 호스트가 SyncVar 로 내리는 몫이다(-54).
     *
     * <p><b>릴레이 세션은 닫지 않는다.</b> 같은 방에서 바로 한 판 더 하는 것이 흔하고,
     * 그때 세션을 다시 열면 주소와 토큰이 새로 발급돼 전원이 다시 붙어야 한다. 세션은
     * 방이 실제로 사라질 때({@link #removeFromRoom}) 닫히고, 백엔드가 죽어 그마저
     * 못 하면 릴레이의 30분 TTL 이 걷어낸다(-299).
     *
     * <p>유예 중인 사람의 타이머도 취소하지 않는다. 취소하면 이미 연결이 끊긴 그
     * 사람의 자리가 영구히 남는다 — 만료되도록 두면 자리가 정리된다.
     */
    private void endGame(Long userId) {
        Room room = roomOf(userId);
        if (room == null || !userId.equals(room.getHostUserId()) || !room.end()) {
            // 저장된 세션은 동시 쓰기를 직렬화하도록 감싼 것이다(-313). 원본으로 보내면 그 보호를 잃는다.
            WebSocketSession session = sessions.get(userId);
            if (session != null) {
                send(session, WsMessage.Type.ERROR, Map.of("message", "cannot end game"));
            }
            return;
        }

        broadcastRoomState(room);
        log.info("game end: room={} by userId={}", room.getRoomId(), userId);
    }

    /**
     * 모두가 같은 공유기 뒤에 있는가 — 공인 IP 가 하나뿐이면 그렇다(S15P21B202-264).
     *
     * <p>그럴 때는 사설 주소로 서로 바로 닿으므로 릴레이가 필요 없다. 굳이 태우면
     * 지연만 늘고 릴레이 동시접속 한도(과금 기준)를 쓴다. 교육장처럼 다 같이 있는
     * 자리가 정확히 이 경우다.
     *
     * <p>같은 공인 IP 라도 공유기가 기기끼리의 통신을 막아 두면(AP isolation) 못 붙는다.
     * 그때는 핫스팟으로 우회한다 — {@code -145 §4-3} 에 적어 둔 그대로다.
     */
    /**
     * 다 같은 망에 있는가 — 그렇다면 릴레이 없이 서로 바로 닿는다.
     *
     * <p>방에서 값만 모아 {@link #sameNetwork(java.util.Collection, java.util.Collection)}
     * 에 넘긴다. 판정 자체는 순수 함수라 값으로 검사한다 — 전에는 검사가 하나도 없었고,
     * 그래서 아래 결함이 2주 동안 남아 있었다(S15P21B202-570).
     */
    private boolean sameNetwork(Room room, LinkedHashMap<Long, String> publicIps) {
        return sameNetwork(publicIps.values(), localIpsOf(room));
    }

    /** 방 사람들이 알려 준 랜 주소. 안 알려 줬거나 세션이 없으면 그 자리는 null 이다. */
    private List<String> localIpsOf(Room room) {
        List<String> localIps = new ArrayList<>();
        for (Long playerId : room.playerIds()) {
            WebSocketSession session = sessions.get(playerId);
            localIps.add(session == null
                    ? null
                    : (String) session.getAttributes().get(LOCAL_IP_ATTR));
        }
        return localIps;
    }

    /**
     * 다 같은 망에 있는가 — 공인 IP 목록과 각자 알려 준 사설 주소만 보고 정한다.
     *
     * <p><b>틀렸을 때의 대가가 한쪽으로 기울어 있다.</b> 릴레이는 느릴 뿐 <b>항상 되고</b>,
     * 직접 접속은 빠르지만 안 될 때 <b>아예 못 들어간다.</b> 그래서 애매하면 릴레이로 간다.
     *
     * <p>같은 망으로 보는 경우:
     * <ul>
     *   <li>공인 IP 가 전부 같다 — 한 공유기 뒤에 있다는 뜻</li>
     *   <li>공인 IP 가 <b>같은 {@code /24} 안</b>이고 각자 알려 준 랜 주소도 같은 대역이다 —
     *       공인 IP 를 여러 개 돌려쓰는 교육장 망이 이 경우다(2026-09-02)</li>
     * </ul>
     *
     * <p><b>사설 대역만으로는 같은 망임을 보일 수 없다</b>(S15P21B202-570). {@code 192.168.31.x}
     * 는 어떤 공유기의 기본값이고 {@code 192.168.0.x}·{@code 172.20.10.x}(아이폰 핫스팟)도
     * 마찬가지라, <b>서로 다른 집에서 같은 대역을 받는 일이 흔하다.</b> 그것만 보고 직접 접속을
     * 시키면 닿지 않아 입장이 통째로 막힌다 — 2026-09-16 에 실제로 그랬다
     * ({@code distinctIps=2} 인데 {@code decision=direct(same network)}).
     */
    static boolean sameNetwork(Collection<String> publicIps, Collection<String> localIps) {
        if (publicIps.size() < 2) {
            return true;
        }
        if (publicIps.stream().distinct().count() == 1) {
            return true;
        }

        // 공인 IP 가 다른 대역이면 다른 망이다 — 사설 대역이 같아도 그렇다.
        String block = null;
        for (String ip : publicIps) {
            String prefix = prefix24(ip);
            if (prefix == null) {
                return false;                       // IPv4 가 아니거나 형식이 아니다
            }
            if (block == null) {
                block = prefix;
            } else if (!block.equals(prefix)) {
                return false;
            }
        }

        // 여기까지 왔으면 공인 IP 가 한 /24 안이다. 랜 주소까지 같은 대역이어야 한다.
        String shared = null;
        for (String ip : localIps) {
            String subnet = subnetOf(ip);
            if (subnet == null) {
                return false;                       // 안 알려 줬거나 사설 주소가 아니다
            }
            if (shared == null) {
                shared = subnet;
            } else if (!shared.equals(subnet)) {
                return false;
            }
        }
        return shared != null;
    }

    /** IPv4 의 앞 세 자리(1.2.3). 형식이 아니면 null — 사설·공인을 가리지 않는다. */
    private static String prefix24(String ip) {
        if (ip == null || ip.isBlank()) {
            return null;
        }
        String[] parts = ip.split("\\.");
        if (parts.length != 4) {
            return null;
        }
        for (String part : parts) {
            if (part.isEmpty() || !part.chars().allMatch(Character::isDigit)) {
                return null;
            }
        }
        return parts[0] + "." + parts[1] + "." + parts[2];
    }

    /** 사설 주소의 앞 세 자리(192.168.31). 사설이 아니거나 형식이 아니면 null. */
    private static String subnetOf(String ip) {
        if (ip == null || ip.isBlank() || !isLocalIp(ip)) {
            return null;
        }
        int last = ip.lastIndexOf('.');
        return last > 0 ? ip.substring(0, last) : null;
    }

    /** 클라이언트가 알려 준 자기 랜 주소를 세션에 적어 둔다. */
    private static void rememberLocalIp(WebSocketSession session, JsonNode data) {
        if (data == null || !data.has("localAddress")) {
            return;
        }
        String local = data.get("localAddress").asString();
        if (local != null && !local.isBlank()) {
            session.getAttributes().put(LOCAL_IP_ATTR, local.trim());
        }
    }

    /**
     * 방 사람들의 공인 IP 를 로비 연결에서 읽는다(-264). 릴레이 세션을 열 때 쓴다.
     * 순서가 응답의 session_users 와 짝이 되므로 넣은 차례를 지키는 맵을 쓴다.
     */
    /**
     * userId → 공인 IP. <b>방장을 맨 앞에 둔다.</b>
     *
     * <p>릴레이 세션에서 방장은 게임 서버 자리에 앉는다. Edgegap 이 목록 순서를 어떻게
     * 해석하는지 문서에 분명치 않은데, 첫 번째를 서버로 보는 구현이라면 순서가 뒤바뀐 순간
     * 방장이 세션 주인으로 인정받지 못하고 참가자는 SessionTimeout 을 받는다.
     * 방장을 앞에 두는 것은 공짜이므로 그 위험을 없앤다(2026-09-02).
     */
    private LinkedHashMap<Long, String> publicIpsOf(Room room) {
        LinkedHashMap<Long, String> ips = new LinkedHashMap<>();

        List<Long> ordered = new ArrayList<>(room.playerIds());
        Long host = room.getHostUserId();
        if (host != null && ordered.remove(host)) {
            ordered.add(0, host);
        }

        for (Long playerId : ordered) {
            WebSocketSession session = sessions.get(playerId);
            String ip = clientIp(session);
            if (ip != null && !ip.isBlank()) {
                ips.put(playerId, ip);
            }
        }
        return ips;
    }

    /**
     * 이 사람이 밖에서 보이는 주소(S15P21B202-264).
     *
     * <p>우리 서버는 nginx 뒤에 있다. 소켓이 보는 상대는 nginx 자신(127.0.0.1)이므로
     * 그것만 보면 <b>모두가 같은 주소로 보이고</b>, 그러면 릴레이가 영원히 안 켜진다.
     * 게다가 그 값을 Edgegap 에 보내면 세션이 엉뚱하게 만들어진다.
     *
     * <p>그래서 핸드셰이크의 {@code X-Forwarded-For} 를 먼저 본다. nginx 가 그 헤더를
     * 채워 주도록 설정돼 있어야 한다. 값이 여러 개면 맨 앞이 실제 사용자다.
     */
    private String clientIp(WebSocketSession session) {
        if (session == null) {
            return null;
        }

        String forwarded = session.getHandshakeHeaders().getFirst("X-Forwarded-For");
        if (forwarded != null && !forwarded.isBlank()) {
            return forwarded.split(",")[0].trim();
        }

        return session.getRemoteAddress() == null
                ? null
                : session.getRemoteAddress().getAddress().getHostAddress();
    }

    /** 사설·루프백 주소인가. 릴레이에 넘기면 안 되는 값이라 로그로 알린다. */
    private static boolean isLocalIp(String ip) {
        return ip == null || ip.startsWith("127.") || ip.startsWith("10.")
                || ip.startsWith("192.168.") || ip.startsWith("172.16.")
                || ip.equals("::1") || ip.equals("0:0:0:0:0:0:0:1");
    }

    /**
     * 자기 위치를 같은 방의 다른 사람들에게 전달한다. 보낸 사람에게는 되돌려 보내지 않는다.
     * 서버는 물리를 계산하지 않고 중계만 하며, 마지막 위치는 GameState에 남겨
     * 복귀한 플레이어가 끊긴 자리에서 이어갈 수 있게 한다.
     */
    private void playerMove(Long userId, JsonNode data) {
        Room room = roomOf(userId);
        if (room == null || room.getStatus() != Room.Status.PLAYING || room.getGameState() == null) {
            return;
        }

        float x = (float) data.get("x").asDouble();
        float y = (float) data.get("y").asDouble();
        float z = (float) data.get("z").asDouble();
        float yaw = data.has("yaw") ? (float) data.get("yaw").asDouble() : 0f;
        room.getGameState().setPosition(userId, x, y, z, yaw);

        Map<String, Object> moved = new LinkedHashMap<>();
        moved.put("userId", userId);
        moved.put("x", x);
        moved.put("y", y);
        moved.put("z", z);
        moved.put("yaw", yaw);
        broadcastExcept(room, userId, WsMessage.Type.PLAYER_MOVED, moved);
    }

    private Room roomOf(Long userId) {
        WebSocketSession session = sessions.get(userId);
        if (session == null) {
            return null;
        }
        String roomId = (String) session.getAttributes().get(ROOM_ID_ATTR);
        return roomId == null ? null : roomManager.find(roomId);
    }

    /** 복귀한 플레이어에게 현재 게임 상태 전체를 보낸다 (지침 문서 4-2). */
    private void sendStateSync(WebSocketSession session, Room room) {
        Map<String, Object> data = new LinkedHashMap<>();
        data.put("roomId", room.getRoomId());
        data.put("gameState", room.getGameState());
        send(session, WsMessage.Type.STATE_SYNC, data);
    }

    private void broadcastRoomState(Room room) {
        Map<String, Object> data = new LinkedHashMap<>();
        data.put("roomId", room.getRoomId());
        data.put("hostUserId", room.getHostUserId());
        data.put("status", room.getStatus().name());
        data.put("stageId", stageIdOf(room));
        data.put("players", room.getPlayers());
        data.put("disconnected", room.getDisconnected());
        broadcast(room, WsMessage.Type.ROOM_STATE, data);
    }

    /**
     * 이 방이 지금 말해야 할 스테이지 (2맵 준비).
     *
     * <p><b>판이 돌고 있으면 그 판이 시작될 때 굳은 값이 사실이다.</b> 방 설정
     * ({@code room.getStageId()})은 <i>다음</i> 판을 위한 값이므로, 진행 중에 그것을
     * 바라보면 재입장한 사람에게 다른 맵을 알려 줄 수 있다 — 한 판에 두 개의 맵이
     * 열린다. 두 값을 같은 이름으로 쓰지 않도록 읽는 자리를 여기 하나로 모은다.
     */
    private static String stageIdOf(Room room) {
        GameState state = room.getGameState();
        if (state != null && state.getStageId() != null) {
            return state.getStageId();
        }
        return room.getStageId();
    }

    private void broadcast(Room room, WsMessage.Type type, Map<String, Object> data) {
        broadcastExcept(room, null, type, data);
    }

    /**
     * 세션은 userId로만 찾으므로, 재접속으로 세션이 바뀐 뒤 옛 방의 브로드캐스트가
     * 새 세션으로 새어 나갈 수 있다. 세션이 실제로 그 방에 속해 있는지 확인하고 보낸다.
     */
    private void broadcastExcept(Room room, Long excludedUserId, WsMessage.Type type,
                                 Map<String, Object> data) {
        for (Long playerId : room.playerIds()) {
            if (playerId.equals(excludedUserId)) {
                continue;
            }
            WebSocketSession session = sessions.get(playerId);
            // 왜 빠졌는지 반드시 남긴다 (S15P21B202-313 ④). 예전에는 조용히 continue 해서
            // 메시지 유실이 서버 로그에 아무 흔적도 남기지 않았다 — 진단이 불가능했다.
            if (session == null) {
                // 유예 중인 사람은 세션이 없는 것이 **정상**이다(끊겼지만 자리는 유지). 그때는 조용히
                // 넘어가고, 끊김 표시도 없는데 세션이 없으면 그것만 경고한다 — 그것이 이상한 경우다.
                if (room.isDisconnected(playerId)) {
                    log.debug("broadcast skip: type={} user={} room={} 이유=유예중(세션없음)",
                            type, playerId, room.getRoomId());
                } else {
                    log.warn("broadcast skip: type={} user={} room={} 이유=세션없음(끊김 표시도 없다)",
                            type, playerId, room.getRoomId());
                }
                continue;
            }
            if (!session.isOpen()) {
                log.warn("broadcast skip: type={} user={} room={} 이유=세션닫힘", type, playerId, room.getRoomId());
                continue;
            }
            Object attr = session.getAttributes().get(ROOM_ID_ATTR);
            if (!room.getRoomId().equals(attr)) {
                log.warn("broadcast skip: type={} user={} room={} 이유=방불일치(attr={})",
                        type, playerId, room.getRoomId(), attr);
                continue;
            }
            send(session, type, data);
        }
    }

    /**
     * 한 세션에 한 건 보낸다.
     *
     * <p><b>예외를 여기서 끝낸다</b>(S15P21B202-313 ③). 예전에는 {@code IOException} 만 잡았는데,
     * Tomcat 은 동시 쓰기·닫힌 엔드포인트에 <b>{@code IllegalStateException}</b>(unchecked)을 던진다.
     * 그것이 {@code send} 를 뚫고 나가면 팬아웃 루프가 그 자리에서 끝나 <b>뒤 순번의 사람들이
     * 그 메시지를 영구히 잃었다.</b> 한 명에게 못 보낸 것이 나머지 전원의 손실로 번지면 안 된다.
     *
     * <p>대상은 {@link #writeTarget}(감싼 세션)으로 바꾼다 — 호출부가 스프링이 준 원본 세션을
     * 넘기더라도 실제 쓰기는 직렬화된 쪽으로 나간다.
     */
    private void send(WebSocketSession session, WsMessage.Type type, Map<String, Object> data) {
        WebSocketSession target = writeTarget(session);
        try {
            String payload = objectMapper.writeValueAsString(
                    Map.of("type", type.name(), "data", data));
            target.sendMessage(new TextMessage(payload));
        } catch (IOException e) {
            log.warn("send failed: type={} sid={} : {}", type, target.getId(), e.getMessage());
        } catch (RuntimeException e) {
            // 여기서 멈춘다. 세션 하나의 문제가 방 전체의 손실이 되지 않게.
            log.warn("send threw: type={} sid={} {}: {}",
                    type, target.getId(), e.getClass().getSimpleName(), e.getMessage());
        }
    }

    /**
     * 입장이 안 된 이유를 함께 보낸다 (S15P21B202-413).
     *
     * <p><b>{@code message} 는 그대로 둔다.</b> 이미 쓰이는 값이고 로그·시험이 그것을
     * 보고 있다. 옆에 {@code reason} 을 더해 클라이언트가 사람이 읽을 문구를 고르게 한다 —
     * 화면에 보일 말은 <b>화면을 가진 쪽</b>이 정하는 것이 맞고, 그래야 문구를 고칠 때
     * 서버를 다시 배포하지 않는다.
     *
     * <p>{@code reason} 을 모르는 옛 클라이언트는 예전처럼 {@code message} 를 보여 준다.
     */
    private void sendJoinError(WebSocketSession session, String reason, String message) {
        send(session, WsMessage.Type.ERROR, Map.of("message", message, "reason", reason));
    }

    /** 이 세션에 쓸 때 실제로 써야 하는 대상 — 등록돼 있으면 감싼 세션, 아니면 그대로. */
    private WebSocketSession writeTarget(WebSocketSession session) {
        Long userId = userId(session);
        WebSocketSession stored = userId == null ? null : sessions.get(userId);
        if (stored != null
                && WebSocketSessionDecorator.unwrap(stored) == WebSocketSessionDecorator.unwrap(session)) {
            return stored;
        }
        return session;
    }

    private Long userId(WebSocketSession session) {
        return (Long) session.getAttributes().get(JwtHandshakeInterceptor.USER_ID_ATTR);
    }

    private String nickname(WebSocketSession session) {
        Object cached = session.getAttributes().get(NICKNAME_ATTR);
        return cached == null ? UNKNOWN_NICKNAME : (String) cached;
    }
}
