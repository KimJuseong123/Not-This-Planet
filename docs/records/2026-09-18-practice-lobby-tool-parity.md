# PracticeLobby 도구 모델 위치 동기화 (S15P21B202-638)

- 기준: `be320ccd`, 브랜치 `S15P21B202-638-qa`, Unity 6000.5.4f1.
- 원인: S15P21B202-635에서 Stage1_Astra의 도구 모델 위치를 복구한 뒤에도 PracticeLobby에는 복구 전 자식 좌표가 남아 있었다.
- 변경: `METAGAME/Assets/Scenes/PracticeLobby.unity`의 방망이 모델 1개와 용접건 모델 31개의 `m_LocalPosition`을 Stage1_Astra의 동일 계층 값으로 맞췄다. 방망이 `Visual_ZUpToYUp`은 `(-0.39283943, -0.2590623, 2.2563381)`에서 `(0, 0, 0)`으로 복구했다.
- 도구 루트의 로비 배치, 회전, 크기, 충돌체, 참조와 런타임 코드는 유지했다. 직렬화 비교로 32개 위치 필드 외 모든 바이트가 동일함을 확인했다. 테스트 기대값은 변경하지 않았다.

## 검증

별도 검증용 프로젝트 `outputs/tractor-tests`에 현재 프로젝트 파일을 동기화하고 Unity 배치 테스트를 실행했다.

- 수정 전: `PracticeLobbyTests.HandToolAndClampSettingsMatchAuthoredStage1` 1개 실행, 보고된 `Visual_ZUpToYUp` 좌표 불일치로 실패 재현.
- 수정 후: `PracticeLobbyTests` **7/7 통과**. Stage1 설정/계층 비교, 실제 플레이어의 도구 집기와 고정못 조작, 연습 객체 동작, 초기화와 Stage1 출발 경로 포함.
- `git diff --check` 통과.
- 로컬 결과: `.local-validation/practice-lobby-638/before.xml`, `after.xml`, `baseline.log`, `after.log`, `changed-positions.json`, `serialization-check.json`.

전체 테스트 스위트, 별도 플레이어 빌드와 수동 화면 검사는 실행하지 않았다. StuckEscape 테스트 격리 개선은 이 변경에 포함하지 않는다.
