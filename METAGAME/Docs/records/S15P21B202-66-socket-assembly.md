# S15P21B202-66 소켓 배치 및 적층 판정

- Epic: S15P21B202-14 [Puzzle] 퍼즐·상호작용 시스템
- 브랜치: `S15P21B202-66-puzzle` (`S15P21B202-64-puzzle` 에서 분기)
- 작성일: 2026-08-27

## 1. 목적 / 배경

빔으로 파츠를 들어 아무 곳에나 내려놓을 수 있을 뿐이었다. 우주선을 만들려면 파츠가
정해진 지점에서 맞물려야 하고 아래에서 위로 쌓이는 순서가 지켜져야 한다.

## 2. 확정 사항

### 2.1 배치는 하이브리드다

기획의 미정 사항이던 "자유 물리 배치와 스냅 중 어느 수준까지" 를 이렇게 정했다.

- 소켓 근처에서는 위치와 회전을 정렬해 붙인다.
- 소켓이 없는 곳에서는 자유 배치를 그대로 둔다.

자유 물리만 쓰면 4인이 3.7m 파츠를 손으로 맞추는 일이 되어 조립이 성립하지 않는다.
스냅만 쓰면 물리 놀이의 재미가 사라진다.

### 2.2 소켓 규약

`PartSocket` 을 파츠의 자식에 붙인다. `transform.forward` 는 접합면이 바라보는
바깥 방향이다. 위쪽 소켓은 위를, 아래쪽 소켓은 아래를 향한다.
두 소켓이 맞물리면 위치가 겹치고 두 forward 가 정반대가 된다.

콜라이더가 없다. 물리 쿼리 대신 `PartSocket.All` 목록을 훑는다.
소켓 수가 파츠당 몇 개뿐이라 목록 순회가 구체 캐스트보다 단순하고 결정적이다.

### 2.3 조립 순서는 "받을 수 있는 상태" 로 표현한다

```csharp
CanReceive = Role == Top
             && !IsOccupied
             && Owner.State is Placed or Welded
```

위쪽 소켓은 **소유 파츠가 이미 조립에 들어간 뒤에만** 받는다.
아직 고정쇠에 박혀 있거나(`Locked`) 떼어냈을 뿐인(`Free`) 파츠 위에는 쌓을 수 없다.
이 한 줄이 "아래에서 위로" 순서 규칙 전부다. 별도 검증 코드가 필요 없다.

### 2.4 후보 선택은 결정적이다

거리 → 각도 오차 → 월드 좌표 순으로 정렬한다.
마지막 기준으로 인스턴스 ID 를 쓰지 않는다. ID 는 머신마다 다르게 배정되어
같은 장면인데도 4인이 각자 다른 후보를 고를 수 있기 때문이다.
`Interactor` 와 `TractorBeam` 이 쓰는 원칙과 같다.

| 상수 | 값 | 의미 |
| --- | --- | --- |
| `SnapDistance` | 1.5m | 두 소켓이 이 거리 안이면 후보 |
| `SnapAngleDegrees` | 30° | 접합면이 마주보는 오차가 이 각도 안이면 후보 |

### 2.5 정렬 계산은 스케일에 영향받지 않는다

파츠의 `localScale` 이 1 이 아니므로(3 × 3.7 × 3) 로컬 좌표로 오프셋을 다루면
스케일이 두 번 곱해진다. 월드 오프셋을 회전 변화량만큼 돌리는 방식으로 계산했다.

```csharp
var deltaRotation = partRotation * Quaternion.Inverse(part.rotation);
var socketOffset = socket.position - part.position;      // 월드 단위
var partPosition = target.position - deltaRotation * socketOffset;
```

실측에서 두 소켓의 겹침 오차가 0.001m 미만, 층간 거리가 정확히 `PartHeight` 로 나왔다.

### 2.6 붙인 파츠는 끊어질 수 있는 조인트로만 잡는다

`FixedJoint` 의 `breakForce` / `breakTorque` 를 낮게 준다.
"아직 용접하지 않은 탑" 을 물리로 표현한 것이며, 운석에 무너지는 연출이 여기서 공짜로 나온다.

| 상수 | 값 | 비고 |
| --- | --- | --- |
| `UnweldedBreakForce` | 8000 N | **임시값** |
| `UnweldedBreakTorque` | 5000 Nm | **임시값** |

이 두 값이 이 시스템의 핵심 튜닝 대상이다. 운석 충격에는 끊어지고 일반 조작에는
끊어지지 않는 구간을 잡아야 하는데, **운석이 아직 없어서 지금은
"3단 탑이 스스로는 무너지지 않는다" 까지만 확인한 값이다.** 실제 값은 운석 티켓에서 확정한다.

### 2.7 조인트가 끊어졌는지 두 경로로 확인한다

`PartAssemblyLink` 가 조인트 하나를 관리한다.

1. `OnJointBreak` — 런타임에서 즉시 온다.
2. `Tick()` 의 조인트 생존 확인 — 콜백이 오지 않는 경우의 안전망이다.
   EditMode 의 `Physics.Simulate` 는 물리 콜백을 보내지 않으므로 테스트도 이 경로를 쓴다.

끊어지면 소켓 맞물림을 풀어 그 자리에 다시 쌓을 수 있게 한다.

링크의 소켓·조인트 참조는 **직렬화한다.** 저장하지 않으면 씬을 로드한 직후
`_joint` 가 null 로 보여 `Tick` 이 곧바로 맞물림을 풀어 버린다.
Sandbox Case7 을 씬에 저장해 보고 발견한 문제다.

### 2.8 빔이 다가가는 동안 미리 자세를 맞춘다

`TractorBeam` 이 매 스텝 후보를 갱신하고, 후보가 있으면 목표 회전을 정렬 회전으로 바꾼다.
파츠가 접근하는 동안 방향이 저절로 돌아가므로 플레이어가 회전을 맞출 필요가 없다.
S15P21B202-64 에서 미뤘던 목표 회전 조종을 이 용도로 넣었다.

놓는 순간 후보가 있으면 정렬 배치되고, 없으면 그 자리에 자유 배치된다.
후보는 `CurrentMatch` 로 노출한다. 미리보기(고스트) 표시는 UI 파트가 이 값을 읽어 그린다.

## 3. 작업 중 발견한 버그

`Free` 상태의 파츠를 조립하면 **상태가 `Placed` 로 올라가지 못하고 조용히 `Free` 로 남았다.**
`Assemble` 이 `Carried` 일 때만 상태 전환을 시도했기 때문이다.
그 결과 붙기는 붙었는데 그 위에 다음 층을 쌓을 수 없는 상태가 됐다.

Sandbox 에서 3단 탑을 만들 때 2단까지만 올라가고 3단이 붙지 않아 드러났다.
`Assemble` 이 조립 가능한 상태를 명시하고, `Locked` · `Free` 는 조용히 넘기지 않고
실패시키도록 바꿨다. 회귀 테스트 두 건을 함께 넣었다.

## 4. 산출물

코드
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/SocketRole.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartSocket.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/SocketMatch.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartAssembler.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/PartAssemblyLink.cs`
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/SpaceshipPart.cs` (소켓 목록)
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Parts/TractorBeam.cs` (목표 회전·스냅 연동)
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Physics/PhysicsScale.cs` (스냅·조인트 상수)
- `METAGAME/Assets/_Project/Scripts/Runtime/Gameplay/Physics/SandboxBeamDriver.cs` (이동 기능)

테스트
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/PartSocketTests.cs`
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/PartAssemblerTests.cs`
- `METAGAME/Assets/_Project/Scripts/Tests/Editor/SocketTestRig.cs`

에셋
- `METAGAME/Assets/_Project/Scenes/Sandbox_Physics.unity` (Case7 · Case8 추가)

## 5. 검증

EditMode 테스트 102건 전부 통과했다. 이번에 추가한 것은 29건이다.

| 영역 | 테스트 |
| --- | --- |
| 소켓 점유 | 연결·해제가 양쪽에 반영, 점유된 소켓은 다시 받지 않음 |
| 조립 순서 | Locked·Free 파츠 위에는 못 쌓음, Placed·Welded 위에는 쌓임, 아래쪽 소켓은 받지 않음 |
| 후보 판정 | 거리 초과 거부, 각도 초과 거부, 가까운 쪽 우선, 반복 호출 시 같은 결과, 점유된 소켓 제외 |
| 정렬 배치 | 두 소켓이 0.001m 미만으로 겹침, 층간 거리가 정확히 PartHeight |
| 조인트 | breakForce·breakTorque 설정, 해제 시 소켓 복구, 안전망 Tick 이 파괴 감지 |
| 상태 가드 | Free 파츠 조립 거부(회귀), 3단 연속 적층 |
| 빔 연동 | 다가가는 동안 목표 자세 정렬, 놓으면 조립, 후보 없으면 자유 배치 |

Sandbox Case7 · Case8 을 저중력에서 검증한 결과다. 실패 0건.

```
Case7 세 파츠 모두 Placed              Placed/Placed/Placed
Case7 조인트가 씬에 저장됨
Case7 링크가 조인트 참조를 유지
Case7 층간 간격이 파츠 높이와 일치        3.700 / 3.700 (기대 3.7)
Case8 빔이 옮길 파츠를 붙잡음            Grabbed
Case8 이동 중 스냅 후보가 잡힘           3.68초 시점
Case8 놓으면 조립됨
Case8 상태가 Placed                  Placed
Case8 정확히 한 층 위에 붙음            3.700 (기대 3.7)
Case8 위 소켓이 다시 받을 수 있음         다음 층을 쌓을 수 있다
Case7 3단 탑 30초 안정                최대 이동 0.0000m
Case7 조인트가 스스로 끊어지지 않음
Case8 조립된 파츠도 유지
```

소켓으로 조립한 3단 탑 (가운데)

![3단 소켓 탑](S15P21B202-66-socket-tower.png)

Sandbox 전경

![전경](S15P21B202-66-sandbox-all.png)

Case8 이 1장의 운반 루프 전 과정을 한 번에 보여 준다.
빔이 파츠를 집어 → 탑 위로 옮기고 → 다가가는 동안 자세가 정렬되고 → 놓으면 소켓에 붙는다.

## 6. 미해결 / 후속

- `UnweldedBreakForce` / `UnweldedBreakTorque` 실측 확정 — 운석 티켓
- 용접 진행도와 접합 확정 — 다음 티켓. 완료 시 조인트 상한을 올리거나 교체한다
- 스냅 미리보기(고스트) 표시 — UI 파트. `TractorBeam.CurrentMatch` 를 읽으면 된다
- 파츠 4종의 실제 소켓 배치와 조립 순서 — 기획 확정 후. 지금은 밑면·윗면 하나씩이다
- 파츠 하나에 링크는 하나다. 아래쪽 소켓이 둘 이상인 파츠가 필요해지면 확장한다
- 우주선 완성 판정 — 소켓 점유 현황을 세면 되므로 이 구조 위에 얹을 수 있다
- 네트워크 동기화 — S15P21B202-15

## 7. 참고

- 네임스페이스는 `Metagame.Gameplay.Parts`
- 소켓의 forward 규약을 어기면 후보가 잡히지 않는다. 윗면은 위, 밑면은 아래를 향한다
- 선행: S15P21B202-63, S15P21B202-64
