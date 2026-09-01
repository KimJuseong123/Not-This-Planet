# S15P21B202-243 — 방망이 휘두르기 동작과 타격 피드백

방망이를 들고 좌클릭해도 <b>때리는 행동이 보이지 않았다.</b> 팔이 운반 자세 그대로였다.

## 판정은 돌고 있었다

입력 경로를 끝까지 추적했고 <b>정상이었다.</b>

```
PlayerRig:1386  SetActiveToolInput(aim, ...)
PlayerRig:1764  PrimaryPressed = primary && !previousToolPrimary
PlayerRig:1707  suppressToolPrimary = GetToolPrimaryHeld()   // 집을 때 클릭 무시
MeteorBat:89    if (!input.PrimaryPressed) return; → Swing()
```

스윙은 일어났다. 그런데 관측 가능한 것이 하나도 없었다 — `Swung` 이벤트에 구독자가
0개였고, 팔은 매 틱 운반 자세로 잠겨 있었다.

## Animator 로는 할 수 없었다

<b>이 프로젝트에 Animator 가 한 개도 없다.</b> 인형은 물리 래그돌이고 자세를 코드로
잡는다.

```
DriveArm(arm, reaching, aim, ...)   팔을 목표로 뻗는다
HoldToolPose(aim)                   도구 회전을 조준에 맞춘다
MoveCarriedBody(body, aim)          도구를 카메라 앞 고정 거리에 둔다
```

자산 목록의 "휘두르기 애니메이션 미포함"([Art], S15P21B202-187)은 Animator 작업 흐름을
전제한 서술인데 <b>그 작업 흐름이 없다.</b> [Art] 에게 클립을 기다리면 영영 오지 않는다.

## 고친 방법 — 방향 하나만 흔들었다

조사하다 알게 된 것이 이 이슈를 작게 만들었다.

<b>`HoldToolPose(aim)` 이 방향 하나로 도구의 위치와 회전을 <b>둘 다</b> 정한다.</b>

```csharp
Vector3 holdPoint = cameraOrigin + direction * Tuning.ToolHoldDistance;   // 위치
Quaternion targetRotation = Quaternion.LookRotation(direction, up);       // 회전
```

그래서 <b>그 방향을 흔들면 도구가 카메라 주위로 호를 그린다.</b> 그리고 같은 방향을
`DriveArm` 에도 넘기면 <b>팔이 저절로 따라온다</b> — 팔을 따로 움직이는 코드가 필요 없다.

`ToolBase` 에 훅 하나를 두고 `PlayerRig` 에서 두 줄을 바꿨다.

```csharp
// ToolBase
public virtual Vector3 CarryAim(Vector3 aim) => aim;

// PlayerRig
SetActiveToolInput(aim, GetToolUtilityHeld());
Vector3 carryAim = activeTool != null ? activeTool.CarryAim(aim) : aim;
HoldToolPose(carryAim);
DriveArm(Doll.Left, true, carryAim);
DriveArm(Doll.Right, true, carryAim);
```

`MoveCarriedBody`·`HoldToolPose`·`DriveArm` 을 <b>고치지 않았다.</b> 용접기·운반체는
지금 동작이 맞고, 기본 구현이 조준을 그대로 돌려주므로 아무 영향이 없다
(`OtherTools_DoNotMoveTheCarryPose` 가 잠근다).

### 조준 자체는 바뀌지 않는다

`SetActiveToolInput` 이 `ToolAimContext.Direction` 에 <b>원래 조준</b>을 넣는다.
쳐낸 운석이 날아가는 방향(§18.2)이 스윙 위상에 따라 달라지면 플레이어가 겨눌 수 없다.

<b>순서가 중요하다.</b> `CarryAim` 을 `SetActiveToolInput` <b>뒤에</b> 묻는다 — 앞에서
물으면 이번 틱의 클릭으로 시작된 스윙이 한 틱 늦게 움직인다.

## 궤적

`MeteorBat.SwingAim(aim, progress01)` 이 순수 함수다. 상태를 갖지 않으므로 검증이
진행도를 직접 넣어 잠글 수 있다.

| 성질 | 값·근거 |
| --- | --- |
| 시간 | 0.28초. 타격 창(1초)의 3분의 1쯤 — 창 안에서 클릭한 스윙이 창이 닫히기 전에 끝난다 |
| 궤적 | 위 55° → 아래 −35°. <b>위에서 앞아래로 내려찍는다</b> |
| 축 | 조준의 <b>수평 성분</b>. 위·아래를 봐도 같은 궤적이다 |
| 가속 | `t²`. 등속이면 휘두르는 것이 아니라 미끄러지는 것으로 보인다 |

야구처럼 수평으로 돌리지 않는 이유는 목표가 <b>떨어지는</b> 운석이라서다. 위에서
오는 것을 맞이하는 궤적이어야 하고, §18.2 의 "약간의 위쪽 각도를 더해" 반사하는 것과도
맞는다.

수평 성분만 쓰는 것은 `LaunchDirection`(반사 방향)이 §18.2 를 지키는 방식과 <b>같은
규칙</b>이다. 규칙을 두 번 쓰지 않았다.

### 휘두르는 중에 카메라를 돌려도 궤적이 휘지 않는다

스윙을 시작한 순간의 조준을 기억해 쓴다. 따라 휘면 무엇을 쳤는지 알 수 없다.
`TurningTheCameraMidSwing_DoesNotBendTheArc` 가 잠근다.

### 동작이 판정을 좌우하지 않는다

`Swing()` 이 <b>판정을 먼저 하고</b> 동작을 시작한다. 기획 §18.1 이 "완벽한 프레임,
등급과 치명타 판정은 없다" 고 명시하므로, 동작 위상이 판정에 섞이면 타이밍 게임이 된다.

`TheMotionDoesNotChangeTheOutcome` 이 동작 중에 다시 휘둘러도 결과가 같은 것을 본다.
기존 `MeteorBatTests` 17개도 <b>그대로 통과</b>한다 — 판정을 건드리지 않았다는 증거다.

## 피드백

`Puff.Thruster` 로 궤적을 낸다(§18.1 "넓은 에너지 궤적으로 판정을 납득시킨다").
맞았을 때는 맞은 자리에서 한 번 더 낸다 — 맞음과 헛스윙이 구분되어야 대기 1초 동안
클릭이 먹히지 않는 이유를 알 수 있다.

<b>헛스윙도 동작이 돈다.</b> 안 보이면 클릭이 먹혔는지 알 수 없다.

### 소리는 붙이지 못했다

<b>프로젝트에 `AudioSource` 사용처가 0곳이다.</b> 재생 경로 자체가 없어서 타격음을
붙일 자리가 없다. `Swung` 이벤트를 붙일 자리로 남긴다 — 소리 시스템이 생기면 그것을
구독한다([Sound]).

## 검증

`MeteorBatSwingTests` <b>16개 전부 통과</b>. EditMode 전체 <b>440개 중 427 통과</b>.

develop 424 + 새로 16 = 440 이고, develop 단독 통과 411 + 16 = 427 이다 —
<b>늘어난 실패가 없다.</b>

| 검증 묶음 | 무엇 |
| --- | --- |
| 동작 수명 | 시작·절반·끝 · 타격 창보다 짧은지 |
| <b>판정 불간섭</b> | 동작 중에 다시 휘둘러도 결과가 같은지 · 헛스윙도 도는지 |
| 궤적 | 위→아래 · 겨눈 쪽 유지 · 위·아래 봐도 같음 · 정규 방향 · 수직 조준 · 가속 |
| 운반 자세 복귀 | 가만히 있을 때 · 휘두르는 중 · 끝난 뒤 · 카메라를 돌려도 · <b>다른 도구는 그대로</b> |

`PlayerRig` 를 건드렸으므로 전체 스위트가 방어선이다. 실패 13개는 develop 에 이미
있던 것이다(S15P21B202-232 건) — `origin/develop` 만 체크아웃해 13개 전부 거기서 같이
실패하는 것을 S15P21B202-237 에서 확인했다.

## 인게임 확인은 못 했다

빌드가 develop 에서 멈춘다(지형 자산 체크아웃 손상 + 로비 씬 Volume Profile 누락).
실측은 `docs/records/S15P21B202-237-tool-carry-collision.md` 에 있다.

EditMode 로 잠근 것은 <b>궤적 계산과 수명</b>이다. "휘두르는 것이 보인다" 와 "맞았는지
화면만 보고 안다" 는 인게임에서 봐야 한다.

<b>확인할 때 함께 볼 것</b> — 휘두르는 동안 플레이어가 튀거나 끌리지 않는지.
S15P21B202-237 이 같은 종류의 함정(도구가 지면을 파고들어 플레이어를 밀어냄)을
고쳤으므로, 궤적이 도구를 아래로 내려찍는 지금은 그 처리가 특히 필요하다. 237 이
머지되어 있어야 한다.

## 남은 것

| 항목 | 몫 |
| --- | --- |
| 타격음·궤적 사운드 | [Sound] — `Swung` 이벤트가 붙을 자리다 |
| 실제 궤적 VFX | [Art] — 지금은 `Puff.Thruster` 다 |
| 소품·플레이어 타격 | D — 소품과 플레이어만, 파츠·용접부는 §18.3 대로 제외 |
| develop 실패 13개 정리 | 별도 이슈 — 고정못·상호작용이라 내 파트다 |
