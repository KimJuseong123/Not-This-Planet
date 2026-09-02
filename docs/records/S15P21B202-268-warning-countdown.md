# S15P21B202-268 낙하 예고를 줄어드는 원으로

## 기획과 반대로 바꿨다

§17.2 원문이다.

> 주황색으로 시작해 운석이 가까워질수록 붉게 변하고 **안쪽이 채워진다**.

S15P21B202-241 이 그대로 만들었다.

```csharp
public static float FillOf(in PendingMeteorDrop drop) => drop.Progress01;   // 0 → 1
```

**인게임에서 보고 줄어드는 원으로 뒤집었다**(2026-09-02). 시간이 없어지는 것을 그쪽이 더
잘 읽힌다는 판단이다 — **[Plan] 확인이 필요하다.** 기획을 어긴 것이므로 기록과 검증
양쪽에 그렇게 적었다.

## "워닝사인의 크기가 운석과 같아야" 를 어떻게 읽었나

지금 테두리 지름은 충격 반경 × 2 다.

```
소형  ImpactRadius 2.0m → 지름 4.0m
중형  ImpactRadius 3.5m → 지름 7.0m
운석  MeteorRadius 0.45m → 겉보기 지름 0.9m
```

화면에서 원이 운석보다 **네 배** 크다. 그런데 §17.2 가 테두리를 "실제 충격 범위와 같은
크기" 로 정하므로 4m 가 맞는 값이다 — 어디까지 위험한지를 알리는 것이 그 원의 일이다.

**둘을 나눠 만족시켰다.**

| 무엇 | 크기 | 알리는 것 |
| --- | --- | --- |
| 테두리 | 충격 범위 고정 (4m / 7m) | §17.2 — 어디까지 위험한가 |
| 안쪽 원 | 테두리 → **운석 크기(0.9m)** | 언제 떨어지는가 + 무엇이 떨어지는가 |

```csharp
public static float CountdownScaleOf(in PendingMeteorDrop drop)
{
    var ring = DiameterOf(drop);
    if (ring <= 0f) return 1f;

    var floor = Mathf.Clamp01(MeteorDiameter / ring);
    return Mathf.Lerp(1f, floor, drop.Progress01);
}
```

**0 까지 줄이지 않는다.** 0 이면 마지막 순간에 아무것도 안 보이고, 무엇이 그 자리에 올지도
알려주지 못한다. 운석 크기에서 멈추면 **줄어들다 멈춘 그 자리에 실제 운석이 박힌다** —
"크기가 운석과 같다" 가 그 뜻이다.

중형이든 소형이든 **안쪽 원의 마지막 크기는 같다.** 떨어지는 운석의 크기가 같기 때문이다.
검증이 그것을 잠근다.

## 뷰를 거의 안 건드렸다

`MeteorWarningView.Apply` 가 이미 `diameter * fill` 로 곱하는 구조였다. 그래서 **비율만
바뀌면 뷰는 그대로** 동작한다 — 이 이슈를 3점에서 2점으로 낮춘 이유다.

이름은 정리했다. 더 이상 채우는 것이 아니다.

```
MeteorWarningLook.FillOf          → CountdownScaleOf
MeteorWarningStage.Filling        → Approaching
MeteorWarningView.Marker.Fill     → Inner
FillMaterial · FillAlphaScale …   → Inner…
```

`MeteorWarningStage.Filling` 은 "차오르는 중" 이라는 뜻이었다. 점멸하지 않는 단계를
가리키는 이름이므로 `Approaching` 이 사실에 맞는다.

## 점멸과 색은 그대로 뒀다

| 무엇 | 기획 | 상태 |
| --- | --- | --- |
| `Strikable` 점멸 3Hz | §18.1 "타격 가능한 순간에는 강하게 점멸한다" | 그대로 |
| `Imminent` 점멸 8Hz | §17.2 "마지막 0.5초에는 빠르게 점멸한다" | 그대로 |
| 주황 → 붉게 | §17.2 | 그대로 |

**셋을 함께 바꾸면 무엇 때문에 눈에 띄는지 알 수 없다.** 크기는 *언제*, 색은 *얼마나
급한가*, 점멸은 *지금 칠 수 있는가*다.

## 검증 4개 추가 (기존 1개 뒤집음)

| 묶음 | 무엇 |
| --- | --- |
| 뒤집은 것 | **안쪽이 줄어든다** (`TheInsideFillsUp` → `TheInsideShrinksTowardTheMeteor`) |
| 새로 | **운석 크기에서 멈춘다** · 되돌아 커지지 않는다 · 중형도 같은 마지막 크기 · **테두리는 충격 범위 그대로** |

전체 629개 중 616 통과. 실패 13개는 develop 에 이미 있던 것이다(S15P21B202-232 건).
625 + 4 = 629 — **늘어난 실패가 없다.**

## 인게임 확인

머지 후에 봐야 한다. 그리고 **아직 아무도 이 표시를 인게임에서 본 적이 없다** —
S15P21B202-254 가 "예고 원이 지면 메시 아래에 그려진다" 를 고치는데 그것도 머지 전이다.

그래서 이 결정은 **화면을 보고 다시 볼 가치가 있다.** §17.2 의 "채워진다" 가 실제로는
나을 수도 있고, 그때는 `CountdownScaleOf` 의 보간 방향만 되돌리면 된다.
