# 697 · 프레임 불안정 / 멀티 이동 누락 조사

조사일: 2026-09-22. 브랜치 `S15P21B202-697-qa`, 기준 `e1cbcaa9`.
워크트리: `C:/workspace/2-PJT/S15P21B202/.local-worktrees/697-qa`.

## 결론

**단일 네트워크 문제로 설명할 수 없다.** 솔로와 멀티에 공통인 CPU/물리/렌더 비용과, 멀티 전용 입력 전달·표시 지연이 별도로 존재한다. 현재 증상의 주원인이 어느 쪽인지는 같은 실행의 CPU/GPU/네트워크 측정이 필요하다.

코드로 확인한 주요 문제는 (1) 짧은 입력이 서버 물리 실행 전에 덮이는 구조, (2) 멀티 끼임 탈출 처리 누락, (3) 테두리의 매 프레임 검색·할당이다. 100Hz 래그돌 물리, 전체 화면 효과, 씬 메시·콜라이더 규모는 성능 측정 우선 후보다. **이 조사로 60FPS 달성이나 모든 오류의 부재를 보장하지 않는다.**

사용자 요청에 따라 화면 조작 없이 파일·설정·기존 로그를 읽었다. 게임 코드, 씬, 프리팹, 설정은 변경하지 않았다. 추가 파일은 이 보고서와 읽기 전용 분석 스크립트뿐이다.

## 기준과 검증 범위

- Unity 6000.5.4f1, URP 17.5.0, Input System 1.19.0 설치. 실제 입력 경로는 `Core/Controls.cs`의 legacy Input이다.
- Mirror는 `Assets/_Project/ThirdParty/Mirror`에 포함된다. Lobby의 전송 주기는 30Hz, 보간 bufferTimeMultiplier는 3, dynamicAdjustment는 활성이다. 초기 버퍼 설정만 환산하면 100ms이며 실제 지연은 적응 조정·전송·표시 단계에 따라 달라진다.
- 원격 697 참조와 현재 develop은 생성 당시 같은 커밋이었다. 기존 작업 폴더의 렌더 설정 수정 6개는 건드리지 않았다.
- 새 워크트리 체크아웃에서 기존 Mirror 폰트 3개의 LFS 포인터 불일치 경고가 있었다. 성능 원인 증거는 아니며 저장소 위생 항목이다. 임의 복구하지 않았다.
- 기존 `LocalLow/Not This Planet/Not This Planet/Player.log` 확인: Unity 버전 일치, RTX 4050 Laptop GPU, 참가자 실행 및 WorldSync 준비 기록 존재. `[PERF]`/`[NETDIAG]` 수치와 Exception 항목은 검색되지 않았다. 해당 빌드의 소스 커밋은 확인하지 못했으므로 697의 런타임 검증으로 사용하지 않는다. 개인정보가 포함된 로그 원문은 보고서에 복사하지 않았다.
- Unity 실행, 신규 컴파일, EditMode/PlayMode, 다중 클라이언트 재현, GPU 프로파일링은 수행하지 않았다. 따라서 아래의 “확인”은 코드/설정의 사실이며 사용자 현상의 재현 성공을 뜻하지 않는다.

아래 경로의 `Runtime/`은 `METAGAME/Assets/_Project/Scripts/Runtime/` 기준이다. 줄 번호는 조사 기준 커밋에 해당한다.

## 우선순위별 결과

### N1. 서버가 짧은 입력을 덮어쓸 수 있다 — 높음 / 구조 확인, 실제 발생 미재현

근거: `Runtime/Network/NetPlayer.cs:1310–1320`의 `[Command] CmdSendInput`은 `pending = frame`만 수행한다. `:1075–1120`에서 물리 스텝마다 pending 하나만 소비한다. Mirror `Core/NetworkServer.cs:934`는 도착한 메시지를 반복 처리하며 `NetworkEarlyUpdate`는 Update/FixedUpdate보다 앞선다.

예: 지연으로 E 누름(seq=1, true)과 다음 프레임 해제(seq=2, false)가 한 물리 스텝 전에 함께 처리되면, 서버가 실행하는 값은 false뿐이다. `Core/Controls.cs:223,234`의 E와 핑은 GetKeyDown/GetMouseButtonDown이므로 특히 취약하다. 짧은 이동·점프 입력도 모두 그 사이에 끝났다면 관측되지 않는다. **계속 누르고 있는 이동이 영구히 무시되는 현상까지 이 문제로 설명할 수는 없다.**

`scripts/qa/audit_697.py`로 위 대입 순서를 모델링하여 첫 동작이 소비되지 않는 반례를 확인했다. 이는 실제 C# 실행/통신 재현 테스트가 아니다.

수정 방향: 지속 상태(Move/Run/hold)와 1회 사건(E/핑 등)을 분리하고, 사건 순번/소비 여부를 서버에서 관리한다. 오래된 이동을 모두 큐에 쌓아 재생하면 지연이 누적되므로 피한다. 잡기·해제·탑승 토글은 처리 순서도 보존해야 한다.

검증: 1프레임 E/핑, 짧은 점프와 이동, 100/200ms 지연, burst 전달, 호스트 프레임 정지, 중복 사건, 탑승/하차 연타. 전송→수신→물리 소비 순번을 별도로 기록한다.

### N2. “20Hz 입력”은 실제 상한이 아니며 신뢰 채널을 사용한다 — 높음 / 설정 확인, 병목 가능

근거: `NetPlayer.cs:30,1361–1399,1459–1493`. `if (!changed && sendTimer < SendInterval) return`이므로 변경 중에는 매 Update 전송할 수 있다. Same은 마우스 Look까지 0.0001 제곱 오차로 비교한다. 상수는 정지 상태의 재전송 간격에 가깝다. 실제 패킷 수는 Mirror batching/sendRate에도 영향을 받으므로 렌더 FPS와 동일하다고 단정하지 않는다.

`CmdSendInput`의 기본 채널은 Reliable이다(`Mirror/Core/Attributes.cs:25–28`). 플레이어 프리팹도 `NetworkTransformReliable`이다(`Prefabs/Network/PF_NetPlayer.prefab:65`). 이동 입력과 플레이어 자세는 손실 시 재전송 대기의 영향을 받을 수 있다. WorldSync의 소품 자세는 이미 Unreliable이므로, “소품은 움직이는데 특정 사람은 뒤늦게 움직임”도 구분해서 측정해야 한다.

수정 방향: 이벤트 보존을 먼저 해결하고, 지속 입력의 전송 빈도 제한·sequence·유효기간을 설계한다. 신뢰 채널을 무조건 Unreliable로 바꾸면 짧은 입력이 패킷 손실로 사라지는 회귀가 생긴다. 채널 변경은 프로토콜/프리팹 호환까지 검증한다.

또한 pending에 수신 시간/만료 처리가 없어 마지막 Move가 이후 입력 도착이나 연결 종료까지 유지된다. 일시적 단절에서 “키를 놓았는데 계속 이동”이 가능한 별도 위험이다.

### N3. 예측 보정은 입력 순번만 맞추고 시뮬레이션 시각은 맞추지 않는다 — 중간 / 유력 위험

근거: `NetPlayer.cs:1060–1065,1387–1392,1428–1451`, `RemoteAvatar.cs:282–297`.

클라이언트는 송신 당시 골반 위치를 저장한다. 서버는 마지막 적용 순번과 현재 골반 위치를 되돌린다. 같은 순번이 여러 물리 스텝 동안 유지될 수 있고, echo 주기는 물리 스텝으로 양자화된다. 따라서 같은 seq라도 같은 시각/같은 물리 스텝의 상태라는 보장이 없다. 특히 변경이 적어 20Hz keepalive로 보내는 동안 echo가 먼저 도착할 수 있다. 첫 echo 이후 같은 seq는 버리며 보정은 골반에 적용된다.

저 FPS, 지연 변동, 밀기·잡기·움직이는 부품 접촉에서 위치 보정이 체감 저항/떨림으로 나타나는지 확인해야 한다. 주석만으로 “같은 순번이라 오차가 정확하다”고 판단하면 안 된다. 이미 3m snap 구분과 오래된 echo 거절은 존재한다.

검증: 송신 위치·서버 소비 tick·echo 위치·보정 벡터를 함께 기록하고 평지 정속 보행에서부터 비교한다. 보정률만 0으로 내리면 시각적 끌림 대신 영구 오차가 남으므로 근본 수정으로 간주하지 않는다.

### N4. 멀티에서는 T 끼임 탈출 호출 경로가 없다 — 높음 / 코드 연결 누락 확인

근거: `Bootstrap/SpaceBootstrap.cs:194`는 클라이언트 또는 서버가 활성일 때 `RecoveryFromServer = true`로 둔다. `Gameplay/GameSession.cs:237`은 그 값이 false일 때만 `Escape.Tick(... StuckEscapeHeld)`를 호출한다. `StuckEscape`는 MonoBehaviour가 아니고 별도 Tick 호출이 필요하다. Network 코드에는 이 입력을 소비하는 서버 탈출 경로가 없다. `NetPlayer.Same`에도 이 필드 비교가 없다(단, keepalive로 필드가 전송될 수 있으므로 비교 누락만이 원인은 아니다).

영향: 네트워크 플레이에서 끼임 복구가 작동하지 않아 “입력이 안 먹는” 상황을 더 오래 지속시킬 수 있다. 호스트도 위 RecoveryFromServer 조건의 영향을 받는다. R 개인 리스폰은 별도 Command가 있어 이 문제와 구별한다.

수정 방향: 각 플레이어의 서버 권한 StuckEscape 상태와 3초 hold 판정을 연결하고, 진행도/안내만 소유자에게 전달한다. 기절 중 부활 금지, 장비 제자리 드롭, 가까운 안전 지면 탐색을 유지한다. 클라이언트에서 임의 Teleport로 우회하면 서버 보정과 충돌한다.

### P1. 부품 테두리가 매 프레임 검색·할당을 반복한다 — 높음 / 반복 작업 확인, 프레임 영향 미측정

근거: `Gameplay/Parts/PartOutlineRendererFeature.cs:51–64`, `PartOutlineTargets.cs:57–100`, `PartOutlineGroups.cs:46–57,75,105–110`.

프레임당 한 번 캐시하는 것은 여러 카메라 간 중복만 막는다. 다음 프레임에는 다시 FindObjectsByType로 모든 SpaceshipPart를 검색하며, 그룹 계산에서 List/Dictionary/배열을 생성한다. 아직 용접된 그룹이 없어서 테두리가 안 그려져도 검색·그룹 계산은 수행된다. 용접 후에는 하위 Renderer 목록도 다시 순회한다.

수정 방향: 활성 부품 등록/해제, 용접 연결·끊기, 씬 전환에서 목록과 그룹을 갱신한다. 재사용 버퍼만 도입해도 할당을 줄일 수 있다. 갱신 누락 시 용접이 끊어져도 연결된 테두리가 남으므로 해당 회귀를 검증한다.

### P2. 100Hz 물리 + 래그돌 + 반복 상호작용 질의 — 높음 / 구성 확인, 병목 미측정

근거: `Core/Tuning.cs:19`, `Bootstrap/SpaceBootstrap.cs:92`, `ProjectSettings/DynamicsManager.asset:13–14`, `Gameplay/Player/RagdollBuilder.cs:365–374`, `PlayerRig.cs:2616–2620,2685`, `GameSession.cs:266`.

물리 간격은 0.01초이고 기본 solver iteration은 14/4다. 래그돌 생성 시 각 몸통/팔다리 Rigidbody에 ContinuousDynamic을 지정한다. 로컬 상호작용 탐색은 GameSession.Update와 PlayerRig.FixedUpdate에서 모두 수행되므로 60FPS에서는 정상 조건에서도 초당 약 160번 호출될 수 있다. 내부는 여러 Raycast/후보 탐색을 포함한다. 이 수치는 호출 빈도이지 소요 ms가 아니다.

원격 캐릭터도 `RemoteAvatar.cs:233–253`에서 골반 이외의 강체는 동적이며 관절 표현을 유지한다. 충돌을 끄더라도 모든 물리 계산이 없어지는 구조는 아니다. `Network/TeamSeats.cs:205–213,329–356`가 빈 자리에 AI 몸을 채우므로 **네트워크 모드의 사람 1명은 캐릭터 1개 비용과 다르다.** 오프라인 솔로에는 이 설명을 그대로 적용하지 않는다.

`TimeManager.asset`의 Maximum Allowed Timestep은 약 0.333초다. 큰 지연 뒤 여러 FixedUpdate가 한 렌더 프레임에 몰릴 수 있으므로 물리 부하와 프레임 지연의 연쇄를 조사해야 한다.

수정 방향: 먼저 물리 simulate/질의/스크립트 시간을 분리 측정. 표시용 원격 몸 비용, 중복 focus 질의, 충돌 모드를 차례로 실험한다. 50Hz 전환이나 solver 일괄 축소는 고정못·관절·용접·협동 밀기 안정성을 바꿀 수 있어 첫 조치로 적용하지 않는다.

### P3. PC 렌더 경로의 여러 전체 화면 효과 — 높음 / 설정 확인, GPU 병목 미측정

근거: `Assets/Settings/PC_Renderer.asset:71–133`, `PC_RPAsset.asset:22–29,45–66`.

PC 경로는 깊이/불투명 텍스처, 원해상도 SSAO(Downsample=0), 4개 그림자 cascade, 추가 광원 그림자, 안개와 두 테두리 feature를 활성화한다. 실제 각 feature는 대상/상태에 따라 실행을 생략할 수 있다. MSAA는 1이고 SRP Batcher는 활성이다. “모든 최적화가 꺼져 있다”는 결론은 맞지 않는다.

`AtmosphereHazeRendererFeature.cs:83–100`은 카메라 색 복사 후 전체 화면 합성을 한다. `PartOutlineRendererFeature.cs:105–145`는 화면 크기 마스크와 합성을 사용한다. 부품을 적게 그려도 화면 해상도에 따른 비용이 남을 수 있다. 반면 가려진 상호작용 테두리에는 카메라 절두체 필터와 화면 영역 제한이 이미 있다.

`QualitySettings.asset`에서 High/Very High/Ultra는 같은 PC RPAsset을, 하위 3개는 같은 Mobile RPAsset을 참조한다. 따라서 Ultra→High만으로 URP feature/그림자 구성이 모두 가벼워질 것으로 기대하면 안 된다. Mobile 경로는 renderScale=0.8이고 일부 기능이 꺼지므로 비교 실험에 쓰되, 용접 연결 시각 안내가 사라지는 기능 차이를 기록한다.

검증: 같은 카메라/해상도에서 SSAO, 안개, 테두리, 그림자를 하나씩 비교하고 GPU ms를 기록한다. 효과를 한꺼번에 끄고 빨라진 것만으로 원인을 특정하지 않는다.

### P4. 씬 메시/콜라이더 규모와 가림·거리 처리 확인 필요 — 중간 / 정적 집계 확인

`scripts/qa/audit_697.py`의 직접 직렬화 기록 집계:

| 씬 | MeshRenderer | MeshCollider | 프리팹 인스턴스 | 직접 LODGroup | Occlusion 데이터 참조 |
|---|---:|---:|---:|---:|---|
| Stage1_Astra | 438 | 524 | 875 | 0 | 없음 |
| Stage2_Earth | 272 | 0 | 1,517 | 0 | 없음 |
| PracticeLobby | 206 | 0 | 0 | 0 | 없음 |

**위 표는 런타임 총수나 draw call 수가 아니다.** 비활성 객체·stripped 기록이 포함될 수 있으며 프리팹 내부/FBX 내부/런타임 생성은 펼치지 않았다. 직접 LODGroup 0만으로 프리팹 내부까지 LOD가 전혀 없다고 판단할 수 없다. Stage1에는 static flag가 설정된 객체도 있으므로 static batching 미사용으로 단정하지 않는다.

메시별 삼각형 수, 실제 보이는 submesh/material pass 수, 인스턴싱 적용 여부, shadow caster 비용은 아직 미측정이다. 환경 메시와 충돌 메시를 분리하여 멀리 있는 장식의 LOD/거리 제거, 복잡한 충돌 메시 단순화를 검토한다. 큰 메시로 무조건 결합하면 가림/거리 제거 단위가 커지고, 콜라이더 삭제는 잡기·등반·발판을 깨뜨릴 수 있다.

### P5. 사운드와 진단 경로의 반복 검색·문자열 — 중간 / 코드 확인

`Gameplay/Audio/SoundRuntime.cs:231–269`는 매 Update AudioListener를 검색하며 0.5초마다 차량·용접 도구·모든 Rigidbody를 검색한다. 신규 강체에는 emitter를 추가한다. `NetPlayer.ReportAim:1219–1240`은 상태가 바뀌었는지 비교하기 **전에** 문자열을 만들며 서버 FixedUpdate에서 매번 호출된다.

수정 방향: AudioListener/소리 대상 등록 및 씬 전환 시 재연결, 조준 상태는 값으로 먼저 비교 후 로그 문자열 생성. 신규 운석/도구의 충돌음 등록과 리스폰 후 사운드 유지가 회귀 검증 대상이다. 0.5초 주기 spike와 GC.Alloc 상관을 측정한다.

## 의심을 낮춘 항목 / 남은 오류 위험

- 팀 초상화: `UI/TeamPortrait.cs:23–26`은 같은 rig·외형·유효 텍스처이면 즉시 반환한다. BakeMesh가 있어도 매 프레임 실행되는 것은 아니다. 최초 입장/외형 변경 순간의 spike는 별도로 측정한다.
- WorldSync: 소품 자세는 Unreliable + sequence 검사, 대상 수 불일치 방어, MTU 초과 시 일부 경로 fallback이 있다. 모든 물체를 매 물리 스텝 reliable 전송한다는 가설은 코드와 다르다. 수신 배열 할당 및 전체 상태 전송 비용은 실제 트래픽과 함께 측정할 항목이다.
- 가려진 테두리: 대상 목록은 0.5초 갱신하고 카메라 절두체로 거른다. PartOutline의 프레임당 검색과 구별해야 한다.
- 운석 ghost 재사용 및 콜라이더 캐시 관련 기존 테스트(`AssaultFrameCostTests`)가 있다. 이번에 실행하지 않았으며 실제 서버 운석 생성 비용까지 검증된 것으로 해석하지 않는다.
- `NetPlayer`/`PlayerRig`의 FixedUpdate 실행 순서는 명시되어 있지 않다. 입력 적용→Tick→echo 관계는 실제 실행 순서를 기록해서 검증해야 한다. 이것만으로 장시간 정지 원인이라고 보지는 않는다.
- 클라이언트 로컬 몸도 NetworkDriven일 때 SpaceSuit는 네트워크 locomotion 상태를 사용한다(`SpaceSuit.cs:254–265`). 물리 예측은 즉시 이동해도 걷기/스윙 표현은 지연될 수 있으므로 “위치가 멈춤”과 “동작 애니메이션이 안 보임”을 분리한다.
- 신규 컴파일/실행을 하지 않았으므로 NullReference, 리트라이/씬 재진입·호스트 이관의 전체 오류 부재는 검증하지 않았다. 기본 입력 차단(CinematicInputBlocked, InputSuppressed), downed/riding/작업대 잠금도 이동 정지 시 함께 기록해야 한다.

## 60FPS 판정과 다음 측정

60FPS의 프레임 예산은 약 16.67ms다. FPS 제한을 60으로 설정해도 처리 시간을 줄여 주지는 않는다. 평균 60만으로 “최소 60 유지”를 통과시키지 않는다. 최저 사양·출력 해상도·품질·호스트/참가자 역할을 고정한 뒤 각 클라이언트에서 별도 판정한다.

1. 오프라인 솔로 / 1명 네트워크+빈자리 AI / 2명 / 4명을 나누어 실행한다. 사람이 늘어서 느려졌는지, 처음부터 AI까지 4몸이었는지 구별한다.
2. 동일 구간에서 30초 준비 후 120초씩 3회: 정지→걷기/회전→동시 밀기→용접/잡기→운석/폭풍. 씬 로딩은 별도 구간으로 보고한다.
3. CPU main/render thread, GPU ms, Physics.Simulate·FixedUpdate 횟수, GC.Alloc/GC, batches/setpass/triangles, 실제 캐릭터·운석 수를 수집한다. 그래픽 없는 batch 실행으로 GPU 성능을 판정하지 않는다.
4. 네트워크는 RTT/jitter/loss와 입력 송신/수신/소비 seq·시각, 마지막 입력 나이, 보정 오차, 이동 차단 상태를 같은 시간축에 남긴다. 민감한 사용자 신원 대신 임시 peer/slot 번호를 쓴다.
5. 16.67ms 초과 프레임 비율, p95/p99, 최악 프레임을 각 PC별로 보고한다. 안정적인 최소 60 요구라면 정상 플레이 구간의 초과 프레임을 실패로 다루고, 평균/p99만 통과해도 보장 달성이라고 표현하지 않는다. 프로파일러 부하가 없는 배포 빌드에서도 재확인한다.

분리 실험: 해상도/SSAO만 낮춰 GPU ms가 감소하면 렌더 후보를 지지한다. 해상도와 무관하고 Physics 시간이 높으면 물리 후보를 지지한다. 프레임은 정상인데 특정 peer의 입력 나이·보정 오차가 커지면 네트워크 경로를 우선한다. 호스트가 느릴 때 모든 참가자가 영향받으면 서버 물리/전송 지연의 연쇄를 확인한다.

기존 계측은 `Core/PerfLog.cs:77–84`에서 실험 빌드에만 설치된다. `Network/NetDiagnostics.cs`의 `-netdiag` 옵션과 함께 쓰면 평균/최저/p95·통신 상태의 출발점이 된다. CPU/GPU 비용 구분과 p99/초과 비율은 추가 계측 또는 Profiler 캡처가 필요하다.

## 수정 순서와 회귀 범위

1. N1·N4의 입력 사건 보존/서버 탈출 처리: E·핑·점프·잡기/해제·기절·R/T 차이·호스트/참가자를 검증한다.
2. P1·P5의 검색·할당 제거: 용접 연결/분리, 리스폰·재접속·씬 재진입, 동적 운석 소리와 UI 표시를 검증한다.
3. 측정으로 CPU/GPU 분리 후 P2 또는 P3/P4에 집중한다. 한 번에 하나만 바꿔 전후 수치를 비교한다.
4. N2·N3의 전송/예측 조정은 지연·손실 주입과 여러 PC로 검증한다. 협동 밀기·탑승·던지기·기절/구조·순간이동·호스트 이관을 포함한다.

이 순서는 리스크와 조사 효율 기준이며, 각 수정으로 얻을 FPS 향상 폭은 아직 알 수 없다.
