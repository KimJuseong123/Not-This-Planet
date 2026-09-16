# Stage1_Astra Part2 경사로 교체

## 현재 최종 상태: 나선형 동선으로 대체

사용자 요청에 따라 아래의 분절 경사로 적용은 나선형 동선으로 교체되었다. 현재 씬에는 기존 Trail0~4, 기존 언덕 연결부, 이전 접합판 4개 및 이전 직선 난간 44개가 남아 있지 않다.

- **11 SpiralHillAscent 1개:** 전망대 중심 `(28, 19)` 주위를 270도 돌아 올라간다. 중심선 반지름 14m, 폭 4.4m. 루트 위치 `(28, -0.2, 19)`, 회전 0, 배율 `(1, 1.071897, 1)`. 지지대 하단을 땅에 묻고 상단을 전망대 높이에 맞추도록 세로 배율만 조정했다. 바닥 높이는 시작 약 0.467m, 정상 7.97m이며 중심선 경사는 약 6.5도다.
- **06 연결로 2개:** 지상 `(17, 0.05, 5)`에서 나선 시작 `(28.25, 0.467, 5)`까지 진입로. 정상 `(14, 7.97, 19.15)`에서 전망대 서쪽 `(23.7, 8.03, 16.4)`까지 연결교. 기존 관측 장비를 피하는 방향이다.
- **09 연결판 2개:** 나선 시작·정상에 배치.
- **10 난간 92개:** 곡선 78개와 연결로 14개. 입구와 정상 회전부는 통행을 막지 않도록 개구부를 확보했다.
- 기존 안내등 **18개 그룹**을 나선 바깥 가장자리로 재배치했다. 가져온 FBX 외관과 숨겨진 원본 충돌 조각을 함께 이동했으며 기존 참조는 보존했다.
- 동선을 가리는 절벽 장식 4개와 주변 바위 장식 2개를 제거했다. 지형 메시, Part2, 전망대, **07 입구 계단 1개와 08 관측 장비 3개는 보존**했다.

루트 이름은 `Astra Part2 Spiral - ...`. 새 길은 모델별 단순 충돌 FBX를 사용하는 정적 Non-convex MeshCollider이며 Ground 레이어, Trigger 꺼짐이다. 나선 외관의 볼트·홈은 보행 충돌에 포함하지 않는다.

검증: 나선 429지점과 연결로 246지점, 총 **675지점 바닥 검사 및 675지점 몸통 캡슐 여유 공간 검사**. 누락 스크립트 0개. 새 모델의 메시·재질·Collider 참조 검사. 기존 씬의 관련 없는 4,162개 블록을 보존했고 이동한 안내등의 모든 직렬화 위치가 반영되는지도 확인했다. 실제 캐릭터 조작 및 화면 렌더 검증은 수행하지 않았다.

최종 기록은 `spiral-validation.json`, `spiral-reload-validation.json`, `spiral-serialization-audit.json`. 이전 동선 백업은 `tmp/part2-trail-integration/Stage1_Astra.before-spiral.unity`. 도구는 `tools/unity/Stage1Spiral.cs`, `publish_stage1_spiral.py`이다. 아래 내용은 이전 적용 이력이다.

## 추가 적용: 07~10 주변 모델

앞선 경사로 6개 교체에 이어, 누락된 주변 모델 4종을 모두 씬에 배치했다.

| 모델 | 수량 | 배치 및 교체 |
| --- | --- | --- |
| 07 ObservatoryEntryStairs | 1 | 전망대 남쪽 입구 (28, 7.2, 12.22), +Z 방향으로 상승. 기존 계단 4개 제거 |
| 08 ObservatoryInstrument | 3 | 기존 관측 장비 자리. 실제 바닥에 접지하고 점검면을 전망대 중앙으로 회전. 구형 장비 3개·표시등 3개 제거 |
| 09 TrailJunction | 4 | Trail0↔1, 1↔2, 2↔3, 3↔4 코너. 4.4×2.4m 원래 크기로 회전·높이 조정 |
| 10 CliffEdgeGuardrail | 44 | Trail1~4 양옆 및 언덕 연결부 양옆. 구간 길이에 맞춰 반복하고 코너·출입부 여유 확보 |

추가된 52개 루트 이름은 `Astra Part2 Detail - ...`이다. 각 모델의 별도 `UCX_SM_Astra_*.fbx` 메시를 이용해 정적 MeshCollider를 구성했다. Ground 레이어, Non-convex, Trigger 꺼짐, Rigidbody 없음. 계단은 4단을 따라가며, 관측 장비는 단순 원통, 연결판은 실제 외곽, 난간은 빈 중간 부분도 통과하지 않는 단순 충돌 형태다. 충돌용 메시에는 렌더러가 없다.

검증: 추가 모델의 바닥 48지점, 난간 44개 충돌, 연결부 60지점, 기존 길 중앙 186지점의 난간 간섭 검사를 수행한다. 연결판 추가 단차 최대 0.1453m로 기존 입구 계단의 0.19m 단차보다 작다. 누락 스크립트 0개. Part2와 기존 경사로 6개 등 관련 없는 데이터 4,285개 블록을 보존했다. 실제 캐릭터 이동·렌더 화면 검증은 하지 않았다.

기록: `details-validation.json`, `details-reload-validation.json`, `details-serialization-audit.json`. 추가 적용 직전 백업은 `tmp/part2-trail-integration/Stage1_Astra.before-details.unity`. 도구는 `tools/unity/Stage1Part2Details.cs`, `publish_part2_details.py`이다.

화면을 조작하지 않고 Unity 6000.5.4f1의 별도 프로젝트 사본에서 FBX를 가져와 씬을 작성했다. 최종 씬에는 교체 대상과 부모의 자식 목록만 반영하며, 나머지 직렬화 데이터는 원본 그대로 유지한다.

## 적용 범위

| 기존 오브젝트 | 새 FBX | 위치 (m) |
| --- | --- | --- |
| Long walking trail 0 | SM_Astra_Trail0 | (34, 0.55, -4.5) |
| Long walking trail 1 | SM_Astra_Trail1 | (42.5, 2.3, 4) |
| Long walking trail 2 | SM_Astra_Trail2 | (44, 4.15, 17.5) |
| Long walking trail 3 | SM_Astra_Trail3 | (38, 5.95, 26.5) |
| Long walking trail 4 | SM_Astra_Trail4 | (30.5, 6.8, 25) |
| Wayfinding hill deck transition | SM_Astra_HillDeckTransition | (30.2, 7.42, 24.9) |

기존 6개 GameObject와 그 렌더러·메시·충돌체를 제거한다. 새 오브젝트 이름은 `Astra Part2 - SM_Astra_*`이다. 원래 부모·위치·회전·Static 설정을 이어받고, 이미 실제 치수로 제작된 FBX이므로 새 루트 배율은 1이다. URP Lit 재질과 텍스처를 명시적으로 연결한다.

Part_2, 전망대, 안내등 및 다른 동선은 유지한다. L11 전체 나선형 모델은 기존 5구간 동선을 통째로 재설계하는 대안이므로 이번 개별 경사로 교체에서는 중복 배치하지 않는다.

## 충돌체

각 길에 정적 BoxCollider 1개, Ground 레이어(8), Is Trigger 꺼짐. 기존 충돌체의 실제 크기·중심·회전·물리 재질을 보존한다. 경사로는 평평한 단일 판이므로 고밀도 외관 MeshCollider 대신 박스 충돌체가 정확하고 가볍다. 작은 볼트와 패널 틈은 충돌체에 포함하지 않아 보행 중 걸리지 않는다. 추가 Rigidbody는 없다. 원래 Default(0)였던 언덕 연결부도 나머지 길과 동일한 Ground(8)로 맞췄다.

## 검증 기록

- `validation.json`: FBX 축·실측 크기·피벗·재질 확인, 길 6개 바닥의 738개 지점 raycast, Part2 위치 보존, 삭제 대상 잔존 여부와 중복 충돌체 검사.
- `serialization-audit.json`: 원본 변경 여부를 해시로 확인하고, 관련 없는 씬 블록 보존 및 로컬 참조 무결성 검사.
- `reload-validation.txt`: 최종 저장 씬을 별도 Unity에서 다시 열어 메시·재질·충돌체와 바닥 높이 검사.

검증은 Unity 배치 모드의 씬 로드 및 물리 쿼리다. 플레이어 조작, 멀티플레이 완주, 화면 렌더 검증은 수행하지 않았다. 원본 백업은 `tmp/part2-trail-integration/Stage1_Astra.before.unity`에 있다. 이미 열려 있던 Editor 화면 상태는 조작하지 않았으므로 결과 확인 시 저장된 Stage1_Astra 씬을 다시 불러와야 한다.

원본 씬을 처음 로드할 때부터 운석의 `largeDebrisPrefab`, `mediumDebrisPrefab`, `smallDebrisPrefab`에 기존 참조 타입 불일치 경고가 있었다. 이번 교체와 관계없는 해당 직렬화 블록은 그대로 보존했다. 기존 코드의 obsolete API 경고도 관찰되었으며, 새 도구의 컴파일 오류는 없다. 교체 대상의 메시·재질 참조 검사와 누락 스크립트 0개 확인은 별도로 수행했다.

도구는 `tools/unity/Stage1Part2TrailIntegration.cs`와 `publish_part2_trails.py`에 보관한다. 프로젝트의 자동 실행 콜백이나 런타임 코드는 추가하지 않는다.
