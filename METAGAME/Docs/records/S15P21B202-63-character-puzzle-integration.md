# S15P21B202-63 캐릭터·퍼즐 시스템 충돌 정리

- Epic: S15P21B202-14 [Puzzle] 퍼즐·상호작용 시스템
- 브랜치: `S15P21B202-63-puzzle`
- 관련: S15P21B202-25 (Character, MR !8), S15P21B202-59 (체력), S15P21B202-13
- 작성일: 2026-08-27

캐릭터 브랜치(25)와 퍼즐 스택(57 · 61 · 62 · 63)을 대조한 결과다.
텍스트 병합 충돌은 없었다(`git merge-tree` 0건). 문제는 같은 자원을 두 시스템이
서로 모르고 쓰고 있는 지점들이었다.

## 1. 레이어 8 번을 둘이 쓰고 있었다 — 해결

`RagdollBuilder.PlayerLayer = 8` 이었고, 인형의 모든 부위가 이 번호로 설정된다.
그런데 S15P21B202-57 의 레이어 체계에서 **8 번은 `Ground`** 다.

이 상태로 두면 세 가지가 조용히 깨진다.

| 증상 | 원인 |
| --- | --- |
| 캐릭터가 지면을 못 찾는다 | `WorldMask = ~(1 << 8)` 이 지면(8)을 함께 제외한다 |
| 파츠 충돌 규칙이 적용되지 않는다 | 매트릭스의 `PartCarried x Player` 규칙은 6 번에 걸려 있는데 인형은 8 번에 있다 |
| 기절 보호가 동작하지 않는다 | `PlayerDowned x Player = OFF` 가 인형에 적용되지 않는다 |

특히 첫 번째가 위험하다. 레벨 제작자가 레이어 체계대로 지면을 `Ground` 에 두는 순간
캐릭터의 접지 판정이 지면을 보지 못한다. 컴파일도 되고 콘솔 오류도 없다.

**해결**: `PlayerLayer` 를 공용 체계의 `GameLayers.Player`(6)로 바꿨다.
상수 하나라 인형 조립, 우주복, 셀프테스트가 한 번에 따라온다.

`WorldMask` 도 함께 넓혔다.

```csharp
public static int WorldMask =>
    ~((1 << GameLayers.Player) | (1 << GameLayers.PlayerDowned)
      | (1 << GameLayers.Interactable) | (1 << GameLayers.BeamQuery));
```

- `PlayerDowned` 를 뺀 이유: 기절한 동료가 지형으로 잡히면 그 위에 올라선다.
- `Interactable` · `BeamQuery` 를 뺀 이유: 고정쇠 트리거가 접지 판정에 걸리면
  허공에 서게 된다. 프로젝트 설정의 `Queries Hit Triggers` 가 켜져 있어 실제로 잡힌다.

이 수정으로 캐릭터 쪽 동작이 달라지는 것은 인형이 올라가는 레이어 번호뿐이다.
`Tuning` 값과 `SpaceBootstrap` 은 건드리지 않았다.

## 2. 손 잡기와 견인이 같은 Rigidbody 를 두고 다툴 예정이었다 — 선제 차단

지금 develop 의 손 잡기는 조인트와 `connectedMassScale` 로 동작하며
`isKinematic` 을 건드리지 않는다. 그래서 **오늘은 충돌하지 않는다.**

문제는 MR !8 이 머지되는 시점이다. 25 브랜치의 `PlayerRig` 는 운반 경로를 바꿔
`BeginCarriedBody` / `MoveCarriedBody` 에서 `Rigidbody.isKinematic = true` 를 직접 켠다.
대상 조건은 이렇다.

```csharp
// PlayerRig.IsUsableTool (S15P21B202-25)
target != null && !target.Climbable && body != null && !body.isKinematic
        && body.mass < Tuning.FixedGripMass    // 40kg
```

견인 중 파츠는 `PhysicsScale.CarriedPartMass` = 35kg 이라 이 조건을 통과한다.
즉 빔이 옮기고 있는 파츠를 플레이어가 손으로 가로챌 수 있고,
그 순간 손과 `SpaceshipPart` 상태머신이 같은 `isKinematic` 을 두고 다툰다.

**해결**: `SpaceshipPart` 가 `Grabbable` 을 `Climbable = true` 로 보장한다.

```csharp
void EnsureGrabbable() => Grabbable.Mark(gameObject, name, climbable: true);
```

- `Climbable` 이 켜지면 `IsUsableTool` 이 false 가 되어 손은 "고정물 짚기" 경로를 탄다.
  `isKinematic` 을 건드리지 않는다.
- 파츠에 매달려 함께 이동하는 것은 그대로 가능하다. 협동의 재미를 잃지 않는다.
- 파츠를 옮기는 수단은 견인 도구 하나로 유지된다. 이것이 기획의 운반 콘텐츠 전제다.
- 이미 핸드레일이 같은 방식으로 표시되어 있어(`SpaceSelfTest` 가 확인한다)
  새로운 개념을 도입하는 것이 아니다.

develop 에서 `Climbable` 은 아직 읽히지 않으므로 **지금은 동작 변화가 없다.**
MR !8 이 머지되는 순간 가드로 작동한다.

`Locked` 상태의 파츠는 kinematic 이라 `IsUsableTool` 의 `!body.isKinematic` 조건에서도
따로 걸러진다. 가드가 이중으로 걸린다.

## 3. 체력과 기절 — 방향 결정

충돌은 아니지만 방향이 갈려 있었다.

| | 캐릭터 (25 · 59) | 퍼즐 (57 기록) |
| --- | --- | --- |
| 피해 모델 | 체력 100, `Health <= 0` 이면 `IsDead` | 기절, 동료가 일으켜 세운다 |
| 피해 전달 | `DamageSource` 컴포넌트 → `PlayerRig.ApplyDamage` | 미구현 |
| 낙하 피해 | 있음 | 없음 |

**결정: 체력을 유지하고, 체력 0 의 결과만 기절로 바꾼다.**

체력을 버리고 기절만 남기면 이미 동작하는 낙하 피해·HUD·`DamageSource` 를 버려야 한다.
반대로 기절을 버리면 "동료가 일으켜 세운다"는 1장의 협동 축이 사라진다.
둘을 합치면 양쪽을 다 지킬 수 있다.

필요한 변경은 `PlayerRig` 에 한정된다.

```
Health <= 0  ->  IsDead 대신 Downed 상태로 전환
Downed 동안   ->  레이어를 GameLayers.PlayerDowned 로 옮긴다
Revive(회복량) ->  동료가 일으켜 세우면 일부 체력으로 복귀
```

`Downed` 로 레이어를 옮기는 것까지 하면 57 에서 만든 두 규칙이 그때 살아난다.
`PlayerDowned x Player = OFF`(구조 시 끼임 방지)와
`PartCarried x PlayerDowned = OFF`(눌려서 구조 불가 방지)다.

파츠와 운석의 피해는 **별도 시스템을 만들지 않고 기존 `DamageSource` 로 통일한다.**
파츠에 `DamageSource` 를 붙이고 `minImpactSpeed` 와 `damage` 를 상태에 맞게 주면
500kg 파츠에 깔릴 때 체력이 0 이 되어 기절로 이어진다.

`PlayerRig` 는 Character 파트 소유 파일이므로 이 변경은 이 티켓에서 하지 않았다.
S15P21B202-13 에서 담당자와 함께 넣는다.

## 4. 검증

EditMode 테스트 57건 전부 통과했다. 이번에 추가한 7건은 위 1·2 번을 감시한다.

| 테스트 | 내용 |
| --- | --- |
| `RagdollPlayerLayer_MatchesSharedLayerScheme` | 인형 레이어가 공용 체계의 Player 와 같은지 |
| `WorldMask_ExcludesPlayerLayers` | 자기 몸과 기절 상태를 쿼리에서 뺐는지 |
| `WorldMask_ExcludesQueryOnlyLayers` | 고정쇠 트리거가 접지 판정에 걸리지 않는지 |
| `WorldMask_KeepsGroundAndParts` | 지면과 파츠는 남겨 두었는지 |
| `Part_IsMarkedClimbable_SoHandsCannotHijackIt` | 파츠가 손 들기 대상에서 빠지는지 |
| `Part_KeepsClimbableFlag_AcrossStateTransitions` | 상태가 바뀌어도 가드가 유지되는지 |
| `LockedPart_IsKinematic_SoHandsSkipItRegardlessOfMass` | 고정 파츠의 이중 가드 |

## 5. 후속

- `PlayerRig` 의 체력 0 → 기절 전환과 `Revive` — S15P21B202-13, 담당자 합의 필요
- 파츠·운석의 `DamageSource` 설정값 — 운석 티켓에서 실측 후 정한다
- MR !8 머지 후 `Tuning.FixedGripMass` 와 `CarriedPartMass` 의 관계를 감시하는
  테스트를 추가한다. 지금은 `FixedGripMass` 가 develop 에 없어 참조할 수 없다
