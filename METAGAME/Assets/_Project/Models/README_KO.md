# Not This Planet 기본 캐릭터 · 우주선 모델

<!-- 아래 표의 `BlenderSource/SpaceFlop/` 경로는 **저장소 밖의 원본 폴더**라
     그대로 둔다 (S15P21B202-399). 아트 담당이 그 폴더를 옮기면 함께 고친다. -->

첨부 콘셉트의 **둥글고 친근한 SD 비율**, **검은 디스플레이형 얼굴**, **수리 흔적이 많은 캡슐 우주선**을 형태 레퍼런스로 삼아 새로 제작한 오리지널 3D 에셋입니다. 외부 텍스처나 유료 폰트는 사용하지 않았습니다.

## 파일 구성

| 용도 | 파일 |
|---|---|
| Unity용 캐릭터 | `Characters/SF_Astronaut_Base.fbx` |
| Unity용 우주선 | `Spaceships/SF_Spaceship_Capsule.fbx` |
| 캐릭터 Blender 원본 | `BlenderSource/SpaceFlop/SF_Astronaut_Base.blend` |
| 우주선 Blender 원본 | `BlenderSource/SpaceFlop/SF_Spaceship_Capsule.blend` |
| 프리뷰 | `BlenderSource/SpaceFlop/Previews/` |
| 재생성 스크립트 | `Tools/Blender/generate_spaceflop_models.py` |
| FBX 재임포트 검사 | `Tools/Blender/validate_spaceflop_models.py` |
| 수치/검사 결과 | `BlenderSource/SpaceFlop/model_manifest.json`, `validation_report.json` |

모든 치수는 `1 Blender Unit = 1 m = 1 Unity Unit` 기준입니다. Blender의 앞쪽 `-Y`는 FBX 변환 뒤 Unity의 앞쪽 `+Z`, 위쪽은 Unity `+Y`가 됩니다.

## 캐릭터 사양

- 이름: `SF_Astronaut_Base`
- 기본 키: 1.33 m, 안테나 포함 약 1.42 m
- 비율: 약 2.1등신, 헬멧 지름 약 0.62 m
- 메시: 52개, 생성 원본 약 34,960 triangles
- 재질 슬롯: 10개
- 자세: 팔을 살짝 내린 A-pose
- 변형 방식: 부품별 rigid skin weight. 관절 사이가 고무 인형처럼 찌그러지지 않고 단단한 장난감 부품처럼 움직입니다.
- 포함 장식: 검은 바이저, 발광 눈/입, 상태등, 가슴 제어판, 명찰, 안테나, 어깨/무릎 패드, PLSS 등짐, 산소통 2개, RCS 노즐 2개, 연결 호스

본 계층은 다음과 같습니다.

```text
Root
└─ Hips
   ├─ Spine ─ Chest ─ Neck ─ Head
   │          ├─ LeftUpperArm ─ LeftLowerArm ─ LeftHand
   │          └─ RightUpperArm ─ RightLowerArm ─ RightHand
   ├─ LeftUpperLeg ─ LeftLowerLeg ─ LeftFoot
   └─ RightUpperLeg ─ RightLowerLeg ─ RightFoot
```

### Unity 임포트 권장값

1. Project 창에서 `SF_Astronaut_Base.fbx`를 선택합니다.
2. **Model** 탭에서 `Scale Factor = 1`, `Convert Units = On`, `Read/Write = Off`를 사용합니다.
3. **Rig** 탭은 현재 물리 인형 구조와 함께 쓰기 쉬운 `Animation Type = Generic`, `Avatar Definition = Create From This Model`을 권장합니다.
4. 이 FBX에는 애니메이션 클립을 넣지 않았습니다. 애니메이터용으로 쓸 경우 A-pose를 기준으로 별도 클립을 만드십시오.
5. **Materials** 탭에서 `Import Materials = On`으로 두고, 필요하면 `Extract Materials...`로 프로젝트 재질을 꺼냅니다. Built-in Render Pipeline에서는 추출한 재질의 Shader를 `Standard`로 두면 됩니다.
6. CapsuleCollider 하나로 캐릭터 전체를 감싸지 말고, 현재 `RagdollBuilder`처럼 머리/가슴/골반/팔다리별 Sphere, Capsule, Box Collider를 유지하는 편이 잡기 물리에 안전합니다.

### 현재 런타임 코드와 연결할 때

현재 `RagdollBuilder.cs`는 프리미티브 메시와 Rigidbody를 실행 중에 직접 생성합니다. 따라서 FBX를 폴더에 넣는 것만으로 플레이 캐릭터의 외형이 자동 교체되지는 않습니다.

권장 연결 방식은 물리 구조를 그대로 보존하고, 아래 본/부위에 대응하는 FBX 시각 메시만 자식으로 붙이는 어댑터 방식입니다.

| FBX 본 | 현재 런타임 부위 |
|---|---|
| `Hips` | `Doll.Hips` (`골반`) |
| `Chest` | `Doll.Chest` (`가슴`) |
| `Head` | `Doll.Head` (`헬멧`) |
| `Left/RightUpperArm` | `Doll.Left/Right.Upper` |
| `Left/RightLowerArm` | `Doll.Left/Right.Lower` |
| `Left/RightHand` | `Doll.Left/Right.Hand.Body` |
| `Left/RightUpperLeg` | `Doll.LeftLeg/RightLeg.Thigh` |
| `Left/RightLowerLeg` | `Doll.LeftLeg/RightLeg.Shin` |
| `Left/RightFoot` | `Doll.LeftLeg/RightLeg.Foot` |

물리 오브젝트의 Renderer만 숨기고 Rigidbody, Collider, ConfigurableJoint는 제거하지 않는 것이 핵심입니다.

## 우주선 사양

- 이름: `SF_Spaceship_Capsule`
- 크기: 약 3.25 m × 3.0 m × 4.45 m
- 메시: 91개, 생성 원본 약 45,032 triangles
- 재질 슬롯: 10개
- 전면 방향: 큰 파란 원형 창 쪽(Unity `+Z`)
- 분리 부품: 상부 해치, 측면 해치, 메인 엔진, 좌/우 RCS, 4개 착륙 다리, 안테나, 외장 패널, 호스, 수리 소켓
- 메타데이터: 주요 오브젝트에 `asset_role`, 교체/수리 가능한 부품에 `repair_socket_id`, `repairable` 사용자 속성이 들어 있습니다.

대표 수리 ID는 `PORTHOLE`, `TOP_HATCH`, `SIDE_HATCH`, `MAIN_ENGINE`, `RCS_L`, `RCS_R`, `LANDING_FL/FR/BL/BR`, `COOLANT_HOSE`, `FUEL_LINE`, `POWER`, `FUEL`, `OXYGEN`, `ANTENNA`, `PLATE_01`~`PLATE_05`입니다.

### Unity 프리팹 구성 권장

1. 포함된 `.meta`에는 **Model / Bake Axis Conversion = On**, **Preserve Hierarchy = On**, **Rig / Animation Type = None**, **Import Animation = Off**가 설정되어 있습니다. 이 값으로 Unity `+Y`가 위, 큰 창 방향이 `+Z`가 됩니다.
2. `SF_Spaceship_Capsule.fbx`를 씬으로 끌어 놓고 가장 바깥 오브젝트를 Prefab으로 만듭니다.
3. 루트에 정적 전시물이라면 Collider만, 움직이는 우주선이라면 `Rigidbody`도 추가합니다.
4. 메인 몸체는 Convex MeshCollider 대신 `CapsuleCollider` 또는 겹친 2~3개의 `SphereCollider`를 권장합니다. 권장 Capsule 값은 대략 `Center (0, 2.0, 0)`, `Radius 1.42`, `Height 3.35`입니다.
5. 네 착륙 발에는 작은 `BoxCollider`를 각각 추가합니다. 동적 물체에 비볼록 MeshCollider를 사용하지 마십시오.
6. Blender 원본의 `UNITY_COLLIDER_GUIDES_NOT_EXPORTED` 컬렉션에는 콜라이더 크기 가이드가 있습니다. 이 컬렉션은 렌더와 Unity FBX에서 제외되어 있습니다.
7. 수리 게임플레이를 붙일 때 `repair_socket_id` 기준으로 대상 부품을 찾고, 완료 전후에 해당 메시 활성화/재질 변경/파티클 재생을 연결하면 됩니다.
8. `Engine_Main_Bell`, `RCS_Nozzle_L/R`의 출구 방향에 별도 빈 GameObject를 두고 화염 ParticleSystem을 자식으로 붙이십시오.

## Blender에서 수정 및 재생성

원본 `.blend`를 직접 수정해도 되고, 비율/색/부품을 반복 생성하려면 PowerShell에서 프로젝트 루트를 기준으로 다음을 실행합니다.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python 'Tools\Blender\generate_spaceflop_models.py'
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python 'Tools\Blender\validate_spaceflop_models.py'
```

생성기는 같은 경로의 `.blend`, `.fbx`, 프리뷰를 결정적으로 갱신합니다. 수작업으로 원본을 수정했다면 생성기를 다시 실행하기 전에 별도 파일로 저장하십시오.

## 최적화 메모

- 현재 수치는 PC 프로토타입과 근접 카메라를 우선한 LOD0입니다.
- 모바일/다수 캐릭터용 LOD1은 우주인 약 12k~18k triangles, 우주선 약 18k~25k triangles를 권장합니다.
- 작은 리벳과 표시등은 원거리 LOD에서 제거하고, 우주선 호스는 단순 튜브나 노멀맵으로 대체하면 효과가 큽니다.
- 캐릭터는 부품별 SkinnedMeshRenderer 수가 많으므로 최종 배포 단계에서 본별 장식을 합쳐 드로우콜을 줄이십시오.
