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
 * - 체크포인트 저장/부활 (복사 후 보관 / 복원)
 * - 재접속 복구 (현재 상태를 해당 플레이어에게 전송)
 * - 진행도 DB 저장 (snapshot_json)
 * - 새 플레이어 입장 동기화
 *
 * TODO: 게임 기획 확정 후 필드 채우기. 아래는 예시 뼈대.
 */
@Getter
public class GameState {

    private String stageId;
    private final Map<Long, PlayerState> players = new ConcurrentHashMap<>();
    private final Set<String> solvedPuzzles = ConcurrentHashMap.newKeySet();
    private final Set<String> collectedItems = ConcurrentHashMap.newKeySet();

    @Getter
    public static class PlayerState {
        private float x;
        private float y;
        private float z;
        private int hp;
        private boolean alive = true;
    }
}
