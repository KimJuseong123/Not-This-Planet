package com.metagame.backend.domain.auth;

import com.metagame.backend.domain.auth.dto.MatchVerifyRequest;
import com.metagame.backend.domain.auth.dto.MatchVerifyResponse;
import com.metagame.backend.domain.room.Room;
import com.metagame.backend.domain.room.RoomManager;
import com.metagame.backend.domain.room.RoomPlayer;
import com.metagame.backend.global.auth.JwtProvider;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Service;

/**
 * 게임 서버(Mirror)가 접속해 온 플레이어를 확인해 준다.
 *
 * 게임 서버는 로비를 모른다. 누가 어느 방 사람인지 아는 곳은 여기뿐이므로,
 * 접속 시점에 물어보게 한다. 통과하면 그 연결의 신원이 확정된다.
 *
 * 방 참가자인지까지 보는 이유는, 토큰만 맞으면 남의 방에 들어갈 수 있기 때문이다.
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class MatchAuthService {

    private final JwtProvider jwtProvider;
    private final RoomManager roomManager;

    /**
     * 확인에 실패하면 null 을 돌려준다. 실패 사유는 <b>돌려주지</b> 않는다 —
     * "토큰이 틀렸는지, 그 방 사람이 아닌지" 를 알려 주면 남의 방을 더듬어 볼 수 있다.
     *
     * <p><b>다만 로그에는 남긴다</b>(S15P21B202-573). 밖으로 안 알리는 것과 우리도
     * 모르는 것은 다르다. 2026-09-16 에 개발 백엔드가 401 을 내는데 <b>세 갈래 중
     * 어디서 떨어졌는지 아무 기록이 없어</b> 원인을 찾는 데 오래 걸렸다 — 실제 원인은
     * "방이 배포 서버에 있었다"(두 번째 갈래)였다.
     */
    public MatchVerifyResponse verify(MatchVerifyRequest request) {
        Long userId = jwtProvider.parseUserId(request.token());
        if (userId == null) {
            log.info("match verify 거절: 토큰을 읽을 수 없다 · room={}", request.roomId());
            return null;
        }

        Room room = roomManager.find(request.roomId());
        if (room == null) {
            // 이 환경에 그 방이 없다. 클라이언트가 **다른 환경에 방을 만들었을 때**가
            // 이 자리다(개발/배포가 갈린 경우) — 그래서 방 개수를 함께 남긴다.
            log.info("match verify 거절: 이 서버에 방이 없다 · room={} userId={} 방수={}",
                    request.roomId(), userId, roomManager.findAll().size());
            return null;
        }

        RoomPlayer player = room.getPlayers().get(userId);
        if (player == null) {
            log.info("match verify 거절: 그 방의 사람이 아니다 · room={} userId={}",
                    request.roomId(), userId);
            return null;
        }

        return new MatchVerifyResponse(userId, player.getNickname());
    }
}
