# S15P21B202-63 파츠 상태머신 및 고정쇠 구현

- Epic: S15P21B202-14 [Puzzle] 퍼즐·상호작용 시스템
- 브랜치: `S15P21B202-63-puzzle` (`S15P21B202-62-puzzle-tuning` 에서 분기)
- 작성일: 2026-08-27

## 1. 목적 / 배경

우주선 파츠는 고정된 상태, 풀린 상태, 견인 중, 놓인 상태, 용접된 상태를 오간다.
상태마다 물리 표현이 달라진다. 고정된 파츠는 운석에 맞아도 움직이면 안 되고,
견인 중에는 동료를 밀칠 수는 있되 깔아뭉갤 수 없어야 하며, 놓인 뒤에는 다시 무거워져야 한다.

이 전환을 각 시스템에 흩어 두면 "견인은 끝났는데 질량이 안 돌아온" 파츠가 생긴다.
견인·적층·용접이 모두 이 상태를 읽고 쓰기 때문에 그 셋을 만들기 전에 한곳으로 모은다.

함께 고정쇠를 구현한다. S15P21B202-61 상호작용 프레임워크의 첫 실제 구현체다.

## 2. 확정 사항

### 2.1 상태와 물리 표현

| 상태 | 레이어 | 질량 | Rigidbody | 의미 |
| --- | --- | --- | --- | --- |
| `Locked` | Part | 500kg | **kinematic** | 고정쇠로 박혀 있다. 무엇이 부딪혀도 밀리지 않는다 |
| `Free` | Part | 500kg | dynamic | 고정쇠가 모두 풀렸다. 아직 들리지 않았다 |
| `Carried` | **PartCarried** | **35kg** | dynamic | 견인 중. 동료를 밀칠 수는 있으나 다치게 하지 않는다 |
| `Placed` | Part | 500kg | dynamic | 내려놓였다. 아직 접합되지 않아 흔들릴 수 있다 |
| `Welded` | Part | 500kg | dynamic | 용접 완료. 되돌아가지 않는다 |

`Locked` 을 kinematic 으로 둔 것이 핵심이다. 질량만 크게 해도 운석에 맞으면 조금씩 밀리는데,
기획상 고정쇠를 풀기 전에는 파츠가 절대 움직이지 않아야 한다.

레이어는 파츠 오브젝트에만 준다. 자식의 고정쇠는 `Interactable` 레이어에 있어야
`Interactor` 가 찾을 수 있으므로 함께 바꾸지 않는다.

### 2.2 허용 전환

```
Locked  -> Free                 (필수 고정쇠가 모두 풀린 경우에만)
Free    -> Carried
Carried -> Placed
Placed  -> Carried | Welded
Welded  -> (없음)
```

그 밖의 전환은 `TryTransitionTo` 가 false 를 돌려주고 상태를 바꾸지 않는다.
같은 상태로의 전환도 거부한다.

`Placed -> Carried` 를 허용한 것은 용접 전이라면 위치를 다시 잡을 수 있어야 하기 때문이다.
`Welded` 는 마지막 상태다.

### 2.3 고정쇠

`PartClamp` 는 `InteractableBase` 를 상속한다. 기본 홀드 시간은 1.5초다.
탭이 아니라 꾹 눌러 푸는 상호작용으로 정했다.

- 해제되면 소속 파츠에 알리고, 필수 고정쇠가 모두 풀린 순간 파츠가 `Free` 로 넘어간다.
- 해제된 고정쇠는 **콜라이더를 끈다.** 풀린 고정쇠가 계속 포커스를 뺏으면
  남은 고정쇠를 고르기 어려워지기 때문이다.
- `IsRequired` 를 끈 고정쇠는 풀지 않아도 파츠가 `Free` 로 넘어간다. 장식용으로 쓴다.
- `Release()` 는 여러 번 불러도 한 번만 동작한다. 연출이나 디버그에서 직접 부를 수 있다.

### 2.4 Awake 에 의존하지 않는다

`Initialize()` 를 공개 메서드로 두고 `Awake` 와 프로퍼티 접근에서 모두 부른다.
Unity 는 EditMode 에서 `Awake` 를 호출하지 않으므로, 이렇게 두지 않으면
상태머신을 EditMode 테스트로 검증할 수 없다. 초기화 순서 문제도 함께 사라진다.

`RefreshClamps()` 는 런타임에 고정쇠를 붙이거나 떼어낸 뒤 목록을 다시 모으는 데 쓴다.

## 3. 산출물

코드
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartState.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/SpaceshipPart.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartClamp.cs`

테스트
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/SpaceshipPartTests.cs`
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/PartClampTests.cs`

에셋
- `METAGAME/Assets/_Project/Scenes/Sandbox_Physics.unity` (Case5 추가)
- `METAGAME/Assets/_Project/Scripts/Editor/SandboxSceneBuilder.cs`

## 4. 검증

EditMode 테스트 50건 전부 통과했다. 이번에 추가한 것은 20건이다.

| 영역 | 테스트 |
| --- | --- |
| 초기 상태 | Locked 로 시작하고 kinematic 이다 |
| 전환 거부 | Locked 에서 바로 Carried 로 못 간다, 같은 상태로 못 간다, Welded 는 되돌아가지 않는다 |
| 고정쇠 가드 | 필수 고정쇠가 남아 있으면 Free 로 못 간다, 장식용은 막지 않는다 |
| 물리 표현 | Carried 만 가볍고 PartCarried 레이어를 쓴다, Placed 에서 질량이 복구된다 |
| 이벤트 | 거부된 전환은 이벤트를 내지 않는다 |
| 고정쇠 | 기본 홀드 1.5초, 해제 시 콜라이더 꺼짐, 중복 해제 무시, 해제 후 NotReady |
| 통합 | Interactor 로 실제 홀드해 해제, 중간 취소 시 고정쇠 유지·점유 해제, 해제된 고정쇠는 포커스되지 않음 |

Sandbox 씬 Case5 를 저중력에서 검증한 결과다. 실패 0건.

```
Case5 초기 상태가 Locked                   Locked
Case5 필수 고정쇠 4개                       4개
Case5 고정된 파츠는 kinematic
Case5 파츠가 충돌에도 꿈쩍하지 않음            이동 0.00000m
Case5 고정쇠 1/4 해제 후 상태                Locked (남은 3)
Case5 고정쇠 2/4 해제 후 상태                Locked (남은 2)
Case5 고정쇠 3/4 해제 후 상태                Locked (남은 1)
Case5 고정쇠 4/4 해제 후 상태                Free   (남은 0)
Case5 해제 후 dynamic 전환
Case5 해제 후 질량 복구                      500kg
Case4 파츠 5단 적층 30초 안정                최대 이동 0.0003m
```

![고정쇠 파츠가 추가된 Sandbox](S15P21B202-63-sandbox-clamps.png)

왼쪽부터 Case1~Case4, 오른쪽 끝이 Case5(고정쇠 파츠와 부딪히는 파츠)다.

## 4.1 캐릭터 시스템과의 충돌 정리

캐릭터 브랜치와 대조해 레이어 번호 충돌과 Rigidbody 소유권 문제를 함께 정리했다.
자세한 내용은 `S15P21B202-63-character-puzzle-integration.md` 를 본다.

- `RagdollBuilder.PlayerLayer` 가 8(= 이 체계의 `Ground`) 이었다. `GameLayers.Player`(6)로 옮겼다
- 파츠에 `Grabbable(Climbable = true)` 을 보장해 손이 견인 중 파츠를 가로채지 못하게 했다
- 체력과 기절은 "체력 유지, 체력 0 의 결과만 기절로" 방향으로 정했다

## 5. 미해결 / 후속

- `Free -> Carried` 를 실제로 호출하는 것은 견인 도구다. 이 티켓은 전환 API 만 노출했다
- `Placed` 의 소켓·스냅 판정과 `Welded` 의 용접 진행도는 이후 티켓에서 붙인다.
  지금은 상태로만 존재한다
- 파츠 아트와 프리팹은 Art 파트 범위다. 지금은 그레이박스 큐브로 검증했다
- 고정쇠 개수와 배치는 레벨 디자인에서 정한다. Case5 는 밑면 네 귀퉁이로 두었다
- 네트워크 동기화 — S15P21B202-15

## 6. 참고

- 네임스페이스는 `Metagame.Gameplay.Parts`
- 질량과 치수는 `PhysicsScale` 을 그대로 쓴다. 새 값을 만들지 않았다
- 고정쇠 콜라이더는 `Interactable` 레이어에 두어야 한다. 다른 레이어면 탐지되지 않는다
- 선행: S15P21B202-57, S15P21B202-61, S15P21B202-62
