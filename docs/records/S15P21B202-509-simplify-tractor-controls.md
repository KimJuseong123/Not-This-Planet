# [Puzzle] 견인 조작 단순화 및 조립 파츠 고정 조건 정리 (S15P21B202-509)

- 담당: 송승우
- 날짜: 2026-09-14
- Jira: https://ssafy.atlassian.net/browse/S15P21B202-509
- Epic: [Puzzle] 퍼즐·상호작용 시스템
- 관련 작업: S15P21B202-500 (MR !415로 머지 완료)
- 브랜치: S15P21B202-509-simplify-tractor-controls
- 분기 기준: develop e59cab1

## 목적

첫 파츠 도킹에 함께 추가했던 특수 회수 조작을 제거하고, 장착과 용접이라는 기존 게임 규칙으로 고정 조건을 설명할 수 있도록 단순화한다.

## 작업 내용

- 최하부 Part_1의 조립대 장착·자동 정렬·고정 및 빌드 런타임 적용을 유지한다.
- 상부 파츠는 얹기만 하면 저중력장 대상이다. 받침 접촉 검사만으로 보호하던 코드를 제거한다.
- 고정된 하부까지 용접 접합으로 연결된 상부는 저중력장 대상에서 제외한다. 상부를 kinematic으로 바꾸지 않고 기존 용접 조인트가 잡아준다.
- 용접 연결을 절단하면 다시 저중력장 대상이 된다.
- Shift+F/Shift+클릭 회수·놓기 분기와 안내 문구를 제거한다. F/좌우 클릭은 저중력장 토글, Shift는 가속 이동을 유지한다.
- 최신 develop의 저중력장/도킹 네트워크 동기화는 변경하지 않는다.
- 씬·프리팹·개인 MCP 패키지 설정은 커밋에 포함하지 않는다.

## 결과물

- AssemblyDock.cs: 불필요한 접촉 기반 적층 보호 제거
- LowGravityField.cs: 조립대와 접합으로 연결된 경우만 보호
- TractorBeamController.cs: 기본 조작으로 복원
- AssemblyDockTests.cs / TractorBeamControllerTests.cs: 용접 전후·절단 및 Shift 동시 입력 회귀 테스트

## 검증 결과

- develop 7a4f54b 기반으로 Unity 컴파일 후 관련 EditMode 테스트 88/88 통과했다.
- 검증 그룹: AssemblyDockTests, TractorBeamTests, AssemblyFloorStateTests, LowGravityFieldTests, TractorBeamControllerTests, PartAssemblyLinkStateTests, WorldSyncReadinessTests, FieldNetworkStateTests.
- 단순 적층 → 용접 → 절단에 따른 저중력장 대상 변화와 Shift 유무 두 경우의 정상 토글을 확인했다.
- 이후 최신 develop e59cab1에서 분기했다. 추가된 develop 변경은 이번 수정 파일과 겹치지 않아 로컬 수정이 충돌 없이 이관됐다. 이 최신 기준에서 Unity 테스트를 재실행한 결과는 아니다.

## 결정과 근거

최하부만 조립대에 직접 장착하고, 상부는 용접으로 연결한다. 단순 적층 보호를 위해 별도 회수 키를 배우게 하지 않는다.

## 한계와 후속 작업

- 2인 접속·실행 파일 빌드는 이번 수정에서 검증하지 않았다.
- 기존 파편 프리팹 참조 불일치 등 콘솔 경고는 수정 범위가 아니다.
- MR 생성 및 develop 머지는 별도 절차로 진행한다.
