# 실시간 메시지 프로토콜 (Unity ↔ 백엔드 계약서)

## 연결

1. REST 로그인: `POST /auth/guest` — body `{ "nickname": "철수", "deviceId": "기기고유값" }`
   → 응답 `{ "userId": 1, "nickname": "철수", "accessToken": "eyJ..." }`
2. WebSocket 연결: `ws://localhost:8080/ws/game?token={accessToken}`
   - 토큰이 없거나 유효하지 않으면 핸드셰이크 거부 (401)

## 메시지 형식

모든 메시지는 텍스트(JSON), `type` + `data` 구조:

```json
{ "type": "JOIN_ROOM", "data": { "roomId": "abc123" } }
```

## Client → Server

| type | data | 설명 |
|---|---|---|
| `CREATE_ROOM` | `{}` | 방 생성. 생성자가 방장 |
| `JOIN_ROOM` | `{ "roomId": "..." }` | 방 입장. WAITING이면 일반 입장(정원 미달일 때만), PLAYING이면 **복귀**(그 판에 참가했던 유저만) |
| `LEAVE_ROOM` | `{}` | 방 퇴장. 방장이 나가면 자동 위임 |
| `READY` | `{ "ready": true }` | 준비 상태 변경 |
| `START_GAME` | `{}` | 게임 시작 (방장만, 전원 준비 시) |

## Server → Client

| type | data | 설명 |
|---|---|---|
| `ROOM_CREATED` | `{ "roomId": "..." }` | 방 생성 완료 (생성자에게만) |
| `ROOM_STATE` | `{ "roomId", "hostUserId", "status", "players": { "1": true, ... }, "disconnected": [2] }` | 방 상태 변화 시 방 전원에게 브로드캐스트. players는 userId → 준비 여부, disconnected는 유예 대기 중인 userId 목록 |
| `GAME_STARTED` | `{ "roomId": "..." }` | 게임 시작 (방 전원에게) |
| `STATE_SYNC` | `{ "roomId", "gameState": { ... } }` | 복귀한 플레이어에게만 현재 게임 상태 전체 전송 |
| `ERROR` | `{ "message": "..." }` | 요청 실패 사유 |

## 연결 끊김과 복귀

게임 중(`PLAYING`)에 연결이 끊기면 **게임은 멈추지 않는다.**

1. 끊긴 즉시 방 전원에게 `ROOM_STATE`가 오고, 해당 userId가 `disconnected`에 담긴다.
   자리는 유예 시간(기본 30초, `game.reconnect-grace-ms`) 동안 유지된다.
2. 유예 안에 다시 접속해 `JOIN_ROOM`을 보내면 복귀한다 → `STATE_SYNC` 수신 후 `ROOM_STATE` 브로드캐스트.
3. 유예를 넘기면 참가자 목록에서 **제외**되고 `ROOM_STATE`가 브로드캐스트된다.
   **남은 인원으로 게임은 계속 진행된다.**
4. 제외된 뒤에도 `JOIN_ROOM`으로 재입장할 수 있다. 단 **그 판에 참가했던 유저만** 허용된다
   (무관한 유저의 난입 방지). 정원이 이미 찼으면 `ERROR: room is full`.
5. **복귀·재입장한 플레이어는 기절(DOWNED) 상태로 돌아온다.** 팀원이 소생시켜야 한다.

> 클라이언트는 `ROOM_CREATED`/`ROOM_STATE`로 받은 `roomId`를 보관해 두었다가,
> 재접속 시 같은 `roomId`로 `JOIN_ROOM`을 보내면 된다. 별도의 복귀 전용 메시지는 없다.

## 미정 (게임 기획 확정 후 추가)

- `PLAYER_MOVE` — 동기화 방식(이벤트 vs 틱) 결정 필요
- `PLAYER_DOWNED`, `REVIVE_*`, `PARTY_WIPE`, `STAGE_CLEARED`, `STAGE_RESTART` — 소생·체크포인트 규칙 확정 필요
- 재입장 스폰 위치 (끊긴 위치 / 팀원 근처 / 스테이지 시작점)

> 이 문서가 곧 서버-클라이언트 계약이다. 메시지 추가/변경 시 반드시 이 문서를 갱신하고 Unity 팀에 공유할 것.
