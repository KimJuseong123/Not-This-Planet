package com.metagame.backend.websocket;

import java.util.Map;

/**
 * 한 매치에 쓸 릴레이 세션 (S15P21B202-264).
 *
 * <p>방장의 PC 가 게임 서버라 참가자가 직접 붙으려면 방장의 공유기를 뚫어야 한다(NAT).
 * 릴레이는 방장도 참가자도 각자 밖으로 나가서 붙게 만들어 그 문제를 없앤다.
 *
 * <p>사람마다 {@code userToken} 이 다르므로 이 값은 방송하지 않고 각자에게 따로 보낸다.
 *
 * @param sessionId   릴레이 쪽 식별자. <b>방이 사라질 때 닫으려면 이것이 필요하다.</b>
 *                    예전에는 이 값이 없어 {@code close()} 를 부를 수 없었고, 세션이 계속
 *                    쌓였다(S15P21B202-298 §5-①). 클라이언트에는 보내지 않는다.
 * @param address     릴레이 주소
 * @param serverPort  방장이 붙을 포트 (relay.ports.server.port)
 * @param clientPort  참가자가 붙을 포트 (relay.ports.client.port)
 * @param sessionToken 방 전원이 같은 값 (authorization_token)
 * @param userTokens  userId 별 개인 값 (session_users[i].authorization_token)
 */
public record RelaySession(
        String sessionId,
        String address,
        int serverPort,
        int clientPort,
        long sessionToken,
        Map<Long, Long> userTokens) {

    /** 이 사람 몫의 값만 담아 GAME_STARTED 에 실을 모양으로 만든다. */
    public Map<String, Object> payloadFor(Long userId) {
        Long userToken = userTokens.get(userId);
        if (userToken == null) {
            return null;
        }
        return Map.of(
                "address", address,
                "serverPort", serverPort,
                "clientPort", clientPort,
                "sessionToken", sessionToken,
                "userToken", userToken);
    }
}
