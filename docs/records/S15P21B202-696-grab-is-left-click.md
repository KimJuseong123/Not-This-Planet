# S15P21B202-696 · 잡기는 좌클릭이다

> "잡기 관련 UI도 현재 'E'로 뜨는데 이거 좌클릭으로 떠야 함"

## E 로는 아무것도 못 줍는다

```csharp
f.GrabLeft        = Input.GetMouseButton(0);      // 좌클릭 — 왼팔
f.TransportAction = Input.GetKeyDown(KeyCode.E);
```

`TransportAction`(E)을 쓰는 곳을 **전부** 찾으면 두 군데뿐이다.

* `GameSession` — 태블릿 안내 단축키
* `TractorBeamController` — 운반 장치 탑승·하차

물건을 잡는 것은 `DriveArm(..., input.GrabLeft, ...)` → `TryGripTarget` 이다.

## 세 이슈를 거쳐 살아남았다

657 의 관측 7번이 *"태블릿은 구조를 E 라고 적는데 실제 키는 G — 두 화면이 서로 다른
키를 가르친다"* 였다. 구조(G)는 떼어냈는데 **줍기는 E 에 남겨 뒀고**, 669 가 그것을
HUD 로 옮기고 679 가 그대로 따라 적었다.

**검사가 오히려 그 짝을 잠그고 있었다.**

```csharp
// TabletOnboardingTests.MustBeDocumented
("줍기·탑승", Controls.TransportAction),   // ← 틀린 짝이 여기 박혀 있었다
```

`EveryKeyThePlayerNeedsIsInTheHelp` 는 **"그 키 이름이 안내 어딘가에 있나" 만 본다.**
맞는 키인지는 안 본다. 그래서 세 번을 지나갔다.

## 고친 것

| 어디 | 전 | 후 |
|---|---|---|
| `GameHud.InteractionHint` · Grabbable | `[E] 잡기` | **마우스 그림** + 잡기 |
| `GameHud.InteractionHint` · Clamp | `[좌클릭 유지] 고정못을…` | **마우스 그림** + 꾹 눌러 고정못을 뒤로 당기기 |
| `TabletHelpContent.Transport()` | `E … 장비 줍기·교환, 운반 장치 탑승·하차` | `E … 운반 장치 탑승·하차` |
| `TabletHelpContent.Interaction()` | (없음) | `좌클릭 … 장비·파츠 잡기. 놓으면 떨어진다` |

**탑승(`[E] 타기`)은 안 건드렸다.** 그쪽은 원래 맞았고, 고치면서 같이 바꾸면 안 된다 —
검사가 그것도 잠근다.

E 줄은 이제 두 번 남의 것을 내려놓았다. 구조(G)는 657 이, 장비 줍기(좌클릭)는 이번이.

## 마우스 그림은 코드로 그린다

`UiTheme.MouseLeftPixels` — 둥근 사각형 SDF 로 몸통을 그리고(위가 더 둥글게 반경을
섞는다) 위 44% 에 칸막이를 넣어 **왼쪽 윗칸만 밝게** 채운다. 128px 로 그려 24px 로
줄여 쓴다.

**텍스처 에셋이 아니다.** `UiTheme` 가 이미 모든 텍스처를 코드로 만들고, 무엇보다
**아무도 참조하지 않는 에셋은 빌드에서 빠진다** — 은하수 셰이더를 조사하다 방금 본
그대로다. 같은 함정을 안 만든다.

**픽셀 계산을 텍스처와 갈라 뒀다.** 올린 텍스처는 `Apply(makeNoLongerReadable: true)`
라 되읽을 수 없어서, 그러지 않으면 **모양을 검사할 방법이 없다.**

### 그림은 바꿀 수 없는 입력에만 쓴다

키보드 키는 사람이 바꿀 수 있으므로 `Controls` 에서 읽어 글자로 적어야 한다 —
그림으로 박으면 키를 바꾼 사람에게 거짓말이 된다(657 이 고친 것이 그것이다).
마우스 버튼은 바꿀 수 없어서 그림이 안전하다. 검사가 이 규칙을 잠근다.

## 검사 — "있나" 가 아니라 "맞나"

| 무엇을 지키나 |
|---|
| **잡기 안내가 `Controls.TransportAction` 을 안 쓴다** |
| 잡기·고정못 안내가 마우스 왼쪽 클릭 그림이다 |
| **탑승 안내는 그대로 `[E]` 다** |
| 태블릿의 E 줄에 "줍기"·"잡기" 가 없다 |
| 태블릿 어딘가에 `좌클릭 … 잡기` 가 있다 |
| 그림과 키 글자를 같이 넣지 않는다 |
| 그림의 **왼쪽 버튼이 오른쪽보다 밝다** |
| 24px 로 줄여도 좌우가 갈린다 |
| 모서리 바깥이 투명하다 |

`HudNoticeStyleTests` 8건 신규. 관련 스위트 32개(487건) 중 12건 실패는 **clean
develop 에서 같은 필터로 돌려도 같은 12건**이다 — 새로 깨진 것은 없다.

## 고친 검사 넷 — 전부 내가 세운 전제다

* `TabletOnboardingTests.MustBeDocumented` 의 `("줍기·탑승", TransportAction)` —
  **틀린 짝이 박혀 있었다**
* `EveryPromptKeyIsBracketed` → `EveryPromptKeyThatIsTextIsBracketed`.
  "모든 안내에 키 글자가 있다" 는 전제가 그림이 생기면서 깨졌다. 재려던 것은 애초에
  "글자로 적을 때 문법이 하나인가" 였다
* `TheKeyNameMatchesTheTablet` · `RebindingChangesTheHint` — 둘 다 잡기로 재고 있었다.
  **탑승으로 바꿨다.** 잡기는 바꿀 수 없는 마우스 버튼이라 키 이름이 없다

## 계측이 또 틀렸다

그림 검사의 헬퍼에서 `v` 축을 뒤집어 읽었다. 텍스처 배열은 **아래가 0행**(Unity
규약)인데 `1 - v` 로 곱해서, **위를 잰다고 해 놓고 아래를 재고 있었다.** "왼쪽 버튼이
비어 있다(alpha 0.9)" 로 나왔는데 0.9 는 몸통 색이었다 — 값이 딱 떨어져서 드러났다.

## 지나가며 훑은 것

안내 줄 전부를 실제 입력 경로와 대조했다. **틀린 것은 위 둘뿐이다.**

* `WASD` · 달리기 · 점프 · 엎드리기 · 저중력장 · 자유 시점 · 버리기·던지기 ·
  구조(G) · 끼임 탈출(T) · 다시 시작 · 태블릿 · 커서 풀기 — 전부 맞다
* 견인 빔 "좌·우클릭" 도 맞다 (`TractorBeamController` 가 `GrabLeft`/`GrabRight` 를 본다)
* `Controls.cs` 의 `StuckEscape` 주석이 *"그 기능도 아직 배선되지 않았다
  (`StuckEscapeHeld` 를 읽는 곳이 없다)"* 라고 하는데 **669 가 배선했다.** 주석만 낡았다

## 남은 것

* **눈 확인** — 그림이 알약 안에서 24px 로 읽히는지. 값으로는 좌우가 갈리는 것까지만 쟀다
* `Controls.cs` 의 낡은 주석 한 줄 — 이 이슈 범위가 아니라 안 건드렸다
