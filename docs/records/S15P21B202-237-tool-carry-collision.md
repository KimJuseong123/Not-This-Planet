# S15P21B202-237 — 방망이를 들고 아래를 보면 플레이어가 튀는 것 수정

[문서 안내](../README.md) → [작업 기록](README.md) → 도구·캐릭터

들고 있는 방망이의 충돌로 플레이어가 튀어 오르던 문제를 수정했습니다. 휴대 중 충돌 처리의 공통화와 방망이 콜라이더 치수 조정을 설명합니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

방망이를 든 상태로 바닥을 보면 플레이어가 공중으로 빠르게 날아갔다. 클릭도 필요 없이
시선을 내리는 것만으로 발생했다.

## 원인 — 용접기에는 있는 처리가 방망이에 없었다

`PlasmaWeldingTool.OnEquipped`에 이 코드가 있었고, <b>주석이 증상을 그대로 적어
놨다.</b>

```csharp
// 1인칭으로 들고 아래를 볼 때 키네마틱 도구 콜라이더가 지면과 겹쳐
// 플레이어를 위로 밀어내지 않도록 휴대 중에만 충돌을 끈다.
bodyCollisionState = Body.detectCollisions;
Body.detectCollisions = false;
```

`MeteorBat.OnEquipped`은 자세만 잡고 이 처리를 하지 않았다.
<b>S15P21B202-233 에서 용접기를 참고해 쓰면서 이 줄을 빠뜨렸다.</b>

### 기제

| 단계 | 값 |
| --- | --- |
| 방망이 질량 | 3kg |
| 손의 키네마틱 전환 문턱 (`Tuning.FixedGripMass`) | 40kg |
| 3kg < 40kg 이므로 | 휴대 중 <b>키네마틱</b>이 된다 (`Hand`:112) |
| 캐릭터 키 | 1.33m |

키네마틱 콜라이더는 밀려나지 않는다. 그래서 아래를 볼 때 도구가 지면을 파고들면 PhysX는
그 겹침을 풀기 위해 <b>비키네마틱인 쪽 — 플레이어 —</b> 를 밀어낸다. 겹침이 깊을수록
밀어내는 속도가 커져 공중으로 튄다.

`PlayerRig.SyncHeldObjectOwnerCollision`이 매 틱 돌지만 하는 일은
`IgnoreHeldObjectFromOwner` — <b>소유자와 도구 사이</b> 충돌만 무시한다.
<b>도구와 지면</b> 충돌은 그대로 남고, 실제로 밀어내는 접촉이 그쪽이다.

## 고친 방법 — 규칙을 ToolBase 로 올렸다

세 줄을 방망이에 복사하는 것으로도 버그는 풀린다. 그렇게 하지 않았다.

<b>"휴대 중에는 도구가 다른 것과 부딪히지 않는다" 는 도구 전체의 규칙이다.</b> 파생
클래스가 기억해야 할 일로 두면 세 번째 도구에서 또 빠뜨린다 — 이미 한 번 빠뜨렸다.

`ToolBase.Equip`/`Unequip`이 직접 한다.

```csharp
public void Equip(PlayerRig owner, Rigidbody body)
{
    ...
    IsEquipped = true;
    SuspendCollisions();     // OnEquipped 보다 먼저 — 파생 훅이 꺼진 상태를 본다
    ResolveMuzzle();
    OnEquipped();
}

public void Unequip()
{
    OnUnequipped();
    RestoreCollisions();     // Body = null 보다 먼저
    ...
}
```

용접기의 중복 코드와 필드 두 개(`bodyCollisionState`, `bodyCollisionStateCaptured`)를
지웠다. <b>동작은 같다</b> — 껐다 되돌리는 시점이 예전과 동일하다.

### 두 번 장착되어도 처음 값을 지킨다

`Equip`이 이미 장착된 상태에서 불리면 안에서 `Unequip`을 먼저 돈다. 그 경로에서
이미 꺼진 `false`를 "원래 값" 으로 기억해 버리면 <b>내려놓아도 충돌이 돌아오지
않는다.</b> `collisionsSuspended` 플래그로 막았고
`EquippingTwice_KeepsTheOriginalValue`가 잠근다.

### 원래 꺼져 있었으면 꺼진 채로 돌린다

무조건 켜면 일부러 끄고 둔 물체가 도구를 한 번 잡았다 놓는 것으로 되살아난다.
`AToolThatStartedWithCollisionsOff_StaysOff`가 그 경우를 본다.

## 콜라이더 치수도 틀려 있었다

S15P21B202-233 의 배치 도구가 이 값을 넣었다.

```csharp
collider.size = new Vector3(0.34f, 1.12f, 0.34f);   // 세로로 긴 막대라고 가정
```

<b>자산 목록의 숫자만 보고 넣은 값이다.</b> 목록의 실제 크기는 `1.12 × 1.20 × 1.08m`
(W × H × D)이고, 그중 1.12 를 <b>세로</b>로 착각했다. 실제 모델은 거의 정육면체에
가까운 SF 방망이다.

이제 <b>렌더러 경계에서 재서</b> 만든다. 실행 결과:

```
[Stage1MeteorBat] 콜라이더를 메시 경계에서 만들었다 — 크기 (0.95, 1.02, 0.92) · 중심 (0.00, 0.60, 0.00)
```

가로·세로 둘 다 틀렸던 것이 확인된다 — X·Z가 0.34 에서 0.95·0.92 로, 즉 <b>세 배</b>
넓다.

세 가지를 지켰다.

* <b>로컬 공간</b>으로 모은다. 월드 경계를 그대로 쓰면 프리팹이 놓인 자리에 따라
  콜라이더가 달라진다.
* 여덟 꼭짓점을 모두 넣는다. 회전이 걸린 자식도 감싸야 한다.
* 0.85 로 줄인다. 경계 상자는 메시를 <b>완전히</b> 감싸므로 그대로 쓰면 눈에 보이는
  것보다 넓게 부딪혀, 바닥에 놓은 방망이가 떠 있는 것처럼 보인다.

### 코드가 만든 콜라이더는 다시 계산한다

예전 규칙은 "없을 때만 보탠다" 였다. 그래서 233 의 잘못된 치수가 프리팹에 <b>커밋된
채</b> 남아 도구를 다시 돌려도 교체되지 않았다.

지금은 이렇게 가른다.

| 상황 | 처리 |
| --- | --- |
| 자식에 콜라이더가 있다 | <b>손대지 않는다</b> — [Art] 가 실제 형태를 아는 쪽이다 |
| 루트에 BoxCollider가 있다 | 코드가 만든 것이므로 지우고 다시 잰다 |
| 아무것도 없다 | 새로 잰다 |

## 검증

`ToolCarryCollisionTests` <b>7개 전부 통과</b>. EditMode 전체 <b>431개 중 418 통과</b>.

<b>도구 전체의 규칙이므로 도구 전체를 잠갔다</b> — 방망이와 용접기 둘 다 같은 검증을
받는다. 새 도구가 생겨도 `ToolBase`가 하므로 저절로 지켜진다.

| 검증 | 무엇 |
| --- | --- |
| `EquippingABat_TurnsOffItsCollisions` | 방망이 |
| `EquippingTheWeldingGun_TurnsOffItsCollisions` | 용접기 — 옮긴 뒤에도 그대로인지 |
| `UnequippingABat_RestoresItsCollisions` | 복원 |
| `UnequippingTheWeldingGun_RestoresItsCollisions` | 복원 |
| `AToolThatStartedWithCollisionsOff_StaysOff` | 원래 꺼져 있던 경우 |
| `UnequippingSomethingNeverEquipped_ChangesNothing` | 장착 없이 해제 |
| `EquippingTwice_KeepsTheOriginalValue` | 재장착 |

실패 13개는 develop에 이미 있던 것이다. S15P21B202-232 가 머지되며 5개 → 13개로
늘었고, `origin/develop`만 체크아웃해 13개 전부 거기서 같이 실패하는 것을 확인했다.

| 실패 | 원인 |
| --- | --- |
| `ClampPullTests` 7개 · `DownedRescueTests` 2개 | 232 가 `PlayerRig.ClampGrabbed`를 플래그에서 <b>실제 손 상태</b> 검사로 바꿨고, 검증 리그를 맞추지 않았다 |
| `TractorBeamControllerTests` 2개 | 232 가 <b>직접 추가한</b> 검증이 실패 중이다 |
| `PartClampTests.Pull_TakesThePlannedTime` | `4abb9c9`의 `ClampPullSeconds` 8초→2초 |
| `TractorBeamControllerTests.VehicleDrive_...` | S15P21B202-218 부터 |

이 13개를 정리하는 것은 별도 이슈다 — 고정못·상호작용이므로 내 파트다.

## 인게임 확인은 못 했다

<b>이 이슈는 인게임에서 확인해야 끝이 나는 종류다</b> — 바닥을 보아도 뜨지 않는지는
실제로 들고 봐야 안다. 빌드가 develop에서 멈춘다.

1. `.gitattributes:4`의 `*.asset text`가 바이너리 TerrainData를 손상시킨다
   (`git blob 393,724` → `worktree 393,726`). `S15P21B202-215-terrain-data-repair`
   브랜치가 있지만 develop에 들어오지 않았다.
2. `Lobby.unity`가 공용 URP Volume Profile을 참조하지 않는다.
   <b>지형을 살려도 이것이 남아 빌드가 멈춘다</b> — 실측했다.

EditMode로 확인한 항목은 "충돌이 꺼지고 복원된다" 는 <b>규약</b>이다. "튀지 않는다" 는
결과는 인게임에서 봐야 한다.

## 남은 것 (방망이 관련)

| 항목 | 몫 |
| --- | --- |
| 운석 낙하 예고 지면 표시 | B — `Pending`을 읽는 곳이 분위기 연출 하나뿐이다 |
| 휘두르기 동작과 타격 피드백 | C — `Swung`에 구독자가 0개, 팔이 매 틱 운반 자세로 고정된다 |
| 소품·플레이어 타격 | D — 소품과 플레이어만, 파츠·용접부는 §18.3 대로 제외 |
