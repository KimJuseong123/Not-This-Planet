# S15P21B202-64 견인 광선 운반 물리

- Epic: S15P21B202-14 [Puzzle] 퍼즐·상호작용 시스템
- 브랜치: `S15P21B202-64-puzzle` (develop 에서 분기)
- 작성일: 2026-08-27

## 1. 목적 / 배경

S15P21B202-63 으로 파츠가 고정쇠에서 풀려 `Free` 가 되지만 옮길 수단이 없었다.
견인 광선은 1장 운반 콘텐츠의 유일한 이동 수단이고, 동시에 500kg 대형 물체를 힘으로
제어하는 부분이라 발산·진동이 나기 가장 쉬운 곳이다.

## 2. 확정 사항

### 2.1 빔의 물리와 빔을 들고 다니는 방식을 분리한다

`TractorBeam` 은 기준점 `Transform` 하나만 안다. 그 기준점이 비행접시에 붙는지
손에 드는 장치에 붙는지는 도구 프리팹이 정한다.

기획서의 도구 형태(조작 방식, 이동 속도, 동시 탑승)가 미정이라 그 결정을 기다리지 않고
빔 물리를 먼저 확정할 수 있게 했다. 도구 형태가 정해지면 기준점을 그쪽에 붙이면 된다.

`Interactor` 와 같은 규칙을 따른다. 입력을 알지 못하고 소유자가 `Tick(deltaTime)` 을
호출한다. 그래서 EditMode 에서 `Physics.Simulate` 와 짝지어 실제 물리 거동까지 검증할 수 있다.

### 2.2 제어는 가속 기반 스프링-댐퍼 + 중력 보상

```
a = (목표점 - 무게중심) * BeamAccel - 속도 * BeamDamp + (-중력 * BeamGravitySupport)
a 를 BeamAccelMax 로 클램프
AddForce(a, ForceMode.Acceleration)
```

`ForceMode.Acceleration` 을 쓰므로 파츠 질량과 무관하게 같은 반응이 나온다.
500kg 을 들면서도 조작이 가볍게 느껴지는 이유가 이것이다.
캐릭터의 손 잡기가 이미 같은 계열을 쓴다(`Tuning.CarryAccel` 계열)는 점도 맞췄다.
별도 PID 를 새로 만들지 않았다.

| 상수 | 값 | 근거 |
| --- | --- | --- |
| `BeamRange` | 12m | 붙잡을 수 있는 최대 거리 |
| `BeamHoldDistance` | 6m | 기준점 앞 목표점까지 |
| `BeamAccel` | 25 (1/s²) | 스프링의 k |
| `BeamDamp` | 10 (1/s) | 임계 감쇠 조건 d = 2√k = 2√25 = 10. 낮추면 출렁인다 |
| `BeamAccelMax` | 30 m/s² | 중력 보상분(2.8)보다 충분히 큼 |
| `BeamGravitySupport` | 1 | 완전 무중력. 놓으면 보상이 사라져 그대로 떨어진다 |
| `BeamAngularGain` | 6 (1/s) | 자세 보정. 결과는 `PartMaxAngularSpeed` 로 잘린다 |

감쇠를 임계값으로 맞춘 것이 핵심이다. 실측에서 정착 오차 0.000m, 정착 속도 0.000m/s 가 나왔다.

### 2.3 자세는 붙잡은 순간의 회전을 유지한다

파츠가 제멋대로 굴러가면 조립 위치를 맞출 수 없다. 붙잡은 시점의 월드 회전을
목표로 잡고 그 자세를 유지시킨다.

각속도를 **직접 대입**한다. 토크로 밀면 대형 파츠가 관성으로 상한을 넘어서기 쉬운데,
직접 쓰면 `PhysicsScale.PartMaxAngularSpeed` 를 확실히 지킬 수 있다.

### 2.4 상태 전환은 상태머신에만 요청한다

빔은 파츠의 질량과 레이어를 **절대 직접 건드리지 않는다.**
`SpaceshipPart.TryTransitionTo` 만 호출한다.

```
붙잡기: Free 또는 Placed  ->  Carried
놓기:   Carried           ->  Placed
```

거부 사유는 `BeamGrabResult` 로 돌려준다. `Clamped`(고정쇠 남음),
`Welded`(이미 접합됨), `AlreadyCarried`(다른 빔이 들고 있음), `BeamBusy`(이 빔이 이미 사용 중),
`NoTarget`. UI 피드백 문구를 여기서 고른다.

`Tick` 안에서 파츠 상태가 `Carried` 가 아니게 되면 빔이 스스로 물러난다.
다른 시스템이 상태를 바꿨을 때 빔이 계속 힘을 주는 상황을 막는다.

### 2.5 자동 해제

사거리를 벗어나거나 파츠가 파괴되면 자동으로 놓고 `Placed` 로 복구한다.
사거리 이탈도 `ReleasedPart` 이벤트를 낸다. 소유자가 두 경우를 구분할 필요가 없다.

### 2.6 대상 탐색

기준점에서 정면으로 반지름 0.5m 구체를 쏘아 가장 가까운 파츠를 고른다.
큰 파츠를 겨눌 때 예민하지 않게 하려고 광선 대신 구체를 썼다.

마스크는 `PartMask | BeamQueryMask` 다. 파츠 본체와 별도로 "여기를 겨누면 이 파츠"라는
대리 콜라이더를 둘 수 있게 했다. `BeamQuery` 레이어는 충돌 매트릭스에서 전부 꺼져 있어
물리에 영향을 주지 않는다.

## 3. 산출물

코드
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/TractorBeam.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/BeamGrabResult.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Physics/PhysicsScale.cs` (빔 상수)
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Physics/SandboxBeamDriver.cs` (Sandbox 전용)

테스트
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/TractorBeamTests.cs`
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/CharacterIntegrationTests.cs` (가드 1건 추가)

에셋
- `METAGAME/Assets/_Project/Scenes/Sandbox_Physics.unity` (Case6 추가)
- `METAGAME/Assets/_Project/Scripts/Editor/SandboxSceneBuilder.cs`

## 4. 검증

EditMode 테스트 73건 전부 통과했다. 이번에 추가한 것은 16건이다.

| 영역 | 테스트 |
| --- | --- |
| 붙잡기 거부 | 대상 없음, 고정쇠 남음, 이미 용접됨, 다른 빔이 들고 있음, 이 빔이 이미 사용 중 |
| 상태 연동 | 붙잡으면 Carried·레이어·질량 전환, 놓으면 Placed·질량 복구, 이벤트 1회 |
| 추적 거동 | 1초 안에 절반 이상 접근, 10초 후 정착·무진동, 중력에 처지지 않음 |
| 자동 해제 | 사거리 이탈, 외부에서 상태가 바뀐 경우 |
| 상한 | 각속도가 `PartMaxAngularSpeed` 를 넘지 않음 |
| 통합 | 놓은 뒤 중력 보상이 사라져 떨어짐 |

Sandbox Case6 를 저중력에서 검증한 결과다. 실패 0건.

```
beam=(50, 10, 0)  forward=(0, -1, 0)  hold=(50, 4, 0)  part=(50, 1.85, 0)

Case6 파츠가 Free 상태로 저장됨          Free
Case6 빔이 파츠를 붙잡음                Grabbed
Case6 붙잡으면 Carried 로 전환          Carried
Case6 견인 중 질량                     35kg
Case6 목표점까지 끌려옴                 오차 2.15m -> 0.000m
Case6 정착 후 출렁이지 않음              속도 0.000m/s
Case6 지면에서 들어 올려짐               y 1.85 -> 4.00
Case6 놓으면 Placed 로 전환            Placed
Case6 놓으면 질량 복구                  500kg
Case6 놓은 뒤 떨어짐                   y 4.00 -> 1.85
Case5 고정 파츠는 여전히 꿈쩍하지 않음     이동 0.00000m
Case4 파츠 5단 적층 안정                최대 이동 0.0003m
```

들고 있는 동안 (파츠가 지면에서 떠 있고 아래에 그림자가 보인다)

![견인 중](S15P21B202-64-beam-held.png)

놓은 뒤

![해제 후](S15P21B202-64-beam-released.png)

빔 자체는 아직 눈에 보이지 않는다. 견인 광선 이펙트는 Art 파트 범위다.

기획대로 **위에서 아래를 겨누는** 배치로 두었다. 비행접시가 대상 위에 떠서 끌어올리는 형태다.
처음에는 수평으로 겨누게 만들었는데 조준선이 파츠 위를 지나가 붙잡지 못했다.

## 5. 미해결 / 후속

- 도구 형태(비행접시 조작, 고도, 동시 탑승) — 기획 확정 후. 빔은 기준점만 받으므로
  그 결정이 빔 코드를 바꾸지 않는다
- 목표 회전을 빔이 조종하는 기능 — 지금은 붙잡은 자세를 유지만 한다.
  적층에서 방향을 맞춰야 하므로 소켓 티켓에서 함께 넣는다
- 소켓 스냅과 적층 판정 — 다음 티켓
- 용접 진행도 — 그 다음 티켓
- 견인 광선 VFX — Art 파트
- 반작용(빔이 파츠를 당긴 만큼 도구가 밀리는 것) — 도구 형태 확정 후.
  `Tuning.CarryReaction` 이 손 잡기에서 같은 문제를 다루고 있다
- 네트워크 동기화 — S15P21B202-15

## 6. 참고

- 네임스페이스는 `Metagame.Gameplay.Parts`
- 빔 상수는 `PhysicsScale` 에 있고 컴포넌트의 직렬화 필드가 그 값을 기본값으로 받는다.
  디자이너가 인스턴스별로 조정할 수 있다
- 선행: S15P21B202-57, S15P21B202-61, S15P21B202-62, S15P21B202-63
