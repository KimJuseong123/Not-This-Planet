# [Infra] 릴레이 실패 원인 규명 — Edgegap 서버 검증이 동작하지 않는다 (S15P21B202-298)

[문서 안내](../README.md) → [작업 기록](README.md) → 네트워크

Edgegap 릴레이 연결 실패를 핸드셰이크와 반복 실험으로 조사한 기록입니다. 제외한 원인, 관측 결과, 진단 도구와 자체 릴레이로 이어지는 권고를 남깁니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

| 항목 | 내용 |
| --- | --- |
| 담당 | 금준영 |
| 기간 | 2026-09-04 |
| Jira | [S15P21B202-298](https://ssafy.atlassian.net/browse/S15P21B202-298) |
| 진단 대상 | [S15P21B202-279](https://ssafy.atlassian.net/browse/S15P21B202-279) (최재현) — 막힘 |
| 도구 | [`scripts/diagnostics/relay_probe.py`](../../scripts/diagnostics/relay_probe.py) |
| 관련 | [-145 동기화 설계](S15P21B202-145-sync-design.md) · [-162 게임 서버 운영](S15P21B202-162-game-server-ops.md) |

<a id="결론부터"></a>

## 변경 배경과 결과

**우리 코드·우리 망·핑 형식의 문제가 아니다.** Edgegap Distributed Relay의 **서버 검증**이 우리 세션에서 동작하지 않는다. 릴레이는 답을 하지만 방장을 세션의 서버로 인정하지 않고, 30초 주기로 재시도하며 계속 실패한다. Edgegap API는 그 내내 `status=Linked · error=null`로 자기 쪽이 정상이라고 본다.

## 0. 이 진단이 가능해진 이유 — 핸드셰이크는 UDP 9바이트다

Unity 로만 확인하려면 빌드 둘·기계 둘·다른 망이 필요하고, 실패해도 `[RelayWatch] 서버=Checking` 한 줄만 남는다. 그런데 릴레이 핸드셰이크는 이게 전부다.

```
핑:  userId(uint32 LE) + sessionId(uint32 LE) + 0x01      (9바이트)
답:  messageType(1) + state(1)                            (2바이트)
state: 0 Disconnected · 1 Checking · 2 Valid · 3 Invalid · 4 SessionTimeout · 5 Error
```

`EdgegapKcpServer.SendPing()` / `RawReceiveFrom()` 그대로다. 파이썬 40줄로 흉내 낼 수 있고, **다른 망도 Unity도 필요 없다** — 릴레이 경로는 양쪽이 밖으로 나가는 연결이라 같은 기계에서도 핸드셰이크가 동일하다.

> 다른 망이 영향을 주는 곳은 **백엔드가 릴레이 세션을 만들지 말지 정하는 판별 하나**뿐이다(`decision=direct(same network)`). 그건 강제하면 되는 것이고, 핸드셰이크 진단과는 무관하다.

## 1. 가장 중요한 정정 — `Checking` 은 "답이 없다" 가 아니다

인계 문서에 이렇게 적혀 있었다.

> 방장은 릴레이 서버 포트로 0.5초마다 핑을 보냅니다 — `[RelayWatch] 서버=Checking` 이 30초 내내 그대로
> **그런데 답이 한 번도 오지 않습니다** (Checking = 보내는 중, 검증 대기)

**릴레이는 답한다.** 300초 담금에서 **260회** 응답했고 전부 `Checking` 이었다.

Mirror는 두 경우를 구분하지 않는다. `state` 초기값이 `Checking` 이고 답을 받을 때만 바뀌므로, **"답이 없음" 과 "릴레이가 Checking 이라고 답함" 이 화면에 똑같이 보인다.**

이 구분이 없어서 조사가 "패킷이 안 도착한다" 쪽으로 몰렸고, 지운 원인 6개가 대부분 그 방향이었다. 진단 로그가 **응답 횟수를 세지 않은 것**이 원인이다.

## 2. 지운 원인

인계 문서의 6개에 더해 오늘 지운 것들. **다시 의심하지 마세요.**

| 의심 | 어떻게 지웠나 |
| --- | --- |
| Mirror의 연결형 UDP 소켓이 답을 버린다 | 아님 · 답의 출발지가 보낸 곳과 **같다**(`relay:31927`). `Start()`가 `relaySocket.Connect()` 뒤 `Receive`를 쓰므로 다른 포트면 커널이 조용히 버리는데, 그 경우가 아니다 |
| Edgegap이 플레이어 호스팅 서버를 서버 포트에서 안 받는다 | 아님 · **한 번은 `Valid`가 됐다**(25세션 중 1회) |
| 출발 IP와 토큰을 맞춰 본다 | 아님 · **남의 토큰**을 우리 IP에서 보내도 `Valid`가 됐다 |
| 릴레이 노드에 따라 갈린다 | 아님 · `172.237.5.224` · `172.237.39.172` 두 노드에서 8세션 전부 실패 |
| 같은 IP 두 명이 원인 | 아님 · 서로 다른 IP 로도 실패 |
| 핑 형식(필드 순서·엔디언·13바이트 패딩) | 아님 · 5종 전부 동일하게 `Checking`. **릴레이의 답은 우리가 보낸 신원과 무관하다** |
| 검증이 느린 것 | 아님 · **300초** 기다려도 안 바뀐다 |
| 세션이 `Linked`가 아니다 | 아님 · API가 300초 내내 `status=Linked · ready=true · linked=true · error=null` |
| 백엔드가 세션을 너무 일찍 닫는다 | 아님 · `close()`는 **호출되지 않는다**(§5) |
| 토큰이 uint32를 넘어 잘린다 | 아님 · `NetworkSession.ToToken()`이 범위를 벗어나면 0 으로 만들고 `Relay.IsUsable`이 직접 접속으로 되돌린다 |

## 3. 관찰된 사실

**릴레이의 답은 신원과 무관하다.** 형식 5종(리틀·빅엔디언 × 필드 순서 × 13바이트)을 다 바꿔도 같은 `Checking`이 온다. 즉 이 답은 패킷 검증 결과가 아니라 **"이 세션에 검증된 서버가 없다" 는 세션 상태**다.

**릴레이는 30초 주기로 검증을 재시도한다.** 클라이언트 상태가 정확히 30초마다 진동한다.

```
  0.0초 클라이언트 → Checking
  2.5초 클라이언트 → SessionTimeout
 30.8초 클라이언트 → Checking      ← 재시도
 32.9초 클라이언트 → SessionTimeout
 61.6초 → Checking · 63.6초 → SessionTimeout
 92.4초 → Checking · 94.4초 → SessionTimeout   … 계속
```

서버 쪽은 260회 내내 `Checking` 한 줄이었다.

**토큰은 생성 시점과 `ready` 시점이 다르다.** `POST` 직후(`status=Seeking`)의 토큰은 4자리이고, `ready` 뒤에는 9~10자리다. 우리 백엔드는 `ready` 응답에서 읽으므로 맞다 — 다만 `POST` 응답의 토큰을 쓰면 안 된다는 것을 적어 둔다.

## 4. 도구

```bash
export EDGEGAP_RELAY_TOKEN=…      # 또는 --env-file ~/metagame/.env (EC2 기본값)
python3 scripts/diagnostics/relay_probe.py once                  # 서버·클라이언트 30초
python3 scripts/diagnostics/relay_probe.py soak --seconds 300    # 담금
python3 scripts/diagnostics/relay_probe.py formats               # 핑 형식 5종
python3 scripts/diagnostics/relay_probe.py nodes --rounds 8      # 노드별
python3 scripts/diagnostics/relay_probe.py raw                   # 세션 응답 전문
```

**EC2에서 돌리는 것을 권한다** — 공인 IP가 고정(`43.201.57.117`)이라 NAT 회전 변수가 없다. 사용자 PC 에서도 돌려 비교하면 IP 관련 가설까지 함께 갈린다.

`once`는 마지막에 읽는 법을 찍는다.

```
답은 17회 오는데 계속 Checking → **릴레이가 답은 하지만 서버를 인정하지 않는다.**
Mirror 화면에서는 '무응답' 과 구분되지 않는다(초기값이 Checking 이라서).
```

**토큰 값은 절대 출력하지 않는다** — 자릿수와 uint32 범위 여부만 찍는다. 세션은 매번 만들고 끝나면 지운다.

## 5. 곁가지로 나온 것 두 개

**① 릴레이 세션이 새고 있다.** `EdgegapRelayService.close()`는 있는데 **호출하는 곳이 없다.** `RelaySession` 레코드가 `session_id`를 들고 있지 않아서 부를 수도 없다.

```java
public record RelaySession(String address, int serverPort, int clientPort,
                           long sessionToken, Map<Long, Long> userTokens) { }
//                         ↑ session_id 가 없다
```

무료 등급의 동시 세션·월 세션 한도가 **문서에 없다**(`-145`에 "상용 릴레이(Edgegap 등) | **계정·요금 확인**" 으로 확인 항목만 남아 있다). 한도가 있으면 유저 테스트 중에 조용히 막힌다. 대시보드에서 등급을 확인해 여기에 적어야 한다.

**② `RelayWatch`가 오해를 만든다.** `Checking`만 찍어 "답이 없다" 로 읽혔다. **응답 횟수를 함께 찍으면** 두 경우가 구분된다.

```
[RelayWatch] 릴레이 상태 · 서버=Checking · 답 0회    ← 패킷이 못 간다
[RelayWatch] 릴레이 상태 · 서버=Checking · 답 17회   ← 릴레이가 우리를 모른다
```

한 줄 차이로 원인 6개를 헛짚는 일을 막을 수 있다. `-279`를 이어받는 사람이 먼저 넣을 값이 있다.

## 6. 다음 (권고)

| 순서 | 무엇 | 왜 |
| --- | --- | --- |
| 1 | **Edgegap 문의** | 이제 근거가 있다. 세션 id · 30초 주기 · 형식 5종 · 300초 담금 · 25세션 중 1회 성공을 붙여 보낸다 |
| 2 | **동시에 EC2 전용 서버 경로 복구** | 다른 망 플레이를 릴레이 없이 굴린다(`-server` 인자, 이미 되던 길). 답변을 기다리며 유저 테스트를 막지 않는다 |
| 3 | Edgegap이 못 고치면 **EOS 교체** | 에픽 릴레이. Unity 6000.5 / Mirror 96 호환 실측이 선행 |
| 4 | `RelayWatch`에 응답 횟수 | §5-② · 한 줄 |
| 5 | 세션 정리 + 등급 확인 | §5-① |

**유저 테스트를 한 장소에서 하면 지금 상태로 가능하다**(같은 망 직접 접속). 릴레이가 시연 일정을 막지 않게 2번을 함께 준비하는 것이 안전하다.

## 7. 이 기록이 다루지 않는 것

- **Edgegap이 왜 검증에 실패하는지** — 릴레이 내부는 볼 수 없다. 1번(문의)이 그것을 위한 것이다
- **EOS로 바꾸는 작업** — 별 이슈
- **같은 망 판별** — `-264`에서 이미 고쳤다(각자 랜 주소를 알려 주고 둘 중 하나만 맞으면 같은 망)
