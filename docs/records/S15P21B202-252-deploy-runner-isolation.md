# [Infra] 배포 스풀을 공용 러너에서 떼어낸다 (S15P21B202-252)

| 항목 | 내용 |
| --- | --- |
| 담당 | 금준영 |
| 기간 | 2026-09-04 |
| Jira | [S15P21B202-252](https://ssafy.atlassian.net/browse/S15P21B202-252) |
| 관련 Epic | [Infra] 개발 환경 (S15P21B202-9) |
| 관련 | [-139 CD 전략](S15P21B202-139-cd-strategy.md) §6 · [-284 dev·prod 두 환경](S15P21B202-284-two-env-on-one-ec2.md) |

## 무엇이 뚫려 있었나

> **2026-09-07 후속(`-324`)** — 이 작업은 "누가 스풀에 쓸 수 있나" 를 배포 러너로 좁혔다. 그런데 **스풀에 쓴 것이 실행되는** 경로가 하나 더 남아 있었다: `install.sh` 가 `deploy.sh` 를 스풀 안에 설치하고 systemd 가 그 경로를 실행했다. 배포 러너에서 도는 잡이 그 스크립트를 덮어쓸 수 있었다는 뜻이다(러너가 Protected 라 develop·master 파이프라인만 들어오므로 신뢰 경계 안이었지만, 마운트로 지키려던 원칙이 실제로는 성립하지 않았다). 스크립트를 `/opt/metagame-deploy/`(root 소유)로 옮겨 닫았다 — **rw 마운트 안에는 데이터만.**

세 가지가 겹쳐서 **MR 을 열 수 있는 사람이면 누구나 리뷰 없이 배포를 실행시킬 수 있었다.**

1. **배포 방아쇠가 파일 하나다.** `-139` 에서 도커 소켓·SSH 키를 잡에 주지 않으려고 스풀 방식을 골랐다. 그래서 호스트에 `PathExists=/home/ubuntu/metagame/deploy/RELEASE` → `deploy.sh` 가 걸려 있다
2. **그 디렉터리가 모든 잡에 쓰기로 마운트돼 있었다.** `config.toml` 의 `volumes` 는 러너의 속성이라 잡이 거부할 수 없다
3. **MR 파이프라인은 소스 브랜치의 `.gitlab-ci.yml` 을 쓴다**

자기 브랜치에 `cp …jar /spool/ && touch /spool/RELEASE` 두 줄을 넣고 MR 을 열면 끝이었다. `develop`·`master` 를 보호 브랜치로 잠근 것이 이 경로로 우회됐다.

**`rules:` 나 `when: manual` 로는 못 막는다.** 그 조건은 우리가 쓴 `deploy-backend` 잡에만 걸린다. 브랜치 작성자가 쓴 다른 잡은 그런 조건을 안 붙이면 그만이다. 브랜치에서 바꿀 수 없는 것은 **러너가 여는 마운트**와 **러너의 Protected 설정**뿐이라, 러너를 갈라야 한다.

## MR 파이프라인은 보호 ref 에서 돌지 않는다 (실측)

이 격리가 성립하는 근거다. `대상 브랜치`와 **파이프라인이 도는 ref** 는 다르다.

| MR | 대상 브랜치 | 파이프라인 ref |
| --- | --- | --- |
| `!212` | `develop` | `refs/merge-requests/212/head` |
| `!210` (스택) | `S15P21B202-281-…` | `refs/merge-requests/210/head` |

대상이 develop 이든 다른 이슈 브랜치든 ref 모양이 같다. **스택 도입과는 무관하다.** 그리고 `refs/merge-requests/…` 는 브랜치가 아니므로 보호 브랜치 설정에 걸릴 수가 없다 — Protected 러너는 MR 잡을 구조적으로 집어 갈 수 없다.

프로젝트에 `merge_pipelines_enabled` 가 꺼져 있어(병합 결과 파이프라인 미사용) 예외 경로도 없다.

## 한 것

**배포 전용 러너 등록** (2026-09-04, 같은 EC2 안 두 번째 러너 항목)

| | 공용 러너 | 배포 러너 |
| --- | --- | --- |
| 이름 | `METAGAME-ip-172-26-13-34` | `METAGAME-deploy-ip-172-26-13-34` |
| GitLab id | 2040 | **2068** |
| 태그 | `linux` | `deploy` |
| volumes | `/cache`, claude 바이너리·설정, **`/spool`**, Unity 라이선스 | **`/cache`, `/spool` 뿐** |

배포 러너에는 **Claude 자격증명도 Unity 라이선스도 넣지 않았다.** 배포 잡은 쓰지 않는다.

`.gitlab-ci.yml` 에서 `deploy-backend` 만 `tags: [deploy]` 로 바꾼다. 나머지 잡은 `default.tags: [linux]` 라 그대로 공용 러너에 남는다.

## 등록에서 막힌 것 — `glrt-` 토큰은 설정 플래그를 받지 않는다

`--tag-list`·`--access-level` 을 붙여 등록하면 **무시되는 게 아니라 FATAL 로 죽는다.**

```
FATAL: Runner configuration other than name and executor configuration is reserved
(specifically --locked, --access-level, --run-untagged, --maximum-timeout, --paused,
 --tag-list, and --maintenance-note) and cannot be specified when registering with a
runner authentication token.
```

gitlab-runner 19.3.1 실측. 태그·Protected·Run untagged 는 **팀장이 러너를 만들 때 UI 에서** 정하고, 등록 명령은 실행기 설정과 이름만 받는다. 러너를 새로 만들 때마다 걸릴 함정이라 적어 둔다.

## 아직 확인 못 한 것

**러너의 `Protected` 와 태그를 Developer 권한으로 볼 수 없다** — 러너 조회 API 가 403 이다. 팀장에게 러너 편집 화면에서 확인받아야 한다.

- **태그가 `deploy` 가 아니면**: `deploy-backend` 가 잡힐 러너를 못 찾아 멈춘다. `when: manual` 이라 실행하려 할 때 드러나고, 배포가 안 될 뿐 다른 피해는 없다
- **Protected 가 꺼져 있으면**: 격리가 성립하지 않는다. 다만 모든 잡이 `tags: [linux]` 라서 `deploy` 태그만 가진 러너로는 흘러가지 않는다. 그래도 **비상 절차로 `default.tags` 를 지우는 순간**(§4 ③) 전부 무태그가 되어 위험해진다 — 그래서 그 절차를 이 MR 에서 함께 고쳤다

**실측 검증**(스풀 마운트 제거 후): 임시 MR 잡에서 `/spool` 접근을 시도해 실패하는 것을 확인한다. 제거 전에는 공용 러너에도 스풀이 있어 결과가 구분되지 않으므로 순서상 마지막이다.

## 순서 (바꾸면 배포가 멈춘다)

1. **배포 러너 등록** ✅ 2026-09-04
2. **`deploy-backend` 를 `tags: [deploy]` 로** ← 이 MR
3. 공용 러너 `config.toml` 에서 `/spool` 제거 → `systemctl restart gitlab-runner`
4. 임시 잡으로 MR 이 스풀에 못 닿는 것을 실측

2번이 머지되기 전에 3번을 하면 배포 잡이 잡힐 러너가 없다.

## 되돌리기

```
gitlab-runner unregister --name "METAGAME-deploy-ip-172-26-13-34"
# 또는 /etc/gitlab-runner/config.toml.bak-before-deploy-runner 복원
```

## 남은 것

- `/home/ubuntu/.claude:/claude-config:rw` 가 **모든 MR 잡에 읽기·쓰기로** 열려 있다. `ai-review` 가 러너 호스트의 Claude 자격증명을 쓰기 위한 마운트인데, 그 결과 아무 MR 잡이나 그 자격증명을 읽을 수 있다. `:ro` 로 낮추고 `ai-review` 가 여전히 도는지 실측할 값이 있다 — 별 이슈
- 저장소의 `scripts/runner/setup-deploy-runner.sh` 는 `--executor shell` 로 등록하는 **옛 설계**다. 지금 설계(스풀+systemd)와 맞지 않으므로 EC2 의 v2 내용으로 교체해야 한다 — 별 이슈
