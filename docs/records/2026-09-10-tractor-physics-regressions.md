# 견인 관련 실패 7건 수정과 물리 검증

## 수정

| 보고된 테스트 | 확인 내용과 변경 |
| --- | --- |
| TouchingSurface_TrueOnStructureTop_FalseWhileHovering | 전체 상자 탐사로 바꾼 뒤 플랫폼-표면 간 0.11m 여유가 0.10m 탐사 범위를 벗어났다. 물리 접촉 오프셋을 탐사 거리에 반영했다. |
| TransportAction_BoardsAndStartsDismountWithoutClampState | 하차 즉시 플레이어를 놓는 현재 동작과 테스트의 탑승 유지 기대가 달랐다. 컨트롤러의 탑승 참조도 즉시 해제하고 기대값을 수정했다. |
| Player_FallsAfterFieldTurnsOff | 자동 부양 중에 장을 끄므로 위쪽 관성이 남았다. 부양이 안정된 후 낙하를 검증하고, 상승 도중 해제 시 중력 복구와 감속을 별도 테스트로 추가했다. 상승 관성을 억지로 삭제하지 않는다. |
| ConversionAndDismount_PreserveCompoundHullAndEmptySpace | EditMode에서 OnEnable이 호출되지 않아 전환 이벤트 구독이 누락됐다. 멱등 Initialize를 제공하고 테스트 준비 단계에서 호출한다. |
| TheTumbleEndsOnTheStepItLands | 회전된 충돌체가 원점보다 먼저 바닥에 도달할 수 있는데 테스트는 원점 높이만 검사했다. 실제 충돌체의 아래 경계와 기존 원점 착지 대역을 함께 측정한다. |
| ImpactPushesPilotedCraft_WithoutPositionSnapping | 무인 호버 테스트를 유인 비행으로 바꾼 뒤에도 0.5m 최소 이동값이 남아 있었다. 강한 유인 제동을 고려해 실제 변위 발생과 정지 후 원위치 복귀가 없음을 검사한다. |
| BoardingAfterLanding_StartsANewFlight | 상승 탐사가 바닥에 접촉한 몸체 전체에서 시작해 겹침을 거리 0 장애물로 검출했다. 이동 방향의 윗면만 탐사해 바닥 접촉을 천장으로 오인하지 않도록 수정했다. |

## 검증 방법

- 사용자 화면 조작과 기존 Editor의 플레이 모드 전환 없음.
- 별도 `METAGAME/Temp/CraftPhysicsProject`에서 Unity 6000.5.4f1을 batchmode/nographics로 실행.
- 게임의 Core/Gameplay 참조와 물리·레이어 설정을 사용하고, 대상 테스트 클래스의
  SetUp → 테스트 메서드 → TearDown을 직접 호출했다. 실제 Unity Physics.Simulate,
  NUnit 단언 및 Unity LogScope 오류 검사를 사용했다. 표준 Test Runner의 전체 프로젝트 실행은 아니다.
- 최초 실행은 로컬 라이선스 IPC 제한으로 실패했다. 제한 밖에서 검증 프로세스를 실행하여 해결했다.
- 셰이더 외관·그래픽 렌더링 검증은 이 실행에 포함하지 않는다.

## 결과

- 보고된 **7개 전부 통과**.
- 대상 6개 클래스의 인자 없는 Test 메서드: **70개 중 66개 통과, 4개 실패**.
- 동일 환경에서 수정 전 HEAD 소스를 별도로 컴파일해 비교: **69개 중 57개 통과, 12개 실패**.
  기존 실패 8개를 해소했고 새 중력 복구 테스트 1개가 통과했다. 새로 발생한 실패는 없다.
- Gameplay 및 전체 Editor 테스트 어셈블리 Roslyn 컴파일 오류 0.

다음 4개는 수정 전후 동일하게 실패하며 이번 변경에서 수정하지 않았다.

- ActivatedPart1_IsRegisteredAsTransportInteractionTarget
- InteractionFocus_SelectsOnlyTheNearestCameraCenterCandidate
- CenterGrabbable_BlocksVehicleEInputWhenVehicleIsAlsoVisible
- VehicleDrive_UsesCameraFacingSeparateFromSidewaysMove

원본 비교 결과와 실행 로그는 `METAGAME/Temp/CraftValidation/`의
`baseline-69-results.txt`, `current-70-results.txt`, `physics-unity.log`,
`baseline-physics-unity.log`에 있다. 이 경로는 버전 관리에서 제외된다.
