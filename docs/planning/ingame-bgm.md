# 인게임 BGM

2026-09-22: 공세·탈출·불시착·2장·연습/조립 테스트의 상황별 곡을 추가했다.
아래의 페이드·5초 대기는 기존 탐색 BGM 기준이며, 새 상시 곡은 공백 없이 반복한다.
컷씬 곡은 공유 연출 시각을 따른다. 연결표와 검증 한계는
[668 BGM 연결 기록](../records/2026-09-22-stage-music.md)을 참고한다.

`METAGAME/Assets/_Project/Resources/Audio/Stage1AudioManager.asset`를 선택하면
Inspector의 **인게임 BGM** 영역에서 다음 직렬화 필드를 조정할 수 있다.

| 항목 | 기본값 |
| --- | --- |
| Music Clip | Weightless Curiosity.wav |
| 기본 음량 | 0.18 |
| 페이드 인 | 4초 |
| 페이드 아웃 | 6초 |
| 곡 사이 대기 | 5초 |

원본 WAV는 변경하지 않고 스테레오 스트리밍으로 임포트한다.
`SpaceBootstrap`이 게임 씬에 `GameMusicPlayer`를 생성한다. 전용 서버 및 배치
실행에서는 생성하지 않는다. 마지막 6초 동안 음량을 줄이고 실제 재생 종료 후
5초 쉬었다가 다시 페이드 인한다. 게임 시간 배율과 독립적으로 재생한다.
씬 해제 또는 컴포넌트 비활성화 시 정지하며 재활성화 시 처음부터 시작한다.

로비 및 Esc 설정의 **음악** 슬라이더는 즉시 반영되며 기존 저장·취소·초기화 흐름을
따른다. 최종 음량은 기본 음량 × 음악 설정 × 전체 음량이다. 효과음과 독립적이며,
음악 100%는 원본 전체 음량이 아닌 제작자가 지정한 기본 음량을 뜻한다.

## 검증 (2026-09-11)

### Stage1_Astra 연결

- `S15P21B202-426-sound-bgm`의 `99ba39b`까지 428 브랜치에 병합했다.
- `Lobby`의 솔로 및 Mirror 목적지는 `Stage1_Astra`이며 빌드 목록에도 포함한다.
- `Stage1_Astra`의 `SpaceBootstrap.audioManager`는 `Stage1AudioManager.asset`를
  직접 참조한다. Main과 같은 BGM·효과음 설정을 사용한다.
- 저장된 화면·조작 설정, 음악·효과음·전체 음량, Esc 메뉴 및 HUD는 공용
  `SpaceBootstrap`과 `GameSettingsRuntime` 경로를 그대로 사용한다.
- Astra의 수제 조명은 유지한다. 모래폭풍 시각 효과는 조명 교체 여부와 분리해
  기존 폭풍 물리와 동일한 프로필·맵 범위·네트워크 시계로 초기화한다.
- 병합 후 Bootstrap 및 참조 런타임 어셈블리 컴파일: 오류 0, 경고 53.
  씬 오디오 자산과 BGM GUID 연결을 확인했다. 전체 씬 실행·청취 검증은 미수행이다.

### BGM 구현 당시 검증

- Gameplay, UI, Bootstrap, Editor 코드를 Unity 참조 어셈블리로 컴파일: 오류 0.
- 격리된 Unity 6000.5.4f1 Play Mode에서 실제 `GameMusicPlayer.cs`와 4초 테스트
  클립으로 페이드 인·아웃, 음악 음소거·복원, 5초 대기, 재시작 페이드 및 비활성화
  정지를 확인했다. 설정 제공자와 AudioManager는 테스트용 대역을 사용했다.
- 결과: `Temp/bgm-playback-result.txt`, 로그: `Temp/bgm-check.log`.
- 원본 WAV: 212초, 스테레오, 48 kHz. GUID로 설정 자산에 연결했다.
- 실제 게임 씬에서 원곡과 효과음·대화를 함께 듣는 검증은 수행하지 않았다.

## 소리가 들리지 않는 문제 수정

실제 Editor 로그에서 `There are no audio listeners in the scene` 경고가 반복됐다.
`SpaceBootstrap`은 기존 씬 카메라의 리스너를 비활성화하지만, `OrbitCamera.Create`가
생성하는 로컬 카메라에는 AudioListener가 없었다. 카메라 생성 시 리스너를 추가했다.
이전 BGM 단독 테스트는 별도 리스너를 생성해 이 통합 문제를 검출하지 못했다.

실제 Gameplay 어셈블리를 격리된 Unity 6000.5.4f1에서 실행한 결과, 수정 전에는
1인칭·3인칭 모두 리스너 0개였고 수정 후에는 모두 활성 리스너 1개였다.
`CameraAudioListenerTests` 회귀 테스트를 추가했으며 Gameplay 및 테스트 컴파일도
통과했다. 증거는 `Temp/camera-audio-baseline.log`, `Temp/camera-audio-fixed.log`다.
사용자가 조정한 BGM 기본 음량 1은 유지했다. 열려 있는 게임은 Play Mode를 다시
시작해야 새 카메라 생성 코드가 적용된다.
