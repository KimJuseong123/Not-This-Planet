# S15P21B202-162 — 게임 서버 운영 절차 (EC2)

[문서 안내](../README.md) → [작업 기록](README.md) → 서버·배포

EC2의 Mirror 전용 게임 서버를 빌드·전송·실행하고 포트와 로그를 확인하는 운영 기록입니다. 이후 리슨 서버·릴레이로 매치 경로가 변경되었으므로 현재 구성은 -145·-299와 대조합니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

Mirror 게임 서버를 EC2에서 돌리는 절차. 빌드 → 전송 → 실행 → 방화벽 → 로그까지 한 장.

`-145 §4`의 결정을 그대로 따른다. **내 PC는 서버가 아니다**(§4-6) — 이슈 제목의
"내 PC에서 운영" 은 EC2를 받기 전에 쓴 것이고, 지금은 EC2가 서버다.

## 0. 한눈에

| 무엇 | 어디 |
| --- | --- |
| 로비·인증 (Spring) | EC2 · `https://j15b202.p.ssafy.io` (nginx 443 → 127.0.0.1:8080) |
| **게임 서버 (Mirror)** | **EC2 · UDP 17777** |
| 게임 서버 실행 파일 | `/opt/metagame/MetaGameServer.x86_64` |
| 로그 | `journalctl -u metagame -f` |

게임 서버가 Spring과 **같은 호스트**여야 한다. 로비가 "게임 시작" 을 누를 때
로비에 입력된 주소의 호스트를 그대로 게임 서버 주소로 쓰기 때문이다
(`LobbyBootstrap.EnterMatch`).

## 1. 포트 — 17777/UDP

`-145 §4-3`이 정한 값. **세 곳이 같아야 한다.**

| # | 어디 | 값 |
| --- | --- | --- |
| ① | EC2 방화벽 | `sudo ufw allow 17777/udp` |
| ② | `Lobby.unity`의 `KcpTransport.port` | `17777` |
| ③ | 클라이언트 | ②와 같은 빌드를 쓰므로 자동 |

②는 이 브랜치에서 `7777` → `17777`로 바꿨다. 7777 은 Mirror 기본값이고
EC2 지급 안내가 기본 포트 변경을 요구한다. **하나만 바꾸면 조용히 접속에 실패한다.**

## 2. 서버 빌드 (내 PC, 수동)

`-145 §4-4`는 CI(GameCI)에서 만드는 것을 목표로 하지만 그것은 `-143` 이다.
지금은 수동이다.

### 2-1. 먼저 — Unity 모듈이 필요하다 (2026-09-01 확인)

이 PC 에는 **Linux 빌드 모듈이 온전하지 않다.** 빌드하면 스크립트 컴파일과 씬
빌드까지 다 지나간 뒤 마지막에서 넘어진다:

```
[Postprocess built player] UnityException: Build target 'StandaloneLinux64' not supported
```

`PlaybackEngines/LinuxStandaloneSupport/Variations`를 보면 **서버 플레이어 파일은
있는데**(`linux64_server_nondevelopment_mono` 70 MB 등) 일반 플레이어 쪽은 200 KB
남짓한 껍데기다. 즉 설치가 반쯤 되어 있다.

**해결** — Unity Hub → Installs → `6000.5.4f1` → ⚙ → Add modules →
**Linux Build Support (Mono)**만 설치한다. 설치 중에는 Unity를 닫아야 할 수 있다.

> **Dedicated Server 모듈은 쓰지 않는다.** Unity 6.5 이상에서는 Hub 가 설치를 막는다
> (2026-09-01 확인). 필요도 없다 — Dedicated Server 는 그래픽 코드를 덜어내는
> 최적화판일 뿐이고, **일반 Linux 빌드를 `-batchmode -nographics` 로 띄우면 똑같이
> 서버로 동작한다.** 우리 코드(`MirrorLaunchArgs`)가 이미 실행 인자로 역할을 정하는
> 방식이라 빌드 종류를 가리지 않는다. 지금 Windows 검증도 같은 방식으로 하고 있다.
>
> 대신 서버 빌드에도 셰이더·텍스처가 들어가 용량이 커지고 메모리를 조금 더 쓴다.
> EC2 는 15 GiB 중 1.3 GiB 만 쓰고 있어 여유가 있다. 정 아쉬우면 나중에 CI(`-143`)의
> GameCI 이미지에서 Dedicated Server 로 만들면 된다 — 그 리눅스 컨테이너에는 모듈이 있다.

> **함정**: 이 상태에서 빌드하면 `BuildReport.summary.result` 가 **`Succeeded` 로 나온다.**
> MCP 빌드 도구도 "Build succeeded" 를 찍는다. 그래서 **산출물 파일이 실제로 생겼는지
> 반드시 눈으로 확인해야 한다** — 폴더가 비어 있으면 실패한 것이다.
> 진짜 원인은 `report.steps[].messages` 에만 나온다.

### 2-2. 빌드

- Unity 빌드 타깃: **Linux (StandaloneLinux64)**, 스크립팅 백엔드 Mono.
  서브타깃은 `player` 그대로 둔다 — 서버 노릇은 실행 인자가 정한다.
- 산출물: `MetaGameServer.x86_64` + `MetaGameServer_Data/` + `UnityPlayer.so`
- 첫 빌드는 플랫폼 전환 때문에 에셋 재임포트가 일어나 10 분 넘게 걸린다.

## 3. 전송

**내 PC 의 PowerShell**에서 (EC2 안이 아니다 — 프롬프트가 `PS C:\...>` 인지 볼 것):

```powershell
cd C:\Users\SSAFY\Desktop
tar -czf metagame-server.tar.gz -C metagame-linux-server .
scp -i C:\Users\SSAFY\Desktop\Ec2\J15B202T.pem metagame-server.tar.gz ubuntu@j15b202.p.ssafy.io:~/
```

**EC2 에서**:

```bash
sudo mkdir -p /opt/metagame
sudo tar -xzf ~/metagame-server.tar.gz -C /opt/metagame
sudo chmod +x /opt/metagame/MetaGameServer.x86_64
sudo chown -R ubuntu:ubuntu /opt/metagame
rm ~/metagame-server.tar.gz
```

## 3-1. 함정 — `-nographics` 만으로는 리눅스에서 죽는다 (2026-09-01 실측)

EC2에서 서버가 **1초 만에 SIGSEGV로 죽고** systemd가 5초마다 되살리는 것을
50번 반복했다. 로그는 여기서 끊긴다:

```
Selected window backend: (null)
...
ThreadLog initialized.
Caught fatal signal - signo:11 code:1 errno:0 addr:(nil)
Obtained 5 stack frames.
#1  PlayerMain(int, char**)
```

매니지드 스택이 없다 — 네이티브에서 죽는 것이라 C# 의 try/catch 로는 잡히지 않는다.

**원인**: Unity 리눅스 플레이어는 `-nographics`를 줘도 창(SDL) 백엔드를 건드린다.
화면이 없는 서버에서는 그 지점에서 넘어진다.

**해결**: 가상 디스플레이(`xvfb`)를 물리고 **`-nographics`를 뺀다.**

```bash
sudo apt-get install -y xvfb
```

`ExecStart`를 `xvfb-run -a`로 감싼다(§4 참조).

**먼저 지운 오답들** — 같은 증상에서 다시 의심하지 않도록 적어 둔다:

| 의심했던 것 | 결과 |
| --- | --- |
| 깨진 `TD_Stage1_Arena.asset` | ✗ Main 씬이 참조하지 않는다(참조 0) |
| 코드 스트리핑(릴리스 빌드) | ✗ 개발 빌드도 똑같이 죽었다 |
| 손으로 쓴 `PF_WorldSync.prefab` | ✗ 없애도 똑같이 죽었다 (그래도 스폰 핸들러 방식이 더 나아 그대로 둠) |
| 빠진 공유 라이브러리 | ✗ `ldd`에 `not found` 없음 |
| 폰트 없음(IMGUI의 `Font.CreateDynamicFontFromOSFont`) | ✗ `fontconfig`·`fonts-nanum`을 깔아도 똑같이 죽었다 |
| **창 백엔드 없음** | **✓ `xvfb-run`으로 해결** |

**가려내는 법**: `-server` 없이 맨손으로 띄워 본다. 그래도 죽으면 씬·네트워크와
무관하고 플레이어 자체가 못 뜨는 것이다.

```bash
cd /opt/metagame
timeout 20 ./MetaGameServer.x86_64 -batchmode -logFile /tmp/mg.log ; echo "종료코드 $?"
```

**124** = timeout이 정상 종료시킴(= 살아 있었다). **139** = SIGSEGV.

## 4. systemd 유닛

```bash
sudo tee /etc/systemd/system/metagame.service > /dev/null <<'EOF'
[Unit]
Description=MetaGame Mirror 게임 서버
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/opt/metagame
ExecStart=/usr/bin/xvfb-run -a /opt/metagame/MetaGameServer.x86_64 -batchmode -server \
  -port=17777 -verifyUrl=https://j15b202.p.ssafy.io/auth/verify
Restart=on-failure
RestartSec=5
# 로그는 journald 로. 파일로 따로 남기지 않는다(회전 관리 한 곳으로).
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now metagame
sudo systemctl status metagame --no-pager
```

**`-roomId`를 주지 않았다.** 주면 그 방 하나만 받는다. 지금은 서버가 하나뿐이라
어느 방이든 받게 열어 둔다 — 대신 **방이 둘 이상이면 모두 같은 세계로 들어간다.**
시연이 한 팀이면 문제없고, 여러 팀을 동시에 받으려면 방마다 서버를 띄워야 한다.

## 5. 방화벽

```bash
sudo ufw allow 17777/udp
sudo ufw status numbered
```

`-145 §4-3`: **UFW가 실제 게이트다.** enable은 유지한다.

## 6. 확인

```bash
# 서버가 그 포트를 듣고 있나
sudo ss -ulnp | grep 17777

# 로그 (접속하면 [NetPlayer] 서버 스폰 이 찍힌다)
journalctl -u metagame -f
```

내 PC에서 밖에서 닿는지:

```powershell
Test-NetConnection j15b202.p.ssafy.io -Port 17777 -InformationLevel Detailed
```

> UDP 는 `Test-NetConnection` 으로 확실히 못 본다(응답이 없어도 열려 있을 수 있다).
> 진짜 확인은 **클라이언트로 붙어 보는 것**이다.

## 7. 코드가 바뀐 뒤 갱신 (한 줄 요약)

> 서버 빌드 → `tar` → `scp` → EC2 에서 `tar -xzf` → `sudo systemctl restart metagame`

## 8. 막혔을 때

| 증상 | 원인 | 할 일 |
| --- | --- | --- |
| 로비에서 "게임 시작" 이 반응 없음 | 게임 서버가 안 떠 있음 | `systemctl status metagame` |
| 접속이 조용히 실패 | 포트 세 곳 중 하나가 다름 | §1 표를 다시 대조 |
| 서버는 떴는데 아무도 못 붙음 | UFW | `sudo ufw status`에 17777/udp 있는지 |
| 붙자마자 끊김 | 토큰 확인 실패 | `journalctl`에 `/auth/verify` 응답 확인 |
| UDP 자체가 막힘 | 망 정책 | `-145 §4-3`의 예비안 — `SimpleWebTransport`로 wss(443) |

## 9. 남은 것

- **`-55`**: 지금은 맵에서 떨어지면 되돌아오지 않는다. 서버에 낙사 판정이 없다.
  이번 테스트는 "떨어지지 않기" 로 피한다.
- **`-143`**: 이 절차의 빌드·전송을 CI로 옮긴다.
- 스왑 0 이므로 4 인 접속 뒤 메모리를 보고 필요하면 2 GB 확보(`-145 §4-5`).
