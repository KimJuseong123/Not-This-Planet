# S15P21B202-535 — 커서가 클릭점 위에 서서 누를 대상을 가린다

> 2026-09-15 · 김주성(물리·퍼즐·캐릭터·상호작용·QA) · 브랜치 `S15P21B202-535-ui`
>
> `LocalCursorFeedback` · `PingMarker.png`

---

## 1. 무엇이 문제였나

> "현재 커서 클릭되는 부분이 불편한 감이 있다."

커서는 `PingMarker.png`(토끼 귀 달린 지도 핀)를 줄여 팀 색으로 물들인 것이다.
**핀이라 hotspot 이 아래쪽 뾰족한 끝**이었다.

```csharp
Cursor.SetCursor(pressed ? _pressed : _normal,
    new Vector2(CursorSize / 2f, CursorSize - 2f), CursorMode.Auto);
//              ^^^^^^^^^^^^^^^^  ^^^^^^^^^^^^^^^ 가로 절반 · 세로 맨 아래
```

| | 클릭하는 곳 | 그림이 있는 곳 |
| --- | --- | --- |
| 이 커서 | **아래쪽 끝** | 클릭점 **위로** 40px |
| 보통 화살표 | 왼쪽 위 끝 | 클릭점 오른쪽 아래 |

지도에서는 맞는 규칙이지만 마우스 커서로는 반대다. **손이 가리키는 점 위에 그림이 통째로
서 있고**, 토끼 귀 때문에 세로로 더 길다.

---

## 2. PNG 를 돌리면 안 된다 — 월드 핑이 같은 그림을 쓴다

```
LocalCursorFeedback.cs:47   Resources.Load<Texture2D>("PingMarker")   ← 커서
TeamPingHud.cs:31           Resources.Load<Texture2D>("PingMarker")   ← 월드 핑
```

그리고 핑은 세워 두기만 하는 것이 아니다. **화면 밖 대상을 가리킬 때 스스로 회전한다.**

```csharp
if (Mathf.Abs(rotation) > 0.01f)
    GUIUtility.RotateAroundPivot(rotation, point);
```

**핀의 뾰족한 끝이 방향 지시자**다. 원본을 45° 돌리면 가리키는 각도가 전부 어긋난다.

### 2-1. 그래서 커서를 만드는 자리에서만 돌렸다

`CreateCursorTexture` 가 이미 원본을 잘라 크기 맞추고 색을 칠해 **새 텍스처를 만든다.**
그 UV 계산을 끝 기준의 투영으로 바꿨다 — **커서만 돌아가고 핑은 그대로**다. 새 에셋이
필요 없다.

```csharp
float dx = x - tip.x, dy = y - tip.y;
float v = (dx * axis.x + dy * axis.y) / height;
float u = (dx * cross.x + dy * cross.y) / width + 0.5f;
```

각도 0 이면 예전 식과 **실루엣이 정확히 같다**(예전의 반 픽셀 중심 오차를 뺀 뒤) —
`ZeroDegreesMatchesTheOldMappingPixelForPixel` 이 픽셀로 잰다. 처음에는 축 벡터만 재다가
**좌우가 뒤집힌 것을 놓쳤다**(§8).

---

## 3. 칸을 48 로 키웠다

40×40 안에 핀을 세로 35px 로 그렸는데, 눕히면 대각선으로 서서 **한 변에 약 41px** 이
필요하다. 40 에는 안 들어간다.

**보이는 크기는 그대로고 늘어나는 것은 투명한 여백뿐**이다 — 핀 길이(35)는 건드리지 않았다.

| 안 | 판단 |
| --- | --- |
| `CursorSize` 를 **48** 로 | **채택.** 보이는 크기 유지 |
| 핀을 30px 로 축소 | 기각. 커서가 작아진다 |

---

## 4. hotspot 을 그리는 쪽에서 받아 온다

예전에는 좌표가 `SetCursor` 자리에 **따로 적혀** 있었다. 각도를 바꾸면 끝이 가는 자리도
달라지므로, 그렇게 두면 **그림과 클릭점이 조용히 어긋난다.**

```csharp
_normal = CreateCursorTexture(_marker, color, false, out _normalHotspot);
…
Cursor.SetCursor(pressed ? _pressed : _normal,
    pressed ? _pressedHotspot : _normalHotspot, CursorMode.Auto);
```

`PinTip` 이 **네 귀퉁이의 극값**으로 끝의 자리를 정하므로 각도가 바뀌어도 잘리지 않는다.
`Hotspot` 은 텍스처(아래가 0)를 화면(위가 0)으로 **뒤집는다** — 이 한 줄을 빠뜨리면
클릭점이 위아래로 뒤집히는데 눈으로는 알아채기 어렵다.

---

## 5. 각도를 고르기까지 — 시안을 실제로 구웠다

SVG 로 그린 시안이 **실제와 달랐다.** 매끈한 벡터로 그리니 괜찮아 보였는데, 48px 텍스처로
구워 보니 다른 그림이었다.

그래서 **제품 코드가 만드는 텍스처를 그대로 PNG 로 뽑아** 보고 골랐다.

```
0도   hotspot=(23.50, 41.00)   ← 지금. 유일하게 토끼로 보인다
100도 hotspot=( 6.27, 20.46)
110도 hotspot=( 7.06, 17.51)
120도 hotspot=( 8.34, 14.75)
135도 hotspot=(11.13, 11.13)   ← 채택
```

**눕히면 토끼로는 안 보인다.** 귀가 옆으로 뻗어 100° 는 물고기, 135° 는 리본처럼 된다.
각도를 고르는 문제가 아니라 **핀을 통째로 돌리는 방식의 한계**다.

> **그래도 135° 로 정했다.** 이 이슈가 고치려는 것은 "무엇을 누르는지 가린다" 이고 그것은
> 확실히 해결된다. 토끼 실루엣은 아이콘·핑·캐릭터가 이미 지고 있다.

### 5-1. 고르지 않은 안

| 안 | 판단 |
| --- | --- |
| PNG 자체를 돌린다 | **기각.** 월드 핑이 기울고 화면 밖 방향 지시가 45° 어긋난다(§2) |
| 표준 화살표로 교체 | **기각.** 익숙하지만 토끼가 완전히 사라진다 |
| 화살표 + 토끼 귀 | **기각.** 48px 에서 귀가 4~5px 이라 잎사귀로 읽힌다 — 시안 4벌을 그려 확인했다 |
| 당근 | **기각.** 끝은 자연스럽게 뾰족하지만 기존 아트와 이어지지 않는다 |
| 세운 채 hotspot 만 옮긴다 | **기각.** 그림과 클릭점이 어긋난다 |
| 100~120° | **기각.** 눕는 정도만 다르고 토끼로 안 보이는 것은 같다 |

---

## 6. 호스트만이 아니라 모두에게 적용된다

> "호스트 말고 참가자들도 다 각자 적용되게 해야 한다?"

**이 건은 보낼 값이 없다.** 커서는 각자 기계의 OS 호출(`Cursor.SetCursor`)이고, 설치는
모든 기계에서 돈다.

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
static void Install()
{
    if (Application.isBatchMode || _instance != null) return;   // 호스트 조건 없음
```

이번 주의 `-505`·`-511`·`-513`·`-519` 와 다른 종류다 — 그쪽은 **서버가 판정한 결과를
보내야** 했고, 이쪽은 애초에 각자 판단한다. 색도 각 기계가 **자기 `NetPlayer`** 에서
읽는다(`isLocalPlayer` 는 "여럿 중 내 것을 고른다" 는 뜻이지 권한 게이트가 아니다).

**그래도 검증으로 못 박았다** — `EveryMachineGetsItsOwnCursorNotJustTheHost` 가 설치와
갱신에 서버 조건이 붙지 않는 것을 지킨다. 규칙이 지켜지는지를 사람 기억에 맡기지 않는다.

---

## 7. 검증

**`CursorPinAngleTests` 15개** 신설.

| | 검증 수 | 실패 |
| --- | --- | --- |
| develop (`origin/develop` 실측) | 1512 | 17 |
| 이 브랜치 | **1528** (+16) | **17** |

**새로 깨진 검증 없음. 실패 셋이 완전히 같다.**

### 7-1. 대조 실험 6개가 전부 잡는다

| 대조 | 결과 |
| --- | --- |
| 수직축을 거울로 되돌린다 | **1개** — `TheTransformIsARotationNotAMirror`(§8) |
| 각도를 0 으로 되돌린다 | **3개** — `ThePinLeansSoItsTipPointsUpAndLeft` · `TheDrawingIsDiagonalNotUpright` · `TheClickPointSitsAtTheTopLeftOfTheDrawing` |
| 칸을 40 으로 되돌린다 | **2개** — `TheDrawingDoesNotTouchTheEdge` · `AnyAngleStillFitsInsideTheCursor` |
| hotspot 의 y 뒤집기를 뺀다 | **3개** — `TheHotspotFlipsFromTextureToScreenCoordinates` · `TheClickedPixelIsActuallyDrawn` · `TheClickPointSitsAtTheTopLeftOfTheDrawing` |
| 끝을 칸 모서리에 붙인다 | **5개** — 잘림 검증 넷 + 기존 `PressedCursor_IsSmallerAndBrighter` |
| 예전처럼 좌표를 따로 적는다 | **1개** — `TheCursorIsSetWithTheHotspotItWasDrawnWith` |

### 7-2. 기존 검증 하나를 고쳤다

`LocalCursorFeedbackTests` 가 칸 크기를 **숫자로** 단언하고 있었다.

```csharp
Assert.That(texture.width, Is.EqualTo(40));   // 이전
```

칸을 48 로 키우면 깨지는데, **동작이 아니라 상수를 재는 단언**이다. `CursorSize` 를 공개해
그 상수를 따라가게 했다 — 각도를 만질 때마다 멀쩡한 검증이 깨지지 않는다.

`PressedCursor_IsSmallerAndBrighter`(누르면 작아지고 밝아진다)는 **회전 뒤에도 그대로**
통과한다.

---

## 8. !449 AI 리뷰에 대한 답 — ⛔ 차단 1건

**차단 판정을 받았고, 지적이 맞았다.**

> `PinCross` 가 `(-axis.y, axis.x)` 라 `(cross, axis)` 기저의 행렬식이 `-1` 이다 —
> 회전이 아니라 좌우 반전이 섞인 변환이다.

**읽고 반박하지 않고 쟀다.**

```
0도 axis=(0.00, 1.00) cross=(-1.00, 0.00) 행렬식=-1.000
dx=+5 일 때 u  새=0.2619  예전=0.7381  같은가=False
원본 좌우 비대칭 픽셀 0 / 88763 = 0.00%
```

**세 줄 다 리뷰가 옳다는 쪽이다.** 행렬식이 −1 이고, 0° 에서 `u` 가 예전과 **부호가
반대**이며, 내가 코드와 문서에 적은 "각도 0 이면 예전 식과 같은 값이 나온다" 는
**거짓이었다.**

`cross` 를 `(axis.y, -axis.x)` 로 바꿔 행렬식을 `+1` 로 만들었다.

### 8-1. 왜 못 봤나 — 그림이 완벽 대칭이라 눈에 안 띈다

`PingMarker` 는 좌우 비대칭 픽셀이 **0/88763** 이다. 거울이어도 **보이는 결과가 같다.**
고친 뒤 다시 구워 이전 것과 견주었다.

```
눈에 띄게 다른 픽셀(>32): 0개 = 0.00%
최대 차이: 32/255   ← 가장자리 안티에일리어싱뿐
```

**그래서 눈으로는 절대 못 잡는 종류다.** 좌우 비대칭인 그림으로 바꾸는 날 조용히
뒤집힌 커서가 나갔을 것이다.

### 8-2. 내 검증이 왜 통과시켰나 — 벡터만 쟀다

`ZeroDegreesIsTheOldUprightPin` 이 **축 벡터만** 확인했다. 축은 거울이어도 `(0,1)` 로
같으므로 통과한다. **결과가 아니라 중간값을 재는 검증**이었다 — 이 세션에서 반복해 겪은
모양이고(`-505`·`-530`), 이번에는 AI 리뷰가 먼저 짚었다.

**픽셀로 재는 검증을 넣었다.** 예전 식을 검증 안에서 그대로 다시 계산해 0° 결과와
한 픽셀씩 견준다.

```
예전 식 그대로 73 픽셀 · 반 픽셀 보정 0 픽셀
```

> **73픽셀은 또 다른 발견이다.** 예전 식은 중심을 `size * 0.5` 로 잡았는데 픽셀 중심은
> `(size - 1) * 0.5` 다 — **반 픽셀 어긋나 있었다.** 새 코드는 픽셀 중심에 맞춘다.
> 그것까지 보정하면 **0픽셀**, 즉 실루엣이 정확히 같다.

그리고 **행렬식 자체를 재는 검증**(`TheTransformIsARotationNotAMirror`)을 따로 두었다 —
대조로 확인했다: `cross` 를 거울로 되돌리면 실패한다.

### 8-3. 받아들이되 이 MR 에서 하지 않는 것

| 지적 | 판단 |
| --- | --- |
| 검증 네임스페이스가 `Metagame.Gameplay.Tests` 인데 대상은 UI 다 | **맞다.** 다만 같은 폴더의 기존 검증(`LocalCursorFeedbackTests` 포함)이 전부 그 이름을 쓴다. 이 MR 에서 하나만 바꾸면 오히려 어긋나므로 **후속으로 일괄 정리**가 맞다 |
| `TheClickedPixelIsActuallyDrawn` 이 ±2px 이웃 최대값이라 느슨하다 | **맞다.** 다만 끝은 안티에일리어싱으로 반투명해지는 자리라 정확히 한 픽셀만 보면 잘 깨진다. 느슨함을 알고 남긴다 |

---

## 9. 남은 것

- **인게임 확인.** 텍스처는 실제로 구워 눈으로 봤지만, 실제 마우스로 눌러 본 적은 없다.
  특히 **클릭이 끝 픽셀에서 먹는지**는 사람만 확인할 수 있다.
- **토끼로 안 보이는 것**(§5)은 이 이슈에서 받아들인 손실이다. 커서 전용 그림을 새로
  그리는 것이 근본 해법이고, 그것은 `[Art]` 후속이다.
- `CursorSize` 가 48 이 되면서 **하드웨어 커서 한계(32px)를 더 넘는다.** 이미 40 이라
  넘고 있었으므로 새로 생긴 문제는 아니지만, 소프트웨어 커서로 떨어지는지는 확인이 필요하다.
