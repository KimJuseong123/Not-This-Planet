# 로비 BGM

`METAGAME/Assets/_Project/Resources/Audio/LobbyAudioManager.asset`의 Inspector에서
BGM 클립과 다음 직렬화 필드를 조정한다.

- Music Clip: `Audio/BGM/Lobby Drift.wav` (148.96초, 스테레오)
- 기본 음량: 0.25
- 페이드 인: 3초
- 페이드 아웃: 곡 마지막 5초
- 곡 사이 대기: 5초

Lobby 씬의 LobbyBootstrap에 설정 자산을 연결했다. 기존 GameMusicPlayer를 사용하며
설정의 음악 및 전체 음량을 따른다. 로비 카메라의 활성 AudioListener로 출력하고,
게임 씬으로 넘어가면 로비 오브젝트와 함께 정지·제거된다. 씬 전환 시에는 곡 끝의
5초 페이드를 기다리지 않는다. 로비에 돌아오면 처음부터 페이드 인한다.
로비 영상의 오디오는 기존 VideoAudioOutputMode.None 설정을 유지한다.

Gameplay, Bootstrap, Editor 어셈블리 컴파일: 오류 0.
격리된 Unity Play Mode에서 원본 Lobby Drift WAV를 스트리밍 임포트하고 실제
GameMusicPlayer로 재생했다. 8초 시점 재생 중, 음량 0.25 및 0이 아닌 PCM 출력을
확인했다 (`Temp/lobby-music-result.txt`). 실제 로비 전체 흐름을 직접 청취한 검증은 아니다.
