# CI/CD 전체 지도 — 한 장

> **이 문서는 지도다.** 무엇이 어디서 돌고, 무엇이 머지를 막고, 실패하면 어디를 보는지만 적는다.
> 각 장치를 **왜** 그렇게 만들었는지와 사고 이력은 [ci-and-notifications.md](./ci-and-notifications.md)(참고서)에 있고,
> 정의의 단일 출처는 [`.gitlab-ci.yml`](../.gitlab-ci.yml) 이다. 이 문서의 잡 이름은 `repo-hygiene` 가 실존 여부를 검사한다.
> 실측 2026-09-07 (GitLab 프로젝트 설정 API · EC2 `config.toml` · `systemctl`).

---

## 0. 전체 그림

```
 Jira 이슈 ──R4──▶ 진행 중                 ① 상태는 Jira 자동화만 바꾼다 (CI 는 읽기 전용)
    │  "브랜치 만들기"
    ▼
 로컬 push ──.githooks/pre-push──▶ 키 없는 브랜치 차단 · develop 최신 자동 리베이스 · LFS
    │
    ▼
 MR 생성 ─────────────────────▶ ② MR 파이프라인 (아래 §2-A)
    │                              prepare  mr-title · mm-mr-opened(🟢)
    │                              lint     mr-lint · mr-overlap → stack-order · repo-hygiene · ci-tests
    │                              test     unity-compile · build-backend
    │                              review   ai-review
    │                              notify   mm-ci-failed(🔴) / mm-ci-resolved(✅)
    ▼
 팀장 머지 ──R6──▶ 이슈 완료    ③ 머지 조건: 파이프라인 성공 + 스레드 전부 해소 (GitLab 설정)
    │
    ▼
 develop push ────────────────▶ ④ develop 파이프라인 (§2-B)
    │                              retarget-pipelines · unity-compile · build-backend ─▶ deploy-backend(▶ 수동)
    │                                                                                       │
    │                                                                        /spool 에 jar + RELEASE
    │                                                                                       ▼
    │                                                            EC2 systemd metagame-deploy.path → /opt/metagame-deploy/deploy.sh
    │                                                            (교체 → 헬스체크 → 실패 시 롤백 → MM 🟢/🔴)
    ▼
 팀장이 release/ 브랜치를 master 에 머지 ▶ ⑥ master 파이프라인 (§2-D): release-windows — 버전 → Windows 빌드 → 패키지 → GitLab Release(태그) → MM 🟢

 매일 09:30 스케줄 ────────────▶ ⑤ 스케줄 파이프라인 (§2-C): ci-tests · retarget-pipelines · mm-digest(📋)

 EC2 호스트 (파이프라인 밖, §4): gitlab-runner ×2 · claude-token-refresh.timer(20분) · metagame-relay · nginx · docker(backend·mysql)
```

---

## 1. 파이프라인은 세 종류다

| 트리거 | 언제 | 생기는 잡 | 비고 |
| --- | --- | --- | --- |
| **A. MR** (`merge_request_event`) | MR 이 열리거나 소스 브랜치에 push | §2-A 전부 (조건부: `ci-tests` `unity-compile` `build-backend` `ai-review`) | 새 push 가 앞 파이프라인을 자동 취소한다(정상) |
| **B. develop push** | 팀장이 MR 을 머지한 직후 | `retarget-pipelines` · `unity-compile`(METAGAME 변경) · `build-backend` → `deploy-backend`(backend 변경, **수동**) | **자동 취소 없음**(`auto_cancel: none`, -309) — 연달아 머지돼도 배포 버튼이 사라지지 않는다 |
| **C. 스케줄** | **평일** 09:30 KST (id 28, `30 9 * * 1-5`, ref develop) — 실제 발송 09:33 | `ci-tests` · `retarget-pipelines` · `mm-digest` | 카드는 항목이 있을 때만 나간다 |
| **D. master push** | 팀장이 `release/<키>-vX.Y.Z` 브랜치(또는 hotfix)를 master 에 머지한 직후 | `release-windows` | **릴리스 = 자동**: 버전 → Windows 빌드 → 패키지 → GitLab Release(태그) → MM. 자동 취소 없음(-326) |

전부 `CI_ENABLE=true` 변수가 있어야 **파이프라인 자체가 생성**된다(없으면 조용히 0건 — §7).

---

## 2. 잡 15종

범례 — **막음**: 실패하면 머지 불가(§3). **비차단**: `allow_failure`, 빨간 ⚠ 만. 스크립트는 전부 `scripts/ci/*.py`, 파이썬 표준 라이브러리만.

### 2-A. MR 파이프라인

| 잡 | 스테이지 | 조건 | 하는 일 | 쓰는 곳 | 실패 시 |
| --- | --- | --- | --- | --- | --- |
| `mr-title` | prepare | 항상 | 브랜치의 Jira 키로 이슈를 읽어 제목을 `[파트] type: 요약 (키)` 로 보정, 본문 `- 이슈:` 채움 | MR 제목·본문 | 비차단 |
| `mm-mr-opened` | prepare | `MM_WEBHOOK_URL` | 🟢 리뷰 요청 카드 — 이 MR 의 **첫** 파이프라인일 때 1회, 리뷰어(기본 팀장) 멘션. Draft 제외 | MM | 비차단 |
| `mr-lint` | lint | 항상 · `needs: mr-title` | Draft 금지 · 소스/대상 브랜치 규칙(master 는 develop·hotfix/*·`release/<키>-vX.Y.Z` 만 — 릴리즈 MR 은 squash 해제·자기 커밋 없음까지 본다, -325) · 제목 형식 · 키 일치 · 템플릿 6섹션 · 작성자 체크리스트 · Jira 이슈 실존·담당자·상위 에픽 · 파트 일치. 경고(브랜치가 master 기반 · develop 대비 뒤처짐 · 다른 키 커밋 섞임 · 담당자 ≠ 작성자)는 MR 코멘트 `⚠ 확인 사항` 로 | MR 코멘트 · MM 🟠(master 기반일 때만) | **막음** |
| `mr-overlap` | lint | 항상 | 같은 대상을 향한 **다른 열린 MR 마다** `git merge-tree` 로 실제 3-way 머지를 시험. 텍스트 충돌이면 **나중 MR** 이 ❌, 먼저 MR 은 ⚠ + 머지 순서. 이 MR 이 다른 MR 위에 얹혀 있으면 대상을 그 브랜치로 **자동 전환**(스택 MR) | MR 코멘트(양쪽) · MR 대상 브랜치 | **막음**(나중 MR 의 텍스트 충돌만) |
| `stack-order` | lint | 항상 · `needs: mr-overlap` · `when: always` | 스택 자식 MR 은 부모가 develop 에 들어간 뒤에만 — 아니면 실패 + **미해소 스레드 🔒** 로 머지 버튼을 잠근다. 부모가 이미 머지됐으면 대상을 develop 으로 되돌리고 통과 | MR 스레드 · MR 대상 브랜치 | **막음**(🔴 카드는 안 보냄 — 잘못이 아니라 순서일 뿐) |
| `repo-hygiene` | lint | 항상 | 경로 180자 상한 · 금지 파일(Library·빌드 산출물·인증키 …) · `.meta` 짝 · Assets 최상위 금지 · LFS 대상이 바이너리로 커밋 · 비-LFS 5MB · **씬/프리팹 변경 vs 본문 선언** · 문서가 말하는 CI 잡 이름 실존 · **MR 잡에 `/spool` 이 보이면 실패**(배포 격리 회귀). 릴리즈 MR(develop → master)은 통째로 건너뜀 | 잡 로그 | **막음** |
| `ci-tests` | lint | `scripts/ci/**` 변경 (+ 스케줄) | `scripts/ci/tests/run_all.py` — CI 스크립트 자신의 오프라인 테스트 34파일. 네트워크·토큰 없이, 발신 비밀은 env 에서 지운 채 | 잡 로그 | **막음** |
| `unity-compile` | test | `METAGAME/**` 또는 `.gitlab-ci.yml` 변경 | GameCI 이미지에서 C# 컴파일 + EditMode 테스트. **대상 브랜치를 합친 뒤** 돌리고 **대상 브랜치 기준선과 비교**해 종료 코드로 판정: `0` 통과(기존 실패만) · `1` **이 MR 이 만든** 컴파일 오류·새 실패 · `2` 판단 불가(기준선 없음·라이선스). 판정 2 는 비차단이라 코멘트가 **막지 않는다는 사실**을 적는다. 기준선은 잡 목록 API 로 찾고 아티팩트는 14일 보관(-342). 웜 70초 / 콜드 8분(`Library` 슬롯 캐시). 동시 1개(`resource_group`) | MR 코멘트(새 실패만, `error CS` 맨 위) · 테스트 위젯(JUnit) | `1` **막음** · `2` 경고 |
| `build-backend` | test | `backend/**` 또는 `.gitlab-ci.yml` 변경 | `./gradlew bootJar` — 컴파일 검사. **테스트는 아직 돌지 않는다**(-302) | jar 아티팩트(3일) | **막음** |
| `ai-review` | review | `AI_REVIEW_CLI=true`(또는 API 키) · 코드/설정 변경 (문서만이면 생략) | 역할별 리뷰어(범위·완료 조건 / C#·네트워크 / 에셋·씬) 병렬 → 요약 1개: ✅/⚠️/⛔ + 리뷰어 확인 5항목 판정표. 러너 호스트의 Claude CLI 로 | MR 코멘트 1개(재실행 시 갱신) | 비차단 |
| `mm-ci-failed` | notify | `on_failure` · needs: `mr-lint` `repo-hygiene` `ci-tests`(opt) `mr-overlap` | 🔴 CI 실패 카드(작성자 멘션 · 실패 잡 · `❌` 이유 3개) + 같은 내용 MR 코멘트. Draft 제외 | MM · MR 코멘트 | 비차단 |
| `mm-ci-resolved` | notify | `on_success` · needs **동일** | 남아 있는 🔴 코멘트를 ✅ 해소됨으로 갱신 | MR 코멘트 | 비차단 |

### 2-B. develop push 파이프라인

| 잡 | 조건 | 하는 일 | 실패 시 |
| --- | --- | --- | --- |
| `retarget-pipelines` | 항상 (+ 스케줄) | 부모 브랜치 삭제로 develop 에 **자동 재대상된 자식 MR** 을 찾아 파이프라인을 만든다 — GitLab 이 그때 안 만들기 때문(-295). 그 파이프라인에서 `stack-order` 가 통과하며 🔒 스레드를 해소한다 | 비차단 |
| `unity-compile` | `METAGAME/**` 변경 | 개별 MR 은 통과했는데 합쳐서 깨지는 경우를 2분 안에 잡는다(MR 에서 합친 결과를 검사하게 된 `-342` 뒤에도 안전망으로 남긴다). 이 결과가 다음 MR 들의 **기준선**이 된다. **주의 — 여기서 실패해도 MM 알림이 없다**: `mm-ci-failed` 가 `merge_request_event` 에서만 돈다(`-344`) | 경고(기준선용) |
| `build-backend` | `backend/**` · `.gitlab-ci.yml` 변경 | 배포용 jar 아티팩트 | 실패 |
| `deploy-backend` | 위와 같음 · `needs: build-backend` · **`when: manual`** · `tags: [deploy]` · `interruptible: false` · `environment: production` · **옛 파이프라인의 버튼을 누르면 경고**(-333) | jar 을 `/spool` 에 원자적으로 놓고 `RELEASE` 표시 → 호스트 `deploy.sh` 가 교체·헬스체크(`POST /auth/guest` 200)·실패 시 **직전 jar 로 롤백**(3개 보관) → 잡은 `STATUS` 에 자기 sha 가 찍힐 때까지 기다림(최대 420초) → MM 🟢 배포 완료 / 🔴 롤백 / 🔴 무응답 | 실패 → MM 🔴(Infra 멘션) |

**왜 수동인가**: 서버 한 대가 테스트 서버이자 시연 서버라 재시작이 WebSocket 세션을 전부 끊는다. dev/prod 가 갈리면(-284) dev 만 자동으로 바꾼다.

### 2-C. 스케줄 파이프라인 (09:30)

| 잡 | 하는 일 |
| --- | --- |
| `ci-tests` | 코드가 안 바뀌어도 **시간이 흘러** 깨지는 것을 잡는다(-281 이 그랬다). 실패하면 아침 카드 맨 위에 🧪 |
| `retarget-pipelines` | 전날 develop 파이프라인이 실패·취소됐어도 다음 아침에 메운다 |
| `mm-digest` | 📋 아침 점검 카드(§6). `needs: ci-tests(optional)` + `when: always` — 테스트가 빨개도 카드는 나간다 |

---

### 2-D. master push 파이프라인 (릴리스)

| 잡 | 조건 | 하는 일 | 실패 시 |
| --- | --- | --- | --- |
| `release-windows` | master push · `resource_group: unity` · 90분 | ① 버전 확정(머지 커밋의 `release/…-vX.Y.Z`, 없으면 hotfix PATCH+1, 그것도 없으면 빌드 전에 실패) ② GameCI windows-mono 이미지에서 `SpaceBuild.BuildWindows` ③ zip(`metagame-vX.Y.Z-날짜-시각.zip`) ④ **패키지 레지스트리** 업로드(잡 토큰 — 100MB 첨부 상한을 피한다) ⑤ GitLab Release + 태그(변경 목록 = 이전 태그 이후 develop 머지 MR) ⑥ MM 🟢(팀장) ⑦ 변수 있으면 Jira 릴리스 웹훅(-327) | 어느 단계에서 왜 인지 MM 🔴(Infra). 같은 버전 재실행은 멱등 |

팀장이 하는 일은 릴리스 브랜치를 머지하는 것까지다 — [git-convention §8](./git-convention.md) · [records/-326](./records/S15P21B202-326-release-pipeline.md).

---

## 3. 무엇이 머지를 막나 (GitLab 설정, 2026-09-07 실측)

| 설정 | 값 | 뜻 |
| --- | --- | --- |
| Pipelines must succeed | **켜짐** | 마지막 파이프라인이 빨간불이면 머지 버튼이 잠긴다. 아래 "막음" 잡이 하드 블록이 되는 근거 |
| Skipped pipelines are considered successful | 꺼짐 | 파이프라인이 없는 MR 도 머지 불가 |
| All threads must be resolved | **켜짐** | `stack-order` 의 🔒 스레드가 머지 버튼을 잠그는 근거 |
| Squash commits | 기본 켜짐(선택 가능) | **릴리즈 MR(→ master)은 해제** — 켜진 채 머지하면 이후 모든 릴리즈가 충돌한다. `mr-title` 이 master 대상 MR 의 squash 를 자동으로 끄고 `mr-lint` 가 확인한다(-325) |
| Merge method | merge commit | |
| 소스 브랜치 삭제 | 기본 켜짐 | 머지되면 브랜치가 사라진다 → 후속은 새 이슈·새 브랜치 |
| 보호 브랜치 | `master` push **No one** / merge Maintainers · `develop` push **No one** / merge Maintainers · `hotfix/S15P21B202-*` push Maintainers / merge No one / force push 허용 | **2026-09-09 API 실측** (`protected_branches`). 규칙은 **이 셋뿐**이다 — `S15P21B202-*` 작업 브랜치 보호는 2026-08-27 해제됐다(git-convention §6). develop 이 문서와 어긋나 있던 것은 해소됐다 |

머지를 막는 잡: `mr-lint` · `mr-overlap`(나중 MR 텍스트 충돌) · `stack-order` · `repo-hygiene` · `ci-tests` · `unity-compile`(종료 1) · `build-backend`.
막지 않는 잡: `mr-title` · `mm-*` · `ai-review` · `retarget-pipelines` · `unity-compile`(종료 2).

---

## 4. 러너와 EC2 호스트

한 대의 EC2(`j15b202.p.ssafy.io`, Ubuntu, 4 vCPU · 15 GiB · 309 GB)가 **CI 러너 · 백엔드 · 릴레이 · 배포**를 전부 맡는다 — 단일 장애점이다.

### 러너 (`/etc/gitlab-runner/config.toml`, `concurrent = 2`)

| 러너 | 태그 | 실행기 | 마운트(볼륨) | 몫 |
| --- | --- | --- | --- | --- |
| `METAGAME-ip-172-26-13-34` (공용) | `linux` | docker · `cpu_shares = 256`(경합 시 게임 서버에 양보) | `/cache` · Claude 바이너리 **ro** · `~/.claude` **ro** · Unity 라이선스 `.ulf` **ro** | `deploy-backend` 빼고 전부 (`default.tags: [linux]`) |
| `METAGAME-deploy-ip-172-26-13-34` (배포 전용, id 2068) | `deploy` · **Protected(`ref_protected`)** · `run_untagged` 없음 — 2026-09-08 러너 API 실측 | docker | `/cache` · `/home/ubuntu/metagame/deploy` → **`/spool` rw** | `deploy-backend` 만. 스풀은 여기에만 있고 **데이터만** 들어 있다 — 실행되는 코드는 `/opt/metagame-deploy/`(-324) |
| 내 PC 윈도우 러너 | — | shell(pwsh) | — | **정지 · 예비.** 받을 잡이 없다 |

잡 컨테이너는 root 로 돈다. 공용 러너에 `:rw` 마운트를 두면 아무 MR 이나 자기 브랜치의 `.gitlab-ci.yml` 한 줄로 그것을 읽고 쓸 수 있다 — 그래서 자격증명은 `:ro`, 스풀은 배포 러너에만(-290 · -252). 그리고 **rw 마운트 안에는 실행되는 코드를 두지 않는다**(-324): 스풀에는 데이터(jar·RELEASE·STATUS·로그·previous)만, `deploy.sh` 는 마운트 밖 root 소유.

### 호스트 systemd (파이프라인 밖에서 도는 것)

| 유닛 | 상태 | 하는 일 | 소스 |
| --- | --- | --- | --- |
| `gitlab-runner.service` | 상시 | 위 러너 2대 | `scripts/runner/setup-linux-runner.sh` · `setup-deploy-runner.sh` |
| `metagame-deploy.path` → `metagame-deploy.service` | 감시 대기 | 스풀에 `RELEASE` 가 생기면 **`/opt/metagame-deploy/deploy.sh`**(root 소유, 스풀 **밖**) 실행 — 잡에 도커 소켓을 주지 않기 위한 우회. 코드가 마운트 안에 있으면 잡이 덮어써 호스트 권한으로 돌 수 있어 밖으로 옮겼다(-324) | `scripts/deploy/host/` |
| `claude-token-refresh.timer` → `.service` | **20분마다** | 호스트에서 `claude -p` 로 토큰을 살려 두고(컨테이너는 ro 라 못 씀) 같은 호출로 자격 점검 → 문턱 넘으면 MM 알림(`/etc/metagame/alert.env` 의 웹훅) · 리프레시 토큰 만료 3일 전 경고 | `scripts/runner/claude_auth_check.py` · `.timer` · `.service` |
| `metagame-relay.service` | 상시 | 방장↔참가자 UDP 중계(**17778/udp**) + 제어 API(**17779/tcp**, 도커 사설망만) — 백엔드가 방 성립 때 세션 등록·종료. `GET /health` 로 세션 수 | `scripts/relay/` |
| `metagame.service` | 상시 | Mirror **데디케이티드** 게임 서버(**17777/udp**, `-verifyUrl` 로 백엔드 인증). -162 에서 올렸다. 지금 매치 경로는 **리슨 서버 + 릴레이**라 실제로 쓰이는지 **확인 필요** | -162 (담당 최재현) |
| `nginx.service` | 상시 | 443 → 백엔드 8080 · `wss /ws/game` · certbot 갱신 | `scripts/deploy/server/nginx/` |
| `docker` | 상시 | `metagame-backend`(127.0.0.1:8080 만) · `metagame-mysql`(호스트 게시 없음) | `scripts/deploy/server/compose.prod.yml` |

UFW: 22 · 80 · 443 · 17777/udp · 17778/udp · 17779/tcp(172.16.0.0/12 만). 8080·3306 은 밖에서 안 보인다.

---

## 5. 변수와 비밀 — 이름과 위치만

| 이름 | 어디 | 누가 쓰나 | 없으면 |
| --- | --- | --- | --- |
| `CI_ENABLE` | GitLab CI 변수 | `workflow.rules` | 파이프라인 **0건**(조용히) |
| `GITLAB_TOKEN` (프로젝트 액세스 토큰, api) | CI 변수, masked | 제목·본문 수정, 코멘트, MR 조회 | 제안만 로그 |
| `JIRA_USER` `JIRA_API_TOKEN` (**읽기만**) | CI 변수, masked | 이슈 실존·파트·아침 점검 JQL | Jira 검사 생략(경고) |
| `MM_WEBHOOK_URL` | CI 변수, masked | `mm-*` 잡 · `deploy_backend.py` | 카드 없음 |
| `AI_REVIEW_CLI=true` (+ `AI_REVIEW_MODEL`/`_EFFORT`) | CI 변수 | `ai-review` | 잡 생성 안 됨 |
| `ANTHROPIC_API_KEY` | (대안, 미사용) | `ai-review` api 백엔드 | — |
| Claude 자격 | EC2 `/home/ubuntu/.claude/` (ro 마운트) | `ai-review` · 갱신 타이머 | 401 → 20분 안에 MM 알림 |
| MM 웹훅(호스트용) | EC2 `/etc/metagame/alert.env` (600) | `claude_auth_check.py` | 점검은 돌고 알림만 생략 — 매 회차 로그에 "알릴 수 없음" |
| Unity 라이선스 `.ulf` | EC2 `/home/ubuntu/unity-license/` (ro 마운트) | `unity-compile` | 잡 즉시 실패. **만료 2026-10-09**, `--update-ulf` |
| 백엔드 운영 값 | EC2 `~/metagame/.env` (600) | compose | — |
| 러너 토큰 | EC2 `/etc/gitlab-runner/config.toml` | gitlab-runner | — |
| EC2 접속 키 `J15B202T.pem` | 내 PC `~/.ssh/` (리포 밖 · `.gitignore` + hygiene 차단) | 사람 | — |

값은 이슈·문서·채팅 어디에도 적지 않는다. `.gitlab-ci.yml` 머리의 `UNITY_RUNNER`/`UNITY_PATH` 설명은 낡았다 — Unity 는 EC2 도커 러너에서 돈다(-271).

---

## 6. 알림 지도 (Mattermost, 채널 1개)

| 카드 | 보내는 것 | 언제 | 멘션 |
| --- | --- | --- | --- |
| 🟢 리뷰 요청 | `mm-mr-opened` | MR 첫 파이프라인 | 리뷰어(기본 팀장) |
| 🔴 CI 실패 → ✅ 해소 | `mm-ci-failed` / `mm-ci-resolved` | 막는 잡이 실패 / 다시 통과 | MR 작성자 |
| 🟠 규칙 위반 | Jira 웹 요청 (R2 제목 형식 · R5 진행 중 되돌림 · R7 머지 없이 닫힘 · R9 완료 되돌림) | 규칙이 되돌릴 때 | 끈 사람 / 담당자 |
| 🟢 머지 완료 · 이슈 완료 | Jira 웹 요청 (R6) | 머지 직후 | 담당자 |
| 🟢 배포 완료 / 🔴 롤백 / 🔴 무응답 | `deploy_backend.py` | 배포 잡 끝 | 실패·롤백·무응답만 Infra. 성공도 보낸다 — "진행 중이던 접속이 끊겼다" 를 알려야 하므로 |
| 🟢 릴리스 vX.Y.Z / 🔴 릴리스 실패 | `release-windows`(`publish_release.py`) | master 머지 뒤 Release 발행 / 빌드·업로드·발행 어느 단계 실패 | 팀장 / Infra |
| 📋 아침 점검 | `mm-digest` | 09:33, 항목 있을 때 | 항목별 담당자 |
| 🔔 Claude 자격 | 호스트 `claude_auth_check.py` | 인증 2회 연속 실패 등 문턱 · 복구 시 · 6시간마다 재알림 | Infra |
| 🔴 develop 파이프라인 실패 | GitLab 내장 통합 | default branch 만 | 없음 |

**아침 점검 블록**(순서대로, 없으면 생략): 🧪 오프라인 테스트 실패 · needs-fix · ⏰ 기한 지남 · 🐢 3일 정체 진행 중 · 👤 스프린트 담당자 없음 · 24h 넘은 MR · 🚫 검사가 돌지 않은 MR(파이프라인 없음·2h 대기) · ⛔/⚠ 충돌하는 열린 MR · 🔗 머지됐는데 완료 안 된 이슈 · 🧩 스택 자식 MR · 🌿 머지 뒤 다시 생긴 브랜치 · 꼬리 🚀 배포 상태(마지막 성공 sha · 대기 잡 수 · **배포본과 develop 의 backend 가 다르면 경고**) — 마지막 시도가 실패·취소면 별도 블록으로 Infra 호출.

MR 하나에 붙을 수 있는 봇 코멘트: `mr-lint` ⚠ · `mr-overlap` · `stack-order` 🔒(스레드) · CI 실패/해소 · `ai-review` · `unity-compile` 결과 — 최대 6종, 전부 마커 기반이라 재실행해도 늘지 않는다.

---

## 7. 이상할 때 어디를 보나

| 증상 | 먼저 볼 것 | 그다음 |
| --- | --- | --- |
| 파이프라인이 **아예 없다** | `CI_ENABLE` 변수 · 소스 브랜치가 master 기반(`.gitlab-ci.yml` 이 없다) | 아침 카드 🚫 가 다음 날 알려 준다 |
| 잡이 `pending` 에서 안 움직인다 | EC2 `systemctl status gitlab-runner` · `journalctl -u gitlab-runner -n 50` | 참고서 §4 비상 복귀 3단계 |
| `ai-review` 코멘트가 없다 | 잡 로그의 `category=` 줄 · EC2 `journalctl -u claude-token-refresh -n 20` | 🔔 알림이 이미 왔어야 한다. `authentication` 이면 호스트 재로그인 |
| 배포 버튼을 눌렀는데 결과가 없다 | EC2 `/home/ubuntu/metagame/deploy/STATUS` · `deploy.log` | `systemctl status metagame-deploy.path` · 스크립트가 있는지 `ls -l /opt/metagame-deploy/deploy.sh` |
| 배포 후 서버가 이상하다 | 🔴 롤백 카드가 왔는지 — 왔으면 **직전 jar 로 돌고 있다** | `docker logs metagame-backend` |
| 아침 카드가 안 왔다 | 항목이 0이면 정상. 스케줄 24 활성 여부 · 스케줄 파이프라인 잡 로그 | `MM_WEBHOOK_URL` |
| 릴레이가 안 붙는다 | EC2 `curl 127.0.0.1:17779/health` · 백엔드 기동 로그 `relay 제어 API 도달` 줄 | [multiplayer-test-guide.md](./multiplayer-test-guide.md) |
| `unity-compile` 이 전부 `2`(판단 불가) | develop 기준선 파이프라인이 취소·실패했는지 → develop 의 `unity-compile` 재시도. `-342` 이후 기준선을 잡 목록 API 로 찾고 아티팩트를 14일 보관하므로 창이 비어서 생기는 경우는 줄었다 | 라이선스 만료(10-09)면 `.ulf` 갱신 |
| 스택 자식 MR 이 빨간불 | 부모가 develop 에 들어갔나. 들어갔으면 `retarget-pipelines` 가 다음 develop push·아침에 새 파이프라인을 만든다 | 급하면 MR 에서 Run pipeline |

---

## 8. 알려진 불일치·남은 일 (2026-09-07)

### 로컬에서 Unity 배치모드 빌드를 돌린 뒤에는 `git status` 를 본다

**배치모드 빌드는 프로젝트를 고친다.** 임포트 과정에서 URP 에셋·`ProjectSettings` 를
재직렬화하고(정상), 에디터 스크립트가 **에셋을 지우기도 한다.**

`ArtLink.CleanStale` 이 `Assets/_Project/Prefabs/Resources` 에서 자기가 만드는 셋만 남기고
나머지를 지웠다 — 손으로 만든 `PF_Stage1_Tablet.prefab` 이 **매 빌드 삭제됐다**(`-351`).
지금은 이름을 아는 것만 지우도록 고쳤지만, 배치모드가 프로젝트를 건드리는 성질 자체는
남아 있다.

```
# 빌드 뒤 반드시
git status --short

# 의도한 파일만 남기고 되돌린다
git checkout -- METAGAME/
```

커밋 전에는 **실제로 담긴 파일**을 본다 — `git add -A` 때 EOL 재stat 경고가 떠도 내용이
같으면 커밋에는 안 들어간다.

```
git show --name-status --format="" HEAD
```

`release-windows` 는 빌드 뒤 `git status --porcelain -- METAGAME/` 를 보고 **삭제(`D`)가
있으면 릴리스를 실패시킨다**(`-351`). 재직렬화(`M`)는 정상이라 개수만 센다.

| 무엇 | 상태 |
| --- | --- |
| 릴리스 파이프라인 **첫 실측 전** | `release-windows`(-326)는 들어갔지만 실제 릴리스는 아직 0회 — 첫 릴리스에서 빌드 시간·zip 크기·패키지 업로드 상한을 실측해 records/-326 §4 에 적는다 |
| ~~Jira R6·R7 정규식이 `release/` 를 모른다~~ | **2026-09-07 해결** — 3곳을 `^(?:hotfix/\|release/)?` 로 바꿨다(부록 A 방식, PUT 200 확인). 첫 실전 확인은 릴리스 MR 머지 때 R6 가 그 이슈를 완료시키는지 |
| ~~Jira 릴리스 fixVersion(-327)~~ | **2026-09-07 규칙 R10 완성**(부록 A 방식). 남은 것은 CI 변수 `JIRA_RELEASE_WEBHOOK` 과 첫 릴리스에서 Jira 릴리스 화면 확인 |
| `build-backend` 가 테스트를 안 돈다 | -302. 플래키 원인(-313)이 배포됐으니 `test bootJar` + junit 으로 |
| `metagame.service`(데디케이티드 서버) 가 아직 돈다 | 리슨 서버 + 릴레이 전환 뒤 사용 여부 확인 |
| `deploy-backend` 가 수동 · dev/prod 미분리 | -284. 보안그룹 답(dev 포트) 대기 |
| Unity 라이선스 만료 10-09 · Claude 리프레시 토큰 만료 09-30 | 사람이 갱신·재로그인. 후자는 3일 전부터 알림 |
| 봇 코멘트 최대 6종 | 합칠지 결정 남음 |
