# S15P21B202-513 — 피가 닳아도 빨간 테두리가 참가자에게만 안 보인다

> 2026-09-14 · 김주성(물리·퍼즐·캐릭터·상호작용·QA) · 브랜치 `S15P21B202-513-network`
>
> 기획 §20.1 체력 없는 상태 기반 피해 · `docs/network-sync-policy.md` §3

---

## 1. 무엇이 문제였나

> "피가 닳으면 빨간 테두리가 안 보임 (호스트만 됨)"

---

## 2. 실측

프로브를 임시로 만들어 쟀다(조사용이라 커밋하지 않았다).

```
테두리   — Health01 을 읽나=True  임계값 0.001 로 끊나=True
호스트   — Health01 1.000 → 0.600 (테두리 진하기 = 1 - Health01 = 0.400)
전송     — NetPlayer 의 'health' 등장 0회 · WorldSync 의 'health' 등장 0회
받는자리 — SetRemoteHealth/ApplyNetworkHealth 있나=False
           (SetRemoteDowned · SetRemoteRiding · SetRemoteBeingRescued 는 있다)
참가자   — ServerAuthority=False 인데 ApplyDamage 로 Health01 1.000 → 0.700
```

**셋째·넷째 줄이 결정적이다.** 전송도 없고 받는 자리도 없다. 다른 셋은 **있는데** 체력만
없다.

**다섯째 줄이 새로 나온 것이다.** `ApplyDamage` 에 권한 게이트가 없어서, 참가자 기계에서
트리거가 닿으면 **자기 체력을 로컬로 깎는다.**

### 2-1. 운석은 이 경로가 아니다

운석 직격은 `ApplyDamage` 를 쓰지 않는다 — `Meteor.cs:912` 가 곧바로 `KnockDown` +
`GoDown` 을 부르고 `downed` 는 전송된다.

그래서 참가자에게 체력은 사실상 **"가득" 아니면 "기절" 둘뿐**이었다. **빨간 테두리가
표현하려던 그 중간이 통째로 비어 있었다.**

---

## 3. 왜 이렇게 되는가

| 단계 | 무엇 |
| --- | --- |
| 테두리 | `GameHud.HealthFrame` 이 `Local.Health01` 을 읽고, `1 - Health01 <= 0.001` 이면 곧바로 반환 |
| 체력을 깎는 곳 | `DamageSource`(씬 프롭 트리거) · `Stage1DustStormFragment` — **둘 다 권한 게이트 없음** |
| 전송 | **없었다** |

### 3-1. 갈라질 수도 있었다

| 상황 | 참가자 화면 |
| --- | --- |
| 서버에서 깎임 (일반) | 테두리 **안 보임** — 값이 안 옴 |
| 참가자 기계에서 트리거가 닿음 | 테두리 **보이는데 서버와 다름** |

**`IsDead`(= `Health <= 0`)도 같이 어긋난다.** `DamageSource` 와
`Stage1DustStormFragment` 가 그 값으로 피해를 거르므로, 서버가 기절시킨 사람을 참가자
기계에서 계속 때릴 수 있다.

---

## 4. 고친 것 — 둘이 짝이다

**하나만 하면 반쪽이다.** 전송만 붙이면 두 값이 싸워 깜빡이고, 게이트만 두면 영영
안 뜬다.

### 4-1. 체력을 전송한다

`-448` 의 `rescueProgress` 와 같은 자리·같은 방식이다.

```csharp
[SyncVar] public float health = Core.Tuning.MaxHealth;   // NetPlayer
health = body.Health;                                     // 서버에서, downed 옆
if (!isServer && isLocalPlayer) body.SetRemoteHealth(health);
```

**기본값을 `MaxHealth` 로 둔다.** 0 이면 스폰 직후 값이 오기 전에 **잠깐 죽은 것으로
보인다.**

**내 몸에만 넣는다.** 지금 테두리는 자기 것만 그린다 — 남의 체력을 읽는 곳이 없다.
팀원 체력바가 생기면 그때 넓힌다.

### 4-2. 비율이 아니라 절댓값을 보낸다

`IsDead` 가 `Health <= 0` 이라, **비율만 보내면 그 값이 로컬로 남아 어긋난다.**
`MaxHealth` 는 상수(`Tuning.MaxHealth`)라 받는 쪽에서 비율을 다시 만들 수 있다.

조사 때 "보내는 단위는 구현에서 판단" 으로 남겼던 것이 이것이고, **절댓값이 맞다.**

### 4-3. 참가자가 로컬로 깎지 못하게 한다

```csharp
public void ApplyDamage(float amount, string source = "피해")
{
    if (!ServerAuthority) return;   // 판정은 서버가 한다
    …
}
```

혼자 실행·호스트에서는 `ServerAuthority` 가 참이라 그대로 돈다.

`SetRemoteHealth` 는 **권위가 있는 몸에서 스스로 무시한다** — `SetRemoteBeingRescued`
와 같은 규칙이다(`-383`).

### 4-4. 테두리 판단을 순수 메서드로 뺐다

```csharp
public static float HealthFrameStrength(float health01) =>
    Mathf.Clamp01(1f - health01) <= 0.001f ? 0f : Mathf.Clamp01(1f - health01);
```

`OnGUI` 안의 조건문으로만 두면 검증이 닿지 못한다 — `-438`·`-448`·`-489` 에서 **세 번**
걸린 자리다.

---

## 5. 검증

**`HealthNetworkStateTests` 12개** 신설.

| 검증 | 무엇을 잠그나 |
| --- | --- |
| **`AParticipantTakesTheHealthFromTheServer`** | **이 이슈의 핵심** |
| **`ItAlsoFixesIsDead`** | **절댓값을 보내는 이유** — 비율만 보내면 `IsDead` 가 어긋난다 |
| `HealingComesDownToo` | 회복도 온다 |
| `AnAuthoritativeBodyIgnoresIt` | 권위 있는 몸은 받은 값을 무시한다(`-383`) |
| `TheIncomingValueIsClamped` | 범위를 벗어난 값도 안전하다 |
| **`AParticipantCannotDamageItself`** | **전송과 짝인 게이트** |
| `TheHostStillTakesDamage` | 호스트·솔로 회귀 방지 |
| `EnoughDamageStillDownsTheHost` | 체력이 다하면 쓰러진다(§20.1) |
| **`TheServerActuallySendsIt`** | 보내는 배선이 있다 |
| `AFullBarDrawsNothing` | 가득이면 안 그린다 |
| `DamageMakesItStronger` | 많이 닳을수록 짙어진다 |
| `ANearlyFullBarStillDrawsNothing` | 부동소수 찌꺼기로 깜빡이지 않는다 |

### 5-1. 대조 실험

넷 다 실제로 돌려 확인했다.

| 대조 | 결과 |
| --- | --- |
| 받는 자리를 없앤다(= 병합 전) | **3개 실패** — 핵심 검증 포함 |
| 권한 게이트를 없앤다 | **1개 실패** — `AParticipantCannotDamageItself` |
| 서버가 채우지 않는다 | **1개 실패** — `TheServerActuallySendsIt` |
| 가득인데도 테두리를 그린다 | **2개 실패** — 가득·거의 가득 |

### 5-2. 기준선 대조

| | 검증 수 | 실패 |
| --- | --- | --- |
| develop (`e59cab1`) | 1414 | 17 |
| 이 브랜치 | **1426** (+12) | **17** |

**새로 깨진 검증 없음. 실패 셋이 완전히 같다.**

---

## 6. 남은 것

- **2인 이상 실제 멀티 확인이 아직이다.** EditMode 는 `SetRemoteHealth` 를 직접 불러
  참가자 상태를 **재현**한 것이다. 봐야 하는 것:
  1. 참가자가 피해를 입으면 **자기 화면에 빨간 테두리가 짙어진다**
  2. 회복되면 옅어지고, 가득 차면 사라진다
  3. **스폰 직후 테두리가 잠깐 뜨지 않는가** — 기본값을 `MaxHealth` 로 둔 이유가 그것이다
  4. 호스트·솔로 회귀 없음
- **남의 체력은 보내지 않는다.** 팀원 체력바가 생기면 그때 넓힌다 — `isLocalPlayer` 로
  좁힌 것이 지금은 맞다(`-448` 의 `rescueProgress` 와 같은 판단).
- **`Revive(health)` 가 복원하는 체력**도 같은 값으로 내려간다. 따로 다룰 것이 없어
  보이지만 인게임에서 확인할 가치는 있다.
