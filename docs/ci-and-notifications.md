# CI · 알림 — 참고서 (잡별 결정 근거와 사고 이력)

> **전체 구조를 먼저 보려면 [ci-cd-pipeline.md](./ci-cd-pipeline.md)(한 장 지도)** — 파이프라인 3종 · 잡 15종 · 머지 조건 · 러너·EC2 · 알림 지도. 이 문서는 그 지도의 각 칸이 **왜** 그렇게 됐는지를 적는 참고서다.
> 기준: [workflow-plan.md §5](./workflow-plan.md) 1-a~2-b. 0단계(Jira 자동화·GitLab 보호 브랜치·템플릿)는 [jira-gitlab-automation.md](./jira-gitlab-automation.md).
> 파일: [`.gitlab-ci.yml`](../.gitlab-ci.yml), [`scripts/ci/`](../scripts/ci/) — Python 표준 라이브러리만 사용(러너에 pip 불필요).

## 1. 4겹 중 3·4겹이 여기다

| 겹 | 장치 | 상태 |
| --- | --- | --- |
| 1 사람 | 컨벤션 문서 · MR 템플릿 · 팀장 리뷰 체크리스트 | ✅ 운영 중 |
| 2 서버 설정 | GitLab 보호 브랜치(master·develop·hotfix) · 머지 시 브랜치 자동 삭제 · Jira 자동화 8개(MM 표 카드 알림 포함) | ✅ 운영 중 — 2026-08-27 자동 삭제·R6 조건·MM 멘션 실전 확인 |
| **3 CI 검사** | `mr-title` → `mr-lint` · `repo-hygiene` · (`unity-compile`) | ✅ 2026-08-27 파이프라인 #175312 에서 전부 성공(임시 Windows 러너, pwsh). 파이프라인 필수 설정 전까지는 "빨간불"만 |
| **4 AI·알림** | `ai-review`(비차단) · Mattermost(GitLab 통합 + Jira 웹 요청 + `mm-digest`) | `ai-review` ✅ 검증(Opus 5/medium, 75초, 코드·설정 변경 MR 에만 실행). Jira→MM ✅ 적용. GitLab→MM 통합은 팀장 설정 대기 |

## 2. MR 파이프라인 잡

모두 `merge_request_event` 에서만 실행. 스크립트는 `GITLAB_TOKEN` 이 없으면 **읽기 전용으로 동작**(수정·코멘트 대신 로그에 제안 출력)하고, Jira 자격이 없으면 Jira 검사만 건너뛴다 — 변수가 덜 등록돼도 파이프라인이 깨지지 않게.

| 잡 | 단계 | 하는 일 | 실패 = 머지 차단? |
| --- | --- | --- | --- |
| `mr-title` | prepare | 브랜치명의 키로 Jira 이슈를 읽어 제목을 **`[Part] type: 요약 (KEY)`** 로 보정. 파트 = Jira 레이블(결정적, 추측 없음). type 은 기존 값 유지·없으면 변경 파일로 추정(docs/scene/feat/asset/chore). GitLab 기본 제목(`S15p21b202 21`)이면 요약을 Jira 제목으로 교체. 본문 `- 이슈: KEY` 줄도 채움 → **하이픈→공백 문제와 MR 미연결 문제를 원천 해결**. 잘못 고쳤으면 MR 제목 옆 "편집 기록(Compare with previous version)"에서 이전 값을 보고 되돌리고, 파트가 틀렸다면 원인은 Jira 이슈의 레이블이므로 그쪽을 고친다 | 아니오(allow_failure) |
| `mr-lint` | lint | Draft 금지 · 소스/대상 브랜치 형식(대상 = develop · master(릴리즈) · 작업 브랜치=**스택 MR 허용, 경고** · `hotfix/*` 대상은 **실패** — 검증받지 않은 변경이 핫픽스에 얹혀 master 로 들어가고 R6 조건도 막힌다, -134) · 제목 정규식 · 제목/브랜치/본문의 키 일치 · 템플릿 섹션 6개 · 작성자 체크리스트 전부 체크 · Jira 이슈 실존(조회 자체가 실패하면 판정하지 않고 경고만 — 장애·토큰 만료를 '이슈 없음' 으로 오해하지 않는다, -94)·Epic 아님·담당자·상위 Epic 있음 · 제목 파트 = Jira 파트. **경고**: MR 작성자 ≠ Jira 담당자(브랜치 번호 오타로 남의 이슈를 건드린 경우가 드러난다, 대행 작업이면 무시 — -94) | **예**. **경고(차단 아님)**: 브랜치가 master(또는 그 이전)에서 갈라짐 · 시작점이 develop 대비 `MR_STALE_WARN_COMMITS`(기본 25) 이상 뒤 · MR 커밋에 다른 이슈 키 섞임(-90). 경고가 있으면 **MR 코멘트 `⚠ 확인 사항`** 1개(마커로 갱신, 해소되면 ✅ 로 바뀜)에 **그 MR 의 모든 경고**(브랜치 베이스·담당자 불일치·완료된 이슈·파트 없음 …, -105)를 담아 남기고, **master 기반일 때만** 🟠 MM 카드(작성자 멘션)를 보낸다 |
| `repo-hygiene` | lint | **경로 길이 상한 180자**(추가되는 경로만 — 러너 빌드 경로 71자를 더하면 Windows MAX_PATH 260 을 넘어 checkout 자체가 죽는다. 2026-08-31 실측: 189자 경로가 develop 에 들어와 파이프라인 10건이 스크립트 실행 전에 전멸) · 금지 파일(Library/Temp/obj/Build·csproj/sln·.vs/.idea·OS 잡파일·빌드 산출물·**인증키(.pem/.key/.p12/id_rsa 등)**) · Assets 아래 `.meta` 짝(파일·상위 폴더·고아 — 점으로 시작하는 파일·`~` 폴더는 Unity 가 임포트하지 않으므로 제외, -122) · Assets 새 최상위 폴더 금지(`_Project/` 아래로) · **LFS 추적 파일이 실제 바이너리로 커밋됨**(오늘 URP.png 사례) · 비-LFS 5MB 초과 · 씬/프리팹 변경 vs MR 본문 선언 불일치. **릴리즈 MR(develop → master)은 통째로 건너뛴다** — 비교 기준이 master 라 develop 의 기존 파일까지 '추가됨' 으로 잡혀 오탐만 나온다(2026-08-28 MR !68 에서 `.gitkeep` 의 `.meta` 누락 14건 오탐 확인, -122) | **예**  **문서가 없는 CI 잡을 말하는지도 본다**(-292) — `docs/*.md` 의 백틱 안 잡 이름을 `.gitlab-ci.yml` 과 대조한다. 잡을 지웠는데 문서가 그대로면 다음 아무 MR 에서나 걸린다. 러너 태그·`docs/records/`·문서 파일 이름은 거른다(처음 짰을 때 9건을 지적했는데 진짜 문제는 0건이었다). 없어진 잡을 **역사로** 적는 것은 정당하므로("한동안 뒀지만 없앴습니다"), 그런 줄에는 `<!-- drift-ok -->` 를 달아 사람이 표시한다 — 기계가 가를 수 없다. |
| `mr-overlap` | lint | **다른 열린 MR 과의 충돌을 MR 시점에**(-138, 팀장 요청). 같은 대상을 향한 다른 열린 MR 마다 `git merge-tree` 로 **실제 3-way 머지를 시험**해 충돌 파일을 증명한다(추측 아님). 같은 씬·프리팹·에셋을 만졌지만 텍스트 충돌은 없는 경우도 ⚠. **나중에 열린 MR 이 조정하는 쪽** — 텍스트 충돌이면 나중 MR 은 ❌(리베이스/merge 명령을 코멘트로), 먼저 MR 에는 ⚠ 정보 + 머지 순서. 이 MR 이 다른 열린 MR 위에 얹혀 있으면(그 끝 커밋이 조상) **대상을 그 브랜치로 자동 전환** — 먼저 머지해도 남의 내용이 몰래 들어가지 않고, 부모가 머지되면 GitLab 이 대상을 develop 으로 되돌린다(18.11 실측). 겹치는 쌍이 있으면 AI 가 '텍스트 충돌은 없지만 서로 깨뜨리는지' 를 판정해 덧붙인다(최대 3쌍). 양쪽 MR 에 코멘트(마커 갱신). **'그 MR 이 스스로 바꾼 파일' 의 기준은 각 MR 의 대상 브랜치와의 공통 조상**(GitLab MR diff 와 같은 기준) — 두 MR 끝 커밋의 쌍별 공통 조상을 쓰면 상대가 오래된 지점에서 갈라졌을 때 그 사이 develop 에 머지된 남의 커밋이 '내 변경' 으로 잡혀 애먼 MR 이 지목된다(2026-08-31 오귀속 4건 실측 후 -188 에서 수정) | **예**(나중 MR 의 텍스트 충돌만) |
| `stack-order` | lint | **스택 자식 MR 은 부모가 develop 에 들어간 뒤에만 머지**(-244). 대상이 다른 이슈의 작업 브랜치면 실패하고 MR 코멘트로 이유를 남긴다. 부모가 머지되면 GitLab 이 대상을 develop 으로 되돌린다. **다만 그때 파이프라인은 생기지 않는다** — 2026-09-01 의 "1초 안에 생긴다" 는 **API 재대상에서만** 참이었고, 브랜치 삭제로 인한 자동 재대상은 09-02~04 실측 16건 중 14건이 미생성이었다(`-295`). 그래서 `retarget-pipelines` 잡이 그 MR 을 찾아 파이프라인을 만들고, 그 파이프라인에서 검사가 통과한다. 부모가 이미 머지됐는데 브랜치만 남은 경우·브랜치가 사라진 경우에는 **대상을 develop 으로 되돌리고 통과**시킨다(억울한 차단 방지). `mm-ci-failed` 의 `needs` 에 **일부러 넣지 않아** 🔴 카드를 보내지 않는다 — 작성자가 잘못한 것이 아니라 아직 순서가 아닐 뿐이다. Draft 를 쓰지 않는 이유: 팀 규칙이 Draft 를 금지하므로(git-convention §5) CI 가 그 상태를 쓰면 규칙에 예외가 생기고 사람이 흉내 내 우회할 수 있다. 비상 스위치 `STACK_ORDER_ENFORCE=0`(경고만). **`needs: [mr-overlap]` + `when: always`** — 둘 다 `target_branch` 를 쓰므로 순서가 필요하고(동시에 돌면 서로의 결과를 못 보고 덮어쓴다), `when` 이 없으면 `mr-overlap` 실패 시 이 잡이 조용히 건너뛰어져 순서 강제가 사라진다(`only_allow_merge_if_pipeline_succeeds=false` 라 빨간 파이프라인도 머지할 수 있다). `-289`. **막는 방식은 미해소 스레드다**(`-297`) — 검사 실패만으로는 부족하다. `Pipelines must succeed` 는 MR 의 **마지막 파이프라인**만 보므로 재대상 직후의 낡은 초록을 그대로 통과시킨다(재대상 → 머지 간격 중위값 약 20초). 그래서 안내를 스레드로 달아 미해소로 두면 `All threads must be resolved` 가 **머지 버튼을 잠근다**. `retarget-pipelines` 가 만든 새 파이프라인에서 이 잡이 통과하면 스레드를 자동 해소한다. 사람이 손으로 해소해도 아직 스택이면 다음 실행이 되돌린다 | **예** — 단 하드 블록은 `Pipelines must succeed` 가 켜진 뒤 |
| `ci-tests` | lint | `scripts/ci/tests/run_all.py` — CI 스크립트 자신의 오프라인 회귀 테스트(네트워크·토큰 없이 `http()` 를 가짜로 바꿔 실행). `scripts/ci/**` 이 바뀐 MR 에서만 잡이 생긴다. 새 규칙·판정 로직을 넣을 때 여기에 케이스를 추가한다(-103). **테스트는 현재 시각에 의존하지 않는다** — 날짜 창을 쓰는 함수(`merged_mrs`·`merged_not_done`·`resurrected_branches`)는 `now` 를 주입받고 테스트가 고정값을 넘긴다. 2026-09-03 17:06 에 08-27 픽스처가 7일 창 밖으로 나가 develop 그대로 실패한 적이 있다(-281). **매일 스케줄에서도 돈다**(-289) — 코드가 안 바뀌어도 시간이 흘러 깨지는 것을 잡기 위해서다. 실패하면 `mm_digest.test_health()` 가 아침 카드 맨 위에 싣는다(**실패만**. `canceled` 는 중복 파이프라인 자동 취소일 뿐이라 보고하지 않는다). `mm-digest` 는 `needs: [ci-tests optional]` + `when: always` 라 테스트가 실패해도 카드는 나간다 | **예**. **릴리즈 MR(develop → master)은 Draft 금지와 대상 브랜치 규칙만 보고 나머지는 건너뛴다** — 이슈 키도 템플릿도 없는 정렬용 MR 이고 내용은 develop 에 들어올 때 이미 검사했다(-122) |
| `build-backend` | test | `eclipse-temurin:17-jdk` 컨테이너에서 `./gradlew --no-daemon bootJar`. **`backend/**` 가 바뀐 MR 에서는 컴파일 검사**(그전에는 백엔드가 MR 시점 빌드 검사 없이 머지됐다), develop 머지에서는 배포용 jar 아티팩트(3일 보관). Gradle 의존성은 `gradle-backend` 캐시로. `gradlew` 는 git 에 실행 권한이 없어 `chmod +x` 가 필요하다. 실측 2026-09-01: 캐시 있는 상태에서 48초 |
| `unity-compile` | test | **Unity C# 컴파일 + EditMode 테스트**(-271). EC2 도커 러너에서 GameCI 이미지(`unityci/editor:ubuntu-6000.5.4f1-base-3.2.2`, 태그는 프로젝트 버전과 같아야 한다 — 다르면 batchmode 가 프로젝트를 조용히 업그레이드한다)로 돈다. **MR(METAGAME 또는 .gitlab-ci.yml 변경) + develop push 에서 자동**. 실측 2026-09-02: 콜드 **8분 16초** → 웜 **70초**(`Library` 2.2GB 가 슬롯에 남아 증분 임포트). 그래서 `GIT_CLEAN_FLAGS: -ffdx -e METAGAME/Library` 가 **전역**에 있다 — 빌드 디렉터리는 그 슬롯의 모든 잡이 공유하므로 잡별 예외는 다음 잡의 `git clean` 에 지워진다(실측). `resource_group: unity` 로 동시 1개만(vCPU 4 · concurrent 2 라 겹치면 가벼운 검사가 굶는다). 라이선스는 **CI 변수가 아니라 러너 마운트**로 온다 — MR 은 보호 브랜치가 아니라 Protected 변수를 못 읽고, 끄면 값이 환경변수로 들어가 로그로 샐 길이 생긴다. 경로·계정 조건·만료(2026-10-09, `--update-ulf` 갱신)는 `docs/records/S15P21B202-271-unity-ci-license.md`. **종료 코드로 판정을 나눈다**(-309): `0` 통과(기존 실패만) · `1` **이 MR 이 만든** 컴파일 오류·새 테스트 실패 → **막는다** · `2` 기준선 없음·라이선스 만료 등 판단 불가 → ⚠ 경고. `allow_failure: exit_codes: [2]` 라 2 만 비차단이다. **MR 에서는 대상 브랜치를 합친 뒤 검사한다**(-342) — GitLab 의 MR 파이프라인은 `refs/merge-requests/N/head`, 곧 **소스 브랜치 끝**에서 돌고 합친 결과에서 도는 merged results pipeline 은 Premium 이다. 그래서 서로를 못 본 두 MR 이 **각자 초록인데 합치면 빨간** 의미 충돌이 지나갔다(2026-09-08 실측: `-331` 이 발사 실패 테스트 7건을 추가하고 바로 뒤 `-339` 가 중력을 `-2.8 → -5` 로 바꿨다. `-339` 의 MR 은 그 7건을 만난 적이 없어 판정 0 이었고 머지되자 3건이 깨졌다 — `-343`). `mr-overlap` 은 `git merge-tree` 로 **텍스트 충돌**만 보므로 이건 못 잡는다. 충돌하거나 대상 브랜치를 못 가져오면 **회귀로 단정하지 않고** 브랜치 끝에서 검사하며 코멘트가 그 사실을 적는다. **기준선은 잡 목록 API 로 찾는다** — 파이프라인 15개만 훑던 동안 창이 자주 비었다(`changes: METAGAME/**` 게이트라 문서만 머지되는 날에는 그 잡이 생기지 않는다. 2026-09-08 실측: 가장 최근 것이 7번째 뒤, 그 전은 14번째 뒤). 아티팩트 만료도 **3일 → 14일** 로 늘렸다 — 이 아티팩트의 존재 이유가 기준선이다. 그래도 기준선이 없으면 판정 2 를 유지하되 코멘트가 **머지를 막지 않는다는 사실**을 명시한다. **검사를 아예 못 한 경우에도 코멘트를 남긴다** — 워크트리에 충돌이 남아 Unity 를 돌리지 못하면 잡이 사유를 `UNITY_ABORTED` 로 넘기고 보고 스크립트가 그것을 코멘트로 올린다. 판정 2 는 비차단이라 파이프라인이 초록인데 코멘트가 없으면 "문서만 바뀌어 잡이 안 돌았다" 와 구분되지 않는다 — `-335` 가 AI 리뷰에서 고친 것과 같은 종류의 침묵이다. **기준선이 합친 base 보다 낡고 그 사이 `METAGAME/` 이 바뀌었으면 판정 1 을 2 로 낮춘다 — 다만 **테스트 실패에만**, 그리고 **기준선 브랜치와 머지 대상이 같을 때만** 적용한다(컴파일 오류는 아래 R2 로 무조건 막는다. 릴리스 MR 은 기준선이 `develop` 인데 합친 base 가 `master` 끝이라 서로 다른 브랜치를 견주게 되어 R1 이 상시 발동한다 — 그러면 릴리스 차단이 통째로 무력화된다. 이 가드가 없으면 낡은 기준선일 때 컴파일이 깨진 MR 이 통과한다 — `!259` 3차 AI 리뷰가 높음으로 잡았다)** — 그 차이에 대상 브랜치가 만든 실패가 섞여 있어 이 MR 탓으로 단정할 수 없다(`git diff --name-only <기준선 sha> <합친 base> -- METAGAME/` 로 판단한다. 안 바뀌었으면 테스트 거동이 바뀔 수 없으므로 낡은 기준선도 유효하고 그대로 차단한다). 2026-09-08 실측 반례: 기준선이 한 파이프라인만 더 옛것이었다면 `-339` 가 깨뜨린 6건이 **남의 MR 을 차단**했다. **컴파일 오류는 여기서 예외다 — 기준선을 보지 않고 막는다.** 합친 결과를 검사하므로 대상 브랜치 자신이 깨져 있으면 그 오류도 함께 실리는데, **그래도 막는 것이 맞다**: 깨진 브랜치 위로는 아무도 머지하면 안 된다. 대신 **코멘트가 단정하지 않는다** — "이 검사는 `<대상>` 를 합친 결과에서 돌았으니 그쪽이 이미 깨져 있었다면 그 오류도 함께 실린다. `<대상>` 의 최신 `unity-compile` 이 초록인지 먼저 확인하라" 를 붙인다. 한때 "오류 파일이 전부 이 MR 밖이면 경고로 낮춘다" 를 넣었다가 되돌렸다(`!259` 리뷰) — **파일 단위 귀속은 한 방향으로만 성립한다**(A 를 바꿔 B 에서 오류가 날 수 있다). "내 파일 밖" 은 결백의 증거가 아닌데 그것으로 차단을 풀면 09-02 사고 경로(컴파일 깨진 채 머지되어 develop 2시간 37분 정지)가 다시 열린다. **틀릴 때는 막는 쪽으로 틀린다.** 전에는 `allow_failure: true` 였고 잡이 **Unity 의 원시 종료 코드**(테스트가 하나라도 실패하면 2)를 그대로 뱉어 신호가 양쪽으로 다 틀렸다 — 기존 실패 25건뿐인 MR 세 건(`!228`·`!229`·`!230`)이 전부 빨간 ⚠ 였고(코멘트는 "새로 생긴 것 0" 이라고 정확히 적고 있었다), 반대로 컴파일이 깨져도 머지가 됐다 | **예** — `1` 이면 실제로 막는다 |
| `ai-review` | review | **v2(2026-08-27, -82)**: 역할별 리뷰어 3(범위·완료 조건·기록 / C#·Unity·네트워크 코드 / 에셋·씬·프리팹·구조)이 변경 내용에 따라 필요한 것만 **병렬** 실행되어 JSON 만 내고, 요약 에이전트가 "팀장이 확인할 것" 1~3개를 쓴다. 최종 코멘트 1개(재실행 시 갱신) = 결론(✅ 머지 가능 / ⚠️ 확인 후 머지 / ⛔ 보류) → 확인할 것 → **리뷰어 확인 5항목 판정표**(범위·완료 조건·씬/프리팹·구조·기록) → 발견 사항 표(높음·중간) → `<details>` 상세. 팀장은 ⚠️/❓ 만 본다. **리뷰어가 전부 실패해도 `❓ 판정 불가` 코멘트를 남긴다**(`-335`) — 각주에 실패한 리뷰어와 이유를 싣는다. 잡이 `allow_failure: true` 라 파이프라인이 초록이므로, 코멘트가 없으면 "문서만 바뀌어 건너뜀" 과 "리뷰가 죽음" 이 구분되지 않는다. 모델 이름·자격증명·CLI 경로 문제는 **항상 전부 실패로 나타나므로** 그 경우가 조용해선 안 된다. 실패 메시지는 **우리가 만든 `RuntimeError` 의 문구만** 싣고 다른 예외는 **종류만** 적는다(`TimeoutExpired` 등) — 상류 원문은 로그에만 남긴다(공개 코멘트에 실리므로). `-335` 는 문서에 이렇게 적고도 코드가 `str(e)` 로 원문을 실었고, `subprocess.TimeoutExpired` 의 `str()` 에는 **argv 전체**(모델명·경로·`--system-prompt` 내용)가 들어갔다. 그리고 **우리 `RuntimeError` 하나가 상류 응답 본문을 담고 있었다** — Anthropic API 오류 raise 가 `str(d)[:300]` 을 메시지에 넣었고, `fail_reason` 은 우리 `RuntimeError` 를 그대로 싣는다. 파일이 생긴 `-58` 이후 한 번도 바뀌지 않은 줄이었다(`!259` 5차 AI 리뷰). 지금은 본문을 로그에만 남기고 분류만 올린다 — 나머지 두 raise 는 처음부터 그 방식이었다 — `-342` 에서 코드를 문서에 맞췄다. **호출은 됐는데 JSON 이 아닌 리뷰어**도 각주에 `출력 해석 실패` 로 드러난다 — 전에는 `failed` 에 들어가지 않아 각주에서 사라졌다. **⛔ 로 파이프라인을 막는 게이트는 별 이슈 `S15P21B202-341` 이고 이 동작이 그 전제다** — 사라지는 신호 위에 게이트를 올리면 조용히 꺼지거나 인프라 사고 하나로 전부 막힌다. **백엔드**: `cli` = 러너 호스트 Claude Code CLI(Team 플랜, 채택) / `api` = `ANTHROPIC_API_KEY`. `AI_REVIEW_CLI=true` 또는 키가 있을 때만 잡 생성 | 아니오 |
| `deploy-backend` | deploy | **develop push + (`backend/**` 또는 `.gitlab-ci.yml`) 변경 + `when: manual`**. `.gitlab-ci.yml` 이 조건에 들어간 이유는 `build-backend` 가 이미 그 조건이어서 **두 잡의 조건이 어긋나 있었기 때문**이다(`deploy` 는 `needs: [build-backend]` 라 build 만 생기는 조합이 계속 만들어졌다, -309). 수동 잡은 눌리기 전까지 러너를 쓰지 않으므로 버튼이 더 자주 생기는 비용은 0 이다. `environment: production` 을 달아 **배포 이력을 GitLab 이 들고 있게** 했다. 빌드된 jar 을 호스트 스풀에 원자적으로 놓고(`.part`→rename) `RELEASE` 표시 파일을 쓰면, 호스트의 systemd path 유닛이 `deploy.sh`(스풀 **밖** `/opt/metagame-deploy/`, root 소유 — 스풀은 배포 러너에 rw 라 안에 두면 잡이 덮어쓸 수 있다, `-324`)를 돌린다 — jar 교체 → `compose up -d --build backend` → 준비 확인(`POST /auth/guest` 200, deviceId 고정이라 사용자 행은 최대 1개) → 실패 시 **직전 jar 로 자동 롤백**(3개 보관). 잡은 `STATUS` 에 자기 sha 가 찍힐 때까지 기다려 판정한다(최대 420초). **수동인 이유**: 재시작이 WebSocket 세션을 전부 끊어 팀원 테스트를 깨뜨린다. 도커 소켓을 잡에 노출하지 않으려고 파일로 신호를 준다(백엔드·DB 가 같은 호스트 — 소켓 = 사실상 root) |
| `mm-digest` | notify | **스케줄 전용**(CI/CD → Schedules 에 `30 9 * * *` Asia/Seoul, develop — 2026-08-28 등록, id 24. Maintainer 소유 필요. **매일**이며 실제 발송은 09:33 — GitLab 이 프로젝트마다 몇 분 오프셋을 더한다). needs-fix · 기한 초과 · 3일 정체 진행 중 · 스프린트 내 담당자 없음 · 24시간 넘게 열린 MR · **머지 뒤 다시 생긴 브랜치**(-110) · **최근 7일 안에 머지됐는데(merged_at) 완료 아닌 이슈**(R6 누락 감지, -83 — 규칙 실패 이메일은 유지하고 이 표가 보조. 규칙 실행 이력은 Jira 프로젝트 설정 → 자동화 → 감사 로그) 를 MM 표 카드로. 항목 없으면 보내지 않음 · **검사가 돌지 않은 MR**(-130 — ① 파이프라인이 아예 없는 MR: 소스 브랜치에 `.gitlab-ci.yml` 이 없다(master 기반 브랜치, 2026-08-28 MR !74 실측) ② 최신 파이프라인이 `DIGEST_PENDING_HOURS`(기본 2)시간 넘게 대기: 러너가 꺼져 있다. 둘 다 **빨간불이 없어 통과한 것처럼 보이는** 상태라 사람이 보지 않으면 그대로 머지된다) 를 MM 표 카드로. 항목 없으면 보내지 않음. **카드가 사실을 말하게 고친 것(-282)**: 헤더 시각을 KST 로(사흘 연속 UTC `00:33` 로 나갔다) · 🌿 는 `develop 에 없는 커밋 수`를 세어 **빈 브랜치**(지우면 끝)와 **유실 후보**(그 커밋이 어디에도 없다)를 가른다 · 🚀 는 **시작조차 안 한 `canceled`** 잡을 배포 시도로 세지 않는다(GitLab 이 같은 ref 의 새 파이프라인을 만들 때 대기 잡을 자동 취소한다 — 09-03 09:52 실측) | — | — |

정규식(단일 출처 `scripts/ci/ci_common.py`):
- 제목 `^\[(Plan|Art|Sound|UI|Character|Physics|Puzzle|Level|Network|Backend|Infra|Data|QA)\] (feat|fix|asset|scene|refactor|docs|test|perf|build|chore)(\(scope\))?: 요약 \(S15P21B202-\d+\)$`
- 브랜치 `^(hotfix/)?S15P21B202-\d+(-[A-Za-z0-9._-]+)?$`

### 로컬에서 돌려 보기
```powershell
# 위생 검사 — 현재 브랜치 vs develop
python scripts/ci/repo_hygiene.py origin/develop
# 특정 구간 (예: 초기 커밋이 LFS 규칙을 어겼는지)
python scripts/ci/repo_hygiene.py <base-sha> <head-sha>
```
`mr-lint`/`mr-title` 은 CI 환경변수(`CI_MERGE_REQUEST_*`)를 읽으므로 로컬에선 환경변수를 흉내 내야 한다(2026-08-27 [Infra] 가 MR !6 실데이터·깨진 제목 시나리오로 검증).

## 3. 필요한 변수 · 토큰 (Settings → CI/CD → Variables, 전부 **Masked**)

| 변수 | 무엇 | 누가 만드나 | 없으면 |
| --- | --- | --- | --- |
| `JIRA_USER`, `JIRA_API_TOKEN` | Jira 읽기(이슈 조회·JQL) — **읽기 전용으로 충분하다.** 쓰기 권한은 두지 않는다(-242): 이슈 상태 전이는 Jira 자동화 규칙(R5·R6·R9)만 하고 CI 는 검사·알림만 맡는다. 그래야 러너가 꺼져 있어도 이슈 완료가 영향받지 않고, 팀 공용 변수에 쓰기 권한이 남지 않고, 전이 규칙이 한 곳에만 있다 | Atlassian 계정 설정 → 보안 → API 토큰 — **스코프 토큰(`read:jira-work`, `read:jira-user`) 권장**. 스크립트는 `https://api.atlassian.com/ex/jira/{JIRA_CLOUD_ID}` 경로로 호출한다(스코프 토큰은 사이트 URL 로는 익명 취급 → 404, 2026-08-27 실측). `JIRA_CLOUD_ID` 기본값은 우리 사이트 | 키 실존·파트 검사 생략(경고) |
| `GITLAB_TOKEN` | MR 제목/본문 수정·코멘트·MR 목록 | Settings → Access Tokens → **프로젝트 액세스 토큰**, 역할 Developer, 스코프 `api` | 제목 보정·AI 코멘트 불가(제안만 로그) |
| `AI_REVIEW_CLI` | `true` — 러너 호스트의 로그인된 Claude Code CLI 로 AI 리뷰(Team 플랜, **채택 방식**) | 팀장 | `ai-review` 잡 생성 안 됨 |
| `AI_REVIEW_MODEL` | (선택) 기본 `claude-opus-5`, `AI_REVIEW_EFFORT` 기본 `medium`(팀 결정). `AI_REVIEW_SUMMARY_MODEL` 요약 에이전트 모델(기본 = 위), `AI_REVIEW_ROLES` 리뷰어 강제(예 `scope,code`), `AI_REVIEW_PARALLEL` 동시 실행 수(기본 3) | — | 기본값 |
| `ANTHROPIC_API_KEY` | (대안) API 종량제로 AI 리뷰 — Team 플랜엔 포함되지 않음, 별도 콘솔·크레딧 필요 | — | cli 백엔드 사용 |
| `MM_WEBHOOK_URL` | Mattermost 수신 웹훅 | MM 채널 → 통합 → 수신 웹훅 | `mm-digest` 생기지 않음 |
| `CI_ENABLE` | 파이프라인 전체 게이트 | 러너 등록 직후 팀장이 `true` | 파이프라인이 생성되지 않음(의도) |
| `UNITY_RUNNER`, `UNITY_PATH` | Unity 잡 게이트·경로 | Unity 러너 준비 시 | `unity-compile` 생기지 않음 |

토큰은 **CI 변수에만** 둔다 — 리포·문서·채팅 어디에도 적지 않는다. 프로젝트 액세스 토큰은 봇 사용자로 동작하므로 코멘트 작성자가 `project_…_bot` 으로 보인다(정상).

## 4. 러너

- **공유 러너**: 설정은 켜져 있지만(`shared_runners_enabled=true`) **실제 러너가 없다** — MR !10 첫 파이프라인이 9분 넘게 `pending`(2026-08-27 실측). 따라서 **프로젝트 러너 등록이 필수**. `image:` 는 Docker 러너용이며 shell 러너에서는 무시됨.
- **현재 배치(2026-09-01)** — 모든 잡이 **EC2 러너로 고정**돼 있다(`.gitlab-ci.yml` 의 `default.tags: [linux]`).

  | 러너 | 어디 | 실행기 | 몫 |
  | --- | --- | --- | --- |
  | `METAGAME-ip-172-26-13-34` | **EC2**(상시, 24시간) | docker · concurrent 2 · 부팅 자동 시작 | 앞으로 전부 |
  | `METAGAME-DESKTOP-UB20P0O` | 내 PC | shell(pwsh) | **예비 전용.** `unity-compile` 이 EC2 도커 러너로 옮겨져(`-271`) **이 러너를 노리는 잡이 하나도 없다.** 투입하려면 팀장이 이 러너에 `linux` 태그를 추가해야 한다 |

  단일 러너가 팀 전체의 단일 장애점이었다 — 08-28 러너 중단 1시간, 08-31 MAX_PATH 로 파이프라인 26건 전멸.
  EC2 는 리눅스·도커라 **MAX_PATH 문제는 원인 자체가 없다.**

- **잡을 러너에 묶는 방식**: 러너 쪽에서 'Run untagged 해제 + 태그' 를 하려면 Maintainer 권한이 필요하다
  (Developer 토큰은 러너 조회조차 403). 그래서 **잡 쪽에 태그를 건다** — `default.tags: [linux]` 한 줄로 모든 잡이
  `linux` 태그를 요구하게 되고, 그 태그를 가진 것은 EC2 러너뿐이다. `unity-compile` 만 `tags: [unity-win]` 으로 덮어쓴다.
- **내 PC 러너는 2026-09-01 부터 정지 상태다.** Unity 잡(`unity-compile`)은 `UNITY_RUNNER` 가 켜지기 전까지
  생성되지 않으므로(최근 파이프라인 실측: 없음) 지금 그 러너가 할 일은 없다. 켜 두면 잡이 양쪽으로 갈려
  환경 차이만 만든다.
- **🚨 EC2 러너 장애 시 복귀** — 파이프라인이 `pending` 에서 멈추면 아래 중 하나:
  ① **팀장**(가장 빠름): Runners 에서 내 PC 러너 **재개(unpause)** + 태그에 `linux` 추가. 두 곳 다 해야 한다 —
     잡이 `linux` 를 요구하므로 재개만으로는 안 받는다
  ② 러너 소유자(금준영): 내 PC 에서 관리자 PowerShell 로 `Start-Service gitlab-runner` (①의 태그 추가는 여전히 필요)
  ③ 팀장이 없을 때: `.gitlab-ci.yml` 의 `default.tags` 줄을 지우는 MR — 그러면 태그 없는 러너 아무 데서나 돈다.
     `build-backend` 는 잡에 태그가 박혀 있으니 그 줄도 함께 지운다.
     **`deploy-backend` 의 `tags: [deploy]` 는 절대 지우지 않는다**(-252). 지우면 그 잡이 무태그가 되어
     스풀이 마운트된 배포 러너 밖으로 나가거나, 반대로 다른 잡이 배포 러너로 흘러든다.
     배포는 팀장이 돌아온 뒤에 한다 — 비상 상황에 배포까지 밀어붙일 이유가 없다
  ④ EC2 쪽 1차 확인: `systemctl status gitlab-runner` · `journalctl -u gitlab-runner -n 50`

- **EC2 러너 설치**: `sudo RUNNER_TOKEN=glrt-… bash scripts/runner/setup-linux-runner.sh` (토큰은 환경변수로만 — `read -rs` 로 받아 화면·히스토리에 남기지 않는다). 러너는 아웃바운드 443 만 쓰므로 **UFW 변경 불필요**.
- **잡 이미지에는 git 이 있어야 한다**(2026-09-01 실측). `python:3.12-slim` 에는 git 이 없어 `ci-tests` 가 EC2 에서
  `FileNotFoundError: 'git'` 로 실패했다. 러너가 소스를 받을 때 쓰는 **헬퍼 이미지는 잡 컨테이너와 별개**라
  체크아웃은 되고 스크립트만 깨진다 — `mr-overlap`·`repo-hygiene` 도 같은 이유로 EC2 배정 시 깨진다.
  그래서 기본 이미지를 **`python:3.12`**(git 2.47.3 포함, 1.62GB)로 둔다. `pull_policy: if-not-present` 라 최초 1회만 받고,
  잡마다 `apt-get install git` 하는 것보다 빠르고 결정적이다.
- **AI 리뷰를 도커 러너에서 돌리는 법**(2026-09-01 구성 · 2026-09-04 `:ro` 로 변경): 호스트에 Claude CLI 를 설치해 로그인하고, `config.toml` 에서 **바이너리도 설정 디렉터리도 읽기 전용**으로 잡 컨테이너에 마운트한다.

  ```toml
  [[runners]]
    environment = ["CLAUDE_BIN=/usr/local/bin/claude", "CLAUDE_CONFIG_DIR=/claude-config", "DISABLE_AUTOUPDATER=1"]
    [runners.docker]
      cpu_shares = 256
      volumes = ["/cache", "/usr/local/bin/claude:/usr/local/bin/claude:ro", "/home/ubuntu/.claude:/claude-config:ro"]
  ```

  **자격증명을 환경변수가 아니라 마운트로 넣는 이유**: 환경변수는 CI 로그에 실수로 찍히기 쉽다. 바이너리는 버전
  디렉터리 대신 `/usr/local/bin/claude` 로 복사해 경로를 고정하고, `DISABLE_AUTOUPDATER=1` 로 CI 중 자동
  업데이트를 막는다.

  **`:rw` 가 아니라 `:ro` 다**(2026-09-04, `-290`). `:rw` 였던 동안에는 **아무 MR 잡이나 팀 계정
  자격증명을 읽고 덮어쓸 수 있었다** — MR 파이프라인은 소스 브랜치의 `.gitlab-ci.yml` 을 쓰므로 자기
  브랜치에 `cat /claude-config/...` 한 줄을 넣으면 됐다. `-252`(배포 스풀)와 같은 종류의 구멍이다.

  **그런데 `:ro` 만으로는 8시간마다 죽는다**(`-304`). 액세스 토큰 수명이 **8시간**이고, 만료되면 CLI 가
  리프레시 토큰으로 새 토큰을 받아 `~/.claude/.credentials.json` 에 **쓴다.** `:ro` 라 그 쓰기가 막혀
  `category=authentication http=401` 이 난다(실측: 만료 09:04 UTC · 실패한 잡 483767 이 09:20 UTC).

  > `-290` 에서 문서의 "설정 디렉터리는 토큰 갱신 때문에 쓰기가 필요하다" 를 **"실측으로 사실이 아니다"**
  > 라고 정정했는데, **그 정정이 틀렸다.** 근거로 삼은 성공(잡 482652)이 **만료 4.5시간 전**이었다.
  > 토큰이 아직 유효한 동안의 성공은 "갱신이 된다" 를 증명하지 않는다.

  **그래서 갱신을 호스트가 한다.** `claude-token-refresh.timer` 가 **20분마다** 호스트에서
  `claude -p` 를 돌려 토큰을 살려 두고, **같은 호출로 자격이 살아 있는지 점검해 알린다**
  (`scripts/runner/claude-token-refresh.{service,timer}` · `scripts/runner/claude_auth_check.py`).

  > **왜 20분인가**(`-309`). 처음 3시간으로 뒀는데 `!229` 의 `ai-review` 가 08:34 UTC 에 401 로
  > 죽고 08:48 틱 뒤에야 살아났다. 원인은 **CLI 가 만료된 뒤에만 갱신한다**는 것이다 —
  > 2026-09-06 EC2 실측: 만료까지 2.8시간 남은 상태에서 `claude -p` 를 돌려도
  > `.credentials.json` 의 mtime 이 바뀌지 않았다. 그러므로 **타이머 주기가 그대로 최대
  > 무응답 구간**이 된다. 8시간 TTL 을 자주 두드리는 것으로는 만료 직후의 구멍이 사라지지 않고,
  > 줄일 수만 있다.
  컨테이너는 읽기만 하고, `:ro` 의 보안 이득을 지킨다. root 는 남의 `600` 파일도 읽으므로 읽기에
  문제가 없다.

  **소유권 함정** — 잡 컨테이너는 **root** 로 돈다. `:rw` 였을 때 컨테이너가 갱신을 쓰면서 파일이
  `root:root 600` 이 되고, 그 뒤로 호스트의 `ubuntu` 는 **자기 자격을 읽지도 못했다**
  (`claude -p` → `Not logged in`). 그래서 `:rw` 로 되돌리는 것은 답이 아니다. 소유권이 넘어갔으면
  `sudo chown -R ubuntu:ubuntu /home/ubuntu/.claude` 로 회수한다.

  **자격이 죽으면 알림이 온다**(`-310`). 예전에는 알아챌 방법이 없었다 — `ai-review` 가
  비차단이라 **파이프라인은 초록이고 MR 코멘트만 사라졌다.** 09-06 에 `!229` 가 401 로 죽은 것을
  잡 로그를 뒤져서야 찾았다. 이제 매 회차가 결과를 판정하고 원인별 문턱을 넘으면 MM 으로 보낸다.

  | 원인 | 알림 문턱 | 왜 |
  | --- | --- | --- |
  | `authentication` | 연속 **2회**(약 40분) | 사람이 재로그인해야 한다. 저절로 낫지 않는다 |
  | `invalid_request` | 연속 2회 | 옵션·모델명 문제. 역시 사람 일 |
  | `network`·`server_error` | 연속 3회(약 1시간) | 대개 저절로 낫는다 |
  | `rate_limit` | 연속 6회(약 2시간) | 구독 한도는 정상 동작의 일부다 |
  | 복구 | 즉시 1회 | "이제 괜찮다" 를 알아야 다음 실패가 신호가 된다 |
  | 계속 실패 | 6시간마다 1회 | 도배 방지 |

  실패 분류는 `ai_review.classify_claude_failure` 와 **같은 규칙**이다. 호스트 스크립트는
  `/opt` 에서 도는 독립 파일이라 `scripts/ci` 를 임포트할 수 없어 규칙이 두 벌인데,
  `test_claude_auth_check.py` 가 같은 입력으로 두 판정을 비교해 **한쪽만 고치면 실패**한다.

  **리프레시 토큰은 2026-09-30 에 만료된다**(`.credentials.json` 의 `refreshTokenExpiresAt` 실측).
  그때는 사람이 러너 호스트에서 재로그인해야 하고, 그 전까지는 **3일 전부터 하루 한 번** 경고가
  온다 — 터진 뒤에 아는 것과 미리 아는 것은 다르다. 근본적으로는 `ANTHROPIC_API_KEY` 백엔드로
  바꾸면 마운트·갱신·소유권 문제가 모두 사라진다(비용은 든다).

  **웹훅 URL 은 EC2 의 `/etc/metagame/alert.env`(600, ubuntu 소유)에 사람이 직접 넣는다.**
  없어도 점검은 돈다 — journal 에만 남고 알림이 생략된다. 값은 이슈·문서·채팅에 적지 않는다.

  **`cpu_shares = 256`**(2026-09-04, `-291`). EC2 한 대가 CI·백엔드·Mirror 게임 서버를 전부 돌리는데
  러너에 자원 제한이 없었다. `unity-compile` 이 4 vCPU 를 7분 30초 동안 전부 쓴다(`-271` 콜드 실측).
  **하드 상한(`cpus`)이 아니라 상대 가중치를 쓴 이유**: `cpus` 는 아무도 CPU 를 안 쓰는 시간에도 CI 를
  느리게 만든다(7분 30초 → 20분). `cpu_shares` 는 경합할 때만 작동해서, 놀고 있으면 CI 가 기계를 다 쓰고
  게임 서버가 일하기 시작하면 양보한다. **배포 러너에는 걸지 않는다** — 배포는 짧고 급하다.
  실측: 컨테이너 안에서 `auth status` → `loggedIn: true`, `ai-review` 잡이 EC2 에서 **64초에 성공**(파이프라인 176070).
- ~~러너 배치 계획~~(아래는 이전 계획 기록 — 지금은 Unity 잡도 EC2 에서 돈다, `-271`)  <!-- drift-ok -->: ① **상시 러너 = 지급 EC2(Linux, Docker 실행기, 24시간)** — mr-title·mr-lint·repo-hygiene·ai-review·mm-digest 전부 여기서. `sudo RUNNER_TOKEN=glrt-… bash scripts/runner/setup-linux-runner.sh` 한 줄(Docker·gitlab-runner 설치·등록·systemd). ② **Unity 러너 = Unity 6000.5.4f1 설치 Windows PC**(태그 `unity-win`, 근무 시간만 켜져도 됨 — allow_failure 잡). ③ EC2 지급 전 임시: 팀 PC 1대에 Windows 러너(아래) — 꺼져 있는 시간엔 파이프라인이 pending 으로 대기하고, 다이제스트는 그 시각에 러너가 없으면 건너뜀. EC2 오면 임시 러너는 GitLab 에서 삭제.
- **프로젝트 러너 등록(Windows, 20분)**: ① 팀장: Settings → CI/CD → Runners → *New project runner* → 태그 `windows`(예비 투입 시 `linux` 추가. **`unity-win` 은 이제 붙이지 않는다** — 그 태그를 노리는 잡이 없다, `-271`), **Run untagged jobs ✔** → 토큰(`glrt-…`) 발급해 등록할 사람에게 직접 전달 ② 등록자(관리자 PowerShell): `$env:RUNNER_TOKEN="glrt-…"; .\scripts\runner\setup-windows-runner.ps1 [-Unity]` — [setup-windows-runner.ps1](../scripts/runner/setup-windows-runner.ps1) 이 바이너리 다운로드·Python 3.12 확인/설치·등록·서비스 설치를 한다 ③ 팀장: 변수 **`CI_ENABLE=true`** → 그때부터 MR 파이프라인이 생성됨(그 전에는 pending 노이즈를 막기 위해 파이프라인을 만들지 않음). 주의: 러너 셸은 **`pwsh`(PowerShell 7, MSI 시스템 설치)** 여야 한다 — 5.1(`powershell`)은 잡 스크립트를 cp949 로 읽어 한글 CI 변수(MR 제목·본문·커밋 메시지)에서 파서 오류로 모든 잡이 실패(2026-08-27 파이프라인 #175296 실측). Store 판 pwsh 는 SYSTEM 서비스가 실행 못 하므로 `runner-use-pwsh.ps1` 로 MSI 설치 + 셸 전환. `glrt-` 토큰은 생성 직후 한 번만 보이니 그때 복사해 전달(`Verifying runner... is not valid` = 토큰 불일치)  <!-- drift-ok -->
- **Unity 러너**(2단계): Unity **6000.5.4f1** 이 설치된 Windows PC 에서 같은 스크립트를 `-Unity` 로 실행(태그 `windows,unity-win`). 그 뒤 변수 `UNITY_RUNNER=true`, `UNITY_PATH=C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe`. 현재 SSAFY PC 에는 6000.0.81f1 만 있어 버전을 맞춰야 함. 러너 PC 는 절전 금지, 프로젝트가 커지면 `GIT_DEPTH` 조정.
- **AI 리뷰(cli 백엔드) 준비 — 러너 호스트에서**: ① 러너를 돌릴 **사용자 계정**으로 로그인한 터미널에서 Claude Code CLI 설치(`irm https://claude.ai/install.ps1 | iex`) 후 `claude` 실행 → `/login` 으로 팀 Claude 계정 로그인(브라우저) → `claude -p "PONG"` 이 답하면 OK ② 러너 잡이 그 로그인을 쓰게 연결: 관리자 PowerShell `.\scripts\runner\runner-claude-env.ps1` — config.toml 의 러너 `environment` 에 `CLAUDE_CONFIG_DIR=<사용자>\.claude`·`CLAUDE_BIN=<사용자>\.local\bin\claude.exe` 를 넣고 서비스 재시작(SYSTEM 계정 서비스가 사용자 프로필의 자격 파일을 그대로 읽음 — 2026-08-27 실측, 비밀번호·토큰 복사 불필요). (서비스를 사용자 계정으로 돌리는 방식은 비밀번호가 명령줄에 노출되는 문제로 채택하지 않음) ③ 팀장: 변수 `AI_REVIEW_CLI=true`(+ 코멘트용 `GITLAB_TOKEN`). 자격은 러너 호스트의 사용자 프로필(`~\.claude`)에만 있고 GitLab 변수·리포에 들어가지 않는다 — EC2 시절과 같은 구조. 사용자의 VS Code Claude Code 와 러너가 같은 자격 파일을 공유하므로 `/logout` 하면 CI 리뷰도 멈추고, 토큰 갱신이 겹쳐 "Not logged in" 이 나면 사용자 세션에서 `claude` 재로그인. 주의: `claude -p --bare` 는 keychain 을 읽지 않아 "Not logged in" 이 되므로 쓰지 않음. 호스트 사용자 비밀번호가 바뀌면 서비스가 멎으니 스크립트 재실행. 이 방식은 팀 구독 사용량을 소비하고, 구독 OAuth 의 자동화 사용은 Anthropic 이 Pro/Max 개인 CI 용도로만 공식 안내하므로 정책 변경 시 `api` 백엔드로 전환할 것(팀 결정 2026-08-27)
- 러너 등록·변수는 **Maintainer** 만 가능(Developer 는 403).

## 5. Mattermost 알림 설계 — 채널 1개, 원칙 3개 (2026-08-27 v2)

원칙: ① **모든 메시지는 조치할 사람을 @멘션** — 멘션 대상이 없는 메시지는 보내지 않는다(noise) ② 첫 줄에 **등급 이모지 + 굵은 제목 + 링크**, 둘째 줄에 **@멘션 + 조치** ③ 일상(🟢 요청·📋 요약)과 조치 필요(🔴 실패·🟠 규칙 위반)를 이모지로 구분. MM 서버가 웹훅 이름 재정의를 막아 발신자는 웹훅 생성자(금준영 BOT)로 보이므로 첫 줄이 곧 제목이다. 계정 매핑의 단일 출처는 [`scripts/ci/team.json`](../scripts/ci/team.json)(GitLab·MM·Jira accountId).

**공통 형식(표 카드, 2026-08-27 v2.1)** — 첫 줄 `등급 이모지 **제목**`, 둘째 줄부터 MM Markdown 표 `| 항목 | 내용 |`: 이슈/MR(링크) · 담당/옮긴 사람(@멘션 + 이름) · 실패한 잡·확인할 것(CI) · **조치**(한 줄). 다이제스트는 항목별 `| 이슈 | 담당 |` 표. Jira 쪽 문구는 **규칙 자신**(R2·R5·R7·R9 의 `jira.issue.outgoing.webhook` 액션 본문)이, CI 쪽은 `mm_notify.card()`·`mm_digest.fmt_issues()` 가 단일 출처. Jira 쪽을 고칠 때는 규칙을 직접 편집한다 — 교정 스크립트는 리포에 두지 않는다(부록 A).

| 등급 | 사건 | 보내는 곳 | @멘션 | 문구 |
| --- | --- | --- | --- | --- |
| 🔴 조치 필요 | MR 파이프라인 실패(mr-lint·repo-hygiene) | CI `mm-ci-failed`(`when: on_failure`) | **MR 작성자** | `🔴 **CI 실패** — [!N 제목](url)` / `@작성자 · 실패: [mr-lint](로그) · 확인할 것: … · 고친 뒤 push 하면 자동 재검사` · **2026-08-28(-102)**: 실패한 잡 로그의 `❌` 줄을 뽑아 카드의 **이유** 칸에 최대 3개 싣고, 같은 내용을 **MR 코멘트**(마커 `ci-failed:metagame`, 재실행 시 갱신)로도 남긴다. **Draft MR 은 카드를 보내지 않는다**(아직 리뷰 요청이 아니므로 — 🟢 리뷰 요청 카드와 같은 기준, 2026-08-28). 코멘트는 그대로 남는다 — 로그를 찾아 들어가지 않아도 원인을 본다. 다시 통과하면 `mm-ci-resolved` 잡이 같은 코멘트를 **✅ 해소됨**으로 바꾼다. **두 잡의 `needs` 는 항상 같아야 한다**(-289) — 달랐던 동안에는 `ci-tests`·`mr-overlap` 이 실패해도 '해소됨' 이 찍혔고, 같은 파이프라인에서 🔴 와 ✅ 가 같은 마커에 동시에 써서 결과가 비결정적이었다. `ci_resolved()` 는 자기 판단을 하지 않으므로 게이트는 `needs` 뿐이다. `test_ci_signal.py` 가 일치를 검사한다 |
| 🔴 | develop(기본 브랜치) 파이프라인 실패 | GitLab 내장 통합(Pipeline, default branch) | 없음 — 유일한 예외(빈도 극히 낮음, 인프라가 봄) | GitLab 고정 형식 |
| 🟠 규칙 위반 | 손으로 진행 중 → 되돌림 (R5) | Jira 웹 요청(되돌림 분기에서만) | **끈 사람**(initiator) | `🟠 **되돌림 (진행 중 → 해야 할 일)** — [KEY](url) 제목` / `@user · 진행 중은 브랜치 만들기로만` |
| 🟠 | 손으로 완료 → 되돌림 (R9) | Jira 웹 요청 | **끈 사람** | `🟠 **되돌림 (완료 → 진행 중)** …` / `@user · 완료는 MR 머지로` |
| 🟠 | 제목 형식 위반 · 상위 Epic 없음 (R2) | Jira 웹 요청 | 담당자(없으면 보고자) | `🟠 **제목 형식 위반** …` / `@user · [파트] 제목 형식으로` |
| 🟠 | MR 머지 없이 닫힘 (R7) | Jira 웹 요청 | 이슈 담당자 | `🟠 **MR 이 머지 없이 닫힘** …` / `@user · 재오픈 또는 팀장에게 정리 요청` |
| 🟢 요청 | MR 열림 = 리뷰 요청 | CI `mm-mr-opened`(MR 의 **첫 파이프라인일 때 1회** — id 가 가장 작은 파이프라인만 보낸다. 예전엔 파이프라인 개수로 판정해 재시도로 2개가 된 MR 이 영구 미발송이었다(-134), Draft 제외. **`needs: [mr-title]`** — 보정된 제목을 싣는다. 동시에 돌던 동안에는 카드에 브랜치 이름 제목이 실렸다(-322). 제목이 생성 시점부터 맞게 하는 쪽은 훅이다: 첫 push 때 `merge_request[title]`·`[target_branch]` 를 채운 새 MR 링크를 찍어 준다) | **리뷰어(기본 팀장)**, 작성자 표기 | `🟢 **리뷰 요청** — [!N 제목](url)` / `@suho6347 · 작성 dorem96 · …` |
| 🟢 완료 / 🔴 실패 | 백엔드 배포 결과 (S15P21B202-144) | CI `deploy_backend.py` → `mm_notify.deploy()` (잡을 따로 두지 않는다 — 결과를 아는 곳이 스크립트다. 잡 종료 코드는 성공/롤백/무응답/실패를 `실패` 하나로 뭉갠다) | **실패·롤백·무응답에만 Infra** — 성공은 멘션 없음 | `🟢 **배포 완료**` + 커밋·결과·누른 사람·링크, 그리고 **"진행 중이던 접속이 모두 끊겼습니다"** (재시작이 WebSocket 세션을 전부 끊는다 — 같은 서버로 테스트하던 사람은 이 알림 말고는 알 방법이 없다. 그래서 **성공도 보낸다**) / `🔴 **배포 롤백**` 은 **지금 서버가 직전 jar 로 돈다**는 것을 명시하고, `🔴 **배포 무응답**` 은 서버 상태를 단정하지 않는다(배포가 시작됐는지조차 모른다) |
| 📋 요약 | 매일 09:30 다이제스트(발송 09:33) | CI `mm-digest`(스케줄) | 항목별 담당자(없으면 팀장) | `📋 **아침 점검 — 확인 필요**` + needs-fix·기한 초과·3일 정체·담당자 없음·24h 넘은 MR·🔗 머지됐는데 완료 안 됨(대상 develop)·🧩 스택 자식 MR(부모가 develop 에 도달한 것만)·검사 안 돈 MR·충돌하는 열린 MR. **꼬리에 🚀 배포 상태 한 줄**(-144): `🚀 배포: \`sha\` (09/01 21:10) · 백엔드 변경 N건 배포 대기`. **블록이 아니라 꼬리다** — 배포 상태는 조치 항목이 아니므로 이것만으로 카드를 보내지 않는다("항목 없으면 보내지 않는다" 원칙 유지). 마지막 시도가 `failed`·`canceled` 로 남아 있으면 그때만 별도 블록으로 Infra 를 부른다. **지금 서버(마지막 성공)와 마지막 시도를 섞지 않는다** — 시도가 실패해도 서버는 그 전 성공분으로 돌고 있다 |
| 🟢 완료 | MR 머지 = 이슈 완료 (R6) | Jira 웹 요청 | **이슈 담당자**(= MR 작성자) | `🟢 **머지 완료 · 이슈 완료**` / 이슈·담당·MR 링크·"브랜치 자동 삭제됨, 후속은 새 이슈로" — 2026-08-27 추가 |
| ✂ 보내지 않음 | push · 코멘트 · 이슈 생성 | — | 태그 대상 없음 | Jira 상태 전이·MR 화면으로 충분 |

**GitLab 내장 통합 설정**: Trigger 는 `Pipeline` 만(Notify only broken pipelines ✔, Branches = Default branch). `Merge request` 트리거는 **끈다**(`mm-mr-opened` 가 멘션 포함으로 대체). Push·Note 등은 끔.

**Jira 웹 요청 문구**: `jira.issue.outgoing.webhook` 액션의 `customBody` = `{"text": "…"}`. 멘션은 accountId → MM 아이디를 smart value 조건으로 내장: `{{#if(equals(issue.assignee.accountId, "<id>"))}}@mm{{/}}` ×6 (initiator 도 동일). 요약·이름은 `{{…jsonEncode}}`. 팀원 변경 시 `team.json` 과 규칙 문구(`mm_msgs.py` + apply 스크립트)를 함께 갱신.
  - **실측(2026-08-27)** ① 조건식 리터럴은 **큰따옴표만** — 작은따옴표는 파서 오류로 액션 자체가 실패(댓글 미게시). ② customBody 안에서 `json.dumps` 가 만든 `\"` 는 파서가 리터럴로 못 읽어 조건이 항상 거짓 → `{{…}}` 안의 따옴표는 이스케이프하지 않은 채 두고(렌더링 후 정상 JSON), 그 외 문자열만 이스케이프(`mm_msgs.body_json`). ③ MM 표는 제목 줄과 표 사이에 **빈 줄**이 있어야 렌더링된다.

**한계**: Jira 자동화 규칙 자체의 *실행 오류*(-46 의 "이슈가 존재하지 않음" 같은)는 MM 으로 보낼 수 없다 — Jira 는 규칙 소유자 이메일 알림만 지원. 감사 로그가 유일한 창이며 `mm-digest` 의 needs-fix·정체 항목이 결과적으로 드러내 준다.
## 7. 지난 프로젝트(FlowerKnot, `s15-webmobile2-sub1/S15P11B210`) 교훈 반영 — 2026-08-27 조사

지난 프로젝트는 EC2 systemd Worker 가 open MR 을 폴링해 **Opus 5 / effort medium** Reviewer 2개(코드·테스트 품질)를 병렬 실행하고 Critical/High 0건이면 Project Bot 이 Squash Auto-Merge 까지 했다(`docs/ai-review-automation.md`, `infra/ai-review/worker.py`). 이번 프로젝트는 **팀장 머지 원칙**이라 Auto-Merge·Gate 는 채택하지 않고, MR 파이프라인 안의 비차단 잡 1개로 단순화했다. 계승한 것:

| 항목 | 지난 프로젝트 | 이번 적용 |
| --- | --- | --- |
| 모델·effort | `claude-opus-5` + `--effort medium` | 동일 (`AI_REVIEW_MODEL`/`AI_REVIEW_EFFORT`) |
| CLI 격리 플래그 | `--tools "" --setting-sources user --strict-mcp-config --disable-slash-commands --permission-mode dontAsk --no-session-persistence`, 호출마다 일회성 `CLAUDE_CONFIG_DIR` | 플래그 동일. 자격은 러너 호스트 사용자 프로필을 공유(`CLAUDE_CONFIG_DIR` 주입) — 단일 러너·단일 계정이라 일회성 디렉터리는 생략 |
| 프롬프트 원칙 | MR 본문·diff 는 untrusted 구분자로 감싸고, "diff 에 없다 ≠ 리포에 없다", 추측 금지, 한국어 출력 | 시스템 프롬프트에 반영 |
| 실패 처리 | stdout/stderr 원문을 로그·코멘트에 남기지 않고 카테고리만(`rate_limit`·`authentication`·`server_error`·`network`·`invalid_request`), 인증·요청 오류는 재시도 안 함 | 카테고리 분류 로그 반영(비차단 잡이라 재시도는 파이프라인 재실행으로) |
| 리뷰 타임아웃 | 900초 | `AI_REVIEW_TIMEOUT` 기본 600초 |

운영 사고 기록 중 우리에게도 해당하는 것:
- **오탐 알림은 신뢰를 깎는다**(2026-08-04, 이미 고쳐진 과거 실패를 "지금 실패"처럼 알림) → `mm-digest` 는 "지금 상태"만 보고(오래 열린 MR·현재 needs-fix), 과거 이벤트를 재통보하지 않는다.
- **default branch 실패는 가장 강한 알림이 필요한데 아무도 mention 되지 않았다**(2026-08-04, 20분 방치) → GitLab MM 통합의 Pipeline 이벤트는 켜 두고, develop 실패는 다이제스트에도 포함할 것(후속).
- **develop 푸시 파이프라인은 자동 취소하지 않는다**(`workflow.rules` 의 `auto_cancel: on_new_commit: none`, -309).
  2026-09-06 19:43 에 `-303`(릴레이)이 머지돼 `build-backend` + `deploy-backend` 가 생겼는데,
  1분 뒤 `-304` 가 머지되면서 GitLab 이 앞 파이프라인을 자동 취소했다 — `default.interruptible: true`
  라 아직 시작하지 않은 `build-backend` 가 취소 대상이었다(`deploy-backend` 는 `interruptible: false`
  였지만 **시작하지 않은 잡은 그것과 무관하게 취소된다**). 그리고 `-304` 는 `backend/` 를 건드리지
  않아 **새 파이프라인에는 백엔드 잡이 아예 없었다.** 결과: 릴레이 수정이 develop 에 들어갔는데
  **누를 배포 버튼이 어디에도 없고, 파이프라인은 전부 초록이라 아무도 몰랐다.** 백엔드는 이틀 전
  코드로 계속 돌았다. MR 파이프라인은 그대로 둔다 — 거기서는 새 push 가 앞 것을 죽이는 게 맞다.
- **아침 카드가 배포 표류를 말한다**(`mm_digest.backend_drift`, -309). `pending`(눌리지 않은 배포 잡 수)
  만으로는 위 상황을 볼 수 없다 — **없는 잡은 셀 수 없다.** 그래서 마지막 성공 배포의 커밋과
  develop 을 직접 비교해 `backend/` 변경이 있으면 경고한다. 비교를 못 했으면(`None`) 경고하지 않는다 —
  빈 목록과 섞으면 조용한 오탐·미탐이 된다.
- **GitLab 내장 MM Pipeline 알림을 all branches 로 두면 MR 파이프라인 자동 취소가 "failed" 로 통보돼 노이즈** → 내장 알림은 default branch 로 한정(체크리스트 §5 반영).
- **PowerShell 5.1 러너의 한글 변수 파손**은 이번 프로젝트에서 새로 겪은 것(Linux Docker 러너였던 지난 프로젝트엔 없음) → pwsh 필수.
- **테스트 없는 알림 항목은 조용히 죽는다**(2026-08-31 실측) — 아침 점검의 `🌿 머지 뒤 다시 생긴 브랜치` 는 `resurrected_branches()` 가 표 행을 만들고도 **반환하지 않아** 도입(-110) 이후 한 번도 나가지 않았다. 호출부가 `if res:` 로 판단하므로 오류도 없이 항목만 사라진다. 미룬 테스트를 옮기다가 잡혔다(-133) → **알림 항목을 만들면 그 자리에서 오프라인 테스트를 같이 만든다.**
- 리뷰 결과는 사실 검증 전까지 증거가 아니다 — 지난 프로젝트도 Medium/Low 는 참고, Critical/High 만 차단으로 썼다. 우리는 전부 참고(비차단)이며 판단은 팀장.
### 비결정 테스트가 무관한 MR 을 막지 않게 한다 (S15P21B202-347)

판정("기준선에 없던 실패는 이 MR 탓")은 **실행이 결정적이라는 것을 전제한다.** 물리
시뮬레이션 테스트 몇 개가 경계에 앉아 있어 그 전제가 깨져 있다.

**실측(2026-09-09)**: `!267` 의 `METAGAME/` 트리가 기준선 잡과 **바이트 단위로 동일**했다
(merge-base 가 develop 팁이고 diff 0). 같은 커밋으로 4회 돌려 **4회 다 실패**했고 기준선은
통과였다 — 그래서 MR 3건이 동시에 막혔다. 실패한 것은 `ShipAscentTests` 의 하중 시험이고,
추력을 무게중심에서 벗어난 노즐에 주기 때문에 탑이 조금만 비뚤어도 접합부가 끊어진다.

장치는 **둘**이고 둘 다 필요하다.

| 장치 | 무엇 | 왜 이것만으로는 부족한가 |
|---|---|---|
| **기준선 합집합** (`BASELINE_RUNS=3`) | 대상 브랜치의 최근 3회 실패를 합쳐 기준선으로 쓴다 | 그날 건은 최근 develop 3회가 **전부 통과 쪽**이라 합집합에 없었다 |
| **필터 재실행** (`RECHECK_RUNS=2`) | 새 실패가 있으면 **그 테스트만 골라** 2회 다시 돌린다. 한 번이라도 **통과**(`Passed`)하면 플래키로 보고 판정에서 뺀다. `Skipped`·`Inconclusive` 는 통과로 세지 않는다 — 스킵된 것을 면제하면 진짜 회귀가 빠진다 | 실패율이 100%에 가까우면 재실행도 계속 실패한다 |
| **재실행 상한** (`RECHECK_MAX_NEW=30`) | 새 실패가 이보다 많으면 **재실행을 건너뛴다.** 그 규모는 플래키가 아니고 `-testFilter` 명령줄만 길어진다. **건너뛴 사실을 로그에 적는다** | 상한 근처의 진짜 플래키 무리는 못 걸러낸다 |

**기본값은 "막는다" 쪽이다.** 재실행을 안 돌렸거나, 결과가 없거나, 결과에 그 이름이 없거나,
결과가 통과도 실패도 아니면 **면제하지 않는다** — 확인하지 못한 것을 면제하면 진짜 회귀가
빠져나간다. 컴파일 오류에는 아예 끼어들지 않는다(09-02 사고 경로).

값이 어디 있는지: `RECHECK_RUNS`·`RECHECK_MAX_NEW` 는 **`.gitlab-ci.yml` 의 `unity-compile`
잡 변수에만**(잡이 Unity 를 돌리므로), `BASELINE_RUNS` 는 **`scripts/ci/unity_report.py`
상수에만**(스크립트가 기준선을 조회하므로) 있다. 한 값을 두 곳에 두면 한쪽만 바뀐다.

재실행 전에 지난 결과 파일을 지운다 — 워크스페이스가 재사용되면 **옛 통과 기록으로 새
실패가 면제될 수 있다.** `-testFilter` 값은 `unity_report.py` 가 **정규식 이스케이프**해
파일로 넘긴다(파라미터화 이름 `Load(1,2)` 를 그대로 넘기면 매칭에 실패한다).

**면제한 것은 코멘트에 적는다.** 그 테스트만 골라 돌려 통과한다는 것은 실패가 **실행
맥락에 달려 있다**는 뜻이고, 원인은 셋 중 하나다.

1. 원래 있던 무작위성 → 그 MR 탓이 아니다 (대부분 이 경우)
2. 다른 테스트가 남긴 상태에 걸려 넘어진 것(테스트 격리 결함) → 그 MR 탓이 아니다
3. **그 MR 이 전역·정적 상태를 오염시켜** 다른 테스트를 깨뜨린 것 → **그 MR 이 원인이다**

③ 이 있으므로 "격리에서 통과했으니 무죄" 라고 단정하지 않는다. 그래도 면제하는 쪽을 택한
이유는 ③ 이 **그 MR 이 `METAGAME/` 을 건드렸을 때만** 가능하고 그 경우 사람이 목록을 보면
몇 초에 가릴 수 있는 반면, ①②로 막으면 아무 잘못 없는 사람이 원인을 찾을 방법이 없기
때문이다. 코멘트가 세 갈래와 ③ 을 가리는 방법을 그대로 적는다.

**대가를 안다**: 합집합은 최근 대상 브랜치에서 한 번 실패한 적 있는 테스트를 **진짜 회귀여도
넘긴다.** 그래서 3 으로 좁게 두고 어느 잡이 무엇을 보탰는지 로그에 남긴다. `BASELINE_RUNS=1`
로 되돌리면 예전 동작이다.

**재실행 경로는 실측으로 확인했다 (2026-09-09).** 이 경로는 **새 실패가 있을 때만** 돌아
정상 MR 에서는 한 번도 실행되지 않으므로, 일부러 실패하는 테스트 셋(하나는 괄호·쉼표가 든
파라미터화 이름)을 넣어 한 번 재 봤다. `!272` 잡 501171:

```
재실행 계획: 새 실패 3건 → unity-recheck.txt
새 실패 3건 — 그 테스트만 골라 2회 다시 돌린다
재실행 unity-recheck-1.xml: 대상 3/3건 · 이 회차 통과 0건
재실행 unity-recheck-2.xml: 대상 3/3건 · 이 회차 통과 0건
재실행 판별: 플래키 0건 · 계속 실패 3건 · 확인 못함 0건
판정 1 (이 MR 이 만든 문제 — 막는다)
```

- **`대상 3/3건`** — 세미콜론 구분자와 정규식 이스케이프 둘 다 통한다. 이스케이프가 안 되면
  괄호가 든 이름만 빠져 `2/3` 이 된다. 이 숫자가 `대상 0/N` 이면 구분자가 안 통하는 것이고,
  그때는 `unity_report.py` 가 `[warn] ... -testFilter 가 이름을 못 잡은 것으로 보인다` 를 찍는다
- **판정 1** — 재실행을 붙였다고 진짜 실패가 통과하지 않는다
- **비용**: 재실행 2회에 잡이 **2.2분 → 4.6분**. 새 실패가 없으면 재실행이 아예 안 돌아 0이다

**근본은 테스트다.** 이 장치는 증상을 가린다. `-348` 이 `TowerCollapseTests` 에 한 처방(각도
단정 → 결과 수준)이 `ShipAscentTests` 에도 필요하다.

## 6. 앞으로
- 파이프라인 필수(Settings → Merge requests → Pipelines must succeed)는 mr-lint·repo-hygiene 이 1주 안정화된 뒤 켠다 — 그 전에는 빨간불을 팀장이 보고 머지 여부 판단
- ~~`unity-compile` 은 `allow_failure: true` 로 시작해 안정화 후 차단으로~~ → **완료**(-309):  <!-- drift-ok -->
  기존 실패는 통과, **이 MR 이 만든 것만** 차단한다. 기준선 비교로 가르므로 "안정화" 를 기다릴 필요가 없었다
- AI 리뷰 결과의 유용성은 스프린트 회고에서 평가(모델·프롬프트·비용 조정)
