# S15P21B202-438 — 운석 흔적이 검은 원으로 남는 것

> 2026-09-11 · 김주성(물리·퍼즐·캐릭터·상호작용·QA) · 브랜치 `S15P21B202-438-puzzle`
>
> 기획 §19 운석 흔적 · §17.4 파도 표 · §17.6 인원 보정

---

## 1. 증상

> "메테오 떨어진 자리에 표시되는 검은색 원 잔상이 남는다" (2026-09-11 플레이 보고)

---

## 2. 무엇인지 — 색으로 갈랐다

후보가 둘이었다. 낙하 예고 원과 분화구 흔적.

| 표현 | 색 범위 |
| --- | --- |
| 낙하 예고 원 | (1.00, 0.55, 0.10) → (1.00, 0.12, 0.05) · 주황→붉은색 |
| **분화구 흔적** | (1.00, 0.35, 0.10) → **(0.12, 0.10, 0.10)** · 주황→**거의 검은색** |

예고는 끝까지 붉다. **검은 원은 흔적이다.**

### 2-1. 색이 5초 만에 굳고 그대로 남았다

`MeteorScarLook.ColorOf(age)` 실측:

| 나이 | RGB | 알파 | 밝기 |
| ---: | --- | ---: | ---: |
| 0초 | (1.00, 0.35, 0.10) | 0.75 | 0.516 |
| 1초 | (0.61, 0.24, 0.10) | 0.64 | 0.333 |
| 2.5초 | (0.38, 0.17, 0.10) | 0.57 | 0.226 |
| **5초** | **(0.12, 0.10, 0.10)** | **0.50** | **0.106** |
| 10초 | (0.12, 0.10, 0.10) | 0.50 | 0.106 |
| 30초 | (0.12, 0.10, 0.10) | 0.50 | 0.106 |

`MeteorScarCoolSeconds` 가 5초라 **그 뒤로는 색도 알파도 변하지 않았다.**
즉 밝기 0.1 의 검은 원이 수명 30초가 끝날 때까지 지면에 박혀 있었다.

### 2-2. 그리고 여러 개가 동시에 깔렸다

**실제 플레이 로그의 실측**:

```
[WorldSync] 움직이는 흔적이 한 묶음에 안 들어간다 — 36개 중 35개만 보낸다
```

**36개가 동시에 살아 있었다.** 상한(`MeteorScarLimit` 200)에는 걸리지 않았다.

수명 30초 대비 파도 표(§17.4) 마지막 구간이 **15초마다 5개**이고 인원 보정(§17.6)이
더 늘린다. 하나하나는 사라지지만 화면에는 늘 수십 개가 깔린다.

그리고 그 개수는 **신뢰 없는 묶음 하나에 들어가지 않는다**(35개만 갔다). 시각 문제가
동기화 대역폭까지 건드리고 있었다.

### 2-3. EditMode 로는 개수를 잴 수 없었다

흔적 수를 재려고 25분치를 돌렸지만 **0이었다**. `OnCollisionEnter` 가 EditMode 에서
돌지 않아 운석이 `Settled` 되지 못하고, 그러면 `Remember` 가 불리지 않는다.
그래서 §2-2 의 로그가 개수에 관한 유일한 실측이다.

### 2-4. 그리고 애초에 사라지지 않았다 — 진짜 원인

위까지가 "**검은**" 의 설명이다. "**잔상**" 은 따로였다.

`MeteorScarDespawnSeconds` 는 **한 번도 동작한 적이 없는 상수였다.**

수명은 `Meteor.Tick` 안의 `_sinceSettled` 가 셌다. 그런데 그 운석을 틱하는
`MeteorSpawner.AdvanceLive` 는 이렇게 되어 있다.

```csharp
// 박힌 운석은 흔적으로 옮기고 더 틱하지 않는다(기획 §19).
if (meteor.Settled)
{
    _live.RemoveAt(i);
    Remember(meteor);
    continue;      // ← meteor.Tick 이 여기서 끊긴다
}
meteor.Tick(deltaTime);
```

정착한 운석은 `_live` 에서 빠지고, `Meteor` 에는 자체 `FixedUpdate` 도 없다. 그래서
`_sinceSettled` 가 늘지 않고 `Expire()` 가 영영 불리지 않는다. `AdvanceScars` 는
`scar.Meteor == null` 일 때만 목록에서 빼므로, **다른 무언가가 운석을 부수지 않는 한
흔적은 영구히 남는다.**

§2-2 의 36개는 여기서 나온 것이다. 30초 수명으로 계산한 "동시에 수십 개" 가 아니라
**시작부터 쌓인 전부**였다.

> **왜 여태 안 드러났나.** 검증은 "수명이 지나면 사라진다" 를 `Meteor` 단독으로
> 확인했다 — 그 경로에서는 `Tick` 을 검증이 직접 불러 주므로 통과한다. 스포너를
> 끼우면 끊기는데, 그 조합을 아무도 재지 않았다. 이번에 추가한
> `TheScarLeavesTheListWhenItsLifetimeEnds` 가 처음으로 그 자리를 밟았다.

**나이를 세는 쪽이 수명도 재게 했다.** 타이머가 두 군데로 갈라져 있던 것이 뿌리다.

```csharp
// MeteorSpawner.AdvanceScars
scar.Age += deltaTime;

if (scar.Age >= _tuning.ScarDespawnSeconds)
{
    scar.Meteor.Despawn();
    _scars.RemoveAt(i);
    continue;
}
```

---

## 3. 고친 것

### 3-1. 식은 뒤 옅어지는 단계를 더했다

```csharp
public static float Opacity01(float age, float coolSeconds, float fadeSeconds)
{
    if (fadeSeconds <= 0f) return 1f;   // §3-4
    if (age <= coolSeconds) return 1f;

    return Mathf.Clamp01(1f - (age - coolSeconds) / fadeSeconds);
}
```

두 단계가 된다 — **식고**(§19 "약 5초 동안 … 약해진다"), 그 뒤 **옅어진다**.

**알파만 뺀다. 색조는 그대로 둔다.** 흰색으로 밝히며 지우면 타다 남은 자리가 아니라
빛나는 원이 되고, 지면색으로 맞추려면 지면마다 값이 달라야 한다. 투명도는 어느
지면에서나 같은 뜻이다.

**선형이다.** 식는 쪽은 `sqrt` 로 앞을 빠르게 했지만 여기는 반대다 — 사라지는 것이
빠르면 결국 툭 끊기는 것으로 보인다.

### 3-2. 수명을 30 → 13 으로 (= 식는 5 + 옅어지는 8)

수명이 `cool + fade` 와 같아야 한다. 더 길면 **보이지 않는 원반이 계속 살아 있어**
전송·상한 계산에 들어가고, 더 짧으면 옅어지는 중에 사라져 툭 끊긴다. 검증이 그
관계를 잠근다.

13초면 후반 파도(15초/5개)에서 동시에 약 4.3개다. 인원 보정(§17.6)은 인원이 적을 때
**줄이기만** 하므로(0.45 / 0.65 / 0.80 / 1.0) 4인이 상한이다.

### 3-3. 페이드를 8 로 잡은 이유 — 쳐낸 소품 수명을 건드리지 않으려고

`MeteorDeflectedDespawnSeconds`(12초)는 흔적 수명보다 **짧아야** 한다
(`TheRollingPropGoesBeforeTheScar` — "플레이어가 처리한 것은 더 빨리 치운다").

처음에는 페이드를 5 로 두어 수명이 10 이 됐고, 그 관계가 뒤집혀 소품 수명을
12 → 8 로 줄였다. **그것이 범위를 넘는 변경이었다** — 소품 수명은 플레이 감각이고
이 이슈는 시각 잔상을 다룬다(!347 AI 리뷰가 짚었다).

**반대로 맞췄다.** 페이드를 8 로 늘려 수명을 13 으로 만들면 12 < 13 이 성립하고,
소품 쪽은 손대지 않는다. 옅어지는 시간이 길어지는 것은 이 이슈의 목적에도 맞는다.

### 3-4. 페이드 값이 없을 때 불투명을 유지한다

```csharp
if (fadeSeconds <= 0f) return 1f;   // 예전: age >= coolSeconds ? 0f : 1f
```

폴백이 0 이면 페이드 값이 저장되지 않은 씬·프리팹에서 흔적이 **5초에 툭 사라진다.**
"툭 사라지지 않는다" 가 이 이슈의 목적이므로 정반대다(!347 AI 리뷰). 소멸은
수명이 맡으므로 여기서 0 을 돌려줄 이유가 없다.

### 3-5. 표시가 옅어짐을 따라가게 했다 — 여기가 두 번째 함정이었다

`MeteorScarView` 는 `if (!scar.Cooled)` 일 때만 색을 다시 넣었다. 성능 때문이다
(§19 "200개에서 비용이 0"). **그런데 그 조건이 곧 증상이다** — 식은 뒤에는 색을
넣지 않으니 알파가 0.5 에 굳어 검은 원이 남는다.

끊는 지점을 `Cooled` 에서 `Faded`(= `Age >= Cool + Fade`)로 옮겼다. 화면에서
사라진 뒤부터 건너뛰므로 성능 기준은 그대로다.

`AdvanceScars` 의 나이 세기는 **아예 끊지 않는다.** 나이가 멈추면 알파도 멈추고,
§2-4 의 수명 판정도 오지 않는다. 여기서 아끼는 것은 float 덧셈 하나뿐이고, 무거운
쪽(재질에 색 넣기)은 `ShouldRepaint` 가 건너뛴다 — §19 의 성능 기준은 거기서 지킨다.

### 3-6. `Faded` 가 두 뜻이었다 → `ColorIsFinal` 하나로

같은 이름이 두 군데에 서로 다른 계산으로 있었다.

| | 계산 | 페이드가 0 일 때 |
| --- | --- | --- |
| `MeteorScarLook.Faded` | `Opacity01(...) <= 0` | **영영 false** (§3-4 폴백이 1 을 돌려주므로) |
| `MeteorSpawner.Scar.Faded` | `Age >= Cool + Fade` | 식는 순간 true |

뷰가 쓰는 것은 아래쪽이라 **실제 증상은 없었다.** 그래도 합친 이유는, `!347` AI
리뷰가 위쪽을 보고 "흔적 200개에 매 프레임 색을 넣는다" 고 판단했기 때문이다.
**리뷰어가 걸린 지점이면 사람도 걸린다.**

계산을 시간 기준 하나로 두고 `Scar` 가 그것을 부르게 했다. 이름도 바꿨다 — 뷰가
알고 싶은 것은 "보이지 않는가" 가 아니라 **"색이 더 변하지 않는가"** 다.

```csharp
public static bool ColorIsFinal(float age, float coolSeconds, float fadeSeconds) =>
    age >= coolSeconds + Mathf.Max(fadeSeconds, 0f);
```

### 3-7. 풀 원판을 흔적에 묶었다

원판은 **목록 번호로** 배정된다. 흔적 하나가 빠지면 뒤가 당겨져 같은 번호에 다른
흔적이 온다. 그런데 `ShouldRepaint` 가 다 옅어진 흔적을 건너뛰므로, 새로 온 흔적이
이미 옅어져 있으면 **앞 흔적의 색이 그대로 남는다.**

지금은 수명이 끝나는 순간 목록에서도 빠져 그런 흔적이 없다 — **우연히** 성립한다.
씬에서 수명만 늘리면 곧바로 깨진다.

```csharp
if (ShouldRepaint(scar) || marker.Bound != scar)
    marker.Material.SetColor("_Color", ColorFor(scar));

marker.Bound = scar;
```

`Hide` 에서는 `Bound` 를 비운다 — 소멸한 흔적을 풀이 계속 붙들고 있을 이유가 없다.

---

## 4. 씬에 저장된 값이 코드 상수를 덮고 있었다

**상수만 고쳤으면 게임에서는 아무것도 바뀌지 않았을 것이다.**

`MeteorTuning` 은 `[Serializable]` 로 `MeteorSpawner` 에 직렬화되고, 씬에 값이
저장돼 있으면 그것이 코드 기본값을 덮는다. 실제로 이렇게 들어 있었다.

```yaml
# Main.unity · Stage1_Astra.unity
_scarDespawnSeconds: 30   # → 13
_scarCoolSeconds: 5
```

두 씬을 텍스트로 고쳤다 — 수명을 **13** 으로, 그리고 **새 필드
`_scarFadeSeconds: 8` 을 명시**했다. 직렬화 데이터에 없는 필드가 이니셜라이저 값을
받는지는 유니티 버전에 따라 다를 수 있어, 0 으로 읽히는 것을 막았다(0 으로 읽혀도
§3-4 의 폴백이 불투명을 유지하므로 검은 원이 갑자기 사라지지는 않는다).

diff 는 각 씬에서 **2줄 추가 · 1줄 삭제**다. 씬 전체가 재직렬화되지 않았다.

---

## 5. 검증

**`MeteorScarFadeTests` 16개** 신설. `MeteorScarLook` 은 상태 없는 순수 계산이라
시간을 직접 넣어 잠근다.

| 검증 | 무엇을 잠그나 |
| --- | --- |
| `WhileCooling_ItStaysFullyOpaque` | 식는 동안은 옅어지지 않는다(두 단계가 겹치면 5초 내내 흐리다) |
| `AfterCooling_ItFadesLinearlyToNothing` | 0.875 / 0.5 / 0.25 / 0 (페이드 8초 기준) |
| **`TheBlackDiscDoesNotLinger`** | **다 옅어지면 알파 0 — 증상을 직접 잠근다** |
| `TheFadeIsGradual_NotAStep` | 절반 시점에 절반 |
| `TheFadeChangesOnlyAlpha_NotTheHue` | 색조는 건드리지 않는다 |
| `WithNoFadeTime_ItStaysOpaqueAndLetsTheLifetimeDoIt` | 페이드 값이 없으면 불투명 유지 — 툭 사라지지 않는다 |
| **`TheLifetimeMatchesCoolPlusFade`** | **수명 = 식는 시간 + 옅어지는 시간** |
| `TheLifetimeKeepsTheGroundClear` | 후반 파도에서 동시에 10개 안쪽 |
| `AFreshTuningTakesTheCodeDefaults` | 새 인스턴스가 코드 기본값을 받는다 |
| `EvenWithoutAFadeTime_NoBlackDiscRemains` | 페이드 0 인 옛 조정값에서도 증상이 없다 |
| **`TheViewKeepsRepainting_WhileTheScarIsFading`** | **옅어지는 동안 색을 계속 넣는다** |
| `TheViewStopsRepainting_OnceInvisible` | 색이 다 변한 뒤에는 건너뛴다(성능) |
| `TheViewColorFollowsTheFade` | 뷰가 넣는 색이 단계를 반영한다 |
| `TheWarningRingIsNotTheBlackDisc` | 예고 원과 흔적을 혼동하지 않는다 |

`MeteorScarTests` 에도 하나 더했다.

| 검증 | 무엇을 잠그나 |
| --- | --- |
| **`TheScarLeavesTheListWhenItsLifetimeEnds`** | **수명이 끝나면 스포너 목록에서 빠진다 — §2-4 의 진짜 원인을 잠근다** |

### 5-1. 대조 실험 — 그리고 세 번째가 처음에 실패했다

| 대조 | 결과 |
| --- | --- |
| 알파 페이드 제거(`color.a *= …` 삭제) | 3개 실패 — `TheBlackDiscDoesNotLinger` 포함 ⭕ |
| 수명을 30 으로 되돌림 | 2개 실패 — `TheLifetimeMatchesCoolPlusFade` 등 ⭕ |
| **뷰의 기준을 `Cooled` 로 되돌림** | **처음: 12개 전부 통과** ❌ → 고친 뒤: 1개 실패 ⭕ |
| `AdvanceScars` 의 수명 판정 삭제(= 병합 전 develop 의 상태) | 1개 실패 — `TheScarLeavesTheListWhenItsLifetimeEnds` ⭕ |

**세 번째가 아무것도 잡지 못했다.** 색 계산만 보고 있었고 **그 색을 언제 넣는지**는
아무도 보지 않았기 때문이다. 즉 **증상을 그대로 되살려도 검증이 초록불이었다.**

판단을 `Tick` 안의 조건문에서 순수 메서드로 뺐다.

```csharp
public static bool ShouldRepaint(MeteorSpawner.Scar scar) => scar != null && !scar.Faded;
public static Color ColorFor(MeteorSpawner.Scar scar) => …
```

`-400` 에서 얻은 규칙과 같은 처리다 — **판정 로직을 소유자 안에 두지 않는다.**
검증이 부를 수 있는 자리에 두면 대조가 실제 경로를 지난다.

### 5-2. 의미가 바뀐 기존 검증 셋

내 변경이 깨뜨렸고, **고친 것이 아니라 뜻이 달라진 것**이라 갱신했다.

| 검증 | 무엇이 바뀌었나 |
| --- | --- |
| `ACooledScarStopsAging` → `ACoolingScarKeepsAgingWhileItFades` | 멈추는 지점이 `Cooled` → `Faded` |
| `TheColorStopsChangingAfterItCools` → `AfterCooling_OnlyTheAlphaKeepsChanging` | 색조는 굳고 알파만 빠진다 |
| `TheColorGoesFromHotToCold` | 다 식은 색을 **색조만** 비교(알파는 페이드가 다룬다) |
| `PausingForALaunchDoesNotEraseScars` | 30초를 돌리던 것을 수명 안쪽으로 — 수명이 실제로 흐르게 되어서다. 일시정지를 재는 검증이 수명을 재게 되면 안 된다 |

### 5-3. 기준선 대조

| | 검증 수 | 실패 |
| --- | --- | --- |
| develop (`e4b4585`) | 1152 | 32 |
| 이 브랜치 | **1169** (+17) | **32** |

**새로 깨진 검증 없음. 실패 셋이 완전히 같다.** 남은 32개는 develop 에서 그대로
들고 온 것이다.

---

## 6. 조사 중에 드러난 것 — 참가자 화면에는 검은 원이 아예 없다

**호스트와 참가자가 다른 것을 본다.**

| | 무엇이 보이나 |
| --- | --- |
| 호스트 · 혼자 실행 | `_scars` → `MeteorScarView` 의 **원반** + 박힌 운석 몸통 |
| 참가자 | `_scarGhosts` → **박힌 운석 몸통만** |

`_scars.Add` 는 `Remember` 에만 있고 그것은 `Tick` 경로다. 그런데
`MeteorClockBridge` 는 권위가 없으면 `AgeNetworkPending` 만 부르고 **`spawner.Tick()`
을 부르지 않고 반환한다.** 그래서 참가자의 `_scars` 는 비어 있고 원반이 그려지지
않는다. `ApplyNetworkScars` 가 채우는 것은 `_scarGhosts`(운석 몸통)뿐이다.

그리고 `NetworkScarState` 는 `Id` · `Position` · `Rotation` 만 담아 **나이가 전송되지
않는다** — 원반을 참가자 쪽에서 그리게 만들려면 그것부터 필요하다.

**이 이슈에서는 건드리지 않았다.** 438 의 목적은 호스트 화면에 남는 검은 원을 없애는
것이고, 원반이 13초 안에 사라지면 두 화면의 차이도 그만큼 짧아진다. 다만 **화면
불일치 자체는 남아 있고 별도 판단이 필요하다.**

---

## 7. !347 AI 리뷰에 대한 답

리뷰가 **차단 5건**을 걸었는데 **다섯 건 모두 같은 주장**이었다 — "MR 본문은 씬에
수명 10 · 페이드 5 를 썼다는데 코드 상수는 13/8 이다".

**그 시점의 본문이 낡아 있었다.** 리뷰 잡은 본문을 고치기 전에 시작했고, 리뷰는
`.unity` 를 "바이너리·메타" 로 분류해 **씬 파일을 읽지 못한다.** 그래서 본문만 보고
씬 값을 추정했다. 실제 씬 값은 처음부터 13/8 이다.

```
$ git diff METAGAME/Assets/Scenes/
-    _scarDespawnSeconds: 10
+    _scarDespawnSeconds: 13
-    _scarFadeSeconds: 5
+    _scarFadeSeconds: 8
```

본문은 이미 고쳤다. **씬을 읽지 못하는 리뷰어에게는 본문이 유일한 근거이므로, 씬
값을 바꿀 때 본문을 먼저 맞추는 것이 맞다.**

### 7-1. 그 외에 받아들인 것

| 지적 | 처리 |
| --- | --- |
| `Opacity01` 이 페이드 0 에서 1 을 돌려주므로 `Faded` 가 영영 false → 200개에 매 프레임 `SetColor` | **받아들였다.** 다만 뷰가 쓰는 것은 `Scar.Faded`(시간 기준)라 실제 증상은 없었다. **문제는 같은 이름이 두 뜻이었다는 것** — 리뷰어가 걸린 지점이 사람도 걸릴 지점이다. 하나로 합치고 `ColorIsFinal` 로 이름을 바꿨다(§3-7) |
| `Hide` 가 `Marker.Bound` 를 비우지 않아 소멸한 흔적을 붙들고 있다 | **받아들였다.** `Hide` 루프에서 `null` 로 되돌린다 |
| 검증 주석이 "소멸은 `_sinceSettled` 가 따로 센다" 라고 적혀 실제 구조와 반대 | **받아들였다.** 진짜 원인을 찾기 전에 쓴 문장이다. `AdvanceScars` 가 잰다고 고쳤다 |
| 기록 문서에 "6. 남은 것" 이 두 번, §7 수치가 10/5 | **받아들였다.** 이 개정판에서 합쳤다 |
| 확인 방법의 테스트 이름·개수가 실제와 다르다 | **받아들였다.** 본문 표를 실제 이름과 16개로 고쳤다 |

### 7-2. 확인 질문에 대한 답

- **`AdvanceScars` 의 삭제 루프가 역순인가** — 그렇다.
  `for (var i = _scars.Count - 1; i >= 0; i--)` 이고 `RemoveAt(i)` 뒤 `continue` 한다.
  diff 에 루프 머리가 보이지 않아 나온 질문이다.
- **씬 실제 저장값** — 위 diff 대로 13/8 이다.
- **씬 동시 편집자** — 확인하지 못했다. 사전 선언과 함께 담당자가 확인해야 한다.

---

## 8. 남은 것

- **인게임 확인이 아직이다.** 페이드가 눈에 자연스러운지는 플레이로만 안다. 지금
  8초는 소품 수명 12 와의 관계에서 거꾸로 나온 값이라 **하한에 가깝다.**
- **§19 와의 관계에 [Plan] 확인이 필요하다.** §19 는 "스테이지 동안 사라지지 않는다"
  이고 `-251` 이 그 근거로 소멸을 지웠다. 그런데 §2-4 가 보여주듯 **소멸은 실제로
  동작한 적이 없으므로 지금까지 §19 대로 굴러온 셈이다.** 13초로 줄이는 것은 기획
  변경이다 — 2026-09-02 에 구두로 정했지만 문서에 반영되어야 한다.
- **지라에 남길 것.** 이슈 본문은 "수명을 줄인다" 로 적혀 있는데, 실제로는
  **수명이 없었다**. 수명 타이머를 `Meteor._sinceSettled` → `MeteorSpawner.AdvanceScars`
  로 옮긴 결정과 함께 코멘트로 남겨야 한다(리뷰 지적).
- **씬 값 변경은 사전 선언이 필요하다.** `Main.unity` · `Stage1_Astra.unity` 를
  건드렸다(각 2줄). 팀 채널에 알려야 한다 — 내가 올릴 수 없다.
- **호스트-참가자 화면 불일치**(§6). 원반을 참가자에게도 보이게 할지, 호스트에서도
  원반을 없애고 운석 몸통만 남길지 판단이 필요하다.
- **패킷 초과는 근본적으로 해소된다.** 흔적이 실제로 사라지므로 누적이 멈춘다. 다만
  상한이 여전히 200 이라 극단적인 경우에는 같은 경고가 나올 수 있다.
