# Stage1 numbered vegetation

## 배치와 사용

- Stage1_Astra.unity에 "Stage1 Vegetation - Numbered Flora" 루트 추가.
- Stage1의 01-ribbon-grass, 02-starflower-moss, 03-curled-rosette, 04-mushroom-cluster, 05-fan-fern, 06-curled-bellflower, 09-lantern-pods FBX와 FBM basecolor를 사용한 식생 721개.
- 낮은 식생 514개는 통과 가능한 Trigger이며 이동속도를 바꾸지 않는다.
- 키 큰 식생 207개는 Trigger와 이동속도 배율 0.45를 사용한다. 겹친 수풀은 가장 강한 감속 하나만 적용한다.
- 기존 MushroomTree 모델을 재사용한 나무 18개는 실제 메시 형태의 정적 MeshCollider로 이동을 막는다.
- 중앙 조립부, 파츠, 시설, 바닥 안내 표시, 스폰 주변의 여유 공간과 기존 충돌체를 피해 고정 시드로 군락 배치.
- 기존 오브젝트는 삭제하거나 이동하지 않는다. 바닥·카메라·조명도 변경하지 않는다.

## Inspector 조정

프리팹 위치: Assets/_Project/Prefabs/Environment/Stage1/Vegetation.
종별 프리팹의 PlantRustle를 수정하면 해당 종의 씬 인스턴스에 적용된다.
개별 인스턴스의 값을 바꾸면 그 인스턴스만 override된다.

| 필드 | 새 식생 초기값 | 의미 |
| --- | --- | --- |
| Movement Speed Multiplier | 낮은 풀 1 / 키 큰 풀 0.45 | 평소 이동속도 대비 배율 |
| Sway Degrees | 25 | 최대 흔들림 각도 |
| Damping | 3.5 | 복원 감쇠. 낮추면 오래 흔들림 |
| Sway Frequency | 2.2 | 왕복 흔들림 주파수 |
| Player Cooldown | 0.22 | 여러 신체 Collider의 연속 접촉을 묶는 시간 |
| Visual | Visual 자식 | 흔들리는 대상. 충돌체는 움직이지 않음 |

새 풀의 Collider는 Prop 레이어다. 기존 Interactable 레이어는 물리 접촉이
꺼져 있어 사용하지 않았다. 프로젝트 충돌 매트릭스 자체는 변경하지 않았다.
기존 PlantRustle의 직렬화 필드 이름은 보존했으며 감속 필드 기본값은 1이다.

이동속도는 PlayerRig의 기존 이동 계산에서 현재 골반 위치를 질의한다.
텔레포트/리스폰/비활성화 후 감속이 남는 Trigger 멤버십 상태를 만들지 않는다.
네트워크 RPC나 새로운 권한 모델을 추가하지 않으며 기존 이동 실행 주체가
같은 정적 씬 충돌체를 조회한다. 다중 접속 환경의 실제 동기화는 별도 검증 대상이다.

## Terrain 후속 작업

현재 지면은 "Basin · sculpted ground" 메시이며 이번에는 Terrain으로 변환하지 않았다.
향후 Terrain/TerrainLayer를 추가한 뒤 메뉴
"Tools > Not This Planet > Vegetation > Resnap vegetation to ground"를 실행하면
새 식생 그룹만 현재 지면 높이에 다시 맞춘다. TerrainCollider와 기존 지면
MeshCollider를 지원하며 Undo 가능하다. 두 지면이 동시에 있으면 더 높은 지면을 선택한다.

## 검증 및 원본 보존

Unity 6000.5.4f1 격리 사본에서 에셋 작성, 컴파일, 물리/시각 테스트,
저장 후 재열기와 오프스크린 렌더를 수행한다. 사용자 Unity 화면은 조작하지 않는다.
artifacts/stage1-vegetation/tests.xml, validation.txt, preservation-audit.json에
최종 결과를 기록한다. overview.png, grove.png는 독립 카메라의 오프스크린 검사 이미지다.
실제 플레이 중 프레임 성능과 다중 클라이언트 검증은 수행하지 않았다.

최종 결과: 관련 테스트 20/20 통과(Play Mode 실제 접촉·흔들림·복원 포함).
배치 721개 지면 밀착, 경로/스폰 검사 지점 34개에서 새 나무 차단 없음,
식생 그룹 missing script 0. 최종 병합 파일도 Unity에서 재열기 검증 통과.
기존 직렬화 블록 4,007개를 그대로 유지했으며 기존 Transform/컴포넌트 변경은 0개다.

씬 반영 도구 tools/unity/publish_vegetation.py는 작업 시작 시의 저장된 씬과
현재 씬이 일치하는지 확인하고, 새 직렬화 블록과 SceneRoots의 새 루트 하나만 추가한다.
기존 Transform/컴포넌트 블록은 원본 그대로 유지하며 사용자 변경도 보존한다.
