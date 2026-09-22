# 668 상황별 BGM 연결

## 연결

| 음원 | 재생 조건 |
| --- | --- |
| Stage 1 Offensive Stage Song.wav | 일반 1장의 공세 연출 진입부터 Combat까지 유지, 공백 없이 반복 |
| Stage 1 Escape BGM.wav | 엔딩 씬에서 서버/솔로 공통 시작 시각 이후 탑승·발사·사진·타이틀 |
| Stage 2 Crash Landing BGM.wav | 같은 엔딩의 SpaceDrift 진입부터 불시착 종료까지 |
| Stage 2 Gameplay BGM.wav | Stage2_Earth 플레이, 공백 없이 반복 |
| practice operation tutorial room.wav | PracticeLobby, AIPracticeLobby, CheatArena, AICompanionTest 진입 메뉴, TestArena.Requested로 들어온 조립 테스트 |

`Main`의 일반 실행과 `Stage1_Astra` 탐색은 기존 Weightless Curiosity, 로비는 Lobby Drift를 유지한다. 연습/조립 테스트에서는 공세 상태보다 연습 음악을 우선한다. 조립 테스트 요청은 기존 코드가 소비하기 전에 음악의 기본 종류로 기억하므로 일반 솔로 재진입에 남지 않는다.

`Resources/Audio/Stage1AudioManager.asset`에 다섯 WAV의 GUID를 연결한다. 커스텀 Inspector의 상황별 BGM 항목에서 바꿀 수 있다. 기존 클립·효과음 설정은 유지하며, 새 곡도 기본 음악 음량(현재 0.18) × 사용자 음악 음량 × AudioListener의 전체 음량을 따른다. 효과음 음량으로 음악을 조절하지 않는다.

## 수명과 멀티플레이

- `GameMusicPlayer`를 확장하여 최대 두 AudioSource로 0.35초 전환을 처리한다. 이전 곡은 전환 후 정지한다. 새 상시 음악은 루프, 컷씬 음악은 원샷이며 기존 Default 곡만 페이드·5초 대기 반복을 유지한다.
- `SceneMusicController`는 이미 복제되는 Stage1Sequence와 DemoEnding 시각만 읽는다. RPC나 네트워크 메시지 포맷을 바꾸지 않는다. 늦참자의 공세는 현재 단계 경과 위치, 컷씬은 공통 경과 위치에서 재생한다. 공세→Combat에서 곡을 재시작하지 않는다.
- 엔딩 시작 전 준비 대기에는 무음이다. 게임 씬에서 엔딩 전송을 준비할 때 기존 음악을 정리하고, 씬 소유 오브젝트가 제거될 때 두 소스를 정지·정리한다.
- `UNITY_SERVER`, 전용 서버 역할 및 배치 실행에서는 자동 음악 플레이어를 만들지 않는다. 솔로·일반 호스트·클라이언트는 로컬 재생한다. 테스트는 배치 자동 생성 차단과 별개로 명시적 테스트 객체를 사용한다.
- 화면·씬·프리팹을 직접 수정하지 않았다. 음원은 Resources 자산의 참조를 통해 빌드에 포함되며 런타임 파일명 탐색이나 Editor 전용 AssetDatabase에 의존하지 않는다.

## 음원 길이

- 공세 214.4초, 탈출 99.08초, 불시착 23.56초, 2장 179.6초, 연습 180초.
- 탈출 연출은 인원에 따라 21~27초이므로 탈출 음원 앞부분을 사용하고 불시착 곡으로 전환한다. 음원 자체는 자르거나 변경하지 않았다.
- 불시착 연출은 23초로 종료되며 잔여 음원은 전환 과정에서 정리한다. 음악 내부의 충돌 강조점이 영상과 실제로 일치하는지는 청취로 확인해야 한다.
- 원본 파일의 시작/끝에 무음이나 잔향이 있으면 `AudioSource.loop`만으로 음악적으로 매끈한 루프를 보장하지는 않는다.

## Git LFS

기존 `.gitattributes`의 `*.wav filter=lfs diff=lfs merge=lfs -text`가 다섯 파일에 적용된다. `.meta`는 텍스트 Git 파일이다. 기존 LFS 필터와 pre-push 훅이 존재하므로 재설치·훅 덮어쓰기·이력 마이그레이션을 하지 않았다.

다섯 음원을 스테이징한 뒤 인덱스에 저장된 LFS 포인터의 SHA-256/크기, 작업 파일, 로컬 `.git/lfs/objects` 객체가 모두 일치함을 검사했다. Unity GUID의 고유성, 설정 참조, WAV 헤더, 스테레오 스트리밍 설정도 검사했다. 이는 로컬 저장 검증이며 원격 업로드 확인을 뜻하지 않는다.

## 검증

- Unity 6000.5.4f1의 숨김 배치 실행으로 전체 스크립트 임포트·컴파일 완료. BGM 변경에 대한 C# 컴파일 오류는 없었다.
- 첫 BGM 테스트 16/16 통과. 반복 재생 및 기존 곡 보존 검사를 추가한 최종 BGM 테스트는 18/18 통과했다. Play Mode 전환을 포함하며 실제 AudioSource의 반복/정지/seek/음량과 재생 소스 수를 확인했다.
- 관련 회귀 묶음은 76개 중 73개 통과, 3개 실패: SceneMusicTests 18/18, CameraAudioListenerTests 2/2, DemoEndingTests 28/28, GameSettingsTests 14/14, Stage1SoundTests 4/4, Stage1CutsceneTests 7/10.
- 실패 3개는 `Stage1CutsceneTests.SerializedAssaultMultiplierControlsInterval`의 세 인자 조합이다. 기존 테스트는 공세 전 30초를 기대하지만 기존 `PhysicsScale.MeteorDrizzleIntervalSeconds`와 `MeteorIntensity`/`MeteorSpawner.IntervalAt`은 10초를 반환한다. 이 네 소스/테스트 파일이 모두 HEAD와 동일함을 비교했다. 이번 BGM 경로를 호출하지 않는 기존 코드·테스트 간 불일치이며, 테스트를 통과시키려고 운석 밸런스나 기대값을 수정하지 않았다. **전체 회귀 묶음이 통과한 상태는 아니다.**
- `UNITY_EDITOR*` 정의를 제거하고 `UNITY_SERVER`를 추가한 Bootstrap 어셈블리 조건부 컴파일: 종료 코드 0. 전체 전용 서버 플레이어 빌드나 실제 서버 접속 검증을 뜻하지 않는다.
- Unity 로그에는 기존 API 폐기 경고, Mirror Weaver의 BouncyCastle 참조 경고와 Terrain 셰이더 의존성 경고가 관찰되었다. 이 작업에서 해당 영역은 수정하지 않았다.
- 근거: 워크트리 `Temp/bgm-validation/regression.xml`, `regression.log`, `server-compile.log`, `assets.json`, `unchanged-meteor-baseline.txt`. `Temp`는 Git에 포함하지 않는다.
- 실제 여러 프로세스/PC의 네트워크 플레이, 전체 클라이언트/서버 빌드, 사람이 듣는 음량·전환·루프 경계 품질, GitLab 원격 업로드는 미검증이다. 원격 전송이나 커밋은 수행하지 않았다.
