# S15P21B202-228 — 비드가 우주선 몸체에 닿으면 접합

비드가 다른 우주선 몸체에 닿으면 두 몸체가 하나로 합쳐진다. 정해진 접합면이 아니어도
되고, 이은 비드가 많을수록 튼튼하다. S15P21B202-226 이 비드가 자라는 것까지 만들었고
여기서 <b>쓸모</b>가 생긴다.

## 강도는 접촉 부피다 — 그리고 그 규칙에 구멍이 있었다

강도를 "상대 몸체에 닿아 있는 비드의 부피" 로 정했다. 긴 기둥은 끝만 닿으므로
한 덩어리 몫이고, 사슬이 여럿이면 더해진다. 그래서 한 자리에 몰아 쏘는 것으로는
채울 수 없고 <b>여러 군데를 이어야</b> 한다 — 기획 §16.1 의 "원형 접합부 전체를
따라간다" 를 비드 방식으로 되살린 것이다.

여기까지 만들고 검증을 돌렸더니 <b>기둥 하나로 완전 강도가 나왔다.</b>

원인은 기둥이 상대 몸체에 닿은 뒤에도 계속 뻗었기 때문이다. 덩어리가 상대 안으로
파고들며 쌓이고, 파묻힌 덩어리가 모두 접촉으로 세어졌다. 그러면 "여러 군데를 이어야
튼튼하다" 가 무력해진다.

<b>닿으면 더 뻗지 않게</b> 했다. 끝 덩어리는 계속 굵어지지만 다음 덩어리를 얹지는
않는다. 부수 효과가 하나 있고 그것이 오히려 좋다 — 자란 것이 없으므로 진행됐다고
보고하지 않고, 그래서 <b>다리가 걸린 순간 불꽃이 멎는다</b>. 진행 바가 없는 설계에서
"이어졌다" 를 알리는 유일한 신호가 공짜로 나왔다.

`ALongPillar_IsNotStrongerThanAShortOne` 이 이 규칙을 잠근다.

## PartAssemblyLink 가 소켓을 전제하고 있었다

지라에 적어 둔 위험이 그대로였다. 네 곳이 소켓 두 개를 직접 읽는다.

```
Bind(movingSocket, targetSocket, joint)   ← 소켓 두 개를 받는다
Gap        _movingSocket / _targetSocket 의 위치와 법선으로 잰다
Tick       _movingSocket.Owner 로 파츠를 찾는다
Release    _movingSocket.Disconnect() 를 부른다
```

<b>소켓을 선택 사항으로 만들었다.</b> 비드 전용 링크 타입을 새로 만드는 길도 있었지만,
그러면 `LaunchTest`·`LowGravityField`·`TractorBeam`·`Hand` 가 링크를 두 종류 알아야
한다. "두 파츠를 잇는 조인트" 의 주인은 하나여야 한다.

| 무엇 | 어떻게 |
| --- | --- |
| 파츠를 찾는 법 | 소켓에서 찾지 않고 `_movingPart`·`_targetPart` 로 직접 갖는다 |
| `Gap` | 소켓이 없으면 0. 비드 접합에는 "접합면 간격" 이 없다 — 벌어지는 것은 비드가 끊어지는 것으로 표현된다 |
| `Release` | 소켓이 없으면 `Disconnect` 를 건너뛴다 |
| `WeldRatio` | 접합선 진행분 + 비드 강도를 <b>더한다</b> |

### 왜 더하는가

둘 중 큰 값만 쓰면 두 방법을 섞는 것이 손해가 된다. 접합선을 반쯤 훑고 비드로 보강했을
때 강도가 0.5 그대로면, 플레이어는 한 방법만 쓰게 된다. `SeamProgressAndBeads_AddUp`
이 이것을 잠근다.

### Welded 로 올리는 쪽

소켓 조립은 예전처럼 <b>위에 올라간 파츠 하나만</b> 올린다. 아래 파츠까지 올리면
용접한 탑의 밑단을 다시 들 수 없게 되고, 그것은 §16.3 재조립과 얽힌 별개의 결정이다.
이 이슈에서 바꾸지 않았다.

비드로 이은 접합은 <b>둘 다</b> 올린다. 옆으로 이었으면 어느 쪽이 "위" 인지가 없고,
이제 하나로 움직이므로 한쪽만 들 수 있으면 안 된다.

## 이미 있는 접합은 보강한다

같은 쌍에 접합이 이미 있으면 조인트를 새로 만들지 않고 강도에 더한다. 찾는 것은
`PartAssemblyLink.FindBetween(a, b)` 이고 <b>순서를 가리지 않는다.</b> 소켓 조립은
링크를 위에 올라가는 파츠에 붙이므로, 비드가 아래에서 위로 자란 경우 소유자와 방향이
뒤집힌다 — 그때 같은 쌍을 못 찾으면 두 파츠 사이에 조인트가 둘이 된다.

비드를 다 깎아도 소켓 접합은 풀지 않는다. 비드는 보강이었을 뿐이고, 풀어 버리면
용접하지 않은 탑이 갑자기 분해된다.

## 이 이슈의 경계 — 파츠 하나에 링크는 하나다

`PartAssemblyLink` 가 `DisallowMultipleComponent` 이고 파츠 루트에 붙는다
(`PartAssembler` 가 세운 규칙이다). 그래서 <b>이미 다른 쌍과 이어진 파츠에서는 비드가
새 접합을 만들지 못한다.</b> 비드는 닿아 있지만 접합이 생기지 않는다.

버그가 아니라 경계다. `APartAlreadyLinked_TakesNoSecondBeadJoint` 가 이 경계를
<b>기록</b>한다 — 3/4 에서 링크를 여럿 허용하면 이 검증이 먼저 깨지고, 그것이 신호다.

푸는 것은 3/4(탑 판정을 조인트 그래프로)의 일이다. 링크를 여러 개 허용하는 순간
`LaunchTest`·`LowGravityField`·`TractorBeam`·`Hand` 의
`GetComponent<PartAssemblyLink>()` 가 모두 "어느 링크?" 를 묻게 되고, 그 답이 곧
그래프 탐색이다.

## 그래서 아직 발사할 수 없다

`ShipAssembly.CollectTower` 가 소켓 사슬을 따라가므로 비드로 붙인 파츠는 완성 판정에
들어가지 않는다. <b>물리적으로는 하나로 움직이지만 발사는 되지 않는다.</b> 이것도
경계이며 3/4 가 푼다.

## 검증하며 두 번 걸린 함정 — 좁은 틈에서 씨앗이 어느 쪽에 내리는가

비드는 노즐에서 <b>가장 가까운</b> 몸체에 씨앗을 내린다. 두 파츠 사이 좁은 틈을
겨누면 어느 쪽이 가까운지가 갈린다.

* 간격 0.3m 인 쌍에서 노즐을 아래 파츠 윗면에서 0.2m 띄웠더니 <b>위 파츠 밑면이
  더 가까워</b>(0.1m) 기둥이 반대로 자랐다. 노즐 여유를 0.08m 로 줄였다.
* 접합선을 옆에서 겨누는 검증에서는 두 표면까지의 거리가 <b>정확히 동점</b>이 나서
  결과가 흔들렸다. 틈 가운데가 아니라 아래 파츠 윗면보다 살짝 위를 겨누게 했다.

게임에서도 같은 성질이 남는다. 좁은 틈을 겨누면 어느 쪽 몸체에서 기둥이 자랄지
플레이어가 고르기 어렵다. 다만 <b>어느 쪽에서 자라도 두 몸체는 이어지므로</b> 결과는
같다 — 지금은 문제로 두지 않고 기록만 남긴다.

## 검증

`WeldBeadJoinTests` <b>14개 전부 통과</b>. EditMode 전체 397개 중 391 통과.

`PartAssemblyLink` 를 고쳤으므로 기존 검증 전체가 방어선이다. 226 시점 377 + 새로 14 =
391 로 <b>기존 검증에서 늘어난 실패가 없다.</b>

실패 5개는 develop 에 이미 있던 것이다(S15P21B202-226 에서 develop 만 체크아웃해
확인했다).

| 실패 | 원인 | 몫 |
| --- | --- | --- |
| `PartClampTests.Pull_TakesThePlannedTime` | `4abb9c9` 가 `ClampPullSeconds` 를 8초→2초로 바꿨다 | [Character] |
| `ClampPullTests.GrabbingAgain_...` | 같은 원인 | [Character] |
| `ClampPullTests.LastClamp_...` | 같은 원인 | [Character] |
| `DownedRescueTests.PlayerKnockedBackByLastClamp_...` | 같은 원인 | [Character] |
| `TractorBeamControllerTests.VehicleDrive_...` | S15P21B202-218 부터 실패 중 | [Character] |

### 한 번은 내 배치 실행이 만든 실패였다

`Stage1TabletViewTests.ProductionPrefab_...` 이 처음 돌렸을 때 실패했다.
`PF_Stage1_Tablet.prefab` 이 <b>사라져 있었다.</b>

원인은 내가 226 에서 돌린 빌드다. `SpaceBuild.Build` 가 검증 전에
`ArtLink.BuildPrefabs()` 를 부르고, 그 안의 `CleanStale()` 이 `RabbitKey`·`CapsuleKey`
말고는 `Assets/_Project/Prefabs/Resources` 의 모든 것을 `AssetDatabase.DeleteAsset`
한다. 추적 중인 태블릿 프리팹이 거기 있다.

`git checkout --` 로 되돌리고 다시 돌려 2개 통과를 확인했다. 코드 변경이 아니라
<b>작업 트리 상태</b>였다.

이것은 배치 모드로 빌드를 돌릴 때마다 반복되는 문제다 — 커밋 전에 작업 트리를
확인해야 한다.

## 인게임 확인은 여전히 못 했다

빌드가 develop 에서 깨져 있다. 두 원인 모두 그대로다.

1. `.gitattributes:4` 의 `*.asset text` 가 바이너리 TerrainData 를 손상시킨다
   (`git blob 393,724` → `worktree 393,726`). 한 줄이면 풀리지만 [Infra] 소유라
   승인 대기 중이다.
2. `Lobby.unity` 가 공용 URP Volume Profile 을 참조하지 않는다 ([Art]·[Level]·[Network]).

자세한 실측은 `docs/records/S15P21B202-226-weld-bead.md` 에 있다.

## 다음

| 항목 | 몫 |
| --- | --- |
| 탑 판정을 소켓 사슬에서 조인트 그래프로 | 3/4 — 파츠당 링크 하나 제약과 완성·발사 판정을 함께 푼다 |
| 원형 접합선 제거와 검증 이관 | 4/4 |
| 불꽃과 용접음 | [Art] · [Sound] — `BeadProgressed` 가 다리가 걸리는 순간 false 가 된다 |
| 실제 비드 메시 | [Art] — `WeldBead.SpawnBlob` 의 `Prim.Ball` 자리 |
