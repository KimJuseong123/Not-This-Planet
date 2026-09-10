# S15P21B202-394 — 협동 밀기가 마찰을 못 이기는 문제

> 2026-09-10 · 김주성(물리·퍼즐·캐릭터·상호작용·QA) · 브랜치 `S15P21B202-394-physics`
>
> 선행 이슈 `-250`(협동 밀기 구현) · `-289`(협력 밀기 보고) · `-247`(운석 충격)

---

## 1. 무엇이 문제였나

**협동 밀기가 한 번도 작동한 적이 없었다.** 셋이 모여도 무거운 파츠가 한 치도
움직이지 않는다. 집계는 정상이고(`PullerCount = 3`) 화면에 붙잡은 자세도 나오는데
파츠는 제자리에 있다.

원인은 둘이다.

### 1-1. 밀기 가속이 마찰의 25% 였다

| 항목 | 값 | 출처 |
| --- | --- | --- |
| 파츠 질량 | 406 ~ 745 kg | `PartSpec.Mass` (부피 x `PartDensity` 70) |
| 중력 | −5 m/s² | `Tuning.Gravity` |
| 지면 정지 마찰 | **0.95** | `Prim.GroundPhysics.staticFriction` |
| 마찰 합성 | **Maximum** | `Prim.GroundPhysics.frictionCombine` |
| **이겨야 하는 가속** | **4.75 m/s²** | 0.95 x 5 |
| **실제 밀기 가속** | **1.2 m/s²** | `PhysicsScale.PartPushAccel` |

스텝(0.01s)당 `Drive()` 가 더하는 속도는 0.012 m/s, 마찰이 지울 수 있는 속도는
0.0475 m/s 다. **지우는 쪽이 4배**라 매 스텝 순수하게 0 이다.

`frictionCombine = Maximum` 이라 **파츠에 미끄러운 재질을 붙여도 무효다** —
지면의 0.95 가 지배한다.

그리고 **인원수는 최고 속도 표에만 들어가고 가속에는 들어가지 않았다.**
셋이 되면 도달 가능한 속도만 0.4 → 1.2 m/s 로 오르는데, 거기까지 가는 힘은
1명일 때와 같은 고정값 1.2 였다. 즉 "셋이 모이면 가중치가 붙는다" 가 코드에 없었다.

### 1-2. 잡고 미는 방향이 사람 위치에서 나왔다

```csharp
// 예전 — GatherPullers()
var away = AwayFrom(player);   // 파츠 중심 → 플레이어 위치
```

`MoveWish` 를 **아예 보지 않았다.** 그래서 잡은 채로 어디로 걸어가든 결과가 같았다.

| 상황 | 예전 결과 |
| --- | --- |
| 셋이 잡고 **뒤로** 걷기 | 파츠가 따라온다 ⭕ |
| 셋이 잡고 **앞으로** 밀기 | 파츠가 **자기들 쪽으로** 와서 몸에 부딪힌다 ❌ |
| 셋이 잡고 **가만히** 있기 | 움직이려 든다 ❌ |

### 1-3. 그리고 1인 기준으로 잡기와 몸밀기에 차이가 없었다

`Drive()` 의 두 분기가 같은 상수와 같은 속도표를 썼고, `IsPulled` 가
`PullerCount >= 3` 을 요구했다. 그래서 **1명이 양손으로 잡아도 몸밀기 분기로
떨어져, 잡는 것과 안 잡는 것이 결과가 완전히 같았다.**

---

## 2. 검증 27개가 왜 전부 초록불이었나

`PartPushTests` 는 **`Physics.Simulate` 를 한 번도 부르지 않았다.** 중력도 껐고
지면도 없었다.

```csharp
static void Push(PartPush push, float seconds)
{
    for (var i = 0; i < steps; i++)
    {
        UnityEngine.Physics.SyncTransforms();
        push.Tick(Step);              // ← Physics.Simulate 가 없다
    }
}

var body = go.AddComponent<Rigidbody>();
body.useGravity = false;              // ← 지면도 없다
```

그래서 실제로 확인한 것은 이것뿐이었다.

> `Drive()` 가 `linearVelocity` **필드에 써 넣은 값을, 같은 필드에서 다시 읽으면
> 같은 값이 나온다.**

셋이 당기는 검증조차 단정이 속도 변수였다 — `Assert.Greater(Horizontal(push).x, 0.3f)`.
**위치는 한 번도 보지 않았다.**

---

## 3. 무엇을 어떻게 고쳤나

### 3-1. 속도 대입을 버리고 힘으로 준다

처음에는 인원별 **가속** 표를 두려 했다(`{0, 1.2, 2.4, 8, 10}`). 실측이 그것을 막았다.

```
[측정] 실제 파츠 형상 · 가속을 직접 대입
Part_1 460kg →  a2.40=1.16m  a4.75=3.25m  ...   ← 2.4 에 이미 밀린다
Part_2 429kg →  a2.40=0.00m  a4.75=0.00m  a5.50=2.42m
Part_3 745kg →  a2.40=0.00m  a4.75=0.00m  a5.50=2.28m
Part_4 406kg →  a2.40=0.00m  a4.75=0.00m  a5.50=0.28m  a6.00=2.69m
평평한 2 x 2 바닥 500kg →  a8.00 으로도 v=(0,0,0)
```

**문턱이 접촉 구성에 따라 2.4 에서 8 이상까지 흔들린다.** Part_1 은 최하단 상자가
0.94 x 0.94 뿐이라 접촉점이 적어 문턱이 가장 낮고, 접촉점이 많은 평평한 바닥은
가장 높다. **가속 상수 하나로는 어떤 값도 옳지 않다.**

그래서 **`AddForce` 로 바꾸고 필요 힘을 질량에서 계산한다.** 힘은 solver 가 마찰과
한 층에서 풀어 형태와 무관하게 일관되고, 질량이 실제로 의미를 갖는다.

```csharp
public static float ForceFor(int people, float mass)
{
    if (people < MinPeople || mass <= 0f) return 0f;

    var needed = mass * GroundFrictionAccel * FrictionMargin;
    return needed * people / MinPeople;
}
```

- `GroundFrictionAccel` = `Prim.GroundPhysics.staticFriction * |Tuning.Gravity|` =
  4.75 m/s². **지면 재질에서 값을 얻으므로 재질을 고치면 따라온다.**
- `FrictionMargin` = **2.5**. PhysX 의 정지 마찰은 이론값의 1.35 ~ 2.05배를 요구했다
  (실측). 남는 여유는 경사면 몫이다 — 20도 경사에서 필요한 값이 1.30배다.
- `MinPeople` = **3**.

### 3-2. 인원 문턱은 규칙으로 둔다 — 물리에 맡길 수 없다

"둘은 무게를 못 이기고 셋은 이긴다" 를 마찰이 저절로 갈라 주면 규칙이 필요 없다.
**그런데 그럴 수 없다.**

둘이 이론값의 2/3 를 낼 때 안 밀리고 셋이 1배를 낼 때 밀리려면 여유가
**2.05배보다 크고 3.08배보다 작아야** 하는데, 두 조건은 동시에 만족되지 않는다
(2/3 x 3.08 = 2.05). 접촉 구성이 문턱을 1.5배로 흔들기 때문이다.

그래서 문턱은 `MinPeople` 로 명시하고, 셋 이상에게는 어떤 형태의 파츠든 확실히
이길 힘을 준다. 셋 미만은 **파츠를 건드리지 않는다** — 지울 것을 알면서 매 스텝
힘을 넣으면 파츠가 미세하게 떨리고 `_pushedRecently` 가 계속 켜져 감쇠와 외부
충격 판별이 내내 헛돈다.

### 3-3. 남은 여유에 맞춰 힘을 깎는다

`AddForce` 는 다음 물리 스텝에야 속도가 된다. 상한 바로 아래에서 온 힘을 넣으면
그 한 스텝이 표를 넘어선다 — **셋이 미는데 1.2 대신 1.31 m/s 가 나왔다.**

```csharp
var room = (top - along) * _body.mass / deltaTime;
var applied = Mathf.Min(force, room);
```

### 3-4. 방향을 이동 입력에서 얻는다

```csharp
var wish = player.MoveWish;
wish.y = 0f;
var heading = wish.normalized;
pusher.Direction = heading;      // 위치가 아니라 입력
```

잡고 밀면 밀리고, 잡고 당기면 당겨진다. **규칙이 하나다.**

몸밀기만 "파츠 쪽으로 향하는 이동인가"(`PartPushAimDot`)를 계속 본다 —
§13.1 "당기기는 제공하지 않으며 반대편으로 이동해 반대 방향으로 민다" 이고,
손이 비었으면 파츠를 당길 수단이 없다. 잡은 사람은 어느 쪽으로든 끌 수 있다.

### 3-5. 잡기와 몸밀기를 하나의 인원으로 합산한다

`HaulerCount` = 손으로 잡은 사람 ∪ 몸으로 미는 사람. **둘이 잡고 하나가 몸으로
밀어도 셋이다.** 같은 사람이 잡고 동시에 몸으로 밀어도 한 명이다.
§13 은 "힘을 합쳐 민다" 이지 "잡는 것과 미는 것이 다른 기능" 이 아니다.

이동 입력이 없으면 참여로 세지 않는다 — 잡고 가만히 서 있는 것은 힘을 쓰는 것이
아니다.

### 3-6. 접촉을 모든 콜라이더로 판정한다

**조사 중에 드러난 별개의 결함이다.** `PartPush` 는 `GetComponent<Collider>()` 로
**첫 콜라이더 하나만** 봤다. 파츠는 실제 형태를 따르는 상자 여럿이므로(§25.3),
Part_1 은 그것이 최하단 0.94 x 0.94 받침이 되어 **골반이 1.08m 떨어진 것으로
계산되고 몸으로 미는 것이 아예 시작되지 않았다.**

`Initialize()` 에서 `GetComponents<Collider>()` 로 전부 모으고, `IsTouching` 은
가장 가까운 상자로, `Toward` 는 모든 상자를 감싼 범위의 중심으로 판정한다.

---

## 4. 검증

`PartPushTests` 를 다시 썼다. **하네스가 둘이고 그 이유가 이 검증의 핵심이다.**

| 하네스 | 조건 | 무엇을 단정하는가 |
| --- | --- | --- |
| `MakePart` | 지면 없음 · 파츠 중력 꺼짐 · `Physics.Simulate` 돈다 | 집계 규칙(인원·방향·유예)과 속도 규칙(표 상한·운석 충격 보존) |
| `MakeGroundedPart` | `Prim.GroundPhysics` 지면 · 중력 켜짐 · `Physics.Simulate` | **이동 거리** |

**27개 → 40개.** 전부 통과.

새로 넣은 것 중 요점.

| 검증 | 무엇을 잠그나 |
| --- | --- |
| `OneHauler_DoesNotBudgeAHeavyPart` | 혼자서는 3초에 0.1m 미만 |
| `TwoHaulers_StillDoNotBudgeIt` | 둘도 못 이긴다 |
| `ThreeHaulers_ActuallyMoveIt` | **셋은 3초에 1.5m 초과 — 이 이슈의 목적** |
| `FourHaulers_MoveItFurtherThanThree` | 셋이 먼저 움직였는지 본 뒤 비교한다 |
| `TheHeaviestPart_MovesWithThree` | 745kg Part_3 을 **실제 콜라이더 구성으로** |
| `TheNarrowestFootedPart_DoesNotMoveWithTwo` | Part_1(문턱이 가장 낮다)이 둘에 안 밀린다 |
| `GrabbingAndWalkingForward_HaulsThePartForward` | **잡고 앞으로 밀면 앞으로 간다** |
| `GrabbingAndWalkingBackward_HaulsThePartBack` | 잡고 뒤로 걸으면 따라온다 |
| `GrabbingWithoutMoving_DoesNothing` | 잡고 서 있는 것은 힘이 아니다 |
| `GrabbersAndBodyPushersAddUp` | 둘이 잡고 하나가 몸으로 밀면 셋 |
| `TheForceForThreeBeatsGroundFriction` | 힘이 지면 재질에서 계산되는가 · 여유가 2.05배 초과인가 |
| `TheForceScalesWithMass` | 질량 2배면 힘 2배 |
| `ThreeHaulersStopAtTheTableSpeed` | 오버슈트 없이 표의 1.2 에 머문다 |

### 4-1. 대조 실험

변경을 일부러 빼면 **정확히 그 검증만** 빨간불이 되는지 확인했다.

| 대조 | 결과 |
| --- | --- |
| `FrictionMargin` 2.5 → 1.0 (이론값만 준다) | 4개 실패 — 이동 검증 3개 + `TheForceForThreeBeatsGroundFriction` |
| 잡기 방향을 위치 기반으로 되돌림 | 6개 실패 — `GrabbingAndWalkingForward…` 포함 |

### 4-2. develop 기준선 대조

| | 검증 수 | 실패 |
| --- | --- | --- |
| develop (`68fa175`) | 986 | 32 |
| 이 브랜치 | **1001** (+15) | **32** |

**새로 생긴 실패 0개.** 32개는 develop 에서 이미 실패하던 것들이고 실패 집합이
정확히 동일하다.

---

## 5. 지운 것

| 무엇 | 왜 |
| --- | --- |
| `PhysicsScale.PartPushAccel` | 인원별로 달라지므로 상수가 아니라 표다. 자리에 이유를 적은 주석을 남겼다 |
| `PhysicsScale.PartPullMinPlayers` | `PartPushTable.MinPeople` 하나에서 따라 나온다. 둘을 두면 표를 고칠 때 어긋나고, 어긋나면 이 버그가 다시 생긴다 |
| `PartPush.AwayFrom` | 방향을 위치에서 얻던 함수. 쓰는 곳이 없어졌다 |
| `PartPush.PushDirection` · `PullDirection` | `HaulDirection` 하나로 합쳤다 |
| `PartPush.IsPushed` · `IsPulled` | `IsHauling`(밀려고 한다) · `IsMoving`(실제로 움직인다) · `Bracing`(붙었는데 모자란다)로 갈랐다 |

기획 §13.2 의 **속도표(`Speeds`)는 그대로 둔다.** 1·2명 행(0.4·0.8 m/s)은 힘이
0 이라 도달하지 않지만, 기획 문서의 값이므로 지우지 않고 주석으로 설명했다.
1~2명이 못 미는 것은 기획 판단으로 확인했다.

---

## 6. 남은 것

### 6-1. 연출이 없다 — 이것이 없으면 고쳐도 "안 되는 것"으로 보인다

`Bracing` 을 노출했지만 **지금 쓰는 곳은 검증뿐이다.** 한두 명이 붙었을 때 화면에
아무 반응이 없으면 플레이어는 고장으로 읽는다. 셋 중 하나가 필요하다.

- 잡고 버티는 자세 (팔에 힘이 들어간 포즈)
- 파츠의 미세 흔들림 (밀리진 않지만 반응한다)
- "○/3 명" 표시 — `PartPush.MinPeopleToMove` 가 그 값을 준다

**만들었는데 아무도 쓰지 않는 상태**이므로, 후속 이슈로 올려 두지 않으면 QA 기록의
유형 A(`MeteorWarningView`·`ShipAssembly.Blocker`·`LaunchTest.FailureReason`)와 같은
사례가 하나 더 늘어난다.

### 6-2. 다중 접속 실측이 0회다

셋 이상이 필요한 기능인데 사람 셋으로 재본 적이 없다.

구조는 확인했다 — 참가자 화면의 파츠는 `WorldEvents.RemoteDecides` 로 항상
kinematic 이라 `CanPush` 가 false 이고 `TryGrab` 도 거부되는데, **이것이 의도된
설계다.** 판정은 서버(방장)에서만 돌고 참가자의 잡기·이동 입력은
`InputFrame.GrabLeft` / `GrabRight` / `Move` 로 서버에 전달되어 서버 쪽 `PlayerRig`
가 대신 짚는다.

다만 "서버가 원격 세 사람의 손 상태를 제대로 집계하는가" 는 **사람 셋으로 재봐야**
안다. EditMode 로는 잡히지 않는 종류다.

### 6-3. 상수 조정에 플레이가 필요하다

- `FrictionMargin` 2.5 — 셋이 밀 때 "무겁게 느껴지는가" 는 플레이로만 안다.
  너무 크면 빈 상자를 미는 느낌이 된다
- `MinPeople` 3 — 기획 §13.2 의 1·2명 행을 사실상 폐기하는 값이다
- 경사면에서의 거동 — 여유 2.5 가 20도까지 커버한다는 것은 계산이고 실측이 아니다

### 6-4. `Welded` 파츠는 여전히 범위 밖

미는 것이 탑 전체를 미는 것이 되므로 `-250` 에서 제외했고 이 이슈도 건드리지
않았다. 기획 판단이 필요하다.

---

## 7. 배운 것

**"안 된다" 는 보고를 코드를 읽는 것만으로는 못 찾는 종류가 있다.** 이 코드는
처음부터 끝까지 설계대로 정확히 작동했다. 잘못은 상수 하나가 물리 조건 대비 4배
작다는 것이었고, 그것은 코드 어디에도 적혀 있지 않았다.

세 가지가 결정적이었다.

1. **지면·중력을 켠 하네스로 이동 거리를 쟀다** — 기존 검증이 안 하던 일이다
2. **마찰만 제거한 대조를 했다** — 같은 코드가 2.40m 움직여 원인이 하나로 좁혀졌다
3. **이론값을 안 믿고 문턱을 훑었다** — 이론 4.75 는 실제로 안 움직였고, 문턱이
   접촉 구성에 따라 흔들린다는 것도 그래서 알았다. 처음 제안했던 가속 표는
   이 훑기 때문에 폐기됐다

그리고 이번에도 확인했다. **검증이 초록불인 것은 "기능이 된다" 는 뜻이 아니다.**
`-275` 는 컴포넌트를 아무도 만들지 않아서, 이번엔 물리가 돌지 않아서 통과했다.
둘 다 검증이 **자기가 만든 좁은 세계 안에서만** 맞았다.

그래서 힘을 넣는 상수 옆에는 **이겨야 하는 저항을 같은 단위로 적어 두기로 했다.**
`PartPushAccel = 1.2f` 옆에 "필요: μs 0.95 x g 5 = 4.75 m/s²" 가 적혀 있었다면
코드 리뷰에서 잡혔다.
