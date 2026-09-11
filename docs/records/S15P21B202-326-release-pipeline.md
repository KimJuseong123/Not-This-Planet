# 릴리스 파이프라인 — master 머지 → Windows 빌드 → GitLab Release (S15P21B202-326)

[문서 안내](../README.md) → [작업 기록](README.md) → 서버·배포

master 머지에서 Windows 빌드·패키지·태그·GitLab Release를 생성하는 파이프라인 구현 기록입니다. 버전 결정과 실패 진단을 설명하며 첫 릴리스 실측란은 별도로 남깁니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

> 2026-09-07 설계·구현. **실측(§4)은 첫 릴리스 뒤에 채운다.**
> 규칙 쪽은 [git-convention §8](../git-convention.md)(-325), 지도는 [ci-cd-pipeline.md](../ci-cd-pipeline.md).

## 1. 무엇을 하나

팀장이 `release/<키>-vX.Y.Z` 브랜치를 master에 머지하면 잡 `release-windows` 가:

1. **버전 확정** — 머지 커밋 메시지의 `release/…-vX.Y.Z`. 없으면 `hotfix/` 머지로 보고 마지막 태그의 PATCH+1. 그것도 없으면 빌드 전에 실패(master에 릴리스·핫픽스 아닌 것이 들어온 것)
2. **Windows 빌드** — GameCI `windows-mono` 이미지에서 기존 `Metagame.EditorTools.SpaceBuild.BuildWindows`를 `-executeMethod`로. 출력은 `$CI_PROJECT_DIR/release/NotThisPlanet` (METAGAME/ 밖)
3. **zip** — `metagame-vX.Y.Z-YYYYMMDD-HHMM.zip`(-269 이름 규칙 + 버전). `*_DoNotShip`·`*_BackUpThisFolder…` 제외
4. **패키지 레지스트리 업로드**(generic, 잡 토큰) — Release 첨부 상한(100MB)을 피한다
5. **GitLab Release** — 태그 `vX.Y.Z`는 `ref`로 GitLab이 만든다. 설명 = 이전 태그 이후 develop으로 머지된 MR 목록(`git log --merges`, MR 번호·이슈 키 링크). 에셋 링크 = 패키지 파일
6. **MM 🟢 카드**(팀장 멘션 · 내려받기 링크) — 실패면 어느 단계에서 왜 인지 🔴(Infra)
7. (변수 있으면) **Jira 릴리스 웹훅**(-327) — 버전·이슈 키 목록을 Jira 자동화에

같은 버전으로 다시 돌려도 안전하다 — 패키지 파일은 덮어쓰고 Release는 갱신하며 링크는 없을 때만 더한다.

## 2. 왜 이렇게

| 결정 | 이유 |
| --- | --- |
| 잡 하나에서 빌드·업로드·발행 | zip이 100MB 를 넘어 잡 아티팩트로 다음 잡에 넘길 수 없다(아티팩트 상한도 인스턴스 기본 100MB). 한 잡 안에서 끝낸다 |
| 패키지 레지스트리(generic) | 첨부 상한은 인스턴스 관리자 것이라 못 늘린다. generic 패키지는 기본 5GB. Release 에셋 링크가 그 파일을 가리키니 받는 사람은 링크 하나 |
| 잡 토큰(`CI_JOB_TOKEN`) | 패키지 업로드·Release 생성(ref로 태그 생성 포함)은 잡 토큰으로 된다. 비밀 변수를 새로 만들지 않는다 |
| 변경 목록은 `git log`로 | GitLab 머지 커밋이 제목에 브랜치, 본문에 `See merge request !NNN`을 남긴다. API 읽기 없이 MR 번호·브랜치·이슈 키가 다 나온다. `GIT_DEPTH: 0` 이라 역사가 다 있다 |
| 버전은 브랜치 이름에서 | `mr-lint`가 MR 시점에 형식을 검사하니 master에서 실패할 일이 없다. 사람이 태그를 달 필요도 없다 |
| 빌드 전에 버전 확정 | 버전을 못 정하는 상황에서 수 분짜리 빌드를 돌릴 이유가 없다 |
| 빌드 실패도 알린다 | 팀장은 머지 버튼을 누르고 자리를 뜬다. 🔴 가 안 오면 성공한 줄 안다 |
| `resource_group: unity` | 컴파일 검사와 Unity 두 개가 겹치면 4 vCPU를 나눠 써 둘 다 느려진다 |

## 3. 실패하면 어디를 보나

| 증상 | 볼 것 |
| --- | --- |
| 🔴 "Unity 빌드 (exit N)" | 잡 아티팩트 `unity-build.log`의 `error CS`·`[NotThisPlanet] 빌드` 줄. 컴파일이면 develop이 깨진 것(unity-compile이 놓친 경우), 셰이더·씬이면 `SpaceBuild.cs`의 검증 |
| 🔴 "패키지 업로드 (HTTP 413)" | 인스턴스의 generic 패키지 상한. `Settings → Packages` 또는 관리자 |
| 🔴 "Release … (HTTP 403)" | 잡 토큰 권한 — 태그 보호 규칙이 생겼거나 프로젝트 설정 변경 |
| 버전 단계에서 실패 | master에 릴리스 브랜치·hotfix가 아닌 것이 머지됐다. 릴리스 브랜치로 다시 |

## 4. 실측 — 첫 릴리스 뒤에 채운다

| 항목 | 값 |
| --- | --- |
| 이미지 pull(최초) | |
| 빌드 시간(콜드/웜) | |
| zip 크기 | |
| 업로드 시간 · 상한에 걸렸는가 | |
| Release 생성 · 태그 | |
| MM 카드 · Jira 웹훅 | |
