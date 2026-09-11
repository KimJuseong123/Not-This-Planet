# [Character] 총·방망이 휴대 위치 및 1인칭 표시 분리

- 담당: 송승우
- 날짜: 2026-09-11
- Jira: https://ssafy.atlassian.net/browse/S15P21B202-444
- Epic: [Character] 캐릭터 조작

## 목적과 작업 내용

월드 도구를 카메라로 옮기는 대신 메시·머티리얼만 복사한 1인칭 표시 모델을 사용한다. 복사본에는 도구 스크립트·콜라이더·Rigidbody를 붙이지 않는다. 실제 에셋의 손잡이 지점을 기준으로 오른손에 정렬하며, 방망이의 대각선 모델 축과 1인칭 크기·각도를 보정한다. 1인칭 손과 방망이는 동일한 스윙 회전을 사용한다.

원격 표시를 위해 도구 소유자·동작·진행도·방향을 기존 WorldSync 전송 주기로 전달한다. 게임플레이 소유권은 표시 상태에서 부여하지 않는다. 네트워크 메시지 형식이 바뀌므로 접속자 모두 동일 버전으로 재빌드해야 한다.

## 변경 범위

- FirstPersonToolVisual, ToolBase, PlasmaWeldingTool, MeteorBat
- FirstPersonHandView, PlayerRig의 표시 도구 참조
- NetPlayer, RemoteAvatar, WorldSync의 원격 표시 상태
- PF_Stage1_MeteorBat.prefab의 1인칭 회전·크기
- 관련 EditMode 회귀 테스트

씬, 렌더링·포그 설정, 프로젝트 빌드 설정은 제외했다. 용접의 기존 0.8m 근접 판정은 유지한다. 실제 총구는 월드 도구를 사용하고 표시 복사본의 위치에는 종속되지 않는다.

## 검증 결과

수정 전 작업 기반에서 Unity 6000.5.4f1 별도 QA 프로젝트로 관련 EditMode 95개를 실행해 93개 통과, 2개 실패했다. 실패는 ToolCarryCollisionTests.AimedEquipmentAtPickupRange_EquipsAndChargesUntilQIsReleased의 총·방망이 케이스이며, 최초 대상 인식에서 Grabbable 대신 None이다. 기존 문제인지 이번 변경의 회귀인지 아직 분류하지 못했다.

제어된 카메라 렌더로 손잡이 접점과 화면 구도를 확인했다. 사용자는 Development Build 두 창에서 휴대 상태를 확인하고 스크린샷을 제공했다. 원격 방망이 스윙이 충분히 전달되지 않고 빈 왼팔까지 올라가는 문제는 여전히 관찰된다.

제출 브랜치는 최신 develop 04a740e에서 생성하고 작업 diff를 3-way 적용했다. 충돌 없이 적용됐지만 이 최신 통합 결과의 Unity 컴파일·전체 테스트·Windows 재빌드는 아직 실행하지 않았다. 앞선 93/95 결과를 최신 통합 결과의 통과 근거로 간주하지 않는다.

## 확인 방법과 남은 것

동일 빌드를 두 창에서 실행할 때 -metagame-device-id=local-test-a / -metagame-device-id=local-test-b로 계정을 구분한다. 로비 방 코드로 접속해 양쪽에서 총·방망이 장착, 해제, 재장착, 스윙, 시점 전환을 관찰하고 역할을 바꿔 반복한다.

원격 방망이 대기·스윙 자세 개선, 도구 줍기 테스트 실패 분류, 최신 develop 통합 검증은 남아 있다. 현재 위치 수정본의 리뷰용 제출이며 전체 멀티플레이 모션 완료를 의미하지 않는다. CI 통과와 팀장 검토 없이 머지하지 않는다.
