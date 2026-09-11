# 멀티플레이 견인장치 탑승 판정과 보행 예측 충돌

## 증상과 원인

- 기준: [1장 상세 기획](../planning/chapter-1-detailed-design.md) §14, §24. 솔로에서는 정상이나 참가자가 견인장치를 조종하면 밖에 있는 것으로 판정되고 흔들린다는 보고.
- `TractorBeamController.SetInput`은 비권위 클라이언트에서 탑승을 실행하지 않는다. 따라서 참가자의 `RidingBody`는 비어 있다.
- 서버의 `NetPlayer.riding`은 복제되고 있었지만 `SetRemoteRiding`은 가시성만 변경했다. `IsRiding`과 `IsMounted`는 거짓이고, 본인용 `RemoteAvatar`는 계속 보행 예측 오차를 적용했다.
- 결과적으로 참가자의 보행·선체 충돌과 서버의 좌석 위치 보정이 충돌하는 경로가 있었다. 실제 두 클라이언트의 화면 재현은 수행하지 않았으며, 해당 경로는 Unity 물리 회귀 테스트로 수정 전후를 비교했다.

## 수정

- 복제된 견인장치 탑승 상태를 보행·부유·상호작용·HUD 판정에 반영한다. 서버는 복제 상태로 원래 탑승 판정을 덮어쓰지 않는다.
- 참가자 본인은 탑승 중 모든 신체 강체를 kinematic 및 충돌 비활성 상태로 전환해 서버 자세를 따른다. 하차 시 원래 강체 설정과 카메라 모드를 복원한다. 서버로 전달하는 조종 입력은 유지한다.
- 탑승 전환마다 보정 이력을 비우고, 탑승 상태의 지연된 위치 응답을 보행 보정에 사용하지 않는다.
- 몸이 늦게 준비되는 경우에도 수신 상태를 적용하며, 연결 종료 시 남아 있는 로컬 리그의 물리를 복원하고 추종 컴포넌트를 제거한다.
- 위치 응답 RPC에 탑승 상태를 추가했으므로 호스트와 참가자는 같은 수정 버전을 사용해야 한다.

## 검증 결과

- Unity 6000.5.4f1 표준 EditMode Test Runner를 `batchmode/nographics`로 실행했다. 화면 직접 조작은 하지 않았다.
- 수정 전 코드를 복원해 새 회귀 테스트 2개가 실패함을 확인했다. 서버 위치 추종 오차는 약 5.11m였고, 하차 뒤 이전 보정이 계속 적용됐다.
- 최종 실행: 관련 테스트 **52개 중 49개 통과, 기존 실패 3개**. 새 테스트 3개(탑승 위치·물리, 반복 하차·보정 제거, 입력 유지·카메라 및 물리 복원)는 모두 통과했다.
- 아래 3개는 수정 전에도 동일한 단언으로 실패했다. 이번 변경으로 새로 발생한 실패는 없다.
  - `TractorBeamControllerTests.CenterGrabbable_BlocksVehicleEInputWhenVehicleIsAlsoVisible`
  - `TractorBeamControllerTests.InteractionFocus_SelectsOnlyTheNearestCameraCenterCandidate`
  - `TractorBeamControllerTests.VehicleDrive_UsesCameraFacingSeparateFromSidewaysMove`
- Unity 컴파일 오류 및 Mirror Weaver 오류 없음. 기존 경고는 유지된다.
- 실행 결과: `METAGAME/Logs/transport-before-fix.xml`, `METAGAME/Logs/transport-final.xml` 및 동명 로그. 생성 로그는 버전 관리하지 않는다.

## 남은 실제 멀티플레이 확인

1. 같은 수정 버전의 호스트와 참가자로 접속한다. 참가자가 견인장치에 탑승해 정지·전후좌우 이동·상승·하강을 수행한다.
2. 참가자의 탑승 표시와 조종석 시점이 유지되고, 걷기/부유 판정이나 반복적인 위치 되돌림이 발생하지 않는지 확인한다.
3. 양쪽 화면에서 탑승자 표시를 확인하고, 호버 중 하차·재탑승·조종자 교대를 반복한다. 하차 후 걷기·점프·충돌이 복원되어야 한다.
4. 호스트 직접 조종 및 솔로를 비교하고, 참가자의 탑승 중 연결 종료·재접속과 지연 환경도 확인한다.

실제 네트워크 지연 및 두 화면의 시각적 일치, 조종석 앵커와 기체의 상대적인 보간 차이는 이번 EditMode 검증의 범위 밖이다.
