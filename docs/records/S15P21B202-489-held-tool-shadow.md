# S15P21B202-489 — 방망이·총을 들면 그 도구의 그림자가 사라진다

> 2026-09-14 · 김주성(물리·퍼즐·캐릭터·상호작용·QA) · 브랜치 `S15P21B202-489-character`
>
> 기획 §10.1 1인칭 시점 · `docs/parts.md` 파트 정의

---

## 1. 무엇이 문제였나

방망이나 용접총을 들면 **그 도구의 그림자가 사라진다.** 놓으면 돌아온다.

**몸 그림자는 멀쩡하다.** 어긋남이 그래서 눈에 띈다 — 캐릭터 그림자는 지면에 있는데
손에 든 방망이만 그림자가 없다.

---

## 2. 실측 — 몸과 도구를 따로 쟀다

후보가 둘이었다(1인칭에서 몸을 감추는 경로 / 도구의 1인칭 사본 경로). 경로마다 따로
재는 프로브를 임시로 만들었다(조사용이라 커밋하지 않았다).

```
몸 1인칭(keepShadow:true)  — 꺼진 렌더러=0  ShadowsOnly=66  On=0
몸 탑승(keepShadow:false)  — 켜진 렌더러=0  꺼진=66
도구 들었을 때 — 원본 forceRenderingOff False → True · 원본 shadowCastingMode On → On
                 · 사본 shadowCastingMode=Off
도구 놓았을 때 — 원본 forceRenderingOff=False shadowCastingMode=On
플래그 독립성  — forceRenderingOff=True 인데 shadowCastingMode=On (서로를 바꾸지 않는다)
```

**몸은 무죄다.** 1인칭에서 렌더러 66개가 전부 `ShadowsOnly` 이고 꺼진 것이 없다 —
메시만 빠지고 그림자는 그대로다. 도구를 드는 것과도 무관하다
(`RefreshDollVisibility` 는 탑승 여부와 시점만 본다).

**도구가 범인이다.** 들면 원본이 `forceRenderingOff = True` 가 되고 놓으면 돌아온다.
증상과 조건이 정확히 같다.

### 2-1. 모드만 봐서는 못 찾는 종류였다

원본의 `shadowCastingMode` 는 **`On` 인 채였다.** 그것만 보면 정상이다. 끄고 있던 것은
**옆에 있는 별도 플래그**이고, 둘은 서로를 바꾸지 않는다.

| | 무엇 | 그림자 |
| --- | --- | --- |
| 진짜 도구 | `forceRenderingOff = true` | **없다** — 어떤 패스에도 안 들어간다 |
| 1인칭 사본 | `shadowCastingMode = Off` | **없다** — 일부러 끈 것이다 |

**두 줄 다 각자는 맞다.** 사본이 그림자를 만들면 안 되는 것은 분명하다 — 사본은
**카메라의 자식**이고 1인칭용 자세·크기로 놓인다(`SetPose` 가 `localScale` 까지
바꾼다). 그 그림자는 얼굴 옆에 거대한 방망이가 드리우는 모양이 된다.

문제는 **진짜 도구를 감추는 방법**이었다. `forceRenderingOff` 는 "이 렌더러를 통째로
쉬게 한다" 이고, 그림자 패스도 패스다.

### 2-2. 같은 프로젝트가 이미 옳은 방법을 쓰고 있었다

`Doll.SetVisible` 이 1인칭에서 몸을 감출 때 쓰는 것이 그것이다.

```csharp
renderers[i].enabled = visible || keepShadow;
renderers[i].shadowCastingMode = visible ? ShadowCastingMode.On
                                         : ShadowCastingMode.ShadowsOnly;
```

그 주석이 이유를 적고 있다 — **"1인칭에서 제 몸을 감출 때 쓴다. 발밑을 읽는 단서가
제 그림자이므로, 메시만 빼고 그림자는 그대로 두는 것이 옳다"**(기획 §10.1).

**몸에 대해 옳다고 판단한 것이 손에 든 물건에는 적용되지 않았다.** 이 이슈는 그
판단을 옮긴 것이고, 새로 고안한 것이 없다.

---

## 3. 고친 것

감추는 수단을 `forceRenderingOff` 에서 `shadowCastingMode = ShadowsOnly` 로 바꿨다.

```csharp
if (visible)
{
    hiddenBefore[i] = originals[i].forceRenderingOff;
    shadowBefore[i] = originals[i].shadowCastingMode;
}

// 원래 통째로 꺼져 있던 것은 계속 꺼 둔다.
originals[i].forceRenderingOff = hiddenBefore[i];

// 메시는 빼고 그림자만 남긴다.
originals[i].shadowCastingMode = visible
    ? ShadowsOnlyFrom(shadowBefore[i])
    : shadowBefore[i];
```

### 3-1. `forceRenderingOff` 를 없애지는 않았다

`hiddenBefore` 가 **"원래 꺼져 있던 렌더러는 그대로 둔다"** 를 지키고 있고 그 뜻은
유지해야 한다. 그림자 모드만으로는 그것을 표현할 수 없다 — 꺼져 있던 부품이 도구를
든다고 갑자기 그림자를 만들면 안 된다.

### 3-2. 없던 그림자를 만들지도 않는다

```csharp
static ShadowCastingMode ShadowsOnlyFrom(ShadowCastingMode before) =>
    before == ShadowCastingMode.Off ? ShadowCastingMode.Off
                                    : ShadowCastingMode.ShadowsOnly;
```

일부러 `Off` 로 둔 렌더러가 있다 — 빔·이펙트가 그렇다(`PlasmaWeldingTool` ·
`MeteorBat` 의 선). **감추는 일이 그림자를 켜는 일이 되면 그것도 틀린 그림이다.**
그래서 감추기 전 모드를 저장해 두고, `Off` 였던 것은 `Off` 로 남긴다.

### 3-3. 고르지 않은 안

| 안 | 판단 |
| --- | --- |
| 사본에 그림자를 켠다 | **기각.** 사본은 카메라 자식이고 1인칭 자세·크기라 그림자가 엉뚱한 자리에 엉뚱한 크기로 생긴다 |
| 도구용 그림자 프록시를 따로 둔다 | **기각.** 진짜 도구가 이미 손에 붙어 제자리에 있다 — 새로 만들 것이 없다 |

---

## 4. 파트를 `[Art]` 에서 `[Character]` 로 정정했다

지라 초안을 `[Art]` 로 냈다. **틀렸다.** `docs/parts.md` 가 태그를 정의하고 있다.

| 태그 | 무엇을 다루나 | 이 이슈인가 |
| --- | --- | --- |
| `[Art]` | 3D 아트 — 모델링, 텍스처, 애니메이션, VFX | **아니다.** 에셋을 하나도 만들지 않는다 |
| `[Network]` | 세션, **동기화**, 매치메이킹 | **아니다.** 혼자 실행해도 난다 |
| `[QA]` | 버그 **탐색·기록·트래킹** | 조사는 QA지만 **고치는 작업**은 코드가 사는 파트다 |
| `[Character]` | 캐릭터 조작 — 컨트롤러, **카메라**, 입력 | **맞다.** 1인칭 카메라에 붙는 표시 모델이다 |

**선례가 결정적이다.** 고칠 파일 `FirstPersonToolVisual.cs` 를 만진 커밋이 하나뿐이고,
그것이 `S15P21B202-444-tool-grip-views`("총·방망이 휴대 위치 및 1인칭 표시 분리")다.
그리고 "보이게/안 보이게" 를 다룬 이슈들이 전부 같은 쪽으로 갔다 —
`-267-character`(기절 3인칭에서 쓰러진 몸이 보이게) · `-305-character`(탑승한 사람의
몸이 남의 화면에서 보이는 것).

`-267` 이 특히 가깝다. **`Doll.SetVisible` 의 `keepShadow` 를 만든 것이 그 이슈**이고,
이번 이슈는 그 판단을 손에 든 물건에 옮기는 것이다.

> **내가 왜 `[Art]` 로 적었나.** "그림자 = 보이는 것 = 아트" 라는 **느슨한 연상**이었다.
> 규약 문장(`parts.md`)에 대보면 맞지 않는다. **태그는 증상이 아니라 고칠 코드가 사는
> 곳을 따른다.**

---

## 5. 검증

**`ToolShadowTests` 10개** 신설.

| 검증 | 무엇을 잠그나 |
| --- | --- |
| **`WhileHeld_TheRealToolStillCastsAShadow`** | **이 이슈의 핵심** — 들고 있어도 그림자가 남는다 |
| `TheFirstPersonCopyNeverCastsOne` | 그림자가 하나뿐 — 사본이 만들면 얼굴 옆에 거대한 방망이가 생긴다 |
| `TheRealMeshIsStillHiddenFromTheCamera` | 메시는 여전히 안 보인다(겹쳐 두 개로 보이지 않는다) |
| `LettingGo_RestoresEverything` | 놓으면 원래대로 — 3인칭 회귀 방지 |
| `ARendererThatWasAlreadyOff_StaysOff` | 숨겨 둔 부품이 나타나지 않는다 |
| `ARendererThatNeverCastShadows_StillDoesNot` | 없던 그림자를 만들지 않는다 |
| `TheBodyKeepsItsOwnShadowInFirstPerson` | **몸 경로를 건드리지 않았다** |
| **`HidingWithoutEverShowing_LeavesTheOriginalAlone`** | **든 적 없이 놓기부터 와도 안전**(탑승·기절 경로 · §5-3) |
| **`ShowingTwiceThenHiding_StillRestoresTheOriginal`** | **들기가 두 번 와도 복원된다**(재장착 경합 · §5-3) |
| `ShowAndHideRepeatedly_DoesNotDrift` | 되풀이해도 값이 흘러가지 않는다 |

### 5-1. 기존 검증 하나도 수단을 재고 있었다

`FirstPersonToolVisualTests.CopyHasOnlyPresentationAndRestoresOriginalVisibility` 가
깨졌다. **이 이슈에서 새로 깨진 검증은 이것 하나다.**

```csharp
view.SetVisible(true);
Assert.IsTrue(renderer.forceRenderingOff);   // 이전
```

**플래그를 직접 단언한다.** 그런데 그 플래그가 곧 이 이슈의 결함이다 — 그림자 패스까지
끄므로 감추는 수단을 바꿔야 했고, 그러자 **동작은 옳은데 단언이 깨졌다.**

검증의 이름이 말하는 것은 "사본은 표시뿐이고 **원본 표시는 되돌아온다**" 이지 "어떤
플래그를 쓴다" 가 아니다. 결과로 재게 바꿨다.

```csharp
static bool DrawnInColorPass(Renderer renderer) =>
    !renderer.forceRenderingOff
    && renderer.enabled
    && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
```

그리고 놓았을 때 **다시 그려지는지**도 함께 잠갔다 — 예전에는 플래그만 봤고, 그러면
"놓았는데 `ShadowsOnly` 로 남아 3인칭에서 도구가 안 보이는" 회귀를 못 잡는다.

### 5-2. 내 검증 하나도 같은 함정에 빠졌다

`TheRealMeshIsStillHiddenFromTheCamera` 를 처음에는 이렇게 적었다.

```csharp
Assert.AreNotEqual(ShadowCastingMode.On, original.shadowCastingMode);
```

대조 실험에서 **병합 전 코드도 이 검증을 깨뜨렸다.** 그런데 그때도 메시는 실제로
감춰져 있었다 — `forceRenderingOff` 가 감추고 있었을 뿐이다. **구현의 모양을 재는
검증은 다른 옳은 구현을 거짓으로 몬다.**

`-448` 에서 같은 것에 걸렸다(소스 글자에 `Accepts` 가 있는지 보던 검증). 그때 얻은
교훈을 이번에는 **내가 쓴 검증에** 적용해야 했다.

**이 이슈에서만 두 번 나왔다** — §5-1 의 기존 검증과 여기. 같은 파일, 같은 플래그,
같은 실수다.

재야 하는 것은 **"카메라 패스에 안 들어간다"** 하나다.

```csharp
var drawnInColorPass = !original.forceRenderingOff
                       && original.enabled
                       && original.shadowCastingMode != ShadowCastingMode.ShadowsOnly;
Assert.IsFalse(drawnInColorPass);
```

고친 뒤 대조 A 는 **진짜 회귀 하나만** 잡는다.

### 5-3. !413 AI 리뷰 — 지적은 옳았고, 막는 것이 이미 있었다

리뷰가 **차단 1건**을 걸었다.

> 저장 없이 `SetVisible(false)` 가 먼저 오면 `shadowBefore` 는 초기값 `Off` 이므로
> 원본의 `shadowCastingMode` 를 `Off` 로 덮어쓴다 — 한 번도 든 적 없는 도구의 그림자가
> 조용히 사라진다. **이 MR 이 새로 만든 경로다.**

그리고 중간 1건 — 들기가 두 번 오면 두 번째가 `ShadowsOnly` 를 "원래 값" 으로 저장해
놓은 뒤에도 3인칭에서 도구가 안 보인다.

**읽어서 반박하지 않고 두 시나리오를 그대로 쟀다. 둘 다 통과한다.** `SetVisible` 셋째
줄의 `if (hidden == visible) return;` 이 저장과 복원이 짝이 되게 막는다 — 그 줄은 이
MR 이전부터 있었고 그대로 두었다.

**그런데 리뷰가 말한 기전은 진짜다.** 가드를 없애는 대조를 돌렸다.

| 대조 | 결과 |
| --- | --- |
| `if (hidden == visible) return;` 삭제 | **2개 실패** — 리뷰가 말한 바로 그 둘 |

> **이 MR 이 그 줄의 무게를 바꿨다.** 예전에는 복원값이 `bool` 이라 짝이 어긋나도
> 무해했다 — `forceRenderingOff` 에 `false` 를 쓰는 것뿐이었다. 그림자 모드로 바꾸면서
> **무해한 최적화가 필요한 보호로 바뀌었다.**
>
> 리뷰가 "재입장 가드도 보이지 않아" 라고 적은 것은 사실과 다르다. 하지만 **틀린 쪽은
> 그 한 줄이고, 위험을 지목한 쪽은 맞다.** 가드에 이유를 적고 검증 셋으로 잠갔다 —
> 다음 사람이 "쓸데없는 최적화" 로 보고 지우는 것을 막는 자리다.

### 5-4. 대조 실험

셋 다 실제로 돌려 확인했다.

| 대조 | 결과 |
| --- | --- |
| 병합 전 상태로(`forceRenderingOff` 로 감춤) | **1개 실패** — `WhileHeld_TheRealToolStillCastsAShadow` |
| 사본에도 그림자를 켠다 | **1개 실패** — `TheFirstPersonCopyNeverCastsOne` |
| 원래 꺼져 있던 렌더러 보호를 없앤다 | **1개 실패** — `ARendererThatNeverCastShadows_StillDoesNot` |
| 재입장 가드를 없앤다 | **2개 실패** — 리뷰가 지목한 두 경로(§5-3) |

### 5-5. 기준선 대조

| | 검증 수 | 실패 |
| --- | --- | --- |
| develop (`7034840`) | 1368 | 17 |
| 이 브랜치 | **1378** (+10) | **17** |

**새로 깨진 검증 없음. 실패 셋이 완전히 같다.**

> 처음 측정에서는 `CopyHasOnlyPresentationAndRestoresOriginalVisibility` 하나가 새로
> 깨져 있었다(18). §5-1 을 고친 뒤 17 로 같아졌다.

---

### 5-6. 내가 기록을 한 번 날렸다

§5-3 과 위 표 세 줄은 **한 번 썼다가 사라졌다.** Unity 가 배치 실행 중에 재직렬화한
에셋을 되돌리는 루프를 쓰는데, 그 필터가 이랬다.

```bash
for f in $(git status --porcelain | awk '{print $2}' | grep -v "Scripts/Runtime\|Scripts/Tests"); do
    git checkout -- "$f"
done
```

`docs/records/...` 가 두 패턴 어디에도 안 걸려 **되돌릴 대상에 들어갔다.** 방금 쓴
기록이 통째로 사라졌고, 이어서 돌린 `.replace()` 들은 `assert` 가 없어 **조용히
아무것도 하지 않았다.** 커밋 메시지와 MR 본문에는 남아 있었으므로 문서만 뒤처졌다.

!413 AI 리뷰가 "문서는 7개라는데 실제는 10개다" 로 잡아 주었다.

> **교훈 둘.** ① 되돌리는 명령은 **지울 것을 열거**해야지 남길 것을 제외하는 식으로
> 쓰면 새 파일이 조용히 휩쓸린다. ② 문자열 치환에 `assert` 를 빼면 실패가 소리 없이
> 지나간다 — 이 프로젝트가 반복해서 겪은 "초록불인데 아무 일도 안 일어남" 과 같은
> 모양이다.

---

## 6. 인게임 확인 — 됐다

2026-09-14 개발 빌드(437.0 MB · 오류 0개)를 담당자가 실행해 확인했다.

> "실행해봤는데 그림자 잘 나온다"

**이것이 이 이슈에서 EditMode 가 재지 못하던 부분이다.** 검증은 렌더러의 플래그가 맞게
서는지만 보고, 그림자가 실제로 지면에 그려지는지는 렌더 결과라 눈으로만 안다.
`!413` AI 리뷰가 두 번 연속으로 이 항목을 짚었고(완료 조건 1·2), 그것이 닫혔다.

**따로 보고받지 않은 것**은 아래 §7 에 남긴다 — 확인했다고 적지 않는다.

---

## 7. 남은 것

- **아래 두 가지는 따로 보고받지 않았다.** 그림자가 나온다는 것은 확인됐지만, 이
  둘은 눈으로 한 번 더 볼 가치가 있다.
  - **3인칭으로 바꿔도 그림자가 하나인가.** 둘로 보이면 원본이 안 감춰진 것이고,
    도구 메시가 안 보이면 놓았을 때 복원이 안 된 것이다. `LettingGo_RestoresEverything`
    이 그 자리를 잠그고 있으므로 회귀 가능성은 낮다.
  - **용접총의 빔에 그림자가 없는가.** `PlasmaWeldingTool` 이 빔 렌더러를 일부러
    `Off` 로 둔다. §3-2 와 `ARendererThatNeverCastShadows_StillDoesNot` 이 그것을
    지킨다.
- **조사 중에 눈에 띈 것(고치지 않음).** `PlayerAppearance.Apply` 가 `keepShadow` 를
  **다시 유도한다** — `surfaces` 를 훑어 `ShadowsOnly` 가 하나라도 있으면 참. 비어
  있거나 전부 꺼져 있으면 거짓이 되고, 그러면 `SetVisible(false, false)` 가 **몸
  렌더러를 전부 끈다**(1인칭인데 몸 그림자가 통째로 사라진다). 지금은 66개가 전부
  `ShadowsOnly` 라 증상이 없다. **상태를 다시 읽어서 의도를 복원하는 모양**이라 읽을
  것이 없을 때 조용히 반대로 간다 — 별도 판단이 필요하다.
