# 실시간 메시지 프로토콜 (Unity ↔ 백엔드 계약서)

## 연결

1. REST 로그인: `POST /auth/guest` — body `{ "nickname": "철수", "deviceId": "기기고유값" }`
   → 응답 `{ "userId": 1, "nickname": "철수", "accessToken": "eyJ..." }`
2. WebSocket 연결: `ws://localhost:8080/ws/game?token={accessToken}`
   - 토큰이 없거나 유효하지 않으면 핸드셰이크 거부 (401)

## 메시지 형식

모든 메시지는 텍스트(JSON), `type` + `data` 구조:

```json
{ "type": "JOIN_ROOM", "data": { "roomId": "K7M2QX" } }
```

## 방 코드 (roomId)

방 참가는 **코드 초대 방식**이다. 호스트가 받은 코드를 친구에게 전달하고, 친구가 그 코드로 입장한다.
공개 방 목록은 제공하지 않는다.

- 형식: **대문자·숫자 6자리** (예: `K7M2QX`)
- 사용 문자: `23456789ABCDEFGHJKMNPQRSTUVWXYZ` — 눈으로 헷갈리는 `0/O`, `1/I/L`은 쓰지 않는다
- **입력 시 대소문자를 구분하지 않는다.** 앞뒤 공백도 무시한다 (서버가 정규화)
- 서버가 응답에 담는 값은 항상 대문자다. 클라이언트는 받은 값을 그대로 보관해 쓰면 된다

## Client → Server

| type | data | 설명 |
|---|---|---|
| `CREATE_ROOM` | `{}` | 방 생성. 생성자가 방장 |
| `JOIN_ROOM` | `{ "roomId": "..." }` | 방 입장. WAITING이면 일반 입장(정원 미달일 때만), PLAYING이면 **복귀**(그 판에 참가했던 유저만) |
| `LEAVE_ROOM` | `{}` | 방 퇴장. 방장이 나가면 자동 위임 |
| `READY` | `{ "ready": true }` | 준비 상태 변경 |
| `START_GAME` | `{}` | 게임 시작 (방장만, 전원 준비 시) |
| `PLAYER_MOVE` | `{ "x": 1.0, "y": 0.0, "z": 3.5, "yaw": 90.0 }` | 자기 위치·방향 전송 (게임 중에만). `yaw`는 생략 가능 |

## Server → Client

| type | data | 설명 |
|---|---|---|
| `ROOM_CREATED` | `{ "roomId": "..." }` | 방 생성 완료 (생성자에게만) |
| `ROOM_STATE` | 아래 예시 참고 | 방 상태 변화 시 방 전원에게 브로드캐스트 |
| `GAME_STARTED` | `{ "roomId": "..." }` | 게임 시작 (방 전원에게) |
| `STATE_SYNC` | `{ "roomId", "gameState": { ... } }` | 복귀한 플레이어에게만 현재 게임 상태 전체 전송 |
| `PLAYER_MOVED` | `{ "userId": 2, "x": 1.0, "y": 0.0, "z": 3.5, "yaw": 90.0 }` | 다른 플레이어의 위치. **보낸 사람에게는 오지 않는다** |
| `ERROR` | `{ "message": "..." }` | 요청 실패 사유 |

### ROOM_STATE 예시

```json
{
  "type": "ROOM_STATE",
  "data": {
    "roomId": "K7M2QX",
    "hostUserId": 1,
    "status": "WAITING",
    "players": {
      "1": { "nickname": "철수", "ready": true },
      "2": { "nickname": "영희", "ready": false }
    },
    "disconnected": [3]
  }
}
```

- `players`의 키는 userId(문자열), 값은 **닉네임과 준비 여부**다. 로비 화면에 그대로 쓰면 된다
- `disconnected`는 연결이 끊겨 유예 시간을 기다리는 중인 userId 목록이다. 이 사람들도 `players`에는 남아 있다
- `status`는 `WAITING`(대기 중) 또는 `PLAYING`(게임 중)

## 인게임 위치 동기화

게임이 시작되면(`GAME_STARTED` 수신 후) 각 클라이언트는 자기 캐릭터의 위치를 주기적으로 보낸다.

```
내 게임 ──PLAYER_MOVE──▶ 서버 ──PLAYER_MOVED──▶ 같은 방의 나머지
```

- **권장 전송 주기: 초당 10~20회.** 매 프레임 보낼 필요는 없다
- 서버는 물리를 계산하지 않고 **그대로 중계**한다. 받은 쪽은 값 사이를 부드럽게 이어(보간) 그린다
- 마지막 위치는 서버의 `GameState`에 남는다. 그래서 복귀한 플레이어가 `STATE_SYNC`로 **끊겼던 자리**를 받는다
- 대기 중(`WAITING`)인 방에서 보낸 `PLAYER_MOVE`는 무시된다

> **범위 주의**: 플레이어 위치만 공유한다. 상자 같은 물리 오브젝트는 각 클라이언트가 따로 계산하므로
> 화면마다 다르게 보인다. 오브젝트 공유가 필요해지면 별도 설계가 필요하다.

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
