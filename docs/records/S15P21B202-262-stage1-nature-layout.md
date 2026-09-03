# S15P21B202-262 Stage1 자연물 시안 기준 재배치

## 작업 내용

- 기존 Terrain 9개와 우주선 파츠·방망이·용접건 등 인공물의 Transform을 유지했다.
- 우주선 파츠 4개를 각각 감싸는 원형 디딤돌 구역 4개를 만들었다.
- 네 구역이 만나는 중앙 연결부와 남쪽 입구까지 이어지는 디딤돌 길을 만들었다.
- 시안의 크기 비율에 맞춰 버섯 나무 4그루를 네 외곽에 배치했다.
- 외곽에는 절벽, 큰 바위와 작은 바위를 겹친 군집, 꽃과 식생을 밀집 배치했다.
- 보라색 꽃 2종과 외계 꽃을 섞은 타원형 꽃밭 7곳을 외곽 빈 바닥에 배치했다.
- Stage1 자연물 중 `planet`, `Meteors`를 제외한 기존·갱신 에셋을 모두 사용했다.
- 아직 프리팹이 없던 갱신 자연물 9종에는 URP 머티리얼과 Stage1 프리팹을 추가했다.

## 산출물 위치

- 씬: `METAGAME/Assets/Scenes/Main.unity`
- 자연물 프리팹: `METAGAME/Assets/_Project/Prefabs/Environment/Stage1/`
- 자연물 머티리얼: `METAGAME/Assets/_Project/Materials/Environment/Stage1/`
- 상단 시점 확인 이미지: `docs/assets/previews/stage1_nature_layout_262.png`

## 자동 검증 결과

| 항목 | 결과 |
| --- | ---: |
| Terrain | 9개, 위치·TerrainData 유지 |
| 원형 구역 | 4개 |
| 디딤돌 | 175개 |
| 버섯 나무 | 4개 |
| 외곽 돌 | 123개 |
| 꽃·식생 | 59개 |
| 꽃밭 꽃 | 104개 |
| 전체 자연물 인스턴스 | 485개 |
| 기존 인공물 Transform | 변경 없음 |

Unity 로그의 `STAGE1_LAYOUT_VALIDATION_OK`와 `STAGE1_LAYOUT_OK`를 확인했다.

## Unity 확인 방법

1. `METAGAME/Assets/Scenes/Main.unity`를 연다.
2. Hierarchy에서 `Stage1/Environment/Nature/NatureLayout_S15P21B202_262`를 확인한다.
3. Scene 뷰를 위에서 내려다보면 원형 돌길 4개, 중앙 연결부와 남쪽 입구가 보인다.
4. Play 후 우주선 파츠와 장비가 기존 위치에 있고 자연물이 지면 위에 놓였는지 확인한다.
