# Stage1_Astra Shelter/Hangar 교체

## MR 설명

Stage1_Astra의 기존 Shelter와 Hangar 외형을 제작한 FBX 프리팹으로 교체하고, 기존 파츠 단상과 연결되도록 크기·방향·기초·접근로를 조정했습니다. 사용자 요청에 따라 플레이어는 Shelter 안에서 출구 방향으로 시작합니다. 새 외벽과 겹치는 캠프 안내등 및 Hangar 지붕 안내등의 위치를 조정했고, 교체 중 잘못 삭제된 Part3 비콘을 원래 재질과 길이로 복구했습니다.

Stage1Buildings는 `Tools > Not This Planet > Buildings`에서 수동 실행하는 에디터 도구입니다. 자동 초기화, EditorApplication.update 등록, 외부 요청 파일 읽기·명령 실행, 자동 Play Mode 조작 코드는 제거했습니다. 브랜치를 가져오거나 스크립트를 import하는 것만으로 씬을 변경하지 않습니다.

## 에셋과 씬 변경

- `METAGAME/Assets/_Project/Art/Buildings/{Shelter,Hangar}/`: FBX, FBM 텍스처, URP 재질, 프리팹, 경사로 충돌 메시와 메타 파일.
- `METAGAME/Assets/Scenes/Stage1_Astra.unity`: 구형 건물 제거 및 새 건물 배치, 아래 동선·안내 변경.
- `METAGAME/Assets/_Project/Scripts/Editor/Stage1Buildings.cs`: 수동 준비·교체·검사·보정 메뉴. 기존 설치 중복 실행 차단, 씬 명령은 Astra 편집 모드 확인 후 실행.

## 스폰·비콘·볼라드 변경 이유

| 대상 | 변경 | 이유 |
| --- | --- | --- |
| AuthoredMapSettings.playerSpawn | (-22, 0.4, -29) → (-31.8, 0.346, -24), Y=90° | 사용자가 건물 교체와 함께 Shelter 내부 시작점을 명시적으로 요청. 미리보기 스폰 표시도 일치시킴 |
| 볼라드 26 | (-29, 0, -29) → (-24.2, 0, -26) | 새 Shelter 외벽과 겹침을 해소하고 출구 옆을 안내 |
| 볼라드 27 | (-26.8, 0, -29) → (-24.2, 0, -22) | 반대쪽 출구 옆 안내 |
| 볼라드 37 | x/z 유지, y=11.7 → 약 12.034 | 새 Hangar 지붕에 안내등을 안착 |
| Part3 비콘 | 보라색 재질·10m 길이·x/z 유지, 중심 y=19.2 → 약 19.622 | 잘못 삭제된 비콘을 복구하고 지붕 안내등 상단에 연결 |
| Hangar 보행 접근로·기초 | 남쪽 접근로를 동쪽 출입구/적재장으로 연결, 기초 확장 | 새 건물 내부와 기존 단상 사이에 이동 공간 확보 |

핵심 파츠 4개의 위치·상태와 단상은 유지했습니다. 런타임 스폰 코드나 파츠 게임플레이 코드는 변경하지 않았습니다.

## 검증과 한계

통합 당시 Unity 6000.5.4f1에서 스크립트 컴파일·에셋 import·씬 저장을 완료했습니다. 누락된 스크립트 0개, 재질 검사 통과, Hangar 접근로의 122개 지점에서 바닥과 몸통 통과 공간을 확인했습니다. 실제 Play Mode에서 플레이어가 Shelter 내부에 안정적으로 생성된 뒤 출구를 통과했습니다. Part3 비콘은 1개이며 원래 재질, 길이, 지붕 위 표시를 확인했습니다.

멀티플레이 동시 생성 및 전체 게임 완주는 검증하지 않았습니다. 자동 Play Mode 검사 코드는 통합 완료 후 제거했으며 검사 결과만 보관합니다.

상세 배치 수치, 실제 Unity 미리보기 및 검증 기록: [통합 기록](../../artifacts/stage1-building-integration/README.md).
