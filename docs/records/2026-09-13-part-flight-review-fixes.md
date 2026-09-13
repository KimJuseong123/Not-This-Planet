# 파츠 비행 검토 후속 수정

## 변경
- 탑승 시 좌석 회전을 골반 오프셋과 인형 전체 방향에 적용한다. 뒤집힌 좌석에서도 세계 위쪽으로 배치하지 않는다.
- 실제 파츠 비행의 수평 속도를 맵 경계까지 남은 거리와 제동력으로 제한한다. 운석 충격으로 회전하는 중에도 적용하고, 안쪽 이동과 수직 이동은 유지한다. 별도 맵 설정이 없으면 기존 ArenaLimit을 사용한다.
- 진입 경로가 없는 무인 호버 분기, 관련 음향 조건과 미사용 상수를 제거했다.
- 연결·용접 탑승 제한은 CanBoardFrom으로 모았다. 비행 중 연결 상태 검사, 네트워크 권한 검사와 kinematic 검사는 유지한다.

## 검증
- Gameplay 빌드: 오류 0개, 기존 경고 16개.
- 격리된 Unity EditMode 검사: 223개 중 222개 통과, 기존 실패 1개.
- 기존 실패: PartTractorActivatorTests.ActivatedPart1_IsRegisteredAsTransportInteractionTarget.
- 새 검사: 좌석 기울기 0/75/180도, 사방 경계에서 운석 충격 후 제한 및 안쪽 재진입, 위치·크기가 다른 맵 경계.
- 기존 용접·복원·견인·네트워크 상태·저중력 관련 검사 131개 모두 통과.
- 결과: .local-validation/part-review-fixes-first.xml 및 part-review-fixes-integration.xml.
- 화면 직접 조작 및 실제 다중 접속 검증은 수행하지 않았다.

## 범위
이전 검토에서 별도로 발견한 공중 하차 직후 returning 상태 해제 문제는 이번 요청에 포함되지 않아 변경하지 않았다.
