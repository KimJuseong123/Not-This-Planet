# METAGAME develop 코드 전체 AI 리뷰 — 2026-08-27

> **무엇** — `develop` 전체 코드에 AI 리뷰를 한 번 돌린 결과입니다(2026-08-27). MR 단위 리뷰가 놓치는 전체 구조·반복 패턴을 보려고 [S15P21B202-82]의 리뷰 도구로 수동 실행했습니다.
> **성격** — AI 가 낸 의견이므로 **차단 조건이 아닙니다.** 사람이 확인한 항목은 §0 에 따로 적혀 있습니다. [Infra]·[Network] 지적은 2026-08-28 에 처리했고(-134·-135·-136), 다른 파트 지적은 **그 코드를 다시 만질 때 정리**하는 것으로 두었습니다.
> **원본 공유본** — 같은 내용의 HTML·PDF 를 팀에 따로 공유했습니다. 생성물이라 리포에는 이 md 만 둡니다.

> 대상 `origin/develop` `fd51282` · 우리 코드 111개 파일(616k자) · 외부 플러그인/Packages 제외  
> 방식: `scripts/ci/ai_review.py` v2 의 코드 리뷰어를 모듈 단위 9청크로, 구조/에셋 리뷰어 1회, 요약 1회 · 모델 `claude-opus-5` (effort medium) · 비차단 참고용  
> 이 문서는 로컬 공유용이며 Jira/MM/GitLab 에 자동으로 게시되지 않았습니다.

## 0. Infra 검증 메모 (AI 결과를 사람이 확인한 것 — 2026-08-27 23:10)

AI 리뷰는 참고용이라, 공유 전에 "지금 고칠 것" 상위 항목을 develop 소스와 CI 기록으로 직접 대조했다.

| # | AI 지적 | 검증 | 근거 |
| --- | --- | --- | --- |
| 1 | 백엔드 시크릿 평문 커밋 | **확인됨 — 즉시 조치** | `backend/src/main/resources/application.yml` 에 DB `password`(12자)·JWT `secret`(56자)이 리터럴로 존재. 값은 이 문서에 적지 않음. 조치: 환경변수 참조로 교체 + **키·비밀번호 회전**(이미 리포 히스토리에 남았으므로 교체만으로는 부족) — Backend(최재현) |
| 2 | CI 실패 알림이 반대로 동작(`when: on_failure` 무시) | **오탐** | 오늘 실패 파이프라인 175386(mr-lint 실패)에서 `mm-ci-failed` 가 실제로 실행(success)되어 🔴 카드 발송, 성공 파이프라인에서는 skipped. GitLab 은 `rules` 에 `when` 이 없으면 잡 수준 `when` 을 기본값으로 쓴다 |
| 3 | 죽어도 계속 움직이는 플레이어 | **확인됨** | `PlayerRig.cs:247` 이 매 틱 `Ragdolled = down && !Hanging` 으로 덮어써 `ApplyDamage`(130)의 `Ragdolled = true` 가 사라짐. `IsDead` 는 125(피해 무시)에서만 참조되고 이동·점프·잡기 경로에는 없음 — Character/Physics |
| 4 | 재조립 시 `AddComponent<PartAssemblyLink>` 가 null | **정황 확인** | `PartAssembler.cs:148` `existing.Release()` 직후 163 에서 `AddComponent<PartAssemblyLink>` — `PartAssemblyLink` 는 `[DisallowMultipleComponent]`. `Release()` 가 `Destroy`(지연)면 재현됨 → `Release` 구현 확인 후 판단 — Puzzle |
| 5 | 상호작용 대상 영구 잠김 | **확인됨** | `Interactor.cs`·`TractorBeam.cs` 모두 `OnDisable`/`OnDestroy` 없음(Cancel/Release 메서드는 있음). 홀드 중 비활성화되면 해제 경로 없음 — Puzzle/Character |
| 6 | 재연결 시 CTS 재사용 예외 | **정황 확인** | `NetworkSession.cs` 43 `socketCancellation` 단일 필드, 184 재생성, `Send`(290)/`CloseSocket`(435) 경로에서 Dispose 후 참조 가능성 — 코드 경로 직접 확인 필요 — Network |
| 7 | 에디터 도구(씬 교체 확인 없음, Editor 폴더 이원화) | 미검증 | 에디터 담당이 확인 |

읽는 법: §2 의 순서는 AI 가 매긴 우선순위이고, 위 표의 "검증" 열이 사람이 본 결과다. **오탐(#2)은 조치하지 말 것.** §5 는 **파트별로 나누고 각 파트 안에서 위험도 순**으로 정렬해 두었으니 각 파트는 자기 표만 보면 된다(청크별 원본 응답은 §8).

## 1. 전반 상태

전체 10개 청크 모두 warn으로, 빌드가 막히는 수준은 아니지만 런타임에서 조용히 진행 불가(스턱)나 예외를 만드는 high 이슈가 9건 남아 있습니다. 특히 보안(시크릿 평문), CI 알림 오작동, 플레이어 사망/조립/상호작용 상태 관리가 즉시 손봐야 할 영역이고, 도구·스크립트 쪽은 실패 처리와 멱등성이 약합니다. 반면 테스트·셀프테스트·CI 린트 자동화 자체는 촘촘하게 깔려 있어 기반은 좋습니다.

## 2. 지금 고칠 것 (우선순위)

1. 백엔드 시크릿 평문 커밋 — DB 비밀번호와 JWT 서명 키가 리포에 그대로 있어 유출 시 토큰 위조 가능. backend/src/main/resources/application.yml 을 ${DB_PASSWORD}/${JWT_SECRET} 환경변수로 바꾸고 기본값 없이 부팅 실패하게. (오늘 중 회전 포함)
2. CI 실패 알림이 반대로 동작 — rules 가 있으면 최상위 when: on_failure 가 무시돼 성공 시 알림이 가고 실패는 조용히 지나감. .gitlab-ci.yml mm-ci-failed 잡의 rules 항목 안으로 when: on_failure 이동. 지금 팀은 실패를 못 보고 있는 상태.
3. 죽어도 계속 움직이는 플레이어 — PlayerRig.UpdateSpeed 가 매 물리틱 Ragdolled 를 덮어써 ApplyDamage 의 사망 상태가 사라지고, IsDead 를 보는 곳이 없어 이동·점프·잡기가 그대로 됨. PlayerRig.cs 의 UpdateSpeed/Locomotion/DriveArms 에 IsDead 반영.
4. 재조립 시 NRE로 조립 불가 — PartAssembler.Assemble 에서 Release()의 Destroy 가 지연되는데 같은 오브젝트에 [DisallowMultipleComponent] 컴포넌트를 다시 AddComponent 해 null. Parts/PartAssembler.cs — 기존 링크 재사용 또는 null 검사.
5. 상호작용 대상이 영구 잠김 — Interactor 에 OnDisable/OnDestroy 가 없어 홀드 중 플레이어가 사라지면 Release 가 안 불려 고정쇠·용접점이 Occupied 로 고착(퍼즐 진행 불가). Interaction/Interactor.cs 에 OnDisable=>Cancel, TractorBeam.cs 도 OnDisable=>Release 동시 처리.
6. 재연결 시 네트워크 예외 — NetworkSession.Send 가 CloseSocket 이 Dispose/null 한 CTS 를 그대로 써 NullReference/ObjectDisposed 발생, 연결 경합으로 옛 소켓이 새 소켓을 덮어쓰는 문제도 동반. Network/NetworkSession.cs Send/CloseSocket/ConnectSocketAsync.
7. 에디터 도구 두 건 — (a) StageSceneSetup/Stage1PuzzleBuilder 가 확인 없이 씬을 교체해 미저장 작업이 날아감(SaveCurrentModifiedScenesIfUserWantsTo 추가), (b) _Project/Editor 와 Scripts/Editor 로 에디터 코드가 이원화돼 asmdef 참조가 깨짐 — 파일을 Scripts/Editor 로 이동.

## 3. 반복되는 패턴

- 수명 종료 정리 누락이 전방위: OnDisable/OnDestroy 에서 해제·복원을 안 해 상태가 고착되거나 누수됨(Interactor, TractorBeam, Starfield 메시, GameHud 텍스처, DamageSource 딕셔너리, PartSocket.All).
- static Instance/전역 상태를 설정만 하고 되돌리지 않음 — GameSession, Goal/PowerSocket/SlidingDoor, SpaceBootstrap 의 Physics.gravity·fixedDeltaTime, LevelBuilder 의 Random.InitState. 씬 재로드·재입장 시 어긋남.
- 문자열·메시지 문구에 로직을 의존 — LobbyHud 의 한글 에러 문자열 비교, repo_hygiene 의 fail 메시지 접두사 판정. 문구 한 줄 바꾸면 조용히 오작동.
- 외부 호출 실패를 '없음'으로 오해석 — mr_lint 의 Jira 예외→이슈 부재 처리, mr_title 은 예외 미처리, repo_hygiene 의 ls-tree check=False. 인프라 장애가 코드 문제로 둔갑.
- 좋은 점: 셀프테스트(SpaceSelfTest), 에디터 테스트, CI 린트·모델 validate 등 자동 검증 레이어가 이미 갖춰져 있음 — 위 이슈들의 회귀 테스트를 붙일 자리가 이미 있다는 뜻. 다만 튜닝 임계값 매직 넘버가 런타임·테스트에 흩어져 있어 값 변경이 조용히 어긋남.

## 4. 미뤄도 되는 것

- 에셋 명명·중복 정리: Environment/Stage1 의 snake_case 리네임, PF_/SF_ 접두어 통일, 중복 Capsule 모델·프리팹 정본화(Unity 내 리네임으로 GUID 유지).
- Resources 폴더 의존 제거 — 프리팹/셰이더를 Addressables 또는 직접 참조로 전환(빌드 크기·로딩 경로 문제, 당장 동작은 함).
- 머티리얼/렌더 품질 이슈: Goal 의 sharedMaterial 전역 오염, Prim.Ghost/Flat 캐시 미적용, GhostSurface.shader 의 Cull Off + ZWrite Off 이중 블렌딩.
- Blender 툴 스크립트 정비 — generate_asset_previews 의 fbx/mat 매핑 덮어쓰기, loop_triangles 집계, manifest 부분 기록, validate 임계값 상수화.
- 러너/알림 스크립트 엣지 케이스 — runner-claude-env.ps1 의 다중 [[runners]] 처리, mm_notify 의 파이프라인 개수 기반 멱등 판정, setup-windows-runner 의 asset null 검사, 매직 넘버·하드코딩(HUD '/ 6', PlayerRig 임계값) 정리.

## 5. 파트별 발견 사항 — 높음 9 · 중간 27 · 낮음 25 (총 61)

파트별로 나누고, 각 파트 안에서 위험도 높은 순으로 정렬했다. 자기 파트 표만 보면 된다. 위험도는 AI 판정이며 §0 검증 메모의 확인 결과가 우선한다.

아래 표의 파트 순서는 '높음 건수'가 많은 순일 뿐이다. **파트를 넘는 처리 순서는 §2**를 따른다 — 건수가 1건인 [Backend] 시크릿이 전체 1순위다.

| 파트 | 담당 | 🔴 높음 | 🟠 중간 | 🟡 낮음 | 합계 |
| --- | --- | --- | --- | --- | --- |
| [Level] | 팀장(임시) | 2 | 6 | 2 | 10 |
| [Infra] | 금준영 | 2 | 5 | 4 | 11 |
| [Character] | 팀장(임시) | 2 | 3 | 4 | 9 |
| [Puzzle] | 팀장(임시) | 1 | 2 | 2 | 5 |
| [Network] | 금준영 | 1 | 1 | · | 2 |
| [Backend] | 최재현 | 1 | · | · | 1 |
| [Art] | 팀장(임시) | · | 5 | 7 | 12 |
| [QA] | 팀장(임시) | · | 2 | 3 | 5 |
| [UI] | 팀장(임시) | · | 2 | 3 | 5 |
| [Physics] | 팀장(임시) | · | 1 | · | 1 |

### [Level] — 담당 팀장(임시) · 🔴 높음 2 · 🟠 중간 6 · 🟡 낮음 2

_범위: 스테이지 월드 오브젝트(체크포인트·목표·진공·별)·레벨 빌더·GameSession·스테이지 에디터 도구_

| 위험도 | 위치 | 문제 | 조치 제안 |
| --- | --- | --- | --- |
| 🔴 높음 | `METAGAME/Assets/_Project/Editor/StageSceneSetup.cs:EnsureStage1Exists` | EditorSceneManager.NewScene(..., NewSceneMode.Single) 를 저장 여부 확인 없이 호출해 현재 열려 있던 미저장 씬 작업이 그대로 날아간다. | 앞에 EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() 를 호출하고 false 면 중단 |
| 🔴 높음 | `METAGAME/Assets/_Project/Editor/StageSceneSetup.cs` | Editor 스크립트가 Scripts/Editor(Metagame.Editor asmdef)와 별개인 _Project/Editor/ 에도 있어 이 파일만 Assembly-CSharp-Editor에 속한다. asmdef 어셈블리(Metagame.Gameplay 등)를 참조할 수 없어 빌드/참조 구조가 깨진다. | StageSceneSetup.cs 를 Assets/_Project/Scripts/Editor/ 로 이동하고 빈 _Project/Editor 폴더 정리 |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Editor/Stage1PuzzleBuilder.cs:Build 첫 줄` | OpenScene(Single) 로 현재 씬을 확인 없이 교체한다. 편집 중이던 변경분이 손실될 수 있다. | SaveCurrentModifiedScenesIfUserWantsTo() 결과가 false 면 return |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/GameSession.cs:Awake / Instance` | static Instance 를 Awake 에서 무조건 덮어쓰고 OnDestroy 에서 비우지 않는다. 씬 재로드·재입장 시 파괴된 인스턴스를 참조하거나 두 번째 인스턴스가 첫 번째를 밀어내 진행 상태가 어긋난다. | Awake 에서 Instance!=null&&Instance!=this 면 Destroy(this), OnDestroy 에서 if (Instance==this) Instance=null |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/Checkpoint.cs:OnTriggerEnter` | 트리거에 닿은 PlayerRig가 로컬 플레이어인지 확인하지 않고 GameSession.Instance.ReachCheckpoint를 호출한다. 원격/다른 플레이어의 콜라이더가 로컬 세션의 부활 지점을 옮길 수 있다. | `if (rig == null \ |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/Vacuum.cs:EnsureLights / OnEnable` | [ExecuteAlways] + 비직렬화 필드 sun/fill 조합이라 에디터 도메인 리로드나 씬 재열기 때마다 sun==null 로 판단해 "해"·"행성 반사광" GameObject를 새로 만든다. 라이트가 씬에 누적된다. | 자식에서 기존 라이트를 먼저 찾거나(transform.Find/GetComponentsInChildren) [SerializeField] 로 참조를 보존 |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/Goal.cs:Update / Create` | ringMat 이 sharedMaterial 이므로 Update의 SetColor 가 같은 Prim.Ghost 머티리얼을 쓰는 다른 오브젝트(착륙등 등)까지 전역으로 물들인다. 또 재생 중 매 프레임 공유 애셋을 수정한다. | ring.GetComponent<Renderer>().material 로 인스턴스 머티리얼을 받아 캐시해 쓰고 OnDestroy 에서 Destroy(ringMat) |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/Goal.cs:Instance (PowerSocket.Instance, SlidingDoor.Instance 동일)` | static Instance 를 Create 에서만 설정하고 OnDestroy 에서 비우지 않는다. 레벨 재빌드·재입장 시 파괴된 옛 인스턴스가 잠시 남아 HUD/셀프테스트가 잘못된 대상을 참조할 수 있다. | 각 클래스에 void OnDestroy() { if (Instance == this) Instance = null; } 추가 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/Starfield.cs:BuildStars / LateUpdate` | 런타임 생성 Mesh 를 파괴 시 해제하지 않아 씬 재생성마다 누수되고, follow 는 한 번 캐시한 뒤 갱신하지 않아 Camera.main 이 교체되면 배경이 옛 카메라를 따라간다. | OnDestroy 에서 Destroy(mesh), LateUpdate 에서 follow==null 또는 비활성일 때 재탐색 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/LevelBuilder.cs:Build()` | Random.InitState(20260825) 로 전역 UnityEngine.Random 상태를 덮어써 같은 프레임의 다른 시스템(파티클·Puff 등) 난수 재현성에 영향을 준다. | var st = Random.state; ... Random.state = st; 로 복원하거나 로컬 System.Random 사용 |

### [Infra] — 담당 금준영 · 🔴 높음 2 · 🟠 중간 5 · 🟡 낮음 4

_범위: CI 스크립트·러너·빌드 설정·부트스트랩_

| 위험도 | 위치 | 문제 | 조치 제안 |
| --- | --- | --- | --- |
| 🔴 높음 | `.gitlab-ci.yml:mm-ci-failed 잡` | rules 가 있으면 잡 최상위 when: on_failure 는 무시되고 rules 항목의 기본 when(on_success)이 적용된다. 실패 알림이 오히려 성공 시 발송되고 실패 시엔 안 나간다. | rules 항목 안으로 옮기기: `- if: $CI_PIPELINE_SOURCE == "merge_request_event" && $MM_WEBHOOK_URL\n  when: on_failure` |
| 🔴 높음 | `scripts/ci/mr_lint.py:Jira 실존·상태 블록` | jira.get_issue 가 RuntimeError 로 실패하면 warn 후 issue=None 이 되는데, 바로 다음 `if jira.available and issue is None` 에서 "Jira 이슈 {key} 가 존재하지 않음" 으로 fail 한다. 네트워크·토큰 오류가 이슈 부재로 둔갑해 파이프라인이 빨간불이 된다. | 조회 오류 플래그를 두고 `if lookup_failed: pass` 로 분기 건너뛰기(예: except 에서 issue=SENTINEL 후 존재 여부 검사 생략) |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Editor/SpaceBuild.cs:EnsureAlwaysIncludedShaders` | AssetDatabase.LoadAllAssetsAtPath 는 Assets/ 하위 자산용이라 "ProjectSettings/GraphicsSettings.asset" 로는 대개 빈 배열을 반환해 조용히 return 된다 — 셰이더 포함 보장이 실제로는 동작하지 않을 수 있다. | GraphicsSettings.GetGraphicsSettings() 를 SerializedObject 로 감싸 수정 |
| 🟠 중간 | `scripts/ci/mm_digest.py:17` | LEAD_MM 이 모듈 임포트 시점의 `next(...)`라 team.json 의 roles.lead 가 members 의 gitlab 값과 어긋나면 StopIteration 으로 스크립트가 즉시 죽는다(알림 전체 유실). | `next((...), None)` + 없으면 경고 로그 후 멘션 생략으로 폴백 |
| 🟠 중간 | `scripts/ci/mr_title.py:issue = jira.get_issue(key)` | mr_lint 와 달리 예외를 감싸지 않아 Jira 일시 장애 시 스크립트가 트레이스백으로 죽고 잡이 실패한다. 이 잡은 보정용이라 실패할 이유가 없다. | try/except RuntimeError 로 감싸고 메시지 출력 후 return 0 |
| 🟠 중간 | `scripts/runner/runner-claude-env.ps1:foreach ($l in $lines) 루프` | $inRunner 가 한 번 true 되면 꺼지지 않고 $done 은 전역 1회라, [[runners]] 가 둘 이상일 때 두 번째 이후 러너의 기존 environment 줄이 continue 로 삭제되고 대체 줄도 안 들어간다(설정 손실). | [[runners]] 를 만날 때마다 $done=$false 로 리셋하고 각 섹션마다 삽입/교체 |
| 🟠 중간 | `scripts/ci/repo_hygiene.py:if asset_added and not any(f.startswith("Assets/") for f in rep.fails)` | 에셋 위치 통과 판정을 fail 메시지 문자열 접두사로 하고 있어, 메시지 문구만 바꿔도 조용히 잘못된 ok 가 찍힌다. | loc_bad 카운터 변수를 두고 `if asset_added and not loc_bad: rep.ok(...)` |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/Bootstrap/LobbyBootstrap.cs:Awake` | gameObject.AddComponent<LobbyHud>() 를 무조건 추가하므로, 같은 오브젝트가 재활성/재초기화되거나 컴포넌트가 두 번 붙으면 HUD 가 중복 생성된다. NetworkSession 생성도 같은 프레임에 두 Bootstrap 이 있으면 경합한다. | GetComponent<LobbyHud>() ?? AddComponent<LobbyHud>() 로 보장하고 NetworkSession 생성은 싱글턴 내부 static 팩토리로 일원화 |
| 🟡 낮음 | `scripts/ci/mm_notify.py:mr_opened` | `len(pls) != 1` 로 첫 파이프라인을 판별하므로, 파이프라인이 재시도/중복 트리거로 2개가 되면 리뷰 요청 알림이 영구히 발송되지 않는다. | MARKER 노트 등 멱등 표식으로 '이미 보냄'을 판정하거나 가장 오래된 파이프라인 id 와 CI_PIPELINE_ID 비교 |
| 🟡 낮음 | `scripts/ci/repo_hygiene.py:base_top = set(git("ls-tree", ... check=False).split())` | check=False 라 base 에 Assets 트리가 없거나 명령이 실패하면 base_top 이 빈 집합이 되어 모든 최상위 폴더가 '새 폴더'로 fail 한다. 또 .split() 은 공백 포함 폴더명을 쪼갠다. | splitlines() 사용 + ls-tree 실패 시 검사 3번을 skip/warn 처리 |
| 🟡 낮음 | `scripts/runner/setup-windows-runner.ps1:2b) pwsh 설치 블록` | runner-use-pwsh.ps1 과 달리 $asset null 검사가 없어 GitHub API 응답에 MSI 자산이 없으면 $asset.name 접근에서 모호한 오류가 난다. | if (-not $asset) { throw "win-x64 MSI 를 못 찾음 — 수동 설치" } 추가 |

### [Character] — 담당 팀장(임시) · 🔴 높음 2 · 🟠 중간 3 · 🟡 낮음 4

_범위: 플레이어 리그·카메라·상호작용·피해 처리_

| 위험도 | 위치 | 문제 | 조치 제안 |
| --- | --- | --- | --- |
| 🔴 높음 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/Interactor.cs:클래스 전체` | OnDisable/OnDestroy 가 없어 홀드 중에 플레이어가 파괴·비활성되면 Held.Release(this) 가 호출되지 않아 대상이 영구히 Occupied 로 남는다(고정쇠·용접점이 아무도 쓸 수 없게 됨). | void OnDisable() => Cancel(); 추가 |
| 🔴 높음 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/PlayerRig.cs:ApplyDamage / UpdateSpeed / Locomotion` | ApplyDamage가 사망 시 Ragdolled=true 로 두지만 UpdateSpeed가 매 물리틱마다 Ragdolled = down && !Hanging 으로 덮어써서 사망 상태가 사라진다. IsDead를 보는 곳이 없어 죽은 뒤에도 이동·점프·잡기가 그대로 동작한다. | UpdateSpeed에서 `Ragdolled = IsDead \ |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/Interactor.cs:Complete()` | 완료 후 Focus 를 그대로 두고 State 만 Idle 로 만들어, 같은 대상이 계속 최적 후보이면 SetFocus 의 ReferenceEquals 로 조기 반환되어 State 가 Focused 로 복귀하지 못한다(콜라이더를 끄지 않는 상호작용에서 UI 상태 불일치). | Complete 끝에서 SetFocus(null) 또는 Focus 재평가를 강제 |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/Doll.cs:SetSleepAllowed` | if(allowed) WakeUp(); else WakeUp(); 로 두 분기가 동일해 파라미터가 무의미하다. 잠들기 허용/금지 의도가 전혀 구현되지 않았다. | else 분기를 Sleep() 로 바꾸거나 메서드를 제거 |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/PlayerRig.cs:RestoreCarriedBodyIfUnused / RestoreAllCarriedBodies` | 운반 중 물체가 파괴되면 body == null 로 조기 return 되어 carriedBodies 항목이 남고, 파괴된 Rigidbody 키가 계속 쌓인다(복원도 안 됨). isKinematic/useGravity를 바꾼 상태로 남는 물체도 생길 수 있다. | 파괴 감지 시 `carriedBodies.Remove(body)` 로 항목을 정리하고, 주기적으로 null 키를 걸러내는 정리 루틴 추가 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/DamageSource.cs:nextDamageTime` | PlayerRig 를 키로 쓰는 딕셔너리를 비우는 경로가 없어 파괴된(리스폰/디스코넥트) 리그 항목이 계속 남는다. 위험 오브젝트가 장수명일 때 누수. | 쿨다운 만료 항목 주기적 제거 또는 PlayerRig 쪽에 마지막 피격 시각을 저장 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/OrbitCamera.cs:LateUpdate (SphereCastNonAlloc)` | 카메라 충돌 검사가 레이어 마스크 ~0 으로 모든 것을 때리고, 히트마다 GetComponentInParent<PlayerRig>() 를 호출해 자기 몸을 걸러낸다. 매 프레임 계층 탐색 비용이며 다른 플레이어 몸에도 카메라가 밀린다. | RagdollBuilder.WorldMask(또는 지형 전용 마스크)를 넘겨 쿼리 단계에서 인형 레이어를 제외 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/PlayerRig.cs:TryGrabTool / ResetToolState` | toolBodyHadGravity 와 BeginCarriedBody 의 CarriedBodyState.HadGravity 가 같은 값을 이중 관리하고, ResetToolState가 복원 뒤 다시 useGravity를 덮어쓴다. 한쪽만 바뀌면 중력 상태가 어긋난다. | toolBodyHadGravity 필드를 제거하고 CarriedBodyState 복원 경로 하나만 사용 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/PlayerRig.cs:ProbeGround / Hover / DriveLegs 등` | 0.22f, 1.6f, 7f, 0.30f, 0.16f, 240f 같은 임계값이 코드에 직접 박혀 있어 Tuning 값과 튜닝 지점이 갈린다. | 접지 여유·점프 락·먼지 임계 등을 Tuning 상수로 이동 |

### [Puzzle] — 담당 팀장(임시) · 🔴 높음 1 · 🟠 중간 2 · 🟡 낮음 2

_범위: 부품 조립·집게·운석 해저드_

| 위험도 | 위치 | 문제 | 조치 제안 |
| --- | --- | --- | --- |
| 🔴 높음 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartAssembler.cs:Assemble: existing.Release() ~ link.Bind` | 재조립 경로에서 기존 PartAssemblyLink.Release() 가 Destroy(this) 를 하지만 플레이 모드에서는 파괴가 지연된다. 직후 같은 오브젝트에 AddComponent<PartAssemblyLink>() 를 호출하면 [DisallowMultipleComponent] 로 null 이 반환되어 link.Bind 에서 NRE. | 기존 링크를 재사용해 Bind 로 갱신하거나, Release 후 컴포넌트 파괴 대신 상태만 초기화하고 AddComponent 결과 null 검사 추가 |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Hazards/MeteorSpawner.cs:AdvanceSchedule / IntervalAt` | _intervalMin(또는 _rampSeconds<=0 시 반환값)이 0 이면 CurrentInterval 이 0 이 되어 매 물리 스텝마다 예고·스폰이 생성되고 운석이 무제한 누적된다. [Min(0f)] 이라 0 입력이 허용된다. | IntervalAt 반환값을 Mathf.Max(minInterval, 0.05f) 같은 하한으로 클램프하고 while 대신 1스텝 1스폰 유지 |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/TractorBeam.cs:클래스 전체` | 빔이 비활성/파괴될 때 Release() 가 불리지 않아 들고 있던 파츠가 Carried 상태(가벼운 질량·PartCarried 레이어)로 영구히 남는다. | void OnDisable() => Release(); 추가 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Demo/DemoClampReleaser.cs:Configure / _done` | Configure 가 _entries 를 교체하면서 _done 을 초기화하지 않아, 이전 실행에서 true 로 표시된 인덱스가 새 항목에 그대로 매핑되어 그 파츠의 고정쇠가 영원히 풀리지 않는다. | Configure 안에서 _done.Clear(); _elapsed = 0f; 추가 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartSocket.cs:All / Register()` | static All 목록에 Register() 로 수동 등록한 소켓은 이미 비활성 상태면 OnDisable 이 오지 않아 파괴 후에도 목록에 남는다. PartAssembler.FindMatch 는 null 만 걸러내므로 stale 엔트리가 후보 탐색을 오염시킬 수 있다. | OnDestroy 에서도 All.Remove(this) 하고, FindMatch 순회 시 파괴/비활성 소켓을 제거 |

### [Network] — 담당 금준영 · 🔴 높음 1 · 🟠 중간 1

_범위: 웹소켓 세션_

| 위험도 | 위치 | 문제 | 조치 제안 |
| --- | --- | --- | --- |
| 🔴 높음 | `METAGAME/Assets/_Project/Scripts/Runtime/Network/NetworkSession.cs:Send / CloseSocket` | Send()는 socket!=null만 검사하고 socketCancellation.Token을 쓴다. CloseSocket()이 CTS를 Cancel+Dispose+null 로 만들므로 종료·재연결 직후 Send 호출 시 NullReference 또는 ObjectDisposedException 이 난다(진행 중인 SendAsync도 Dispose된 토큰을 사용). | var cts = socketCancellation; if (!IsConnected \ |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Network/NetworkSession.cs:ConnectSocketAsync` | 백그라운드 스레드에서 socket 필드에 직접 대입하고 connectTask 완료를 기다리지 않으므로, 이전 Connect가 진행 중인 상태에서 다시 Connect 하면 늦게 끝난 옛 소켓이 socket 을 덮어쓰는 경합이 있다. | 연결 시퀀스 번호/토큰을 비교해 자신의 CTS가 유효할 때만 socket 대입, 또는 Connect 시 이전 connectTask 취소 완료 대기 |

### [Backend] — 담당 최재현 · 🔴 높음 1

_범위: 서버 설정_

| 위험도 | 위치 | 문제 | 조치 제안 |
| --- | --- | --- | --- |
| 🔴 높음 | `backend/src/main/resources/application.yml:1-24` | DB 비밀번호(metagame1234)와 JWT 서명 시크릿이 리포지토리에 평문 커밋되어 있어, 그대로 배포/유출되면 토큰 위조가 가능하다. 주석만으로는 교체가 강제되지 않는다. | `password: ${DB_PASSWORD}` / `secret: ${JWT_SECRET}` 로 바꾸고 기본값 없이 부팅 실패하게 두기 |

### [Art] — 담당 팀장(임시) · 🟠 중간 5 · 🟡 낮음 7

_범위: Blender 에셋 생성·프리뷰 도구, 모델·텍스처·프리팹·셰이더, Prim(프리미티브 메시)_

| 위험도 | 위치 | 문제 | 조치 제안 |
| --- | --- | --- | --- |
| 🟠 중간 | `METAGAME/Tools/Blender/generate_asset_previews.py:80-100` | prefab 순회에서 fbx/mat 을 각각 마지막 참조로 덮어쓰고 tex_map 키가 fbx.stem 이라, 프리팹이 머티리얼을 여러 개 참조하거나 같은 FBX를 쓰는 프리팹이 둘 이상이면 엉뚱한 BaseColor 로 렌더된다. | prefab 당 (fbx, mat) 쌍을 리스트로 모으고 이미 매핑된 stem 은 덮어쓰지 않도록(또는 경고 출력) 처리 |
| 🟠 중간 | `METAGAME/Tools/Blender/generate_spaceflop_models.py:main/build_ship` | manifest 는 두 빌드가 모두 끝난 뒤에만 기록되는데, build_ship 이 중간에 실패하면 FBX/blend 는 이미 덮어써진 반면 model_manifest.json 은 옛 내용이라 validate 가 실제와 다른 자산을 검사한다. | 각 build_* 직후 부분 manifest 를 쓰거나, 실패 시 manifest 를 무효화(삭제)하도록 처리 |
| 🟠 중간 | `METAGAME/Assets/_Project/Prefabs/Resources/` | SF_Rabbit_Base/SF_Spaceship_Capsule 프리팹이 Resources 폴더에 있어 참조 여부와 무관하게 항상 빌드에 포함되고 로딩 경로가 문자열에 의존한다. Shaders/Resources 도 동일. | Addressables 또는 프리팹 직접 참조로 전환하고 Resources 폴더 제거(불가하면 사용 이유를 문서화) |
| 🟠 중간 | `METAGAME/Assets/_Project/Models/Spaceships/SF_Spaceship_Capsule.fbx` | Stage1/SF_Stage1_Spaceship_Capsule.fbx 와 사실상 중복인 구버전 모델·프리팹(Resources/SF_Spaceship_Capsule.prefab)이 함께 남아 어느 쪽이 정본인지 불명확. | 정본 하나만 남기고 구버전 모델/프리팹 삭제 후 참조 갱신 |
| 🟠 중간 | `METAGAME/Assets/_Project/Models/Environment/Stage1/alien_flower_cluster.fbx` | Environment/Stage1 의 fbx·텍스처가 모두 snake_case 소문자로, 다른 에셋의 SF_/T_ 접두어 PascalCase 규칙과 어긋나 검색·일괄 처리 규칙이 깨진다(planet, rock_pillars 등 7종 동일). | SF_Stage1_AlienFlowerCluster.fbx 형태로 일괄 리네임(Unity 내에서 리네임해 GUID 유지) |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/Core/Prim.cs:Ghost / Flat` | Dough 는 캐시하지만 Ghost·Flat 은 호출마다 new Material 을 만들고 해제 경로가 없다. 런타임에서 반복 생성되는 표시등·테두리에 쓰이면 머티리얼이 계속 쌓인다. | Dough 와 동일하게 색/파라미터 키로 Dictionary 캐시 사용 |
| 🟡 낮음 | `METAGAME/Tools/Blender/generate_asset_previews.py:205` | loop_triangles 는 calc_loop_triangles() 호출 전에는 항상 비어 있어 폴리곤 수가 'triangles' 로 보고된다(쿼드 메시면 절반으로 과소 집계). | o.data.calc_loop_triangles() 호출 후 len(o.data.loop_triangles) 사용 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Shaders/Resources/GhostSurface.shader:Pass` | Cull Off + ZWrite Off 조합이라 뒷면·앞면이 이중 블렌딩되어 겹치는 부분이 과하게 밝아진다(포털 링·포식자 몸에서 눈에 띈다). | Cull Back 으로 두거나 뒷면 패스를 분리해 순서대로 그리기 |
| 🟡 낮음 | `METAGAME/Tools/Blender/validate_spaceflop_models.py:48-79` | 0.90 / 8 / 45 / 70 / 10 같은 임계값이 코드에 흩어진 매직 넘버라, 모델 구성이 조금만 바뀌어도 의미 없는 FAIL 이 난다. | manifest 값 기반 상대 임계치 + 파일 상단 상수로 추출 |
| 🟡 낮음 | `METAGAME/Tools/Blender/generate_spaceflop_models.py:clear_scene 첫 줄` | `bpy.ops... if cond else None` 조건 표현식으로 부수효과를 내는 관용구는 의도가 불명확하고, 배경 실행에서 활성 오브젝트가 있어도 편집 모드가 아니면 아무 것도 하지 않는 것처럼 읽힌다. | 일반 if 문으로 풀고 `bpy.ops.object.mode_set` 호출을 try/except 없이 명시적 조건으로 감싸기 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Prefabs/Resources/SF_Rabbit_Base.prefab` | 프리팹 접두어가 PF_(Stage1 프리팹)와 SF_(Resources 프리팹)로 혼용되어 파일 종류를 이름으로 구분할 수 없다. | 프리팹은 PF_ 접두어로 통일하고 SF_ 는 소스 모델에만 사용 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Textures/Concept/Characters/SF_RabbitAstronaut_TPose_Front_v2.png` | 컨셉 아트 v1/v2/v3/Standalone 버전 잔여 파일이 함께 커밋되어 리포 용량과 혼동을 늘린다. | 최종 버전만 남기고 나머지는 삭제(필요하면 사내 드라이브로 이관) |

### [QA] — 담당 팀장(임시) · 🟠 중간 2 · 🟡 낮음 3

_범위: 에디터 테스트·셀프테스트_

| 위험도 | 위치 | 문제 | 조치 제안 |
| --- | --- | --- | --- |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Bootstrap/SpaceSelfTest.cs:Start()` | 조기 종료 조건에 doll 이 빠져 있어(rig 는 있으나 rig.Doll 이 null 인 경우) 바로 뒤 Body() 의 doll.Bodies 에서 NullReference 로 셀프테스트가 예외로 죽고 Finish/Quit 도 안 불린다. | if (session == null \ |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Tests/Editor/*.cs` | PartSocket.All 정적 레지스트리를 PartAssembler/PartSocket/WeldPoint 테스트만 SetUp/TearDown 에서 Clear 하고 PartClampTests·SpaceshipPartTests·TractorBeamTests 는 손대지 않는다. 파괴된 오브젝트의 소켓이 남으면 실행 순서에 따라 결과가 달라진다. | 공통 베이스 픽스처(또는 [SetUp] 공유 헬퍼)에서 모든 테스트가 PartSocket.All.Clear() 와 simulationMode 복원을 하게 통일 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Tests/Editor/MeteorSpawnerTests.cs:PointsFrom` | guard 200000 스텝이 소진되면 요청한 개수보다 적은(또는 빈) 리스트를 반환하고, 두 빈 리스트 비교라도 CollectionAssert.AreEqual 이 통과해 시드 재현성 테스트가 헛돈다. | return 전에 Assert.AreEqual(count, points.Count) 로 수집 실패를 실패로 드러낼 것 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Tests/Editor/PartAssemblerTests.cs:FindMatch_IsRepeatable` | first 에 대해 HasValue 를 검사하지 않아 후보가 아예 없으면 'None == None' 으로 통과하거나 TargetSocket 접근에서 엉뚱한 예외가 난다. | 루프 전에 Assert.IsTrue(first.HasValue) 추가 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Tests/Editor/PartClampTests.cs:AddClamp/CreateInteractor` | 반경 0.3, 사거리 3f, 시야 60/80 도 같은 값이 테스트마다 흩어진 매직 넘버라 튜닝 값이 바뀌면 조용히 어긋난다. | 테스트 전용 상수 또는 Tuning/PhysicsScale 값 참조로 대체 |

### [UI] — 담당 팀장(임시) · 🟠 중간 2 · 🟡 낮음 3

_범위: 로비 HUD·게임 HUD_

| 위험도 | 위치 | 문제 | 조치 제안 |
| --- | --- | --- | --- |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/UI/LobbyHud.cs:EnsureStyles/FindKoreanFont` | GameHud 가 명시한 규칙(글리프 요청은 OnGUI 밖, Font.textureRebuilt 감시)을 LobbyHud 는 지키지 않는다. OS 동적 폰트로 한글을 그리는데 사전 RequestCharactersInTexture 도, 아틀라스 재빌드 시 재요청도 없어 로비 텍스트가 엉뚱한 글자로 보일 수 있다. | Initialize/OnEnable 에서 로비에 쓰는 문자열을 모아 RequestCharactersInTexture 로 예열하고 Font.textureRebuilt 구독·해제 추가 |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/UI/GameHud.cs:Reticle / 오른쪽 위 몸 상태 블록` | HandLine 은 hand==null 을 처리하는데, 호출부에서 Local.LeftHand.Gripping / Local.RightHand.Aimed 를 먼저 직접 참조한다. 손이 아직 초기화되지 않은 프레임에 NRE 로 OnGUI 전체가 죽는다. | 지역 변수로 var lh = Local.LeftHand; 받아 null 검사 후 Gripping/Aimed 접근 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/UI/GameHud.cs:Awake` | Awake 에서 만든 Texture2D white 를 해제하는 경로가 없어 HUD 가 파괴·재생성될 때마다 텍스처가 누적된다. | void OnDestroy() { if (white != null) Destroy(white); } 추가 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/UI/LobbyHud.cs:Update` | 에러 상태를 한글 문자열 리터럴 비교로 판별해, 메시지 문구가 바뀌면 조용히 동작하지 않는다. 게다가 매 프레임 자기 값에 자기 값을 대입하는 무의미한 분기다. | disconnected 같은 bool 플래그(또는 상수)로 상태를 표현하고 Update 분기를 그 플래그로 교체 |
| 🟡 낮음 | `METAGAME/Assets/_Project/Scripts/Runtime/UI/GameHud.cs:"표지 " + s.CheckpointIndex + " / 6"` | 체크포인트 총 개수 6 이 HUD 문자열에 하드코딩돼 스테이지가 바뀌면 표시가 틀어진다. | GameSession 이 노출하는 총 개수(예: s.CheckpointCount)를 사용 |

### [Physics] — 담당 팀장(임시) · 🟠 중간 1

_범위: 부트스트랩의 전역 물리 설정(gravity·fixedDeltaTime)_

| 위험도 | 위치 | 문제 | 조치 제안 |
| --- | --- | --- | --- |
| 🟠 중간 | `METAGAME/Assets/_Project/Scripts/Runtime/Bootstrap/SpaceBootstrap.cs:Awake` | Physics.gravity·Time.fixedDeltaTime·targetFrameRate 전역 값을 Awake 에서 바꾸고 복원하지 않는다. 씬을 다시 로드하거나 로비로 돌아가면 값이 남아 다른 씬 물리에 영향을 준다. | 원래 값을 보관해 OnDestroy 에서 복원하거나, 전역 설정은 단일 진입점(Bootstrap 상위)에서만 설정 |
## 6. 구조 판정

| 항목 | 판정 | 근거 |
| --- | --- | --- |
| 구조 | ⚠️ | 에디터 코드가 _Project/Editor(asmdef 없음)와 _Project/Scripts/Editor(Metagame.Editor asmdef)로 이원화돼 있고, 빌드/퍼즐 도구가 참조하는 씬 경로는 규칙 밖인 Assets/Scenes/Main.unity 다 |
| 씬/프리팹 | ❓ | .unity 씬 파일이 스냅샷에 하나도 없고 씬은 Editor 스크립트(StageSceneSetup/Stage1PuzzleBuilder)로 생성되는 구조로 보여 공유 씬 충돌 여부를 판단할 근거가 없음 |

## 7. 사람이 확인해야 할 질문

- CI_ENABLE=true 로 켠 상태에서 mr-lint 를 일부러 실패시켜 mm-ci-failed 가 실제로 발송되는지 확인 부탁드립니다.
- Tools 메뉴로 Stage1 씬 구성/1장 퍼즐 배치를 실행할 때, 편집 중인 미저장 씬이 저장 확인 없이 사라지는지 재현 확인 부탁드립니다.
- SpaceSelfTest 가 붙은 씬을 에디터에서 Play 하면 Finish() 의 Application.Quit 이 실행되는지(커맨드라인 -space-selftest 로만 붙는 구조인지) 확인 필요
- 이 프로젝트의 피해·리스폰 판정이 서버 권위로 처리되는 레이어가 별도로 있는지(DamageSource/GameSession 이 클라이언트 로컬에서 직접 적용해도 되는 구조인지)

## 8. 청크별 원본 응답

<details><summary>청크 1: .gitlab-ci.yml · METAGAME/Assets/_Project/Editor · Editor · Runtime/Bootstrap — 판정 warn</summary>

```json
{"verdict":"warn","checks":{"structure":{"status":"warn","why":"에디터 코드가 _Project/Editor(asmdef 없음)와 _Project/Scripts/Editor(Metagame.Editor asmdef)로 이원화돼 있고, 빌드/퍼즐 도구가 참조하는 씬 경로는 규칙 밖인 Assets/Scenes/Main.unity 다"}},"findings":[{"severity":"high","file":".gitlab-ci.yml","line":"mm-ci-failed 잡","issue":"rules 가 있으면 잡 최상위 when: on_failure 는 무시되고 rules 항목의 기본 when(on_success)이 적용된다. 실패 알림이 오히려 성공 시 발송되고 실패 시엔 안 나간다.","fix":"rules 항목 안으로 옮기기: `- if: $CI_PIPELINE_SOURCE == \"merge_request_event\" && $MM_WEBHOOK_URL\\n  when: on_failure`"},{"severity":"high","file":"METAGAME/Assets/_Project/Editor/StageSceneSetup.cs","line":"EnsureStage1Exists","issue":"EditorSceneManager.NewScene(..., NewSceneMode.Single) 를 저장 여부 확인 없이 호출해 현재 열려 있던 미저장 씬 작업이 그대로 날아간다.","fix":"앞에 EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() 를 호출하고 false 면 중단"},{"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Editor/Stage1PuzzleBuilder.cs","line":"Build 첫 줄","issue":"OpenScene(Single) 로 현재 씬을 확인 없이 교체한다. 편집 중이던 변경분이 손실될 수 있다.","fix":"SaveCurrentModifiedScenesIfUserWantsTo() 결과가 false 면 return"},{"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Editor/SpaceBuild.cs","line":"EnsureAlwaysIncludedShaders","issue":"AssetDatabase.LoadAllAssetsAtPath 는 Assets/ 하위 자산용이라 \"ProjectSettings/GraphicsSettings.asset\" 로는 대개 빈 배열을 반환해 조용히 return 된다 — 셰이더 포함 보장이 실제로는 동작하지 않을 수 있다.","fix":"GraphicsSettings.GetGraphicsSettings() 를 SerializedObject 로 감싸 수정"},{"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Runtime/Bootstrap/SpaceBootstrap.cs","line":"Awake","issue":"Physics.gravity·Time.fixedDeltaTime·targetFrameRate 전역 값을 Awake 에서 바꾸고 복원하지 않는다. 씬을 다시 로드하거나 로비로 돌아가면 값이 남아 다른 씬 물리에 영향을 준다.","fix":"원래 값을 보관해 OnDestroy 에서 복원하거나, 전역 설정은 단일 진입점(Bootstrap 상위)에서만 설정"},{"severity":"low","file":"METAGAME/Assets/_Project/Scripts/Runtime/Bootstrap/LobbyBootstrap.cs","line":"Awake","issue":"gameObject.AddComponent<LobbyHud>() 를 무조건 추가하므로, 같은 오브젝트가 재활성/재초기화되거나 컴포넌트가 두 번 붙으면 HUD 가 중복 생성된다. NetworkSession 생성도 같은 프레임에 두 Bootstrap 이 있으면 경합한다.","fix":"GetComponent<LobbyHud>() ?? AddComponent<LobbyHud>() 로 보장하고 NetworkSession 생성은 싱글턴 내부 static 팩토리로 일원화"}],"questions":["CI_ENABLE=true 로 켠 상태에서 mr-lint 를 일부러 실패시켜 mm-ci-failed 가 실제로 발송되는지 확인 부탁드립니다.","Tools 메뉴로 Stage1 씬 구성/1장 퍼즐 배치를 실행할 때, 편집 중인 미저장 씬이 저장 확인 없이 사라지는지 재현 확인 부탁드립니다."]}
```

</details>

<details><summary>청크 2: Runtime/Bootstrap · Runtime/Core · Runtime/Gameplay — 판정 warn</summary>

```json
{"verdict":"warn",
 "checks":{"structure":{"status":"ok","why":"파일이 METAGAME/Assets/_Project/Scripts/Runtime/{Bootstrap,Core,Gameplay} 아래에 있고 네임스페이스(Metagame.Bootstrap/Core/Gameplay[.Hazards/.Interaction/.Demo])가 폴더와 일치, Core asmdef 는 references 가 비어 Core→상위 역참조 없음"}},
 "findings":[
  {"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Runtime/Bootstrap/SpaceSelfTest.cs","line":"Start()","issue":"조기 종료 조건에 doll 이 빠져 있어(rig 는 있으나 rig.Doll 이 null 인 경우) 바로 뒤 Body() 의 doll.Bodies 에서 NullReference 로 셀프테스트가 예외로 죽고 Finish/Quit 도 안 불린다.","fix":"if (session == null || level == null || rig == null || doll == null) { Finish(); yield break; }"},
  {"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Hazards/MeteorSpawner.cs","line":"AdvanceSchedule / IntervalAt","issue":"_intervalMin(또는 _rampSeconds<=0 시 반환값)이 0 이면 CurrentInterval 이 0 이 되어 매 물리 스텝마다 예고·스폰이 생성되고 운석이 무제한 누적된다. [Min(0f)] 이라 0 입력이 허용된다.","fix":"IntervalAt 반환값을 Mathf.Max(minInterval, 0.05f) 같은 하한으로 클램프하고 while 대신 1스텝 1스폰 유지"},
  {"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/GameSession.cs","line":"Awake / Instance","issue":"static Instance 를 Awake 에서 무조건 덮어쓰고 OnDestroy 에서 비우지 않는다. 씬 재로드·재입장 시 파괴된 인스턴스를 참조하거나 두 번째 인스턴스가 첫 번째를 밀어내 진행 상태가 어긋난다.","fix":"Awake 에서 Instance!=null&&Instance!=this 면 Destroy(this), OnDestroy 에서 if (Instance==this) Instance=null"},
  {"severity":"low","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/DamageSource.cs","line":"nextDamageTime","issue":"PlayerRig 를 키로 쓰는 딕셔너리를 비우는 경로가 없어 파괴된(리스폰/디스코넥트) 리그 항목이 계속 남는다. 위험 오브젝트가 장수명일 때 누수.","fix":"쿨다운 만료 항목 주기적 제거 또는 PlayerRig 쪽에 마지막 피격 시각을 저장"},
  {"severity":"low","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Demo/DemoClampReleaser.cs","line":"Configure / _done","issue":"Configure 가 _entries 를 교체하면서 _done 을 초기화하지 않아, 이전 실행에서 true 로 표시된 인덱스가 새 항목에 그대로 매핑되어 그 파츠의 고정쇠가 영원히 풀리지 않는다.","fix":"Configure 안에서 _done.Clear(); _elapsed = 0f; 추가"},
  {"severity":"low","file":"METAGAME/Assets/_Project/Scripts/Runtime/Core/Prim.cs","line":"Ghost / Flat","issue":"Dough 는 캐시하지만 Ghost·Flat 은 호출마다 new Material 을 만들고 해제 경로가 없다. 런타임에서 반복 생성되는 표시등·테두리에 쓰이면 머티리얼이 계속 쌓인다.","fix":"Dough 와 동일하게 색/파라미터 키로 Dictionary 캐시 사용"}
 ],
 "questions":["SpaceSelfTest 가 붙은 씬을 에디터에서 Play 하면 Finish() 의 Application.Quit 이 실행되는지(커맨드라인 -space-selftest 로만 붙는 구조인지) 확인 필요","이 프로젝트의 피해·리스폰 판정이 서버 권위로 처리되는 레이어가 별도로 있는지(DamageSource/GameSession 이 클라이언트 로컬에서 직접 적용해도 되는 구조인지)"]}
```

</details>

<details><summary>청크 3: Runtime/Gameplay — 판정 warn</summary>

```json
{"verdict": "warn",
 "checks": {"structure": {"status": "warn", "why": "위치·asmdef 의존(Gameplay→Core)은 규칙에 맞으나 Physics/·Player/ 폴더의 타입들이 모두 네임스페이스 Metagame.Gameplay 로 폴더와 불일치(GameLayers 는 이유 주석 있음, Player/Arm·Doll·Hand 등은 근거 없음)"}},
 "findings": [
  {"severity": "high", "file": "METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartAssembler.cs", "line": "Assemble: existing.Release() ~ link.Bind", "issue": "재조립 경로에서 기존 PartAssemblyLink.Release() 가 Destroy(this) 를 하지만 플레이 모드에서는 파괴가 지연된다. 직후 같은 오브젝트에 AddComponent<PartAssemblyLink>() 를 호출하면 [DisallowMultipleComponent] 로 null 이 반환되어 link.Bind 에서 NRE.", "fix": "기존 링크를 재사용해 Bind 로 갱신하거나, Release 후 컴포넌트 파괴 대신 상태만 초기화하고 AddComponent 결과 null 검사 추가"},
  {"severity": "high", "file": "METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/Interactor.cs", "line": "클래스 전체", "issue": "OnDisable/OnDestroy 가 없어 홀드 중에 플레이어가 파괴·비활성되면 Held.Release(this) 가 호출되지 않아 대상이 영구히 Occupied 로 남는다(고정쇠·용접점이 아무도 쓸 수 없게 됨).", "fix": "void OnDisable() => Cancel(); 추가"},
  {"severity": "medium", "file": "METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/TractorBeam.cs", "line": "클래스 전체", "issue": "빔이 비활성/파괴될 때 Release() 가 불리지 않아 들고 있던 파츠가 Carried 상태(가벼운 질량·PartCarried 레이어)로 영구히 남는다.", "fix": "void OnDisable() => Release(); 추가"},
  {"severity": "medium", "file": "METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Interaction/Interactor.cs", "line": "Complete()", "issue": "완료 후 Focus 를 그대로 두고 State 만 Idle 로 만들어, 같은 대상이 계속 최적 후보이면 SetFocus 의 ReferenceEquals 로 조기 반환되어 State 가 Focused 로 복귀하지 못한다(콜라이더를 끄지 않는 상호작용에서 UI 상태 불일치).", "fix": "Complete 끝에서 SetFocus(null) 또는 Focus 재평가를 강제"},
  {"severity": "medium", "file": "METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/Doll.cs", "line": "SetSleepAllowed", "issue": "if(allowed) WakeUp(); else WakeUp(); 로 두 분기가 동일해 파라미터가 무의미하다. 잠들기 허용/금지 의도가 전혀 구현되지 않았다.", "fix": "else 분기를 Sleep() 로 바꾸거나 메서드를 제거"},
  {"severity": "low", "file": "METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartSocket.cs", "line": "All / Register()", "issue": "static All 목록에 Register() 로 수동 등록한 소켓은 이미 비활성 상태면 OnDisable 이 오지 않아 파괴 후에도 목록에 남는다. PartAssembler.FindMatch 는 null 만 걸러내므로 stale 엔트리가 후보 탐색을 오염시킬 수 있다.", "fix": "OnDestroy 에서도 All.Remove(this) 하고, FindMatch 순회 시 파괴/비활성 소켓을 제거"}
 ],
 "questions": ["Placed 상태 파츠를 빔으로 다시 집어 같은 소켓에 재조립할 때 플레이 모드에서 PartAssemblyLink AddComponent 오류/NRE 가 나는지 확인 부탁드립니다.", "파츠 상태·조립·용접을 네트워크에서 누가 권위로 결정하는지(호스트 전용 Tick 호출 지점)가 이 청크에 없는데, 별 모듈에 있는지 확인 필요합니다."]}
```

</details>

<details><summary>청크 4: Runtime/Gameplay — 판정 warn</summary>

```json
{"verdict":"warn","checks":{"structure":{"status":"ok","why":"Player/World 파일이 _Project/Scripts/Runtime/Gameplay 아래에 있고 네임스페이스 Metagame.Gameplay, 참조는 Metagame.Core 방향뿐이라 Core←Gameplay 규칙과 일치"}},"findings":[{"severity":"high","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/PlayerRig.cs","line":"ApplyDamage / UpdateSpeed / Locomotion","issue":"ApplyDamage가 사망 시 Ragdolled=true 로 두지만 UpdateSpeed가 매 물리틱마다 Ragdolled = down && !Hanging 으로 덮어써서 사망 상태가 사라진다. IsDead를 보는 곳이 없어 죽은 뒤에도 이동·점프·잡기가 그대로 동작한다.","fix":"UpdateSpeed에서 `Ragdolled = IsDead || (down && !Hanging);` 로 하고 Locomotion/DriveArms 진입부에 `if (IsDead) return;` 추가"},{"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/Checkpoint.cs","line":"OnTriggerEnter","issue":"트리거에 닿은 PlayerRig가 로컬 플레이어인지 확인하지 않고 GameSession.Instance.ReachCheckpoint를 호출한다. 원격/다른 플레이어의 콜라이더가 로컬 세션의 부활 지점을 옮길 수 있다.","fix":"`if (rig == null || !rig.IsLocal || GameSession.Instance == null) return;` (권한 판정은 세션 소유자 기준으로)"},{"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/PlayerRig.cs","line":"RestoreCarriedBodyIfUnused / RestoreAllCarriedBodies","issue":"운반 중 물체가 파괴되면 body == null 로 조기 return 되어 carriedBodies 항목이 남고, 파괴된 Rigidbody 키가 계속 쌓인다(복원도 안 됨). isKinematic/useGravity를 바꾼 상태로 남는 물체도 생길 수 있다.","fix":"파괴 감지 시 `carriedBodies.Remove(body)` 로 항목을 정리하고, 주기적으로 null 키를 걸러내는 정리 루틴 추가"},{"severity":"low","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/OrbitCamera.cs","line":"LateUpdate (SphereCastNonAlloc)","issue":"카메라 충돌 검사가 레이어 마스크 ~0 으로 모든 것을 때리고, 히트마다 GetComponentInParent<PlayerRig>() 를 호출해 자기 몸을 걸러낸다. 매 프레임 계층 탐색 비용이며 다른 플레이어 몸에도 카메라가 밀린다.","fix":"RagdollBuilder.WorldMask(또는 지형 전용 마스크)를 넘겨 쿼리 단계에서 인형 레이어를 제외"},{"severity":"low","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/PlayerRig.cs","line":"TryGrabTool / ResetToolState","issue":"toolBodyHadGravity 와 BeginCarriedBody 의 CarriedBodyState.HadGravity 가 같은 값을 이중 관리하고, ResetToolState가 복원 뒤 다시 useGravity를 덮어쓴다. 한쪽만 바뀌면 중력 상태가 어긋난다.","fix":"toolBodyHadGravity 필드를 제거하고 CarriedBodyState 복원 경로 하나만 사용"},{"severity":"low","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Player/PlayerRig.cs","line":"ProbeGround / Hover / DriveLegs 등","issue":"0.22f, 1.6f, 7f, 0.30f, 0.16f, 240f 같은 임계값이 코드에 직접 박혀 있어 Tuning 값과 튜닝 지점이 갈린다.","fix":"접지 여유·점프 락·먼지 임계 등을 Tuning 상수로 이동"}],"questions":["사망(Health 0) 후 캐릭터가 계속 걷고 점프하는지 인게임에서 확인 부탁","도구를 들고 있을 때 ToolAction 이외 방법으로 놓을 수 있는지(의도된 조작인지) 확인"]}
```

</details>

<details><summary>청크 5: Runtime/Gameplay · Runtime/Network — 판정 warn</summary>

```json
{"verdict":"warn","checks":{"structure":{"status":"ok","why":"파일이 _Project/Scripts/Runtime/{Gameplay,Network} 아래에 있고 네임스페이스(Metagame.Gameplay / Metagame.Network)와 폴더·asmdef 이름이 일치하며 Gameplay→Core 방향만 참조한다"}},"findings":[{"severity":"high","file":"METAGAME/Assets/_Project/Scripts/Runtime/Network/NetworkSession.cs","line":"Send / CloseSocket","issue":"Send()는 socket!=null만 검사하고 socketCancellation.Token을 쓴다. CloseSocket()이 CTS를 Cancel+Dispose+null 로 만들므로 종료·재연결 직후 Send 호출 시 NullReference 또는 ObjectDisposedException 이 난다(진행 중인 SendAsync도 Dispose된 토큰을 사용).","fix":"var cts = socketCancellation; if (!IsConnected || socket == null || cts == null) { ReportError(...); return; } 로 로컬 캡처 후 사용하고, Dispose는 진행 중 작업 완료 후로 미룬다"},{"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Runtime/Network/NetworkSession.cs","line":"ConnectSocketAsync","issue":"백그라운드 스레드에서 socket 필드에 직접 대입하고 connectTask 완료를 기다리지 않으므로, 이전 Connect가 진행 중인 상태에서 다시 Connect 하면 늦게 끝난 옛 소켓이 socket 을 덮어쓰는 경합이 있다.","fix":"연결 시퀀스 번호/토큰을 비교해 자신의 CTS가 유효할 때만 socket 대입, 또는 Connect 시 이전 connectTask 취소 완료 대기"},{"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/Vacuum.cs","line":"EnsureLights / OnEnable","issue":"[ExecuteAlways] + 비직렬화 필드 sun/fill 조합이라 에디터 도메인 리로드나 씬 재열기 때마다 sun==null 로 판단해 \"해\"·\"행성 반사광\" GameObject를 새로 만든다. 라이트가 씬에 누적된다.","fix":"자식에서 기존 라이트를 먼저 찾거나(transform.Find/GetComponentsInChildren) [SerializeField] 로 참조를 보존"},{"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/Goal.cs","line":"Update / Create","issue":"ringMat 이 sharedMaterial 이므로 Update의 SetColor 가 같은 Prim.Ghost 머티리얼을 쓰는 다른 오브젝트(착륙등 등)까지 전역으로 물들인다. 또 재생 중 매 프레임 공유 애셋을 수정한다.","fix":"ring.GetComponent<Renderer>().material 로 인스턴스 머티리얼을 받아 캐시해 쓰고 OnDestroy 에서 Destroy(ringMat)"},{"severity":"medium","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/Goal.cs","line":"Instance (PowerSocket.Instance, SlidingDoor.Instance 동일)","issue":"static Instance 를 Create 에서만 설정하고 OnDestroy 에서 비우지 않는다. 레벨 재빌드·재입장 시 파괴된 옛 인스턴스가 잠시 남아 HUD/셀프테스트가 잘못된 대상을 참조할 수 있다.","fix":"각 클래스에 void OnDestroy() { if (Instance == this) Instance = null; } 추가"},{"severity":"low","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/Starfield.cs","line":"BuildStars / LateUpdate","issue":"런타임 생성 Mesh 를 파괴 시 해제하지 않아 씬 재생성마다 누수되고, follow 는 한 번 캐시한 뒤 갱신하지 않아 Camera.main 이 교체되면 배경이 옛 카메라를 따라간다.","fix":"OnDestroy 에서 Destroy(mesh), LateUpdate 에서 follow==null 또는 비활성일 때 재탐색"},{"severity":"low","file":"METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/World/LevelBuilder.cs","line":"Build()","issue":"Random.InitState(20260825) 로 전역 UnityEngine.Random 상태를 덮어써 같은 프레임의 다른 시스템(파티클·Puff 등) 난수 재현성에 영향을 준다.","fix":"var st = Random.state; ... Random.state = st; 로 복원하거나 로컬 System.Random 사용"}],"questions":["로비에서 START_GAME 호스트 검증이 서버 쪽에 확실히 있는지(클라이언트는 누구나 StartGame() 호출 가능) 확인 필요","레벨 재빌드/재입장 시 RopeBuilder 의 Physics.IgnoreCollision 설정과 static Instance 들이 정상 초기화되는지 실제로 재현해 볼 것"]}
```

</details>

<details><summary>청크 6: Runtime/UI · Tests/Editor — 판정 warn</summary>

```json
{"verdict": "warn",
 "checks": {"structure": {"status": "warn", "why": "경로 규칙(_Project/Scripts/Runtime|Tests)은 맞지만 Tests/Editor 어셈블리 rootNamespace 는 Metagame.Gameplay.Tests 인데 NetworkSerializationTests.cs 만 Metagame.Network.Tests 로 폴더/어셈블리 네임스페이스와 어긋난다"}},
 "findings": [
  {"severity": "medium", "file": "METAGAME/Assets/_Project/Scripts/Runtime/UI/LobbyHud.cs", "line": "EnsureStyles/FindKoreanFont", "issue": "GameHud 가 명시한 규칙(글리프 요청은 OnGUI 밖, Font.textureRebuilt 감시)을 LobbyHud 는 지키지 않는다. OS 동적 폰트로 한글을 그리는데 사전 RequestCharactersInTexture 도, 아틀라스 재빌드 시 재요청도 없어 로비 텍스트가 엉뚱한 글자로 보일 수 있다.", "fix": "Initialize/OnEnable 에서 로비에 쓰는 문자열을 모아 RequestCharactersInTexture 로 예열하고 Font.textureRebuilt 구독·해제 추가"},
  {"severity": "medium", "file": "METAGAME/Assets/_Project/Scripts/Runtime/UI/GameHud.cs", "line": "Reticle / 오른쪽 위 몸 상태 블록", "issue": "HandLine 은 hand==null 을 처리하는데, 호출부에서 Local.LeftHand.Gripping / Local.RightHand.Aimed 를 먼저 직접 참조한다. 손이 아직 초기화되지 않은 프레임에 NRE 로 OnGUI 전체가 죽는다.", "fix": "지역 변수로 var lh = Local.LeftHand; 받아 null 검사 후 Gripping/Aimed 접근"},
  {"severity": "low", "file": "METAGAME/Assets/_Project/Scripts/Runtime/UI/GameHud.cs", "line": "Awake", "issue": "Awake 에서 만든 Texture2D white 를 해제하는 경로가 없어 HUD 가 파괴·재생성될 때마다 텍스처가 누적된다.", "fix": "void OnDestroy() { if (white != null) Destroy(white); } 추가"},
  {"severity": "low", "file": "METAGAME/Assets/_Project/Scripts/Runtime/UI/LobbyHud.cs", "line": "Update", "issue": "에러 상태를 한글 문자열 리터럴 비교로 판별해, 메시지 문구가 바뀌면 조용히 동작하지 않는다. 게다가 매 프레임 자기 값에 자기 값을 대입하는 무의미한 분기다.", "fix": "disconnected 같은 bool 플래그(또는 상수)로 상태를 표현하고 Update 분기를 그 플래그로 교체"},
  {"severity": "low", "file": "METAGAME/Assets/_Project/Scripts/Runtime/UI/GameHud.cs", "line": "\"표지 \" + s.CheckpointIndex + \" / 6\"", "issue": "체크포인트 총 개수 6 이 HUD 문자열에 하드코딩돼 스테이지가 바뀌면 표시가 틀어진다.", "fix": "GameSession 이 노출하는 총 개수(예: s.CheckpointCount)를 사용"},
  {"severity": "low", "file": "METAGAME/Assets/_Project/Scripts/Tests/Editor/MeteorSpawnerTests.cs", "line": "PointsFrom", "issue": "guard 200000 스텝이 소진되면 요청한 개수보다 적은(또는 빈) 리스트를 반환하고, 두 빈 리스트 비교라도 CollectionAssert.AreEqual 이 통과해 시드 재현성 테스트가 헛돈다.", "fix": "return 전에 Assert.AreEqual(count, points.Count) 로 수집 실패를 실패로 드러낼 것"}
 ],
 "questions": ["Metagame.UI / Tests 어셈블리가 Metagame.Network 를 참조하는 것이 팀이 합의한 의존 방향(Core ← Gameplay ← UI) 안에서 허용되는지 확인 필요"]}
```

</details>

<details><summary>청크 7: Tests/Editor · METAGAME/Assets/_Project/Shaders · METAGAME/BlenderSource · METAGAME/Tools — 판정 warn</summary>

```json
{"verdict": "warn",
 "checks": {"structure": {"status": "warn", "why": "위치는 _Project/Scripts/Tests/Editor · Shaders 규칙에 맞고 의존은 Tests→Gameplay→Core 방향이나, 네임스페이스 Metagame.Gameplay.Tests 와 폴더(Scripts/Tests/Editor)가 대응하지 않는다"}},
 "findings": [
  {"severity": "medium", "file": "METAGAME/Assets/_Project/Scripts/Tests/Editor/*.cs", "line": "", "issue": "PartSocket.All 정적 레지스트리를 PartAssembler/PartSocket/WeldPoint 테스트만 SetUp/TearDown 에서 Clear 하고 PartClampTests·SpaceshipPartTests·TractorBeamTests 는 손대지 않는다. 파괴된 오브젝트의 소켓이 남으면 실행 순서에 따라 결과가 달라진다.", "fix": "공통 베이스 픽스처(또는 [SetUp] 공유 헬퍼)에서 모든 테스트가 PartSocket.All.Clear() 와 simulationMode 복원을 하게 통일"},
  {"severity": "medium", "file": "METAGAME/Tools/Blender/generate_asset_previews.py", "line": "80-100", "issue": "prefab 순회에서 fbx/mat 을 각각 마지막 참조로 덮어쓰고 tex_map 키가 fbx.stem 이라, 프리팹이 머티리얼을 여러 개 참조하거나 같은 FBX를 쓰는 프리팹이 둘 이상이면 엉뚱한 BaseColor 로 렌더된다.", "fix": "prefab 당 (fbx, mat) 쌍을 리스트로 모으고 이미 매핑된 stem 은 덮어쓰지 않도록(또는 경고 출력) 처리"},
  {"severity": "low", "file": "METAGAME/Assets/_Project/Scripts/Tests/Editor/PartAssemblerTests.cs", "line": "FindMatch_IsRepeatable", "issue": "first 에 대해 HasValue 를 검사하지 않아 후보가 아예 없으면 'None == None' 으로 통과하거나 TargetSocket 접근에서 엉뚱한 예외가 난다.", "fix": "루프 전에 Assert.IsTrue(first.HasValue) 추가"},
  {"severity": "low", "file": "METAGAME/Tools/Blender/generate_asset_previews.py", "line": "205", "issue": "loop_triangles 는 calc_loop_triangles() 호출 전에는 항상 비어 있어 폴리곤 수가 'triangles' 로 보고된다(쿼드 메시면 절반으로 과소 집계).", "fix": "o.data.calc_loop_triangles() 호출 후 len(o.data.loop_triangles) 사용"},
  {"severity": "low", "file": "METAGAME/Assets/_Project/Scripts/Tests/Editor/PartClampTests.cs", "line": "AddClamp/CreateInteractor", "issue": "반경 0.3, 사거리 3f, 시야 60/80 도 같은 값이 테스트마다 흩어진 매직 넘버라 튜닝 값이 바뀌면 조용히 어긋난다.", "fix": "테스트 전용 상수 또는 Tuning/PhysicsScale 값 참조로 대체"},
  {"severity": "low", "file": "METAGAME/Assets/_Project/Shaders/Resources/GhostSurface.shader", "line": "Pass", "issue": "Cull Off + ZWrite Off 조합이라 뒷면·앞면이 이중 블렌딩되어 겹치는 부분이 과하게 밝아진다(포털 링·포식자 몸에서 눈에 띈다).", "fix": "Cull Back 으로 두거나 뒷면 패스를 분리해 순서대로 그리기"}
 ],
 "questions": ["테스트를 임의 순서(또는 단독 실행이 아닌 전체 실행)로 두 번 돌렸을 때 PartSocket.All 잔여로 인한 실패가 재현되는지", "generate_asset_previews.py 를 현재 리포에서 실행해 프리팹 다수가 같은 FBX를 참조하는 케이스가 실제로 있는지"]}
```

</details>

<details><summary>청크 8: METAGAME/Tools · backend · backend/src · scripts/ci — 판정 warn</summary>

```json
{"verdict": "warn",
 "checks": {"structure": {"status": "unknown", "why": "이 청크는 Tools/backend/scripts 만 포함해 METAGAME/Assets/_Project 배치·어셈블리 의존 방향을 판단할 근거가 없음"}},
 "findings": [
  {"severity": "high", "file": "backend/src/main/resources/application.yml", "line": "1-24", "issue": "DB 비밀번호(metagame1234)와 JWT 서명 시크릿이 리포지토리에 평문 커밋되어 있어, 그대로 배포/유출되면 토큰 위조가 가능하다. 주석만으로는 교체가 강제되지 않는다.", "fix": "`password: ${DB_PASSWORD}` / `secret: ${JWT_SECRET}` 로 바꾸고 기본값 없이 부팅 실패하게 두기"},
  {"severity": "medium", "file": "scripts/ci/mm_digest.py", "line": "17", "issue": "LEAD_MM 이 모듈 임포트 시점의 `next(...)`라 team.json 의 roles.lead 가 members 의 gitlab 값과 어긋나면 StopIteration 으로 스크립트가 즉시 죽는다(알림 전체 유실).", "fix": "`next((...), None)` + 없으면 경고 로그 후 멘션 생략으로 폴백"},
  {"severity": "medium", "file": "METAGAME/Tools/Blender/generate_spaceflop_models.py", "line": "main/build_ship", "issue": "manifest 는 두 빌드가 모두 끝난 뒤에만 기록되는데, build_ship 이 중간에 실패하면 FBX/blend 는 이미 덮어써진 반면 model_manifest.json 은 옛 내용이라 validate 가 실제와 다른 자산을 검사한다.", "fix": "각 build_* 직후 부분 manifest 를 쓰거나, 실패 시 manifest 를 무효화(삭제)하도록 처리"},
  {"severity": "low", "file": "METAGAME/Tools/Blender/validate_spaceflop_models.py", "line": "48-79", "issue": "0.90 / 8 / 45 / 70 / 10 같은 임계값이 코드에 흩어진 매직 넘버라, 모델 구성이 조금만 바뀌어도 의미 없는 FAIL 이 난다.", "fix": "manifest 값 기반 상대 임계치 + 파일 상단 상수로 추출"},
  {"severity": "low", "file": "METAGAME/Tools/Blender/generate_spaceflop_models.py", "line": "clear_scene 첫 줄", "issue": "`bpy.ops... if cond else None` 조건 표현식으로 부수효과를 내는 관용구는 의도가 불명확하고, 배경 실행에서 활성 오브젝트가 있어도 편집 모드가 아니면 아무 것도 하지 않는 것처럼 읽힌다.", "fix": "일반 if 문으로 풀고 `bpy.ops.object.mode_set` 호출을 try/except 없이 명시적 조건으로 감싸기"},
  {"severity": "low", "file": "scripts/ci/mm_notify.py", "line": "mr_opened", "issue": "`len(pls) != 1` 로 첫 파이프라인을 판별하므로, 파이프라인이 재시도/중복 트리거로 2개가 되면 리뷰 요청 알림이 영구히 발송되지 않는다.", "fix": "MARKER 노트 등 멱등 표식으로 '이미 보냄'을 판정하거나 가장 오래된 파이프라인 id 와 CI_PIPELINE_ID 비교"}
 ],
 "questions": ["application.yml 의 JWT 시크릿이 실제 배포에서도 쓰이는지, 환경변수 주입 경로가 준비돼 있는지 확인", "Blender 5.x 에서 `BLENDER_EEVEE` 엔진명과 `Emission Color` 입력명으로 generate 스크립트가 실제 렌더까지 통과하는지 1회 실행 확인"]}
```

</details>

<details><summary>청크 9: scripts/ci · scripts/runner — 판정 warn</summary>

```json
{"verdict":"warn","checks":{"structure":{"status":"unknown","why":"이 청크는 scripts/ci·scripts/runner 만 포함해 METAGAME/Assets/_Project 배치나 Core←Gameplay←UI 의존 방향을 판단할 근거가 없음"}},"findings":[{"severity":"high","file":"scripts/ci/mr_lint.py","line":"Jira 실존·상태 블록","issue":"jira.get_issue 가 RuntimeError 로 실패하면 warn 후 issue=None 이 되는데, 바로 다음 `if jira.available and issue is None` 에서 \"Jira 이슈 {key} 가 존재하지 않음\" 으로 fail 한다. 네트워크·토큰 오류가 이슈 부재로 둔갑해 파이프라인이 빨간불이 된다.","fix":"조회 오류 플래그를 두고 `if lookup_failed: pass` 로 분기 건너뛰기(예: except 에서 issue=SENTINEL 후 존재 여부 검사 생략)"},{"severity":"medium","file":"scripts/ci/mr_title.py","line":"issue = jira.get_issue(key)","issue":"mr_lint 와 달리 예외를 감싸지 않아 Jira 일시 장애 시 스크립트가 트레이스백으로 죽고 잡이 실패한다. 이 잡은 보정용이라 실패할 이유가 없다.","fix":"try/except RuntimeError 로 감싸고 메시지 출력 후 return 0"},{"severity":"medium","file":"scripts/runner/runner-claude-env.ps1","line":"foreach ($l in $lines) 루프","issue":"$inRunner 가 한 번 true 되면 꺼지지 않고 $done 은 전역 1회라, [[runners]] 가 둘 이상일 때 두 번째 이후 러너의 기존 environment 줄이 continue 로 삭제되고 대체 줄도 안 들어간다(설정 손실).","fix":"[[runners]] 를 만날 때마다 $done=$false 로 리셋하고 각 섹션마다 삽입/교체"},{"severity":"medium","file":"scripts/ci/repo_hygiene.py","line":"if asset_added and not any(f.startswith(\"Assets/\") for f in rep.fails)","issue":"에셋 위치 통과 판정을 fail 메시지 문자열 접두사로 하고 있어, 메시지 문구만 바꿔도 조용히 잘못된 ok 가 찍힌다.","fix":"loc_bad 카운터 변수를 두고 `if asset_added and not loc_bad: rep.ok(...)`"},{"severity":"low","file":"scripts/ci/repo_hygiene.py","line":"base_top = set(git(\"ls-tree\", ... check=False).split())","issue":"check=False 라 base 에 Assets 트리가 없거나 명령이 실패하면 base_top 이 빈 집합이 되어 모든 최상위 폴더가 '새 폴더'로 fail 한다. 또 .split() 은 공백 포함 폴더명을 쪼갠다.","fix":"splitlines() 사용 + ls-tree 실패 시 검사 3번을 skip/warn 처리"},{"severity":"low","file":"scripts/runner/setup-windows-runner.ps1","line":"2b) pwsh 설치 블록","issue":"runner-use-pwsh.ps1 과 달리 $asset null 검사가 없어 GitHub API 응답에 MSI 자산이 없으면 $asset.name 접근에서 모호한 오류가 난다.","fix":"if (-not $asset) { throw \"win-x64 MSI 를 못 찾음 — 수동 설치\" } 추가"}],"questions":["Jira 자격증명이 없거나 API 가 5xx 인 상태에서 mr-lint 를 돌려, 실제로 '이슈가 존재하지 않음' 으로 실패하는지 재현 확인","runner-claude-env.ps1 을 [[runners]] 가 2개 이상인 config.toml 에서 실행했을 때 다른 러너의 environment 가 유지되는지 확인"]}
```

</details>

<details><summary>구조/에셋 리뷰어 — 판정 warn</summary>

```json
{"verdict": "warn",
 "checks": {
   "scene": {"status": "unknown", "why": ".unity 씬 파일이 스냅샷에 하나도 없고 씬은 Editor 스크립트(StageSceneSetup/Stage1PuzzleBuilder)로 생성되는 구조로 보여 공유 씬 충돌 여부를 판단할 근거가 없음"},
   "structure": {"status": "warn", "why": "_Project 분류 폴더는 대체로 지켜졌으나 Editor 스크립트 위치 이원화, 접두어(SF_/PF_)·소문자 파일명 혼용, Resources 사용, 중복/버전 잔여 파일 존재"}
 },
 "findings": [
   {"severity": "high", "file": "METAGAME/Assets/_Project/Editor/StageSceneSetup.cs", "line": "", "issue": "Editor 스크립트가 Scripts/Editor(Metagame.Editor asmdef)와 별개인 _Project/Editor/ 에도 있어 이 파일만 Assembly-CSharp-Editor에 속한다. asmdef 어셈블리(Metagame.Gameplay 등)를 참조할 수 없어 빌드/참조 구조가 깨진다.", "fix": "StageSceneSetup.cs 를 Assets/_Project/Scripts/Editor/ 로 이동하고 빈 _Project/Editor 폴더 정리"},
   {"severity": "medium", "file": "METAGAME/Assets/_Project/Prefabs/Resources/", "line": "", "issue": "SF_Rabbit_Base/SF_Spaceship_Capsule 프리팹이 Resources 폴더에 있어 참조 여부와 무관하게 항상 빌드에 포함되고 로딩 경로가 문자열에 의존한다. Shaders/Resources 도 동일.", "fix": "Addressables 또는 프리팹 직접 참조로 전환하고 Resources 폴더 제거(불가하면 사용 이유를 문서화)"},
   {"severity": "medium", "file": "METAGAME/Assets/_Project/Models/Spaceships/SF_Spaceship_Capsule.fbx", "line": "", "issue": "Stage1/SF_Stage1_Spaceship_Capsule.fbx 와 사실상 중복인 구버전 모델·프리팹(Resources/SF_Spaceship_Capsule.prefab)이 함께 남아 어느 쪽이 정본인지 불명확.", "fix": "정본 하나만 남기고 구버전 모델/프리팹 삭제 후 참조 갱신"},
   {"severity": "medium", "file": "METAGAME/Assets/_Project/Models/Environment/Stage1/alien_flower_cluster.fbx", "line": "", "issue": "Environment/Stage1 의 fbx·텍스처가 모두 snake_case 소문자로, 다른 에셋의 SF_/T_ 접두어 PascalCase 규칙과 어긋나 검색·일괄 처리 규칙이 깨진다(planet, rock_pillars 등 7종 동일).", "fix": "SF_Stage1_AlienFlowerCluster.fbx 형태로 일괄 리네임(Unity 내에서 리네임해 GUID 유지)"},
   {"severity": "low", "file": "METAGAME/Assets/_Project/Prefabs/Resources/SF_Rabbit_Base.prefab", "line": "", "issue": "프리팹 접두어가 PF_(Stage1 프리팹)와 SF_(Resources 프리팹)로 혼용되어 파일 종류를 이름으로 구분할 수 없다.", "fix": "프리팹은 PF_ 접두어로 통일하고 SF_ 는 소스 모델에만 사용"},
   {"severity": "low", "file": "METAGAME/Assets/_Project/Textures/Concept/Characters/SF_RabbitAstronaut_TPose_Front_v2.png", "line": "", "issue": "컨셉 아트 v1/v2/v3/Standalone 버전 잔여 파일이 함께 커밋되어 리포 용량과 혼동을 늘린다.", "fix": "최종 버전만 남기고 나머지는 삭제(필요하면 사내 드라이브로 이관)"}
 ],
 "questions": [
   "씬(.unity)을 리포에 두지 않고 Editor 빌더 스크립트로 생성하는 방식이 팀 합의인가요? 그렇다면 씬 사전 선언 규칙을 어떻게 대체할지 정해야 합니다.",
   "_Project/Editor/StageSceneSetup.cs 가 Scripts/Editor 의 Stage1PuzzleBuilder 와 기능이 겹치는지(둘 중 하나가 구버전인지) 확인 필요합니다."
 ]}
```

</details>
