# 1장 탈출 · 불시착 · 2장 BGM 생성 프롬프트

2026-09-21 / S15P21B202-668-sound-bgm. 코드·기획·씬 직렬화 데이터 검토 기반의 창작 제안. 음원 생성·청취·게임 연결·화면 조작은 수행하지 않았다.

## 1. 1장 탈출 — 해냈다는 안도감에서 우주의 경이로움으로

Create a short instrumental cinematic cue for a stylized cooperative sci-fi adventure. A small crew has finally assembled a slightly awkward handmade spaceship together and escapes a meteor-struck alien planet. Capture earned relief, warm camaraderie, a joyful liftoff, and the breathtaking openness of space. The feeling is “We actually made it — together.”

Around 108 BPM, 4/4. Warm analog synth chords, sparkling arpeggios, flowing strings, gentle rounded brass, and restrained uplifting percussion. A simple four-note ascending motif, hopeful suspended harmonies opening into a bright major chord. Keep the triumph intimate, adventurous, and slightly playful.

Use an editable modular structure: a gentle anticipation section that can be extended during crew boarding; a clear five-second rising swell for ignition and ascent; a warm five-second celebratory phrase for the team photograph; then a soft three-second transition into spacious, weightless synth ambience. Leave the final harmony open to continue into the next scene. Provide clean musical edit points and a short natural reverb tail.

Instrumental only. No vocals, choir, spoken countdown, rocket sound effects, explosions, military march, oversized trailer drums, or final end-credits climax. Leave room for engine audio. This is a chapter victory with more adventure ahead.

## 2. 불시착 — 안도감의 붕괴와 위기, 낯선 지구

Create a 23-second instrumental cinematic transition cue for a stylized cooperative sci-fi adventure. A homemade spaceship drifts peacefully through space, is struck by a meteor, loses control, and makes an emergency descent toward a ruined Earth. Move from fragile relief to sudden shock, urgent survival, and an uncertain landing. Keep the crew's adventurous spirit alive beneath the danger.

0–5.5 seconds: airy synth pads, sparse bell-like notes, and a gentle ascending motif suggesting the previous escape victory. 5.5–8 seconds: introduce a tightening low pulse and unstable harmony as danger approaches. At 8 seconds: sharply interrupt the hopeful phrase with a brief dissonant musical accent, leaving space for the game's collision sound. 8–14 seconds: fragmented arpeggios, nervous staccato strings, and irregular accents over a steady pulse suggest hull damage and loss of control. 14–21.6 seconds: urgent forward motion, approximately 140 BPM in feel, with descending strings and controlled electronic percussion for emergency atmospheric descent. At 21.6 seconds: abruptly thin the music for the landing impact. 21.6–23 seconds: a short, dark unresolved synth tail leading into the abandoned launch site.

Through-composed, non-looping, with clear edit points at the major transitions. Instrumental only. No vocals, choir, literal alarms, warning beeps, explosion samples, engine samples, horror screams, huge sub-bass drops, or a victorious ending. The game already supplies impact and warning sounds. Dramatic and tense, but not hopeless or tragic.

## 3. 2장 — 폐허 속 협동 작업과 재출발

Create an instrumental looping gameplay soundtrack for a stylized cooperative sci-fi game set in an abandoned launch complex on a ruined Earth. A stranded crew explores weathered concrete structures, rusted cranes, overgrown platforms, flowing drainage channels, and leaking industrial facilities. They open cargo containers, lift rocket sections, guide each other from ladders, tighten bolts, and decontaminate teammates while rebuilding a way to escape.

Blend atmospheric electronic adventure music with a restrained industrial groove. 104 BPM, steady 4/4. Muted analog bass pulses, soft mechanical percussion, dry brushed drums, warm electric piano, sparse rounded metallic mallet notes, and distant weathered synth pads. Make the percussion musical rather than literal recordings of tools. Use a short, curious four-note motif, minor or Dorian colors, and occasional warm harmonic openings suggesting resilience and new life among the ruins.

The mood is quietly mysterious, resourceful, gently uneasy, and determined: “This place is broken, but we can build something here.” Maintain enough rhythmic movement to support coordinated work, with subtle playful syncopation for the crew's awkward physical teamwork. Keep the melody sparse and the energy moderate so players can think, communicate, and concentrate on precise crane placement.

Approximately three minutes. Establish the atmosphere within eight seconds, then develop a seamless main loop with subtle variations every sixteen or thirty-two bars. Alternate slightly lighter exploration textures and a fuller working groove without dramatic drops, sudden danger cues, a climax, or a final cadence. Match the ending harmony and pulse to the loop beginning; no baked-in fade-out. Controlled bass, smooth treble, spacious midrange, and room for voice chat, machinery, water, and contamination warnings.

No vocals, choir, sirens, Geiger-counter clicks, alarm-like beeps, literal machinery or water samples, relentless battle drums, bleak funeral music, horror drones, aggressive industrial metal, or overly cheerful comedy music. Evoke an abandoned world with a believable possibility of escape.

## 근거와 적용 시 주의점

- `docs/planning/chapter-2-plan.md`: 폐허가 된 지구 발사장, 크레인·사다리·볼트·제독 중심 협동. 문서의 초안·미정 항목을 구현 완료로 간주하지 않는다.
- `METAGAME/Assets/Scenes/Stage2_Earth.unity`: 폐허 스카이라인, 크레인·로켓 모듈·가스 탱크, 식생과 배수로 배치 확인. 씬 화면을 직접 보거나 렌더링한 검토는 아니다.
- `DemoEnding.cs`: 탑승 8~14초(인원수에 따라 변동), 발사 5초, 사진 5초, 타이틀 3초. 불시착 도입은 항해 8초 + 피격 6초 + 강하 9초 = 23초.
- `DemoEndingView.Arrival.cs`: 8초 충돌음, 14초 경고음, 21.6초 착지 충격음. 음악에 같은 효과음을 구워 넣으면 중복·타이밍 어긋남이 생긴다.
- 생성 도구가 초 단위 큐나 무봉합 루프를 정확히 지킨다는 보장은 없다. 컷씬 음악은 편집으로 큐를 맞추고, 1장 탑승 길이는 연장 가능한 구간으로 처리한다. 실제 발사만 덮는다면 첫 프롬프트의 탑승 부분을 제외해 5초 상승 큐를 추출한다.
- 세 곡의 모티프를 실제로 통일하려면 같은 4음 멜로디를 작곡해 참조 음원으로 제공하거나 편곡한다. 텍스트만 동일하게 써서는 같은 선율이 보장되지 않는다.
- 기본 BGM의 곡 끝 페이드·5초 반복 대기는 컷씬 큐와 무봉합 루프 요구에 맞지 않는다. 연결 시 기존 BGM 정리, 음악/전체 음량 적용, 씬 전환 중 이중 재생 및 멀티플레이 공통 연출 시각을 검증한다.
- 이번 작업은 문서 추가만 수행했다. 게임 코드·씬·오디오 설정의 동작 변경은 없다.
