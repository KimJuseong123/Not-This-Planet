## Jira
- 이슈: S15P21B202-603
- 작업 유형: 코드 / 에셋 / 씬·프리팹 / 문서·기록

## 무엇을 · 왜
Stage2_Earth 초안을 chapter-2-plan에 맞춰 Terrain 기반의 폐산업 배수로/발사 작업장으로 구성했다. 크레인·컨테이너·로켓 모듈·체결·제독을 모델과 연결하고 주변 폐허·식생을 배치했다.
후속 요청으로 배수로 감속/부유, 서버 권한의 압력 분출/감압과 Stage1→Stage2 불시착 전환을 추가했다. 컷씬은 기존 엔딩에 유영·운석 충돌/파손·불시착의 세 구간을 잇는 하나의 23초 시퀀스다. 원래 초안 이슈보다 확장된 범위이므로 Jira에 이 추가 요청을 반영해야 한다.
리뷰 지적에 따라 기대 상호작용을 29개/압력 밸브 3개로 고정하고 ID·등록 목록을 검사한다. Terrain 변환은 지면을 삭제하지 않고 미리보기로 남기며, 별도 에셋에 작업하고 자동 저장 없이 Undo/Redo를 지원한다. 배수로 높이도 보존한다.

## 머지 전에 팀장이 확인할 것
- [범위·완료 조건·이미지·검증 근거](https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202/-/blob/S15P21B202-603-plan-2stage/docs/records/S15P21B202-603-review-evidence.md)를 확인하고 Jira 템플릿을 구체적인 완료 조건으로 바꾼다. 이 파일은 외부 본문에 아직 반영되지 않은 교체안이다.
- [이번 에디터 안전성 검증](https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202/-/blob/S15P21B202-603-plan-2stage/artifacts/stage2-review/README.md)에서 누락/초과 타깃, Undo/Redo, 자동 저장 방지, 배수로/원본 에셋 보존, 실패 롤백 결과를 확인한다.
- [기존 실제 3프로세스 멀티 검증](https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202/-/blob/S15P21B202-603-plan-2stage/artifacts/stage2-final-validation/README.md)을 확인한다. 압력 8개, 조립 작업장 5개 시나리오는 localhost 결과다.
- Stage1 엔딩부터 Stage2 도착까지 이어서 재생하고, 1~4인 장시간 완주/조작감과 실제 인터넷 지연 환경을 확인한다. 컷씬 범위를 이 이슈에 포함할지 분리할지도 머지 전에 확정한다.

![Stage2 현재 전체 배치](https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202/-/raw/S15P21B202-603-plan-2stage/artifacts/stage2-review/overview.png)
![배수로 수면](https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202/-/raw/S15P21B202-603-plan-2stage/artifacts/stage2-water/02-water-surface.png)
![실행 중 압력 분출](https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202/-/raw/S15P21B202-603-plan-2stage/artifacts/stage2-pressure/pump-0.png)
![불시착 전 우주선 파손](https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202/-/raw/S15P21B202-603-plan-2stage/artifacts/stage2-arrival/04-broken-ship.png)

전체 배치는 이번 리뷰 수정 후 현재 씬의 검수 렌더다. 수면/압력/컷씬 이미지는 해당 기능 구현 당시의 Unity 검수 렌더이며 촬영 범위와 한계는 위 근거 문서에 적었다.

## 남은 것
- 외부 Jira 범위와 완료 조건, MR 본문의 교체안 반영. GitLab API 401 및 Jira 연결 부재로 외부 수정은 수행하지 못했다.
- 실제 다중 PC 인터넷 지연/손실, 4인 장시간 완주와 재미/품질 검수. 현재 자동 테스트와 localhost 실측으로 이 조건까지 완료했다고 판단하지 않는다.
- Terrain 미리보기 Undo 뒤 생성 에셋은 Redo를 위해 남는다. 최종 폐기 시 참조가 없는 해당 Preview 폴더만 정리한다.
