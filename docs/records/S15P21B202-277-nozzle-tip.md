# 플라즈마 용접기 노즐 기준점 정렬 (S15P21B202-277)

[문서 안내](../README.md) → [작업 기록](README.md) → 도구·캐릭터

용접기의 빔과 용접 판정 시작점을 실제 모델의 노즐 끝에 맞췄습니다. 장착 시 모델과 기존 Muzzle 기준점이 어긋난 원인과 수정·검증을 설명합니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

## 원인 — Muzzle 이 임시 총에 맞춰져 있고, 장착하면 실제 총만 움직인다

```
PF_Stage1_WeldingPlasmaGun
├── AttachPoints
│   ├── Muzzle              (0, 0.08, 0.84)   ← 빔 시작점. 절대 안 움직인다
│   ├── VFXPoint            (0, 0.08, 0.82)
│   └── ReplacementModel
│       └── SF_Stage1_PlasmaWeldingGun        ← 실제 보이는 총
└── PlaceholderVisual                          ← 코드로 만든 임시 총
    ├── Nozzle              (0, 0.08, 0.68)
    └── EnergyTip           (0, 0.08, 0.79)
```

`Muzzle`(z 0.84)의 위치는 **`PlaceholderVisual`의 끝**(`EnergyTip` 0.79, `VFXPoint`
0.82)에 맞춰 만든 값이다. [Art] 가 FBX를 주기 전에 놓은 자리다.

그런데 장착하면 `ApplyHeldVisualPose()`가 **`ReplacementModel` 아래의 실제 총만**
옮긴다.

```csharp
[SerializeField] Vector3 heldVisualLocalPosition = new Vector3(0.7f, -0.9f, 0.25f);
[SerializeField] Vector3 heldVisualLocalEuler = new Vector3(-90f, -80f, 10f);
...
heldVisual = transform.Find("AttachPoints/ReplacementModel/SF_Stage1_PlasmaWeldingGun");
heldVisual.SetLocalPositionAndRotation(heldVisualLocalPosition,
                                       Quaternion.Euler(heldVisualLocalEuler));
```

`Muzzle`은 `ReplacementModel`의 **형제**라 따라가지 않는다.

| | 값 |
|---|---|
| Muzzle 지역 좌표 | (0, 0.08, 0.84) |
| 장착 시 보이는 총의 위치 | (0.7, −0.9, 0.25) |
| 두 점의 거리 | **1.34m** |
| 회전 차 | 약 90° |
| `Tuning.ToolBeamRange` | **0.80m** |

**사거리(0.8m)가 어긋난 거리(1.34m)보다 짧다.** 보이는 총이 닿는 곳과 빔이 닿는 곳이
겹칠 수가 없었다.

## 연출 문제가 아니었다

`Muzzle.position`을 쓰는 곳이 빔만이 아니다.

```csharp
WeldingProgressed = WeldBead.TryGrowNear(Muzzle.position, deltaTime, out var bead);
```

**용접·절단 판정 자체가 총에서 1.34m 떨어진 허공에서 이뤄졌다.** 기획 §16 의 조립이
여기에 걸려 있다 — 플레이어는 보이는 노즐이 아니라 보이지 않는 점을 파츠에 갖다 대야
용접이 됐다.

## 고친 것 — 기준점 하나를 바꿔 읽는 쪽을 하나도 고치지 않았다

`Muzzle`을 읽는 자리가 **빔 · 용접 · 절단 · 파티클**로 여러 군데다. 읽는 쪽마다
고치면 하나를 빼먹는다. 그래서 `ToolBase`에 세터를 두고 기준점 자체를 보이는 모델의
끝으로 갈아 끼웠다.

```csharp
protected void SetMuzzle(Transform replacement)
```

### 총구 축을 추측하지 않는다

FBX 마다 축이 다르고 휴대 자세가 90도 돌린다. 축을 정해 두면 모델이 바뀔 때마다
어긋난다. 빔은 어차피 조준 방향으로 나가므로(§16.3), **그 방향에서 가장 앞선 점**이
눈에 맞는 시작점이다.

```csharp
var reach =
    Mathf.Abs(Vector3.Dot(space.right * (extents.x * scale.x), aim)) +
    Mathf.Abs(Vector3.Dot(space.up * (extents.y * scale.y), aim)) +
    Mathf.Abs(Vector3.Dot(space.forward * (extents.z * scale.z), aim));

return center + aim * reach;
```

경계 상자를 조준 방향으로 밀었을 때의 지지 거리다. **모서리가 아니라 지지면의 중심**을
쓴다 — 모서리를 쓰면 총 옆으로 비켜난 자리에서 빔이 나간다.

### FBX 노드 이름에 의존하지 않는다

`ResolveHeldVisual`의 기존 주석이 "FBX를 다시 내보내 루트 이름이 바뀌더라도" 를 이미
걱정하고 있다. 이름 대신 **렌더러의 경계 상자**를 쓰면 다시 내보내도 살아남는다.

경계 상자는 모델 지역 좌표로 **한 번만** 계산한다 — 총이 손에서 어떻게 돌아가든 모델
안에서의 모양은 바뀌지 않는다.

**꼬리와 파티클 렌더러는 세지 않는다.** 크기가 매 프레임 변해 끝점을 흔들고, 빔이
나가는 자리가 떨리면 조준할 수 없다.

### 끝점을 도구 루트에 매단다

모델 아래에 매달면 자세를 되돌릴 때(`RestoreDroppedVisualPose`) 같이 끌려가 한 프레임
어긋난다.

## 씬·프리팹을 바꾸지 않았다

`Muzzle`을 `ReplacementModel` 아래로 재부모화하면 자세를 따라가게 되지만, **씬·프리팹
변경이라 사전 선언이 필요하고** [Art] 의 FBX 재내보내기와 얽힌다. 팀이 2스테이지 레벨
배치를 하고 있어 지금 프리팹을 건드리면 충돌을 만든다.

[Art] 가 FBX에 끝 노드를 넣어 주기로 하면 그 노드를 우선 쓰고 경계 상자를 대체 수단으로
남기면 된다 — `NozzleGeometry`를 따로 뗀 이유가 그것이다.

## 검증

```
develop : 647개 중 630 통과 / 17 실패
277     : 664개 중 647 통과 / 17 실패

277 에서만 실패 : 없음
```

새 `NozzleTipTests` 17개 전부 통과. 17개 실패는 모두 develop 것이다(S15P21B202-232 건).

`TheStaleAttachPointIsFartherThanTheBeamCanReach`가 **이 이슈가 존재하는 이유를 산술로
남긴다** — 어긋남 1.34m 가 사거리 0.8m 보다 크다는 것이 전제이므로, 누가 휴대 자세를
조정하면 이 검증이 함께 다시 보게 만든다.

<a id="남은-것"></a>

## 남은 작업

- **사거리 0.8m 가 맞는 값인지 재확인이 필요하다.** 지금까지 1.34m 어긋난 상태로
  조정된 값이라면, 제대로 맞춘 뒤에는 너무 짧거나 길 수 있다. 플레이로 봐야 한다
- **`PlaceholderVisual`과 `ReplacementModel`이 둘 다 active 다.** 임시 총이 아직
  화면에 있는지 에디터에서 확인이 필요하고, 있다면 정리한다
- **운석 방망이는 이 문제가 없다.** `PF_Stage1_MeteorBat` 에는 `AttachPoints/Muzzle`이
  아예 없어 `ToolBase.Muzzle`이 툴 루트로 떨어진다. 방망이의 판정은 `Owner.Position`을
  쓰고(§18.1 이 낙하 지점 기준이다) S15P21B202-276 의 궤적도 거기서 그리므로, 어긋나는
  것은 타격 파티클의 위치뿐이다 — 같은 함정이라 기록에 남긴다
