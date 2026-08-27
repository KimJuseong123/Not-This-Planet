# S15P21B202-75 캐릭터 모델 통일 및 씬 단일화

- Epic: S15P21B202-14 [Puzzle] 퍼즐·상호작용 시스템
- 브랜치: `S15P21B202-75`
- 작성일: 2026-08-27

## 1. 목적 / 배경

캐릭터 모델이 `SF_Rabbit_Base` 와 `SF_Astronaut_Base` 두 종류로 섞여 있었다.
런타임은 이미 Rabbit 을 쓰고 있는데 검증 스크립트는 Astronaut 를 가리켜,
**실제로 쓰지 않는 파일을 검증하고 있었다.**

씬도 `Assets/Scenes` 와 `Assets/_Project/Scenes` 두 곳으로 나뉘어,
실행 진입점(SampleScene)과 레벨(Stage1)이 다른 폴더에 있었다.
어느 씬을 열어야 게임이 도는지 알 수 없었다.

## 2. 확정 사항

### 2.1 캐릭터 모델은 SF_Rabbit_Base 하나

`SF_Astronaut_Base.fbx` 를 제거했다. GUID 로 전체를 검색해 **참조하는 파일이 하나도 없음**을
확인한 뒤 지웠다. 런타임은 이미 Rabbit 을 쓰고 있었다
(`SpaceSuit.ResourceKey`, `ArtLink.RabbitKey`, `Resources/SF_Rabbit_Base.prefab`).

### 2.2 검증기 기준을 실제 모델에 맞췄다

`SpaceFlopModelValidator` 가 Astronaut 를 검사하고 있었다. Rabbit 으로 돌리자 실패했다.
두 모델의 구조가 달랐기 때문이다.

| 항목 | 예전 기준 (Astronaut) | 새 기준 (Rabbit 실측) |
| --- | --- | --- |
| 본 이름 | `Hips`, `Spine`, `LeftUpperArm` … (Humanoid) | `Hip`, `Spine01`, `L_Upperarm` … |
| 높이 | 1.25 ~ 1.60m | 0.95 ~ 1.25m (실측 1.084m) |
| SkinnedMeshRenderer | 45개 이상 | 1개 이상 (실측 1개) |
| 재질 슬롯 | 8개 이상 | 1개 이상 (실측 1개) |

Astronaut 는 블렌더에서 손으로 만들어 Humanoid 이름을 쓰고 여러 부위로 쪼개져 있었다.
Rabbit 은 Tripo 로 생성해 온 것이라 자체 본 이름을 쓰고 스킨 하나로 되어 있다.
비틀림 본(`*Twist*`)과 발가락은 물리 인형이 쓰지 않으므로 필수에서 뺐다.

검증기는 이제 PASS 한다. 임포트 스케일 사고와 재질 유실은 여전히 잡는다.

### 2.3 씬은 Assets/Scenes/Main.unity 하나

| 원본 씬 | 처리 |
| --- | --- |
| `Scenes/SampleScene.unity` | `SpaceBootstrap` 만 가져오고 카메라·조명·볼륨은 버림 |
| `_Project/Scenes/Stage1.unity` | 루트 `Stage1`(자식 4)을 그대로 합침. 자체 카메라·조명을 갖고 있다 |
| `_Project/Scenes/Surface.unity` | **합치지 않고 삭제.** `SpaceBootstrap` 이 SampleScene 과 완전히 중복이었다 |
| `_Project/Scenes/Lobby.unity` | `LobbyBootstrap` 을 **비활성 상태로** 합침 |
| `_Project/Scenes/Sandbox_Physics.unity` | **삭제.** 퍼즐 내용을 Main 에 직접 배치했다 (2.4) |

결과 `Main.unity` 의 루트 3개:

```
SpaceBootstrap   활성    (게임 진입점)
Stage1           활성    자식 4  (레벨 구성)
Lobby            비활성  (LobbyBootstrap)
```

SampleScene 의 카메라·조명·볼륨을 버린 이유는 `Stage1` 이 자체 카메라와 조명을
자식으로 갖고 있어서다. 그대로 두면 방향광이 두 번 들어간다.

빌드 씬 목록은 `Assets/Scenes/Main.unity` 하나만 가리킨다.

### 2.4 Sandbox 를 없애고 퍼즐을 Main 에 직접 배치했다

처음에는 Sandbox 를 `Dev/` 로 옮겨 검증 전용으로 남겼다. 그런데 그 상태로는
**Main 을 열어 Play 해도 퍼즐이 하나도 없었다.** 지금까지 만든 고정쇠·견인·소켓·용접·운석이
전부 Sandbox 씬에만 있었기 때문이다. Main 은 캐릭터와 레벨뿐이었다.

그래서 Sandbox 를 지우고 퍼즐 내용을 Main 에 직접 배치했다.
`_Project/Scenes` 폴더 자체가 없어졌고 씬은 `Assets/Scenes/Main.unity` 하나뿐이다.

배치는 기획 1장을 따른다. `Stage1/Gameplay/Puzzle` 아래에 있다.

| 오브젝트 | 내용 |
| --- | --- |
| `Part_1` ~ `Part_4` | 아레나 네 곳에 건물처럼 박힌 파츠. 각각 `Locked`, 고정쇠 4개, 용접점 4개, 소켓 2개 |
| `AssemblyPoint` | 조립 위치 (0, 0, 10) |
| `TractorBeam_1`, `_2` | 견인 빔. 도구 형태가 미정이라 기준점만 두고 위에서 아래를 겨눈다 |
| `MeteorSpawner` | 아레나보다 좁은 36x36m 영역에 운석을 떨어뜨린다 |
| `~시연 (입력 붙으면 삭제)` | 고정쇠를 시간차로 풀어 주는 시연 장치 |

물리 회귀 검증은 Sandbox 씬이 아니라 **EditMode 테스트 140건**이 담당한다.
테스트는 씬에 의존하지 않고 각자 리그를 코드로 만든다. Sandbox 를 지워도 검증은 남는다.

### 2.4.1 씬을 덮어쓰지 않는 배치 도구

`Stage1PuzzleBuilder` 는 열려 있는 씬의 `Stage1/Gameplay/Puzzle` 그룹만 지우고 다시 만든다.
씬 전체를 새로 만들지 않는다. 예전 `SandboxSceneBuilder` 와 `SpaceSceneBuilder` 는
`NewScene` 후 `SaveScene` 을 해서 대상 씬을 통째로 덮어썼고, 그 방식이면 레벨과
부트스트랩이 사라진다. 그래서 두 도구를 제거하고 이 도구로 대체했다.

### 2.4.2 시연 장치를 둔 이유

입력 바인딩(S15P21B202-13)이 없어 플레이어가 고정쇠를 누를 수 없다.
그대로 두면 Play 를 눌러도 퍼즐 루프가 시작되지 않는다.

`Gameplay/Demo` 아래에 세 컴포넌트를 두어 흐름을 눈으로 볼 수 있게 했다.
`DemoClampReleaser`(고정쇠 해제), `DemoBeamDriver`(빔 조작), `DemoMeteorDriver`(스포너 구동).
입력이 붙으면 씬에서 `~시연` 오브젝트와 드라이버를 지우면 된다.
게임 로직은 이것들에 의존하지 않는다.

### 2.5 씬을 덮어쓰던 도구 두 개를 제거했다

`SandboxSceneBuilder` 와 `SpaceSceneBuilder` 는 둘 다 `NewScene` 후 `SaveScene` 을 해서
대상 경로의 씬을 통째로 덮어썼다. `SpaceSceneBuilder` 는 **빌드 씬 목록까지 통째로 교체**했다.
Main 하나로 통일한 뒤에는 이런 도구가 메뉴 한 번에 레벨을 지울 수 있다.

둘 다 제거하고, 씬 전체를 건드리지 않는 `Stage1PuzzleBuilder` 로 대체했다(2.4.1).

`SpaceBuild` 는 `Main.unity` 를 빌드하도록 바꿨다. 빌드 씬 목록이 없을 때만 **더하고**
덮어쓰지 않는다. 손으로 구성한 씬을 도구가 지우는 일이 없어야 한다.
`SpaceSceneBuilder.EnsureScene()` 을 부르던 자리도 이 확인 코드로 바꿨다.

### 2.6 로비 전환은 Network 파트에 넘긴다

`LobbyBootstrap` 은 네트워크 세션과 로비 HUD 를 만드는 실제 기능이고
`SceneManager.LoadScene("Surface")` 로 게임 씬으로 넘어갔다. Surface 를 지우면 깨진다.

지우지 않고 이렇게 처리했다.

- 씬에는 오브젝트를 남기되 **비활성**으로 둔다. 직렬화된 설정(백엔드 URL, 닉네임)이 보존된다
- 대상 씬 이름을 직렬화 필드로 빼고 기본값을 `Main` 으로 했다
- 대상 씬이 이미 열려 있으면 **다시 로드하지 않고 경고만 남긴다.**
  같은 씬을 다시 로드하면 방금 만든 세션까지 초기화된다

로비와 게임이 한 씬에 있게 되었으므로 **전환 방식 자체를 다시 정해야 한다.**
씬을 다시 나눌지, 같은 씬 안에서 상태로 전환할지는 Network 파트의 결정이다.

## 3. 산출물

에셋
- `METAGAME/Assets/Scenes/Main.unity` (신규, 통합 씬)
- 삭제: `_Project/Scenes/` 폴더 전체 (Sandbox 포함)
- 삭제: `Scenes/SampleScene.unity`, `_Project/Scenes/{Stage1,Surface,Lobby}.unity`
- 삭제: `_Project/Models/Characters/SF_Astronaut_Base.fbx`

코드
- `Editor/Stage1PuzzleBuilder.cs` (신규, Main 씬 퍼즐 배치)
- 삭제: `Editor/SandboxSceneBuilder.cs`, `Editor/SpaceSceneBuilder.cs`
- `Runtime/Gameplay/Demo/DemoClampReleaser.cs` (신규)
- `Runtime/Gameplay/Demo/DemoBeamDriver.cs`, `DemoMeteorDriver.cs` (Physics 에서 이동·개칭)
- 삭제: `Runtime/Gameplay/Physics/SandboxLauncher.cs`
- `Editor/SpaceFlopModelValidator.cs` (검사 대상과 기준치)
- `Editor/ArtLink.cs` (주석)
- `Editor/SpaceBuild.cs` (빌드 대상 씬, 목록 덮어쓰기 제거)
- `Runtime/Bootstrap/LobbyBootstrap.cs` (전환 안전 처리)

설정
- `METAGAME/ProjectSettings/EditorBuildSettings.asset`

## 4. 검증

- 모델 검증기 **PASS** — `캐릭터 skins=1 materials=1 height=1.084`, `우주선 meshes=91 materials=10 height=4.568`
- EditMode 테스트 **140건 전부 통과.** 운석 작업(S15P21B202-70)을 이 브랜치로 가져왔다.
  Main 하나로 지금까지 만든 것을 모두 실행하려면 운석까지 필요했다
- `Main.unity` 루트 3개 확인 — `SpaceBootstrap`(활성), `Stage1`(활성), `Lobby`(비활성)
- **Main 씬 하나로 1장 루프 전체가 도는 것을 시뮬레이션으로 확인했다. 실패 0건.**

```
파츠 4기 · 빔 2기 · 운석 스포너 존재
Part_1~4 초기 상태 Locked / 필수 고정쇠 4개 / 용접점 4개 / kinematic
Part_1 고정쇠 해제 후 Free
빔1 이 Part_1 을 붙잡음 → Carried → 놓으면 Placed
Part_1 이 조립 위치 근처에 놓임          수평 거리 0.00m
Part_2 고정쇠 해제 후 Free → 빔2 가 붙잡음
빔2 가 스냅 후보를 잡음
Part_2 가 조립됨 / 정확히 한 층 위에 붙음   3.700 (기대 3.7)
미접합 breakForce                     8000N
용접 완료 후 Welded / 용접 후 breakForce   200000N
운석이 예고되고 떨어짐                    예고 8회 / 낙하 8회
용접된 접합은 운석에도 버팀
```

![Main 씬 초기 배치](S15P21B202-75-main-scene.png)

- 빌드 씬 목록에 `Assets/Scenes/Main.unity` 하나만 등록
- 남은 씬 파일 **1개** — `Assets/Scenes/Main.unity`. `_Project/Scenes` 폴더는 삭제됐다
- 코드·씬에 `Astronaut` 문자열은 변경 이력을 설명하는 주석 한 줄만 남았다

## 4.1 작업 중 발견한 것 — 파츠를 어떻게 놓으면 넘어지는가

배치 시연을 처음 돌렸을 때 1단 파츠가 90도로 쓰러져 다음 층이 스냅되지 않았다.
원인을 두 번 잘못 짚었고, 두 경우 모두 실패 방식이 달랐다.

| 시도 | 결과 | 원인 |
| --- | --- | --- |
| 빔을 높이(4m) 두고 놓기 | 낙하하며 쓰러짐 | 2m 남짓 떨어지면서 기울어졌다 |
| 놓을 높이로 운반하기 | 끌리며 쓰러짐 | 파츠 밑면이 지면에 닿아 마찰로 3.6m 밀려났다 |
| **높이 운반 후 제자리 하강** | **똑바로 놓임** | 지면과 닿지 않고 낙하도 하지 않는다 |

`DemoBeamDriver` 에 경유점 개념을 넣어 세 번째 방식으로 바꿨다.
**이것은 시연만의 문제가 아니다.** 실제 플레이에서도 견인 도구를 낮게 끌고 가면 파츠가
넘어지고, 높은 곳에서 놓으면 쓰러진다. 견인 도구 조작을 설계할 때 고려할 사항이다.

## 5. 미해결 / 후속

- **입력 바인딩이 붙으면 `~시연` 오브젝트와 Demo 드라이버를 지운다** — S15P21B202-13
- 파츠 배치 좌표와 조립 위치는 그레이박스 기준이다. 레벨 디자인에서 확정한다
- 견인 도구를 낮게 끌면 파츠가 넘어진다 (4.1). 도구 조작 설계 시 고려한다
- **로비 → 게임 전환 방식** — Network 파트 결정 필요 (2.6)
- `Stage1` 루트 이름을 그대로 두었다. 단일 씬이 되었으니 `Level` 같은 이름이 맞을 수 있으나
  Level 파트의 판단이 필요해 건드리지 않았다
- `Main.unity` 안의 레벨 재배치와 아트 교체 — Level / Art 파트
- 캐릭터 리깅과 애니메이션 — Art 파트
- `Tuning` 은 캐릭터 키를 1.33m 로 잡고 있고 모델 bounds 는 1.084m 다.
  `RagdollBuilder` 주석의 ModelScale 1.3585 와 관련된 값으로 보이나 이 티켓에서 확인하지 않았다.
  Character 파트에서 정합성을 볼 필요가 있다
- Unity 가 생성한 `METAGAME/.vsconfig` 는 이 브랜치에 포함하지 않았다. `.gitignore` 는 Infra 판단

## 6. 참고

- `ProjectSettings/EditorBuildSettings.asset` 은 전 팀 공유 파일이다. 머지 후 Unity Editor 재시작 필요
- 씬을 지우고 합치는 작업이라 되돌리기 어렵다. Level(Stage1)·Network(Lobby) 파트 확인 필요
- `SF_Astronaut_Base` 를 되살려야 하면 이 커밋을 되돌리면 된다. 참조가 없었으므로 삭제만 취소하면 복구된다
