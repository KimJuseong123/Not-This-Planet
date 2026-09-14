> 이 문서는 MR !415 당시의 이력이다. 상부 보호·특수 회수 규칙은 폐기했고, 현재는 최하부 장착만 유지한다. 최종 범위는 [509번 작업 기록](S15P21B202-509-simplify-tractor-controls.md)을 따른다.

# 조립대 첫 파츠 도킹과 적층 보호

## 범위

Jira: S15P21B202-500 — [Puzzle] 첫 파츠 조립대 자동 잠금 및 적층 파츠 재부양 방지.
브랜치: S15P21B202-500-assembly-pad-docking. develop 기반이며 MR은 별도 요청 전까지 만들지 않는다.
Unity MCP 설치에 따른 Packages 파일 변경은 개인 환경 설정이며 기능 커밋에서 제외한다.

기존 조립대 용접 고정(S15P21B202-447)에 첫 파츠 한정 자동 받침을 더하는 기획 변경이다.
자동 정렬 제거(S15P21B202-172) 원칙은 상부 적층에 유지한다.

## 게임 규칙

- `ShipAssembly.Start`가 `AssemblyDock`을 자동 설치한다. 에디터 메뉴 실행이나 별도 씬 저장 없이 빌드에서도 적용한다.
- `PartSpec.All[0]`인 Part_1만 도킹한다. Stage1_Astra의 견인 활성기는 Part_4에 있으므로 그대로 보존한다.
- 구형 Main처럼 Part_1 자체가 TransportVehicle인 경우에는 도킹하지 않는다. 운반 장치를 영구 고정하는 회귀를 방지한다.
- 모든 고정못이 풀린 첫 파츠가 중앙 0.9m 이내, 받침 높이 오차 0.18m 이내, 기울기 18도 이내, 속도 1.8m/s 이하이면 0.35초간 정렬한다.
- 도킹 후 첫 파츠는 kinematic이다. 용접 질량 갱신과 호스트 권한 복구에서도 고정을 유지한다.
- 상부 파츠에는 도킹 위치/회전 스냅을 적용하지 않는다. 도킹 바닥과 접합된 파츠, 또는 그 위에 실제로 받쳐진 파츠는 광역 저중력장에서 제외한다.
- 지지 판정은 파츠 하단의 다섯 점에서 짧게 탐사한다. 조립대 위 공중의 화물은 제외하지 않는다. 지지를 잃은 미용접 파츠는 물리적으로 떨어질 수 있다.
- F / 좌우 클릭은 기존 광역장 토글이다. 탑승 중 Shift+F(또는 Shift+클릭)는 바로 아래 미용접 파츠의 명시적 견인/놓기다. 명시적 견인 중에는 광역장이 같은 몸체에 힘을 중복 적용하지 않는다.
- 명시적으로 놓을 때 광역장도 끈다. 다시 켜기 전에 파츠가 아래에 착지할 시간을 주면 재부양되지 않는다.
- 도킹 바닥과 접합된 덩어리는 명시적으로도 들어 올릴 수 없다. 상부를 재배치하려면 연결 비드를 먼저 절단한다. 첫 파츠 도킹에는 해제 조작을 추가하지 않았다.
- 12cm 높이의 네 잠금 장식은 충돌체가 없다. 장착 후 조명이 빨강에서 청록으로 바뀌고 기존 금속 충돌 사운드 슬롯으로 잠금 소리를 재생한다.

## 동기화와 호환성

기존 WorldSync Pose에 DockedToPad 1바이트를 더했다. 계속 전송되는 상태여서 늦게 들어온 참가자도 도킹을 복원한다. 참가자는 판정/스냅을 수행하지 않는다. 양쪽 모두 같은 빌드가 필요하다.
기존 용접 그래프와 엔딩 성공 조건은 변경하지 않는다. 도킹 자체는 용접 비드 연결이나 데모 성공으로 세지 않는다.

## 조절 위치

Play 중 조립 판정 오브젝트의 Assembly Dock 컴포넌트에서 Capture Radius, Height Tolerance, Max Tilt, Max Capture Speed, Snap Duration을 조절할 수 있다.
영구 기본값은 AssemblyDock.cs의 직렬화 필드 초기값이다. Play 중 조절값은 종료하면 초기화된다.

## 검증

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
