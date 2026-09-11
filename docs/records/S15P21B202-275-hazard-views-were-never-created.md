# 운석 예고·분화구 표시의 런타임 생성 연결 (S15P21B202-275)

[문서 안내](../README.md) → [작업 기록](README.md) → 운석·환경

스크립트만 존재하던 운석 예고·분화구 표현을 실제 런타임 생성 경로에 연결했습니다. 생성 누락 원인과 기존 테스트가 놓친 조건, 추가 검증을 기록합니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

## 원인 — 아무도 그 컴포넌트를 만들지 않았다

스크립트 GUID로 `*.unity` · `*.prefab` 전체를 훑었다.

| 컴포넌트 | 씬·프리팹에 붙어 있는 곳 | 런타임 생성 |
|---|---|---|
| `MeteorSpawner` | `Main.unity` | — |
| `SpaceBootstrap` | `Main.unity` | — |
| `Stage1AtmosphereAlert` | 0곳 | `SpaceBootstrap:166` |
| **`MeteorWarningView`** | **0곳** | **없음** |
| **`MeteorScarView`** | **0곳** | **없음** |

```csharp
// SpaceBootstrap.cs:166 — 대기 연출만 붙였다
var atmosphereAlert = sys.AddComponent<Stage1AtmosphereAlert>();
atmosphereAlert.Configure(Atmosphere, FindAnyObjectByType<MeteorSpawner>());
```

그래서 이렇게 되어 있었다.

- **S15P21B202-241**이 원을 그리는 `MeteorWarningView`를 만들었다 → 실행된 적 없음
- **S15P21B202-251**이 분화구 `MeteorScarView`를 만들었다 → 실행된 적 없음
- **S15P21B202-268**이 줄어드는 원으로 바꿨다 → 실행된 적 없음
- **S15P21B202-254**가 예고 지점을 실제 지면 위로 내렸다 → 필요했지만 그것만으로는
  아무것도 보이지 않는다

"운석 워닝포인트가 안보인다" 의 진짜 원인이 이것이다. 254 에서 지면 높이(바닥 메시
0.06 vs 리프트 0.03)를 찾아 고쳤는데, **그리는 컴포넌트가 있는지는 확인하지 않았다.**

<a id="고친-것"></a>

## 변경 내용

**장치는 `MeteorSpawner`, 정책은 부트스트랩.** 어셈블리 경계가 그렇게 나누게 했다.

```
Metagame.Gameplay          → Metagame.Core 만 참조   (Network 를 못 본다)
Metagame.Gameplay.Tests    → Bootstrap 을 참조하지 않는다
```

그래서 `MeteorSpawner` 안에서는 전용 서버인지 알 수 없고, 검증에서는
`SpaceBootstrap`을 볼 수 없다. 나눠 두면 둘 다 풀린다.

```csharp
public int EnsureViews()
{
    var added = 0;

    if (GetComponent<MeteorWarningView>() == null)
    {
        gameObject.AddComponent<MeteorWarningView>().Configure(this);
        added++;
    }

    if (GetComponent<MeteorScarView>() == null)
    {
        gameObject.AddComponent<MeteorScarView>().Configure(this);
        added++;
    }

    return added;
}
```

```csharp
// SpaceBootstrap — 전용 서버는 위에서 이미 돌아나갔다
var meteorSpawner = FindAnyObjectByType<MeteorSpawner>();
if (meteorSpawner != null) meteorSpawner.EnsureViews();
```

**`Awake`로 스스로 붙이지 않았다.** 화면 없는 전용 서버에서 원판 200개를 만들 이유가
없고, 그 판단에 필요한 `MatchRole`은 Gameplay에서 보이지 않는다.

**`useAtmosphere` 안에 넣지 않았다.** 진공에서도 운석은 떨어진다.

## 검증이 이 실수를 잡을 수 없었던 이유

기존 검증은 뷰를 **직접 만들어 붙인다.**

```csharp
// MeteorWarningTests.MakeView
var view = go.AddComponent<MeteorWarningView>();
```

그러면 "그리는 규칙" 은 잠기지만 **"게임에서 아무도 만들지 않는다" 는 늘 통과한다.**
`MeteorWarningTests` 22개가 전부 초록불인 채로 인게임에는 아무것도 안 나왔다.

두 겹으로 막았다.

**① `HazardViewWiringTests`(8개)** — 배선을 본다. `EnsureViews`가 붙이는지, 두 번
불러도 하나씩인지, **손으로 붙이지 않고** 예고와 분화구가 나오는지, 지면 위에 있는지.

**② `SpaceSelfTest.CheckHazardViews`** — 세계에 실제로 있는지 본다. EditMode 로는
잡을 수 없는 종류이므로 세계를 세워 보는 쪽에 둔다.

```csharp
Check(warning != null, "낙하 예고 원을 그리는 것이 존재한다 (§17.2)");
Check(scars != null, "운석 분화구를 그리는 것이 존재한다 (§19)");
```

## 하늘에서 내려오는 선은 넣지 않았다

원래 요청은 "공중에 수직으로 빨간 선을 해당 위치까지 내려찍고" 였다. 그런데
S15P21B202-274 로 **운석이 대각선으로 온다** — 수직 선은 운석이 오는 방향과 다르다.

**원만 남겼다.** 이유가 둘이다.

1. 대각선 선은 화면을 가로질러 시야를 가린다. §17.4 공세 후반에 동시에 다섯 개가
   뜨면 서로의 시야를 지운다
2. 타이밍 정보는 줄어드는 원이 이미 전달한다. 선은 "어디" 만 더하는데 그건 원이
   하는 일이다

## 검증 결과

```
develop : 647개 중 630 통과 / 17 실패
275     : 654개 중 637 통과 / 17 실패

275 에서만 실패 : 없음
```

새 `HazardViewWiringTests` 8개 포함, `MeteorWarningTests` · `MeteorScarTests`와 함께
**45개 전부 통과.** 17개는 모두 develop 것이다(S15P21B202-232 건).

`TheWarningSitsAboveTheGroundSurface`는 씬과 같은 조건(바닥 메시 y=0.06)에서 표시가
그 위에 오는지 본다 — 254 가 고친 것이 실제로 효과가 있는지를 처음으로 확인한 검증이다.

<a id="남은-것"></a>

## 남은 작업

**인게임 확인이 필요하다.**이 표현들은 지금까지 화면에 나온 적이 없으므로, 원 크기 ·
리프트 · 머티리얼 · 점멸 · 분화구 색이 전부 첫 노출이다. 어긋난 것이 나오면 별건으로
잡는다.

- **D** 대각선 낙하에 맞춘 방망이 타격 판정 재확인
- **E** 플라즈마 용접 건 빔이 노즐 끝에서 나가게

색은 이 이슈에서 건드리지 않았다. §17.2 는 "주황색으로 시작해 붉게 변한다" 이고 코드가
그대로 되어 있다 — 실제로 보고 나서 판단한다.
