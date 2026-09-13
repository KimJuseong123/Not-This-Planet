# 고정못 해제 후 탑승 안내 범위 수정

## 원인과 수정
탑승 가능 거리는 루트 원점까지 수평 4.2m, 조준 판정은 루트 원점에서 세계 위쪽 1.2m인 단일 점을 기준으로 했다. 원점과 몸체가 떨어지거나 기체가 회전하면 표면을 보고 있어도 탈락했다.

보존된 몸체 BoxCollider까지의 실제 거리로 탑승 가능 여부를 계산한다. 후속 피드백에 따라 근처 표면을 자동 선택하는 보정은 제거했다. 파츠 탑승은 크로스헤드 광선의 가장 앞 충돌을 먼저 확인하고, 등록된 견인 파츠에 맞았을 때 그 대상을 확정한다. 먼 기체도 조준 대상으로 유지하지만 기존 4.2m 제한과 연결·용접·점유 조건을 만족할 때만 CanInteract가 참이 되어 E를 표시한다. 벽에 가리거나 빗나간 기체는 선택하지 않는다. 별도 구형 운반체의 기존 판정은 유지한다.

## 검증
- 원점이 치우친 몸체에서 0/90/180도 회전, 표면 조준, 먼 거리와 수직 거리 제한을 검사했다.
- 수정 전 HEAD를 격리 복사본에 적용하면 새 검사 3개 모두 실패한다. 수정 후 모두 통과한다.
- PartTractorActivatorTests, TransportCockpitTests, TransportDismountTests, TractorBeamControllerTests: 57개 중 56개 통과.
- 기존 실패 ActivatedPart1_IsRegisteredAsTransportInteractionTarget은 남아 있다.
- Gameplay 빌드 오류 0개, 경고 16개. 화면 직접 조작 없이 격리 Unity EditMode에서 검사했다. 실제 플레이 화면과 다중 접속은 검증하지 않았다.
- 증거: .local-validation/boarding-focus.xml, boarding-focus-baseline.xml, boarding-focus-build.log.

## 크로스헤드 우선 후속 검증
- 회전별 검사에 조준 빗나감, 벽 가림, 먼 거리에서 대상 유지와 CanInteract 비활성화, 실제 탑승 거부를 추가했다.
- 기존 보정 코드에서 빗나감 문제가 재현됐으며, 최종 검사에서는 테스트 광선이 실제 몸체를 빗나가는지도 확인한다.
- 최종 EditMode 검사 73개 중 72개 통과. 위에 기록한 기존 등록 테스트 실패 1개만 남아 있다.
- Gameplay 빌드 오류 0개, 기존 경고 16개.
- 최종 증거: .local-validation/crosshair-first-after.xml, crosshair-first-build.log.

작업 시작 시 Stage1_Astra.unity에 있던 사용자 변경은 수정하지 않았다.
