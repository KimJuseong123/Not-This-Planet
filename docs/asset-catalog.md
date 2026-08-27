# Unity 에셋 카탈로그

> 기준 커밋: `develop` **054a2a6** · 작성 2026-08-27 · 이슈 [S15P21B202-80](https://ssafy.atlassian.net/browse/S15P21B202-80)
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

FBX 파일만 열어봐서는 크기를 알 수 없다. Tripo3D로 만든 13종은 전부 **1m 이하로 정규화**되어 있고,
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
| FBX 모델 | 14 | 사용 가능 6 · 미연결 7 · 기존 1 |
| 텍스처 (`Textures/`) | 24 | Stage1 PBR 20 · 콘셉트 4 |
| 텍스처 (`.fbm/` 임베드) | 9 | 캐릭터 2 · 환경 7 |
| Material | 5 | 전부 사용 가능 |
| Prefab | 7 | Stage1 5 · 기존 2 |
| Shader | 3 | 기존 SpaceFlop 런타임 전용 |
| Scene | 1 | `Scenes/Main.unity` |
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

## 6. Stage1 환경 자연물 — 미연결 (7종)

**현재 그대로 씬에 배치할 수 없다.** FBX와 `.fbm/` 임베드 BaseColor 1장만 있고,
**Material 에셋도 프리팹도 없다.** 배치하려면 Material 추출·프리팹 제작이 먼저 필요하다.

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
임베드 텍스처는 `Models/Environment/Stage1/<모델>.fbm/<모델>_basecolor.jpg` 1장이다.

`.fbm` 텍스처가 크다 — 3.3 MB(`glowing_spiral_plant`) ~ 7.0 MB(`mossy_boulder_cluster`), **합계 약 34 MB**.

`globalScale` 값은 [S15P21B202-67](https://ssafy.atlassian.net/browse/S15P21B202-67)에서 보정한 결과다.

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
| `Assets/Scenes/Main.unity` | 유일한 씬. **아트 에셋을 하나도 참조하지 않는다** — 참조 대상은 게임플레이 스크립트(`SpaceshipPart`, `PartSocket`, `WeldPoint`, `PartClamp`, `TractorBeam`, `MeteorSpawner`, `Grabbable`, `SpaceBootstrap`, `LobbyBootstrap` 등)와 `Settings/SampleSceneProfile.asset`뿐이다. 즉 퍼즐 로직은 프리미티브 도형으로 동작 중이다. |

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
| 1 | **환경 자연물 7종에 Material 에셋과 프리팹이 없다.** 텍스처는 `.fbm/` 임베드뿐이라 Unity에서 편집할 수 없고, 프리팹이 없어 씬 배치도 안 된다. [S15P21B202-65](https://ssafy.atlassian.net/browse/S15P21B202-65)("텍스처 및 머티리얼 적용")가 완료 상태인데, FBX 내장 재질로 처리한 것을 완료로 본 것일 수 있다. | Art |
| 2 | **Roughness 텍스처 5장이 어떤 Material에도 참조되지 않는다.** Material 5개 전부 BaseColor·Normal·Metallic 3장만 참조한다. URP에서 Metallic 맵 알파를 smoothness로 쓰는 관례상 의도일 수 있다. | Art |
| 3 | **`Main.unity`에 아트 에셋이 하나도 배치되지 않았다.** 완성된 프리팹 5종이 씬에서 쓰이지 않는 상태다. | Level · Puzzle |
| 4 | **프리팹에 Collider·Rigidbody가 없다.** 퍼즐 물리를 붙일 때 배치하는 쪽에서 추가해야 한다. | Puzzle |
| 5 | **`.fbm` 임베드 텍스처가 크다** (합계 약 34 MB, 최대 7.0 MB). LFS 용량과 임포트 시간에 영향이 있다. 다운스케일 여부 검토 필요. | Art · Infra |
| 6 | `METAGAME/Assets/_Project/Models/README_KO.md`가 삭제된 `SF_Astronaut_Base.fbx`를 안내한다. 루트 `README.md`도 삭제된 `Surface.unity`를 안내한다. | Plan · Infra |
| 7 | Unity URP 템플릿 잔여물(`Readme.asset`, `TutorialInfo/`)이 남아 있다. | Infra |

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
