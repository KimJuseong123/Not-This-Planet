# S15P21B202-61 상호작용 프레임워크 구현

- Epic: S15P21B202-14 [Puzzle] 퍼즐·상호작용 시스템
- 브랜치: `S15P21B202-61-puzzle` (`S15P21B202-57-puzzle` 에서 분기)
- 작성일: 2026-08-27

## 1. 목적 / 배경

고정쇠 해제, 견인기 조작, 용접, 동료 구조는 모두 "대상을 바라보고 키를 눌러 상호작용한다"는
같은 뼈대를 쓴다. 콘텐츠마다 따로 만들면 특수 케이스 코드가 누적되므로, 구현체가 생기기 전에
공통 인터페이스와 탐지·홀드·점유 규칙을 먼저 확정한다.

## 2. 확정 사항

### 2.1 구성

| 타입 | 역할 |
| --- | --- |
| `IInteractable` | 상호작용 대상의 계약 |
| `InteractableBase` | 점유·포커스 처리를 제공하는 기본 구현. 구현체는 이걸 상속한다 |
| `Interactor` | 대상을 찾고, 고르고, 홀드해서 완료시키는 주체 |
| `InteractionAvailability` | 지금 가능한지와, 안 되면 그 이유 |
| `InteractionFailure` | 실패 사유 열거형. UI 문구 선택에 사용 |
| `InteractorState` | Idle / Focused / Holding |

네임스페이스는 `Metagame.Gameplay.Interaction` 이다.

### 2.2 입력을 프레임워크 밖에 둔다

`Interactor` 는 입력을 알지 못한다. 소유자(캐릭터)가 아래를 호출한다.

```
Tick(deltaTime)   매 프레임. 홀드 중이면 진행도, 아니면 포커스 갱신
TryBegin()        상호작용 키를 누른 순간
Cancel()          키를 뗀 순간 또는 강제 중단
```

이유는 세 가지다.

- 입력 방식(Input System / 레거시 / 네트워크 입력)이 바뀌어도 이 클래스는 그대로다.
- `Time.deltaTime` 을 직접 읽지 않으므로 PlayMode 없이 EditMode 에서 시간을 넣어 검증할 수 있다.
- 호스트 권위 물리에서 틱 순서를 소유자가 통제할 수 있다.

프로젝트에 Input System 패키지가 아직 없다는 점도 이 결정과 맞물린다.
지금 입력 방식을 고를 필요가 없다.

### 2.3 대상 선정 규칙

`GameLayers.InteractableMask` 로 사거리 안을 훑고, 시야각 안의 대상만 남긴 뒤 아래 순서로 고른다.

1. 정면에 가까운 것 (각도)
2. 각도가 같으면 가까운 것 (거리)
3. 그것도 같으면 월드 좌표 x, y, z 순

**마지막 기준으로 인스턴스 ID 를 쓰지 않는다.** ID 는 머신마다 다르게 배정되므로
같은 장면인데도 4인이 각자 다른 대상을 집어들 수 있다. 월드 좌표는 어느 클라이언트에서나 같다.

`Interactable` 레이어는 충돌 매트릭스에서 전부 꺼져 있으므로(S15P21B202-57)
이 콜라이더들은 물리에 영향을 주지 않고 쿼리에만 잡힌다.

### 2.4 점유된 대상도 포커스에는 포함한다

다른 플레이어가 쓰고 있는 대상도 포커스 후보에 남긴다. 후보에서 빼버리면
"지금 누가 쓰는 중"이라는 피드백을 줄 수 없기 때문이다.
거절은 시작 시점에 `TryBegin` 이 `InteractionFailure.Occupied` 로 처리한다.

### 2.5 홀드 규칙

- `HoldSeconds == 0` 이면 누르는 즉시 완료되는 탭 상호작용이다.
- 홀드 중에는 포커스를 갱신하지 않는다. 한 번 잡은 대상을 끝까지 붙든다.
- 아래 중 하나라도 발생하면 취소되고 진행도가 0 으로 돌아간다.
  - 대상이 사거리 또는 시야각을 벗어남
  - 대상의 조건이 깨짐 (`GetAvailability` 가 거절로 바뀜)
  - 대상이 파괴됨
  - `Cancel()` 호출
- 완료든 취소든 점유는 반드시 해제된다.

진행도 누적에는 1e-4 의 허용 오차를 둔다. 1초짜리 홀드를 0.02초씩 50번 더하면
부동소수점 오차로 0.99999997 이 되어 플레이어가 이유 없이 한 프레임을 더 눌러야 하기 때문이다.

### 2.6 다중 인원 상호작용은 아직 넣지 않는다

지금은 한 대상에 한 명만 붙는 단일 점유다. "2인 이상이 동시에 잡아야 하는 대상"은
필요해지는 시점에 `IInteractable` 에 필요 인원 개념을 추가해 확장한다.
지금 넣으면 쓰지 않는 추상화가 된다.

## 3. 산출물

코드
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/IInteractable.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/InteractableBase.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/Interactor.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/InteractionAvailability.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/InteractionFailure.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/InteractorState.cs`

테스트
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/InteractorTests.cs`

씬·프리팹 변경 없음.

## 4. 검증

EditMode 테스트 28건 전부 통과했다 (기존 GameLayers 10건 + 신규 Interactor 18건).

| 영역 | 테스트 |
| --- | --- |
| 탐지 | 사거리 밖 제외, 시야각 밖 제외, 대상 없음 |
| 선정 | 정면 우선(가까운 비스듬한 대상보다 먼 정면을 선택), 동일 대상 재통지 안 함 |
| 포커스 | 진입·이탈 콜백, 이전/현재 함께 통지 |
| 시작 | 탭 즉시 완료, 대상 없음 거절, 조건 미충족 거절, 홀드 중 재시작 거절 |
| 홀드 | 지정 시간 후 완료, 진행도 1 클램프, 사거리 이탈 취소, 조건 상실 취소 |
| 점유 | 두 번째 Interactor 차단, 완료 후 인계, 취소 시 해제 |

## 5. 미해결 / 후속

- 입력 바인딩 — S15P21B202-13 (Character)
- 첫 구현체(고정쇠) — 파츠 상태머신 티켓
- 상호작용 UI(포커스 표시, 홀드 게이지, 실패 문구) — UI 티켓
- 네트워크 동기화 — S15P21B202-15 (Network)
- 다중 인원 상호작용 — 필요해지는 시점

## 6. 참고

- 선행: S15P21B202-57 (`GameLayers.InteractableMask` 의존). 본 브랜치는 57 에서 분기했다
- 구현체 콜라이더는 `Interactable` 레이어에 두어야 한다. 다른 레이어면 탐지되지 않는다
- `Unity 6.5` 에서 `Object.GetInstanceID()` 가 폐기되었다.
  대체 API(`GetEntityId`)의 int 변환도 폐기 예정이라 아예 쓰지 않는 쪽을 택했다 (2.3 참고)
