# 태블릿 첫 사용 안내 — 로컬 초안

## 기준과 범위

- 새 복사본: `S15P21B202-tablet-guide-20260914/METAGAME`.
- 최초 복제 기준: `be9cd5e176d89f4b73633c7a809ce95c7e6cefaf`. 작업 중 새 develop을 fast-forward로 반영한 최종 기준: `6713b7a41e291e6ad74fee3cdb43aa30ac5fdc0a` (509 머지 포함). 로컬 안내 수정과 들어온 커밋의 파일 겹침 없음.
- Jira: S15P21B202-520(UI), S15P21B202-521(Art). 아트 브랜치 `S15P21B202-521-tablet-guide-art`와 그 위의 UI 브랜치 `S15P21B202-520-tablet-first-guide`로 분리 제출한다. 아트 → UI 순서로 develop에 머지한다.
- 기존 프로젝트와 진행 중인 509 브랜치를 변경하지 않는다.
- 이번 범위: 통신 수신 안내 → 기존 태블릿 안의 그림 가이드 4장 → 기존 진행도 화면.
- 연습 로비, 장비 연습, 컷신/운석 발생, 물리·용접 규칙, 네트워크 메시지는 변경하지 않는다.

## 확인 순서

1. Unity Hub에서 이 폴더의 **METAGAME**을 연다. 기존에 열어 둔 다른 폴더에는 반영되지 않는다.
2. `Assets/Scenes/Stage1_Astra.unity`에서 Play하거나 기존 Lobby 진입 흐름으로 시작한다.
3. 매 새 게임 시작 후 플레이 가능한 상태에서 약 2초 뒤 왼쪽 가장자리의 통신 곡선 3개와 그 아래 TAB 표시가 나타난다. 네모 패널은 없다. 짧은 세 음 두 묶음(삐비빅, 삐비빅)을 9초 간격으로 최대 3번 재생한다.
4. TAB(설정에서 변경했다면 해당 키)을 누르면 조립 목표 → 고정못 → 몽둥이 → 용접 안내가 나타난다.
5. A/D 또는 기존 태블릿 이전/다음 키로 이동한다. 태블릿 화면의 화살표도 클릭 가능하다.
6. 어느 장에서든 E(기존 상호작용 키) 또는 **확인 · 진행도 보기**를 누른다. 태블릿은 닫지 않고 기존 파츠 상태 화면으로 돌아온다. 모든 장 읽기를 강제하지 않는다.
7. 태블릿의 **사용 가이드** 또는 E로 다시 볼 수 있다. 도중에 TAB으로 닫으면 다음에 이어서 볼 수 있다.

## 상태와 안전성

- 완료 여부는 이번 게임의 메모리 상태로만 유지한다. 이전 초안의 PlayerPrefs `NotThisPlanet.TabletGuide.v1.Completed`는 더 이상 읽거나 쓰지 않으며 삭제도 하지 않는다. 새 게임/ResetRun 시 안내를 다시 표시한다. HUD 재연결만으로는 완료 여부를 초기화하지 않는다.
- 안내 페이지는 `TabletGuideState`, 기존 진행도 페이지는 `Stage1Exploration.TabletPage`로 분리한다. 완료 시 기존 페이지 0으로 돌아간다.
- 기존 태블릿 입력 차단과 커서 처리를 재사용한다. 다른 플레이어와 세계 시간은 멈추지 않는다. 따라서 안내 중 무적/운석 유예 기능은 이번 작업에 없다.
- 완료 전에는 기존 하단 TAB 키 안내 대신 왼쪽 수신 알림을 사용한다. 완료 후 기존 하단 키 안내가 다시 표시된다.
- 사운드는 개인 2D 오디오이며 기존 SFX 및 마스터 볼륨을 따른다. 생성한 오디오 클립은 HUD 종료 때 정리한다.
- 씬/프리팹 수동 연결은 필요 없다. 기존 부트스트랩/HUD가 초기화하고 `Resources/TabletGuide/Page1~4`를 읽으므로 에디터 실행 순서에 의존하지 않는다. 화면 순서는 Page1 → Page4 → Page2 → Page3이다.
- 이 새 폴더에는 개인 Unity MCP 패키지를 복사하거나 설치하지 않았다.

## 아트

- built-in image_gen으로 생성한 PNG 4장. 추가 API/Tripo 유료 생성은 사용하지 않았다.
- 게임 스크린샷의 토끼 우주인·도구를 참고한 단색 선화. 정확한 파츠 도면이 아닌 행동 설명용 개념 그림이다. 정확한 파츠 형태/순서는 기존 진행도 화면을 기준으로 한다.
- 최종 파일: `METAGAME/Assets/_Project/UI/Resources/TabletGuide/Page1.png`, `Page2.png`, `Page3.png`, `Page4.png`.
- 프롬프트 구성은 아래 JSON에 기록한다. Page1은 최종 편집에서 가운데/오른쪽 우주선의 아래→위 순서를 엔진(노즐) → 납작한 부양 디스크 → 큰 캡슐 → 작은 상단 캡으로 수정했다. 나머지 구도와 스타일은 유지했다.

```json
{
  "base": "Use case: illustration-story. Create a game tutorial illustration strip, wide landscape 3:1. Reference image ONLY for character/prop identity, NOT screenshot style. Cute rabbit-eared round astronaut with big round visor and simple eyes, little scarf, stubby limbs. Monochrome pale cyan-white hand-drawn chalk-like LINE ART, crisp readable thick outlines with playful drawn gestures. Flat uniform dark digital tablet background #09161D, no chalkboard/paper texture, no colored fills, no realism, no UI frame, NO TEXT, no letters, no numbers. Simple very large subjects, generous margins. ",
  "page1": "Scattered four rounded spacecraft modules → two rabbit astronauts assembling a modular ship → the assembled ship launching. Cute cyan-white line art on dark digital tablet. No text.",
  "page2": "Rabbit astronaut holding the existing sci-fi bat → swinging it to bat a cratered meteor away. Same line style. No text.",
  "page3": "Unstable stacked modules and worried rabbit → rabbit welding the seam with a pistol-shaped welder → connected stable modules and proud rabbit. Same line style. No text."
}
```

## 검증

### MR 제출 검증

- 최신 develop `74f7116`으로 fast-forward 후 빨간 통신 표시를 포함한 최종본을 검증했다.
- Unity 6000.5.4f1 EditMode **87/87 통과**, 실패 0. 기존 53개 범위에 TeammateTossTests를 추가했다. `.local-validation/tablet-mr.xml`, `.local-validation/tablet-mr-retry.log`.
- 최초 배치 실행이 테스트 시작 전 종료돼 재실행했으며, 재실행에서 컴파일과 테스트가 완료됐다.
- 기존 Unity 설정/머티리얼의 줄바꿈 재기록은 제출에서 제외했다. 씬·프리팹·패키지·물리 동작 변경 없음.
- 플레이어 빌드 및 2인 접속 검증은 하지 않았다. 사용자 확인과 EditMode 렌더/상태 테스트를 해당 검증으로 대체했다고 주장하지 않는다.

### 9/14 그림 피드백 반영

- 후속 알림 수정: 이전 완료 PlayerPrefs로 안내가 사라지던 원인을 제거하고 매 새 게임 첫 TAB에서 그림을 표시한다. E는 첫 장을 포함한 모든 장에서 진행도 화면으로 복귀한다. 왼쪽 전파는 오른쪽 방향 원호 3개와 TAB만 표시하며 짧은 알림음 두 묶음과 함께 점등한다. 알림 타이머는 RunVersion 변경 시 초기화한다.
- 후속 검증: Unity 6000.5.4f1 EditMode **53/53 통과**, 실패 0 (`.local-validation/tablet-signal.xml`). 기존 완료 값 1이 있어도 HUD 초기화 후 그림이 열리는지, 첫 장에서 확인 가능한지, 같은 게임 재열기/새 게임 ResetRun이 분리되는지 검증했다. 실제 게임에서 가장자리 알림의 점멸과 소리 청취는 사용자 확인이 필요하다.
- 추가 보정: Page4 마지막 장면에서 고정못과 파츠를 잇던 줄을 제거해 확실히 분리된 모습으로 교체했다. 앞 두 장면의 연결 줄은 유지했다. built-in image_gen 편집 결과를 육안 확인하고 원래 PNG 경로에 반영했으며 meta/코드는 변경하지 않았다. 이 PNG 교체 후 Unity 재검사는 하지 않았다.
- 추가 보정 최종 프롬프트: Use case: precise-object-edit. Input is the exact edit target, a wide 3:1 game tablet tutorial illustration. Make ONE localized correction ONLY in the RIGHTMOST FINAL SCENE: the curved tether/rope currently joins the loose anchor pin on the ground to the hovering spaceship capsule. That connection must be BROKEN. Remove the entire long curved connecting rope between the pin and capsule and replace with matching dark teal background. Optionally leave very short loose cut stubs at each attachment, with a LARGE EMPTY GAP between them so the pin is completely disconnected from the hovering capsule. No intact line may connect them. Preserve the floating capsule, hover strokes, happy rabbit, loose pin, all positions and shapes, color palette, linework, background, composition, resolution and aspect ratio. Keep LEFT and MIDDLE scenes, their intact tethers, mouse icons, S key and both sequence arrows unchanged. Do not add scissors, tools, labels, text, new objects, or redesign anything. Only break/remove the rightmost final tether.
- 4장 버전 검증: Unity 6000.5.4f1 EditMode **52/52 통과**, 실패/스킵 0. 결과 `.local-validation/tablet-four-page.xml`, 로그 `.local-validation/tablet-four-page.log`. 첫 검사에서 폴백 UI의 타입 네임스페이스 참조 오류를 발견해 수정한 뒤 재검사했다.
- 실제 태블릿 프리팹의 카메라 렌더에서 새 고정못 2/4 페이지, 용접 4/4 페이지 및 확인 버튼을 확인했다. 캡처 `.local-validation/four-page-preview/`. 실제 플레이 입력 및 배포 빌드 검증은 별도다.
- 용접(Page3): 가운데 플레이어를 파츠에서 떨어뜨려 공중에 배치하고 총구 → 빈 공간 → 파츠 이음새로 이어지는 긴 레이저와 스파크를 표시했다. 양옆 장면과 기존 선화 스타일은 유지했다.
- 고정못(Page4): 손으로 잡기 → 클릭 유지 + S로 뒤로 당기기 → 고정못 해제 후 살짝 호버링하는 파츠와 기쁜 플레이어. 설명에는 **모두 뽑으면**이라고 명시해 못 하나만으로 해제된다는 오해를 줄였다.
- S는 기본 이동키를 설명하는 그림이다. 키를 재설정했다면 해당 뒤로 이동 키를 사용한다. 게임 입력·고정못·부유 물리는 수정하지 않았다.
- 페이지 수/기본 및 폴백 표시를 4장으로 확장했다. 이후 사용자 피드백으로 영구 완료 저장과 전체 페이지 확인 강제를 제거했다. 이미 확인한 같은 게임에서는 사용 가이드(E)로 다시 볼 수 있다.
- 생성 방식: built-in image_gen. 최종 프롬프트:
  - 용접: "Edit only the middle scene of the existing wide 3:1 cyan-white line-art tablet illustration. Separate the floating rabbit astronaut from the stacked modules, aim a welding pistol across a substantial empty gap, and draw a clear long straight cyan laser ending in a spark at the seam. Preserve left/right scenes, character identity, dark digital background, monochrome style, no text."
  - 고정못: "Create a matching wide 3:1 three-step cyan-white line-art strip on dark teal digital background: rabbit grips a tethered ground anchor pin; rabbit holds mouse left-click and leans/steps backwards straining, with a backward arrow and S key; pin is fully released and the module hovers slightly while the rabbit celebrates. No launch flames; only S text; preserve reference rabbit astronaut identity and readable simple silhouettes."

### 이전 3장 버전 검증 이력

- 대상: TabletGuideTests, Stage1ExplorationTests, Stage1TabletViewTests, TabletTowerTests, KeyGuideHolderTests.
- 결과 파일: 로컬 `.local-validation/tablet-guide.xml`, 로그 `.local-validation/tablet-guide.log`.
- 최초/가독성 보완 후 Unity 6000.5.4f1 EditMode **42/42 통과** (새 가이드 테스트 12개 포함).
- 최신 develop `6713b7a` 반영 후 AssemblyDockTests까지 포함하여 **50/50 통과**, 실패/스킵 0. 결과: `.local-validation/tablet-integrated.xml`, 로그: `.local-validation/tablet-integrated.log`.
- 태블릿 프리팹의 World Space Canvas를 실제 카메라에 렌더링해 3장 안내와 기존 진행도 복귀를 확인. 작은 글씨를 굵게 하고 확인 버튼 배경을 추가했다. 캡처: `.local-validation/final-preview/`. 이후 Page1의 그림 순서만 최종 편집했다(코드 변경 없음).
- PNG 3장과 모든 신규 스크립트에 Unity가 생성한 meta 포함. 그림은 비정사각 비율을 유지하고 가장자리 반복을 막도록 importer 설정. 씬·프리팹·패키지 설정에 기능상 변경 없음. 첫 임포트 중 일부 기존 설정 파일의 줄바꿈만 재기록됨(내용 diff 없음).
- 남은 검증: 실제 Stage1에서 알림 타이밍/볼륨, A/D/E 및 마우스 입력, UI 배율 80~150%, 종료 후 재실행 시 완료 저장, 2인 접속, 배포용 플레이어 빌드. 카메라 캡처와 EditMode 테스트를 실플레이/멀티플레이 검증으로 간주하지 않는다.
