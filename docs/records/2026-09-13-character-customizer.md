# 출발 전 캐릭터 커스터마이징

기준: [1장 상세 기획](../planning/chapter-1-detailed-design.md) §3.1(캐릭터별 능력 차이 없음), §22(엔딩과 로비 복귀).

## 플레이 흐름

- 혼자 하기: `Lobby → Customizer → Stage1_Astra → Stage1_Ending → Lobby`.
- 함께 하기: `Lobby → Customizer(외형 선택 → 기존 방 생성/참가·준비 화면) → Stage1_Astra → Stage1_Ending → Lobby`.
- 함께 하기는 외형 선택을 완료해야 방 화면에 진입한다. 방장이 시작한 후 각자 외형을 고르게 하면 참가자가 방장을 기다리는 동안 기존 10초 접속 제한에 걸릴 수 있으므로, 방 준비 전에 완료한다. 기존 Spring 방 프로토콜과 Mirror 씬 전환을 재사용한다.
- 취소하면 저장하지 않고 로비로 돌아간다. 저장하고 출발/방 참가를 누르면 로컬 `PlayerPrefs`의 `appearance.v1`에 저장한다. 다음 실행과 다음 판에도 유지된다.
- `Assets/Scenes/Customizer.unity`를 Build Settings와 `SpaceBuild.RequiredScenes`에 추가했다. 별도 Inspector 연결은 필요 없다. 씬의 `CustomizerBootstrap`이 미리보기 카메라·조명·UI를 생성한다.

## 가능한 커스터마이징과 적용 범위

현재 `SF_Rabbit_Base`는 스킨된 단일 메시이며 Resources 프리팹에는 재질 슬롯이 하나다. 이번 구현은 원본 FBX·메시·텍스처·공유 재질을 수정하지 않는다.

| 항목 | 선택 |
| --- | --- |
| 전체 색조 | 기본, 민트, 라벤더, 살구, 하늘 |
| 헬멧 장식 | 없음, 탐사 안테나, 통신 헤드셋 |
| 가슴 장식 | 없음, 탐사 배지, 쌍별 배지 |

전체 색조는 기존 텍스처 위에 곱하는 색이다. 얼굴·귀·복장 중 특정 영역만 칠하는 마스크는 아직 없다. 헬멧과 배지는 기존 머리/가슴 강체를 따라가는 시각 장식으로, 추가 콜라이더나 질량이 없다. 첫 번째 장식 에셋은 기본 도형으로 구성했다.

1인칭 손에도 선택한 색조를 적용한다. 토끼 몸과 손은 서로 다른 텍스처이므로 완전히 같은 색감까지 보장하지 않는다. 기존 팀 슬롯·이름표 색상과 능력치, 래그돌 크기·본 길이는 바꾸지 않는다.

향후에는 UV 영역 마스크를 제작해 귀 안쪽/헬멧/우주복/장갑/부츠 색상을 독립적으로 바꾸거나, 장식 프리팹을 추가할 수 있다. 몸통·머리·귀 자체 교체와 체형 조절은 메시 분리, 웨이트 또는 블렌드셰이프 제작이 먼저 필요하다.

## 상태와 동기화

- `CustomizationData`: 유효 범위, 색조 팔레트, 정수 인코딩, 로컬 저장.
- `PlayerAppearance`: 인스턴스별 MaterialPropertyBlock과 장식 생성. 원본 재질은 보존한다. 1인칭 그림자/탑승 숨김 상태에서도 새 장식이 나타나지 않도록 기존 가시성을 재적용한다.
- `SpaceBootstrap`과 `FirstPersonHandView`: 솔로와 로컬 몸/손에 저장값 적용.
- `NetPlayer`: 소유자의 Command를 서버에서 정규화하고 SyncVar로 전달한다. 서버 몸, 로컬 몸 인계, 원격 몸 생성 때 다시 적용하여 초기 훅 순서와 늦은 참가를 처리한다. 매 프레임 외형 데이터를 보내지 않는다.

## 검증

원본 화면은 조작하지 않는다. `outputs/tractor-tests`의 별도 복사 프로젝트에서 Unity 6000.5.4f1 배치 모드로 검증한다.

- Unity 컴파일 및 `CustomizationTests` 7개 통과: 45개 조합 인코딩, 잘못된 네트워크 값, 물리 불변성, 숨김·장식 갱신, 공유 재질 보호, 씬/빌드 참조.
- 결과: `.local-validation/customizer.xml`, `.local-validation/customizer-tests.log`.
- 캐릭터/엔딩 관련 40개 테스트 통과(`CustomizationTests`, `CustomizerRuntimeTests`, `RemoteAvatarTests`, `RabbitLocomotionTests`, `EndingSceneTests`). 런타임 검증은 헤드리스 Play Mode에서 미리보기 생성, 비물리 상태, 취소 후 로비 복귀와 미리보기 정리를 확인했다. 결과: `.local-validation/customizer-regression.xml`.
- 최종 UI 조정 후 컴파일 및 25개 테스트 통과(`CustomizationTests`, `CustomizerRuntimeTests`, `DollVisibilityTests`, `FirstPersonVisibilityTests`, `RemoteRidingVisibilityTests`). 결과: `.local-validation/customizer-final.xml`, `.local-validation/customizer-final.log`. 앞선 40개와 중복되는 테스트가 포함되어 있다.
- 실제 화면에서의 장식 위치·색감·해상도별 UI와 서로 다른 두 클라이언트 간 외형 표시는 별도 시각/멀티플레이 확인이 필요하다.
