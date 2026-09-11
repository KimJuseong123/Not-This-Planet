# S15P21B202-448 — 살리기 표시·게이지가 참가자에게 안 보인다

> 2026-09-11 · 김주성(물리·퍼즐·캐릭터·상호작용·QA) · 브랜치 `S15P21B202-448-network`
>
> 기획 §20.3 기절과 구조 · §20.5 전원 기절 · `docs/network-sync-policy.md`

---

## 1. 무엇이 문제였나

2026-09-11 4인 QA 신고 두 건.

1. "살리기 G 게이지 안 보임 (호스트만 됨)"
2. "살리기 위한 G 클릭 자체도 안 뜸"

**뿌리가 다른 두 결함이다.** 하나는 버그이고 하나는 만든 적이 없는 기능이다.

`-390` 이 배선을 만들고 `-433` 이 진단을 넣었는데, 둘 다 이 증상을 끝내지 못했다.
`-433` 의 주석이 남긴 질문이 정확히 이것이다.

> 그것이 ① 구조 지점이 아예 없어서인지 ② 지점은 있는데 인터랙터가 후보로 잡지
> 못해서인지 ③ 표시가 없어 사람이 누르지 않아서인지 **구별할 근거가 로그에 없었다.**

**②다.**

---

## 2. 실측 — 호스트와 참가자를 같은 방법으로 쟀다

프로브를 임시로 만들어 두 경로를 각각 쟀다(조사용이라 커밋하지 않았다).

```
호스트  — 구조지점존재=True 활성=True                        질의에걸린수=1 구조지점찾음=True
호스트  — 인터랙터상태=Focused 후보=프로브
참가자  — 구조지점존재=True 활성=True 지점레이어=Interactable
          골반detectCollisions=False                         질의에걸린수=0 구조지점찾음=False 후보=없음
대조    — detectCollisions 끈채로=False · 켠뒤=True
```

**마지막 줄이 이 조사의 전부다.** 참가자 상태를 그대로 두고 `detectCollisions`
**하나만** 되돌리면 즉시 찾힌다. 단일 변수 대조다.

### 2-1. 코드를 읽어서는 찾을 수 없는 종류였다

구조 지점은 **존재하고, 활성이고, 레이어도 `Interactable` 로 맞다.** 전부 정상으로
보인다. 갈라놓는 것은 **다른 컴포넌트가 부모 리지드바디에 건 스위치 하나**다.

| 단계 | 무엇 |
| --- | --- |
| 구조 지점 | `Doll.Hips` 의 **자식** GameObject · `Interactable` 레이어 · `SphereCollider(isTrigger)` |
| 후보 탐색 | `Interactor` 가 `Physics.OverlapSphereNonAlloc(..., InteractableMask, QueryTriggerInteraction.Collide)` |
| 참가자 기계 | `RemoteAvatar` 가 남의 몸에 `b.detectCollisions = false` |

**자식 콜라이더는 자기 `Rigidbody` 가 없으면 가장 가까운 부모 것에 붙는다.** 구조
지점 트리거는 그래서 `Hips` 의 액터가 되고, `detectCollisions = false` 는 그 액터를
시뮬레이션에서만이 아니라 **씬 질의에서도** 뺀다.

**`RemoteAvatar` 의 그 줄은 의도대로 동작했다** — 그림이 물리에 끼지 않게. 다만 같은
몸에 매달린 **질의용 트리거까지 함께 지웠다.** 어느 쪽도 잘못 쓰이지 않았고,
**두 의도가 같은 리지드바디를 공유한 것**이 문제다.

### 2-2. 442 의 잡기는 왜 멀쩡한가 — 선례가 이미 있었다

442 에서 만든 잡기 후보 탐색(`FindTeammateGrabPoint`)은 **물리 질의를 쓰지 않는다.**
`PlayerRig.All` 을 훑는다. 그렇게 만든 이유는 `-249` 의 "마스크를 열면 고정못이 딸려
온다" 였는데, **결과적으로 이 결함 유형을 통째로 피했다.**

**같은 프로젝트 안에 같은 문제의 두 해법이 공존하고, 한쪽만 참가자에서 동작했다.**
그것이 해법 선택의 근거다 — 새로 고안할 것이 없었다.

---

## 3. 고친 것 ① — 후보를 목록으로도 댄다

`Interactor` 에 **물리 질의가 볼 수 없는 후보를 소유자가 대는 자리**를 열었다.

```csharp
public Action<List<IInteractable>> ExtraCandidates;
```

`PlayerRig` 가 `CollectRescueCandidates` 를 걸고, 그 함수는 `All` 을 훑어 기절한
동료의 구조 지점을 담는다.

### 3-1. 점수 계산은 나누지 않았다

후보 채점을 `Consider` 로 뽑아 **질의로 찾은 것과 목록에서 온 것이 같은 길을
지나게** 했다.

```csharp
void Consider(IInteractable candidate, Vector3 origin, Vector3 forward,
              ref IInteractable best, ref float bestAngle,
              ref float bestSqrDistance, ref Vector3 bestPosition)
```

**거리·각도를 `CollectRescueCandidates` 에서 다시 자르지 않는다.** 거기서 한 번 더
자르면 두 경로의 기준이 갈라지고, `FindBestCandidate` 의 주석이 정한 "같은 상황에서
항상 같은 대상이 나와야 4인이 각자 화면에서 같은 판단을 한다" 가 깨진다.

같은 대상이 양쪽에서 두 번 들어와도 안전하다 — 두 번째는 각도·거리·좌표가 모두 같아
`IsBetter` 가 거짓을 돌려준다. 그래서 중복 제거를 따로 두지 않았다.

### 3-2. 무엇을 고치지 않았는가

- **`RemoteAvatar` 의 `detectCollisions = false` 를 되돌리지 않았다.** 되돌리면 그림이
  물리에 끼어 서버 권위가 깨진다. `ThePhysicsQueryStillCannotSeeIt_TheListIsWhatFindsIt`
  이 **여전히 질의로는 못 찾는다는 사실**을 잠근다 — 다음 사람이 "간단히" 되돌리는
  것을 막는 자리다.
- **`RagdollBuilder.WorldMask` 를 열지 않았다**(`-249`). 애초에 레이어는 맞았으므로
  마스크는 원인이 아니었다.
- **구조 지점을 몸에서 떼어내지 않았다.** 별도 루트로 두고 위치만 따라가게 하는 안도
  있었지만, 순간이동·리스폰마다 따라가는 코드가 새로 늘고 정적 콜라이더를 매 프레임
  움직이는 모양이 된다. 목록 훑기가 선례와도 일치한다.

---

## 4. 고친 것 ② — 살아나는 쪽 게이지

이쪽은 `detectCollisions` 와 무관한 **별개의 결함이고, 아직 만든 적이 없는 기능**이다.
두 겹이었다.

**① 화면이 명시적으로 막았다.**

```csharp
// DownedRescueHud.DrawScaledGui (이전)
if (me.Downed) return;   // 내가 기절해 있으면 아무것도 그리지 않는다
```

남을 일으킬 수 없는 것은 맞지만, 그렇다고 **자기가 일으켜지는 중인지도** 모르게 둘
이유는 없다. **기다려야 하는 쪽이 그 사람이다.**

**② 열어도 그릴 값이 그 기계에 없었다.** 진행분은 구조자의 인터랙터가
`DownedRescue.OnHoldProgress` 로 대상의 컴포넌트에 쌓고, 그 일은 **서버에서만**
일어난다(입력이 `CmdSendInput` 으로 와서 서버의 `PlayerRig` 가 실행한다).
`NetPlayer` 의 SyncVar 목록에 살리기 진행도가 **없었다** — `RescueHeld` 는 입력 프레임
비교에만 쓰인다. 실측으로도 대상 쪽 값이 0.000 이었다.

> **①만 고치면 빈 막대가 그려진다.** 유형 A("만들었는데 실행된 적이 없다")의 사촌 —
> **그릴 곳은 있는데 그릴 값이 없는 경우**다.

### 4-1. 방향이 반대인 값을 하나 더 둔다

| | 뜻 |
| --- | --- |
| `RescueProgress01` | **내가 남을** 일으키는 진행도 (있었다) |
| `BeingRescuedProgress01` | **남이 나를** 일으키는 진행도 (신설) |

서버가 계산하고 참가자는 받는다 — `Downed` 와 같은 처리다.

```csharp
[SyncVar] public float rescueProgress;          // NetPlayer
rescueProgress = body.BeingRescuedProgress01;   // 서버에서, downed 옆
```

`SetRemoteBeingRescued` 는 **권위가 있는 몸에서 스스로 무시한다.** 호스트에서는
`RefreshBeingRescued` 가 자기 `RescueTarget` 에서 직접 읽으므로, 받은 값으로 덮으면
값이 두 곳에서 오게 된다 — `-383` 의 "판정을 두 곳에 두면 반드시 갈라진다" 다.

**진행도만 보낸다.** 누가 일으키고 있는지는 보내지 않는다 — 쓰러진 사람 화면에 필요한
것은 "얼마나 찼는가" 뿐이고, 사람을 실으면 `WorldSync` 의 번호 짝짓기를 건드린다.
`hauling` 세 값이 같은 판단을 한 자리다.

### 4-2. 아무도 안 오면 아무것도 그리지 않는다

```csharp
public static bool ShouldDrawBeingRescued(bool downed, float beingRescued01) =>
    downed && beingRescued01 > 0f;
```

"일으켜 줄 사람을 기다린다" 같은 문구를 상시로 띄우면, 솔로에서 6초 뒤 스스로
일어나는 동안(§20.3)에도 계속 보여 **거짓말이 된다.**

**순수 메서드로 뺀 이유**는 `-400`·`-438` 에서 같은 자리에 걸렸기 때문이다 — 판단이
`OnGUI` 경로 안의 조건문으로만 있으면 검증이 닿지 못하고, 증상을 되살려도 초록불이
된다. 실제로 `-438` 의 대조 실험에서 검증 12개가 전부 통과했다.

---

## 5. 소스 글자를 보던 검증을 동작 검증으로 바꿨다

`RescueWiringTests.TheRescueInteractorTakesOnlyRescuePoints` 가 깨졌다. 내가 후보
채점을 `Consider` 로 뽑으면서 **필터가 있는 위치가 바뀌었기** 때문이다.

```csharp
// 이전
var find = TestSource.Method(..., "IInteractable FindBestCandidate()");
Assert.That(find, Does.Contain("Accepts"), "필터를 두고 후보 탐색에서 쓰지 않으면 …");
```

**필터는 그대로 걸려 있고 동작도 그대로인데 빨간불이 됐다.** 반대로 글자만 있고
동작하지 않아도 초록불이 되는 검증이다.

의도("필터를 두고 쓰지 않으면 아무 효과가 없다")는 옳으므로 **동작으로 재게** 바꿨다 —
필터가 받지 않는 대상을 `ExtraCandidates` 로 바로 앞에 대고 `Focus` 가 비는지 본다.
리팩터로 깨지지 않고, 글자만 맞는 경우도 잡는다.

> **교훈**: 소스 글자를 보는 검증은 **리팩터에 깨지고 거짓 통과도 허용한다.** 배선이
> 정말 없던 시절(`-390`)에는 그것이 유일한 수단이었지만, 부를 수 있는 자리가 생기면
> 동작으로 바꿔야 한다.

---

## 6. 기획과 구현이 어긋나 있다 — 키가 다르다

| | 무엇 |
| --- | --- |
| 기획 §20.3 | "동료가 가까이에서 **`E`** 를 2초 동안 유지하면 일으킨다" |
| 구현 | `Controls.RescueTeammate = KeyCode.G` |

화면 문구는 `Controls.RescueTeammate` 를 그대로 찍으므로 **`[G]`** 로 나온다. 신고가
"G" 라고 적힌 것도 그래서다. `E` 는 이미 상호작용에 쓰이므로 고칠 것은 기획서 쪽으로
보이지만 **[Plan] 판단이다.** 이 이슈에서 키를 건드리지 않았다.

---

## 7. 검증

**`RescueVisibilityTests` 18개** 신설.

| 검증 | 무엇을 잠그나 |
| --- | --- |
| **`OnAParticipantMachine_ADownedTeammateIsStillACandidate`** | **이 이슈의 핵심** — 참가자 화면에서 후보가 잡힌다 |
| **`ThePhysicsQueryStillCannotSeeIt_TheListIsWhatFindsIt`** | **질의로 풀지 않았다** — `detectCollisions` 되돌림 방지 |
| `OnTheHost_ItStillWorksTheSameWay` | 호스트 경로 회귀 방지 |
| `AStandingTeammateIsNotACandidate` | 멀쩡한 사람은 대상이 아니다 |
| `ADownedPlayerCannotRescueAnyone` | 쓰러진 사람은 못 일으킨다 |
| `YouAreNeverYourOwnRescueCandidate` | 자기 자신은 후보가 아니다 |
| `ATeammateBehindYou_IsNotACandidate` | **목록 후보도 시야각 규칙을 지난다** |
| `ATeammateTooFarAway_IsNotACandidate` | 사거리도 같다 |
| **`TheServerComputesHowFarAlongMyRescueIs`** | 서버가 내 구조 진행도를 만든다 |
| `AParticipantReceivesItInsteadOfComputingIt` | 참가자는 받는다 |
| `AnAuthoritativeBodyIgnoresTheIncomingValue` | 권위 있는 몸은 받은 값을 무시한다(-383) |
| `GettingUp_ClearsIt` | 일어나면 게이지가 사라진다 |
| **`WhenTheRescuerLetsGo_TheGaugeStops`** | **손을 떼면 멈춘다 — 거짓 표시 방지**(§4-3) |
| **`AParticipantDoesNotReviveOnItsOwn`** | 참가자는 완료를 스스로 하지 않는다(-145 · -378) |
| `TheHostDoesRevive` | 호스트에서는 실제로 일어난다 |
| **`TheDownedPlayersOwnScreenDrawsIt`** | **화면이 실제로 그리기로 한다** |
| `NobodyComing_DrawsNothing` | 아무도 안 오면 빈 막대를 띄우지 않는다 |
| `AStandingPlayerNeverDrawsIt` | 서 있는 사람은 구조자 쪽 표시를 쓴다 |

### 7-1. 대조 실험

셋 다 실제로 돌려 확인했다.

| 대조 | 결과 |
| --- | --- |
| `RescueInteractor.ExtraCandidates` 대입 삭제(= 병합 전 상태) | **1개 실패** — `OnAParticipantMachine_…` |
| `RefreshBeingRescued()` 호출 삭제 | **2개 실패** — `TheServerComputesHowFarAlongMyRescueIs` · `GettingUp_ClearsIt` |
| `ShouldDrawBeingRescued` 를 `false` 로 | **1개 실패** — `TheDownedPlayersOwnScreenDrawsIt` |
| 손을 떼도 진행분을 그대로 내보낸다(리뷰가 짚은 상태) | **1개 실패** — `WhenTheRescuerLetsGo_TheGaugeStops` |
| 쓰러진 사람에게도 후보를 댄다 | **1개 실패** — `ADownedPlayerCannotRescueAnyone` |

### 7-2. 기준선 대조

| | 검증 수 | 실패 |
| --- | --- | --- |
| develop (`04a740e`) | 1204 | 32 |
| 이 브랜치 | **1222** (+18) | **32** |

**새로 깨진 검증 없음. 실패 셋이 완전히 같다.** 남은 32개는 develop 에서 그대로
들고 온 것이다.

### 4-3. 손을 떼면 멈춘다 — !356 AI 리뷰가 짚었다

처음에는 `BeingRescuedProgress01` 을 `RescueTarget.Progress` 그대로 냈다. **그것이
거짓 표시를 만든다.**

`DownedRescue.Progress` 는 구조자가 손을 떼도 **남는다** — 다른 사람이 이어받게 하려고
일부러 그렇게 되어 있다(`Release`). 그 값을 그대로 내보내면 아무도 오지 않는 동안에도
쓰러진 사람 화면에 "일으켜지는 중…" 이 계속 뜬다. **§4-2 에서 피하려던 것 그대로다.**

그래서 이 값의 뜻을 **"지금 일으켜지고 있는 진행도"** 하나로 좁혔다.

```csharp
BeingRescuedProgress01 = Downed && RescueTarget != null && SomeoneIsRescuingMe
    ? RescueTarget.Progress : 0f;

bool SomeoneIsRescuingMe =>
    RescueTarget != null && RescueTarget.Occupant != null
    && RescueTarget.Occupant.State == InteractorState.Holding;
```

남은 진행분은 **구조자 쪽**이 알아야 하는 정보고 그쪽 화면은 이미 "이어서 일으키기"
로 보여준다 — 쓰러진 사람이 그것으로 할 수 있는 일은 없다.

> **내가 왜 놓쳤나.** §4-2 에 "아무도 안 오면 아무것도 그리지 않는다" 고 **적어
> 두고도** 그 조건을 `progress > 0` 으로 구현했다. 진행분이 남는다는 것은 같은 파일
> 주석에 적혀 있었다. **의도를 문장으로 적는 것과 그 의도를 코드가 지키는 것은
> 다르다** — 대조 실험을 그 문장에 대고 돌려야 했다.

### 4-4. 쓰러진 사람에게는 후보를 대지 않는다

막는 곳이 `DownedRescue.GetAvailability` 하나뿐이었다. 리뷰가 "그 한 줄이 사라져도
검증이 못 잡는다" 고 짚었고, 실제로 내 검증이 OR 단언이라 그랬다.

수집기에서 `if (Downed) return;` 으로 끊고 단언을 `IsNull` 로 조였다. `TickRescue` 도
`Downed` 이면 인터랙터를 취소하고 돌아가므로 뜻이 일관된다.

---

## 8. 남은 것

- **2인 이상 실제 멀티 확인이 아직이다.** EditMode 는 참가자 상태를
  `RemoteAvatar.Attach(mine: false)` + `SetRemoteDowned` 로 **재현**한 것이고, 두
  프로세스를 띄운 것이 아니다. **권한·예측(0.98 → 서버 확정) 경로는 실측으로만
  확인된다**(!356 AI 리뷰가 두 번 짚었다).

  | # | 누가 누구를 | 볼 것 |
  | --- | --- | --- |
  | 1 | 호스트 → 참가자 | `[G] 일으키기` 가 뜨고, 게이지가 차고, 일어난다 |
  | 2 | **참가자 → 호스트** | 같은 것. **이 이슈의 핵심** — 예전에는 표시조차 없었다 |
  | 3 | 참가자 → 참가자 | 같은 것. 서버가 남의 몸 둘을 다루는 경로다 |
  | 4 | 쓰러진 쪽 화면 | "일으켜지는 중…" 이 보이고, **손을 떼면 사라진다** |

  **참가자가 누를 때** 게이지가 끝까지 찬 뒤 잠깐 멈춘 것처럼 보이다가 일어나는 것이
  정상이다(`RescuePredictionCap` 0.98 에서 서고 서버 확정을 기다린다). **참가자 화면에서
  먼저 일어나면 그것이 잘못이다** — `-383` 이다.

  **이어받기도 한 번 본다** — 절반쯤 누르다 놓고 다른 사람이 이어서 누르면 그 화면
  게이지가 0 이 아니라 **절반에서** 시작해야 한다.
- **기획 §20.3 의 키가 `E` 로 적혀 있다.** 화면에는 `[G]` 가 나온다. [Plan] 확인.
- **이어받기 화면이 참가자에서 어떻게 보이는지 확인이 아직이다.** 앞사람이 남긴
  진행분은 `DownedRescue.Progress` 에 있고 구조자 쪽 게이지는 그 값을 그리는데,
  참가자의 구조자 화면은 자기 인터랙터의 예측을 본다. 값이 갈릴 수 있는 자리다.
- **`ExtraCandidates` 는 살리기에만 걸었다.** 고정쇠 인터랙터는 그대로 질의만
  쓴다 — 파츠는 참가자 기계에서도 물리가 살아 있어 증상이 없다. 다만 같은 함정이
  있는 자리인지 한 번 훑어볼 가치는 있다.
