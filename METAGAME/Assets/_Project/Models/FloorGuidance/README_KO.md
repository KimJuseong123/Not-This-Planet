# Astra 바닥 유도등 · 배치용 에셋
2026-09-16 · S15P21B202-560-art · Blender MCP 제작

[모델 미리보기](Previews~/index.html) · [Unity 검증 결과](unity-validation.json)

## 구성
| 폴더 | 모델 / 프리팹 | 실제 외곽 크기 X×Y×Z (m) | 삼각형 |
|---|---|---|---|
| 01-floor-light | SM_Astra_FloorLight / PF_Astra_FloorLight | 0.352 × 0.0351 × 0.12265 | 4,252 |
| 02-direction-tile | SM_Astra_DirectionTile / PF_Astra_DirectionTile | 0.220 × 0.0251 × 0.220 | 4,240 |
| 03-stair-edge | SM_Astra_StairEdgeLight / PF_Astra_StairEdgeLight | 0.400 × 0.0601 × 0.0809 | 3,780 |
| 04-terrain-light | SM_Astra_TerrainLight / PF_Astra_TerrainLight | 0.380 × 0.1401 × 0.140 | 5,916 |

각 폴더에 FBX 1개, 같은 이름의 .fbm 텍스처 폴더, URP Lit 재질 5개(외장 + 청록/앰버/라임/오키드 렌즈), 프리팹 1개, Blender 재가져오기 검증 기록이 있다. 미리보기와 편집 가능한 Blender 원본은 Unity가 자동 임포트하지 않는 Previews~/Source~에 보관했다.

## Unity에서 사용
1. 원하는 PF_Astra_*.prefab을 Scene으로 드래그한다. 단위는 미터이며 Scale=(1,1,1)을 기준으로 한다.
2. 렌더러의 재질 슬롯 0은 Body, 슬롯 1은 Lens다. 색상을 바꾸려면 슬롯 1에 같은 모델 폴더의 Lens_Amber / Lens_Lime / Lens_Orchid를 할당한다.
3. 원하는 다른 색은 Lens 재질을 복제한 뒤 Base Map의 색상과 Emission HDR 색상/강도를 조절한다. Body는 그대로 둔다. 여러 오브젝트가 같은 재질을 공유하면 그 재질 수정이 모두 반영된다.
4. 발광은 표면 emission이며 주변을 비추는 실시간 Light는 포함하지 않았다. 최종 씬의 노출/Bloom에 맞게 밝기를 조절한다.

## 배치 기준
- FBX는 모두 하단 중앙 원점, Y-up, 정면 -Z. 모서리/점검판 돌출을 포함한 크기는 위 표를 따른다.
- 기본등·방향 타일: 프리팹 원점을 지면 높이에 맞춘다. 방향 타일 화살표는 로컬 +Z를 가리킨다.
- 계단등: 프리팹 원점은 전면 하단이다. 계단 상단보다 약 0.061m 낮추고 발광면(-Z)을 내려가는 쪽으로 향하게 한다. L자 형태에 맞춘 BoxCollider 2개가 있다.
- 지면 설치형: 프리팹은 FBX 자식을 Y=-0.089m 이동해 크래들 바닥이 지면에 닿고 핀이 땅속으로 들어간다. 프리팹 원점은 지면 기준이며 원본 FBX의 핀 끝 기준과 다르다. 콜라이더는 지상 크래들/등체만 포함한다.
- 나머지 프리팹은 단순 BoxCollider 1개. Rigidbody나 네트워크 컴포넌트는 추가하지 않았다. 장식물로 쓸 때는 Collider를 끌 수 있다.

## 색상을 고정하지 않는 텍스처
각 FBM 폴더에는 512×512 PNG 6개가 있다.
- BaseColor: 화이트·라벤더·티타늄 팔레트. 렌즈 셀은 중립 회색이며 청록을 굽지 않았다.
- Emission: 렌즈 셀만 흰색인 흑백 마스크. 컬러는 재질에서 정한다.
- Metallic / Roughness: 다른 DCC 툴에서도 사용할 수 있는 별도 데이터 맵.
- MS: Unity URP용 R=Metallic, A=Smoothness(1-Roughness). 선형 데이터로 임포트.
- Normal: 중립 탄젠트 노멀. 주요 디테일은 지오메트리로 제작했다.

UV0는 여백 있는 팔레트 셀을 공유하는 재질 UV이며 고유 페인팅용 비중첩 전개가 아니다. UV1은 따로 패킹한 라이트맵 UV다. 외장과 렌즈는 하나의 메시 안에서 2개 재질 슬롯으로 분리되어 있다.

## 컨셉에서 조정한 부분
- 정면/측면 참고 이미지의 미세한 불일치는 좌우 대칭 구조로 통일했다.
- 코너 나사, 육각 소켓, 앞쪽 슬롯, 뒤쪽 점검판, 렌즈 베젤, 가스켓, 계단 미끄럼 방지 홈을 입체로 제작했다.
- 끝판과 외장 상면의 겹침을 제거했고, 폭은 범퍼 돌출을 포함해 352mm로 조정했다.
- 지면 설치형은 기본 조명에 크래들·칼라·8각 고정핀을 결합했다.
- 여러 개를 배치하기 쉽게 모델당 한 메시로 통합했다. LOD는 포함하지 않으므로 대량 배치 시 거리별 최적화는 별도 적용한다.

## 검증
- Blender 5.2.2 LTS MCP에서 4종 생성 → FBX export → 새 씬에서 reimport.
- 4종 모두 크기/삼각형 수/UV 2개/재질 2개 유지. 누락 텍스처·비다양체 에지·퇴화 면 0.
- Unity 6000.5.4f1 / URP 17.5.0의 독립 검증 프로젝트에서 실제 FBX/텍스처 임포트, 외장/렌즈 재질 연결, 스케일/축, 프리팹 저장을 확인했다.
- 최종 Unity 검증 PASS, 오류 0. 메타파일은 Unity가 생성한 것을 에셋과 함께 전달했다.
- Blender에서 4종 및 광색 3가지 추가 미리보기를 렌더링해 확인했다. 미리보기는 Unity 최종 씬 화면이 아니다.
- Stage1_Astra 실제 씬 배치, Play Mode 충돌/동선, 최종 씬 조명/Bloom 및 플레이어 빌드는 검증하지 않았다. 기존 씬은 수정하지 않았다.

## 제작 재현
저장소 tools/blender/build_floor_guidance.py를 Blender MCP에서 실행해 build_asset(0..3), render_asset(0..3)를 호출한다. 새 Blender 세션에서 실행하는 것을 권장한다.
tools/unity/prepare_floor_guidance_validation.py는 로컬 캐시 패키지를 이용해 tmp/floor-guidance-unity 검증 프로젝트를 만든다. tools/unity/BuildFloorGuidance.cs의 Run을 batchmode로 실행하면 Unity 재질/프리팹/메타파일을 생성하고 검증한다.
검증 프로젝트의 Assets/FloorGuidance 결과를 이 폴더로 복사할 때 .meta도 함께 보존한다.

이 패키지는 ZIP을 생성하지 않고 직접 저장했다. 최종 에셋 폴더의 ZIP 파일 수는 0이다.

