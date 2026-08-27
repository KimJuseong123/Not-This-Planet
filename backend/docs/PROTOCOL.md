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
| `JOIN_ROOM` | `{ "roomId": "..." }` | 방 입장 (WAITING 상태 + 정원 미달일 때만) |
| `LEAVE_ROOM` | `{}` | 방 퇴장. 방장이 나가면 자동 위임 |
| `READY` | `{ "ready": true }` | 준비 상태 변경 |
| `START_GAME` | `{}` | 게임 시작 (방장만, 전원 준비 시) |

## Server → Client

| type | data | 설명 |
|---|---|---|
| `ROOM_CREATED` | `{ "roomId": "..." }` | 방 생성 완료 (생성자에게만) |
| `ROOM_STATE` | `{ "roomId", "hostUserId", "status", "players": { "1": true, ... } }` | 방 상태 변화 시 방 전원에게 브로드캐스트. players는 userId → 준비 여부 |
| `GAME_STARTED` | `{ "roomId": "..." }` | 게임 시작 (방 전원에게) |
| `ERROR` | `{ "message": "..." }` | 요청 실패 사유 |

## 미정 (게임 기획 확정 후 추가)

- `PLAYER_MOVE`, `GAME_STATE_SYNC` — 동기화 방식(이벤트 vs 틱) 결정 필요
- `CHECKPOINT_REACHED`, `RESPAWN` — 체크포인트 규칙(전원/1인 도달) 결정 필요
- 재접속 복구 (`RECONNECT` → 현재 GameState 전송)

> 이 문서가 곧 서버-클라이언트 계약이다. 메시지 추가/변경 시 반드시 이 문서를 갱신하고 Unity 팀에 공유할 것.
