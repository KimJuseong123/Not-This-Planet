# Stage1_Astra — 메인 조립·발사대 컨셉

## 디자인 의도
기존 Central Assembly Dock 위에 설치하는 중앙 조립 장치다. 핵심 파츠를 가져와 수직으로 쌓고 용접한 뒤 탈출 로켓으로 완성하는 목적을 표현한다.

기존 FBX의 씬 적용 미리보기와 FBM 베이스컬러에서 확인한 아이보리 외장, 연보라 패널, 청록 발광, 작은 호박색 상태등을 따른다. 낮은 고정턱 네 개와 평평한 중앙 받침은 조립 기능을, 후방의 독립된 에너지 기둥 두 개와 동심원 발광은 외계 기술을 이용한 발사 보조 기능을 표현한다. 후방 기둥 사이에는 가로 구조물을 두지 않아 위쪽 발사 경로를 비운다.

## 이미지
- 01-mood.png: 기존 도크 및 외계 행성 배경에 배치한 전체 분위기.
- 02-front.png: 정면.
- 03-left.png: 오브젝트 왼쪽에서 본 측면.
- 04-right.png: 오브젝트 오른쪽에서 본 측면.
- 05-top.png: 위. 이미지 아래가 정면, 위가 후면.
- 06-back.png: 후면.

방향별 이미지는 바깥쪽 Central Assembly Dock을 제외한 중앙 조립 장치만 보여준다. 정면 기준 오른쪽에 조작판, 후면 양쪽에 기둥을 둔다. 왼쪽 측면에서는 앞이 화면 왼쪽, 오른쪽 측면에서는 앞이 화면 오른쪽이다.

## 참고한 기존 자료
- METAGAME/Assets/_Project/Models/AssemblyParts/central-assembly-dock/tripo_convert_55de9302-5ec4-4a8a-a0a9-7245ab05152b.fbx
- 같은 폴더의 FBM: central-assembly-dock_basecolor.JPEG 직접 확인.
- docs/assets/previews/stage1-assembly-platforms/CentralAssemblyDock-scene.png — 기존 FBX의 씬 적용 모습과 환경.
- docs/assets/concepts/stage1-large-buildings/hangar/01-concept.png — 건축물 형태와 재질.
- artifacts/stage1-related-hangar/04-foundation-approach-deck/preview.png — 기존 모듈 바닥.
- docs/stage1-assembly-platform-integration.md — 도크 크기 및 배치 이력.
- METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/AssemblyDock.cs — 런타임 고정턱 네 개의 생성 및 동작.

## 모델링 시 참고
이 파일은 컨셉 제안이며 Unity 씬이나 FBX를 수정하지 않았다. 현재 런타임 고정턱은 높이 0.12이고 충돌체가 없는 시각 장식이다. 컨셉의 기둥, 받침과 조작판은 신규 제안이므로 실제 적용 시 파츠·운반장치의 진입 공간, 용접 접근성, 현재 도킹 높이를 별도로 맞춰야 한다. 받침 상면은 기존 도킹 기준 높이를 유지하도록 바닥에 매립하는 방향을 권장한다.

AI가 생성한 방향별 컨셉은 정밀 CAD 투영도가 아니다. 미세한 패널·부품 위치의 차이는 최종 모델링 단계에서 하나의 구조로 통일해야 한다. 컨셉 내 장치 크기는 시각 제안이며 실제 치수 검증 결과가 아니다.

## 제작
내장 image_gen 사용. 전체 분위기를 먼저 제작한 뒤 이를 공통 참조로 다섯 방향 이미지를 생성했다. 실제 생성 프롬프트는 PROMPTS.md에 기록한다.

