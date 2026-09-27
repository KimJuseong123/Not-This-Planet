# 688 운석 타격 시선 방향

## 변경

- 기준: `origin/S15P21B202-688-puzzle`, `4489bb25`. 작업 위치는 `.local-worktrees/688-puzzle`.
- 기존에는 `MeteorBat.LaunchDirection`이 시선의 수직 성분을 버리고 고정 상향 각도를 적용했다. 성공한 운석 타격만 실제 시선의 상하·좌우 방향을 사용한다.
- 장착된 방망이는 `Owner.AimForward`를 사용한다. 로컬은 카메라 각도, 서버는 전달받은 입력의 yaw/pitch를 읽는다. 팔 들어 올리기 자세가 시선에 섞이지 않는다.
- 독립 호출은 `Swing`의 facing을 정규화한다. 시선이 없는 경우 기존 방향 보정을 사용한다.
- 기존 판정별 초기 속도를 유지한다: Good 14m/s, Great 18.9m/s, Perfect 25.2m/s. 실제 거리와 체공 시간은 중력·시선 각도·장애물에 따라 달라진다. 아래를 보면 지면 쪽으로 발사된다.

## 기존 기능 영향 검토

- 운석 선택 우선순위, 착탄 시간과 거리 판정, Miss 및 쿨다운은 변경하지 않는다.
- 동료의 넘어짐, 소품의 상향 충격량, 스윙 애니메이션은 기존 계산을 유지한다.
- `Meteor.Deflect`의 피해 없는 소품 전환, 등급별 효과 및 소멸 시간은 유지한다.
- `WorldSync.SendMeteors`는 변경된 Rigidbody 속도·위치·등급을 기존 스냅샷으로 전달한다. 프로토콜·권한·직렬화 필드를 바꾸지 않는다.
- 씬·프리팹·패키지·프로젝트 설정 변경 및 추가 Inspector 설정은 없다. 되돌리기는 이 작업의 코드/테스트 변경만 되돌리면 된다.

## 검증

화면 조작 없이 별도 축소 Unity 프로젝트에서 실제 프로젝트 스크립트와 Unity 6000.5.4f1을 사용한다. 검증 프로젝트는 로컬 캐시의 패키지를 읽으며, 원본 프로젝트의 패키지 설정은 바꾸지 않는다.

테스트 결과는 저장소 루트 `.local-validation/688-bat/`에 보존한다.

- 변경 전 관련 테스트: `baseline.xml`, 112/112 통과.
- 새 방향 검사를 기존 구현에 적용: `regression-before.xml`, 43개 중 11개 실패. 수평/상하/수직 방향 및 서버 시선 검사가 이전 고정 상향 구현을 검출했다.
- 변경 후 첫 확대 검사: `final.xml`, 185개 중 184개 통과. 나머지 1개는 `BatSwingArcTests`의 이전 고정 상향 기대값이었다. 새 요구사항인 수평 시선의 수평 출발로 검사 기대값을 갱신했다.
- 최종: `verified.xml`, **185/185 통과**, C# 컴파일 오류 없음. `verified.log`에 실행 로그를 보존한다.
- 실행: Unity 6000.5.4f1, `-batchmode -nographics -runTests -testPlatform EditMode -testCategory !Heavy`.
- 최종 필터: `MeteorBatTests|MeteorBatPropTests|MeteorBatPlayerStrikeTests|MeteorBatSwingTests|BatSwingArcTests|MeteorAftermathTests|MeteorTests|MeteorDiagonalFallTests|MeteorWarningTests`.
- 실제 Rigidbody 속도로 시선과 등급을 검사했다. 카메라 없는 PlayerRig의 입력 yaw/pitch도 검사했다. 동료 타격·소품 물리·운석 피해와 소품 전환·Miss·쿨다운·스윙 회귀 검사 포함.
- 초기 샌드박스 실행은 Unity 라이선스 통신 제한으로 진행하지 못했다. 해당 검증 프로세스만 종료하고 정상 권한의 화면 없는 실행으로 완료했다.
- `git diff --check` 통과. 원본 develop 작업 폴더는 변경하지 않았다.

실제 Stage1 전체 맵의 시각적 조작감, 다중 프로세스 네트워크 플레이 및 제품 빌드는 별도 검증 대상이다.
