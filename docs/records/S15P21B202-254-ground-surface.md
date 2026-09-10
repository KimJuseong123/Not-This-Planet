# S15P21B202-254 지면 판정을 씬과 맞춘다

[문서 안내](../README.md) → [작업 기록](README.md) → 운석·환경

운석 예고·분화구·끼임 탈출에서 서로 달랐던 지면 판정을 정리했습니다. 실제 씬의 지면 구성과 공통 SurfaceMask를 적용한 결과를 기록합니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

<a id="증상-세-개-뿌리-하나"></a>

## 지면 판정 불일치로 발생한 세 가지 증상

인게임 확인(2026-09-02)에서 나온 것과, 그 원인을 좇다 찾은 것이다.

| 증상 | 어디서 |
| --- | --- |
| 낙하 예고 원이 안 보인다 | 인게임 |
| 운석이 제대로 떨어지는지 알 수 없다 | 인게임 |
| **`T` 3초 유지가 아무 일도 하지 않는다** | 이 조사에서 발견 |

셋 다 **코드가 기대하는 지면과 씬의 지면이 다른 것**에서 나왔다.

## 씬의 지면은 이렇다

```
Ground                      y=0     Transform 만 (콜라이더 없음)
 └ ENV_Ground_Arena_50m     y=0.06  MeshFilter · MeshCollider · MeshRenderer
                                    m_Layer: 0 (Default)
```

씬 전체 레이어 분포를 세어 봤다.

```
layer 0  Default        31개
layer 9  Part           15개
layer 12 Interactable   17개
layer 13 Prop            6개
```

**Ground(8) 레이어 오브젝트가 0개다.**

## 저장소에 지면 정의가 셋 있었다

| 어디 | 무엇을 지면으로 봤나 | 결과 |
| --- | --- | --- |
| 플레이어 접지 | `RagdollBuilder.WorldMask` (Default 포함) | **동작했다** |
| 낙하 예고 착탄 지점 | 스폰 영역 transform의 y (=0) | 바닥(0.06)보다 낮게 잡혔다 |
| 끼임 탈출 | `GameLayers.GroundMask` (Ground 레이어만) | **아무것도 못 찾았다** |

하나로 합치는 것이 이 이슈다.

## ① 예고 원이 바닥에 묻혀 있었다

```csharp
Vector3 EnvironmentPoint()
{
    ...
    return new Vector3(x, center.y, z);   // center.y = 0, 지형을 훑지 않는다
}
```

착탄 지점 y=0 + 띄움 0.03 = 0.03 이고 바닥은 0.06 이다. **3cm 차이로 묻혔다.**

**상수로 맞추지 않았다.** 착탄 지점을 실제로 훑는다.

```csharp
Vector3 OnGround(Vector3 point)
{
    var from = point + Vector3.up * PhysicsScale.MeteorGroundProbe;
    return UnityEngine.Physics.Raycast(from, Vector3.down, out var hit,
                                       PhysicsScale.MeteorGroundProbe * 2f,
                                       GameLayers.SurfaceMask,
                                       QueryTriggerInteraction.Ignore)
        ? hit.point : point;
}
```

`PointFor` 한 곳에서 붙인다 — 네 종류 목표(§17.5 플레이어 주변·작업 지역·발사대·환경)가
모두 같은 경로를 지난다.

**못 찾으면 받은 값을 그대로 쓴다.** 예고를 빼면 §17.4 의 공세 개수 표가 어긋나고, 운석은
어차피 물리로 떨어져 무엇에든 부딪힌다 — 예고가 조금 어긋나는 편이 공세가 비는 것보다 낫다.

부수 효과로 `meteor.SetGroundHeight(impactPoint.y)`가 실제 지면을 받는다. 방망이의
착탄 예측(`TryPredictImpact`)이 그 값을 기준면으로 쓴다.

## ② 분화구는 두 번 묻혀 있었다

착탄 지점이 낮은 것에 더해, 흔적이 **박힌 몸통 위치**를 기억하고 있었다.

```csharp
Point = meteor.transform.position,   // Settle 이 반지름 0.6배만큼 지면 아래로 내린 자리
```

§19 의 "절반 이상 지면에 박힌 모습" 을 만드는 그 이동 때문에, 분화구를 거기에 그리면
반드시 묻힌다. `Meteor.ImpactPoint`(박히기 전 지점)를 추가해 그것을 쓴다.

띄움값도 0.01 → 0.02 로 올렸다. 원판 두께가 0.015 라 절반이 지면 아래로 들어갔다.

## ③ 끼임 탈출은 두 겹으로 막혀 있었다

`GroundMask`를 쓰는 곳이 저장소에 한 곳뿐이고 그것이 어제 만든 `SafeGround`
(S15P21B202-248)였다.

```csharp
Physics.Raycast(from, down, out hit, distance, GameLayers.GroundMask, ...)
```

씬에 Ground 레이어가 0개이므로 **영영 실패한다.** `TryFind`가 항상 false를 돌려주고
`WaitingForRoom`만 섰다 — `T`를 3초 눌러도 아무 일이 없었다. **실패도 로그도 없어서
이 조사 없이는 알 수 없었다.**

두 번째 겹은 `IsClear` 다. `~GroundMask`로 겹침을 봤는데 씬의 바닥은 Default 라
마스크에서 빠지지 않았고, **바닥이 곧 장애물**이 됐다.

레이어로 가르는 것을 버렸다. **캡슐을 발보다 5cm 위에서 시작한다** — 어느 레이어에 있든
발밑은 세지 않고, 무릎 높이의 장애물은 그대로 걸린다.

## GameLayers.SurfaceMask

```csharp
public const int SurfaceMask =
    ~((1 << Player) | (1 << PlayerDowned) | (1 << Interactable)
      | (1 << BeamQuery) | (1 << Meteor) | (1 << PartCarried));
```

빼는 것과 이유를 주석에 남겼다.

- **Player · PlayerDowned** — 사람 위에 착탄 지점을 잡거나 사람을 밟고 서지 않는다
- **Interactable · BeamQuery** — 물리 충돌이 없는 질의 전용 레이어다
- **Meteor** — 이미 박힌 운석 위에 예고를 다시 그리지 않는다(§19)
- **PartCarried** — 견인 중인 파츠는 곧 자리를 뜬다

## 씬의 레이어는 건드리지 않았다

`ENV_Ground_Arena_50m`을 Ground(8)로 옮기는 방법도 있었다. 접었다.

- 씬 자산 수정이라 [Level] 소관이다
- 충돌 매트릭스의 Ground 행이 이미 여러 레이어와 맞물려 있다
- **플레이어가 설 수 있는 곳이면 탈출해서 설 수도 있어야 하는 것이 맞다** — 지면 정의가
  둘인 것 자체가 원인이었으므로 코드를 현실에 맞추는 편이 옳다

레이어 정리는 [Level] 과 별 이슈로 남긴다.

## 배운 것

**EditMode 검증이 씬을 대신하지 못한다.** 248 의 검증 15개는 지면을 `GameLayers.Ground`
레이어로 만들어서 전부 통과했다. 씬에는 그 레이어가 없다 — **검증이 코드의 가정을 그대로
복제하면 그 가정이 틀렸을 때 아무것도 잡지 못한다.**

그래서 이번 검증은 지면을 **Default 레이어로도** 만들어 두 조건을 함께 본다.

## 검증 14개

| 묶음 | 무엇 |
| --- | --- |
| 마스크 | Default 포함 · Ground 포함 · 사람·질의·박힌 운석·견인 파츠 제외 |
| 낙하 예고 | **지면(0.06) 위에 옴** · 원판이 바닥보다 위 · 지면 높이를 따라감(12.5m) · 지면 없어도 공세 유지 · **사람이 지면이 되지 않음** |
| 분화구 | `ImpactPoint`가 박히기 전 지점 · 흔적이 그것을 기억 |
| **끼임 탈출** | Default 바닥에서 찾음 · Ground 바닥도 그대로 · **발밑이 장애물이 아님** · 몸 높이 장애물은 막음 · **실제로 옮겨짐** |

전체 574개 중 561 통과. 실패 13개는 develop에 이미 있던 것이다(S15P21B202-232 건).

## 인게임 확인

이 수정의 결과는 인게임에서 봐야 한다 — 예고 원이 보이는지, 분화구가 보이는지,
`T` 3초가 실제로 옮기는지. EditMode로 확인한 항목은 **높이와 마스크 계산**이다.
