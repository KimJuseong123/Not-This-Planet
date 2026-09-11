# S15P21B202-230 — 원형 접합선 제거와 용접 검증 이관

[문서 안내](../README.md) → [작업 기록](README.md) → 조립·용접·발사

비드 용접으로 대체된 원형 접합선을 제거하고 기존 검증을 이관했습니다. 공유 상수·도구 이벤트 정리, 재조립 강도 복구와 기획 반영, 당시 인게임 검증 한계를 남깁니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

비드가 자국·강도·피드백을 모두 갖게 되었으므로(S15P21B202-226 · 228 · 229) 원형
접합선은 <b>같은 일을 하는 두 번째 장치</b>가 되었다. 걷어냈다.

용접기는 이제 비드 하나만 다룬다.

## 지운 것

| 무엇 | 규모 |
| --- | --- |
| `WeldSeam` | 클래스 하나 |
| `WeldSeamTests` · `WeldCutTests` | 검증 35개 |
| `Stage1PuzzleBuilder.AddWeldSeam` | 씬 배치 |
| `PlasmaWeldingTool`의 접합선 경로 | `WeldingSeam` · `CuttingSeam` · 우선순위 분기 |
| `PartAssemblyLink`의 접합선 경로 | `Seam` · `RefreshSeam` · `EnsureSeam` · `_seamCollected` |
| `PhysicsScale` 상수 | `WeldSeamSeconds` · `WeldSeamSegments` · `WeldSeamSecondsPerSegment` · `CutSeamSeconds` · `CutSeamSecondsPerSegment` · `WeldSecondsPerPoint`(Obsolete) |

## 지우지 않은 것 — 여기가 이 이슈의 함정이었다

### WeldSeamRange 는 비드가 쓰고 있었다

`WeldBead`가 사거리를 이 값으로 잰다. 이름만 접합선이고 실제로는 <b>용접기의 값</b>이다
(기획 §16.1 "용접기 최대 사거리는 약 0.8m"). 지웠으면 비드가 컴파일되지 않았다.

`WeldRange`로 개명했다. `Core.Tuning.ToolBeamRange`가 같은 0.80m 이며 같은 값이어야
한다 — 빔이 닿아 보이는데 용접이 안 되면 플레이어가 이유를 알 수 없다.

### 접합 허용 오차 셋은 소켓 조립의 것이다

`WeldCenterTolerance`(0.8m) · `WeldGapTolerance`(0.2m) · `WeldTiltToleranceDegrees`(25도)
는 이름에 Weld가 붙었지만 `PartAssembler.FindMatch`의 <b>소켓 정렬 판정</b>이다.
기획 §15.2 의 값이고 접합선과 무관하다. 남겼다.

### PartAssemblyLink.Gap 도 남겼다

읽는 곳이 <b>없어졌다</b> — 비드는 벌어지는 것을 간격이 아니라 비드가 끊어지는 것으로
표현한다. 그래도 지우지 않았다. 소켓 두 개를 가진 곳이 여기뿐이고 "두 접합면이 얼마나
벌어져 있는가" 는 조립 상태를 진단하는 값이다(QA 도구가 쓸 자리다).

### 문서 참조 두 곳

`PartClamp`와 `DownedRescue`의 주석이 "진행분을 저장하는 이유" 와 "누적하고 넘기는
패턴" 의 <b>선례</b>로 `WeldSeam`을 가리켰다. `WeldBead`로 옮겼다.

<a id="206-의-결정이-값을-했다"></a>

## 완전 용접 상태를 만드는 공통 경로의 재사용

`ForceWeldComplete`를 부르는 스위트가 다섯 개다 — `LaunchTestTests` · `MeteorTests` ·
`ShipAscentTests` · `ShipRideTests` · `TowerCollapseTests`.

그 다섯은 <b>용접을 검증하는 것이 아니다.</b> "완전히 용접된 탑" 이라는 상태를 필요로
할 뿐이다. S15P21B202-206 이 "용접을 끝낸다" 의 표현을 이 메서드 하나로 모아 두었기
때문에, 이번에 <b>메서드의 안만 바뀌고 다섯 스위트는 한 줄도 고치지 않았다.</b>

```csharp
public void ForceWeldComplete() => SetBeadWeldRatio(1f);
```

## 검증 이관 — 살릴 성질을 가렸다

35개를 그냥 버린 것이 아니다.

| 성질 | 어디로 |
| --- | --- |
| 진행분이 누적되고 중간에 놓아도 남는다 | 비드가 이미 만족 — `WeldBeadTests` |
| <b>다른 플레이어가 이어서 채운다</b> | <b>비드에 없었다.</b> `WeldBeadJoinTests`로 옮겼다 |
| 완료 비율만큼 조인트가 튼튼해진다 | `WeldBeadJoinTests` |
| 절단하면 약해지고 다 자르면 분리된다 | `WeldBeadJoinTests` |
| 같은 자리에 계속 대도 진행되지 않는다 | <b>비드는 반대다</b> — 계속 대면 커진다. 규칙이 바뀌었으므로 지웠다 |
| 접합면이 벌어지면 중단된다 (§16.1) | 비드에 그 개념이 없다. 지웠다 |

`AnotherPlayer_ContinuesWhereTheFirstStopped`가 새로 옮겨 온 것이다. 진행분이
<b>도구가 아니라 대상</b>에 있어야 성립하는 성질이고, 비드에서는 파츠에 남은 덩어리가
그것이다.

## 접합선을 지우며 드러난 구멍 하나

접합선이 있을 때 `Release`는 `_seam.ResetProgress()`로 진행분을 되돌렸다. 비드는
파츠에 <b>그대로 남는다</b> — 자국이 남는 것이 기획 §16.2 다.

그래서 조인트가 운석에 끊어졌다가 다시 조립되면, 비드는 눈에 보이는데 새 접합의 강도는
0 에서 시작한다. <b>보이는 것과 실제 강도가 어긋난다.</b>

`WeldBead.RefreshBetween`을 만들어 `PartAssemblyLink.Bind`가 부르게 했다. 접합을 맺을
때 남아 있는 비드를 다시 재어 강도를 되찾는다.

`ReassemblingWithBeadsStillThere_RecoversTheirStrength`가 잠근다.

## 도구의 신호를 하나로 합쳤다

접합선과 비드가 공존하던 동안 신호가 넷이었다 —
`WeldingProgressed` · `CuttingProgressed` · `BeadProgressed` · `BeadShrunk`.

둘로 줄였다. `WeldingProgressed`와 `CuttingProgressed`가 [Art]·[Sound] 가 붙을
유일한 자리다.

`WeldingProgressed`에 성질이 하나 붙어 있다 — <b>다리가 걸리는 순간 false가 된다.</b>
비드가 더 뻗지 않기 때문이고(228), 진행 바가 없는 설계에서 <b>불꽃이 멎는 것</b>이
"이어졌다" 를 알리는 신호가 된다.

## 검증

EditMode 전체 <b>374개 중 369 통과</b>.

409 → 374 는 삭제한 35개(`WeldSeamTests` 20 + `WeldCutTests` 15)와 정확히 맞는다.
다른 스위트에서 더하고 뺀 것은 상쇄된다.

| 회귀 방어선 | 결과 |
| --- | --- |
| `ShipBoardingTests` (완성 판정) | 통과 |
| `LaunchTestTests` (하중 시험) | 통과 |
| `TowerCollapseTests` (미접합 붕괴) | 통과 |
| `ShipRideTests` · `ShipAscentTests` | 통과 |
| `MeteorTests` | 통과 |
| `PartGraphTests` · `WeldBeadTests` · `WeldBeadJoinTests` | 통과 |

실패 5개는 develop에 이미 있던 것이다(S15P21B202-226 에서 develop만 체크아웃해 확인).

| 실패 | 원인 | 몫 |
| --- | --- | --- |
| `PartClampTests.Pull_TakesThePlannedTime` | `4abb9c9`가 `ClampPullSeconds`를 8초→2초로 바꿨다 | [Character] |
| `ClampPullTests.GrabbingAgain_...` · `LastClamp_...` | 같은 원인 | [Character] |
| `DownedRescueTests.PlayerKnockedBackByLastClamp_...` | 같은 원인 | [Character] |
| `TractorBeamControllerTests.VehicleDrive_...` | S15P21B202-218 부터 실패 중 | [Character] |

### QA 도구가 비드 값을 검사한다

`Stage1QaAudit`에서 접합선 검사를 빼고 비드 값을 넣었다. 실행 결과:

```
OK   용접기 사거리 (§16.1 약 0.8m) = 0.8
OK   용접 최단 (§16.1 약 10초) = 9.6
OK   비드 덩어리 상한 (§16.2) = 0.1
OK   완전 강도까지 덩어리 (§16.2) = 3
실패 고정못 1명 (§12.2 약 8초) 인데 구현은 2 이다
실패 고정못 2명 (§12.2 약 4.5초) 인데 구현은 1.12 이다
```

<b>QA 도구가 `4abb9c9`의 어긋남을 독립적으로 잡아낸다.</b> 검증이 실패하는 것과 같은
원인이며 두 경로가 같은 결론에 이른다.

## 기획 문서를 고쳤다

`docs/planning/chapter-1-detailed-design.md` §16 을 비드 방식으로 다시 썼다. §16.4
(조립 순서와 발사 판정)를 새로 두어 229 의 결정을 적었다.

기획 밖의 서술 다섯 곳도 함께 고쳤다(개요 · 도구 표 · 역할 · §15.1 · §15.2).
[Art] 자산 표 두 줄은 <b>판단을 남겨 두었다</b> — "원형 용접 경로" 를 위한 접합 링이
기능상 필수가 아니게 되었지만 남길지는 [Art] 몫이다. VFX 표의 "용접 비드" 는 이제
VFX가 아니라 실제 물체이므로 그렇게 적었다.

<b>기획은 [Plan] 소유다.</b> 내용은 김주성 님이 정한 방향과 제약을 적은 것이지만
승인은 필요하다.

## 인게임 확인은 못 했다 — 그리고 이제 되돌릴 수 없다

<b>접합선을 지웠으므로 비드가 손맛에 안 맞아도 돌아갈 안전망이 없다.</b> 그 확인을
못 한 상태로 지운 것이 이 이슈에서 가장 아쉬운 점이다.

빌드가 develop에서 깨져 있다.

1. `.gitattributes:4`의 `*.asset text`가 바이너리 TerrainData를 손상시킨다
   (`git blob 393,724` → `worktree 393,726`). 한 줄이면 풀리지만 [Infra] 소유라
   승인 대기 중이다. QA 도구도 같은 진단을 낸다 — "TD_Stage1_Arena.asset이
   체크아웃에서 깨졌을 수 있다".
2. `Lobby.unity`가 공용 URP Volume Profile을 참조하지 않는다 ([Art]·[Level]·[Network]).

<a id="비드-스택이-끝났다"></a>

## 비드 용접 구현 단계와 후속 작업

| 이슈 | 무엇 |
| --- | --- |
| 226 | 비드 생성과 성장 |
| 228 | 닿으면 접합, 강도는 접촉 부피 |
| 229 | 탑 판정을 조인트 그래프로 |
| 230 | 원형 접합선 제거 |

남은 것은 다른 파트다.

| 항목 | 몫 |
| --- | --- |
| 불꽃과 용접음 | [Art] · [Sound] — `WeldingProgressed` · `CuttingProgressed` |
| 실제 비드 메시 | [Art] — `WeldBead.SpawnBlob`의 `Prim.Ball` 자리 |
| 접합 링 자산을 남길지 | [Art] |
| 기획 §16 승인 | [Plan] |
| `ClampPullSeconds` 8초/2초 정리 | [Character] |
| `.gitattributes` 한 줄 | [Infra] |
