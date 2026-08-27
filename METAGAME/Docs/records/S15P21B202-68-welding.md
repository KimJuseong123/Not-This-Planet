# S15P21B202-68 용접 진행도 및 접합 확정

- Epic: S15P21B202-14 [Puzzle] 퍼즐·상호작용 시스템
- 브랜치: `S15P21B202-68-puzzle` (develop 에서 분기)
- 작성일: 2026-08-27

## 1. 목적 / 배경

S15P21B202-66 으로 파츠가 소켓에 붙지만 `breakForce` 가 낮은 조인트로만 잡혀 있어
흔들리면 떨어진다. 용접은 그 접합을 확정하는 단계다.

## 2. 확정 사항

### 2.1 완료 여부가 아니라 완료 비율로 강도를 올린다

"덜 용접한 상태" 가 물리로 존재해야 한다. 용접점 4개 중 2개만 채웠으면 절반만 튼튼해야
운석이 떨어질 때 용접하지 않은 층이 무너지는 긴장이 생긴다.

```csharp
breakForce  = Lerp(UnweldedBreakForce, WeldedBreakForce, WeldRatio)
breakTorque = Lerp(UnweldedBreakTorque, WeldedBreakTorque, WeldRatio)
```

`WeldRatio` 는 접합에 달린 용접점들의 진행도 평균이다.

| 상수 | 값 | 비고 |
| --- | --- | --- |
| `WeldSecondsPerPoint` | 3초 | 용접점 하나를 채우는 시간 |
| `WeldedBreakForce` | 200,000 N | 미접합 8,000N 의 25배 |
| `WeldedBreakTorque` | 150,000 Nm | |

용접 완료값을 무한으로 두지 않았다. 기획서가 "운석 충격에도 **쉽게** 분리되지 않는다" 라고
표현하고 있어, 극단적인 충격에는 떨어질 여지를 남기는 편이 의도에 맞다고 봤다.

### 2.2 진행분이 누적되고 여러 명이 나눠 할 수 있다

용접은 한 번에 끝까지 누르는 동작이 아니다. 중간에 놓아도 채운 만큼 남고
다른 플레이어가 이어서 채울 수 있다. 여러 명이 나눠 용접하는 것이 1장 협동의 한 축이다.

문제는 S15P21B202-61 의 `Interactor` 가 홀드를 취소하면 진행도를 버린다는 점이었다.
그래서 프레임워크에 훅 하나를 추가했다.

```csharp
// IInteractable
void OnHoldProgress(Interactor actor, float progress01);
```

`WeldPoint` 는 이 값을 초 단위로 받아 두고, 홀드가 끝나는 순간 확정한다.
확정 신호는 `Release(actor)` 다. 완료와 취소 양쪽에서 호출되므로 "홀드가 끝났다" 는
신호로 그대로 쓸 수 있다. 이를 위해 `InteractableBase.Release` 를 virtual 로 바꿨다.

진행 중에는 커밋하지 않는다. `HoldSeconds` 가 중간에 바뀌면 `Interactor` 의
진행도 계산이 어긋나기 때문이다.

`HoldSeconds` 는 항상 **남은 시간**이다. 절반 채운 용접점은 절반만 더 누르면 끝난다.

### 2.3 구조는 고정쇠와 거울이다

| | 고정쇠 (S15P21B202-63) | 용접점 (이 티켓) |
| --- | --- | --- |
| 하는 일 | 꾹 눌러 **푼다** | 꾹 눌러 **채운다** |
| 다 하면 | 파츠가 `Locked` → `Free` | 접합이 `Placed` → `Welded` |
| 기반 | `InteractableBase` | `InteractableBase` |
| 완료 후 | 콜라이더를 꺼서 포커스에서 사라진다 | 같음 |

### 2.4 용접점은 붙어 있는 파츠에만 유효하다

`GetAvailability` 가 소유 파츠의 상태가 `Placed` 이고 온전한 `PartAssemblyLink` 가
있는지 확인한다. 아직 들려 있거나 자유 배치된 파츠에는 용접할 것이 없다.

조인트가 끊어지면 모든 용접점의 진행도가 0 으로 돌아간다.
접합이 풀렸으면 용접도 없던 일이 된다. 다시 올려 처음부터 용접해야 한다.

### 2.5 용접 도구 조건은 아직 넣지 않았다

`WeldPoint.RequiresTool` 플래그만 두고 기본값은 꺼져 있다.
플라즈마 용접 건을 들고 있어야 하는 조건은 캐릭터의 도구 시스템(`HeldTool`)에
의존하므로 후속에서 연결한다.

## 3. 작업 중 발견한 버그

**씬에 저장된 용접이 로드 후 사라졌다.** `_weldedSeconds` 가 직렬화되지 않아
파츠 상태는 `Welded` 인데 진행도는 0 으로 보였고, 그 결과 조인트 강도가 미접합 값으로
되돌아가 용접한 탑이 충격에 떨어졌다.

Sandbox Case9 를 씬에 저장해 돌려 보고 드러났다. `[SerializeField]` 를 붙여 고쳤고,
`EditorUtility.CopySerialized` 로 직렬화를 감시하는 테스트를 넣었다.

S15P21B202-66 의 조인트 참조 문제와 같은 계열이다. **런타임에서만 만들어지는 상태를
씬에 저장할 때 직렬화를 빠뜨리는 실수**가 이 시스템에서 반복되고 있다.
앞으로 링크·용접 관련 상태를 추가할 때 먼저 확인할 항목이다.

## 4. 산출물

코드
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/WeldPoint.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartAssemblyLink.cs` (용접 연동)
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/IInteractable.cs` (훅)
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/InteractableBase.cs` (훅, virtual Release)
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/Interactor.cs` (훅 호출)
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Physics/PhysicsScale.cs` (용접 상수)

테스트
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/WeldPointTests.cs`

에셋
- `METAGAME/Assets/_Project/Scenes/Sandbox_Physics.unity` (Case9 추가)

## 5. 검증

EditMode 테스트 116건 전부 통과했다. 이번에 추가한 것은 14건이다.

| 영역 | 테스트 |
| --- | --- |
| 진행도 누적 | 취소해도 채운 만큼 남음, 남은 홀드 시간이 짧아짐 |
| 인계 | 두 번째 플레이어가 이어서 완료 |
| 가용성 | 완료된 용접점은 상호작용 대상 아님, 붙어 있지 않은 파츠는 용접 불가 |
| 직렬화 | 진행도가 직렬화를 견딤 (회귀) |
| 강도 보간 | 미접합값, 절반 용접 시 보간값, 완전 용접 시 완료값 |
| 완료 전환 | Placed → Welded, 완료 이벤트, 용접된 파츠는 다시 못 듦 |
| 초기화 | 조인트 파괴 시 진행도 0 으로 복귀 |
| 경계 | 용접점이 없는 접합은 저절로 완성되지 않음 |

Sandbox Case9 를 저중력에서 검증한 결과다. 실패 0건.

```
Case9 용접점 4개씩 인식                    4 / 4
Case9A 미접합 비율 0                      0.00
Case9B 용접 비율 1 (씬 저장 후에도)          1.00
Case9A 상태 Placed                      Placed
Case9B 상태 Welded                      Welded
Case9A breakForce 가 미접합값             8000N
Case9B breakForce 가 용접값              200000N
Case9B 용접된 파츠는 다시 못 든다
Case9B 완료된 용접점은 상호작용 대상 아님
Case9A 미접합 접합은 충격에 끊어짐
Case9B 용접된 접합은 같은 충격을 버팀
Case7 3단 탑 여전히 조립됨
```

**핵심 증명**: 두 탑에 같은 충격(속도 변화 1m/s, 약 500kg / 0.01s → 약 50,000N)을 주면
미접합(8,000N)만 끊어지고 용접(200,000N)은 버틴다. 운석이 들어올 때의 거동을 미리 확인한 것이다.

왼쪽이 미접합, 오른쪽이 용접 완료 (충격 전)

![용접 비교](S15P21B202-68-welding.png)

Sandbox 전경

![전경](S15P21B202-68-sandbox-all.png)

## 6. 미해결 / 후속

- `UnweldedBreakForce` 실측 확정 — 운석 티켓. 지금 8,000N 은 임시값이다
- 플라즈마 용접 건을 들어야 하는 조건 — `RequiresTool` 훅만 두었다.
  캐릭터 도구 시스템 연동은 후속
- 용접 진행도 게이지 표시 — UI 파트. `WeldPoint.Progress` 와
  `PartAssemblyLink.WeldRatio` 를 읽으면 된다
- 용접 VFX 와 사운드 — Art / Sound 파트
- 접합 하나당 용접점 수는 레벨 디자인에서 정한다. Sandbox 는 파츠 밑면 네 귀퉁이에 4개다
- 우주선 완성 판정 — 모든 접합의 `IsFullyWelded` 를 세면 된다
- 네트워크 동기화 — S15P21B202-15

## 7. 참고

- 네임스페이스는 `Metagame.Gameplay.Parts`
- 용접점 콜라이더는 `Interactable` 레이어에 두어야 한다. 다른 레이어면 탐지되지 않는다
- `WeldPoint.ForceComplete()` 는 레벨 초기 상태 구성·연출·디버그용이다
- 선행: S15P21B202-61, S15P21B202-63, S15P21B202-66
