# S15P21B202-306 Stage1 바닥 텍스처·자연물 에셋 추가 기록

[문서 안내](../README.md) → [작업 기록](README.md) → 아트·레벨

Stage1 바닥 텍스처 후보와 자연물 FBX·PBR·프리뷰를 등록한 기록입니다. 원본 해시와 경로, 임포트 기준을 남기고 씬 배치·Material·Prefab 연결은 범위에서 구분합니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

## 작업 정보

- Jira: [S15P21B202-306](https://ssafy.atlassian.net/browse/S15P21B202-306)
- 브랜치: `S15P21B202-306-stage1-environment-assets`
- 기준 브랜치: `develop`
- 범위: 바닥 Base Color 후보 3종, 자연물 FBX/PBR 4종, 정면 프리뷰 및 카탈로그 등록
- 제외: 씬·Terrain·Terrain Layer·Material·Prefab·Collider·LOD·기존 자연물 배치 변경

## 입력 원본

| 원본 | SHA-256 |
| --- | --- |
| `moss_cliff.zip` | `B691E0D5E66EE82BE5B235363F9498D58164786C916326DC555E13322B46BE3B` |
| `moss_cliff_large.zip` | `4AAAC46023C5C5B5F19A39286224BB139551FD9F07948D4C364F24196145F0CD` |
| `mush_tree.zip` | `E774C7633104D353EEE70D9B4ED356BC7F82D0A4E2103DC2247B230E49879040` |
| `umbrella_tree.zip` | `D9CCEB197FE340D07C04422F6DDA79D75121BFD5838B6C1615C9247330431A90` |

바닥 텍스처 `GrassTexture.png`, `SandTexture.png`, `StoneTexture.png`와 각 `.meta`는 이 브랜치를 만들기 전 로컬 작업 트리에 있던 파일을 변경 없이 포함했다.

## 산출물 위치

| 구분 | 경로 |
| --- | --- |
| 자연물 원본 | `METAGAME/Assets/_Project/Models/Environment/Stage1/{moss_cliff,moss_cliff_large,mush_tree,umbrella_tree}/` |
| 바닥 텍스처 | `METAGAME/Assets/_Project/Textures/Environment/Stage1/{GrassTexture,SandTexture,StoneTexture}.png` |
| 정면 프리뷰 | `docs/assets/previews/{moss_cliff,moss_cliff_large,mush_tree,umbrella_tree}_front.png` |
| 카탈로그 | `docs/asset-catalog.md` |

## 구성과 임포트 기준

- 자연물마다 FBX 1개와 BaseColor·Normal·Roughness·Metallic·RM 텍스처 5장을 포함한다.
- FBX 내부 텍스처 참조를 위해 UUID가 들어간 `.fbm` 폴더명은 원본 그대로 보존한다.
- FBX는 Material·애니메이션·카메라·라이트를 자동 생성하지 않는 원본 보관 설정을 사용한다.
- BaseColor는 sRGB, Normal은 Normal map, Roughness·Metallic·RM은 선형 데이터로 임포트한다.
- 모든 Unity 에셋과 폴더에 `.meta`를 포함하며 신규 GUID는 저장소 전체에서 중복되지 않아야 한다.

## 확인 방법

1. Unity Project 창에서 `Assets/_Project/Models/Environment/Stage1`을 연다.
2. 신규 자연물 폴더 4개에 이름이 같은 FBX와 UUID `.fbm` 폴더가 있는지 확인한다.
3. 각 `.fbm` 폴더에 텍스처 5장과 `.meta`가 있는지 확인한다.
4. `Assets/_Project/Textures/Environment/Stage1`에서 풀·모래·돌 PNG 3종이 보이는지 확인한다.
5. `docs/asset-catalog.md`의 `Stage1 자연물·맵 타일 리뉴얼`에서 자연물과 바닥 후보 및 정면 프리뷰를 확인한다.

## 제한 사항과 후속 작업

- 신규 자연물 4종과 바닥 텍스처 3종은 원본 등록 상태이며 게임 씬에 연결되지 않았다.
- 실제 크기·축·피벗·폴리곤 수, 충돌체와 LOD, 바닥의 seamless 반복 여부는 후속 적용 단계에서 확인한다.
- Material과 Prefab을 만든 뒤 기존 Stage1 아트 스타일 및 성능 기준에 맞는지 검토해야 한다.
