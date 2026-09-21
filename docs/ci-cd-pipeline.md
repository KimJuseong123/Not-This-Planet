# CI/CD 구조와 운영 안내

파이프라인이 언제 실행되는지, 어떤 검사가 머지를 막는지, 러너와 서버가 어떻게 연결되는지 설명합니다. 장애가 발생하면 §7의 증상별 확인 순서에서 시작합니다.

[문서 안내](README.md) · 선행: [작업 흐름](workflow.md) · 상세: [CI·알림 동작과 이력](ci-and-notifications.md)

> 설계 근거와 사고 이력은 [CI·알림 상세](./ci-and-notifications.md)에 있으며,
> 정의의 단일 출처는 [`.gitlab-ci.yml`](../.gitlab-ci.yml) 이다. 이 문서의 잡 이름은 `repo-hygiene` 가 실존 여부를 검사한다.
> 실측 2026-09-07 (GitLab 프로젝트 설정 API · EC2 `config.toml` · `systemctl`).

---

**이 문서의 내용**

- [0. 전체 그림](#section-1)
- [1. 파이프라인 실행 조건](#section-2)
- [2. 파이프라인별 잡](#section-3)
- [3. 무엇이 머지를 막나 (GitLab 설정, 2026-09-07 실측)](#section-4)
- [4. 러너와 EC2 호스트](#section-5)
- [5. 변수와 비밀 — 이름과 위치만](#section-6)
- [6. 알림 지도 (Mattermost, 채널 1개)](#section-7)
- [7. 증상별 확인 순서](#section-8)
- [8. 알려진 불일치·남은 일 (2026-09-07)](#section-9)

<a id="section-1"></a>

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
    │                              review   ai-review → ai-review-gate (⛔ 만 차단)
    │                              notify   mm-ci-failed(🔴) / mm-ci-resolved(✅)
    ▼
 팀장 머지 ──R6──▶ 이슈 완료    ③ 머지 조건: 파이프라인 성공 + 스레드 전부 해소 (GitLab 설정)
    │
    ▼
 develop push ────────────────▶ ④ develop 파이프라인 (§2-B)
    │                              retarget-pipelines · unity-compile · build-backend ─▶ deploy-dev(▶ 수동)
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

<a id="1-파이프라인은-세-종류다"></a>

<a id="section-2"></a>

## 1. 파이프라인 실행 조건

| 트리거 | 언제 | 생기는 잡 | 비고 |
| --- | --- | --- | --- |
| **A. MR** (`merge_request_event`) | MR이 열리거나 소스 브랜치에 push | §2-A 전부 (조건부: `ci-tests` `unity-compile` `build-backend` `ai-review`) | 새 push가 앞 파이프라인을 자동 취소한다(정상) |
| **B. develop push** | 팀장이 MR을 머지한 직후 | `retarget-pipelines` · `unity-compile`(METAGAME 변경) · `build-backend` → `deploy-dev`(backend 변경, **버튼만** — 자동 배포는 새벽 스케줄, -455) | **자동 취소 없음**(`auto_cancel: none`, -309) — 연달아 머지돼도 앞 파이프라인의 배포가 취소되지 않는다 |
| **C. 스케줄** | **평일** 09:30 KST (id 28, `30 9 * * 1-5`, ref develop) — 실제 발송 09:33 | `ci-tests` · `retarget-pipelines` · `mm-digest` | 카드는 항목이 있을 때만 나간다 |
| **D. master push** | 팀장이 `release/<키>-vX.Y.Z` 브랜치(또는 hotfix)를 master에 머지한 직후 | `release-windows` → `publish-builds`(자동) · `build-backend`(자동) → **`release-prod`(수동, 버튼 하나)** — 백엔드 배포 + `latest.json` (-592) | **릴리스 = 자동, 공개는 버튼**: 버전 → Windows 빌드 → 패키지 → GitLab Release(태그) → MM. 자동 취소 없음(-326) |

전부 `CI_ENABLE=true` 변수가 있어야 **파이프라인 자체가 생성**된다(없으면 조용히 0건 — §7).

---

<a id="2-잡-15종"></a>

<a id="section-3"></a>

## 2. 파이프라인별 잡

범례 — **막음**: 실패하면 머지 불가(§3). **비차단**: `allow_failure`, 빨간 ⚠ 만. 스크립트는 전부 `scripts/ci/*.py`, 파이썬 표준 라이브러리만.

### 2-A. MR 파이프라인

| 잡 | 스테이지 | 조건 | 하는 일 | 쓰는 곳 | 실패 시 |
| --- | --- | --- | --- | --- | --- |
| `mr-title` | prepare | 항상 | 브랜치의 Jira 키로 이슈를 읽어 제목을 `[파트] type: 요약 (키)`로 보정, 본문 `- 이슈:` 채움 | MR 제목·본문 | 비차단 |
| `mm-mr-opened` | prepare | `MM_WEBHOOK_URL` | 🟢 리뷰 요청 카드 — 이 MR의 **첫** 파이프라인일 때 1회, 리뷰어(기본 팀장) 멘션. Draft 제외 | MM | 비차단 |
| `mr-lint` | lint | 항상 · `needs: mr-title` | Draft 금지 · 소스/대상 브랜치 규칙(master는 develop·hotfix/*·`release/<키>-vX.Y.Z`만 — 릴리즈 MR은 squash 해제·자기 커밋 없음까지 본다, -325) · 제목 형식 · 키 일치 · 템플릿 필수 2섹션(`## Jira`, `## 무엇을 · 왜`) · Jira 이슈 실존·담당자·상위 에픽 · 파트 일치. 경고(브랜치가 master 기반 · develop 대비 뒤처짐 · 다른 키 커밋 섞임 · 담당자 ≠ 작성자)는 MR 코멘트 `⚠ 확인 사항`로 | MR 코멘트 · MM 🟠(master 기반일 때만) | **막음** |
| `mr-overlap` | lint | 항상 | 같은 대상을 향한 **다른 열린 MR 마다** `git merge-tree`로 실제 3-way 머지를 시험. 텍스트 충돌이면 **나중 MR**이 ❌, 먼저 MR은 ⚠ + 머지 순서. 이 MR이 다른 MR 위에 얹혀 있으면 대상을 그 브랜치로 **자동 전환**(스택 MR) | MR 코멘트(양쪽) · MR 대상 브랜치 | **막음**(나중 MR의 텍스트 충돌만) |
| `stack-order` | lint | 항상 · `needs: mr-overlap` · `when: always` | 스택 자식 MR은 부모가 develop에 들어간 뒤에만 — 아니면 실패 + **미해소 스레드 🔒**로 머지 버튼을 잠근다. 부모가 이미 머지됐으면 대상을 develop으로 되돌리고 통과 | MR 스레드 · MR 대상 브랜치 | **막음**(🔴 카드는 안 보냄 — 잘못이 아니라 순서일 뿐) |
| `repo-hygiene` | lint | 항상 | 경로 180자 상한 · 금지 파일(Library·빌드 산출물·인증키 …) · `.meta` 짝 · Assets 최상위 금지 · LFS 대상이 바이너리로 커밋 · 비-LFS 5MB · **씬/프리팹 변경 vs 본문 선언** · 문서가 말하는 CI 잡 이름 실존 · **MR 잡에 `/spool`이 보이면 실패**(배포 격리 회귀). 릴리즈 MR(develop → master)은 통째로 건너뜀 | 잡 로그 | **막음** |
| `ci-tests` | lint | `scripts/ci/**` 변경 (+ 스케줄) | `scripts/ci/tests/run_all.py` — CI 스크립트 자신의 오프라인 테스트 34파일. 네트워크·토큰 없이, 발신 비밀은 env에서 지운 채 | 잡 로그 | **막음** |
| `unity-compile` | test | `METAGAME/**` 또는 `.gitlab-ci.yml` 변경 | GameCI 이미지에서 C# 컴파일 + EditMode 테스트. **대상 브랜치를 합친 뒤** 돌리고 **대상 브랜치 기준선과 비교**해 종료 코드로 판정: `0` 통과(기존 실패만) · `1` **이 MR이 만든** 컴파일 오류·새 실패 · `2` 판단 불가(기준선 없음·라이선스). 판정 2 는 비차단이라 코멘트가 **막지 않는다는 사실**을 적는다. 기준선은 잡 목록 API로 찾고 아티팩트는 14일 보관(-342). **기준선은 최근 3회의 합집합**이고, 새 실패가 있으면 **그 테스트만 골라 2회 다시 돌려** 한 번이라도 통과하면 플래키로 판정에서 뺀다 — 뺀 것은 코멘트에 적는다. 새 실패가 `RECHECK_MAX_NEW`(30)를 넘으면 재실행을 건너뛰고 그 사실을 로그에 적는다. **면제가 있었고 그 MR이 `METAGAME/`을 바꿨으면 판정을 `0` → `2`(경고)로 낮춘다** — 격리 통과가 그 MR이 만든 상태 오염일 수도 있어서다. 안 바꿨으면 그 경우가 불가능하므로 `0` 이다(-347). **실측 2026-09-17: 6분** — 임포트 36초(웜) · 도메인 리로드 35초 · **테스트 275초(1795건)**. 콜드 임포트는 8분(`Library` 슬롯 캐시). 느린 것이 테스트 시간의 대부분을 먹어 **`-testCategory "!Heavy"` 로 뺀다 — 빼는 것이 기본이고 새벽 스케줄(`NIGHTLY_DEPLOY_DEV`)에서만 전체를 돌린다**(-662). 머지 직후 develop 도 `!Heavy` 다 — 그 15분이 5일에 진짜 회귀 1건을 잡는 동안 `resource_group: unity` 를 물어 MR 검사와 릴리스를 막았다(2026-09-21 실측). 지난 실행과 검사 대상(브랜치·합칠 develop)이 같으면 **Unity 를 건너뛰고 그 결과를 그대로 쓴다** — 둘 다 -602. 동시 1개(`resource_group: unity`, **`oldest_first`**) | MR 코멘트(새 실패만, `error CS` 맨 위) · 테스트 위젯(JUnit) | `1` **막음** · `2` 경고 |
| `build-backend` | test | `backend/**` 또는 `.gitlab-ci.yml` 변경 | `./gradlew bootJar` — 컴파일 검사. **테스트는 아직 돌지 않는다**(-302) | jar 아티팩트(3일) | **막음** |
| `ai-review` | review | `AI_REVIEW_CLI=true`(또는 API 키) · 코드/설정 변경 (문서만이면 생략) | 역할별 리뷰어(범위·완료 조건 / C#·네트워크 / 에셋·씬) 병렬 → 요약 1개: ✅/⚠️/⛔ + 5항목 판정표(범위·완료 조건·씬/프리팹·구조·기록). 러너 호스트의 Claude CLI 로 | MR 코멘트 1개(재실행 시 갱신) | 비차단 (⛔ 는 아래 `ai-review-gate` 가 막는다) |
| `ai-review-gate` | review | `ai-review` 와 같은 조건 (**릴리즈 MR → master 는 건너뜀** — `ai-review` 가 그 MR 의 리뷰를 생략하므로 판정이 없다) | 위 코멘트의 판정을 읽어 **⛔ 면 막는다**. ⚠️·✅·❓ 는 통과. 코멘트가 없거나 ❓ 거나 **다른 커밋의 것**이면 막지 않고 🟠 카드로 알린다(fail-open) | 잡 로그 · 🟠 카드 | **예** — ⛔ 일 때만 |
| `mm-ci-failed` | notify | `on_failure` · needs: `mr-lint` `repo-hygiene` `ci-tests`(opt) `mr-overlap` | 🔴 CI 실패 카드(작성자 멘션 · 실패 잡 · `❌` 이유 3개) + 같은 내용 MR 코멘트. Draft 제외 | MM · MR 코멘트 | 비차단 |
| `mm-ci-resolved` | notify | `on_success` · needs **동일** | 남아 있는 🔴 코멘트를 ✅ 해소됨으로 갱신 | MR 코멘트 | 비차단 |

### 2-B. develop push 파이프라인

| 잡 | 조건 | 하는 일 | 실패 시 |
| --- | --- | --- | --- |
| `retarget-pipelines` | 항상 (+ 스케줄) | 부모 브랜치 삭제로 develop에 **자동 재대상된 자식 MR**을 찾아 파이프라인을 만든다 — GitLab이 그때 안 만들기 때문(-295). 그 파이프라인에서 `stack-order`가 통과하며 🔒 스레드를 해소한다 | 비차단 |
| `unity-compile` | `METAGAME/**` 변경 | 개별 MR은 통과했는데 합쳐서 깨지는 경우를 2분 안에 잡는다(MR에서 합친 결과를 검사하게 된 `-342` 뒤에도 안전망으로 남긴다). 이 결과가 다음 MR 들의 **기준선**이 된다. **주의 — 여기서 실패해도 MM 알림이 없다**: `mm-ci-failed`가 `merge_request_event` 에서만 돈다(`-344`) | 경고(기준선용) |
| `build-backend` | `backend/**` · `.gitlab-ci.yml` 변경 | 배포용 jar 아티팩트 | 실패 |
| `deploy-dev` | **develop push** + 위와 같음 · `needs: build-backend` · **`when: manual`**(머지는 버튼만) + **새벽 스케줄 자동**(`$NIGHTLY_DEPLOY_DEV`, -455) · `tags: [deploy]` · `interruptible: false` · `environment: development` · `DEPLOY_SPOOL: /spool-dev` | jar을 `/spool-dev`에 원자적으로 놓고 `RELEASE` 표시 → 호스트 `deploy.sh`가 교체·헬스체크(`POST /auth/guest` 200, **8081**)·실패 시 **직전 jar로 롤백**(3개 보관) → 잡은 `STATUS`에 자기 sha가 찍힐 때까지 기다림(최대 420초) → MM 🟢 배포 완료 / 🔴 롤백 / 🔴 무응답 | 실패 → MM 🔴(Infra 멘션) |
| `deploy-prod` | **web 파이프라인만**(`DEPLOY_PROD=1`, -464) — 릴리스 경로에서는 빠졌다(-592) · `needs: build-backend` · **`when: manual`** · `tags: [deploy]` · `interruptible: false` · `environment: production` · **옛 파이프라인의 버튼을 누르면 경고**(-333) | 위와 같고 스풀만 기본값 `/spool`, 헬스체크는 **8080** | 실패 → MM 🔴(Infra 멘션) |
| `publish-builds` | **master push** · `needs: release-windows` · `tags: [deploy]` · 자동 | 패키지 레지스트리에서 zip 을 받아 풀고 **매니페스트 + 내용 주소 저장소**를 `/builds` 에 만든다(`--no-latest`). zip 도 함께 둔다 — 런처를 안 쓰는 사람 길이 남아야 한다. **아직 아무도 이 버전을 받지 않는다** | 실패 → MM 🔴 |
| `release-prod` | **master push** · **`when: manual`** · `environment: production` · `needs: build-backend`(optional, jar) | **① 배포 백엔드에 jar 적용 → ② `latest.json` 이동** (-592). 백엔드가 실패하면 거기서 멈춘다 — `latest.json` 은 안 움직인다. 백엔드가 안 바뀐 릴리스는 ①을 건너뛴다. `latest.json` 을 이 버전으로 옮긴다 — **누르는 순간 모든 런처가 그 버전으로 간다.** 옮기기 전에 그 버전이 쓰는 파일이 저장소에 다 있는지 확인한다. 되돌리기도 이 잡이다(`PROMOTE_VERSION` 에 옛 버전) | 실패 → MM 🔴 |
| `publish-site` | **develop push** + `website/**` 변경 · `tags: [deploy]` | `website/` 를 `/site` 로 옮긴다. **LFS 포인터가 섞여 있으면 멈춘다** — 올리면 영상·이미지가 전부 깨지는데 200 으로 내려가 화면만 보고는 못 찾는다 | 실패 → MM 🔴 |

**배포 시점** (-455). 재시작은 어느 쪽이든 WebSocket 세션을 전부 끊는다. 끊기는 사람이 다를 뿐이다 — dev 는 **테스트하는 팀원**, prod 는 **실유저**. 그래서 둘 다 머지가 자동으로 배포하지 않는다. dev 는 **새벽 스케줄**(끊길 세션이 없는 시간)이 자동으로 맞추고 낮에는 버튼이며, prod 는 릴리스 뒤 사람이 시점을 고른다. `-139` §4 가 처음부터 적어 둔 이유이고, 한때 "나눈 뒤 실유저는 prod 에 있으니 dev 는 자동" 이라고 바꿨던 것이 틀렸다.

**구버전 차단** (-404). 서로 다른 버전이 같은 방에 들어오면 동기화가 깨진다. 런처로 막으면
우회된다 — 압축 푼 폴더의 exe 를 직접 실행하는 것이 가장 흔하고 그게 정확히 막아야 하는
경우다. 그래서 **로그인(`POST /auth/guest`)에서** 막는다.

| | |
| --- | --- |
| 클라이언트가 보내는 것 | `BuildStamp` 의 `버전=` 줄(`release-windows` 가 `SPACE_RELEASE_VERSION` 으로 넘긴다) |
| 서버가 보는 것 | `metagame.client.min-version` (`CLIENT_MIN_VERSION`) |
| 막는 조건 | **`클라 < 최소` 일 때만.** 모르는 새 버전은 통과시킨다 |
| 거절 | `426` + 사유 · **받을 곳 URL** · 최소 버전 |

**허용 목록이 아니라 최소 버전인 이유**: 백엔드(`deploy-*`)와 클라이언트(`release-windows`)는
트리거·브랜치·시점이 달라 **동시에 나가지 않는다.** 허용 목록이면 클라이언트가 먼저 나간
순간 백엔드가 그 버전을 몰라 **전원 거절**한다.

**기본값은 비어 있고, 비면 게이트가 꺼진다.** 이 코드가 배포되는 순간 이미 사람들 손에 있는
빌드가 전부 막히면 안 된다. **최소 버전을 올리는 시점이 곧 구버전 차단 시점**이고 그 판단은
사람이 한다 — 릴리스를 내보낸 뒤 `CLIENT_MIN_VERSION` 을 그 버전으로 올리고 백엔드 컨테이너만
다시 띄운다(재배포가 아니다).

**개발 서버(`/dev`)는 비워 둔다.** 에디터 실행과 dev·qa 빌드는 `vX.Y.Z` 모양이 아니라
(= "버전을 못 읽음") 켜는 순간 팀이 전부 막힌다. 켜져 있을 때 버전을 못 읽는 빌드를
거절하는 것은 의도한 것이다 — 스탬프가 없던 시절 빌드가 정확히 막아야 할 대상이다.
**개발·QA 빌드** (-460). QA 는 두 번 일어난다 — **머지 뒤**(합쳐진 상태가 되나 · `develop`)와
**머지 전**(이 MR 이 그 버그를 고쳤나 · 그 브랜치). 런처는 릴리스된 버전만 볼 수 있어서
그때는 손으로 빌드해 나눠 줬다.

| 잡 | 언제 | 러너 | 하는 일 |
| --- | --- | --- | --- |
| `build-devbuild` | **새벽 스케줄**(`NIGHTLY_DEPLOY_DEV`) 또는 **MR 의 버튼** | `linux` | Unity 빌드 → zip → 패키지 레지스트리 → (qa 면) 배치 트리거 |
| `publish-devbuild` | 새벽은 같은 파이프라인 · qa 는 **트리거로 자동** | `deploy` | 받아서 `make_manifest --channel` 로 배치하고 **전송용 패키지를 지운다** |

**qa 빌드는 파이프라인을 하나 더 거친다.** 배포 러너는 `ref_protected` 라 **MR ref 의 잡을
아예 집지 않는다**(`-405` 실측). MR 파이프라인 안에 배치 잡을 두면 눌렀을 때 러너를 찾지 못해
**영원히 pending 으로 파이프라인을 붙잡는다.**

그래서 `build-devbuild` 가 끝나면서 `trigger_stage.py` 로 **`develop` 대상 파이프라인을
만든다**(`-582`). 그쪽 ref 는 보호 브랜치라 배포 러너가 집는다. 넘어가는 것은 레지스트리의
zip 하나와 변수 셋(`STAGE_VERSION`·`STAGE_BRANCH`·`STAGE_MR`)뿐이고, 배치 잡은 **develop 의**
`.gitlab-ci.yml` 과 스크립트로 돈다 — 그래서 "아무 MR 이나 자기 `.gitlab-ci.yml` 한 줄로
`/var/www` rw 를 만진다" 가 생기지 않는다.

**누르는 사람과 배치하는 사람이 같아야 한다.** 전에는 배치가 Run pipeline(`develop`)의
버튼이었는데, develop 은 보호 브랜치라 **Maintainer 만** 누를 수 있었다 — 그러면 `-460` 이
없애려던 "손으로 빌드해 나눠 주는 일" 이 "손으로 등록해 달라고 부탁하는 일" 로 모양만
바뀐다. **개발 빌드에 무엇을 추가하는 일은 Developer 가 스스로 할 수 있어야 한다**(팀 결정
2026-09-17). 트리거 토큰(`DEVBUILD_TRIGGER_TOKEN`)은 한 번 만들면 그 뒤로 손이 가지 않는다.

그 토큰은 **Protected 로 둘 수 없다** — MR 파이프라인은 보호 ref 가 아니라서 Protected
변수가 전달되지 않는다. 대신 `workflow.rules` 가 트리거를 `develop` + `STAGE_VERSION` 일
때만 받으므로, 그 토큰으로 다른 ref·다른 잡을 돌릴 수 없다.

손으로 배치하는 길(`web` 파이프라인 + `STAGE_VERSION`)은 **되돌아갈 자리로 남겨 뒀다.**
다만 그 길은 `release-windows` 의 리허설(90분, `resource_group: unity`)이 딸려 온다.

**qa 는 `dev-latest.json` 을 가져가지 않는다**(`-582`). dev·qa 는 목록 파일을 함께 쓰는데
(개발 런처가 한 화면에서 고른다), qa 가 최신까지 가져가면 **누가 MR 버튼을 누를 때마다 팀
전체 개발 런처의 기본값이 그 미머지 브랜치가 된다.** 목록에는 올라가므로 골라서 쓴다.

**그때 출처는 `CI_COMMIT_REF_NAME` 이 아니다.** 그 잡은 `develop` 에서 도므로 그대로 쓰면
목록에 `develop @ …` 이 찍힌다 — **출처를 말한다는 것이 곧 틀린 것을 말하는 것**이 된다.
커밋은 **버전 이름에서** 꺼내고(`qa-401-1a2b3c4` → `1a2b3c4`), 브랜치는 `STAGE_BRANCH` 로
**명시해서** 준다. 안 주면 비운다 — **비어 있는 것은 "모른다" 이고 틀린 값보다 낫다.**

**왜 머지마다가 아닌가.** 웜이어도 10분대이고 `resource_group: unity` 라 `unity-compile`
과 직렬이다 — 낮에 돌리면 **아침에 MR 검사가 줄을 선다.** 그래서 develop 은 새벽이고
MR 은 사람이 누른다(`allow_failure` 라 누르지 않은 버튼이 파이프라인을 붙잡지 않는다).

#### 큐 순서는 `oldest_first` 다 (S15P21B202-605)

`resource_groups/unity` 의 `process_mode` 는 **`oldest_first`**(FIFO)다. API 로만 바꿀 수 있고
(`PUT /projects/:id/resource_groups/unity`) 웹 화면이 없다.

**`newest_first` 로 두면 안 된다.** 가장 최근 잡부터 집으므로 오래 기다린 잡이 계속 밀리는데,
하필 굶는 것이 **develop 의 `unity-compile`** 이다. 그 잡은 **모든 MR 의 차단 판정이 기대는
기준선**을 만든다 — 기준선이 낡으면 "develop 이 만든 실패와 이 MR 이 만든 실패를 가릴 수
없다" 로 판정 1(차단)이 2(경고)로 내려간다(위 표의 `-342` 규칙). 즉 **큐 순서가 속도가 아니라
판정 정확도를 좌우한다.**

2026-09-17 실측: develop 의 잡 `565619` 가 생성 17:00 → 시작 18:32 로 **92분** 밀렸고, 그동안
돌던 MR 이 한 단계 옛 기준선을 써서 **새 실패 10건을 경고로 통과**시켰다. develop 이 깨졌다.

`unordered`(기본값)로 돌아갈 이유는 없다 — 순서 보장이 없어 같은 굶주림이 무작위로 난다.

**`newest_first` 로 갈 수 있는 조건**(그때는 Retry 가 곧 우선권이 된다): ① 기존 실패를 0 으로
만들고(`-346`) ② 판정을 "하나라도 실패하면 막는다" 로 바꾸고 ③ 낡은 기준선 완화 규칙을 끈다.
기준선이 빈 집합이면 그 내용도 신선도도 판정에 영향을 주지 않기 때문이다. **①②③ 없이 ④만
하면 위 사고가 그대로 반복된다.**

**패키지 레지스트리는 운반 수단일 뿐이다.** 빌드는 공용 러너에서 돌고 배포 폴더는 배포
러너에만 마운트돼 있는데 아티팩트 상한이 100 MB 다. 배치가 끝나면 **그 패키지를 지운다** —
릴리스와 달리 dev·qa zip 은 보관할 이유가 없고, 매일 156 MB 씩 쌓으면 할당량을 먹는다.

**dev·qa zip 은 공개 폴더에 두지 않는다.** nginx 허용 목록이 `*.zip` 을 열어 두므로 두면
누구나 받을 수 있고, 그것은 "배포 런처에 dev 빌드가 보이면 안 된다" 는 결정과 어긋난다.

**이 빌드들은 `SPACE_DEV=1` 이다** — 개발 백엔드(`/dev`)를 가리킨다. 그리고 `vX.Y.Z` 가
아니므로 `-404` 의 버전 게이트에서 "버전을 못 읽음" 이 된다. **개발 서버는 게이트를 꺼
둬야 한다**(`CLIENT_MIN_VERSION` 을 비워 둔다).

**사용자용 요약 초안** (-462). 릴리스 노트는 `!350 · S15P21B202-440` 같은 줄의 목록이라
**개발자용**이다. 플레이어는 "이번에 뭐가 좋아졌나" 를 알 수 없다.

`release-windows` 뒤의 **`release-summary` 잡**이 `release_summary.py` 로 **초안 한
장**(`release-summary.md`)을 아티팩트로 남긴다. 재료는 머지 커밋에서 뽑은 이슈 키 →
**Jira 요약**이다(규약이 `[파트] 무엇을 했는지` 로 쓰게 하므로 이미 읽을 수 있다). 세
곳에 맞춰 따로 적는다 — 사이트 `/play/`(플레이어) · GitLab Release 맨 앞(팀) · Jira
릴리스(팀·평가). 버전은 `release-windows` 의 `release-version.txt` 를 물려받는다 —
여기서 다시 정하면 두 곳이 어긋난다.

**공개하지 않는다.** AI 가 쓴 문장이 그대로 나가면 이상한 한 줄이 게임의 첫인상이 된다.
사람이 보고 고친 뒤에 싣는다 — `release-prod`(공개 버튼)와 같은 결이다.

**별도 잡인 이유**(-579). 처음에는 `release-windows` 마지막 줄이었다. 그 잡은 Unity
이미지라 **`claude` CLI 가 없고**, `|| true` 가 그 실패를 성공으로 만들었다 — 초안은
v1.1.0 까지 **한 번도 나온 적이 없었다.** 이제 기본 이미지(`ai-review` 와 같은 곳)에서
돌고, 막지 않는 방법도 `|| true` 가 아니라 `allow_failure` 다: 릴리스는 그대로 나가되
실패가 파이프라인에 ⚠ 로 **남는다.** `test_release_summary.py` 가 이 잡에 `image`·`tags`
덮어쓰기가 없는지 검사한다.

초안이 **스스로 경고한다**: 사이트 문구에 이슈·MR 번호가 남아 있으면 그 줄을 짚어 주고,
Jira 요약을 못 읽은 이슈가 있으면 몇 건이 빠졌는지 본문에 적는다 — **재료가 반쪽인데
완성처럼 보이는 것**이 이 자리에서 가장 조용한 실패다.

### 2-C. 스케줄 파이프라인 (09:30)

| 잡 | 하는 일 |
| --- | --- |
| `ci-tests` | 코드가 안 바뀌어도 **시간이 흘러** 깨지는 것을 잡는다(-281 이 그랬다). 실패하면 아침 카드 맨 위에 🧪 |
| `retarget-pipelines` | 전날 develop 파이프라인이 실패·취소됐어도 다음 아침에 메운다 |
| `mm-digest` | 📋 아침 점검 카드(§6). `needs: ci-tests(optional)` + `when: always` — 테스트가 실패해도 카드는 나간다 |

---

### 2-D. master push 파이프라인 (릴리스)

| 잡 | 조건 | 하는 일 | 실패 시 |
| --- | --- | --- | --- |
| `release-windows` | master push · `resource_group: unity` · 90분 | ① 버전 확정(머지 커밋의 `release/…-vX.Y.Z`, 없으면 hotfix PATCH+1, 그것도 없으면 빌드 전에 실패) ② GameCI windows-mono 이미지에서 `SpaceBuild.BuildWindows` ③ zip(`metagame-vX.Y.Z-날짜-시각.zip`) ④ **패키지 레지스트리** 업로드(잡 토큰 — 100MB 첨부 상한을 피한다) ⑤ GitLab Release + 태그(변경 목록 = 이전 태그 이후 develop 머지 MR) ⑥ MM 🟢(팀장) ⑦ 변수 있으면 Jira 릴리스 웹훅(-327) | 어느 단계에서 왜 인지 MM 🔴(Infra). 같은 버전 재실행은 멱등 |

팀장이 하는 일은 릴리스 브랜치를 머지하는 것까지다 — [git-convention §8](./git-convention.md) · [records/-326](./records/S15P21B202-326-release-pipeline.md).

#### master 머지는 **빌드 릴리스**이지 서버 배포가 아니다 (S15P21B202-464)

릴리스가 났다고 **배포 서버(8080)의 백엔드는 바뀌지 않는다.** master 머지 뒤에 자동으로 도는
것은 빌드·패키지·Release·배치까지이고, 그 전부가 **실제 플레이에 영향을 주지 않는다** —
런처는 `latest.json` 만 보는데 그것을 아직 안 옮기기 때문이다. 올려 두는 것과 가리키는 것은
다르다(`-405`).

**사용자에게 내보내는 것은 버튼 하나다** — `release-prod`. 그것이 ① 배포 백엔드에 jar 을
적용하고 ② `latest.json` 을 옮긴다. **이때 세션이 끊긴다**(백엔드 재시작 약 7초).

| 하고 싶은 것 | 어떻게 |
| --- | --- |
| 릴리스를 사용자에게 내보낸다 | 릴리스 파이프라인의 **`release-prod` 를 누른다** — 백엔드와 클라이언트가 같이 간다 |
| **서버만** 고친다(설정 한 줄·보안 수정) | Pipelines → **Run pipeline** → ref `master` · `DEPLOY_PROD=1` → `deploy-prod` 를 누른다. 그 파이프라인에서는 `release-windows` 가 **돌지 않는다** — 90분을 기다릴 이유가 없다 |
| 되돌린다 | **Run pipeline** → ref `master` · `PROMOTE_VERSION=<옛 버전>` → `release-prod`. **백엔드는 건드리지 않는다** — 급할 때 쓰는 길이라 서버까지 되돌리면 그 시간이 곧 장애 시간이다 |
| 사이트에 **이번에 바뀐 것**을 싣는다 | **`release-summary` 잡**의 아티팩트 `release-summary.md`(AI 초안, `-462`·`-579`)를 열어 **사이트용 3~6줄을 사람이 고쳐** `website/release-config.js` 의 `notes` 에 넣고 develop 에 머지한다. `publish-site` 가 올린다. **비워 두면 그 절이 화면에 안 나온다** — 안 싣는 것도 선택이다(`-595`)<br>**릴리스 당일에 한다.** v1.1.0·v1.2.0 둘 다 이 줄이 여기 적혀 있는데도 비어 있었다 — 파이프라인이 초록이고 아티팩트도 있어서 **빠뜨려도 아무 신호가 없다**(`-649`) |
| 지금 서버에 무엇이 올라가 있는지 본다 | **아침 점검 카드의 🚀 두 줄** (개발·배포 각각). `-464` 전에는 잡 이름이 어긋나 늘 "이력 없음" 이었다 |

**전에는 버튼이 둘이었다**(`deploy-prod` · `promote-builds`)**.** 둘이 서로를 몰라 순서도
눌렀는지도 알 수 없었고, 실제로 어긋났다 — v1.1.0 에서 promote 만 눌려 배포 백엔드가
**나흘째 옛 jar** 이었다(`-570` 이 prod 에 없었다). 한쪽만 눌러도 파이프라인이 초록이라
아무도 몰랐다(`-592`).

**서버만 고치는 경우에도 코드는 `master` 에 있어야 한다** — `hotfix/` 브랜치를 master 에 머지한
뒤 위 Run pipeline 을 돌린다. 릴리스를 건너뛰는 것이지 브랜치 규칙을 건너뛰는 것이 아니다.

**`deploy-prod` 를 누른 뒤 MM 에 🟢/🔴 카드가 온다.** 안 오면 그것 자체가 신호다 —
`-464` 전에는 알림 잡 이름도 어긋나 있어 **카드가 한 장도 나가지 않았다.**

---

<a id="section-4"></a>

## 3. 무엇이 머지를 막나 (GitLab 설정, 2026-09-07 실측)

| 설정 | 값 | 뜻 |
| --- | --- | --- |
| Pipelines must succeed | **켜짐** | 마지막 파이프라인이 검사가 실패하면 머지 버튼이 잠긴다. 아래 "막음" 잡이 하드 블록이 되는 근거 |
| Skipped pipelines are considered successful | 꺼짐 | 파이프라인이 없는 MR도 머지 불가 |
| All threads must be resolved | **켜짐** | `stack-order`의 🔒 스레드가 머지 버튼을 잠그는 근거 |
| Squash commits | 기본 켜짐(선택 가능) | **릴리즈 MR(→ master)은 해제** — 켜진 채 머지하면 이후 모든 릴리즈가 충돌한다. `mr-title`이 master 대상 MR의 squash를 자동으로 끄고 `mr-lint`가 확인한다(-325) |
| Merge method | merge commit | |
| 소스 브랜치 삭제 | 기본 켜짐 | 머지되면 브랜치가 사라진다 → 후속은 새 이슈·새 브랜치 |
| 보호 브랜치 | `master` push **No one** / merge Maintainers · `develop` push **No one** / merge Maintainers · `hotfix/S15P21B202-*` push Maintainers / merge No one / force push 허용 | **2026-09-09 API 실측** (`protected_branches`). 규칙은 **이 셋뿐**이다 — `S15P21B202-*` 작업 브랜치 보호는 2026-08-27 해제됐다(git-convention §6). develop이 문서와 어긋나 있던 것은 해소됐다 |

머지를 막는 잡: `mr-lint` · `mr-overlap`(나중 MR 텍스트 충돌) · `stack-order` · `repo-hygiene` · `ci-tests` · `unity-compile`(종료 1) · `build-backend`.
막지 않는 잡: `mr-title` · `mm-*` · `ai-review` · `retarget-pipelines` · `unity-compile`(종료 2).
`ai-review-gate` 는 **⛔ 일 때만** 막는다 — 판정을 못 읽으면 통과시키고 알린다(-341).

**⛔ 가 나는 조건은 2026-09-13 에 넓어졌다** (`-477`). 리뷰어가 `verdict: block` 을 낼 때에
더해, **높음 발견 사항이 하나라도 있으면** ⛔ 다. 리뷰어가 `severity: high` 를 적어 놓고
`verdict` 는 `warn` 으로 두는 일이 있어서, 사람이 보기에 심각한 것이 그대로 통과했다.
판정과 심각도가 어긋나면 **막는 쪽**으로 떨어진다. `중간`·`낮음` 은 그대로 통과한다 —
전부 막으면 거의 모든 MR 이 서고, 그러면 ⛔ 가 "원래 뜨는 것" 이 된다.

`block` 의 정의도 넓혔다: 데이터 손상·인증/권한 오류·범위 밖의 위험한 변경에 더해
**조용히 틀리는 것**(검사가 통과하는데 실제로는 보지 않는다), **사용자에게 나가는 것을
잘못 내보내는 것**(배포·릴리스·버전), **되돌릴 수 없는 것**, 그리고 **그 MR 이 새로 넣은
코드의 명백한 오류**. 취향·문서 범위·후속 과제는 여전히 ⚠️ 다.

**되돌리는 법**: CI 변수 `AI_REVIEW_BLOCK_SEVERITY` 를 비우면 심각도 차단이 꺼져 예전처럼
판정만 본다. `high,medium` 으로 조일 수도 있다. **MR 없이 돌릴 수 있어야 한다** — 되돌릴
길이 없는 규칙은 사람들이 우회하기 시작한다.

---

<a id="section-5"></a>

## 4. 러너와 EC2 호스트

한 대의 EC2(`j15b202.p.ssafy.io`, Ubuntu, 4 vCPU · 15 GiB · 309 GB)가 **CI 러너 · 백엔드 · 릴레이 · 배포**를 전부 맡는다 — 단일 장애점이다.

### 러너 (`/etc/gitlab-runner/config.toml`, `concurrent = 2`)

| 러너 | 태그 | 실행기 | 마운트(볼륨) | 몫 |
| --- | --- | --- | --- | --- |
| `METAGAME-ip-172-26-13-34` (공용) | `linux` | docker · `cpu_shares = 256`(경합 시 게임 서버에 양보) | `/cache` · Claude 바이너리 **ro** · `~/.claude` **ro** · Unity 라이선스 `.ulf` **ro** | `deploy` 태그 잡 빼고 전부 (`default.tags: [linux]`) |
| `METAGAME-deploy-ip-172-26-13-34` (배포 전용, id 2068) | `deploy` · **Protected(`ref_protected`)** · `run_untagged` 없음 — 2026-09-08 러너 API 실측 | docker | `/cache` · `/home/ubuntu/metagame/deploy` → **`/spool` rw** · `/home/ubuntu/metagame-dev/deploy` → **`/spool-dev` rw**(-284) · `/var/www/metagame-builds` → **`/builds` rw** · `/var/www/metagame-site` → **`/site` rw**(-405) | `deploy-dev`·`deploy-prod`·`publish-builds`·`release-prod`·`publish-site`만. 스풀은 여기에만 있고 **데이터만** 들어 있다 — 실행되는 코드는 `/opt/metagame-deploy/`(-324) |
| 내 PC 윈도우 러너 | — | shell(pwsh) | — | **정지 · 예비.** 받을 잡이 없다 |

잡 컨테이너는 root로 돈다. 공용 러너에 `:rw` 마운트를 두면 아무 MR 이나 자기 브랜치의 `.gitlab-ci.yml` 한 줄로 그것을 읽고 쓸 수 있다 — 그래서 자격증명은 `:ro`, 스풀은 배포 러너에만(-290 · -252). 그리고 **rw 마운트 안에는 실행되는 코드를 두지 않는다**(-324): 스풀에는 데이터(jar·RELEASE·STATUS·로그·previous)만, `deploy.sh`는 마운트 밖 root 소유.

### 호스트 systemd (파이프라인 밖에서 도는 것)

| 유닛 | 상태 | 하는 일 | 소스 |
| --- | --- | --- | --- |
| `gitlab-runner.service` | 상시 | 위 러너 2대 | `scripts/runner/setup-linux-runner.sh` · `setup-deploy-runner.sh` |
| `metagame-deploy.path` → `metagame-deploy.service` | 감시 대기 | 스풀에 `RELEASE`가 생기면 **`/opt/metagame-deploy/deploy.sh`**(root 소유, 스풀 **밖**) 실행 — 잡에 도커 소켓을 주지 않기 위한 우회. 코드가 마운트 안에 있으면 잡이 덮어써 호스트 권한으로 돌 수 있어 밖으로 옮겼다(-324) | `scripts/deploy/host/` |
| `claude-token-refresh.timer` → `.service` | **20분마다** | 호스트에서 `claude -p`로 토큰을 살려 두고(컨테이너는 ro 라 못 씀) 같은 호출로 자격 점검 → 문턱 넘으면 MM 알림(`/etc/metagame/alert.env`의 웹훅) · 리프레시 토큰 만료 3일 전 경고 | `scripts/runner/claude_auth_check.py` · `.timer` · `.service` |
| `metagame-relay.service` | 상시 | 방장↔참가자 UDP 중계(**17778/udp**) + 제어 API(**17779/tcp**, 도커 사설망만) — 백엔드가 방 성립 때 세션 등록·종료. `GET /health`로 세션 수 | `scripts/relay/` |
| `metagame.service` | 상시 | Mirror **데디케이티드** 게임 서버(**17777/udp**, `-verifyUrl`로 백엔드 인증). -162 에서 올렸다. 지금 매치 경로는 **리슨 서버 + 릴레이**라 실제로 쓰이는지 **확인 필요** | -162 (담당 최재현) |
| `nginx.service` | 상시 | 443 → 백엔드 8080 · `wss /ws/game` · certbot 갱신 | `scripts/deploy/server/nginx/` |
| `docker` | 상시 | `metagame-backend`(127.0.0.1:8080 만) · `metagame-mysql`(호스트 게시 없음) | `scripts/deploy/server/compose.prod.yml` |

### 밖에서 들어오는 요청은 어디로 가나 (-461, 2026-09-11 실측)

```
                        인터넷 (누구나)
       ┌──────────────┬──────┴───────┬───────────────┐
    :80 tcp        :443 tcp      :17778 udp      :17779 tcp
       │              │              │         (172.16.0.0/12 만)
     nginx          nginx         python3          python3
  301 → https         │         (릴레이 중계)    (릴레이 제어 API)
                      │
                      ├─ /play/                  → /var/www/metagame-site/    정적
                      ├─ = /play/site.webmanifest → 같은 폴더 + MIME 지정
                      ├─ /builds/                → /var/www/metagame-builds/  정적 · 목록 403
                      ├─ /dev/                   → 127.0.0.1:8081   개발 백엔드
                      ├─ /auth/                  → 127.0.0.1:8080   **속도 제한**
                      └─ /                       → 127.0.0.1:8080   배포 백엔드 · wss

                   :22 tcp → sshd (pem 필요)
                   :17777 udp → **아무도 안 듣는다** (정지한 데디케이티드 서버 자리)
```

**무엇으로 갈리나.** `server_name` 으로 호스트를 고르고, 그 안에서 `location` 이
**URL 경로**만 본다 — 쿼리·헤더·메서드는 보지 않는다. nginx 는 **가장 긴 접두**가
이기고(`/dev/` 가 `/` 를 이긴다) `=` 는 정확히 일치로 최우선이다.

| 경로 | 처리 | 밖에서 |
| --- | --- | --- |
| `/play/` · `/builds/<파일>` | nginx 정적 | **공개** |
| `/builds/` (목록) | — | **403** — 받는 것과 훑는 것은 다르다(-454) |
| `/auth/guest` · `/auth/verify` | 배포 백엔드 | **공개 + 속도 제한** |
| `/ws/game` | 배포 백엔드 | 공개(핸드셰이크에서 JWT 검증) |
| `/dev/…` | 개발 백엔드 | **공개** — 범위는 `-459` 에서 정한다 |
| 그 밖 백엔드 경로 | — | **401** (`anyRequest().authenticated()`) |
| 8080 · 8081 · 3306 | — | **안 보인다**(127.0.0.1 바인딩 · 호스트 게시 없음) |
| 22 | sshd | 열려 있지만 **pem 키가 있어야 한다** |

**pem 은 "서버를 운영하는 권한" 이다.** 게임과 사이트는 pem 과 무관하게 누구나 쓴다.

### 로그인 속도 제한 (-461)

백엔드가 인증 없이 받는 것은 `/auth/guest` · `/auth/verify` · `/ws/game` 셋뿐이고
**남의 데이터를 읽는 API 는 없다.** 그래도 게스트 로그인을 무한히 부를 수 있어
계정 테이블이 부풀고 CPU 를 먹는다. 사이트를 공개하기 전까지 사실상의 방어는
**"아무도 주소를 모른다"** 였는데 그 전제가 사라졌다.

`limit_req_zone … rate=60r/m` + `burst=30 nodelay`(설정은 `conf.d/limits.conf`).
**SSAFY 망이 NAT 뒤**라 IP 당 제한을 빡빡하게 잡으면 우리가 먼저 막힌다 — 방장이
참가자마다 `/auth/verify` 를 부르므로 6인 방이 차면 몇 초 안에 6번이 몰린다.
그래서 burst 를 넉넉히 준다. 응답은 503 이 아니라 **429** 다(503 은 "서버가 죽었다"
로 읽혀 사용자가 기다려야 할지 다시 눌러야 할지 알 수 없다).

2026-09-11 실측: 방 하나가 차는 **8회 연속 전부 200** · 40회 몰아치면 **27 통과 ·
13 차단** · `/play/` 와 `/dev/` 는 영향 없음.

**`/dev/` 에는 걸지 않았다.** 같은 NAT 뒤에서 팀이 동시에 시험하면 우리가 먼저
막히고, 그러면 "왜 막혔는지" 를 찾는 데 시간이 든다. dev 를 누구에게 열지는
`-459` 에서 정한다.

UFW: 22 · 80 · 443 · 17777/udp · 17778/udp · 17779/tcp(172.16.0.0/12 만). 8080·3306 은 밖에서 안 보인다.

### 개발·배포 환경 (-284)

백엔드가 두 벌 뜬다. **새 포트를 열지 않고 경로로 나눈다** — 포트는 SSAFY 보안그룹이 열어 줘야 하고 우리가 정할 수 없다.

| | 배포(prod) | 개발(dev) |
| --- | --- | --- |
| 사용자가 보는 주소 | `https://j15b202.p.ssafy.io` | `https://j15b202.p.ssafy.io/dev` |
| nginx | `location /` → 8080 | `location /dev/` → 8081, **끝 슬래시가 접두를 뗀다** |
| compose 프로젝트 | `metagame` | `metagame-dev` |
| MySQL 볼륨 | `mysql-data` | `mysql-dev-data` |
| 스풀 | `/spool` | `/spool-dev` |
| 배포 잡 | `release-prod` (master, **수동** — 백엔드+클라이언트) · `deploy-prod` 는 web 전용 | `deploy-dev` (develop, **수동** — 자동은 새벽 스케줄뿐, -455) |

`proxy_pass http://127.0.0.1:8081/` 의 **끝 슬래시를 빼면** `/dev/auth/guest` 를 그대로 넘겨 404 가 되고, 원인이 눈에 보이지 않는다.

**게임 서버는 나누지 않는다.** dev 전용 게임 서버를 띄우지 않고 **로컬 리슨 서버로 테스트한다.** 이유 셋:

1. 리슨 서버로 전환한 뒤 `metagame.service`(데디케이티드)는 **정지·비활성** 상태다(2026-09-11 실측 `inactive · disabled · NRestarts=0`). 나누어야 할 상시 프로세스가 애초에 없다.
2. 릴레이는 **공유한다.** `python3` 한 프로세스가 TCP 17779 를 잡고 있고 두 번째를 띄우려면 포트를 또 열어야 하는데, 그것이 우리가 정할 수 없는 것이다.
3. 4 vCPU 한 대에서 CI 러너·백엔드 두 벌·MySQL 두 벌·릴레이가 이미 돈다. 게임 서버를 한 벌 더 띄우면 `unity-compile` 이 굶는다(공용 러너는 `cpu_shares = 256`, 곧 1/4 몫이다).

그래서 UDP 17778 은 그대로 한 벌이고, 개발 테스트는 사람이 자기 PC 에서 호스트한다.

### 클라이언트가 환경을 고르는 방법 (-284)

주소는 `BackendEndpoints` 한곳에서 파생된다 — HTTP 로그인 · WebSocket 방 · 참가자 토큰 검증 셋이 같은 base 에서 나온다. 전에는 셋이 흩어져 있었고 그중 `MatchAuthenticator.VerifyUrl` 은 **코드 기본값이 배포 주소로 박혀** 있었다.

| 무엇으로 | 결과 | 왜 그 순서인가 |
| --- | --- | --- |
| `-backend=<주소>` 실행 인자 | 그 주소 | 가장 세다. QA 가 빌드를 다시 만들지 않아도 되게 |
| 에디터 | 항상 **개발** | 개발자가 로컬에서 돌리는 것이 실유저 데이터를 건드리면 안 된다 |
| 빌드 | 스탬프의 `백엔드=dev\|prod` | `SpaceBuild` 가 정하고 `BuildStamp` 가 `build-stamp.txt` 2번째 줄부터에 적는다 |
| 스탬프에 그 줄이 없음 | 개발 빌드면 개발, 배포 빌드면 배포 | 표시가 없던 시절 빌드는 전부 배포를 가리켰다. 개발 빌드가 그 자리에 오는 것은 스탬프를 못 쓴 경우뿐이라, 그때 배포로 가면 **이 구조가 막으려던 실패가 재현된다** |

### 스탬프의 줄들 (`build-stamp.txt`)

첫 줄은 사람이 읽는 표시고, 둘째 줄부터는 **기계가 읽는 `머리표=값`** 이다. 줄이 없으면 그 값은 없는 것이고, 없는 것을 어떻게 볼지는 **읽는 쪽**이 정한다.

| 줄 | 넣는 변수 | 누가 읽나 | 어느 빌드에 있나 |
| --- | --- | --- | --- |
| `백엔드=dev\|prod` | `SPACE_DEV` (`SpaceBuild` 가 되돌려 박는다) | `BackendEndpoints` | 전부 |
| `버전=vX.Y.Z` | `SPACE_RELEASE_VERSION` (`release-windows`) | 서버의 **버전 게이트**(-404) | **릴리스만** |
| `빌드=<이름>` | `SPACE_BUILD_ID` (`build-devbuild`) | **이벤트**·런로그 헤더(-597) | 전부 (릴리스는 버전을 그대로 쓴다) |
| `실험=<라벨>` | `SPACE_EXPERIMENT` (사람이 손으로) | `RunLog` 진단 | 실험 빌드만 |

**`버전=` 과 `빌드=` 은 뜻이 다르다.** 게이트는 `vX.Y.Z` 만 판정할 수 있어서 개발·QA 빌드에는 `버전=` 을 **일부러 넣지 않는다.** 그 값을 신원으로도 쓰던 동안 개발 빌드의 이벤트는 `build: ""` 였고, QA 는 개발 빌드로 하므로 쌓인 것 대부분이 그랬다(-597). 거꾸로 개발 빌드에 `SPACE_RELEASE_VERSION` 을 주면 접속이 거절된다 — 두 변수를 섞지 않는다. 두 방향 모두 `test_build_identity.py` 가 막는다.

`SpaceBuild` 는 dev 결정을 `SPACE_DEV` 환경변수로 되돌려 박는다 — `BuildStamp` 는 `BuildPlayerProcessor` 라 지역변수를 볼 수 없다. 박은 뒤 `finally` 로 **원래 값으로 되돌린다**: 에디터 세션에 남으면 이어서 도는 다른 빌드가 그 값으로 찍힌다.

---

<a id="section-6"></a>

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
| EC2 접속 키 `J15B202T.pem` | 내 PC `~/.ssh/` (저장소 밖 · `.gitignore` + hygiene 차단) | 사람 | — |

### 비밀을 추가할 때 — `SECRET_ENVS` 도 같이 고친다 (S15P21B202-484)

외부 호출이 4xx 로 실패하면 **응답 본문을 잡 로그에 남긴다**(`ci_common.why()`).
이유를 못 봐서 같은 실패를 세 번 겪은 뒤에 넣은 것이다 — Jira 릴리스 웹훅 400 두 번,
MM 카드 400 한 번. 그중 MM 400 때문에 🔴 배포 무응답 카드가 안 떨어져서 개발 배포가
멈춘 것을 1일 15시간 동안 아무도 몰랐다(`-483`).

**응답 본문은 상대 서버가 만든다.** 요청 주소를 그대로 되비추는 서버가 흔해서,
찍기 전에 `ci_common.redact()` 가 **알려진 비밀 값을 이름으로 바꾼다.**

| 규칙 | 값 |
| --- | --- |
| 가릴 대상 | `ci_common.SECRET_ENVS` 에 적힌 환경변수의 **값** |
| 예외 | **8자 미만**은 안 가린다 — 두세 글자를 지우면 문장이 걸레가 되어 사유를 못 읽는다 |
| 길이 | 300자에서 자른다 |
| 어디까지 | **잡 로그만.** MM 카드·MR 코멘트에는 상태 코드까지만 싣는다(`-335`) |

**새 비밀 CI 변수를 추가하면 `SECRET_ENVS` 에도 넣는다.** 목록에 없는 값은 안 가려지고,
그 사실은 실패가 실제로 날 때까지 드러나지 않는다.

값은 이슈·문서·채팅 어디에도 적지 않는다. `.gitlab-ci.yml` 머리의 `UNITY_RUNNER`/`UNITY_PATH` 설명은 낡았다 — Unity는 EC2 도커 러너에서 돈다(-271).

---

<a id="section-7"></a>

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
| 🔴 develop 파이프라인 실패 | GitLab 내장 통합 | default branch만 | 없음 |

**아침 점검 블록**(순서대로, 없으면 생략): 🧪 오프라인 테스트 실패 · needs-fix · ⏰ 기한 지남 · 🐢 3일 정체 진행 중 · 👤 스프린트 담당자 없음 · 🔢 스토리 포인트 없음(열린 스프린트의 미완료만) · 24h 넘은 MR · 🚀 내보내지 않은 런처(-508) · 🖥 낡은 호스트 배포 스크립트(-485) · 🚫 검사가 돌지 않은 MR(파이프라인 없음·2h 대기) · ⛔/⚠ 충돌하는 열린 MR · 🔗 머지됐는데 완료 안 된 이슈 · 🧩 스택 자식 MR · 🌿 머지 뒤 다시 생긴 브랜치 · 꼬리 🚀 배포 상태(마지막 성공 sha · 대기 잡 수 · **배포본과 develop의 backend가 다르면 경고**) · 꼬리 🖥 서버 자원(디스크·메모리·도커·인증서·잰 시각 — **정상일 때도 나온다**, S15P21B202-486) — 마지막 시도가 실패·취소면 별도 블록으로 Infra 호출.

MR 하나에 붙을 수 있는 봇 코멘트: `mr-lint` ⚠ · `mr-overlap` · `stack-order` 🔒(스레드) · CI 실패/해소 · `ai-review` · `unity-compile` 결과 — 최대 6종, 전부 마커 기반이라 재실행해도 늘지 않는다.

---

<a id="7-이상할-때-어디를-보나"></a>

<a id="section-8"></a>

## 7. 증상별 확인 순서

| 증상 | 먼저 볼 것 | 그다음 |
| --- | --- | --- |
| 파이프라인이 **아예 없다** | `CI_ENABLE` 변수 · 소스 브랜치가 master 기반(`.gitlab-ci.yml`이 없다) | 아침 카드 🚫 가 다음 날 알려 준다 |
| 잡이 `pending`에서 안 움직인다 | EC2 `systemctl status gitlab-runner` · `journalctl -u gitlab-runner -n 50` | 참고서 §4 비상 복귀 3단계 |
| `ai-review` 코멘트가 없다 | 잡 로그의 `category=` 줄 · EC2 `journalctl -u claude-token-refresh -n 20` | 🔔 알림이 이미 왔어야 한다. `authentication` 이면 호스트 재로그인 |
| 배포 버튼을 눌렀는데 결과가 없다 | EC2 `/home/ubuntu/metagame/deploy/STATUS` · `deploy.log` | `systemctl status metagame-deploy.path` · 스크립트가 있는지 `ls -l /opt/metagame-deploy/deploy.sh` |
| 배포 후 서버가 이상하다 | 🔴 롤백 카드가 왔는지 — 왔으면 **직전 jar로 돌고 있다** | `docker logs metagame-backend` |
| 아침 카드가 안 왔다 | 항목이 0이면 정상. 스케줄 **28** 활성 여부 · 스케줄 파이프라인 잡 로그 | `MM_WEBHOOK_URL` |
| 릴레이가 안 붙는다 | EC2 `curl 127.0.0.1:17779/health` · 백엔드 기동 로그 `relay 제어 API 도달` 줄 | [multiplayer-test-guide.md](./multiplayer-test-guide.md) |
| `unity-compile`이 전부 `2`(판단 불가) | develop 기준선 파이프라인이 취소·실패했는지 → develop의 `unity-compile` 재시도. `-342` 이후 기준선을 잡 목록 API로 찾고 아티팩트를 14일 보관하므로 창이 비어서 생기는 경우는 줄었다 | 라이선스 만료(10-09)면 `.ulf` 갱신 |
| 무관한 MR이 `1`(막는다)로 걸린다 | 잡 로그의 `재실행 판별:` 줄 — 플래키로 빠졌는지. 코멘트의 접힌 "플래키로 판정에서 뺀" 절 | 재실행도 계속 실패하면 `git diff <기준선 sha> <합친 base> -- METAGAME/`로 트리가 같은지 본다. 같으면 그 MR 탓이 아니다(-347) |
| 스택 자식 MR이 실패 표시 | 부모가 develop에 들어갔나. 들어갔으면 `retarget-pipelines`가 다음 develop push·아침에 새 파이프라인을 만든다 | 급하면 MR에서 Run pipeline |

---

<a id="section-9"></a>

## 8. 알려진 불일치·남은 일 (2026-09-07)

### 로컬에서 Unity 배치모드 빌드를 돌린 뒤에는 `git status` 를 본다

**배치모드 빌드는 프로젝트를 고친다.** 임포트 과정에서 URP 에셋·`ProjectSettings`를
재직렬화하고(정상), 에디터 스크립트가 **에셋을 지우기도 한다.**

`ArtLink.CleanStale`이 `Assets/_Project/Prefabs/Resources`에서 자기가 만드는 셋만 남기고
나머지를 지웠다 — 손으로 만든 `PF_Stage1_Tablet.prefab`이 **매 빌드 삭제됐다**(`-351`).
지금은 이름을 아는 것만 지우도록 고쳤지만, 배치모드가 프로젝트를 건드리는 동작 특성은
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

`release-windows`는 빌드 뒤 `git status --porcelain -- METAGAME/`를 보고 **삭제(`D`)가
있으면 릴리스를 실패시킨다**(`-351`). 재직렬화(`M`)는 정상이라 개수만 센다.

| 무엇 | 상태 |
| --- | --- |
| 릴리스 파이프라인 **첫 실측 전** | `release-windows`(-326)는 들어갔지만 실제 릴리스는 아직 0회 — 첫 릴리스에서 빌드 시간·zip 크기·패키지 업로드 상한을 실측해 records/-326 §4 에 적는다 |
| ~~Jira R6·R7 정규식이 `release/`를 모른다~~ | **2026-09-07 해결** — 3곳을 `^(?:hotfix/\|release/)?`로 바꿨다(부록 A 방식, PUT 200 확인). 첫 실전 확인은 릴리스 MR 머지 때 R6가 그 이슈를 완료시키는지 |
| ~~Jira 릴리스 fixVersion(-327)~~ | **2026-09-07 규칙 R10 완성**(부록 A 방식). 남은 것은 CI 변수 `JIRA_RELEASE_WEBHOOK`과 첫 릴리스에서 Jira 릴리스 화면 확인 |
| `build-backend`가 테스트를 안 돈다 | -302. 플래키 원인(-313)이 배포됐으니 `test bootJar` + junit으로 |
| `metagame.service`(데디케이티드 서버) 가 아직 돈다 | 리슨 서버 + 릴레이 전환 뒤 사용 여부 확인 |
| ~~백엔드 배포가 수동 · dev/prod 미분리~~ | **2026-09-11 해결** — -284 로 `deploy-dev`(develop 머지 → 개발 서버, 자동)와 `deploy-prod`(master 머지 → 배포 서버, 수동)로 갈랐다. 새 포트는 우리가 정할 수 없어 보안그룹 답을 기다리는 대신 **경로 접두 `/dev`** 로 나눴다(nginx 가 8081로 넘긴다). prod 가 수동인 것은 이제 결함이 아니라 결정이다 |
| Unity 라이선스 만료 10-09 · Claude 리프레시 토큰 만료 09-30 | 사람이 갱신·재로그인. 후자는 3일 전부터 알림 |
| 봇 코멘트 최대 6종 | 합칠지 결정 남음 |
