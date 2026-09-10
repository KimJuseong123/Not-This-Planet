# S15P21B202-400 — 파츠 밀기 3인칭 카메라

> 2026-09-10 · 김주성(물리·퍼즐·캐릭터·상호작용·QA) · 브랜치 `S15P21B202-400-character`
>
> 부모: `S15P21B202-394`(협동 밀기가 마찰을 못 이기는 문제) — MR !304 위에 쌓았다
>
> 기획 §10.2 파츠 밀기 카메라 · §13 파츠 수동 밀기

---

## 1. 무엇이 없었나

기획 §10.2 의 **다섯 항목이 전부 미구현**이었다. 저장소에 `PushCam` 류가 한 곳도
없었다.

그리고 `-394` 가 노출한 값을 **아무도 읽지 않았다.**

| 값 | 뜻 | `-394` 시점 런타임 사용처 |
| --- | --- | --- |
| `PartPush.IsMoving` | 셋 이상이 모여 파츠가 실제로 가속되는가 | **0곳** |
| `PartPush.IsHauling` | 누군가 밀려고 하는가(실제 이동과 무관) | **0곳** |
| `PartPush.HaulDirection` | 합산 밀기 방향 | **0곳** |
| `PartPush.MinPeopleToMove` | 3 | **0곳** |

QA 기록의 유형 A(만들었는데 아무도 안 씀)가 되기 직전이었다 —
`MeteorWarningView` · `ShipAssembly.Blocker` · `LaunchTest.FailureReason` 에 이어서다.

---

## 2. 무엇을 만들었나

### 2-1. 전환 조건은 `IsMoving` 이다 — `IsHauling` 이 아니다

**이것이 이 이슈에서 가장 중요한 한 줄이다.** 한두 명이 붙어 파츠가 꿈쩍하지 않을
때 3인칭으로 빠지면 "시점은 바뀌었는데 아무 일도 안 일어나는" 화면이 된다.
`-394` 가 두 값을 가른 이유가 정확히 이것이다.

연출 범위도 그 결정에 맞춰 정해져 있다(2026-09-10 기획 판단) — **셋 이상이 모여
실제로 밀릴 때만** 연출을 만들고, 한두 명이 버티는 상태에는 아무 표시도 넣지 않는다.

### 2-2. 0.5초 복귀를 카메라가 세지 않는다

§10.2 "밀기를 멈추거나 접촉이 끊기면 0.5초 뒤 1인칭으로 복귀한다".

`PartPush.Gather` 가 이미 `PhysicsScale.PartPushReleaseSeconds` 동안 인원을
유지하므로, 걸음을 멈춰도 `IsMoving` 이 0.5초간 참이다. **타이머를 하나 더 두면 두
값이 어긋난다.** 그 유예가 실제로 그 값인지 검증이 잰다
(`TheHalfSecondGraceComesFromPartPush_NotTheCamera`).

### 2-3. 카메라 중심은 사람과 접촉 지점 사이

§10.2 그대로다. 접촉 지점을 **밖에서 다시 재지 않는다** — `PartPush` 가 접촉 판정
(`IsTouching`)에 쓰는 것과 같은 계산을 `GraspPointFor` 로 꺼냈다. 밖에서 재면 두
계산이 어긋나 카메라 초점과 밀기 판정이 다른 곳을 가리킨다.

`PartPushCamFocusBias = 0.35` 로 **사람 쪽에 더 둔다.** 정확히 가운데(0.5)면 파츠가
화면 가운데를 차지해 미는 사람이 보이지 않는다.

### 2-4. 캐릭터가 밀기 방향을 유지한다 — 시작 순간에 한 번 고정

§10.2 "마우스로 제한적인 자유 시점이 가능하되 캐릭터는 **기존** 밀기 방향을
유지한다".

**여기에 순환이 숨어 있었다.** `Locomotion` 이 카메라 기준으로 `MoveWish` 를 만들고,
`PartPush` 가 그 `MoveWish` 들의 합으로 `HaulDirection` 을 만든다. 그래서 이동 기준을
매 틱 `HaulDirection` 으로 따라가게 하면 서로를 물어본다 — **A 만 눌러도 기준이
계속 왼쪽으로 돌아 제자리에서 빙빙 도는 조작이 된다.**

그래서 `haulBasisYaw` 를 **밀기 시작 순간에 한 번** 정하고 밀기가 끝날 때까지 붙든다.
§10.2 의 "기존" 이 바로 그 고정을 뜻한다고 읽었다.

좌우 자유 시점은 그 기준 ±`PartPushCamYawRangeDegrees`(55도)로 묶는다. 제한이 없으면
카메라를 뒤로 돌린 채 밀게 되어 무엇을 어디로 밀는지 화면에서 사라진다.

### 2-5. 벽 끼임 — 이미 있었다

§10.2 "벽과 파츠 사이에 카메라가 끼면 자동으로 가까워진다".

`OrbitCamera.LateUpdate` 의 `SphereCastNonAlloc` 이 3인칭이면 항상 그 일을 한다.
**새로 만들지 않았다.** 자기 몸만 제외하고 나머지는 다 막는 것으로 보므로 파츠·벽
모두 걸린다.

### 2-6. 참가자 화면까지 전환된다

**이것이 이 이슈의 절반이다.** 2026-09-10 부터 모든 기능이 멀티를 전제한다.

`PartPush` 는 서버에서만 돈다 — 참가자 화면의 파츠는 kinematic 이고
`CanPush` 가 false 라 `Tick` 이 첫 분기에서 모든 값을 0 으로 지운다. 즉 **참가자는
`IsMoving` 을 스스로 알 수 없다.** 배선하지 않으면 방장만 3인칭이 되고 참가자는
1인칭에 갇혀, 셋이 모여 미는 장면이 참가자 화면에서는 그냥 걷는 화면이 된다.

**파츠를 짝지어 보내지 않았다.** 카메라가 필요한 것은 파츠가 무엇인지가 아니라
**어느 지점을 어느 쪽으로** 밀고 있는가뿐이다. 그래서 사람당 세 값으로 끝나고
`WorldSync` 의 번호 짝짓기(`-352` 의 판단)를 건드리지 않는다.

```csharp
[SyncVar] public bool hauling;
[SyncVar] public Vector3 haulDirection;
[SyncVar] public Vector3 haulGrasp;
```

**SyncVar hook 을 쓰지 않는다.** hook 은 그 변수가 바뀔 때만 불리는데 방향·접촉
지점은 매 틱 조금씩 움직인다. `hauling` 하나에만 hook 을 걸면 켜지는 순간의 값으로
굳어 카메라 초점이 파츠를 따라가지 않는다. 그래서 `NetPlayer.LateUpdate` 가 매
프레임 `body.SetHaulState(...)` 를 부른다.

두 경로가 만나는 자리는 `PlayerRig` 하나다.

| 어디 | 어떻게 채우나 |
| --- | --- |
| 방장 · 혼자 실행 | `PlayerRig.Tick` → `RefreshHaulState` → `PartPush.MovingPartFor(this)` |
| 참가자 | `NetPlayer.LateUpdate` → `PlayerRig.SetHaulState(...)` |

`RefreshHaulState` 는 `WorldEvents.RemoteDecides` 면 **아무 일도 하지 않는다.**
그러지 않으면 참가자 화면에서 로컬 조회가 서버 값을 덮어 늘 "밀고 있지 않다" 가
된다.

동기화 정책은 `docs/network-sync-policy.md` §3 표에 **P3 · 📐(3인 실측 전)** 로
등록했다. 카메라 자체는 P4 로 남는다 — 전환 *조건*만 받고 카메라는 각자 화면에서 돈다.

---

## 3. 검증

**`PartPushCameraTests` 22개.** 카메라는 `LateUpdate` 표현이라 화면을 볼 수 없으므로
`DownedCameraTests` 와 같은 방식으로 **상태와 전환을** 잠근다.

| 검증 | 무엇을 잠그나 |
| --- | --- |
| `HaulingStarts_TheCameraPullsBackToThirdPerson` | 셋이 밀면 3인칭 · 거리 5.6 |
| `BracingWithoutMoving_LeavesTheCameraAlone` | **한두 명일 때는 1인칭에 머문다** |
| `HaulingWithNoDirection_DoesNotSwitch` | 방향이 상쇄되면 전환하지 않는다 |
| `HaulingEnds_ItGoesBackToWhatItWas` | 거리·1인칭 여부를 되돌린다 |
| `TheHalfSecondGraceComesFromPartPush_NotTheCamera` | 0.5초 유예가 `PartPush` 에서 온다 |
| `TheFocusMovesTowardTheGraspPoint_ButStaysNearerThePlayer` | 초점이 접촉 지점 쪽 · 그래도 사람 쪽이 가깝다 |
| `TurningTheCamera_DoesNotTurnTheHaulBasis` | **카메라를 돌려도 이동 기준은 그대로** |
| `TheHaulBasisIsFixedAtTheStart_NotFollowedEveryTick` | 순환을 막은 고정 |
| `StartingToHaulAgain_PicksUpTheNewDirection` | 다시 시작하면 새 방향 |
| `TheFreeLookIsClampedAroundTheHaulDirection` | ±55도 한계 |
| `GoingDownWhileHauling_RestoresTheWalkingCamera` | 저장 슬롯 충돌 방지 |
| `MountingWhileHauling_RestoresTheWalkingCamera` | 같음 |
| `WhileDowned_ThePushViewDoesNotTakeOver` | 기절 시야가 이긴다 |
| `WhileMounted_ThePushViewDoesNotTakeOver` | 탑승 시야가 이긴다 |
| `AParticipantSwitchesFromTheServerState` | **서버 값만으로 전환된다** |
| `OnAParticipant_TheLocalTickDoesNotOverwriteTheServerState` | 로컬 조회가 덮지 않는다 |
| `PlayerRigTick_ReadsThePartPushState` | **배선 — `-394` 의 값이 실제로 읽힌다** |
| `SomeoneElsesHauling_DoesNotMoveMyCamera` | 남의 밀기는 내 카메라를 안 건드린다 |
| `MovingPartFor_FindsOnlyThePartIAmActuallyMoving` | 참여 판정 |
| `MovingPartFor_IgnoresAPartThatIsOnlyBracedByTwo` | 둘이면 찾지 않는다 |
| `TheGraspPointIsOnThePartSurface` | 접촉 지점이 표면이다(중심 아님) |
| `WalkingAround_ThereIsNoPushView` | 기본 상태 |

### 3-1. 대조 실험 — 그리고 첫 번째 대조가 실패했다

| 대조 | 결과 |
| --- | --- |
| 전환 조건을 `Hauling` 대신 `true` 로 | **처음: 22개 전부 통과** ❌ → 고친 뒤: 3개 실패 ⭕ |
| 이동 기준을 매 틱 밀기 방향으로 따라가게 | 1개 실패 — `TheHaulBasisIsFixedAtTheStart…` ⭕ |
| 참가자 화면에서 로컬 조회가 서버 값을 덮게 | 1개 실패 — `OnAParticipant…` ⭕ |

**첫 번째 대조가 아무것도 잡지 못했다.** 전환 로직을 `GameSession` 안에 두었는데
검증 하네스가 같은 두 줄을 흉내내는 꼴이 되어, **실제 배선이 검증 경로 밖**이었다.

```csharp
// 검증 하네스 — GameSession 을 흉내낸다. 배선이 틀려도 통과한다.
player.Cam.SetPushView(player.Hauling, player.HaulDirection);
player.Cam.TickPushView(player.HaulGrasp, player.HaulDirection, Step);
```

`PlayerRig.SyncPushCamera(deltaTime)` 로 옮기고 `GameSession` 은 그 한 줄만 부르게
했다. 하네스도 같은 함수를 부른다. 그러자 대조가 3개를 잡았고 그중
`BracingWithoutMoving_LeavesTheCameraAlone` 이 핵심으로 걸렸다.

> **`-394` 에서 두 번 겪은 것을 여기서 또 겪었다.** 그때는 물리를 돌리지 않아서,
> 그 다음엔 콜라이더를 루트에 붙여서, 이번엔 배선을 하네스가 흉내내서 통과했다.
> **세 번 다 "검증이 자기가 만든 좁은 세계 안에서만 맞다" 였다.**
>
> 그래서 규칙을 하나 더 얻었다 — **전환·판정 로직을 소유자(`GameSession`) 안에
> 두지 않는다.** 검증이 부를 수 있는 자리(`PlayerRig`)에 두고 소유자는 한 줄만
> 부른다. 그러면 대조 실험이 실제 경로를 지난다.

### 3-2. 기준선 대조

| | 검증 수 | 실패 |
| --- | --- | --- |
| `-394` 기준선 | 1043 | 32 |
| 이 브랜치 | **1065** (+22) | **32** |

**새로 생긴 실패 0개.** 실패 32개의 이름까지 동일하다.

---

## 4. 남은 것

### 4-1. 3인 접속 실측이 0회다

전환 조건이 셋이므로 **검증만으로는 닫을 수 없다.** 두 가지를 봐야 한다.

1. 참가자 화면에서 3인칭 전환이 실제로 걸리는가 (`hauling` SyncVar 가 도착하는가)
2. 카메라 초점이 파츠를 따라가는가 (`haulGrasp` 가 매 프레임 갱신되는가)

`docs/multiplayer-test-guide.md` 의 절차가 필요하고, **서로 다른 공인 IP** 여야 한다.
2인으로도 "참가자에게 상태가 가는가" 는 볼 수 있지만 `IsMoving` 이 참이 되려면
셋이 붙어야 한다.

### 4-2. 상수 넷이 플레이 확인 전이다

| 상수 | 값 | 무엇을 정하나 |
| --- | --- | --- |
| `PartPushCamDistance` | 5.6 | 걸을 때(3.6)보다 멀다 — 파츠가 폭 2.3m 라서 |
| `PartPushCamPitchDegrees` | 14 | 기절 3인칭(25)보다 완만하다 |
| `PartPushCamFocusBias` | 0.35 | 초점을 접촉 지점 쪽으로 옮기는 비율 |
| `PartPushCamYawRangeDegrees` | 55 | 좌우 자유 시점 한계 |

전부 **계산이 아니라 감각**의 값이다. 셋이 파츠를 미는 장면을 실제로 보고 조정해야
한다.

### 4-3. 이동 기준 고정이 조작으로 어떻게 느껴지는지 모른다

`haulBasisYaw` 를 시작 순간에 고정한 것은 §10.2 의 "기존" 을 그렇게 읽은 것이고
순환을 피하는 유일한 방법이었다. 다만 **밀면서 방향을 크게 트는 조작**이 어떻게
느껴지는지는 플레이로만 안다. 지금은 밀기를 놓고 다시 잡아야 기준이 새로 정해진다.

기준을 천천히 따라가게 하는 방법(느린 보간)도 있지만, 그러면 §2-4 의 순환이 약한
형태로 돌아온다 — 회전 속도가 느려질 뿐 제자리에서 도는 것은 그대로다. 감각을
보고 판단해야 한다.

### 4-4. `IsHauling` 은 아직 런타임 사용처가 0곳이다

이 이슈가 `IsMoving`·`HaulDirection`·`MinPeopleToMove`·`GraspPointFor` 를 배선했다.
`IsHauling`(밀려고 하지만 움직이지 않는 상태를 포함)은 여전히 검증만 읽는다.

한두 명 연출을 만들지 않기로 했으므로 **당장 쓸 곳이 없는 것이 맞다.** 다만
`-394` 에서 `Bracing` 을 같은 이유로 지웠으니, `IsHauling` 도 끝까지 쓰이지 않으면
지우는 쪽이 일관된다. HUD 에 "○/3 명" 을 넣기로 하면 그때 쓰인다.
