# Stage1_Astra 메인 발사대 통합

검증일: 2026-09-17, Unity 6000.5.4f1 / URP 17.5.

## 배치 및 기능

- `Assets/Scenes/Stage1_Astra.unity`의 기존 AssemblyPoint를 유지하고 `PF_Stage1_MainAssemblyLaunchpad`를 자식으로 배치했다. Edit Mode에서도 모델이 보인다.
- 기존 CentralAssemblyDock 위 중앙 (0, 1.44, 0)에 조립면을 맞췄다. 모델 Y 회전 45°로 경사로가 남서쪽 엔진 파츠 운반 방향을 향한다. 하부 받침 약 8cm는 기존 도크에 묻힌다.
- 기존 ShipAssembly, LaunchTest, ShipHatch 및 발사 기준 Transform 참조를 유지했다. 캡처 반경, 허용 높이/기울기/속도, 네트워크 권한과 스냅 판정은 변경하지 않았다.
- AssemblyDock는 새 AssemblyDockVisual에 도킹 상태를 전달한다. 네 고정턱이 25cm 안쪽으로 움직이고 대기 적색 등이 고정 청록 등으로 바뀐다. 원본 FBX/FBM은 변경하지 않고 고정턱의 상태등 면만 파생 메시의 두 번째 재질 슬롯으로 분리했다.
- 모델 참조가 없는 다른 씬에는 기존 런타임 생성 장식을 유지한다.
- World 레이어 고정 충돌체 8개: 중앙 저밀도 받침, 실제 경사로 메시, 기둥과 기둥 받침 4개, 후방 정비 장치, 조작판. 고정턱은 시각 장식이며 로켓 중앙 적층 공간에는 충돌체가 없다.

## 검증 결과

- Unity 스크립트 컴파일 오류 없음.
- 중앙 엔진 받침면 9개 지점 레이 검사 통과. 조립 기준 높이 오차 5mm 이내, 로켓 적층 공간 비어 있음. 기존 발사/해치 컴포넌트와 새 시각 컴포넌트 연결 확인.
- Edit Mode: AssemblyDockTests 8/8, AssemblyDockVisualTests 2/2 통과.
- LaunchTestTests: 8/9 통과. `FailedLaunch_LeavesSomeJointsIntact`는 미용접 발사 실패 후 접합부가 모두 끊어져 실패했다. 변경 전 HEAD의 AssemblyDock 코드로 되돌려 동일 테스트군을 재실행해 같은 8/9 및 같은 실패를 재현했다. 이 기존 발사 물리 문제는 이번 모델 교체 범위에서 변경하지 않았다.
- Play Mode: 실제 Stage1_Astra의 Part_1 고정핀 해제 후 조립면에 놓아 실제 FixedUpdate 도킹 경로를 검증했다. 중앙 정렬, 조립면 높이, kinematic 고정, 고정턱 4개 이동, 청록 상태등 4개 및 기존 절차 생성 장식 미생성을 확인했다. 검증 후 Play Mode를 종료했으며 런타임 파츠 이동은 씬에 저장하지 않았다.
- 전체 플레이어 빌드, 멀티플레이 세션, 플레이어의 전체 운반/용접/탈출 플레이는 수행하지 않았다.

검증 XML, 텍스트 결과와 씬 이미지는 `artifacts/stage1-main-assembly-launchpad/scene-integration/`에 보관한다. 임시 Editor 실행/Play 검증 도구는 제거했다. 재생성 및 정적 검증 메뉴는 `Metagame > Stage1`에 있다.
