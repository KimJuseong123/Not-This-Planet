# S15P21B202-278 캐릭터 걷기·달리기 애니메이션 적용

- Jira: https://ssafy.atlassian.net/browse/S15P21B202-278
- 브랜치: `S15P21B202-278-rabbit-walk-run`
- 기준: `develop`의 `da8c33e`
- 원본: 사용자가 Mixamo에서 내려받은 `Standard Walk.fbx`, `Running.fbx`

## 범위와 보존

기존 토끼의 모델, 색, 머리/몸 비율, 래그돌 및 상호작용을 유지하면서 지상 이동 모션을 연결한다.
Main 씬, TerrainData, 자연물 및 인공물 배치, 기존 캐릭터 FBX/프리팹은 수정하지 않는다.
원본 프로젝트 Unity를 열거나 씬을 다시 저장하지 않고, 별도 임시 프로젝트에서 임포트/변환/검증한다.

## 연결 방식

1. Source FBX 두 개를 Humanoid로 임포트한다. 원본 파일 바이트는 변경하지 않는다.
2. 기존 토끼의 독자 뼈대(Hip, L_Thigh 등)에 임시 Humanoid 아바타를 만들어 리타기팅한다.
3. 팔다리 14개 본의 회전만 30fps로 추출해 반복 `.anim` 클립을 생성한다. 루트 위치 곡선은 넣지 않는다.
4. 런타임에는 렌더러/물리가 없는 임시 뼈대에서 두 클립을 혼합한 뒤 `SpaceSuit.Sync()` 이후 표시용 본에 적용한다.
5. PlayerRig의 기존 물리 계산은 변경하지 않는다. NetPlayer는 서버의 표시용 이동 상태를 전달해 원격 캐릭터에도 같은 모션을 선택한다.

## 상태별 처리

| 상태 | 처리 |
| --- | --- |
| 지상 전진 | Standard Walk, 현재 이동 속도에 따라 재생 속도 조절 |
| Shift 전진 | Running으로 0.16초 동안 전환 |
| 정지 | 0.12초 동안 기존 물리 포즈로 복귀 |
| 후진 | 선택된 전진 클립을 역재생 |
| 순수 옆걸음 | 전용 파일이 없으므로 기존 절차적 옆걸음 유지 |
| 공중/부유/기절/래그돌/탑승/매달림 | 애니메이션을 즉시 중단하고 기존 물리 포즈 유지 |
| 한 손 잡기 | 해당 팔의 기존 물리 포즈 유지 |
| 도구 사용/두 팔 들기 | 두 팔의 기존 물리 포즈 유지 |

## 산출물

- `METAGAME/Assets/_Project/Animations/Characters/Rabbit/Source/Standard Walk.fbx`
- `METAGAME/Assets/_Project/Animations/Characters/Rabbit/Source/Running.fbx`
- `METAGAME/Assets/_Project/Animations/Characters/Rabbit/Resources/RabbitLocomotion/Rabbit_Walk.anim`
- `METAGAME/Assets/_Project/Animations/Characters/Rabbit/Resources/RabbitLocomotion/Rabbit_Run.anim`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/RabbitLocomotion.cs`
- `METAGAME/Assets/_Project/Scripts/Editor/RabbitLocomotionAssets.cs`
- `METAGAME/Assets/_Project/Scripts/Editor/RabbitLocomotionPreview.cs`
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/RabbitLocomotionTests.cs`
- 기존 연결 코드: `SpaceSuit.cs`, `NetPlayer.cs`
- 카탈로그: `docs/asset-catalog.md` 캐릭터 절

## Unity 확인 방법

1. 임포트 및 컴파일 완료 후 **Tools → Metagame → 캐릭터 → 걷기·달리기 미리보기**를 연다.
2. 걷기/달리기 탭을 바꿔 두 모션의 차이를 확인한다. 반복 재생을 끄고 시간 슬라이더로 자세를 확인할 수도 있다.
3. 게임에서는 W로 걷고 Shift+W로 달린다. 1인칭에서는 원래 자기 몸을 숨기므로 모션은 미리보기 창, Play 중 Scene 뷰, 또는 다른 플레이어 시점으로 확인한다.
4. 정지/점프/기절/장비 잡기/탑승 시 기존 포즈와 조작이 유지되는지 확인한다.
5. 두 클라이언트로 접속해 서버의 걷기·달리기 상태가 상대방 화면에도 보이는지 확인한다.

## 재생성

일반 사용에는 재생성이 필요 없다. 원본을 다시 변환할 때만
**Tools → Metagame → 캐릭터 → 걷기·달리기 FBX 가져오기**에서 걷기 FBX, 달리기 FBX 순으로 선택한다.
다른 내용의 FBX가 기존 Source 경로에 있으면 무조건 덮어쓰지 않고 중단한다.
변환은 PreviewScene에서 수행하며 실제 게임 씬을 열거나 저장하지 않는다.

## 검증

- Unity 6000.5.4f1의 분리된 검증 프로젝트에서 원본 두 FBX 및 기존 토끼의 Humanoid 아바타가 유효함을 확인.
- 기존 토끼 본으로 변환: 걷기 1.166667초, 달리기 0.7초, 각 14본/56 회전 곡선.
- 동일한 런타임 소스와 Mirror를 포함한 분리 프로젝트 컴파일 통과.
- EditMode `RabbitLocomotionTests`: 14개 통과 / 실패 0개. 모션 선택/전환, 반복 클립, 정지/공중/차단 상태, 후진/옆걸음, 잡는 팔 보존, Rigidbody 위치·회전·속도 보존 검사.
- 임시 환경에서 걷기·달리기 각 3개 시점의 변형 메시를 렌더링해 서로 다른 보행 자세와 기존 재질을 확인.
- 제공 FBX와 프로젝트 Source FBX의 SHA-256 일치 확인.
- Main 씬 SHA-256 작업 전/후 동일: `CA73F404C4EDC309DCC44B8ABE9B74866D75BBED6B87465E67CBFA988D8241CC`.
- 실사용 프로젝트의 Play/멀티플레이 세션 검증은 사용자의 씬을 보호하기 위해 별도로 남긴다.

## 한계

받은 파일은 전진 걷기·달리기 두 개뿐이다. 전용 대기·옆걸음·후진·점프·기절 애니메이션을 새로 만들지는 않았다.
원본의 골반 이동/상체 전체 동작을 그대로 재생하는 방식이 아니라, 기존 물리 캐릭터를 유지하며 팔다리 모션을 적용한다.
완전한 애니메이션 기반 캐릭터 컨트롤러로 교체하는 작업은 포함하지 않는다.
