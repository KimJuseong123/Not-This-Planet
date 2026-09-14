# S15P21B202-519 — 동료를 양손으로 들어 위로 던지기

> 2026-09-14 · 김주성(물리·퍼즐·캐릭터·상호작용·QA) · 브랜치 `S15P21B202-519-character`
>
> 기획 §20.4 동료 운반 · `docs/network-sync-policy.md` §3

---

## 1. 무엇을 만들었나

잡기는 `-442` 에서 열렸지만 **잡은 사람을 던지는 길은 없었다.** 기존 `ThrowTool` 은
`toolHeld` 만 보므로 사람에게는 닿지 않는다.

원하는 동작은 한 팔 오버핸드가 아니다.

> "양손으로 허리나 다리를 잡고 위로 던져버리는거지"
>
> "뒤쪽으로 던지는게 아닌, 높으면서 살짝 앞으로"

**힘이 팔이 아니라 다리와 상체 폄에서 나온다.** 그래서 기존 동작을 고쳐 쓸 수 없고 새로
만들어야 했다.

---

## 2. 동작 — `Rabbit_TossUp` (1.04초 · 18개 뼈)

### 2-1. 축을 쟀다

짐작으로 각도를 넣으면 팔이 엉뚱한 방향으로 꺾인다. 블렌더에서 뼈의 로컬 축을 먼저 쟀다.

```
R_Upperarm   X(-0.17,+0.00,+0.99) Y(-0.99,-0.00,-0.17) Z(+0.00,-1.00,-0.00)
→ +X = 앞으로,  +Z = 아래로

L_Upperarm   X(-0.16,-0.03,-0.99) Y(+0.96,+0.21,-0.16) Z(+0.21,-0.98,-0.00)
→ +X = 앞으로,  +Z = **위로** (오른팔과 부호가 반대다)
```

왼팔의 Z 가 뒤집혀 있다는 것을 재지 않았으면 양팔이 반대로 벌어졌을 것이다.

### 2-2. 회전만 키잉한다

`RabbitLocomotion` 은 애니메이션된 **위치를 쓰지 않는다.**

```csharp
if (!link.PreserveAnchor)
    link.Target.localPosition = Vector3.Lerp(link.PhysicsPosition, link.RestPosition, weight);
```

`link.RestPosition` 은 생성할 때 적어 둔 **기준 위치**다. 위치 키는 죽은 무게다 —
기존 `Rabbit_Walk.anim` 도 회전 커브만 들어 있다.

### 2-3. 액션을 `.blend` 에 저장하지 않았다

`METAGAME/Tools/Blender/generate_rabbit_toss.py` 가 액션을 만들고 FBX 로 내보낸다.
저장소에 이미 있는 `generate_spaceflop_models.py` · `generate_asset_previews.py` 와
같은 자리다.

**동작이 바이너리 덩어리가 아니라 읽히는 텍스트 diff 로 남는다.** 각도 하나를 바꾼
커밋이 무엇을 바꿨는지 리뷰에서 보인다.

### 2-4. 고르지 않은 안

| 안 | 판단 |
| --- | --- |
| `.blend` 에 액션을 저장 | **기각.** 3MB 짜리 zstd 압축 바이너리라 diff 가 안 보인다 |
| Mixamo 모션을 가져와 리타기팅 | **기각.** 이 동작은 기성 모션에 없고, 리그가 이미 있어 직접 만드는 편이 짧다 |
| 던져지는 쪽도 애니메이션으로 | **기각.** §5-2 — 그 그림은 래그돌이 그린다 |

---

## 3. 굽는 중에 잡은 것 둘 — 가드가 없었으면 조용히 나갔다

### 3-1. 클립 길이가 10.375초로 나왔다

블렌더 FBX 내보내기는 **액션이 아니라 씬 프레임 범위를 굽는다.** 기본값 1~250 이 그대로
구워져 24fps 기준 10.4초가 되었다. 내보내기 전에 범위를 액션에 맞췄다.

### 3-2. 커브가 0/18 개 뼈에 붙었다

처음에는 걷기와 똑같이 프리팹에 클립을 물려 프레임마다 샘플링했다. **움직인 뼈가
0개였다.**

| | 경로 |
| --- | --- |
| FBX 안의 커브 | `Root/Hip/Waist/...` |
| 프리팹의 계층 | `우주 토끼 3D 모델/Root/Hip/Waist/...` |

**아마튜어만 내보내면 그 오브젝트가 FBX 의 뿌리가 되어 이름 한 마디가 통째로 빠진다.**
뼈 이름으로 짝짓도록 바꿨다 — 그 층이 몇 마디든 상관없다.

> 걷기가 재생을 거치는 것은 **리타기팅이 필요해서**이지 이 방식이 모자라서가 아니다.

**이것이 이 이슈에서 가장 위험했던 자리다.** 에러 없이 "던지는데 아무 일도 안 일어나는"
클립이 나간다. 그래서 굽는 쪽에 가드를 두었다.

```csharp
var still = BoneNames.Where(n => !moved.Contains(n)).ToArray();
if (still.Length > 0)
    throw new InvalidOperationException(
        $"클립이 움직이지 않는 뼈가 있다 ({still.Length}/{bones.Length}개): " + …);
```

> 처음에는 "절반 미만" 이었다. !436 AI 리뷰가 **한두 뼈만 어긋나면 통과한다**고 짚어
> 하나라도 빠지면 실패하도록 좁혔다(§9-1).

읽는 쪽에도 검증을 두었다(`TheBakedClipDrivesEveryBoneTheCodeReads`) — **굽는 목록과 읽는
목록이 어긋나도 조용히 이상해지기** 때문이다.

---

## 4. 한 번짜리 클립 재생

`RabbitLocomotion` 이 걷기/달리기 2입력 믹서였다. 3번째 입력을 더하고 던지기가 보행보다
**앞서게** 했다 — 섞으면 던지는 동안 다리가 걸음을 걷는다.

### 4-1. 뼈 목록을 하나로 합치지 않았다

| | 움직이는 뼈 |
| --- | --- |
| 보행 | 팔·다리 **14개** |
| 던지기 | + 허리·척추·쇄골·머리·발 = **18개** |

**합치면 걷기가 망가진다.** 보행 클립에는 허리 커브가 없으므로, 목록에 넣으면 걷는 동안
허리가 물리가 아니라 **기준 자세로** 끌려간다. `InWalk` / `InToss` 로 갈랐고
`WalkingLeavesTheWaistToPhysics` 가 그 자리를 잠근다.

### 4-2. 팔이 바쁜지 보지 않는다

보행은 `LeftArmBusy` / `RightArmBusy` 인 팔을 건너뛴다. 그런데 **던지는 사람은 반드시
남을 들고 있으므로** 그 검사를 그대로 두면 정작 던지는 두 팔이 빠진다.

### 4-3. 물리 자세에서 들어갔다 나온다

```csharp
public static float TossWeight(float time, float length)
```

갑자기 1 로 켜면 팔이 순간이동하고, 갑자기 0 으로 끄면 끝나는 순간 팔이 툭 떨어진다.
조건문으로 `Apply` 안에만 두면 검증이 닿지 못한다 — `-400`·`-438`·`-448`·`-505` 에서
**네 번** 겪은 자리라 이번에는 먼저 뺐다.

---

## 5. 던지기 판정

### 5-1. 앞 성분을 수평으로 눕힌다

```csharp
var forward = aim;
forward.y = 0f;
```

조준을 그대로 쓰면 **하늘을 보고 던질 때 위 성분이 두 번 더해져** 천장까지 날아가고,
**땅을 보고 던지면 상쇄되어** 제자리에 떨어진다.

### 5-2. 속도는 몸 전체에 넣는다

골반에만 넣으면 나머지 부위가 조인트에 끌려가며 몸이 늘어나 보인다.

**날아가는 그림은 애니메이션이 아니다.** `RabbitLocomotion.Apply` 첫 줄이
`if (state.Blocked || !state.Grounded) { _weight = 0f; return; }` 이라, 발이 땅에서
떨어진 동안은 클립을 만들어도 가중치가 0 이다. 래그돌이 그린다.

### 5-3. 속도를 한 틱 늦춘다

```csharp
pendingTossVictim = victim;
pendingTossVelocity = TossVelocity(direction, charge);
```

놓은 조인트는 다음 틱에 사라진다. 같은 틱에 속도를 넣으면 **아직 살아 있는 구속이 그것을
먹는다.** 바로 위 도구 투척(`pendingThrowBody`)이 이미 같은 이유로 그렇게 하고 있었다.

### 5-4. 툭 던져도 사람이 떠야 한다

```csharp
public static float TossStrength(float charge01) =>
    Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(charge01));
```

충전에 완전히 비례시키면 문턱을 갓 넘긴 던지기가 **제자리 놓기와 구별되지 않는다.**

---

## 6. 멀티 — 값이 셋이고 방향이 둘이다

> "멀티가 꼭 되어야 한다. 호스트 뿐만 아니라 참가자도 제대로 보이고 시야도 던져지는
> 시야가 제대로 적용이 되어야 해"

| 값 | 어디로 | 왜 |
| --- | --- | --- |
| `tossed` | **내 몸에만** | 던져진 사람의 **시야**를 바꾼다. 카메라는 각자 화면의 일이다 |
| `tossSeq` | **남의 몸에도** | 던지는 **동작**은 남이 보는 그림이다 — 위와 정반대 |
| `tossCharge` | **내 몸에만** | 충전 게이지. 세는 것은 서버다 |

### 6-1. 사건을 수로 바꿔 보낸다

던지기는 한 순간의 사건이라 `ClientRpc` 로 보내고 싶어진다. 그러면 **패킷 하나를 잃으면
그 동작은 영영 안 보인다.**

세어 보내면 받는 쪽이 "마지막으로 본 수와 다르다" 로 판단하므로 한 번 늦게 와도
복구되고, 늦게 들어온 사람도 어긋난 상태로 시작하지 않는다.

```csharp
public static bool ShouldPlayToss(uint seen, uint now, bool known) => known && now != seen;
```

**`>` 가 아니라 `!=` 다.** `uint` 는 한 바퀴 돌면 작아지고, 그때 `>` 는 영영 거짓이 되어
던지기가 멈춘다.

**첫 값으로는 재생하지 않는다.** 늦게 들어온 사람에게는 이미 쌓인 수가 한꺼번에 오는데,
그것을 새 던지기로 보면 접속하자마자 모두가 허공에 던지는 시늉을 한다.

### 6-2. 충전을 참가자가 스스로 세지 않는 이유

**잡기 판정은 서버가 한다.** 참가자 기계의 내 손에 그 조인트가 있다고 믿을 근거가 없다.
그래서 서버가 세고 값을 보낸다 — `-448` 의 `rescueProgress` 와 같은 판단이다.

그런데 그렇게 하니 함정이 바로 따라왔다.

```csharp
var victim = TossVictim;
if (victim == null)
{
    tossCharge = 0f;     // ← 참가자 화면에서 매 틱 서버 게이지를 지운다
```

참가자 기계에서 `TossVictim` 이 null 이면 **서버가 방금 보낸 값을 매 틱 0 으로 덮는다.**
게이지가 호스트에만 보이는, 이번 주 내내 본 그 모양이다(QA 유형 L). `ServerAuthority`
일 때만 지우도록 고쳤다.

### 6-3. 시야는 기절 경로를 그대로 쓴다

```csharp
public bool BodyViewActive => Downed || Tossed;
```

둘 다 "몸이 구르는 대로 화면이 넘어갔다가 3인칭으로 물러난다" 로 같다. 카메라에 상태를
새로 만들면 저장·복원이 두 벌이 되어 서로 덮는다 — `SetDownedView` 안의
`pushView`·`vehicleView` 처리가 이미 그 문제를 적어 두었다.

`-511` 이 정확히 여기서 났다: 값은 잘 내려오고 플래그도 서는데 **받는 쪽이 카메라를 안
바꿨다**(QA 유형 M). 그래서 검증도 `BodyViewActive` 가 아니라 **`Cam.DownedView` 를
잰다** — 플래그만 보면 그 결함을 통과시킨다.

---

## 7. 검증

**`TeammateTossTests` 34개** 신설. 전체 **1479개 / 17 실패.**

| | 검증 수 | 실패 |
| --- | --- | --- |
| develop (`origin/develop` 실측) | 1444 | 17 |
| 이 브랜치 | **1479** (+35) | **17** |

**새로 깨진 검증 없음. 실패 셋이 완전히 같다.**

### 7-1. 대조 실험 11개가 전부 하나씩 잡는다

| 대조 | 결과 |
| --- | --- |
| 보내는 자리를 없앤다 | **1개** — `TheServerActuallySendsIt` |
| 받고도 카메라를 안 바꾼다 | **1개** — `AParticipantsCameraActuallySwitches` |
| 참가자도 스스로 날린다 | **1개** — `AParticipantCannotLaunchItself` |
| 골반에만 속도를 넣는다 | **1개** — `EveryPartGetsTheSameLift` |
| 조준을 눕히지 않는다 | **1개** — `LookingUpOrDownDoesNotChangeHowHighItGoes` |
| 수 비교를 `>` 로 한다 | **1개** — `ItSurvivesTheCounterWrappingAround` |
| 코드가 클립에 없는 뼈를 읽는다 | **1개** — `TheBakedClipDrivesEveryBoneTheCodeReads` |
| 충전을 안 보낸다 | **1개** — `TheServerSendsTheChargeAndTheHudDrawsIt` |
| HUD 가 안 그린다 | **1개** — 〃 |
| 참가자가 자기 빈손으로 게이지를 지운다 | **1개** — `AParticipantsGaugeSurvivesItsOwnEmptyHands` |
| 권위 있는 몸도 받은 충전을 쓴다 | **1개** — `AnAuthoritativeBodyIgnoresTheRemoteCharge` |
| 굽는 목록에만 뼈를 하나 더한다 | **1개** — `TheBakeListAndTheRuntimeListAreTheSame`(§9-2) |

### 7-2. 기존 검증 하나를 고쳤다

`RabbitLocomotionTests.WalkingChangesTheExistingRabbitLeg` 가 깨졌다. **이 이슈에서 새로
깨진 검증은 이것 하나다.**

```csharp
Assert.That(Suit.Locomotion.BoneCount, Is.EqualTo(14));   // 이전
```

짝 목록이 합집합(20)이 되면서 깨졌는데, **동작은 옳고 내부 구조를 재던 단언이 깨진
것**이다 — `-489` 에서 겪은 것과 같은 모양이다. 검증의 이름이 말하는 것은 "걷기가 다리를
움직인다" 이지 "짝 목록이 몇 개다" 가 아니다.

`WalkBoneCount` 로 바꾸고, **걷기가 허리를 건드리지 않는지** 잠그는 검증을 새로 넣었다.

### 7-3. 게임에서 봐야 하는 것

EditMode 에는 **PlayMode 검증 어셈블리가 없다**(`Tests/Editor` 하나뿐). Mirror 의
SyncVar 경로를 실제로 돌 수 없으므로 아래 넷은 2인 프로세스로 확인해야 한다.

1. 참가자가 던질 때 호스트 화면에, 호스트가 던질 때 참가자 화면에 **동작**이 보이는가
2. 던져진 사람의 **시야**가 몸을 따라 돌다가 3인칭으로 물러나는가
3. 착지하면 시야가 돌아오는가
4. **충전 게이지**가 참가자 화면에도 보이는가

---

## 8. 파트를 `[Character]` 로 정했다

`docs/parts.md` 기준으로 **고칠 코드가 사는 곳**을 따랐다.

| | 비중 | 무엇 |
| --- | --- | --- |
| `PlayerRig` · `RabbitLocomotion` · `SpaceSuit` | 대부분 | 캐릭터 조작·동작 |
| `NetPlayer` | 중간 | SyncVar 3개 |
| `GameHud` | 네 줄 | 게이지 |

선례도 같다 — **`-442`(살아있는 플레이어 잡기)가 `[Character]`** 이고 이 이슈는 그 잡기
위에 얹는 동작이다.

> **반대 논거.** 이번 주 `-505`·`-513` 이 "참가자에게 값이 안 간다" 를 `[Network]` 로
> 갔고 이 이슈도 멀티가 요구사항의 절반이다. 다만 **그 둘은 전송만이 작업의 전부**였고
> 여기는 **동작을 새로 만드는 것이 본체**라 갈랐다. 팀 규약이 다르면 바꾼다.

---

## 9. !436 AI 리뷰에 대한 답

⚠️(머지를 막지 않음) 판정이었고 중간 4건이었다. **셋을 받아들였고 하나는 적용할 수 없었다.**

### 9-1. 굽는 가드가 "절반 미만" 만 잡았다 — 받아들임

> 한두 뼈만 어긋나면 가드를 통과한 채 읽는 쪽이 그 뼈를 조용히 건너뛴다.

**정확하다.** 내가 "절반" 으로 느슨하게 잡은 이유가 없었다. **하나라도 안 움직이면
실패**로 좁히고, 어느 뼈가 빠졌는지 메시지에 나열한다 — 그것이 없으면 실패해도 어디를
봐야 할지 모른다.

### 9-2. 뼈 목록이 두 곳에 하드코딩 — 받아들임

에디터 어셈블리가 게임플레이 어셈블리를 참조하지 않아 목록이 둘이다
(`RabbitLocomotionAssets` 가 먼저 그렇게 하고 있었다). 참조를 만들 수 없으니
**소스 글자로 맞대어 보는 검증**을 넣었다(`TheBakeListAndTheRuntimeListAreTheSame`).
대조로 확인했다 — 굽는 목록에만 뼈 하나를 더하면 실패한다.

### 9-3. early return 이 도구 상태 초기화를 건너뛴다 — 받아들임

> 도구 충전 중 상태가 남은 채 양손 잡기로 넘어가면 HUD 가 `ChargingTool` 분기를 먼저 타
> 던지기 게이지가 가려질 수 있다.

**읽어서 반박하지 않고 확인했다.** `ResetToolState` 가 `chargingTool` 을 지우고 있어
실제 증상이 나려면 도구가 외부에서 파괴되는 좁은 경로여야 한다. **그래도 지적은 맞다** —
early return 이 아래 초기화를 건너뛰는 구조 자체가 함정이다. 초기화 세 줄을 던지기 분기
**위로** 옮겨 두 경로가 모두 지나가게 했다.

### 9-4. `tossCharge` 를 `SyncMode.Owner` 로 — **적용할 수 없다**

> `[SyncVar(hook=null)]` 대신 `syncMode = SyncMode.Owner` 지정

**Mirror 에는 그 API 가 없다.** 실측으로 확인했다.

```csharp
// ThirdParty/Mirror/Core/Attributes.cs
public class SyncVarAttribute : PropertyAttribute
{
    public string hook;      // ← 이것뿐이다
}

// ThirdParty/Mirror/Core/NetworkBehaviour.cs
[HideInInspector] public SyncMode syncMode = SyncMode.Observers;   // ← 컴포넌트 단위
```

`syncMode` 는 **NetworkBehaviour 단위**라, `NetPlayer` 를 `Owner` 로 바꾸면
`downed`·`tossSeq`·`riding`·외모까지 전부 소유자에게만 가서 **남의 화면에서 사라진다.**

**대역폭 지적 자체는 맞다.** 제대로 고치려면 소유자만 읽는 값들(`health` ·
`rescueProgress` · `tossCharge`)을 별도 NetworkBehaviour 로 떼어야 하고, 그것은 이 MR 의
범위를 넘는다 — §10 에 후속으로 적었다.

### 9-5. 리뷰가 짚지 않았지만 그 덕에 찾은 것 — **빌드에서 안 움직일 뻔했다**

9-1 을 고치려고 다시 구웠더니 `.anim` 에서 **181줄이 사라졌다.**

```yaml
m_ClipBindingConstant:
  genericBindings: []      # ← 18개였던 것이 비었다
```

이 표가 **커브를 실제 뼈에 붙이는 것**이다. 비면 **에디터에서는 커브가 멀쩡히 보이는데
빌드에서는 아무 뼈도 안 움직인다.**

원인은 덮어쓰기다. `EditorUtility.CopySerialized` 는 메모리의 클립을 그대로 복사하는데
그 표는 **새 에셋을 직렬화할 때** 만들어진다. 첫 굽기는 `CreateAsset` 이라 채워졌고,
두 번째부터 비었다. 재임포트(`ForceUpdate`)로는 되살아나지 않는 것도 실측으로 확인했다.

**언제나 지우고 새로 만들도록** 바꿨다. GUID 가 바뀌지만 이 클립은 `Resources.Load` 로
이름으로만 찾으므로 참조가 깨질 데가 없다(GUID 검색 0건).

> **기존 검증이 이것을 못 잡는다.** `TheBakedClipDrivesEveryBoneTheCodeReads` 는
> **에디터 커브**를 보므로 통과한다. 그래서 **파일 자체**를 보는 검증을 따로 넣었다
> (`TheShippedClipCanActuallyBindAtRuntime`).

> **걷기도 같은 함정을 안고 있다.** `RabbitLocomotionAssets` 가 같은 `CopySerialized`
> 패턴이다. 지금 `Rabbit_Walk.anim` 은 표가 차 있지만(한 번만 구웠다), **다시 구우면
> 비게 된다.** §10 에 적었다.

---

## 10. 남은 것

- **2인 프로세스 확인** (§7-3). 이것이 끝나기 전에는 "멀티가 된다" 고 적지 않는다.
- **PlayMode 검증 어셈블리 신설**은 여전히 후속 이슈다. `-448`·`-505` 에 이어 세 번째로
  같은 한계에 걸렸다.
- **`RabbitLocomotionAssets` 도 같은 덮어쓰기 함정을 안고 있다**(§9-5). 걷기·달리기를
  다시 구우면 바인딩 표가 비어 빌드에서 안 움직인다. 이번 MR 에서 고치지 않은 것은 지금
  그 클립들이 멀쩡하고, 남의 기능을 같은 MR 에서 건드리지 않기 위해서다.
- **소유자만 읽는 SyncVar 를 별도 NetworkBehaviour 로**(§9-4). `health` ·
  `rescueProgress` · `tossCharge` 가 방 전원에게 브로드캐스트된다.
- 기획 §20.4 에 던지기 항목이 없다. `[Plan]` 에 한 줄 필요하다 — 조작(양손 잡기 + Q
  충전)과 세기를 적어 두어야 다음 사람이 도구 투척과 헷갈리지 않는다.
