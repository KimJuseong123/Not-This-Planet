package com.metagame.backend.domain.auth;

import com.metagame.backend.domain.auth.dto.MatchVerifyRequest;
import com.metagame.backend.domain.auth.dto.MatchVerifyResponse;
import com.metagame.backend.domain.room.Room;
import com.metagame.backend.domain.room.RoomManager;
import com.metagame.backend.domain.room.RoomPlayer;
import com.metagame.backend.global.auth.JwtProvider;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;

/**
 * 게임 서버(Mirror)가 접속해 온 플레이어를 확인해 준다.
 *
 * 게임 서버는 로비를 모른다. 누가 어느 방 사람인지 아는 곳은 여기뿐이므로,
 * 접속 시점에 물어보게 한다. 통과하면 그 연결의 신원이 확정된다.
 *
 * 방 참가자인지까지 보는 이유는, 토큰만 맞으면 남의 방에 들어갈 수 있기 때문이다.
 */
@Service
@RequiredArgsConstructor
public class MatchAuthService {

    private final JwtProvider jwtProvider;
    private final RoomManager roomManager;

    /**
     * 확인에 실패하면 null 을 돌려준다. 실패 사유는 구분하지 않는다 —
     * "토큰이 틀렸는지, 그 방 사람이 아닌지" 를 알려 주면 남의 방을 더듬어 볼 수 있다.
     */
    public MatchVerifyResponse verify(MatchVerifyRequest request) {
        Long userId = jwtProvider.parseUserId(request.token());
        if (userId == null) {
            return null;
        }

        Room room = roomManager.find(request.roomId());
        if (room == null) {
            return null;
        }

        RoomPlayer player = room.getPlayers().get(userId);
        if (player == null) {
            return null;
        }

        return new MatchVerifyResponse(userId, player.getNickname());
    }
}
