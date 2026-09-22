# 공세 BGM 생성 프롬프트 (668)

2026-09-21. 기준: S15P21B202-668-sound-bgm, 80b4b210. 음악 생성과 게임 연결은 아직 수행하지 않았다.

## 음악 방향

운석 속에서 파츠 운반·용접·방어·구조를 분담하는 우주 협동 게임에 맞춰, 긴장과 함께 서로를 재촉하며 움직이는 추진력을 만든다. 136 BPM, 4/4, 전자음 중심의 리듬과 가벼운 오케스트라 질감을 제안한다. 이는 창작 제안이며 기존 두 WAV를 청취해 조성·템포를 맞춘 결과는 아니다.

## 복사용 프롬프트

Create an instrumental gameplay soundtrack for a stylized cooperative sci-fi physics game set on an alien planet. A meteor barrage erupts while a small crew frantically carries spaceship parts, welds their escape ship, deflects meteors, and rescues teammates. Capture escalating urgency, exhilarating teamwork, and a touch of mischievous space-adventure energy. The emotional message is: “The sky is falling, but we can still pull this off together.”

136 BPM, steady 4/4. Use a tight pulsing synth bass, nimble analog arpeggios, crisp syncopated electronic percussion, restrained staccato strings, and a short memorable futuristic motif. Minor-key tension with occasional hopeful harmonic lifts. Keep the groove readable and propulsive, with controlled low end, smooth high frequencies, and space in the midrange for teammate voices and gameplay effects.

Establish urgency within the first two seconds. Build rhythmic density over the first eight seconds, then sustain a driving gameplay groove. Add subtle variations every eight or sixteen bars without a long breakdown or a dramatic final climax. Aim for approximately two minutes, with the main section arranged for seamless looping and compatible beginning and ending harmony. Keep the ending rhythmically active, with no fade-out or final resolving chord.

Instrumental only. No vocals, lyrics, spoken countdowns, choir, sirens, alarm beeps, explosion samples, horror drones, overwhelming brass, abrasive distortion, giant EDM drops, or long silent passages. Express rising danger through rhythm and arrangement rather than simply increasing loudness. Exciting, focused, adventurous, and suitable for repeated listening during sustained cooperative play.

## 코드 검토 및 연결 시 주의점

현재 구현의 오류로 단정할 항목이 아니라, 새 공세 BGM을 연결할 때 해소해야 할 제약이다.

1. **확인 / 중간: 공세 전용 선택 경로 없음.** `AudioManager.cs`에는 단일 `musicClip`이 있으며 `GameMusicPlayer.Configure`가 이를 재생한다. 기존 Stage1 설정의 곡을 교체하면 탐색 구간 음악도 바뀐다. 공세용 클립과 상태별 전환을 별도로 추가해야 한다.
2. **확인 / 중간: 공세와 맞지 않는 반복 공백.** `GameMusicPlayer.cs`는 `loop = false`이며 곡 끝 페이드 후 `MusicRepeatDelaySeconds`만큼 기다린다. `Stage1AudioManager.asset` 값은 5초다. 공세용 반복 정책을 별도로 두고 로비·탐색의 기존 페이드와 대기 동작은 유지한다. 생성 프롬프트만으로 실제 무봉합 루프를 보장할 수 없으므로 음원 경계 편집 및 청취가 필요하다.
3. **확인 / 중간: Assault는 전체 전투가 아닌 8초 연출 상태.** `Stage1Sequence.cs`에서 5초에 `AssaultEnabled`가 켜지고 8초에 `Combat`으로 전환된다. `Phase == Assault`만 음악 조건으로 삼으면 실제 전투 시작 때 음악이 종료된다. 연출 진입부터 시작하고 이후 공세 활성 상태에서도 유지하도록 설계한다. 종료·로비 복귀 조건은 별도로 정의한다.
4. **가능 / 중간: 이중 재생 및 설정 우회.** `SpaceBootstrap.CreateSoundRuntime`가 기존 음악 플레이어를 생성한다. 새 AudioSource만 추가하면 일반 BGM과 겹칠 수 있다. 전환 시 기존 곡을 감쇠·정지하고 음악/전체 음량, 음소거, 씬 종료 정리를 함께 적용한다. 현재 BGM은 음악 설정을 곱하고 전체 음량은 AudioListener에 맡긴다.
5. **연결 후 검증:** 솔로·호스트·클라이언트의 탐색→공세 연출→Combat, 중도 참가, 연출 재실행, 엔딩·로비 복귀, 음악 0/복원, 설정 취소·초기화, 반복 경계 및 효과음/팀 대화의 명료도를 확인한다. 서버 권한 조건으로 클라이언트의 로컬 BGM까지 막지 않는다.

## 검증 범위 및 작업 환경

- 코드·기획·오디오 설정을 정적으로 검토했다. 화면 조작, Unity 실행, 빌드, 런타임 테스트, 원곡 청취는 하지 않았다. 오류가 전혀 없다는 보장은 하지 않는다.
- 게임 코드·씬·음원·설정은 수정하지 않았다. 이번 문서 추가로 실행 기능이 바뀌지는 않는다.
- 워크트리 생성 시 기존 Mirror/Edgegap 폰트 3개에 LFS 포인터 형식 경고가 발생했다. BGM 변경으로 생긴 문제가 아니며 폰트는 수정하지 않았다.
