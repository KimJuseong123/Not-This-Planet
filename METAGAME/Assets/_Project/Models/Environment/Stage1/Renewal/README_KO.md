# Stage1 자연물 · 맵 타일 리뉴얼 에셋

Jira [S15P21B202-163](https://ssafy.atlassian.net/browse/S15P21B202-163)에서 사용할 신규 원본 에셋 모음입니다.
기존 Stage1 자연물과 바닥을 바로 덮어쓰지 않고 `Renewal` 경로에 분리해 두었습니다.

## 현재 상태

- 자연물: FBX와 Base Color, Normal, Metallic, Roughness 텍스처까지 추가됨
- 맵 타일: 1024 × 1024 seamless Base Color 이미지 추가됨
- 프리뷰: 자연물마다 앞·뒤·좌·우 이미지 추가됨
- 미작업: Unity Material, Prefab, Collider, LOD, `Main.unity` 배치 및 기존 에셋 교체

즉, 이 폴더의 파일은 **가공 전 원본 에셋**입니다. 씬에는 아직 연결하지 않았습니다.

## 파일 구성

### 자연물 8종

기준 경로:

`Assets/_Project/Models/Environment/Stage1/Renewal/Nature/`

| 폴더·FBX 이름 | 용도 |
| --- | --- |
| `alien_flower_cluster` | 외계 꽃 군락 |
| `alien_rock_cluster_a` | 외계 바위 군락 A |
| `alien_rock_cluster_b` | 외계 바위 군락 B |
| `alien_rock_flat` | 낮고 평평한 바위 |
| `alien_rock_flat_cracked` | 갈라진 낮은 바위 |
| `alien_rock_oval` | 타원형 바위 |
| `alien_rock_round` | 둥근 바위 |
| `alien_rock_round_cracked` | 갈라진 둥근 바위 |

각 에셋 폴더에는 다음 파일이 있습니다.

```text
<asset_name>/
├─ <asset_name>.fbx
└─ tripo_convert_<id>.fbm/
   ├─ Color.jpg
   ├─ Normal.jpg
   ├─ <asset_name>_metallic.JPEG
   └─ <asset_name>_roughness.JPEG
```

`.fbm` 폴더 이름은 FBX 내부 텍스처 경로와의 호환성을 위해 원본 이름을 유지했습니다.
FBX와 해당 `.fbm` 폴더는 항상 함께 이동해야 합니다.

### 맵 타일

| 용도 | 경로 |
| --- | --- |
| Stage1 신규 바닥 Base Color | `Assets/_Project/Textures/Environment/Stage1/Renewal/Ground/T_Stage1_AlienGroundTile_BaseColor.png` |

이미지 크기는 1024 × 1024이며 반복 배치를 전제로 만든 seamless 텍스처입니다.
현재는 Base Color 한 장만 있으므로 Normal, Mask/Metallic, Height가 필요하면 별도로 제작해야 합니다.

### 프리뷰

문서용 4방향 프리뷰는 아래 경로에 있습니다.

`docs/assets/previews/stage1-renewal/`

각 자연물마다 `_front`, `_back`, `_left`, `_right` PNG가 있으며 총 32장입니다.

## Unity 적용 권장값

### 자연물

1. FBX Import Settings에서 축 방향과 실제 크기를 먼저 확인합니다.
2. `Normal.jpg`는 Texture Type을 `Normal map`으로 지정합니다.
3. Metallic과 Roughness는 sRGB를 끄고 선형 데이터로 사용합니다.
4. Standard 계열 셰이더는 Roughness를 직접 받지 않으므로 `Smoothness = 1 - Roughness`로 변환해 사용합니다.
5. FBX를 씬에 직접 배치하지 말고 Material과 `PF_Stage1_*` Prefab을 만든 뒤 배치합니다.
6. 바위는 단순화한 Box 또는 Convex Collider를 사용하고, 꽃 군락은 장식용이면 Collider를 생략합니다.

### 맵 타일

1. Wrap Mode를 `Repeat`로 지정합니다.
2. Filter Mode와 압축 품질은 플레이 카메라 거리에서 경계가 보이지 않는지 확인하며 조정합니다.
3. 기존 `M_Stage1_ArenaGround`를 즉시 덮어쓰지 말고 신규 Material을 만들어 비교합니다.
4. 50 m × 50 m 바닥 전체에서 반복 무늬, 색 띠, UV 경계가 드러나지 않는지 확인합니다.
5. 바닥 교체 후 기존 MeshCollider와 높이 `y = 0.06 m`가 계속 필요한지 다시 검증합니다.

## 완료 전 확인 사항

- 모델의 라이선스·배포 가능 범위가 원본 파일에 포함되어 있지 않습니다. 외부 배포 전 출처와 이용 조건을 확인해야 합니다.
- 모델의 폴리곤 수, 실제 치수, 피벗, 전방 축은 아직 검증하지 않았습니다.
- 기존 `alien_flower_cluster`와 이름이 겹치므로 신규 Prefab과 Material에는 `Renewal` 또는 구분 가능한 이름을 사용해야 합니다.
- 원본 ZIP은 저장소에 넣지 않았습니다. Unity에서 실제로 사용하는 FBX와 텍스처만 추가했습니다.
