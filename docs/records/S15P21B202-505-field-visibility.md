# S15P21B202-505 — UFO 빔(저중력장)이 호스트에게만 보인다

> 2026-09-14 · 김주성(물리·퍼즐·캐릭터·상호작용·QA) · 브랜치 `S15P21B202-505-network`
>
> 기획 §14 저중력장 · `docs/network-sync-policy.md` §3

---

## 1. 무엇이 문제였나

> "UFO 빔(저중력장)을 해결하자. 호스트만 보이고 다른 참여자들은 안보인다."

**파츠가 뜨는 것은 참가자도 본다** — 장이 띄운 파츠 위치는 `WorldSync` 가 나른다.
**무엇이 띄우는지만 안 보인다.**

---

## 2. 실측

프로브를 임시로 만들어 쟀다(조사용이라 커밋하지 않았다).

```
참가자 — 저중력장 False → False (Authoritative=False · SetInput 이 곧바로 반환)
전송   — NetPlayer 의 'Field' 등장 2회 (입력 비교만인가=True) · WorldSync 의 'Field' 등장 0회
빔표시 — SetActive 가 `on && Application.isPlaying` 일 때만 Visual 을 만든다
```

**둘째 줄이 결정적이다.** `WorldSync` 에 `Field` 라는 글자가 **0회**다. `NetPlayer` 의
2회는 `a.FieldToggle == b.FieldToggle` — **입력 프레임 비교뿐**이고, `-448` 의
`RescueHeld` 가 있던 자리와 같다.

### 2-1. 호스트 경로는 재지 못했다

컨트롤러의 저중력장 토글은 `mountedVehicle != null` 일 때만 요청된다. 하네스가
`TryBoard` 는 성공시켰는데 **컨트롤러가 그 탑승을 자기 것으로 인식하지 않았다** —
그쪽은 조준(`FocusedVehicle`)을 거쳐 자기 경로로 타야 한다.

**그래서 "호스트에서는 보인다" 는 측정이 아니라 신고 내용이다.** 거기에 하네스를 더
붙이지 않은 이유는, 고쳐야 할 곳이 호스트 쪽이 아니라 **전송이 없는 것**이기 때문이다.

---

## 3. 왜 이렇게 되는가

| 단계 | 무엇 |
| --- | --- |
| 빔 그림 | `LowGravityField.SetActive(on)` 안에서 `on && Application.isPlaying` 일 때 `LowGravityFieldVisual` 을 붙인다 |
| `IsActive` 를 바꾸는 곳 | 켜는 호출은 **`TractorBeamController` 하나뿐**(`ShipHatch` · `DemoEnding` 은 끄기만 한다) |
| 그 호출 앞 | `SetInput` 이 `if (!Authoritative) { …; return; }` |
| 전송 | **없었다** |

참가자 기계에도 `LowGravityField` 컴포넌트는 **있다** — 씬/프리팹에 들어 있으니까.
다른 것은 `IsActive` 하나뿐이고, 그래서 그림만 안 생긴다.

### 3-1. `-448` 과 같은 모양이다

| | 올라가는 것 | 내려오는 것 |
| --- | --- | --- |
| 살리기 | `InputFrame.RescueHeld` | **없었다** → 게이지가 안 보임 |
| 저중력장 | `InputFrame.FieldToggle` | **없었다** → 빔이 안 보임 |

둘 다 서버가 입력을 받아 **제대로 실행한다.** 물리 결과도 맞다. **실행 결과가 화면에
필요한 값인데 그것을 아무도 돌려보내지 않는다.**

> QA 기록에 **유형 L** 로 적었다. 유형 J(한 리지드바디에 두 의도)와 헷갈리지 않게 —
> J 는 **있는데 질의에 안 걸리는** 것이고, L 은 **값 자체가 그 기계에 온 적이 없는**
> 것이다.

---

## 4. 고친 것

`WorldSync` 가 탈것별 저중력장 상태를 나른다. **고정쇠(`ClampState[]`)와 같은 자리,
같은 방식**이다 — 바뀔 때만 보내고, 신뢰 없는 채널이라 조용해도 `ClampKeepalive`
마다 다시 보낸다.

```csharp
public struct FieldState
{
    public int Vehicle;         // tracked 색인
    public bool Active;
}
```

### 4-1. 탈것 번호는 이미 있는 것을 쓴다

`-434` 가 이렇게 적어 두었다.

> 여기서 탈것만 따로 줄을 세우면 -315 가 기록한 사고(개수는 같은데 순서만 어긋남)를
> 새로 만든다.

그래서 **새 줄을 세우지 않았다.** `vehicleSlots` 에 담는 것은 `tracked` 의 색인이고,
그 색인은 `Collect()` 의 정렬이 끝난 뒤에 확정된다 — `trackedParts` 가 같은 이유로
같은 자리에 있다. 받는 쪽은 이미 있는 `VehicleAt(index)` 로 되찾는다.

**번호를 함께 실어 보내므로 개수가 달라도 버리지 않는다.** 고정쇠와 다른 점이다 —
저쪽은 색인이 곧 자리라서 개수가 어긋나면 남의 고정쇠에 값이 들어간다. 여기는 못 찾은
번호만 건너뛰면 된다.

### 4-2. 참가자는 그림만 켠다

```csharp
public void ApplyNetworkActive(bool on)
{
    if (IsActive == on) return;
    SetActive(on);
}
```

**물리는 따라오지 않는다.** `LowGravityField.Tick` 이 `!_physicsAuthority` 에서
반환하므로 참가자가 이 값을 켜도 힘을 넣지 않는다. 켜지는 것은 **그림과 그 그림이 읽는
상태뿐**이다 — 판정을 두 곳에 두면 반드시 갈라진다(`-383` · `-145`).

이름은 `PartClamp.ApplyNetworkState` 와 같은 규칙을 따랐다.

### 4-3. 보낼 때를 순수 메서드로 뺐다

```csharp
public static bool ShouldSendFields(FieldState[] now, FieldState[] lastSent,
                                    float keepaliveTimer, float keepalive) =>
    !SameFields(now, lastSent) || keepaliveTimer >= keepalive;
```

`SendFromServer` 안의 조건문으로만 두면 검증이 닿지 못한다 — `-400`·`-438`·`-448` 에서
**세 번** 겪은 자리다.

### 4-4. 고르지 않은 안

| 안 | 판단 |
| --- | --- |
| 참가자도 로컬로 `Toggle` | **기각.** 판정이 두 곳이 된다(`-383`). 안 탄 사람 화면은 여전히 못 맞춘다 |
| `NetPlayer` 에 SyncVar | **기각.** 장은 **탈것**에 속한다 — 타는 사람이 바뀌어도, 아무도 안 타도 켜져 있을 수 있다 |
| Visual 을 항상 만들고 보이기만 토글 | **기각.** `IsActive` 가 전송되지 않으면 똑같다 |

---

## 5. 하마터면 놓칠 뻔한 것 — 클램프의 채널이 바뀌었다

구조체와 Rpc 를 고정쇠 옆에 끼워 넣으면서, 원래 `RpcApplyClamps` 를 꾸미던
`[ClientRpc(channel = Channels.Unreliable)]` 가 **새 함수로 옮겨가고** 클램프에는 내가
새로 쓴 `[ClientRpc]`(신뢰 채널)가 붙었다.

**남의 기능의 전송 방식을 조용히 바꾸는 변경**이었다. `git diff` 에서 `+` 만 있고 `-`
가 없는지 확인해 되돌렸다.

> **교훈**: 기존 선언 **사이에** 끼워 넣을 때는 어트리뷰트가 누구 것인지 확인한다.
> 삽입 지점이 `[Attribute]` 와 `void Method()` 사이면 그 어트리뷰트는 새 것에 붙는다.

---

## 6. 검증

**`FieldNetworkStateTests` 12개** 신설.

| 검증 | 무엇을 잠그나 |
| --- | --- |
| **`AParticipantTurnsTheFieldOnFromTheServerState`** | **이 이슈의 핵심** |
| `AndTurnsItOffAgain` | 끄는 것도 온다 |
| **`TurningItOnDoesNotGiveTheParticipantPhysics`** | **참가자는 그림만** — 판정이 두 곳이 되지 않는다(`-383`) |
| `TheHostStillDrivesPhysicsWhenOn` | 호스트 경로 회귀 방지 |
| **`TheServerActuallySendsIt`** | **보내는 배선이 있다**(호출부로 범위를 좁혀서) |
| `ItSendsWhenSomethingChanged` | 바뀌면 보낸다 |
| `ItStaysQuietWhenNothingChanged` | 안 바뀌면 조용하다 |
| `ButItResendsOnTheKeepalive` | 조용해도 가끔 다시 |
| `ANewVehicleCountCountsAsAChange` | 탈것이 늘면 보낸다 |
| `ADifferentVehicleNumberCountsAsAChange` | 번호가 달라지면 다른 이야기다 |
| **`TheNumberRoundTripsToTheSameVehicle`** | **보내는 번호로 받는 쪽이 같은 탈것을 되찾는다**(`-434` · §7-1) |
| `AnOutOfRangeNumberFindsNothing` | 못 찾은 번호는 건너뛴다 |

### 6-1. 내 검증 둘이 아무것도 못 잡았다

**첫 대조에서 넷 중 둘이 0개였다.** 둘 다 내 검증이 틀린 것이다.

**하나 — 파일 전체에서 글자를 찾았다.**

```csharp
Assert.That(TestSource.Blank(sync), Does.Contain("RpcApplyFields"));   // 처음
```

보내는 **호출을 지워도** 메서드 **정의**에 그 글자가 남아 통과했다. 글자를 보는 검증은
**범위를 좁혀야 뜻이 산다** — `TestSource.Method(sync, "void SendFromServer()")` 안에서
`"RpcApplyFields("` 를 찾도록 바꾸니 잡힌다.

**둘 — EditMode 에서 관찰할 수 없는 것을 쟀다.** "같은 값이 두 번 와도 다시 세우지
않는다" 를 `LowGravityFieldVisual` 이 같은 인스턴스인지로 확인했는데, EditMode 는
`Application.isPlaying` 이 거짓이라 **그 컴포넌트가 아예 안 만들어진다.**
`AreSame(null, null)` 이라 어떤 구현에서도 통과한다. **그 검증을 지웠다** —
가드 자체는 남겼지만(0.5초마다 `Initialize` 를 다시 부르지 않기 위해) 검증이 그것을
잠근다고 적지 않는다.

> **교훈**: 대조 실험에서 0개가 나오면 **제품이 아니라 검증을 먼저 의심한다.**
> 이 프로젝트에서 반복해 겪은 모양이고(`-394`·`-400`·`-438`·`-448`·`-489`), 이번에는
> 내가 쓴 검증에서 두 개가 한꺼번에 나왔다.

### 6-2. 대조 실험

고친 뒤 넷 다 잡힌다.

| 대조 | 결과 |
| --- | --- |
| 보내는 호출을 없앤다(= 병합 전) | **1개 실패** — `TheServerActuallySendsIt` |
| 받은 값을 장에 안 넣는다 | **1개 실패** — `TheServerActuallySendsIt` |
| 참가자도 물리를 돌게 한다 | **1개 실패** — `TurningItOnDoesNotGiveTheParticipantPhysics` |
| 바뀌지 않아도 매 틱 보낸다 | **1개 실패** — `ItStaysQuietWhenNothingChanged` |
| `VehicleAt` 이 한 칸 어긋나게 돌려준다 | **1개 실패** — `TheNumberRoundTripsToTheSameVehicle`(§7-1) |

### 6-3. 기준선 대조

| | 검증 수 | 실패 |
| --- | --- | --- |
| develop (`7034840`) | 1368 | 17 |
| 이 브랜치 | **1380** (+12) | **17** |

**새로 깨진 검증 없음. 실패 셋이 완전히 같다.**

---

## 7. !417 AI 리뷰에 대한 답

⚠️(머지를 막지 않음) 판정이었고 **두 건을 받아들여 고쳤다.**

### 7-1. 번호 왕복 검증이 아무것도 잠그지 못했다

> 이 테스트는 `FieldState.Vehicle` 필드의 존재·타입·기본값 0만 확인한다. '보내는
> 번호가 `VehicleAt` 이 되찾는 번호와 같다' 는 주장(-434 재발 방지)은 전혀 잠그지
> 못하며, **잘못된 색인을 넣어도 통과한다.**

**정확하다.** §4-1 에서 `-434` 를 근거로 들어 놓고, 정작 그 주장을 재는 검증이 없었다.

공개 API 둘(`IndexOfTracked` · `VehicleAt`)로 **왕복**을 재도록 바꿨다. `Collect()` 가
비공개라 리플렉션으로 부른다 — 짝짓기 목록을 채우는 유일한 경로다.

대조로 확인했다: `VehicleAt` 이 한 칸 어긋나게 돌려주도록 고치면
`TheNumberRoundTripsToTheSameVehicle` 이 실패한다. **이제 실제로 잠근다.**

### 7-2. 부작용 있는 게터를 서버 루프에서 두 번 읽었다

> 전송 틱마다 탈것 수만큼 `vehicle.Field` 를 두 번 읽는데, MR 본문 스스로 이 게터가
> 매번 `Configure` 를 호출한다고 적고 있다.

**정확하고, 내가 본문에 적어 놓고도 코드에서는 그렇게 했다.**

```csharp
Active = vehicle != null && vehicle.Field != null && vehicle.Field.IsActive,   // 이전
```

한 번만 읽도록 고쳤다. `TransportVehicle.Field` 게터는 부를 때마다 `Configure` 와
`SetPhysicsAuthority` 를 다시 부른다.

### 7-3. 받아들이되 이 MR 에서 하지 않는 것

| 지적 | 판단 |
| --- | --- |
| `TheServerActuallySendsIt` 이 소스 글자라 실제 동작을 보증하지 못한다 | **맞다.** 이 저장소에는 **PlayMode 검증 어셈블리가 없다**(`Tests/Editor` 하나뿐). RPC 경로를 도는 검증은 어셈블리 신설이 필요하므로 **후속 이슈**다. 이 MR 에서는 한계를 검증 주석과 §6-1 에 적어 두었다 |
| 지라 완료 조건이 템플릿 그대로다 | **담당자가 해야 한다** — 내가 지라를 고칠 수 없다. 아래 §8 의 6가지가 그대로 완료 조건이다 |

---

## 8. 남은 것

- **2인 이상 실제 멀티 확인이 아직이다.** EditMode 는 Mirror 의 `[ClientRpc]` 를 부를
  자리가 없어 **받는 쪽 함수까지만** 잰다. 봐야 하는 것:
  1. 호스트가 켜면 **참가자 화면에 빔이 뜬다**
  2. 끄면 사라진다
  3. **안 탄 참가자에게도 보인다** — 장은 탈것에 속하지 탑승자에 속하지 않는다
  4. **늦게 들어온 사람**이 이미 켜져 있는 장을 본다(`WorldSync` 의 `ready` 이후 —
     keepalive 가 0.5초마다 다시 보내므로 될 것으로 보이지만 재야 한다)
  5. 하차할 때 꺼지는 것도 참가자 화면에 반영된다(`TransportVehicle` 897행)
- **장의 모양은 보내지 않는다.** `TransportVehicle.Field` 게터가 매번 `Configure` 를
  불러 기체 위치에서 다시 잡으므로, 탈것 자세가 동기화되는 것으로 따라올 것으로 본다.
  **인게임에서 모양이 맞는지 확인이 필요하다.**
- ~~`vehicleSlots` 가 런타임에 생긴 탈것을 놓치지 않는가~~ — **확인했다. 덮여 있다.**
  `TransportVehicle.AnyActivated` 에 걸린 `OnVehicleActivated` 가 `Collect()` 를 다시
  부르고, `vehicleSlots` 는 그 안에서 채워진다. 새 탈것의 빔도 그때부터 실린다.
