# Stage1 Astra · Hangar 주변 모델

`docs/assets/concepts/stage1-related-hangar`의 13개 컨셉 시트를 바탕으로 Blender MCP에서 제작한 정적 환경 모델이다.

## 파일 구성

각 번호 폴더는 독립적으로 사용할 수 있다.

- `SM_Astra_*.fbx`: 미터 단위, 단일 메시, 단일 재질, 삼각형 처리된 게임용 모델.
- `SM_Astra_*.fbm/`: 2048px BaseColor / Metallic / Roughness / Normal / Emission / MetallicSmoothness PNG 6개.
- `M_Astra_*.mat`: 프로젝트의 기존 Hangar URP Lit 설정을 바탕으로 만든 재질. FBX importer의 재질 매핑과 연결되어 있다.
- `preview.png`: 실제 제작 모델을 Blender에서 렌더한 미리보기.
- `asset-info.json`: 치수, 삼각형 수, 원본·검증 파일 위치, FBX 체크섬.
- `.meta`: Unity GUID, 텍스처 색공간, 노멀맵 유형, 모델 재질 매핑.

FBM은 파일 확장자가 아니라 **FBX에 딸린 텍스처 폴더**다. FBX와 같은 번호 폴더 안에서 함께 이동한다. ZIP은 생성하지 않으며 최종 폴더에도 두지 않는다.

## 모델별 설계

| 번호 | 모델 | 구현 내용 |
|---|---|---|
| 01 | SouthApproachWalkway | 길이 14.1m 보행 데크. 교체식 라벤더 패널, 매입등, 서비스 손잡이, 볼트, 하부 가로·세로 보강재. |
| 02 | EastEntranceLanding | 3.5 × 9.5m 랜딩. 넓은 평탄 통행면과 이중 진행 화살표. |
| 03 | PedestrianAccess | 폭 3.2m, 경사 길이 17.144428m, 높이차 5.33m의 경사로. 15개 구간에 미끄럼방지 3연 리브. |
| 04 | FoundationApproachDeck | 23.1 × 15.5m 기초 데크. 넓은 패널 격자, 유지보수 손잡이와 체결구, 낮은 측면 외피. |
| 05 | BrokenPersonnelBridge0 | 작은 모서리 파손, 후면 체결 귀와 소켓, 미세 균열. |
| 06 | BrokenPersonnelBridge1 | 좌우로 갈라진 상판, 들뜬 옆판, 드러난 대각 구조재. |
| 07 | BrokenPersonnelBridge2 | 후면 불규칙 절단면, 노출된 I형 보 끝, 황색 유도등. |
| 08 | BrokenPersonnelBridge5 | 정면 홈 형태의 절단면. 2번의 반대편 끝을 표현. |
| 09 | BrokenPersonnelBridge6 | 도착부 후면 체결 귀, 반대쪽 모서리 파손. |
| 10 | ModularEdgeCurb | 2m 공용 연석. 흰 외피, 라벤더 측판, 연속 청록 렌즈, 낮은 장착부. |
| 11 | CargoApronEntryStairs | 4단 계단. 기존 단 간격 0.48m / 높이차 0.19m 유지, 미끄럼방지 발판과 하부 스트링거. |
| 12 | AdjustableDeckSupport | 약 4.8m 지지대. 텔레스코픽 기둥·칼라, 상부 가로 보, 정비 해치, 배관, 기초 볼트. |
| 13 | RouteWayfindingBeacon | 높이 1.6m 안내 표지. 입체 발광 화살표와 격납고 기호, 후면 통풍구·상태등. |

교량 5종은 폭 2.6m, 깊이 1.15m, 기본 두께 0.4m를 공유한다. 체결 귀와 볼트 등은 기본 판 치수 밖으로 조금 돌출된다. 실제 전체 외곽 치수는 `asset-catalog.json`에 기록되어 있다.

## 재질과 UV

- 기존 `Art/Buildings/Hangar/AstraHangar.fbm` 팔레트·PBR 텍스처를 각 모델의 FBM에 복사해 사용했다. 기존 Hangar와 재질이 이어지며 외부 텍스처 폴더에 의존하지 않는다.
- UV0는 16개 팔레트 셀의 내부를 사용하는 면별 투영이다. 판 이음새, 파손, 볼트, 렌즈 등은 실제 형상이다.
- UV1은 별도로 펼치고 패킹한 라이트맵 좌표다. Unity의 자동 Secondary UV 생성은 꺼두었다.
- BaseColor/Emission은 sRGB, Metallic/Roughness/MetallicSmoothness는 선형, Normal은 노멀맵 유형이다.
- Unity용 MetallicSmoothness는 R=Metallic / A=Smoothness. Roughness는 Blender 및 다른 도구용으로 함께 제공한다.

## 배치할 때

- 실제 크기로 제작했으므로 기본 Scale은 `(1, 1, 1)`이다. 기존 회색 박스의 큰 Scale을 새 FBX에 그대로 적용하지 않는다.
- Blender 원본은 X=폭, Y=길이, Z=높이이며 앞은 -Y다. FBX는 `-Z forward / Y up`으로 내보냈다. Unity에서 진행 방향을 확인해 Y 회전을 맞춘다.
- 평판 원점은 바닥 중심이다. 기존 씬 박스는 중심점 기준이므로 바닥 높이 또는 상면을 기준으로 위치를 조절한다.
- **03 경사로는 경사를 형상에 포함했다.** 원래 경사로의 피치 각도를 중복 적용하지 않는다. 낮은 끝과 높은 끝을 먼저 맞추고 수평 회전만 조절한다. 부착 리브가 바닥보다 약간 높다.
- 07(교량2)의 후면 절단과 08(교량5)의 정면 절단을 서로 마주 보게 배치한다. 3·4번 공백을 장식 보로 연결하지 않는다.
- 10 연석은 남쪽·동쪽 공용이며 길이 끝부분은 원본에서 조절할 수 있다.
- 12 지지대는 현재 고정 메시다. 텔레스코픽 구조는 시각적 표현이며 자동 높이 조절이나 애니메이션은 제공하지 않는다.
- `Assets/Scenes/Stage1_Astra.unity`의 `Hangar Related - Concept Models`에 13종, 총 25개를 배치했다. 원래 블록 오브젝트 15개는 제거했다. 보행 충돌은 별도 박스/경사 충돌체로 구성했다.
- 경사로는 데크 앞쪽에 맞춰 길이를 조절하고 입구 연석을 열었다. 파손 교량은 경사로 간섭을 피하도록 동쪽으로 2m 옮겼다. 화물 계단은 원형 플랫폼의 실제 외곽에 맞춰 남쪽으로 약 3.38m 이동하고 높이를 맞췄다.
- 연석 10개, 지지대 2개, 안내 표지 3개를 포함한다. 충돌체는 씬에 저장되며 FBX 원본은 독립적인 외관 자산으로 유지한다.

## 편집 원본과 검증

편집 가능한 `.blend`는 저장소 루트의 `artifacts/stage1-related-hangar/<번호-이름>/`에 있다. Unity의 `.blend` 자동 가져오기로 동일 모델이 중복 유입되지 않도록 Assets 밖에 보관했다. 각 원본은 개별 부품, 재질, 텍스처, 스튜디오 카메라·조명을 포함한다.

`validation.json`에는 Blender FBX 재가져오기 검증을 기록했다. 별도의 Unity 배치·충돌·저장 검증과 실제 씬 미리보기는 `artifacts/stage1-related-hangar/integration/`에 있다. 통로 검사와 플레이 시작 검증은 전체 게임 플레이 또는 성능 검증을 대신하지 않는다.

이음새와 볼트가 실제 형상이므로 특히 04 기초 데크는 비교적 고밀도 모델이다. LOD와 모바일 성능 검증은 별도로 수행해야 한다.
