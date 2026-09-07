# S15P21B202-305 운반체에 탑승한 사람의 몸이 남의 화면에서 보이는 것을 고친다

## 증상

운반체에 탑승하면 **다른 유저 화면에서** 토끼 귀가 기체 밖으로 튀어나온다.
탄 본인 화면은 정상이다.

## 원인 — 감추는 조건이 로컬 상태다

`PlayerRig.RefreshDollVisibility` 는 **이미 탑승하면 감추고 있었다.**

```csharp
if (IsRiding) Doll.SetVisible(false);
else if (Cam != null && Cam.FirstPerson) Doll.SetVisible(false, keepShadow: true);
else Doll.SetVisible(true);
```

`Board()` 의 주석도 의도를 적어 두었다 — "조종석 안에 있으므로 몸을 감춘다. …
3인칭으로 강제 전환해도 **기체 밖으로 튀어나온 몸이 보이지 않는다**".

문제는 판단 기준이다.

```csharp
public bool IsRiding { get { return RidingBody != null; } }   // Board() 가 채운다
```

**`RidingBody` 는 로컬에서만 채워진다.** 남의 화면에 있는 몸은
`NetPlayer.BuildRemoteBody()` 가 만든 **별개의 `PlayerRig`** 이고 거기서는 `Board()` 가
돌지 않는다. 그래서 `IsRiding` 이 계속 거짓이고 몸이 그려졌다.

몸통은 조종석 안에 묻혀 안 보이는데 **귀만 기체 밖으로 나왔다.**

## 고칠 재료가 이미 있었는데 아무도 읽지 않았다

```csharp
[SyncVar] public bool riding;      // NetPlayer:45  선언
riding = transport.IsControlling;  // NetPlayer:230 서버가 매 물리 스텝 쓴다
```

전체 코드에서 **읽는 곳이 한 군데도 없었다.** 주석에는 "HUD 가 옳게 그린다" 고 적혀
있는데 HUD 도 읽지 않는다. 값은 이미 모든 클라이언트에 도달해 있었다.

S15P21B202-275 와 같은 유형이다 — **만들어 놓고 연결하지 않았다.**

## 고친 것

훅을 달아 몸에 적용한다.

```csharp
[SyncVar(hook = nameof(OnRidingChanged))]
public bool riding;

void OnRidingChanged(bool _, bool nowRiding) => ApplyRiding(nowRiding);
```

`PlayerRig` 에 원격용 플래그를 하나 둔다. 로컬 규칙과 섞지 않기 위해 별도 이름을 쓴다.

```csharp
if (IsRiding || RemoteRiding) Doll.SetVisible(false);
```

### 몸을 지우거나 비활성화하지 않는다

세 가지 이유다.

1. 자세 동기화가 계속 돌아야 **하차한 자리에 서 있다**
2. 기절 판정과 구조 상호작용(§20)이 몸에 붙어 있다
3. `Doll.SetVisible` 은 1인칭·기절 3인칭에서 쓰던 방법이다 — 새 방법을 만들면 상태가
   두 갈래가 된다

### 그림자도 남기지 않는다

1인칭은 `keepShadow: true` 로 그림자를 남긴다(§10.1 "발밑을 읽는 단서가 제 그림자다").
**탑승 중에는 남기지 않는다** — 조종석 안에 있는 몸의 그림자가 기체 밖 지면에 생기면
몸이 보이는 것보다 더 이상하다.

`Doll.SetVisible(false)` 는 렌더러를 실제로 끄므로 그림자도 사라진다.

### 필드가 아니라 훅 인수를 쓴다

훅이 도는 시점에 필드가 이미 갱신되었는지는 Mirror 버전에 달려 있다. 인수는 어느
버전에서나 새 값이다.

### 몸보다 훅이 먼저 올 수 있다

스폰 메시지가 몸을 세우기 전에 도착하는 경우가 있다(`NetPlayer.Update` 의 기존 주석이
같은 상황을 적어 두었다). 그리고 **훅은 값이 바뀔 때만 돈다** — 이미 탄 채로 접속한
사람에게는 오지 않는다.

그래서 몸을 만드는 자리 세 곳에서도 한 번 더 적용한다.

- `BuildRemoteBody()` — 남의 몸
- `EnsureBody()` 의 방장 몸 인수인계 경로
- `EnsureBody()` 의 서버 몸 생성 경로

## 검증

새 `RemoteRidingVisibilityTests` 11개. `DownedBodyVisibleTests` 와 함께 **26개 전부 통과.**

develop 기준선과 실패 목록을 집합으로 비교했다.

```
develop : 758개 중 733 통과 / 25 실패
305     : 769개 중 744 통과 / 25 실패

305 에서만 실패 : 없음
```

25개는 모두 develop 것이다(`ClampPull` · `TractorBeamController` · `FirstPersonHandView` ·
`PartTractorActivator` · `DustStormSchedule` 등).

### 되돌리는 쪽을 더 많이 검증했다

감추기만 하고 하차에서 되돌리지 않으면 **투명인간이 생기고, 그것은 원래 버그보다
나쁘다.** 그래서 양방향과 반복 탑승·하차를 함께 잠갔다.

- `AfterDismount_TheBodyComesBack`
- `BoardingAndDismountingRepeatedly_StaysConsistent` (4회 반복)
- `TheLocalRidingRuleStillWins` — 서버가 "안 타고 있다" 를 늦게 보내도 1인칭이면 감춘 채
- `FirstPersonKeepsItsShadow_RidingDoesNot` — 두 규칙이 섞이지 않는지
- `HidingDoesNotRemoveTheBody` · `AHiddenBodyStillMoves` — 몸이 살아 있는지

## 남은 것

- **인게임 확인이 필요하다.** EditMode 는 남의 화면을 직접 볼 수 없다. 2인 이상으로
  접속해 한 명이 탑승했을 때 다른 화면에서 몸과 귀가 사라지는지, 하차하면 즉시
  돌아오는지 봐야 한다
- `riding` SyncVar 의 주석이 말하는 "HUD 가 옳게 그린다" 는 아직 구현되지 않았다.
  이 이슈에서는 렌더링만 고쳤다
- 탑승 중 기절이 가능한지(§20) 확인이 필요하다. 가능하다면 감춘 몸이 기절 표시와
  충돌하지 않는지 봐야 한다

## AI 리뷰 대응

**중간 1건 — `Doll` 이 없을 때 플래그만 남는 경로.** `SetRemoteRiding` 이 같은 값이면
바로 돌아나가고 `RefreshDollVisibility` 는 `Doll` 이 없으면 아무것도 하지 않으므로,
인형이 생기기 전에 `true` 가 한 번 들어오면 이후 같은 값이 다시 와도 렌더러가 끝내
꺼지지 않는다 — **원래 증상이 조용히 되돌아온다.**

지금 경로에서는 닿지 않는다. `PlayerRig.Create` 가 인형을 만들고 나서 리그를 돌려주고
(`rig.Doll = RagdollBuilder.Build(...)` 뒤에 `return rig`), `ApplyRiding` 은 `body` 가
없으면 돌아나간다. 그래도 `Doll` 은 public 필드라 불변식이 없고 실패 모드가 원래 버그와
같으므로 조건을 좁혔다.

```csharp
if (RemoteRiding == riding && Doll != null) return;
```

**낮음 — 검증의 전역 로그 무시.** `LogAssert.ignoreFailingMessages` 를 아예 뺐다.
없이도 11개가 모두 통과하므로 필요 없는 설정이었다.

**낮음 — 로컬 소유자에게도 훅이 돈다.** 사실이고, 그대로 둔다. 탑승은 `Board()` 가
`RidingBody` 를 먼저 채워 즉시 감춰지고, 하차는 로컬이 먼저 풀린 뒤 서버 값이 늦게
와도 그 사이에는 `RemoteRiding` 이 참이라 **계속 감춰져 있다** — 늦게 보이는 쪽이
아니라 늦게까지 감춰지는 쪽이고, 둘 중에는 이쪽이 안전하다.

**후속 이슈로 등록할 것** — 아래 "남은 것" 의 HUD 표시와 탑승 중 기절 두 항목은
이슈 키가 아직 없다. 등록 후 이 문서에 키를 적는다.

