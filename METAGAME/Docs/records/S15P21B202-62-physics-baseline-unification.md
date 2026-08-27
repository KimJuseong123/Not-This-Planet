# S15P21B202-62 물리 기준을 저중력 Tuning 으로 통일

- Epic: S15P21B202-14 [Puzzle] 퍼즐·상호작용 시스템
- 브랜치: `S15P21B202-62-puzzle-tuning` (`S15P21B202-61-puzzle` 에서 분기)
- 작성일: 2026-08-27

## 1. 목적 / 배경

물리 상수가 두 곳에 나뉘어 서로 다른 값을 갖고 있었다.

| | `Metagame.Core.Tuning` | `PhysicsScale` (S15P21B202-57) |
| --- | --- | --- |
| 중력 | -2.8 | -9.81 |
| Fixed Timestep | 0.01 (100Hz) | 0.02 (50Hz) |
| 캐릭터 | 1.33m / 35kg | 1.8m / 70kg |

여기에 더해 `SpaceBootstrap` 이 실행 시 `Physics.gravity` 와 `Time.fixedDeltaTime` 을
`Tuning` 값으로 덮어쓰는 반면 `ProjectSettings` 는 옛 값(-9.81, 50Hz)을 그대로 두고 있었다.
그 결과 Project Settings 창에서 보는 값, EditMode 테스트가 검증하는 값, 실제 게임이 도는 값이
모두 달랐다. S15P21B202-57 의 `GlobalPhysicsSettings_MatchRecordedBaseline` 테스트는
통과하고 있었지만 실제 게임과 무관한 값을 확인하고 있었다.

## 2. 확정 사항

### 2.1 Tuning 을 기준으로 삼는다

`Tuning` 쪽으로 통일한다. 근거는 다음과 같다.

- 액티브 래그돌의 `RideSpring`, `UprightSpring`, `JumpSpeed`, 낙하 피해가 전부 -2.8 에
  맞춰 튜닝되어 이미 동작한다. 중력을 바꾸면 이 값들을 전부 다시 잡아야 한다.
- `PhysicsScale` 쪽 값 위에는 아직 아무것도 올라가 있지 않다. 버리는 비용이 없다.
- 행성 탐사와 우주선 조립이라는 설정에 저중력이 맞고,
  운석에 맞아 날아가는 연출도 저중력에서 살아난다.
- 100Hz 는 적층 안정성에 유리하다. 솔버가 두 배로 촘촘하게 돈다.

`Tuning` 의 값은 하나도 바꾸지 않았다. `SpaceBootstrap` 도 건드리지 않았다.
바꾼 것은 `ProjectSettings` 와 퍼즐 쪽 값뿐이다.

### 2.2 ProjectSettings 를 단일 진실 공급원으로 만든다

| 파일 | 이전 | 이후 |
| --- | --- | --- |
| `DynamicsManager.asset` `m_Gravity` | -9.81 | **-2.8** |
| `TimeManager.asset` Fixed Timestep | 0.0199999 (50Hz) | **0.01 (100Hz)** |

Fixed Timestep 은 유리수로 저장된다. `m_Count / m_Rate.m_Numerator` 가 초 단위 값이며
`1411200 / 141120000 = 0.01` 이다.

이제 `SpaceBootstrap` 의 런타임 덮어쓰기는 같은 값을 다시 쓰는 것이 되어 무해하다.
Project Settings 창에서 보는 값과 실제 게임 값이 일치한다.

### 2.3 PhysicsScale 은 퍼즐 전용 값만 갖는다

중력, Fixed Timestep, 캐릭터 치수·질량을 `PhysicsScale` 에서 제거하고 `Tuning` 을 참조한다.
`Metagame.Gameplay` 는 이미 `Metagame.Core` 를 참조하고 있어 추가 설정이 필요 없었다.

### 2.4 파츠 수치 재산정

캐릭터를 기준자로 삼아 비율을 유지했다.

| 항목 | 이전 (캐릭터 1.8m·70kg 기준) | 이후 (캐릭터 1.33m·35kg 기준) |
| --- | --- | --- |
| 파츠 치수 | 4 x 5m | **3 x 3.7m** |
| 파츠 질량 | 1000kg | **500kg** |
| 견인 중 질량 | 70kg (= 당시 캐릭터 무게) | **35kg** (= `Tuning.BodyMass`) |
| 견인 도구 | 50kg | **25kg** |

견인 중 질량을 캐릭터 무게로 두는 규칙은 그대로다.
밀칠 수는 있지만 깔아뭉갤 수는 없어야 한다.

캐릭터의 손 잡기가 이미 같은 문제를 같은 방식으로 풀고 있다.
`Tuning.CarryMassScale = 0.35`, `Tuning.CarrySupport = 0.85`, `ForceMode.Acceleration`.
견인 빔도 PID 를 새로 짜기보다 이 방식을 확장하는 편이 일관된다.

## 3. 산출물

설정
- `METAGAME/ProjectSettings/DynamicsManager.asset`
- `METAGAME/ProjectSettings/TimeManager.asset`

코드
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Physics/PhysicsScale.cs`
- `METAGAME/Assets/_Project/Scripts/Editor/SandboxSceneBuilder.cs`
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/Metagame.Gameplay.Tests.Editor.asmdef`
  (`Metagame.Core` 참조 추가)
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/GameLayersTests.cs`

에셋
- `METAGAME/Assets/_Project/Scenes/Sandbox_Physics.unity` (저중력 기준으로 재생성)

## 4. 검증

EditMode 테스트 30건 전부 통과했다.

새로 추가한 검사는 세 가지다.

| 테스트 | 검증 내용 |
| --- | --- |
| `ProjectSettings_MatchRuntimeTuning` | ProjectSettings 의 중력·틱이 `Tuning` 과 같은지. 다시 어긋나면 실패한다 |
| `GlobalSolverSettings_MatchRecordedBaseline` | solver 기록값과 실제 설정 일치 |
| `PartDimensions_StayLargerThanCharacter` | 파츠가 캐릭터의 2배 높이, 10배 질량 이상인지 |

Sandbox_Physics 씬을 저중력에서 30초(3000 스텝) 재시뮬레이션한 결과다. 실패 0건.

```
gravity=-2.8  fixedDeltaTime=0.01  bodyMass=35  partMass=500  carried=35

Case1 견인 파츠(35kg)가 플레이어를 밈           밀림 0.03m
Case1 플레이어가 지면 위에 남아 있음            y=0.67
Case2 놓인 파츠(500kg)가 훨씬 강하게 밀어냄     밀림 0.54m (견인 중 대비 15.5배)
Case3a 기절 캡슐과 플레이어가 서로 밀지 않음     거리 0.141 -> 0.141
Case3b 견인 파츠가 기절 캡슐을 통과            이동 0.0000m
Case4 파츠 5단 적층 30초 안정 (저중력)         최대 이동 0.0003m
```

![저중력 Sandbox 30초 시뮬레이션](S15P21B202-62-sandbox-lowgravity.png)

왼쪽부터 Case1(견인 파츠 충돌), Case2(놓인 파츠 충돌), Case3(기절 보호), Case4(5단 적층).

### 예상과 달랐던 점

착수 전에는 저중력이 적층에 불리할 것으로 봤다.
마찰력은 중력에 비례해 줄어드는데 충격량은 중력과 무관하므로 탑이 더 쉽게 밀릴 것이라는 판단이었다.

실측은 반대였다.

| | 지구 중력 / 50Hz | 저중력 / 100Hz |
| --- | --- | --- |
| 5단 적층 30초 최대 이동 | 0.0073m | **0.0003m** |
| 견인 중 대 놓인 파츠 밀림 차이 | 4.0배 | **15.5배** |

100Hz 로 솔버가 두 배 촘촘히 도는 효과가 마찰 감소분을 크게 앞질렀다.
질량 대비 충돌 위력 차이도 저중력에서 오히려 뚜렷해졌다. 마찰이 낮아 밀린 거리가 멀리 간다.

따라서 착수 전에 대비책으로 적어 두었던 파츠 전용 고마찰 재질은 지금 단계에서는 필요 없다.
운석이 들어오는 시점에 다시 측정한다.

## 5. 미해결 / 후속

- 견인 빔의 제어 방식 — `Tuning.CarryMassScale` / `CarrySupport` 방식을 확장할지
  별도 PID 를 둘지는 견인 구현 티켓에서 정한다
- 파츠 4종의 실제 치수·질량 — 기획 확정 후. 지금 값은 그레이박스 기준이다
- 기절 유발 충격량 임계값 — S15P21B202-13 (Character)

## 6. 참고

- `Tuning` 과 `SpaceBootstrap` 은 Character 파트 소유 파일이며 이번 작업에서 수정하지 않았다.
  값을 맞춘 쪽은 `ProjectSettings` 와 퍼즐 코드다
- S15P21B202-57 기록 문서의 스케일 수치는 이 문서로 대체된다
- 선행: S15P21B202-57, S15P21B202-61
