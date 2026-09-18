# Stage2 Earth 씬 검수 자료

Unity 6000.5.4f1, 603 브랜치. [작업 기록](../../docs/records/2026-09-17-stage2-art-integration.md).

- [전체 배치](overview.png), [도착 지점](arrival.png), [조립 구역](assembly.png), [폐허·식생](ruins-and-vegetation.png), [크레인·보관 구역](crane-and-containers.png)
- [모델 배치 수](placement-counts.json): 46종 1,289개. 식생 메시 888개.
- [씬 검사](scene-validation.json): 상호작용 26개, 볼트 9개, 로켓 모듈 3개, 누락 스크립트 0개.
- [Terrain 검사](terrain-validation.json): 320m 지형, 513 높이맵, 4개 표면 레이어, TerrainCollider 및 평탄한 핵심 작업 구역. 이미지는 Terrain 전환 후 갱신했다. 기존 배치 수 JSON은 첫 모델 통합 당시 기준이며 이후 외곽 식생이 추가됐다.
- [Unity 자동 테스트](test-results.xml): `Stage2Tests;StageCatalogTests;Stage2ArtTests`, 18개 통과, 실패 0개.
- [실행 중 전체 화면](../stage2-draft/overview-play.png): 기존 Stage2 회귀 테스트에서 갱신.

검증 범위는 로컬 플레이 진입, Stage1 엔딩 → Stage2 진입, 기존 조립·복구·제독 규칙, 통행 공간, 실제 볼트와 조작 위치 일치, 복제 상태에 따른 크레인 트롤리·뚜껑·승강기·사다리 외형 동작 및 케이블 재질이다. 초기 테스트의 오프라인 권한 재설정과 비동기 씬 로딩 대기 문제를 보완한 후 통과했다.

실제 여러 PC 간 접속·늦참·재접속, VR 기기 성능, 전체 플레이어 빌드는 이번 검증 범위에 포함하지 않았다. 배치 실행 로그와 씬 변경 전 로컬 백업은 커밋에서 제외한다.

2026-09-18 오염 전용 검증: [수정 전 4/5 통과](contamination-baseline.xml), [수정 후 회귀 포함 23/23 통과](contamination-validation.xml). [기획 대조와 미구현 피드백](../../docs/records/2026-09-18-stage2-contamination-audit.md) 참고.
