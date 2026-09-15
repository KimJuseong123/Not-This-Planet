# S15P21B202-557: 플레이어 이모티콘 선택 및 정면 표시

## 기준과 범위

- 시작 기준: 2026-09-16 작업 시 fetch한 origin/develop `11515cb`.
- Jira: [S15P21B202-557](https://ssafy.atlassian.net/browse/S15P21B202-557), 상위 에픽 S15P21B202-11 (UI/UX 프레임워크 및 메뉴 시스템).
- 제출 브랜치: `S15P21B202-557-player-emotes`. 임시 브랜치 `codex/emote-billboard`의 이모티콘 변경만 선별한다.
- 제출 범위: UI/입력/상태/네트워크 연결, PNG 6종과 meta, 임포트 도구, 테스트, 이 작업 기록. AI 실험 코드·서버·씬, 물리 및 렌더링 설정 변경은 제외한다.
- AI 실험 폴더와 현재 열려 있는 PracticeLobby 프로젝트는 수정하지 않았다.
- 입력 원본은 사용자가 제공한 Tripo FBX+텍스처 ZIP 6개. 로컬 Blender 정면 렌더로 512×512 RGBA PNG를 생성했다. 추가 생성 크레딧/결제 없음.

## 동작

- F1 유지 → 마우스를 원하는 그림 방향으로 움직임 → F1을 놓아 전송.
- 중심에서는 취소, 우클릭/Esc로 취소. F1 키는 Core/Controls.EmoteWheel에 모았다.
- 물음표/느낌표/엄지/하트/울음/화남. 3.5초 표시, 마지막 0.35초 페이드, 0.8초 재사용 간격.
- SpaceBootstrap에서 자동 구성하므로 Stage1 및 같은 bootstrap을 쓰는 로비/빌드에 적용된다. 씬 수동 배치 불필요.
- 소유자 Command → 서버 종류/쿨다운 검사 → NetPlayer SyncVar(종류, 종료 시각) → 각 관찰자 HUD.
- 화면에 PNG를 투영하므로 앞/뒤/옆 어디서 봐도 정면이다. 카메라 회전은 전송하지 않는다.
- 남의 머리 위에 표시하며 벽 뒤/카메라 뒤/45m 밖에서는 숨긴다. 본인은 화면 아래 작은 확인 아이콘으로 본다.
- 솔로도 같은 상태 규칙 사용. AI 명령 실패와 연결하는 기능은 이번 범위에 포함하지 않았다.

## 파일/용량

- 런타임은 Resources/Emotes의 PNG 6장만 로드. FBX와 4K/8K 원본 텍스처는 게임에 넣지 않음.
- RGBA32 512px×6, mipmap 없음: 기본 텍스처 데이터 약 6MiB (엔진 오버헤드 제외).
- 원본과 렌더 스크립트는 별도 output/emotes-billboard-20260916에 보관.

## 검증

- 이미지 6개 정면 렌더와 투명 배경 검사.
- Unity 6000.5.4f1 배치 임포트/스크립트 컴파일 정상 종료. 새 PNG 및 스크립트 .meta 생성 확인.
- EditMode `Metagame.Gameplay.Tests.EmoteTests`: 18/18 통과, 실패 0. 종류 검증/쿨다운/만료, 6방향 선택과 중심 취소, 6개 리소스/알파 임포트, 일시정지 입력 잠금 보존 검사.
- 기록: 별도 output/emotes-billboard-20260916의 unity-import.log, unity-tests.log, emote-tests.xml.
- 실제 플레이어 빌드는 이번에 생성하지 않았다. Resources 포함 경로는 확인했으나 빌드 실행 검증과 동일하지 않다.
- 실제 두 클라이언트 접속, F1 입력과 타인 시점 실플레이는 별도 확인 필요.
- 최신 develop에 이미 존재하는 LFS 포인터 경고(ThirdParty 폰트 3개)는 수정하지 않았다.

## 독립 제출 재검증 (2026-09-16)

- 최신 `origin/develop` `ec7f09ed0934cbe985bd898d8dde596d2235cd15` 위로 이모티콘 커밋만 재배치했다. 충돌 없음.
- Unity 6000.5.4f1, 배치 EditMode, 필터 `Metagame.Gameplay.Tests.EmoteTests`: 18/18 통과, 실패/건너뜀 0.
- 로컬 결과: 프로젝트 저장소 루트의 `emote-557-tests.xml`, `emote-557-tests.log` (커밋 제외).
- develop 대비 26개 파일에만 변경이 있으며 씬/프리팹/ProjectSettings/렌더링 설정/AI 코드 변경은 없다.
- PNG 6장은 Git LFS 대상으로 추적한다. 원본 3D 모델 및 AI 모델은 포함하지 않는다.
- 2인 실제 접속 및 실행 파일 빌드 검증은 미실시. 브랜치 공유는 가능하지만 출시 검증 완료를 뜻하지 않는다.
- MR 생성 및 develop 머지는 이번 제출 범위에서 제외한다.
