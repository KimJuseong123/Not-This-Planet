# 조립대 첫 파츠 장착과 용접 연결 고정

## 범위

Jira: S15P21B202-500 — [Puzzle] 첫 파츠 조립대 자동 잠금 및 적층 파츠 재부양 방지.
최초 구현은 MR !415로 develop에 머지됐다. 아래 규칙은 사용자 피드백에 따라 수정한 현재 작업 기준이다.
후속 수정은 별도 작업 [S15P21B202-509](S15P21B202-509-simplify-tractor-controls.md)에서 관리한다. 기존 500번 이슈의 완료 상태는 유지한다.
Unity MCP 설치에 따른 Packages 파일 변경은 개인 환경 설정이며 기능 커밋에서 제외한다.

기존 조립대 용접 고정(S15P21B202-447)에 첫 파츠 한정 자동 받침을 더하는 기획 변경이다.
자동 정렬 제거(S15P21B202-172) 원칙은 상부 적층에 유지한다.

## 게임 규칙

- `ShipAssembly.Start`가 `AssemblyDock`을 자동 설치한다. 에디터 메뉴 실행이나 별도 씬 저장 없이 빌드에서도 적용한다.
- `PartSpec.All[0]`인 Part_1만 도킹한다. Stage1_Astra의 견인 활성기는 Part_4에 있으므로 그대로 보존한다.
- 구형 Main처럼 Part_1 자체가 TransportVehicle인 경우에는 도킹하지 않는다. 운반 장치를 영구 고정하는 회귀를 방지한다.
- 모든 고정못이 풀린 첫 파츠가 중앙 0.9m 이내, 받침 높이 오차 0.18m 이내, 기울기 18도 이내, 속도 1.8m/s 이하이면 0.35초간 정렬한다.
- 도킹 후 첫 파츠는 kinematic이다. 용접 질량 갱신과 호스트 권한 복구에서도 고정을 유지한다.
- 상부 파츠에는 도킹 위치/회전 스냅을 적용하지 않는다. 그냥 얹어두면 저중력장으로 다시 들어 올릴 수 있다.
- 고정된 최하부 파츠까지 용접 접합으로 연결된 상부만 광역 저중력장에서 제외한다. 상부 Rigidbody를 강제로 동결하지 않고 기존 용접 조인트로 유지한다.
- 연결을 절단해 고정된 최하부와 떨어지면 다시 저중력장 대상이 된다. 조립대와 연결되지 않은 별도 용접 덩어리를 도킹으로 취급하지 않는다.
- F / 좌우 클릭은 기존 광역장 토글이다. Shift는 가속 이동이며, 함께 눌러도 회수/놓기 모드로 전환하지 않는다. 단일 파츠 견인 API는 기존 내부 용도로만 남긴다.
- 첫 파츠 도킹에는 해제 조작을 추가하지 않았다. 저중력장 네트워크 상태 동기화는 최신 develop 구현을 보존한다.
- 12cm 높이의 네 잠금 장식은 충돌체가 없다. 장착 후 조명이 빨강에서 청록으로 바뀌고 기존 금속 충돌 사운드 슬롯으로 잠금 소리를 재생한다.

## 동기화와 호환성

기존 WorldSync Pose에 DockedToPad 1바이트를 더했다. 계속 전송되는 상태여서 늦게 들어온 참가자도 도킹을 복원한다. 참가자는 판정/스냅을 수행하지 않는다. 양쪽 모두 같은 빌드가 필요하다.
기존 용접 그래프와 엔딩 성공 조건은 변경하지 않는다. 도킹 자체는 용접 비드 연결이나 데모 성공으로 세지 않는다.

## 조절 위치

Play 중 조립 판정 오브젝트의 Assembly Dock 컴포넌트에서 Capture Radius, Height Tolerance, Max Tilt, Max Capture Speed, Snap Duration을 조절할 수 있다.
영구 기본값은 AssemblyDock.cs의 직렬화 필드 초기값이다. Play 중 조절값은 종료하면 초기화된다.

## 규칙 단순화 검증 (2026-09-14)

- develop `7a4f54b`를 받아 수정했다. 기존 개인 MCP 패키지 설정은 보존하고 기능 변경에 포함하지 않았다.
- Unity 에셋 새로고침/컴파일 후 삭제한 지지 판정 API가 실제 로드된 어셈블리에서도 사라진 것을 확인했다.
- 선택 EditMode 테스트 88/88 통과: AssemblyDockTests, TractorBeamTests, AssemblyFloorStateTests, LowGravityFieldTests, TractorBeamControllerTests, PartAssemblyLinkStateTests, WorldSyncReadinessTests, FieldNetworkStateTests.
- 얹기 → 용접 연결 → 절단에 따른 저중력장 대상 변화를 검증했다. 고정된 최하부는 kinematic, 용접된 상부는 기존 조인트로 유지한다.
- Shift 유무 두 경우 모두 F/클릭이 저중력장 토글만 수행하고 단일 회수로 바뀌지 않는 것을 검증했다.
- 새 컴파일 오류는 확인되지 않았다. 기존 파편 프리팹 참조 불일치 등 콘솔 경고는 이 수정 범위에서 해결하지 않았다.
- 이번 수정은 EditMode 검증이며 2인 접속 및 실행 파일 빌드는 수행하지 않았다.

## 최초 구현 검증 기록 (MR !415)

아래는 수정 전 기록이다. 미용접 적층 보호 및 특수 회수 조작 관련 결과는 현재 규칙의 검증으로 사용하지 않는다.

- 첫 검증: AssemblyDockTests, TractorBeamTests, AssemblyFloorStateTests 등 선택 테스트 51/51 통과.
- 최종 선택 검증: AssemblyDockTests, TractorBeamTests, AssemblyFloorStateTests, LowGravityFieldTests, TractorBeamControllerTests, PartAssemblyLinkStateTests, WorldSyncReadinessTests 총 75/75 통과.
- 새 테스트: 첫 파츠 도킹/질량 갱신/권한 복원, 잘못된 파츠·공중·기울기·견인 중 거부, 다층 지지와 공중 화물 구분, 명시적 회수, 광역장 대상 분리.
- 추가 회귀 검사: 실제 콜라이더 높이와 피벗 기준 정렬, 용접 연결/절단에 따른 앵커 해제, 구형 견인 파츠 도킹 방지.
- 승인된 임시 Play 테스트에서 Stage1_Astra 자동 설치와 도킹을 확인했다. 최종 파츠 콜라이더 하단 Y=0.99154, 조립대 Y=0.99. Rigidbody 보간 위치와 Transform 위치를 섞어 높이가 어긋나던 문제도 보정했다.
- 실제 Part_2를 첫 파츠 위에 올린 채 임시 광역장을 켜서, 첫 파츠 Y 고정/상부 지지 유지/별도 화물 상승(Y=1.6→3.9758)을 확인했다. 임시 오브젝트 및 위치 변경은 Play 종료로 폐기했고 씬을 저장하지 않았다.
- 별도 확장 실행에서는 PartTractorActivatorTests.ActivatedPart1_IsRegisteredAsTransportInteractionTarget 1건이 실패했고 단독 재실행에서도 재현됐다. 기대한 FocusedVehicle 대신 null. 이번 변경 전에도 실패했는지는 검증하지 않았으며, 이 실패를 해결했다고 주장하지 않는다.
- Play 콘솔에 largeDebrisPrefab/mediumDebrisPrefab/smallDebrisPrefab의 직렬화 참조 타입 불일치 3건이 관찰됐다. 이번 작업에서 해당 에셋을 수정하지 않았다. 플레이어 빌드에서 null로 취급된다는 로그이므로 배포 전 별도 확인 필요.
- 실제 두 클라이언트 접속 및 호스트 이전 통합 검증은 아직 수행하지 않았다.
- 실행 파일 빌드는 이번 검사에서 만들지 않았다. 자동 적용 경로는 런타임 ShipAssembly.Start이며 Editor 전용 도구에 의존하지 않는다.
