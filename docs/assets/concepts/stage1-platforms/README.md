# Stage1 조립대·파츠 배치 장소 컨셉아트
작성: 2026-09-15. 생성: 내장 image_gen. 모델링 방향 제안이며 씬 적용이나 FBX 제작은 포함하지 않는다.

## 산출물
- [중앙 조립대](central-assembly-dock-v1.png): 낮은 원형 받침, 사방 경사로, 중앙 도킹판과 네 잠금 장식. 빈 모습·평면·측면·잠금 상세·적층 예시.
- [Part_1 남서 엔진 정비대](part1-engine-camp-v1.png): 둥근 사각형 바닥, 평행 정비 띠, 밀어서 반출하는 낮은 경사로.
- [Part_2 북동 언덕 관측대](part2-hover-observatory-v1.png): 원형 바닥, 방사형 눈금, 언덕 접근 경사로.
- [Part_3 북서 격납고 운반 데크](part3-capsule-hangar-v1.png): 사각 격납고 바닥 → 넓은 연결로 → 야외 원형 인양대.
- [Part_4 남동 견인 도크](part4-tractor-dock-v1.png): 둥근 팔각형 바닥, 상태등 네 칸, 맞은편 접근 경사로.

## 분위기 분석
반입된 Stage1 우주선 4종의 저장 프리뷰를 시각적으로 확인했다. 공통 요소는 아이보리 외장, 라벤더 테두리·패널, 작은 청록 조명, 알 형태의 둥근 실루엣과 두꺼운 모서리다. 환경 프리뷰의 보라색 버섯 수관·꽃, 남색 잎, 올리브 이끼와 둥근 암석을 연결해 친근한 외계 탐사기지 분위기로 해석했다. 작업대는 작은 가구보다 우주선 크기의 낮은 지상 설비로 설계했다.

확인한 자료:
- ../../previews/SF_Stage1_Spaceship_Robot.png
- ../../previews/SF_Stage1_Spaceship_HoverPod.png
- ../../previews/SF_Stage1_Spaceship_Capsule.png
- ../../previews/SF_Stage1_TractorCraft.png
- ../../previews/mush_tree_front.png
- ../../previews/purple_flower_large_front.png
- ../../previews/moss_cliff_front.png
- ../../previews/stage1_nature_layout_262.png
- docs/asset-catalog.md — FBX와 프리뷰 대응 관계
- METAGAME/Assets/_Project/Models/Spaceships/Stage1/*.fbx — 로컬 파일 존재 확인
- METAGAME/Assets/Scenes/Stage1_Astra.unity — 저장된 씬 계층·Transform 확인
- METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartSpec.cs
- METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/AssemblyDock.cs
- docs/records/S15P21B202-509-simplify-tractor-controls.md

이번 분석은 기존 FBX 프리뷰와 직렬화된 씬에 기반한다. Unity 실행 화면을 새로 캡처하거나 FBX를 새로 렌더링하지 않았다.

## 씬에서 확인한 배치
아래는 파츠 루트의 저장된 localPosition이며 바닥 표면 높이와 다르다.
| 파츠 | 위치 (x,y,z) | 주변 장소 |
| --- | --- | --- |
| Part_1 엔진 | (-20, 1.6065, -20) | 남서 시작 기지. Habitat front deck은 별도 위치 (-29,0.2,-26)에 있어, 제안 정비대는 기존 바닥 그대로의 복제가 아니다. |
| Part_2 부양·안정화 | (28, 8.22, 19) | Hill observatory |
| Part_3 거주 | (-29, 6.5335, 25) | Hangar slab. 야외 Cargo pickup apron 중심은 (-9.5,5.72,29) |
| Part_4 상단·견인 | (22.83, 2.069, -19) | Tractor dock |

원형 deck들의 저장된 localScale.y는 0.1이며, 별도 foundation을 포함한 전체 높이로 해석하지 않았다. 이미지의 비례는 디자인 제안이고 씬의 정확한 축척 도면이 아니다.

## 모델링 전달 기준
- 공통 재질: 아이보리 외장 / 라벤더 바닥·테두리 / 청록 발광 / 어두운 틈. 경로별 앰버·청록·오키드·라임은 작은 보조 표시로만 사용.
- 메시 분리: 바닥 본체, 중앙 판, 테두리 반복 조각, 경사로, 고정못 소켓, 조명. 지형·식생·우주선 파츠는 받침대와 분리.
- 중앙 조립대는 Part_1만 자동 도킹하고 상부는 적층·용접하는 기존 규칙을 따른다. 발사 공간과 접근로를 열어 둔다.
- 배치 장소 4종은 파츠 반출 후 남는 고정 바닥이다. 컨셉의 소켓·상태등은 시각적 제안이며 새로운 퍼즐·잠금 규칙을 뜻하지 않는다.
- 소스 위치의 못은 기존 상호작용 에셋·위치를 우선한다. 아트 속 핀 모양을 새 기능으로 해석하지 않는다.
- 그림의 우주선은 크기·분위기 전달을 위한 재해석이다. 새 착륙 다리 등 생성된 디테일을 기존 FBX 변경 지시로 해석하지 않는다.
- 다각도 이미지에는 경사로 수·소켓 표시 등 세부 차이가 있다. 제작 기준은 각 시트의 TOP / EMPTY를 우선하고, Part_1 경사로는 서로 마주 보는 두 개로 통일한다. 실제 콜라이더·파츠 하단과 맞춰 치수를 확정한다.
- 이미지의 안개·배경 풍경은 분위기 표현이며 현재 씬의 조명·지형 변경 지시가 아니다.

## 생성 프롬프트
원문 전체는 [prompts.md](prompts.md)에 저장했다.

