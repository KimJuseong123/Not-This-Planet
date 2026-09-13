# 핑 커서와 개인 UI 클릭 피드백

- 기존 `Resources/PingMarker`의 알파 모양을 재사용한 40px 커서. 핀 끝을 클릭 지점으로 사용하고 어두운 테두리로 밝은 배경에서도 구분한다.
- 마우스 왼쪽·오른쪽·휠 버튼을 누르면 같은 끝점을 유지한 채 축소하고 밝게 한다. 기존 게임의 커서 잠금·숨김 정책을 유지한다.
- 멀티 색상은 로컬 소유 `NetPlayer.PingColor`를 우선 읽고, 솔로에서는 `GameSession.LocalPlayer.TeamColor`를 읽는다. 플레이어가 없는 로비에서는 흰색이고, 배정색 변경/게임 진입/로비 복귀 때 갱신한다.
- 로비·설정·일시정지·커스터마이징·탐색 페이지·엔딩 확인 UI의 버튼, 토글, 슬라이더, 텍스트 입력, 선택 그리드에 적용했다. IMGUI가 활성 컨트롤의 마우스 누름을 실제 소비했을 때만 피드백을 낸다. 드래그 중 반복 재생은 하지 않는다.
- 클릭 위치에서 배정색을 사용하는 불꽃 12개가 약 0.4초 동안 퍼진다. 고정 배열 96개를 재사용하고 비배율 시간을 사용하므로 일시정지에도 동작한다. 화면 표시만 수행하고 입력은 가로채지 않는다.
- 0.1초짜리 부드러운 전자 클릭음을 런타임에 한 번 합성한다. 영구 로컬 AudioSource 한 개로 2D 재생하며 네트워크 메시지나 월드 효과음 경로를 사용하지 않는다. 전체 음량(AudioListener)과 효과음 음량 설정을 따른다.
- 자동 초기화하므로 Inspector/씬/프리팹 연결 작업은 없다. 배치/전용 서버에서는 자동 생성하지 않는다.

## 검증

Unity 6000.5.4f1의 별도 복사 프로젝트(`outputs/tractor-tests`)에서 컴파일 성공, EditMode 22개 통과:

- `LocalCursorFeedbackTests`: 핑 알파·색상·원본 보존, 누름 상태 축소/밝기, 클릭음 길이·신호 범위·페이드 검증 3개.
- `TeamPingTests`, `GameSettingsTests`: 기존 핑·설정 회귀 테스트 19개.
- 결과: `.local-validation/cursor-feedback.xml`, `.local-validation/cursor-feedback-final.log`.
- `git diff --check` 통과.

실제 OS 커서 표시, 스크롤·UI 확대 배율에서의 입력, 클릭음 청취와 두 클라이언트의 개별 색상/소리 분리는 수동 플레이 확인이 필요하다. 기존 게임 시점 조작 중에는 커서가 잠겨 보이지 않으며 메뉴를 열면 커서가 나타난다.

## 추가 회귀 검토 (2026-09-13)

별도 복사 프로젝트에서 그래픽을 활성화한 Unity 배치 테스트 50개가 모두 통과했다. 결과는 `.local-validation/cursor-review-verified.xml`과 `.local-validation/cursor-review-verified.log`에 기록했다.

- 새 `CursorInteractionRegressionTests`는 기본 Unity 컨트롤과 래퍼에 같은 실제 IMGUI 이벤트를 보내 비교한다. 버튼은 뗄 때 실행되고, 밖에서 떼면 취소되며, 비활성/스크롤 영역 밖 버튼은 실행되지 않는다. 토글·슬라이더·텍스트 입력·선택 그리드·GUILayout 버튼도 동작한다.
- Play Mode에서 커서 컴포넌트 생성·비활성화·재활성화·제거를 실행했다. 커서 표시/잠금 상태를 변경하지 않고, 2D AudioSource 하나를 사용하며 NetworkIdentity를 추가하지 않는다. 이 경로에서 예기치 않은 Unity 로그는 없었다.
- 기존 `TeamPingTests`, `TeamSlotsTests`, `GameSettingsTests`, `CameraAudioListenerTests`, `Stage1ExplorationTests`, `Stage1TabletViewTests`, `CustomizerRuntimeTests`를 포함했다. 커스터마이징 → 로비 복귀 흐름도 통과했다.
- 처음 GUI 테스트는 그래픽 장치 없는 실행 옵션과 테스트 창 크기 때문에 기본 Unity 버튼에서도 실패했다. 그래픽과 창 크기를 지정해 해소했다. 최초 확장 실행의 태블릿 테스트 1개 실패는 복사본에만 없는 프리팹 때문이었으며 원본 프리팹을 보충한 뒤 통과했다. 제품 코드 결함으로 분류하지 않았다.
- 검토 범위에서 수정이 필요한 기존 기능 회귀는 발견되지 않아 런타임 코드는 변경하지 않았다. 테스트와 검토 기록만 추가했다.

한계: 실제 두 클라이언트 동시 접속, OS 커서의 시각적 품질, 음향 청취, UI 확대 배율별 수동 조작과 배포용 빌드는 이번 검증에 포함하지 않았다. 모든 환경에서 오류가 없다는 보장은 아니다.
