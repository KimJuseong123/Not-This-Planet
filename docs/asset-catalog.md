# Unity 에셋 카탈로그

> 기준 커밋: `develop` **054a2a6** · 작성 2026-08-27 · 이슈 [S15P21B202-80](https://ssafy.atlassian.net/browse/S15P21B202-80)
> 갱신 2026-08-27 — 환경 자연물 연결과 `Main.unity` 아트 배치 · 이슈 [S15P21B202-84](https://ssafy.atlassian.net/browse/S15P21B202-84)
> 갱신 2026-08-28 — Stage1 운석 모델·PBR 텍스처 4종 교체 · 이슈 [S15P21B202-98](https://ssafy.atlassian.net/browse/S15P21B202-98)
> 갱신 2026-08-31 — Stage1 자연물 8종·맵 타일 리뉴얼 원본 추가 · 이슈 [S15P21B202-163](https://ssafy.atlassian.net/browse/S15P21B202-163)
> 관련 문서: [문서 목록](./README.md) · [1장 기획](./planning/chapter-1-plan.md) · [파트 구분](./parts.md)

**어떤 에셋이 무엇이고, 씬에 갖다 쓸 수 있는 상태인지** 한 장에서 확인하는 문서다.
연결 관계는 모두 `.meta` GUID를 역참조해 실측했다 — 파일명으로 추측한 값은 없다.

이 표의 경로는 앞에 `METAGAME/Assets/_Project/`가 붙는다.
예: `Models/Props/Stage1/SF_Stage1_AnchorPin.fbx` →
`METAGAME/Assets/_Project/Models/Props/Stage1/SF_Stage1_AnchorPin.fbx`

---

## 1. 쓰기 전에 알아야 할 것 3가지

**① `PF_*` 프리팹을 쓴다. FBX를 씬에 직접 끌지 않는다.**
Stage1 FBX는 전부 **Z-up으로 제작**되어 Unity에 그대로 넣으면 누워서 들어온다.
프리팹이 `Visual_ZUpToYUp` 자식으로 감싸고 X축 −90° 회전 + 배치용 스케일을 적용해 이 문제를 해결해 둔 상태다.

**② 프리팹에는 Collider·Rigidbody가 없다.**
Stage1 프리팹 5종 모두 루트에 Transform 하나뿐이다. 물리·상호작용 컴포넌트는 배치하는 쪽에서 붙인다.

**③ 실제 배치 크기는 3단 곱셈이다.**

```
실제 크기 = FBX 원본 크기 × .meta globalScale × 프리팹 스케일
```

FBX 파일만 열어봐서는 크기를 알 수 없다. 기존 카탈로그에서 실측한 Tripo3D 모델 13종은 전부 **1m 이하로 정규화**되어 있고,
실제 크기는 `.meta`의 `globalScale`과 프리팹 스케일이 결정한다.

---

## 2. 상태 라벨

| 라벨 | 의미 |
| --- | --- |
| **사용 가능** | Material·프리팹까지 갖춰져 씬에 바로 배치할 수 있다 |
| **미연결** | FBX는 있으나 Material 에셋 또는 프리팹이 없어 그대로 배치할 수 없다 |
| **기존** | 이전 SpaceFlop 프로토타입 에셋. 신규 Stage1 작업에는 쓰지 않는다 |
| **참고** | 제작 참고용. 런타임에 쓰지 않는다 |
| **설정** | URP·입력 설정 또는 Unity 프로젝트 템플릿 잔여물 |

## 3. 전체 요약

| 종류 | 수량 | 상태 |
| --- | ---: | --- |
| FBX 모델 | 26 | 사용 가능 14 · 미연결 11 · 기존 1 |
| 텍스처 (`Textures/`) | 25 | Stage1 PBR 20 · 리뉴얼 바닥 1 · 콘셉트 4 |
| 텍스처 (`.fbm/` 임베드) | 57 | 캐릭터 2 · 환경 7 · 운석 PBR 16 · 리뉴얼 자연물 PBR 32 |
| Material | 14 | 파츠·소품 5 · 환경·바닥 8 · 운석 1 |
| Prefab | 16 | Stage1 14 · 기존 2 |
| Shader | 3 | 기존 SpaceFlop 런타임 전용 |
| Scene | 1 | `Scenes/Main.unity` — Stage1 프리팹 12종이 122개 배치돼 있다 |
| Unity 설정 Asset | 8 | URP 7 · Readme 1 |
| Input Actions | 1 | Unity 기본 액션 맵 |

연결 구조: `Texture → Material → Prefab(FBX 포함) → Scene`

FBX·JPG·PNG는 전부 **Git LFS** 대상이다(`.gitattributes`).

---

## 4. Stage1 우주선 파츠 — 사용 가능 (4종)

퍼즐로 조립하는 우주선 파츠. 4종 모두 구조가 동일하다.

```
PF_Stage1_*.prefab  (루트: Transform 만)
 └─ Visual_ZUpToYUp   X축 −90° 회전 + 균일 스케일
      └─ SF_Stage1_*.fbx  +  M_Stage1_*.mat
```

| 프리뷰 | 파츠 | FBX 원본 | 프리팹 스케일 | **실제 크기** | 삼각형 |
| --- | --- | --- | ---: | --- | ---: |
| <img src="./assets/previews/SF_Stage1_Spaceship_Capsule.png" width="110"> | **Capsule**<br>조종석 캡슐 | 0.83 × 0.85 × 0.98 m | 2.6 | **2.16 × 2.21 × 2.55 m** | 22,480 |
| <img src="./assets/previews/SF_Stage1_Spaceship_HoverPod.png" width="110"> | **HoverPod**<br>부양 추진부 | 0.98 × 0.57 × 0.98 m | 2.35 | **2.30 × 1.34 × 2.30 m** | 19,037 |
| <img src="./assets/previews/SF_Stage1_Spaceship_Robot.png" width="110"> | **Robot**<br>로봇 파츠 | 0.98 × 0.92 × 0.71 m | 2.35 | **2.30 × 2.16 × 1.67 m** | 18,291 |
| <img src="./assets/previews/SF_Stage1_TractorCraft.png" width="110"> | **TractorCraft**<br>견인 장치 | 0.76 × 0.98 × 0.74 m | 2.5 | **1.90 × 2.45 × 1.85 m** | 39,207 |

크기는 W × H × D(미터). `globalScale`은 4종 모두 `1`이다.

### 파일 경로

| 파츠 | Prefab | FBX | Material |
| --- | --- | --- | --- |
| Capsule | `Prefabs/Spaceships/Stage1/PF_Stage1_Spaceship_Capsule.prefab` | `Models/Spaceships/Stage1/SF_Stage1_Spaceship_Capsule.fbx` (698 KB) | `Materials/Spaceships/Stage1/M_Stage1_Spaceship_Capsule.mat` |
| HoverPod | `Prefabs/Spaceships/Stage1/PF_Stage1_Spaceship_HoverPod.prefab` | `Models/Spaceships/Stage1/SF_Stage1_Spaceship_HoverPod.fbx` (572 KB) | `Materials/Spaceships/Stage1/M_Stage1_Spaceship_HoverPod.mat` |
| Robot | `Prefabs/Spaceships/Stage1/PF_Stage1_Spaceship_Robot.prefab` | `Models/Spaceships/Stage1/SF_Stage1_Spaceship_Robot.fbx` (564 KB) | `Materials/Spaceships/Stage1/M_Stage1_Spaceship_Robot.mat` |
| TractorCraft | `Prefabs/Spaceships/Stage1/PF_Stage1_TractorCraft.prefab` | `Models/Spaceships/Stage1/SF_Stage1_TractorCraft.fbx` (1,139 KB) | `Materials/Spaceships/Stage1/M_Stage1_TractorCraft.mat` |

각 Material이 실제로 참조하는 텍스처는 **BaseColor · Normal · Metallic 3장**이다. Roughness는 참조되지 않는다([§11](#11-미해결-목록) 참고).

---

## 5. Stage1 고정못 — 사용 가능 (1종)

| 프리뷰 | 용도 | FBX 원본 | 프리팹 스케일 | **실제 크기** | 삼각형 |
| --- | --- | --- | ---: | --- | ---: |
| <img src="./assets/previews/SF_Stage1_AnchorPin.png" width="110"> | 우주선 모듈을 지면에 고정하는 고정못 | 0.37 × 0.98 × 0.37 m | 0.55 | **0.20 × 0.54 × 0.20 m** | 18,870 |

- Prefab: `Prefabs/Props/Stage1/PF_Stage1_AnchorPin.prefab`
- FBX: `Models/Props/Stage1/SF_Stage1_AnchorPin.fbx` (569 KB)
- Material: `Materials/Props/Stage1/M_Stage1_AnchorPin.mat`
- 텍스처: `Textures/Props/Stage1/AnchorPin/T_Stage1_AnchorPin_{BaseColor,Normal,Metallic,Roughness}.jpg`

---

## 6. Stage1 환경 자연물 — 사용 가능 (7종)

[S15P21B202-84](https://ssafy.atlassian.net/browse/S15P21B202-84)에서 Material 7개와
배치용 프리팹 7개를 만들어 연결했다. 이제 다른 Stage1 에셋과 같이 `PF_*` 프리팹으로 배치한다.

`globalScale`이 종류별로 크게 다르다(0.41 ~ 4). 이 값이 실제 크기를 결정한다.

| 프리뷰 | 모델 | 용도 | FBX 원본 | globalScale | **실제 크기** | 삼각형 |
| --- | --- | --- | --- | ---: | --- | ---: |
| <img src="./assets/previews/planet.png" width="100"> | `planet` | 행성 본체 또는 원경 | 1.0 × 1.0 × 1.0 m | 1 | **1.00 × 1.00 × 1.00 m** | 4,746 |
| <img src="./assets/previews/modular_cliff_wall.png" width="100"> | `modular_cliff_wall` | 조립식 절벽·벽 지형 | 1.0 × 0.53 × 0.46 m | **4** | **4.00 × 2.12 × 1.84 m** | 4,235 |
| <img src="./assets/previews/rock_pillars.png" width="100"> | `rock_pillars` | 바위 기둥 | 1.0 × 0.75 × 0.99 m | **3** | **3.00 × 2.25 × 2.97 m** | 4,078 |
| <img src="./assets/previews/alien_mushroom_tree.png" width="100"> | `alien_mushroom_tree` | 외계 버섯 나무 | 0.99 × 1.0 × 0.82 m | **3** | **2.97 × 3.00 × 2.46 m** | 4,642 |
| <img src="./assets/previews/mossy_boulder_cluster.png" width="100"> | `mossy_boulder_cluster` | 이끼 낀 바위 군락 | 0.96 × 0.54 × 1.0 m | **2.5** | **2.40 × 1.35 × 2.50 m** | 4,203 |
| <img src="./assets/previews/alien_flower_cluster.png" width="100"> | `alien_flower_cluster` | 외계 꽃 군락 | 1.0 × 0.91 × 0.93 m | **0.4386** | **0.44 × 0.40 × 0.41 m** | 4,641 |
| <img src="./assets/previews/glowing_spiral_plant.png" width="100"> | `glowing_spiral_plant` | 발광 나선형 식물 | 1.0 × 0.98 × 0.95 m | **0.4087** | **0.41 × 0.40 × 0.39 m** | 4,631 |

경로는 모두 `Models/Environment/Stage1/<모델>.fbx` (150~163 KB) 이며,
텍스처는 `Models/Environment/Stage1/<모델>.fbm/<모델>_basecolor.jpg` 1장이다.

### 파일 경로

FBX 는 Tripo3D 산출 이름(소문자 snake_case)을 그대로 두고, 새로 만든 Material·프리팹만
프로젝트 명명 규칙인 PascalCase 로 맞췄다.

| 모델 | Prefab | Material |
| --- | --- | --- |
| `planet` | `Prefabs/Environment/Stage1/PF_Stage1_Planet.prefab` | `Materials/Environment/Stage1/M_Stage1_Planet.mat` |
| `modular_cliff_wall` | `…/PF_Stage1_CliffWall.prefab` | `…/M_Stage1_CliffWall.mat` |
| `rock_pillars` | `…/PF_Stage1_RockPillars.prefab` | `…/M_Stage1_RockPillars.mat` |
| `alien_mushroom_tree` | `…/PF_Stage1_MushroomTree.prefab` | `…/M_Stage1_MushroomTree.mat` |
| `mossy_boulder_cluster` | `…/PF_Stage1_BoulderCluster.prefab` | `…/M_Stage1_BoulderCluster.mat` |
| `alien_flower_cluster` | `…/PF_Stage1_FlowerCluster.prefab` | `…/M_Stage1_FlowerCluster.mat` |
| `glowing_spiral_plant` | `…/PF_Stage1_SpiralPlant.prefab` | `…/M_Stage1_SpiralPlant.mat` |

구조와 재질 연결이 §4 의 파츠와 다르다.

```
PF_Stage1_*.prefab  (루트: Transform 만)
 └─ Visual   회전·스케일 없음 — 이 7종은 FBX 가 Y-up 이라 축 변환이 필요 없다
      └─ <모델>.fbx
```

Material 은 각 FBX 의 `.meta` 외부 오브젝트 맵(재질 추출)으로 물려 있다. 즉 임베드 재질이 아니라
`M_Stage1_*.mat` 을 쓰므로, FBX 를 어디에 인스턴스화하든 같은 재질이 따라오고 에디터에서 편집할 수 있다.
셰이더는 기존 5개와 같은 built-in **Standard** 다(§11-10 — 이 프로젝트는 URP 패키지가 있어도
실제로는 Built-in 파이프라인으로 돈다). BaseColor 한 장뿐이라 Metallic 0 · Glossiness 0.2 로 두었다.

Material·프리팹은 `Stage1EnvironmentAssets` 에디터 도구가 만든다
(`Tools/둥실둥실 우주 대탈출/Main 씬에 1장 전체 구성`에서 자동 실행). 이미 있는 파일은 건드리지 않으므로
다시 만들려면 해당 파일을 지우고 실행한다. 콜라이더는 붙어 있지 않다(§4 와 같다).

`.fbm` 텍스처가 크다 — 3.3 MB(`glowing_spiral_plant`) ~ 7.0 MB(`mossy_boulder_cluster`), **합계 약 34 MB**.

`globalScale` 값은 [S15P21B202-67](https://ssafy.atlassian.net/browse/S15P21B202-67)에서 보정한 결과다.

### Stage1 기본 Terrain — 1차 적용

[S15P21B202-177](https://ssafy.atlassian.net/browse/S15P21B202-177)에서 기존 50 × 50m Plane 대신
같은 범위의 평평한 Unity Terrain을 `Main.unity`에 적용했다. 자연물과
주요 오브젝트를 먼저 배치할 수 있도록 전체를 기존 지면 높이 0.06m로 유지하며, 지형 높낮이는
배치가 끝난 뒤 조정한다. 기존 `ENV_Ground_Arena_50m` Plane은 비교와 복구를 위해 삭제하지 않고
비활성화했다. 자연물 배치와 지면 텍스처 연결은 각각 S15P21B202-97과 S15P21B202-74에서 진행한다.

| 종류 | 경로 |
| --- | --- |
| 배치 프리팹 | `Prefabs/Environment/Stage1/PF_Stage1_Terrain.prefab` |
| TerrainData | `Terrain/Stage1/TerrainData/TD_Stage1_Arena.asset` |

`Stage1TerrainBuilder` 에디터 도구가 에셋 생성과 씬 배치를 함께 수행한다
(`Tools/둥실둥실 우주 대탈출/Main 씬에 1장 전체 구성`에서 자동 실행). 반복 실행하면 기존 Terrain 인스턴스를
교체하므로 씬에 중복되지 않는다.

---

## 6-1. Stage1 운석 — 본체 사용 가능 · 파편 미연결 (4종)

[S15P21B202-98](https://ssafy.atlassian.net/browse/S15P21B202-98)에서 Tripo 산출물을
영문 snake_case 이름으로 정리하고 Unity 임포트를 확인했다. `meteor_main`은 S15P21B202-163 에셋 정리에서
Material·아트 Prefab과 [운석 퍼즐 런타임](https://ssafy.atlassian.net/browse/S15P21B202-70)에 연결했다.
대·중·소 파편 3종은 FBX와 PBR 텍스처만 있고 Material·Prefab·런타임 연결은 아직 하지 않았다.

| 프리뷰 | 모델 | 용도 | 상태 | FBX 원본 | globalScale | **실제 크기** | 삼각형 |
| --- | --- | --- | --- | --- | ---: | --- | ---: |
| <img src="./assets/previews/meteor_main_front.png" width="100"> | `meteor_main` | 낙하·충돌 전 운석 본체 | 사용 가능 | 0.97 × 0.96 × 0.98 m | 1 | **0.97 × 0.96 × 0.98 m** | 17,790 |
| <img src="./assets/previews/meteor_debris_large_front.png" width="100"> | `meteor_debris_large` | 충돌 후 큰 파편 | 미연결 | 0.98 × 0.66 × 0.82 m | 1 | **0.98 × 0.66 × 0.82 m** | 18,030 |
| <img src="./assets/previews/meteor_debris_medium_front.png" width="100"> | `meteor_debris_medium` | 충돌 후 중간 파편 | 미연결 | 0.97 × 0.69 × 0.98 m | 1 | **0.97 × 0.69 × 0.98 m** | 18,070 |
| <img src="./assets/previews/meteor_debris_small_front.png" width="100"> | `meteor_debris_small` | 충돌 후 작은 파편 | 미연결 | 0.98 × 0.62 × 0.91 m | 1 | **0.98 × 0.62 × 0.91 m** | 17,638 |

경로는 모두 `Models/Environment/Stage1/Meteors/<모델>.fbx`이며 파일 크기는 각각
`meteor_main` 551 KB · `meteor_debris_large` 544 KB · `meteor_debris_medium` 541 KB ·
`meteor_debris_small` 524 KB다.

각 모델은 같은 이름의 `.fbm` 폴더에서 BaseColor · Normal · Metallic · Roughness 4장을 참조한다.
예: `Models/Environment/Stage1/Meteors/meteor_main.fbm/meteor_main_basecolor.jpg`.
FBX 내부의 Tripo UUID 경로도 새 영문 경로로 다시 내보냈으며, Unity에서 모델 4개와 텍스처 16개의
임포트를 검증했다. 네 모델 모두 메시 1개이며 에셋 전체 용량은 약 12.0 MB다.
각 모델의 앞·뒤·좌·우 프리뷰는 `docs/assets/previews/<모델>_{front,back,left,right}.png`에 있다.

---

## 6-2. Stage1 자연물·맵 타일 리뉴얼 — 미연결 (자연물 8종 + 타일 1종)

[S15P21B202-163](https://ssafy.atlassian.net/browse/S15P21B202-163)에서 신규 원본 에셋을 추가했다.
기존 Stage1 자연물 파일을 덮어쓰지 않고, 각 신규 자연물을 Stage1 바로 아래의 개별 폴더로 분리했다.
현재는 FBX·PBR 텍스처·프리뷰까지만 있으며 Material·Prefab·Collider·LOD·씬 배치는 아직 하지 않았다.

| 프리뷰 | 모델 | 용도 | 상태 |
| --- | --- | --- | --- |
| <img src="./assets/previews/alien_flower_cluster_front.png" width="100"> | `alien_flower_cluster` | 외계 꽃 군락 | 미연결 |
| <img src="./assets/previews/alien_rock_cluster_a_front.png" width="100"> | `alien_rock_cluster_a` | 외계 바위 군락 A | 미연결 |
| <img src="./assets/previews/alien_rock_cluster_b_front.png" width="100"> | `alien_rock_cluster_b` | 외계 바위 군락 B | 미연결 |
| <img src="./assets/previews/alien_rock_flat_front.png" width="100"> | `alien_rock_flat` | 낮고 평평한 바위 | 미연결 |
| <img src="./assets/previews/alien_rock_flat_cracked_front.png" width="100"> | `alien_rock_flat_cracked` | 갈라진 낮은 바위 | 미연결 |
| <img src="./assets/previews/alien_rock_oval_front.png" width="100"> | `alien_rock_oval` | 타원형 바위 | 미연결 |
| <img src="./assets/previews/alien_rock_round_front.png" width="100"> | `alien_rock_round` | 둥근 바위 | 미연결 |
| <img src="./assets/previews/alien_rock_round_cracked_front.png" width="100"> | `alien_rock_round_cracked` | 갈라진 둥근 바위 | 미연결 |

자연물 기준 경로는 `Models/Environment/Stage1/<모델>/`이다. 각 폴더에는 같은 이름의
FBX와 `tripo_convert_<id>.fbm/` 폴더가 있고, `.fbm` 안에 Base Color·Normal·Metallic·Roughness 4장이 있다.
`.fbm` 이름은 FBX 내부 텍스처 경로 호환을 위해 원본을 유지했으므로 FBX와 해당 `.fbm` 폴더를 함께 이동해야 한다.

### 맵 타일

| 용도 | 경로 | 상태 |
| --- | --- | --- |
| Stage1 신규 바닥 Base Color | `Textures/Environment/Stage1/Renewal/Ground/T_Stage1_AlienGroundTile_BaseColor.png` | 1024 × 1024 seamless 원본·Terrain 연결 |

현재 Base Color 한 장을 [S15P21B202-74](https://ssafy.atlassian.net/browse/S15P21B202-74)에서
`TL_Stage1_AlienGround` Terrain Layer로 연결했다. 50 × 50m Terrain에 5m 단위로 반복되어
화면에는 10 × 10 타일처럼 보인다. Wrap Mode는 `Repeat`, Mipmap과 Aniso Level 4를 사용한다.
Normal·Mask/Metallic·Height가 필요하면 별도로 제작해야 한다.

### 적용 전 확인

- 자연물별 정면 프리뷰 8장은 `docs/assets/previews/`에 있다.
- `Normal.jpg`는 Normal map으로, Metallic·Roughness는 sRGB를 끄고 선형 데이터로 임포트한다.
- Roughness를 직접 받지 않는 셰이더는 `Smoothness = 1 - Roughness`로 변환해 사용한다.
- FBX를 씬에 직접 배치하지 말고 Material과 `PF_Stage1_*` Prefab을 만든 뒤 배치한다.
- 모델의 폴리곤 수·실제 치수·피벗·전방 축과 라이선스·배포 범위는 아직 검증하지 않았다.
- 기존 `alien_flower_cluster`와 이름이 겹치므로 신규 Material·Prefab은 `Renewal` 등으로 구분한다.
- 바닥은 신규 Material에서 먼저 비교하고, 50 m × 50 m 전체에서 반복 무늬·색 띠·UV 경계를 확인한다.

---

## 7. 플레이어 캐릭터 — 사용 가능 (1종)

| 프리뷰 | 모델 | FBX 원본 | 삼각형 | 비고 |
| --- | --- | --- | ---: | --- |
| <img src="./assets/previews/SF_Rabbit_Base.png" width="110"> | `SF_Rabbit_Base` 토끼 우주비행사 | 0.89 × 0.98 × 0.42 m | 21,084 | `globalScale` 1 |

- Prefab: `Prefabs/Resources/SF_Rabbit_Base.prefab` — **SkinnedMeshRenderer** 보유
- FBX: `Models/Characters/SF_Rabbit_Base.fbx` (2,164 KB)
- 텍스처: `Models/Characters/SF_Rabbit_Base.fbm/SF_Rabbit_Color.jpg` (851 KB) · `SF_Rabbit_Normal.jpg` (438 KB) — **FBX 내장 재질을 사용하며 별도 `.mat` 에셋은 없다**
- `Prefabs/Resources/` 아래에 있는 이유는 런타임 코드가 `Resources.Load<GameObject>()`로 집기 때문이다.
- 콘셉트 이미지는 [§9](#9-참고-에셋)에 있다.

[S15P21B202-75](https://ssafy.atlassian.net/browse/S15P21B202-75)에서 캐릭터 모델을 이것으로 통일했다. 이전 `SF_Astronaut_Base.fbx`는 삭제됐다.

---

## 8. 기존 SpaceFlop 에셋 — 기존

이전 프로토타입 자산이다. **신규 Stage1 작업에는 쓰지 않는다.** 런타임 코드가 아직 참조하므로 남아 있다.

| 프리뷰 | 에셋 | 크기 | 비고 |
| --- | --- | --- | --- |
| <img src="./assets/previews/SF_Spaceship_Capsule.png" width="110"> | `Models/Spaceships/SF_Spaceship_Capsule.fbx` (1,999 KB) | **3.42 × 4.57 × 2.80 m** | 유일하게 실척으로 제작된 모델. 메시 91개 · 45,032 삼각형 · FBX 재질 슬롯 10개 |

- Prefab: `Prefabs/Resources/SF_Spaceship_Capsule.prefab` — MeshRenderer 보유
- 텍스처 없음(절차적 재질). 프리뷰는 중립 회색으로 렌더한 것이다.
- `§4`의 `SF_Stage1_Spaceship_Capsule.fbx`와 **이름이 비슷하지만 다른 파일**이다. 혼동 주의.
- Blender 원본: `METAGAME/BlenderSource/SpaceFlop/SF_Spaceship_Capsule.blend`

### 셰이더 (3종)

`Shaders/Resources/` 아래에 있고, 코드가 `Resources.Load<Shader>()`로 로드한다(빌드 포함 보장).

| 파일 | Shader 이름 |
| --- | --- |
| `Shaders/Resources/DoughSurface.shader` | `SpaceFlop/Dough` |
| `Shaders/Resources/FlatUnlit.shader` | `SpaceFlop/Flat` |
| `Shaders/Resources/GhostSurface.shader` | `SpaceFlop/Ghost` |

---

## 9. 참고 에셋

런타임에 쓰지 않는 제작 참고용 이미지다.

| 파일 | 크기 |
| --- | ---: |
| `Textures/Concept/Characters/SF_RabbitAstronaut_TPose_Front.png` | 1,340 KB |
| `Textures/Concept/Characters/SF_RabbitAstronaut_TPose_Front_v2.png` | 1,347 KB |
| `Textures/Concept/Characters/SF_RabbitAstronaut_TPose_Front_v3.png` | 1,515 KB |
| `Textures/Concept/Characters/SF_RabbitAstronaut_TPose_Standalone.png` | 1,372 KB |

`docs/design/assets/chapter-1-concept-art.png`도 기획 참고용으로 별도 존재한다.

---

## 10. 씬 · 설정 · 텍스처 전체 목록

### 씬 (1개)

| 파일 | 내용 |
| --- | --- |
| `Assets/Scenes/Main.unity` | 유일한 씬. 게임플레이 스크립트(`SpaceshipPart`, `PartSocket`, `WeldPoint`, `PartClamp`, `TractorBeam`, `MeteorSpawner`, `Grabbable`, `SpaceBootstrap`, `LobbyBootstrap` 등)와 `Settings/SampleSceneProfile.asset` 위에, [S15P21B202-84](https://ssafy.atlassian.net/browse/S15P21B202-84)에서 Stage1 프리팹 12종을 122개 배치했다(아래). |

### `Main.unity` 의 아트 배치 (S15P21B202-84)

`Stage1SkinBuilder` 에디터 도구가 만들며, `Tools/둥실둥실 우주 대탈출/Main 씬에 1장 전체 구성`에서 자동 실행된다.
**물리와 좌표는 건드리지 않는다** — 프리미티브의 MeshRenderer 만 끄고 같은 자리에 모델을 `Visual` 자식으로 얹는다.
그래서 `Visual` 자식만 지우면 프리미티브 룩으로 돌아간다.

| 씬 위치 | 배치 | 개수 |
| --- | --- | ---: |
| `Stage1/Gameplay/Puzzle/Part_1‥4` | HoverPod → Robot → Capsule, 그리고 TractorCraft | 4 |
| `…/Puzzle/AnchorPins` | AnchorPin — 각 `Clamp_0‥3` 의 좌표에, 제작 크기 그대로 바닥을 지면에 | 16 |
| `…/TractorBeam_1‥2` | TractorCraft (빔 기준점 y=12 에 수평으로) | 2 |
| `Stage1/Environment/Boundaries` | CliffWall 링 (배율 1.6, 한 변 8개, ±23.5m) | 32 |
| `Stage1/Environment/Nature` | RockPillars 8 · MushroomTree 10 · BoulderCluster 12 · FlowerCluster 20 · SpiralPlant 16 | 66 |
| `Stage1/Environment/DistantBackground` | Planet 2기 (지름 90m · 40m, 별밭 반지름 300 안쪽) | 2 |
| `Stage1/Environment/Ground` | `M_Stage1_ArenaGround` 재질 + y를 0.06m 로 올림 | 1 |
| `Stage1/Lighting/Directional Light` | Light 컴포넌트를 끔 (아래) | 1 |

- **파츠 스킨 배율은 1이다**(S15P21B202-166). 파츠 콜라이더가 `PartSpec` 의 실측 형태를 따르므로
  모델을 배율 1로 얹고 바닥만 맞추면 둘이 일치한다. 프리팹 피벗은 모두 바닥에 있다(실측 확인).

  그 전에는 4기 모두 `PhysicsScale` 의 3 × 3.7 × 3 m 상자를 써서, 모델을 균일 배율로 넣은 뒤 세로로
  1.35배까지 늘렸다(Robot 3.70 · TractorCraft 3.70 · Capsule 3.53 · HoverPod 2.34 / 3.70m). 납작한
  HoverPod 은 그래도 박스를 못 채워 위에 쌓인 파츠와 사이가 벌어졌다. 상세 기획(S15P21B202-107 §7.1)이
  "동일한 3 × 3.7 × 3m 상자로 늘리지 않는다" 고 정하면서 물리 기준값 쪽을 바꾸었다.

- **파츠별 콜라이더·질량**(S15P21B202-166): 프리팹 메시를 높이 10구간으로 잘라 각 구간의 x·z 폭을 재고,
  폭이 크게 달라지는 경계에서 상자를 나눠 컴파운드 콜라이더를 만들었다. 값은 `PartSpec` 에 있다.

  | 파츠 | 조립 순서 | 상자 | 높이 | 질량 | 중심질량 y |
  | --- | --- | --- | --- | --- | --- |
  | Robot | 1층 주 추진 엔진 | 3 | 2.16 m | 460 kg | −0.008 |
  | HoverPod | 2층 부양·안정화 | 2 | 1.33 m | 429 kg | +0.076 |
  | Capsule | 3층 승무원 거주 | 2 | 2.22 m | 745 kg | −0.033 |
  | TractorCraft | 4층 견인 조종 | 4 | 2.45 m | 406 kg | −0.360 |

  합산 높이 **8.16m** — 기획 §7.1 의 약 8.2m 와 맞는다. 질량은 부피 × `PhysicsScale.PartDensity`(70)로
  계산하므로 튜닝 손잡이가 하나다. 중심질량은 지정하지 않는다 — Unity 가 콜라이더에서 계산하므로
  상자가 형태를 따르면 저절로 따라온다. 위 값은 고정쇠가 풀린(non-kinematic) 상태에서 잰 것이다.
- **자연물 배치**는 씨앗 20260827 결정론적 난수다. 파츠 자리·조립 위치·플레이어 스폰 주변은 비운다.
  결과가 매번 같으므로 다시 실행해도 씬 diff 가 늘지 않는다.
- **콜라이더**: 자연물은 장식이라 붙이지 않았다. 절벽 벽에는 보이지 않는 높이 9m `BoxCollider` 를 붙였다 —
  모델 높이(약 3.4m)만으로는 저중력(-2.8) 점프로 넘어간다.
- **고정못은 고정쇠의 자식이 아니다.** 자식으로 붙였더니 견인 빔이 파츠를 들어 올릴 때 못 16개가
  같이 공중으로 따라 올라갔다(빌드 실행으로 확인). 못은 땅에 박힌 물건이라 파츠가 떠나도 남아야 하므로
  좌표만 빌려 별도 그룹에 세운다.
- **지면 재질**: 프리미티브 Plane 에 기본 흰색 재질이 붙어 있어 해를 받으면 화면이 하얗게 날아갔다.
  `M_Stage1_ArenaGround`(Standard, 바위 군락 BaseColor 10회 타일링, 틴트 0.46 회색, Glossiness 0.08)를
  물렸다. 색·타일링은 `Stage1SkinBuilder` 의 `GroundTint`·`GroundTiling` 한 줄로 조정한다.
- **지면 높이 0.06m**: 런타임 `LevelBuilder` 의 착륙장 슬랩(y=0)·크레이터 바닥(y≈0.05)과 같은 평면이라
  z-파이팅으로 지면이 지글거렸다. 6cm 띄워 아레나 안에서는 Stage1 지면만 보이게 했다. §11-9 의 임시 대응이며
  `buildLevel` 을 끄면 필요 없다.
- **템플릿 Directional Light 를 껐다.** 런타임 조명은 `Vacuum` 이 소유한다(해 1.75 Soft + 행성 반사광 0.16).
  `SpaceBootstrap` 은 씬 카메라만 끄고 라이트는 끄지 않아 Unity 템플릿 라이트(강도 2.0, Soft 그림자)가
  함께 켜져 있었다. 결과가 두 가지였고 둘 다 빌드 실행으로 확인했다.
  ① 빛이 약 두 배가 되어 지면이 날아간다 ② 그림자를 굽는 방향성 광원이 둘인데 강도가 2.0 대 1.75 로
  비슷해 프레임마다 주광원 선택이 뒤집히며 그림자가 깜빡인다.
  오브젝트는 남기고 Light 컴포넌트만 껐다. 끈 뒤 정지 화면 연속 두 프레임의 차이는 전체의 0.42%
  (HUD 경과 시간·별 반짝임)뿐이다.

[S15P21B202-75](https://ssafy.atlassian.net/browse/S15P21B202-75)에서 씬을 이 하나로 통일했다. 이전 `Surface.unity`·Sandbox·`SampleScene`은 삭제됐다.

### Stage1 PBR 텍스처 (20장)

세트별로 `BaseColor · Metallic · Normal · Roughness` 4장 구성이다.

| 세트 | 경로 | BaseColor | Metallic | Normal | Roughness |
| --- | --- | ---: | ---: | ---: | ---: |
| AnchorPin | `Textures/Props/Stage1/AnchorPin/T_Stage1_AnchorPin_*.jpg` | 895 KB | 484 KB | 581 KB | 478 KB |
| Capsule | `Textures/Spaceships/Stage1/Capsule/T_Stage1_SpaceshipCapsule_*.jpg` | 814 KB | 269 KB | 423 KB | 298 KB |
| HoverPod | `Textures/Spaceships/Stage1/HoverPod/T_Stage1_SpaceshipHoverPod_*.jpg` | 767 KB | 293 KB | 451 KB | 325 KB |
| Robot | `Textures/Spaceships/Stage1/Robot/T_Stage1_SpaceshipRobot_*.jpg` | 955 KB | 299 KB | 520 KB | 395 KB |
| TractorCraft | `Textures/Spaceships/Stage1/TractorCraft/T_Stage1_TractorCraft_*.jpg` | 790 KB | 410 KB | 413 KB | 416 KB |

### Unity 설정 · 템플릿 잔여물 (설정)

| 파일 | 내용 |
| --- | --- |
| `Assets/Settings/PC_RPAsset.asset` · `PC_Renderer.asset` | PC용 URP 파이프라인·렌더러 |
| `Assets/Settings/Mobile_RPAsset.asset` · `Mobile_Renderer.asset` | 모바일용 URP 파이프라인·렌더러 |
| `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset` | URP 전역 설정 |
| `Assets/Settings/DefaultVolumeProfile.asset` · `SampleSceneProfile.asset` | Volume 프로파일 (후자는 `Main.unity`가 참조) |
| `Assets/InputSystem_Actions.inputactions` | Input System 기본 액션 맵 |
| `Assets/Readme.asset` · `Assets/TutorialInfo/Icons/URP.png` · `TutorialInfo/Layout.wlt` | Unity URP 템플릿 잔여물. 삭제 가능 |

### 아직 빈 폴더

`Animations/` · `Audio/` · `Data/` · `ThirdParty/` · `UI/` · `VFX/` — `.gitkeep`만 있다.

---

## 11. 미해결 목록

카탈로그 작성 중 실측으로 확인된 것들이다. **판단은 각 파트에 맡기고 사실만 적는다.**

| # | 내용 | 관련 |
| --- | --- | --- |
| 1 | ~~환경 자연물 7종에 Material 에셋과 프리팹이 없다.~~ **해소** — S15P21B202-84 에서 `M_Stage1_*` 7개를 만들어 재질 추출로 물리고 `PF_Stage1_*` 7개를 만들었다(§6). | Art |
| 2 | **Roughness 텍스처 5장이 어떤 Material에도 참조되지 않는다.** Material 5개 전부 BaseColor·Normal·Metallic 3장만 참조한다. URP에서 Metallic 맵 알파를 smoothness로 쓰는 관례상 의도일 수 있다. | Art |
| 3 | ~~`Main.unity`에 아트 에셋이 하나도 배치되지 않았다.~~ **해소** — S15P21B202-84 에서 12종 122개를 배치했다(§10). | Level · Puzzle |
| 11 | **`SpaceBootstrap` 이 씬 카메라만 끄고 씬 라이트는 끄지 않는다.** 그래서 Unity 템플릿 Directional Light 가 `Vacuum` 의 해와 함께 켜져 있었다(§10 에서 씬 쪽 라이트를 꺼 대응). 근본적으로는 `Vacuum` 이 조명을 소유한다는 규칙을 코드로 강제하는 편이 맞다 — 카메라를 끄는 그 루프에서 `Light` 도 같이 끄면 된다. | Bootstrap |
| 4 | **프리팹에 Collider·Rigidbody가 없다.** 퍼즐 물리를 붙일 때 배치하는 쪽에서 추가해야 한다. 절벽 벽만 씬 배치 쪽에서 `BoxCollider` 를 붙여 경계로 쓰고 있다. | Puzzle |
| 8 | **납작한 HoverPod 이 파츠 박스 높이(3.7m)를 못 채운다** — 스킨 높이 2.34m. 파츠 위에 다른 파츠를 쌓으면 사이가 벌어진다. 파츠별 높이를 따로 두거나(=`PhysicsScale.PartHeight` 분리, 물리 기준값 변경) 모델을 고쳐야 한다. | Art · Puzzle |
| 9 | **런타임 `LevelBuilder` 가 예전 SpaceFlop 코스를 Stage1 아레나와 같은 좌표에 겹쳐 세운다** (`SpaceBootstrap.buildLevel = 1`, 스폰 `(0,0,0)`). 빌드를 실행해 확인했다 — 아레나 위에 예전 코스의 지형·소품·HUD("표지 0/6")가 겹쳐 보인다. 스킨 작업 범위가 아니라 그대로 두었다. | Level · Puzzle |
| 10 | **프로젝트가 URP 가 아니라 Built-in 파이프라인으로 돈다.** `ProjectSettings/GraphicsSettings.asset` 의 `m_CustomRenderPipeline` 과 모든 품질 레벨의 `customRenderPipeline` 이 비어 있다. URP 패키지·`PC_RPAsset` 등은 있지만 할당돼 있지 않다. 그래서 Material 5개가 built-in Standard 를 쓰고, 런타임 셰이더 3종(`SpaceFlop/*`)도 built-in 용이다. **README 와 이 문서가 "URP 17.5.0" 이라고 적어 온 것은 사실과 다르다.** URP 로 전환할지, 문서를 사실에 맞출지 결정이 필요하다 — 전환하면 재질 12개와 런타임 셰이더 3종을 모두 고쳐야 한다. | Infra · Art |
| 5 | **`.fbm` 임베드 텍스처가 크다** (합계 약 34 MB, 최대 7.0 MB). LFS 용량과 임포트 시간에 영향이 있다. 다운스케일 여부 검토 필요. | Art · Infra |
| 6 | `METAGAME/Assets/_Project/Models/README_KO.md`가 삭제된 `SF_Astronaut_Base.fbx`를 안내한다. 루트 `README.md`도 삭제된 `Surface.unity`를 안내한다. | Plan · Infra |
| 7 | Unity URP 템플릿 잔여물(`Readme.asset`, `TutorialInfo/`)이 남아 있다. | Infra |
| 12 | ~~운석 본체가 런타임에 연결되지 않았다.~~ **본체 해소** — `meteor_main`을 Material·Prefab과 운석 스포너에 연결했다. 대·중·소 파편 3종은 아직 미연결이다(§6-1). | Art · Puzzle |
| 13 | **리뉴얼 자연물 FBX 8종과 바닥 타일은 Material·Prefab·씬에 연결되지 않았다.** 적용 전에 크기·축·피벗·반복 경계를 검증해야 한다(§6-2). | Art · Level |

---

## 12. 이 문서 갱신하기

에셋을 추가·교체하는 MR에서는 **이 문서도 같이 고친다.**

**① 프리뷰 이미지 재생성** — 리포 루트에서:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python 'METAGAME/Tools/Blender/generate_asset_previews.py'
```

특정 모델만 다시 만들려면 뒤에 `-- <FBX이름>`을 붙인다(확장자 없이).
출력은 `docs/assets/previews/<FBX이름>.png`이고, 스크립트가 크기·삼각형 수·연결된 텍스처를 함께 출력하므로 그 값을 표에 반영한다.

렌더는 결정론적이다 — 같은 FBX면 항상 같은 이미지가 나오므로, 바뀌지 않은 모델은 diff에 뜨지 않는다.

**② 표에 행 추가** — 해당 절(`§4`~`§9`)에 프리뷰·경로·크기·연결 체인을 적는다.
`실제 크기 = FBX 원본 × globalScale × 프리팹 스케일`을 반드시 계산해 넣는다.

**③ 미연결이면 `§11`에도 적는다** — 프리팹이나 Material이 없는 채로 커밋한다면 사유와 담당 파트를 남긴다.

### 명명 규칙 (현재 사용 중)

| 접두사 | 대상 | 예 |
| --- | --- | --- |
| `SF_Stage1_*` | Stage1 FBX 모델 | `SF_Stage1_Spaceship_Capsule.fbx` |
| `PF_Stage1_*` | Stage1 프리팹 | `PF_Stage1_AnchorPin.prefab` |
| `M_Stage1_*` | Material | `M_Stage1_TractorCraft.mat` |
| `T_Stage1_*_{BaseColor,Normal,Metallic,Roughness}` | 텍스처 | `T_Stage1_SpaceshipRobot_Normal.jpg` |
| (소문자 snake_case) | 외주·생성 도구 산출 환경 모델 | `alien_mushroom_tree.fbx` |

환경 모델만 소문자 규칙인 것은 Tripo3D 산출물 이름을 그대로 둔 결과다. 통일 여부는 정해지지 않았다.
