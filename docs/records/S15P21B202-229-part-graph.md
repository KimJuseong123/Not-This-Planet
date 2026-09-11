# S15P21B202-229 — 탑 판정을 소켓 사슬에서 조인트 그래프로

[문서 안내](../README.md) → [작업 기록](README.md) → 조립·용접·발사

우주선 완성·발사 판정을 소켓 사슬에서 접합 그래프로 변경했습니다. 비드로 연결한 탑을 인식하고 파츠 하나에 여러 접합을 허용한 결과를 검증했습니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

S15P21B202-228 로 비드가 두 몸체를 물리적으로 이었지만 <b>발사는 되지 않았다.</b>
완성 판정이 소켓 사슬만 따라갔기 때문이다. 이 이슈가 그것을 그래프로 바꾼다.

이제 비드로만 이은 탑이 완성 판정을 통과하고 발사된다.

## 탐색을 한 곳으로 모았다

`ShipAssembly`와 `LaunchTest`가 <b>각자</b> `PartSocket.ConnectedTo` 사슬 코드를 갖고
있었다. `ShipAssembly`의 주석이 그 위험을 이미 적어 두었다 — "두 곳이 다른 방법을 쓰면
탑승은 됐는데 시험이 파츠를 못 찾는 어긋남이 생긴다".

`PartGraph` 하나로 옮겼다.

```csharp
PartGraph.Collect(engine, _tower);          // 접합으로 이어진 파츠를 아래에서 위로
PartGraph.CollectLinks(_tower, _links);     // 덩어리 안의 모든 변
```

너비 우선 탐색이고, 방문 표시를 따로 두지 않고 결과 목록을 그대로 쓴다. 파츠 수가 한
자리이므로 `Contains`가 더 싸고, 목록이 곧 방문 집합이라 둘이 어긋날 여지가 없다.

### 정렬은 높이로

그래프에는 "i번째 층" 이 없다. 예전에는 사슬을 올라간 순서가 그대로 층이었다.
높이(`position.y`)로 정렬한다.

동순위 판정은 <b>이름</b>이다. 4인 멀티에서 각자 다른 순서를 얻으면 "누구는 탑승 가능,
누구는 불가" 가 되기 때문이다 — `PartAssembler`가 인스턴스 ID를 쓰지 않는 것과 같은
이유다.

이러면 기획 §7.1 의 조립 순서가 그대로 남는다. 비드는 <b>붙이는 수단</b>이지 순서를
우회하는 길이 아니다. `ABeadTowerInTheWrongOrder_DoesNotBoard`가 그 동작을 검증한다.

### 하중 시험은 모든 변을 본다

소켓 사슬을 쓸 때는 "위에 올라가는 파츠의 접합" 하나씩이면 충분했다. 그래프에서는 옆으로
이은 접합도 하중을 받으므로 빠뜨리면 <b>그 자리가 실제로는 끊어지는데 시험은 통과한다.</b>

덩어리 <b>밖으로</b> 나가는 접합은 세지 않는다. 발사하는 것은 이 덩어리이고, 옆에 세워 둔
다른 파츠에 실수로 이어 둔 접합의 하중까지 볼 이유가 없다.

## 파츠 하나에 접합 여럿을 허용했다

`PartAssemblyLink`의 `DisallowMultipleComponent`를 떼었다. S15P21B202-228 이 남긴
경계다.

<b>뗄 수 있었던 근거</b>는 이 클래스가 원래 갖고 있던 두 경로다.

```
1) OnJointBreak  — 런타임에서 즉시 온다. 조인트와 같은 오브젝트여야 받는다.
2) Tick 의 조인트 생존 확인 — 콜백이 오지 않는 경우의 안전망.
```

`OnJointBreak`가 <b>유일한 경로가 아니다.</b> 조인트가 끊어지면 Unity가 Joint 컴포넌트를
지우므로 `Tick`이 다음 틱에 잡는다. 콜백은 즉시성일 뿐이고 정확성은 안전망이 갖고 있다.
EditMode 검증이 이미 그 경로로 돌아 왔다.

### 쌍마다 하나는 남는다

두 파츠 사이에 접합이 둘이면 강도가 두 배가 되고 하중 시험이 같은 자리를 두 번 센다.
`FindBetween`이 그것을 막고 `OnePair_StillHoldsOnlyOneLink`가 잠근다.

### 등록부가 필요해졌다

접합은 두 파츠 중 <b>한쪽에만</b> 붙어 있다. 파츠에서 `GetComponents` 로는 자기가 가진
접합만 보이고 상대가 가진 접합은 보이지 않는다 — 그러면 그래프의 변 절반을 잃는다.

`PartAssemblyLink.All`을 두었다. `WeldSeam.All` · `PartSocket.All`과 같은 방식이고,
EditMode에서 `OnEnable`이 돌지 않으므로 `Bind`/`BindBeadWeld`가 직접 등록한다.

## "어느 접합?" 을 다섯 곳에서 답했다

`GetComponent<PartAssemblyLink>()`가 모두 모호해졌다. 지라에 적어 둔 다섯 자리다.

| 자리 | 바뀐 것 |
| --- | --- |
| `LaunchTest` | `PartGraph.CollectLinks` — 그래프의 모든 변 |
| `PartAssembler` 재조립 | `SocketLinkOf(moving)` — <b>소켓 접합만</b> 갈아 끼운다 |
| `WeldSeam.CanWork` | `SocketLinkOf(_owner)` — 옆 비드 접합의 `Gap`은 이 접합선과 무관하다 |
| `WeldSeam.TryCut` | `SocketLinkOf(_owner)` — 완전 절단은 소켓 접합만 푼다 |
| `WeldBead.EnsureLink` | "링크가 있으면 포기" 를 지웠다 |

`SeveringTheSeam_KeepsBeadLinksOnOtherPairs`가 네 번째를 잠근다 — 접합선을 잘랐을 뿐인데
옆에 이어 둔 자리가 사라지면 안 된다.

## 검증하며 알게 된 것 — 붙어 있는 파츠는 "재조립" 이 안 된다

`PartAssembler`의 재조립 분기를 검증하려고 조립된 파츠를 그대로 다시 `Assemble` 했는데
`FindMatch`가 후보를 못 찾았다.

```csharp
if (movingSocket.IsOccupied) continue;   // FindMatch
```

밑면 소켓이 이미 맞물린 파츠는 건너뛴다. 즉 <b>먼저 떼어내야 다시 올릴 수 있다</b>.
229 가 바꾼 것이 아니라 원래 그렇다.

따라오는 사실이 하나 더 있다. `Assemble`의 `existing != null → Release()` 분기는
`FindMatch`를 거쳐서는 <b>도달할 수 없다</b> — 살아 있는 소켓 접합은 곧 맞물린 소켓을
뜻하고, 맞물린 소켓은 `FindMatch`에서 걸러진다. 손으로 `SocketMatch`를 만들어 넘기는
경로만 남는다. 그래도 `SocketLinkOf`로 좁혀 둔 것은 유지한다 — 그 분기가 도는 날
비드 접합을 함께 지우지 않아야 한다.

검증은 실제 흐름으로 다시 썼다(`ReassemblingAfterACut_...`): 접합선을 완전 절단해 떼어낸
뒤 다시 올린다. 기획 §16.3 의 "절단한 구간은 … 재조립" 이 그 흐름이다.

### ForceCut 은 해제하지 않는다

`WeldSeam.ForceCut`은 진행분만 채우고 접합 해제 경로를 타지 않는다. 해제는 `TryCut`이
다 잘린 것을 확인할 때 일어난다. 검증에서 한 바퀴 훑는 `SweepCut`을 따로 두었다.

### 검증 리그의 이름이 겹쳤다

`MakePart`가 `PartSpec` 이름을 그대로 쓰므로, 옆에 세운 파츠가 탑의 파츠와 같은 이름이
됐다. 완성 판정의 층 검사와 높이 정렬의 동순위 판정이 <b>둘 다 이름을 본다</b> —
겹치면 결과가 흔들린다. 옆 파츠는 이름을 바꿔 준다.

<a id="228-의-경계-검증은-지우지-않고-뒤집었다"></a>

## 접합 개수 제한 해제에 따른 검증 변경

`WeldBeadJoinTests.APartAlreadyLinked_TakesNoSecondBeadJoint`가
`...TakesASecondBeadJoint`로 바뀌었다.

경계가 사라진 것 자체가 잠글 값이 있는 사실이고, 지워 버리면 나중에 누가 제약을
되살려도 아무것도 알려 주지 않는다.

## 검증

`PartGraphTests` <b>12개</b>, `WeldBeadJoinTests` 14개 — 26개 전부 통과.
EditMode 전체 <b>409개 중 404 통과</b>.

회귀 방어선이 이 이슈의 핵심이었다. 195 · 200 · 201 · 204 가 만든 것이 모두 사슬 위에
서 있었다.

| 스위트 | 결과 |
| --- | --- |
| `ShipBoardingTests` (완성 판정) | 통과 |
| `LaunchTestTests` (하중 시험) | 통과 |
| `TowerCollapseTests` (미접합 붕괴) | 통과 |
| `ShipRideTests` (탑승·사출) | 통과 |
| `ShipAscentTests` (상승·1장 완료) | 통과 |
| `WeldSeamTests` · `WeldCutTests` | 통과 |

<b>수를 맞춰 확인했다.</b> 228 시점 392 통과 + 새로 12 = 404. 늘어난 실패가 없다.

실패 5개는 develop에 이미 있던 것이다(S15P21B202-226 에서 develop만 체크아웃해
확인했다).

| 실패 | 원인 | 몫 |
| --- | --- | --- |
| `PartClampTests.Pull_TakesThePlannedTime` | `4abb9c9`가 `ClampPullSeconds`를 8초→2초로 바꿨다 | [Character] |
| `ClampPullTests.GrabbingAgain_...` | 같은 원인 | [Character] |
| `ClampPullTests.LastClamp_...` | 같은 원인 | [Character] |
| `DownedRescueTests.PlayerKnockedBackByLastClamp_...` | 같은 원인 | [Character] |
| `TractorBeamControllerTests.VehicleDrive_...` | S15P21B202-218 부터 실패 중 | [Character] |

## 인게임 확인은 여전히 못 했다

<b>이 이슈는 특히 인게임 확인이 필요한 조각이다.</b> 완성 판정과 하중 시험은 실제로
쌓아 보고 발사해 봐야 안다. 빌드가 develop에서 깨져 있어 못 했다.

1. `.gitattributes:4`의 `*.asset text`가 바이너리 TerrainData를 손상시킨다
   (`git blob 393,724` → `worktree 393,726`). 한 줄이면 풀리지만 [Infra] 소유라
   승인 대기 중이다.
2. `Lobby.unity`가 공용 URP Volume Profile을 참조하지 않는다 ([Art]·[Level]·[Network]).

실측은 `docs/records/S15P21B202-226-weld-bead.md`에 있다.

<a id="다음"></a>

## 후속 작업

| 항목 | 몫 |
| --- | --- |
| 원형 접합선 제거와 검증 이관 | 4/4 — 마지막 |
| 불꽃과 용접음 | [Art] · [Sound] — `BeadProgressed`가 다리가 걸리는 순간 false가 된다 |
| 실제 비드 메시 | [Art] — `WeldBead.SpawnBlob`의 `Prim.Ball` 자리 |
