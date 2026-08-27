# S15P21B202-57 상호작용 물리 기준 및 레이어 체계 확정

- Epic: S15P21B202-14 [Puzzle] 퍼즐·상호작용 시스템
- 브랜치: `S15P21B202-57-puzzle`
- 작성일: 2026-08-27

> 이 문서의 스케일 수치(중력 -9.81, Fixed Timestep 0.02, 캐릭터 1.8m·70kg,
> 파츠 4x5m·1000kg)는 S15P21B202-62 에서 저중력 `Metagame.Core.Tuning` 기준으로 대체되었다.
> 레이어 체계와 충돌 매트릭스 결정은 그대로 유효하다.
> 최신 수치는 `S15P21B202-62-physics-baseline-unification.md` 를 본다.

## 1. 목적 / 배경

퍼즐·상호작용 시스템(고정쇠, 견인, 적층, 용접, 붕괴)을 구현하기 전에 레이어 체계와
물리 기준값을 고정한다. 레이어 인덱스와 기준 질량은 확정 후 변경하면 배치된 모든
파츠·플레이어 프리팹을 다시 설정해야 하므로, 게임플레이 코드가 없는 지금이 비용이 가장 낮다.

## 2. 확정 사항

### 2.1 Layer

| idx | 이름 | 용도 |
| --- | --- | --- |
| 6 | Player | 플레이어 캡슐 |
| 7 | PlayerDowned | 기절 상태 |
| 8 | Ground | 지형·기지 정적 콜라이더 |
| 9 | Part | 우주선 파츠 |
| 10 | PartCarried | 견인 중인 파츠 |
| 11 | Meteor | 운석 |
| 12 | Interactable | 고정쇠·용접점 (쿼리 전용) |
| 13 | Prop | 소품 |
| 14 | BeamQuery | 견인 광선 레이캐스트 전용 |

Layer 0~5(Default, TransparentFX, Ignore Raycast, Water, UI)는 변경하지 않았다.

### 2.2 충돌 매트릭스

OFF 로 설정한 조합과 근거:

| 조합 | 근거 |
| --- | --- |
| PlayerDowned x Player | 기절한 동료를 구조할 때 캡슐끼리 끼는 현상 방지 |
| PlayerDowned x PlayerDowned | 위와 동일 |
| PartCarried x PlayerDowned | 기절한 플레이어는 스스로 빠져나올 수 없다. 파츠에 눌리면 구조가 불가능해진다 |
| Meteor x Meteor | 낙하 중 운석끼리 튕겨 예측 불가한 궤도가 되는 것 방지 |
| Interactable x 전 레이어 | 물리 충돌 없이 레이어마스크 쿼리로만 사용 |
| BeamQuery x 전 레이어 | 위와 동일 |

**PartCarried x Player 는 켠다.** 견인 중인 파츠로 동료를 툭 치며 방해하는 것이
협동 게임의 재미이므로 충돌 자체를 없애지 않는다. 압사 방지는 레이어가 아니라
질량으로 처리한다 (2.4 참고).

Player x Ground, Player x Part, Player x PartCarried, Part x Ground,
Part x PartCarried, Meteor x Player, Meteor x Part 는 켜진 상태를 유지한다.

### 2.3 스케일 기준

1 unit = 1 meter.

| 항목 | 값 |
| --- | --- |
| 플레이어 | 높이 1.8m / 반지름 0.3m / 70kg |
| 우주선 파츠 | 4 x 5m / 1000kg |
| 견인 도구 | 50kg |
| 중력 | -9.81 |
| Fixed Timestep | 0.02 (50Hz) |

### 2.4 파츠 충돌 피해 규칙

파츠는 상태에 따라 질량을 바꾼다. 레이어로 충돌을 끄는 대신 질량으로 위력을 조절한다.

| 상태 | 레이어 | 질량 | 플레이어에게 |
| --- | --- | --- | --- |
| 견인 중 | PartCarried | 70kg (`CarriedPartMass`) | 밀림. 방해는 되지만 다치지 않는다 |
| 놓임 / 용접됨 | Part | 1000kg (`PartMass`) | 기절 판정 대상 |

견인 중 질량을 사람 한 명 무게(70kg)로 잡은 이유:

- 10kg 처럼 더 낮추면 운반 속도로 부딪혀도 플레이어가 2~3cm 밀릴 뿐이라
  "방해"가 성립하지 않는다. 충돌 위력은 질량에 비례하기 때문이다.
- 플레이어보다 무거우면 밀쳐서 넘어뜨릴 수 있게 되어 압사 문제가 돌아온다.
- 사람 한 명 무게라는 기준은 팀에 설명하기 쉽고 튜닝 근거가 명확하다.

"1000kg 을 빔으로 들면 가벼운 느낌"은 이 질량이 아니라 **견인 빔의 제어 게인**으로
만든다. 취급감(드는 느낌)과 충돌 위력(남을 미는 힘)은 분리해서 다룬다.

깔렸을 때의 처리는 **기절**로 확정했다. 즉사나 리스폰이 아니라 동료가 일으켜 세워야 한다.
운석에 맞았을 때의 규칙과 일관되며, 튜토리얼 장에 즉사는 과하다.
기절을 유발하는 충격량 임계값과 지속 시간은 캐릭터 물리 반응 티켓에서 수치로 정한다.

### 2.5 전역 물리 설정을 변경하지 않기로 한 결정

당초 Solver Iterations 12 / Velocity 2 / Max Angular Speed 20 으로 상향할 계획이었으나,
Unity 6000.5.4f1 프로젝트의 실제 현재값이 **14 / 4 / 28** 로 목표치보다 이미 높았다.
따라서 `DynamicsManager.asset` 의 solver 관련 값은 변경하지 않는다.

대형 파츠의 회전 폭주 억제는 전역 `m_DefaultMaxAngularSpeed` 를 낮추는 대신
Part 프리팹의 `Rigidbody.maxAngularVelocity` 에만 적용한다. 전역값을 낮추면
운석·소품·파편까지 함께 묶여 의도하지 않은 제약이 생기기 때문이다.
기준값은 `PhysicsScale.PartMaxAngularSpeed = 5` 이며 견인 구현 티켓에서 실측 후 조정한다.

## 3. 산출물

설정
- `METAGAME/ProjectSettings/TagManager.asset` — Layer 6~14 정의
- `METAGAME/ProjectSettings/DynamicsManager.asset` — 충돌 매트릭스 재구성

패키지
- `METAGAME/Packages/manifest.json`, `packages-lock.json` — `com.unity.test-framework@1.7.0` 추가

코드
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Physics/GameLayers.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Physics/PhysicsScale.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Physics/SandboxLauncher.cs` (Sandbox 전용 디버그)
- `METAGAME/Assets/_Project/Scripts/Editor/SandboxSceneBuilder.cs`
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/Metagame.Gameplay.Tests.Editor.asmdef`
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/GameLayersTests.cs`

에셋
- `METAGAME/Assets/_Project/Scenes/Sandbox_Physics.unity` (그레이박스 검증용, 빌드 씬 미등록)

네임스페이스 주의: 폴더는 `Physics/` 지만 네임스페이스는 `Metagame.Gameplay` 를 사용한다.
`Metagame.Gameplay.Physics` 로 두면 하위 네임스페이스에서 `UnityEngine.Physics` 가 가려진다.

## 4. 검증

EditMode 테스트 10건 전부 통과했다 (`-runTests -testPlatform EditMode`).

| 테스트 | 검증 내용 |
| --- | --- |
| LayerConstants_MatchTagManager | 상수와 TagManager 인덱스 일치 |
| LayerIndices_AreUnique | 인덱스 중복 없음 |
| CarriedPart_CollidesWithStandingPlayer | 견인 파츠가 서 있는 플레이어와 충돌 |
| CarriedPart_IgnoresDownedPlayer | 견인 파츠가 기절 플레이어는 통과 |
| CarriedPartMass_IsFarLighterThanPlacedPart | 견인 중 질량이 플레이어 이하이고 놓인 상태보다 훨씬 가벼움 |
| DownedPlayer_IgnoresOtherPlayers | 기절 캡슐 끼임 방지 |
| Meteor_DoesNotCollideWithOtherMeteors | 운석끼리 비충돌 |
| QueryOnlyLayers_HaveNoCollisionsAtAll | Interactable/BeamQuery 전 레이어 OFF |
| GameplayCollisions_RemainEnabled | 필요한 충돌이 꺼지지 않았는지 (과다 차단 회귀 방지) |
| GlobalPhysicsSettings_MatchRecordedBaseline | 중력·틱·solver 기록값과 실제 설정 일치 |

Sandbox_Physics 씬을 `Physics.Simulate` 로 30초(1500 스텝) 돌린 결과다.

| 케이스 | 검증 | 결과 |
| --- | --- | --- |
| Case1 | 운반 중 파츠(70kg)가 4m/s 로 플레이어와 충돌 | 플레이어 0.03m 밀림, 서 있는 상태 유지 |
| Case2 | 같은 조건의 1000kg 파츠 | 플레이어 0.11m 밀림 (견인 중 대비 4.0배) |
| Case3a | 기절 캡슐과 플레이어가 겹침 | 거리 0.141 -> 0.141, 서로 밀지 않음 |
| Case3b | 견인 파츠가 기절 캡슐 위로 낙하 | 이동 0.0000m, 완전 통과 |
| Case4 | 1000kg 파츠 5단 적층 30초 | 최대 이동 0.0073m |

Case1 과 Case2 의 밀림 거리가 절대값으로 작은 것은 견인 도구가 아직 없어서
파츠가 자유 낙하·활주하며 지면 마찰에 에너지를 잃기 때문이다.
실제 게임에서는 빔이 파츠를 계속 구동하므로 훨씬 강하게 민다.
**이 티켓은 "충돌이 켜져 있고 질량으로 위력을 나눈다"는 규칙까지만 확정하고,
미는 세기의 실제 수치는 견인 구현 티켓에서 빔과 함께 튜닝한다.**

![Sandbox_Physics 30초 시뮬레이션 결과](S15P21B202-57-sandbox.png)

왼쪽부터 Case1(견인 파츠 충돌), Case2(1000kg 충돌), Case3(기절 보호), Case4(5단 적층).

씬은 `Tools > Metagame > Rebuild Physics Sandbox` 로 재생성할 수 있다.
Play 하면 `SandboxLauncher` 가 파츠에 운반 속도를 주어 충돌이 재현된다.

## 5. 미해결 / 후속

- 견인 PID 게인, `PartMaxAngularSpeed`, 운반 중 충돌 위력 실측 조정 — 견인 구현 티켓
- 기절 유발 충격량 임계값과 기절 지속 시간 — 캐릭터 물리 반응 티켓 (S15P21B202-13)
- 파츠 4종의 실제 치수·질량 확정 — 기획 확정 후

## 6. 참고

- 전제: 4인 고정 협동, 호스트 권위 물리 (물리 시뮬레이션은 호스트에서만 수행)
- 범위 밖: 캐릭터 이동·시점(S15P21B202-13), Netcode 도입(S15P21B202-15)
- `Packages/manifest.json` 에 `com.unity.test-framework@1.7.0` 을 추가했다.
  본 티켓 이전까지 프로젝트에 테스트 프레임워크가 설치되어 있지 않아
  EditMode 테스트를 컴파일할 수 없었다.
- README 는 URP 와 Input System 을 사용한다고 기술하고 있으나
  실제 `manifest.json` 에는 두 패키지가 모두 없다. 본 티켓 범위 밖이므로 손대지 않았다.
- `ProjectSettings/TagManager.asset`, `DynamicsManager.asset` 은 전 팀 공유 파일이며
  `m_LayerCollisionMatrix` 는 단일 해시 문자열이라 git 자동 머지가 불가능하다.
  머지 후 각 팀원은 Unity Editor 재시작이 필요하다.
