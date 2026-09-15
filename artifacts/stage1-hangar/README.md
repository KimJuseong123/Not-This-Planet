# Astra Hangar — Blender MCP 모델링 결과

Stage1_Astra의 Part3 격납고 컨셉을 기반으로 Blender MCP에 연결된 Blender 5.2.1 LTS에서 제작했습니다. 기존 씬의 창고를 자동 교체하는 파일이 아니라, 가져오기와 배치가 가능한 모델 패키지입니다.

## 제공 파일

- `AstraHangar.fbx`: 17개 부위 메시 + 루트, 실제 미터 단위, -Z Forward / Y Up 내보내기.
- `AstraHangar.fbm/`: 2048×2048 PNG 텍스처 6종. FBX와 함께 같은 위치에 보관합니다. `.fbm`은 텍스처 폴더입니다.
- `AstraHangar.blend`: 831개 세부 메시와 비파괴 bevel modifier를 보존한 편집 원본. 텍스처도 packed 상태로 포함합니다. 기본 렌더 카메라·조명·촬영용 바닥은 FBX에 포함하지 않았습니다.
- `preview-*.png`: 외관, 정면, 후면, 위, 실내, 지붕을 숨긴 실내 구조 미리보기.
- `validation.json`: FBX 실제 재불러오기 결과 및 기하 검사 수치.

## 구조 및 상세

1. 흰색 아치 구조 프레임 4열, 검은 하부 받침, 라벤더 기둥 인서트와 체결 볼트.
2. 완전히 열린 전면 화물 출입구, 금속 문틀, 가스켓, 수납된 셔터 카세트, 청록 상태 조명.
3. 경사 진입 램프, 양측 미끄럼 방지 그레이팅, 두 줄의 화물 안내 레일과 발광 라인.
4. 좌우 3구획 측벽, 상부 창틀·유리·멀리언, 하부 서비스 패널과 실제 메시 환기 루버.
5. 오른쪽 전면 보조 출입문, 문틀, 발판, 키패드와 상태등. 문짝은 FBX에서 별도 메시입니다.
6. 닫힌 후면 외벽과 실내 벽 마감, 범퍼, 실내 작업등, 배관과 고정 클램프.
7. 분할 지붕 패널, 길게 이어지는 중앙 채광창, 프레임과 하부 보강봉.
8. 뒤쪽 환기 설비 2대, 흡배기 루버, 정비용 상판과 볼트.
9. 실내 천장 크레인: 좌우 종방향 레일, 브래킷, 이중 거더, 구동 바퀴, 모터, 트롤리, 권양기 드럼과 수납된 인양부.
10. 바닥 패널 72개, 배수 홈·그레이팅, 넓게 확보된 화물 통로.

문과 크레인은 후속 애니메이션 작업을 위한 분리된 **정적 메시**입니다. 실제 작동 로직·리깅·애니메이션은 포함하지 않습니다. 유리는 불투명한 짙은 청록 PBR 재질로 표현해 복잡한 투명 정렬을 요구하지 않습니다.

## 크기와 메시

| 항목 | 값 |
| --- | --- |
| 전체 폭 | 20.815m — 보조 출입문 발판 포함 |
| 전체 깊이 | 21.640m — 전면 램프 포함 |
| 전체 높이 | 10.665m — 지붕 설비 포함 |
| 본체 기준 크기 | 약 20 × 18m |
| 바닥 | 기초 상면 0.32m, 타일 상면 약 0.37m |
| 출입구 하단 유효 폭 | 약 16m |
| 수납된 권양기 아래 높이 | 타일 바닥 기준 약 6.56m |
| 원본 세부 메시 | 831개 |
| FBX 메시 | 17개 |
| FBX 정점 / 삼각형 | 80,152 / 156,980 |
| 재질 | 공통 PBR atlas 1개 |
| UV | UVMap: 재질 atlas 공유 / LightmapUV: 메시별 자동 전개 |

삼각형 수는 세부 베벨·루버·볼트를 포함한 상세 모델 기준입니다. 플랫폼에 맞는 LOD와 충돌체 단순화는 별도 작업입니다. 하나의 볼록 충돌체로 건물 전체를 감싸면 입구가 막히므로, Unity에서는 바닥·벽·램프를 구분해 충돌체를 구성해야 합니다.

## 텍스처 연결 — Unity URP Lit

텍스처들은 팔레트/트림 방식의 공통 atlas입니다. 주요 디테일은 실제 메시이며, Normal은 미세 표면 질감을 위한 맵입니다. UVMap을 전체 0–1 공간에 임의로 다시 펼치면 색 구획이 깨집니다.

| PNG | 용도 / 가져오기 |
| --- | --- |
| `AstraHangar_BaseColor.png` | Base Map / sRGB 켬, Base Color 흰색 |
| `AstraHangar_Normal.png` | Normal Map / Texture Type Normal map, 강도 약 0.35 |
| `AstraHangar_MetallicSmoothness.png` | Metallic Map / sRGB 끔, R=Metallic, A=1−Roughness, Smoothness Source=Metallic Alpha, Smoothness 1 |
| `AstraHangar_Emission.png` | Emission Map / sRGB 켬, Emission 활성화, 흰색 배수부터 장면 조명에 맞게 조정 |
| `AstraHangar_Metallic.png` | 다른 PBR 프로그램용 개별 Metallic / Non-Color |
| `AstraHangar_Roughness.png` | 다른 PBR 프로그램용 개별 Roughness / Non-Color |

Blender의 Principled 재질과 텍스처 참조는 FBX에 포함돼 있지만, URP 셰이더 자동 변환 결과는 Unity 설정에 따라 달라질 수 있습니다. 위 채널 표에 따라 URP Lit 재질을 연결합니다. 원본 Blender의 Emission Strength는 2.3입니다. Unity에서 발광 주변의 번짐은 Bloom 설정에 좌우됩니다.

## 검증

- FBX를 별도 Blender 씬에 실제 재불러오기: **통과**.
- 최종 `.blend`를 별도 Blender 프로세스로 다시 열기: 831개 모델 메시, packed 텍스처 5개, 누락 이미지 0개 확인. 추가 URP용 MetallicSmoothness는 `.fbm` 폴더에 포함됩니다.
- 재불러오기 전후 메시 수, 정점 수, 삼각형 수 일치.
- 전체 크기 오차 0.00001m 미만.
- 17개 메시의 UV 2채널 유지.
- FBX가 참조하는 텍스처 누락 0건. URP용 MetallicSmoothness는 추가 제공 맵입니다.
- 메시별 비매니폴드 에지·면적이 0에 가까운 면 검사: 0건. 건물은 여러 폐쇄 부품의 조합이며 하나의 연결 메시로 합친 형태는 아닙니다.
- 화물 통로 x=-6, 0, 6m의 높이 2m 전방 레이 검사: 출입구와 실내를 통과한 뒤 후면 벽에 도달.
- 외관·전면·후면·탑뷰·실내 렌더 검사 완료. 크레인 구동부와 지붕 경사면의 간섭 수정.
- Stage1_Astra 배치, Unity 조명/콜라이더/런타임 성능 검증은 이번 작업 범위에 포함하지 않았습니다.

재생성 및 내보내기 스크립트: 저장소 `tools/blender/build_astra_hangar.py`, `tools/blender/export_astra_hangar.py`. 출력 경로 `BASE`를 작업 환경에 맞춰 변경할 수 있습니다.

## Unity 프로젝트 내 에셋 위치 (2026-09-15)

FBX 및 .fbm 텍스처 폴더는 METAGAME/Assets/_Project/Art/Buildings/Hangar/ 로 이동했습니다. 아래 파일 목록은 제작 패키지 기준이며, Blender 원본·미리보기·검증 기록과 ZIP 패키지는 artifacts에 보관합니다. 이번 이동에서 Unity 씬은 변경하지 않았습니다.
