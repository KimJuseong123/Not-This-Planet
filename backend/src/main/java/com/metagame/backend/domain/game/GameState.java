package com.metagame.backend.domain.game;

import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import lombok.Getter;

/**
 * 게임 한 판의 모든 상태를 담는 객체 (지침 문서 4장).
 *
 * 규칙: 게임 진행 중 변하는 상태는 전부 이 클래스 안에 넣는다.
 * 다른 클래스나 static 변수에 흩어놓지 않는다.
 *
 * 이 객체 하나가 JSON 직렬화 가능하면 다음이 전부 해결된다:
 * - 재접속 복구 (현재 상태를 해당 플레이어에게 전송)
 * - 새 플레이어 입장 동기화
 * - 스테이지 재시작 (해당 스테이지의 초기 상태를 새로 생성)
 * - 진행도 DB 저장
 *
 * TODO: 게임 기획 확정 후 필드 채우기 (S15P21B202-54). 아래는 예시 뼈대.
 */
@Getter
public class GameState {

    private final String stageId;
    private final Map<Long, PlayerState> players = new ConcurrentHashMap<>();
    private final Set<String> solvedPuzzles = ConcurrentHashMap.newKeySet();
    private final Set<String> collectedItems = ConcurrentHashMap.newKeySet();

    /**
     * 어느 스테이지의 판인지를 <b>들고 태어난다</b> (2맵 준비).
     *
     * <p>예전에는 필드만 있고 아무도 넣지 않아 언제나 {@code null} 이었다. 이 객체는
     * 재접속 복구({@code STATE_SYNC})와 체크포인트 복원에 그대로 실려 나가므로,
     * 스테이지가 비어 있는 틈에 만들어진 스냅샷은 <b>"어느 맵이었는지 모르는 판"</b>
     * 이 된다. 그래서 나중에 채우지 않고 생성자에서만 정한다.
     */
    public GameState(String stageId) {
        this.stageId = stageId;
    }

    public void addPlayer(Long userId) {
        players.computeIfAbsent(userId, id -> new PlayerState());
    }

    /**
     * 기절 상태를 설정한다. 플레이어가 없으면 새로 만들어 설정한다
     * (제외된 뒤 재입장하는 경우 — 기획: 재입장 시 기절 상태로 복귀).
     */
    public void setDowned(Long userId, boolean downed) {
        players.computeIfAbsent(userId, id -> new PlayerState()).setDowned(downed);
    }

    /**
     * 클라이언트가 보내온 위치를 보관한다.
     * 유예를 넘겨 참가자 목록에서 빠져도 여기 남으므로, 재입장 시 끊긴 그 자리로 돌려보낼 수 있다
     * (기획: 재입장 스폰 위치는 끊긴 위치).
     */
    public void setPosition(Long userId, float x, float y, float z, float yaw) {
        players.computeIfAbsent(userId, id -> new PlayerState()).setPosition(x, y, z, yaw);
    }

    @Getter
    public static class PlayerState {
        private float x;
        private float y;
        private float z;
        private float yaw;   // 바라보는 방향(수평 회전)
        private int hp;

        // 기절 상태. 사망 개념은 없고 팀원이 소생시킨다 (기획: 전원 기절 시 스테이지 재시작)
        private boolean downed;

        void setDowned(boolean downed) {
            this.downed = downed;
        }

        void setPosition(float x, float y, float z, float yaw) {
            this.x = x;
            this.y = y;
            this.z = z;
            this.yaw = yaw;
        }
    }
}
