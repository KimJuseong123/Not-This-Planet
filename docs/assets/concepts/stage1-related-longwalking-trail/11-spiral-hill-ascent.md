# L11 · 나선형 오르막길

![나선형 오르막길 6뷰](11-spiral-hill-ascent.png)

## 컨셉

언덕의 외곽을 감아 올라가는 길 전체를 하나의 곡선형 시설로 정리했다. 기존 흰 프레임·라벤더 바닥판·청록 유도등을 유지하고, 곡률을 따라 사다리꼴 패널이 반복된다. 낮은 입구에서 높은 출구까지 계단 없이 이어지는 완만한 오르막이며, 중앙은 지형이 들어갈 수 있도록 비워 둔다. 상부 구간의 가는 지지대는 기존 조절식 지지대 에셋을 재사용하는 방향이다.

## 씬과의 관계

현재 씬의 Long walking trail 0~4는 여러 직선 구간이다. 별도 이름의 나선형 길 오브젝트는 검색에서 확인하지 못했다. 이번 시트는 사용자 요청의 나선형 오르막을 **핵심 파츠 2로 향하는 언덕 외곽 길 전체의 곡선 대안**으로 해석한 컨셉이다. 현재 씬 형상을 그대로 복원한 모델 도면이 아니며, 기존 L01~L05와 함께 중복 설치하는 추가 길도 아니다. 실제 적용 시 기존 노선과 비교해 선택한다.

## 제작 기준 제안

- 보행 폭 약 **4.4m**, 데크 두께 약 **0.6m**: 기존 보행로의 기준을 계승.
- 전체 상승 약 **7m**, 중심선 반지름 약 **14m**, 회전 약 **270°**를 초기 설계 목표로 제안. 이 경우 호 길이 약 66m, 평균 경사 약 6°이다. 새 곡선의 값이며 씬에서 측정한 반지름이 아니다.
- 이미지의 회전량·지지대 위치·뷰별 비례는 정확한 수치 도면이 아니다. 최종 입구·출구를 현재 지형 및 관측대 연결판에 맞추며 곡률을 결정한다.
- 처음과 끝은 개방된 접속면으로 두고, 상부 출구는 L06 연결판 또는 그 곡선 변형으로 관측대에 연결한다.
- 바닥판 경계는 평평하게 유지한다. 높은 연석과 볼라드는 유효 운반 폭을 침범하지 않도록 외측에 배치한다.
- 실제 모델은 곡선 구간 여러 개와 양끝 접속부로 분할 제작하는 편이 지형 대응에 유리하다. 통합 외형 컨셉 1종이며 세부 모듈 도면은 별도다.

## 뷰와 검토

한 장에 전체 컨셉 / 정면 / 오른쪽 / 왼쪽 / 위 / 뒤를 구성했다. 상단은 CONCEPT·FRONT·RIGHT, 하단은 LEFT·TOP·BACK 순서다. 정면은 낮은 진입구 쪽을 기준으로 삼는다. 일부 방향 뷰에 투시가 남아 있어 정밀 정투상 자료로 쓰려면 단일 모델로 재검증해야 한다.

기존 분위기, 연속 경사, 열린 중앙부, 여섯 패널을 시각 확인했다. 씬 배치·충돌·플레이 검증은 수행하지 않았다. 내장 이미지 생성 도구로 제작했고 Unity 씬은 변경하지 않았다.

## 생성 프롬프트

```text
Use case: stylized-concept. Create ONE polished landscape 1536x1024 game environment concept art sheet titled "L11 | SPIRAL HILL ASCENT". EXACTLY six panels in a 3 column by 2 row grid labeled in reading order "CONCEPT", "FRONT", "RIGHT", "LEFT", "TOP", "BACK". Show the SAME complete gently ascending curved ramp assembly in all six panels. CONCEPT is a beautiful three-quarter elevated perspective; FRONT, RIGHT, LEFT, BACK are clear orthographic elevations; TOP is true overhead. Same topology, rise, entry and exit in all views.
SUBJECT: a wide continuous walkable uphill path that wraps three quarters of a circle around an OPEN CENTER, about 270 degrees total turn, NOT a tight corkscrew and NOT a staircase. ONE gradual rising C-shaped ribbon, no stacked repeated coils, no full closed ring. Broad 4.4m usable walking width, approximately 0.6m deck thickness, centerline radius around 14m, total rise about 7m over approximately 66m curved path. Lowest entry at front/south, route follows around the east then north towards a higher west exit; uphill path is a continuous 270-degree curve, ends are distinct and unconnected, with short open straight approach lips. Deck floor gently rises all along the arc, NEVER any steps or stairs. There is NO central tower, NO elevator, NO observatory building, NO decorative circular platform filling the center.
Stage1_Astra visual language: pearl white rounded chamfered outer frames, broad matte muted lavender trapezoid floor panels following the curve, narrow cyan edge guide strips, dark titanium underside with restrained structural ribs. Sparse slim adjustable support feet under high sections make structure legible without filling the open center. Low white edge curbs, no full-height guardrails. Friendly clean space-exploration facility, slightly softened edges, no rusty military grunge.
All objects fit fully in their panels. Every orthographic elevation clearly distinguishes lower entry from high exit. TOP shows the open C-shaped continuous route and two separate ends, not concentric disconnected circles. Neutral uniform OPAQUE pale gray-white background, soft studio shadows, crisp DARK readable labels, subtle panel dividers. No dark gradients, no terrain, no people, no annotations or dimension text, no logo, no watermark. This is a whole-route curved design proposal related to five existing straight walkway modules.
```

