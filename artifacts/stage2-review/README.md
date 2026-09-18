# Stage2 리뷰 수정 검증 — 2026-09-18

Unity 6000.5.4f1. [tests.xml](tests.xml): **12개 통과, 실패 0개**.

원본 프로젝트가 Unity 에디터에서 열려 있어 `tmp/stage2-review/METAGAME`에 에셋/설정과 캐시를 복사하고, 동일한 수정 소스로 배치 실행했다. 원본 에디터를 종료하거나 화면을 조작하지 않았다. 첫 원본 폴더 배치 실행은 프로젝트 점유 상태에서 코드 1로 종료되어 검증 결과로 계산하지 않는다.

## 이번 실행 범위

- Stage2ArtTests 2개: 실제 씬 모델/참조/동선 및 클라이언트 복제 상태에 따른 크레인·뚜껑·승강기·사다리 아트 동작.
- Stage2ReviewTests 10개: 압력 밸브 0/2/4개를 모두 거부, 개수가 같아도 ID가 바뀌면 거부, 씬 대상과 등록 목록 불일치 거부.
- 변환: 기존 지면은 남겨 비활성화, 신규 Terrain 생성, 디스크 씬 파일은 동일, Undo 한 번으로 원래 지면 복원/신규 Terrain·외곽 식생 제거.
- 보정: TerrainData 복사본 사용, 배수로 높이 보존, 원본 TerrainData와 씬 파일의 바이트 동일, Undo/Redo로 Terrain과 Collider의 참조 및 모든 기존 Transform 위치 복원.
- 실패 경로: 구조가 잘못된 씬에서 데이터 복사 후 실패하더라도 원래 참조 복원 및 이번 실행의 새 폴더만 제거.
- 저장하지 않은 씬은 변경 전에 거부, 비활성 Terrain도 중복 변환으로 거부.

실제 소스 기능을 호출해 Unity Undo, 파일 바이트, Terrain 높이와 참조를 검사했다. 원본 프로덕션 씬/지형 에셋은 이번 수정에 포함하지 않는다. 테스트용 씬과 Preview 에셋은 테스트 종료 시 정리한다.

## 현재 씬 증거

[씬 검사](scene-validation.json), [Terrain 검사](terrain-validation.json), [전체 배치](overview.png), [폐허·식생](ruins-and-vegetation.png).

![현재 Stage2 전체 배치](overview.png)

이번 수정 후 소스로 기존 저장된 Stage2_Earth 씬을 읽어 Unity 카메라로 렌더했다. 생성형 컨셉 아트가 아니다. 실행 중의 압력 분출은 [기존 PlayMode 검수](../stage2-pressure/pump-0.png)를 별도로 본다.

## 기존 결과와의 구분

이번 수정은 에디터 도구/검증 코드이며 런타임 네트워크 코드는 바꾸지 않았다. [이전 3프로세스 실측](../stage2-final-validation/README.md)의 압력 8 PASS/작업장 5 PASS는 이전 실행 결과다. 이번에 인터넷 멀티, 4인 장시간 완주, 호스트 이전, 전체 플레이어 빌드를 새로 검증했다고 주장하지 않는다.

기존 코드의 obsolete API 경고 및 Mirror.BouncyCastle.Cryptography 해석 경고가 컴파일 로그에 남아 있다. 수정 코드 컴파일과 선택 테스트는 성공했다. MR/Jira의 외부 본문은 아직 갱신하지 못했으며 [리뷰 근거](../../docs/records/S15P21B202-603-review-evidence.md)와 [MR 교체안](../../docs/records/S15P21B202-603-mr-description.md)에 그 한계를 명시했다.
