# S15P21B202-594 — 쉘터 내부 도구 거치대

2026-09-17. 작업 브랜치 `S15P21B202-594-art`에 로컬 `develop`의 `d8c573d1`을 fast-forward 머지한 뒤 작업했다.

## 배치

`METAGAME/Assets/Scenes/Stage1_Astra.unity`의 플레이어 스폰 쉘터 측면 창가에 두 거치대를 배치했다. Hierarchy의 `Shelter - Tool Displays`에서 찾을 수 있다. 기존 게임 도구 `MeteorBat_Stage1`, `WeldingPlasmaGun_Stage1`를 옮겨 거치했으며 복제된 장식 도구를 추가하지 않았다. 기존 도구의 부모 계층과 이름, 스폰 Transform, 쉘터 구조, 진입로는 유지했다.

| 항목 | 월드 위치 | Y 회전 | 균일 배율 |
|---|---|---|---|
| PF_MeteorBatStand | (-31.4, 0.30, -20.7) | 180° | 1.0 |
| PF_PlasmaWeldingGunStand | (-29.8, 0.30, -20.7) | 180° | 0.67 |

거치대 앞면이 실내를 향한다. 방망이는 손잡이 끝을 하단 컵에 넣고 몸통을 상단 받침에 기댔다. 용접건은 손잡이 하단과 몸통을 각각 뒤쪽 컵과 앞쪽 받침에 맞췄다. 도구 자체의 기존 크기는 유지했다.

## 에셋 및 동작

사용자가 제공한 `Assets/_Project/Models/meteor-bat`, `plasma-welding-gun` FBX/FBM을 사용했다. 원본 모델 재수출 없이 Unity에서 메타데이터, URP Lit 재질, 재사용 가능한 거치대 프리팹을 생성했다. 각 재질은 해당 FBM의 BaseColor와 Normal을 참조한다. 거치대는 Ground 레이어의 정적 MeshCollider를 사용한다.

단순 물리 배치에서는 방망이가 넘어져, 초기 거치만 유지하는 `ToolDisplayMount`를 두 기존 도구에 연결했다. 런타임 FixedJoint가 처음 위치를 유지하며 도구 Rigidbody의 dynamic/gravity/constraints 설정은 바꾸지 않는다. 거치 중에만 해당 거치대와의 충돌을 제외한다. `ToolBase.Equip` 또는 원격 held 상태가 적용되면 고정을 해제하고 충돌을 복원한다. 원격 참가자가 이미 내려놓은 도구 위치를 처음 수신한 경우에도 초기 거치로 끌어당기지 않는다. 꺼낸 도구를 자동으로 재거치하는 기능은 추가하지 않았다.

## 검증

- Unity 6000.5.4f1에서 임포트 및 컴파일 완료.
- `ToolDisplayMountTests`, `ToolCarryCollisionTests`, `FirstPersonToolVisualTests`: **20/20 통과**, 실패/건너뜀 0.
- 실제 씬의 거치대와 도구를 분리된 검사 씬으로 복제하여 Play Mode에서 지구 중력 9.81 m/s²로 10초 시뮬레이션: 두 도구의 이동 및 회전 변화 0.
- PlayerRig의 실제 조준 → 집기 → 양손 장착 → 고정 해제 → Q 내려놓기 경로를 두 도구 모두 통과. 이후 dynamic, gravity, 충돌 복원과 외력에 따른 이동 확인.
- 원격 held 전환, 이미 이동한 kinematic 네트워크 표시 객체, 컴포넌트 비활성화 시 고정/충돌 제외 해제 확인.
- 씬 직렬화는 기존 5개 블록만 변경하고 8개 블록을 추가했다. 관련 없는 기존 5,672개 블록을 그대로 보존했다. 도구의 기존 충돌 형상도 유지했다.
- 새 에셋의 메타데이터, 재질/텍스처/셰이더/프리팹 GUID 참조 및 씬 내부 참조 검사 통과.
- 실내 전체, 스폰 시점, 각 도구 근접 이미지를 확인했다. 스폰과 출입 동선을 가로막지 않는다.
- 검사 실행으로 생긴 렌더링/물리 프로젝트 설정 변경과 임시 에디터 스크립트는 제거했다.

검증 범위: 실제 다중 클라이언트 접속 및 플레이어 빌드는 실행하지 않았다. 전체 매치 대신 원본 배치를 복제한 Play Mode 검사로 물리와 집기 흐름을 확인했다. 초기 씬 검사에서 기존 운석 파편 prefab 참조 타입 경고가 있었으며 이번 작업에서 해당 데이터를 변경하지 않았다. 편집 모드 오프스크린 렌더에서는 기존 PartOutlineTargets의 material 접근 메시지가 발생했다.

## 결과 자료

- [실내 배치](../../artifacts/shelter-tool-stands/shelter-inside.png)
- [방망이 근접](../../artifacts/shelter-tool-stands/meteor-bat-mounted.png)
- [용접건 근접](../../artifacts/shelter-tool-stands/plasma-welding-gun-mounted.png)
- [스폰 시점](../../artifacts/shelter-tool-stands/spawn-view.png)
- [Play Mode 기록](../../artifacts/shelter-tool-stands/play-validation.txt)
- [테스트 결과](../../artifacts/shelter-tool-stands/tests.xml)
- [씬 변경 감사](../../artifacts/shelter-tool-stands/serialization-audit.json)
- [에셋 참조 검사](../../artifacts/shelter-tool-stands/asset-validation.json)
