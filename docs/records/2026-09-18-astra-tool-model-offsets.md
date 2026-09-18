# Stage1_Astra 도구 모델 위치 복구

- 기준: `83f2e240`, Unity 6000.5.4f1. 작업 전 저장소 변경 없음.
- 증상: 거치대 밖에 보이는 도구, 낙하 후 바닥 아래로 들어가는 모델, 잡으면 사라지는 용접건.

## 원인과 변경

씬에 저장된 모델 자식 Transform이 Rigidbody/Collider와 분리된 위치로 이동해 있었다.
방망이 `Visual_ZUpToYUp`의 로컬 위치는 원래 `(0, 0, 0)`에서
`(-0.39283943, -0.2590623, 2.2563381)`로 바뀌어 있었다.
용접건의 몸체 조각 26개와 꽃잎 5개도 이동했고, 몸체 조각에는 공통 로컬 이동량
약 `(-4.512677, -0.518125, -0.785680)`이 적용되어 있었다.

Unity에서 수정 전 방망이 모델 중심과 가장 가까운 충돌체 사이 거리는 1.851m,
용접건 Petal_01 중심과 충돌체 사이는 2.010m였다. 모델과 물리가 다른 위치에 있어
물리 본체가 정상적으로 고정되거나 바닥에 멈춰도 보이는 모델은 떠 있거나 지하로 들어갔다.
용접건은 손에 들 때도 잘못된 자식 위치를 유지해 1인칭 카메라 시야 밖에 있었다.
방망이의 1인칭 표시 검사는 수정 전에도 통과했다. 모든 '안 보임' 증상을 두 도구에
공통으로 재현했다고 주장하지 않는다.

`artifacts/stage1-vegetation/Stage1_Astra.before.unity`의 동일 fileID를 대조해
위 32개 모델 Transform의 `m_LocalPosition`만 복구했다. 도구 본체의 거치 위치와 회전,
거치대, 충돌체, 물리·네트워크 코드, 나머지 씬 블록 6,266개는 보존했다.
씬 전체 재저장이나 프리팹 교체는 하지 않았다.

## 검증

원본 에디터에는 연결하지 않고 `outputs/tractor-tests` 복사본에서 Unity로 실행했다.
기존 테스트에 실제 씬 모델/충돌체 근접, 1인칭 카메라 시야 및 놓은 뒤 모델 자세 복원,
10초 낙하 후 안정화와 모델의 바닥 위 잔류 검사를 추가했다.

- 수정 전: ToolDisplayMountTests 6개 중 4개 실패. 양쪽 도구의 모델/충돌체 불일치,
  용접건의 1인칭 시야 이탈, 낙하한 용접건 꽃잎 중심이 y=-1.077m인 상태를 재현.
- 수정 후: ToolDisplayMountTests, ToolCarryCollisionTests, FirstPersonToolVisualTests,
  ToolShadowTests **34/34 통과**. EnterPlayMode에서 실제 PlayerRig의 집기와 Q 놓기,
  거치 해제, 10초 중력 시뮬레이션 포함. 컴파일 오류 없음.
- 씬 정리 검사 통과. 직렬화 비교에서 32개 위치 필드 외 변경 없음 확인.
- 기존 large/medium/smallDebrisPrefab 참조 타입 경고는 이번 수정 대상 밖이다.

자료: `artifacts/astra-tools-baseline.xml`, `artifacts/astra-tools-after.xml`,
동명 로그, `artifacts/astra-tool-probe.txt`,
`artifacts/astra-tool-repair/changed-positions.json`, `play-validation.txt`.
수정 전 씬 백업은 `.local-validation/astra-tool-before.unity`에 보관했다.

한계: 1인칭 검증은 카메라 frustum과 렌더러 상태 검사이며 픽셀 캡처 검사는 아니다.
실제 호스트/클라이언트 접속, 플레이어 빌드, 바닥을 조준한 채 놓는 등 모든 투척 조건을
검증한 것은 아니다. 열린 원본 에디터가 이전 씬을 유지한다면 Play Mode 종료 후
`Stage1_Astra`를 다시 열어 수정된 씬 데이터를 로드해야 한다.
