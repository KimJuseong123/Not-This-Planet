# CI 배포·Unity 판정·AI 인증 점검의 결함 분석 (S15P21B202-309)

[문서 안내](../README.md) → [작업 기록](README.md) → 협업·자동화

배포 잡 취소, Unity 검사 판정, AI 인증 점검에서 발견한 CI 결함을 조사했습니다. 실제 MR 상태와 다른 신호가 나온 원인 및 수정·후속 작업을 기록합니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

2026-09-06 · Infra(금준영)

우리가 올린 MR 세 건(`-301`·`-303`·`-304`)이 머지된 뒤 남은 MR 들을 보니 경고·실패가 붙어
있었다. 하나하나 따져 보니 **세 건 모두 CI 설정의 결함**으로 확인했다.
그중 배포 잡 취소 문제로 릴레이 변경의 배포가 이틀 지연되었다.

---

## 1. 배포 잡이 사라졌다 — 릴레이가 이틀 묻혔다

### 무슨 일이 있었나

| 시각(KST) | 일 |
| --- | --- |
| 19:43 | `-303`(컨테이너에서 릴레이 제어 API에 닿게 함) 머지 → develop 파이프라인 **#180211** 생성. `build-backend` + `deploy-backend`(수동) 포함 |
| 19:44 | `-304`(토큰 갱신) 머지 → 파이프라인 **#180212** 생성 |
| 19:44 | GitLab이 **#180211 을 자동 취소** — `build-backend`(`created`)·`deploy-backend`(`created`) 둘 다 `canceled` |
| — | #180212 에는 **백엔드 잡이 아예 없다** — `-304`는 `backend/`를 건드리지 않아 `changes:`에 걸리지 않았다 |

결과: **릴레이 수정이 develop에 들어갔는데 누를 배포 버튼이 어디에도 없었다.** 백엔드
컨테이너는 `Up 2 days`로 그대로였고, 파이프라인 목록은 전부 초록이었다. 아무도 알아채지 못했다.

### 왜 `interruptible: false` 로 막히지 않았나

`deploy-backend` 에는 이미 `interruptible: false`가 있었다(`-141`). 그런데

> **아직 시작하지 않은 잡은 그 설정과 무관하게 취소된다.**

`build-backend`는 `default.interruptible: true`를 물려받고 `created` 상태였고,
`deploy-backend`는 `needs: [build-backend]` 라 역시 `created` 였다. 잡 단위 설정으로는
막을 수 없는 자리였다.

### 고친 것

```yaml
workflow:
  rules:
    - if: $CI_PIPELINE_SOURCE == "push" && $CI_COMMIT_BRANCH == "develop"
      auto_cancel:
        on_new_commit: none
```

develop 머지 파이프라인은 뒤따라오는 머지가 죽이지 못한다. **MR 파이프라인은 그대로 둔다** —
거기서는 새 push가 앞 것을 죽이는 것이 맞다.

그리고 `deploy-backend`의 `changes:`에 `.gitlab-ci.yml`을 더했다. 이유는 두 가지다.

1. `build-backend`는 이미 `["backend/**/*", ".gitlab-ci.yml"]` 이었다 — **두 잡의 조건이
   어긋나 있었고**, `deploy`는 `needs: [build-backend]` 이므로 "build만 생기는 조합" 이
   계속 만들어지고 있었다
2. 배포 정의가 바뀌었으면 그것을 실제로 배포해 볼 수 있어야 한다

수동 잡은 눌리기 전까지 러너를 쓰지 않으므로 **버튼이 더 자주 생기는 비용은 0** 이다.

### 안전망 — 없는 잡은 셀 수 없다

`mm_digest` 에는 이미 `pending`(눌리지 않은 배포 잡 수)이 있었다. 그런데 이번 경우 `pending`
은 **0** 이다. 잡이 만들어지지 않았으니 셀 것이 없다. 아침 카드는 아무 문제도 말하지 않았다.

그래서 잡 목록이 아니라 **커밋을 직접 비교**한다.

```python
def backend_drift(gl, live, ref="develop"):
    sha = ((live or {}).get("commit") or {}).get("id")
    if not sha:
        return None
    paths = gl.compare_paths(sha, ref)
    if paths is None:
        return None
    return [p for p in paths if p.startswith("backend/")]
```

`None`(확인 못 했다)과 `[]`(다른 것이 없다)를 반드시 구분한다. 섞으면 조용한 오탐·미탐이 된다.
카드에는 `pending == 0` 인데 `drift`가 있을 때만 뜬다 — 그것이 정확히 "잡이 사라졌다" 는 상태다.

> ⚠ **배포 대기 잡이 없는데 develop 의 backend 가 배포본과 다릅니다**(파일 2개)
> — 배포 잡이 만들어지지 않은 것입니다 → @rmawnsdud 확인

`deploy-backend`에 `environment: production`도 달았다. 배포 이력을 GitLab이 들고 있으면
"지금 서버에 뭐가 올라가 있나" 를 잡 목록을 훑지 않고 답할 수 있다.

---

## 2. `unity-compile` 이 통과인데 ⚠ 로 떴다 — `!228` `!229` `!230` 전부

`unity_report.py`는 처음부터 제대로 판정하고 있었다. MR 코멘트는 이렇게 적혀 있었다.

> ✅ **Unity 컴파일 통과 · 이 MR 이 만든 테스트 실패 없음**
> 이 MR 의 실패 25 · 그중 **새로 생긴 것 0** · `develop` 에도 있던 것 25

그런데 잡의 마지막 줄이

```yaml
- exit "$(cat "$CI_PROJECT_DIR/unity-rc.txt")"
```

였다. **Unity의 원시 종료 코드**다. Unity는 테스트가 하나라도 실패하면 2 로 끝나므로,
기준선에도 똑같이 있는 실패 25건만 가진 MR이 빨간 ⚠ 를 받았다.

**판정은 있었는데 종료 코드에 반영되지 않았다.** 그리고 `allow_failure: true` 였으니 신호가
반대 방향으로도 틀렸다 — **컴파일이 깨져도 머지됐다.** 09-02 에 develop이 2시간 37분 멈춘
그 경로가 그대로 열려 있었다.

### 고친 것 — 종료 코드가 판정을 담는다

| 코드 | 뜻 | 파이프라인 |
| --- | --- | --- |
| `0` | 통과 (기존 실패만 있음) | 초록 |
| `1` | **이 MR이 만든** 컴파일 오류 · 새 테스트 실패 | **막는다** |
| `2` | 기준선 없음 · 라이선스 만료 · 에디터 크래시 등 판단 불가 | ⚠ 경고 |

```yaml
allow_failure:
  exit_codes: [2]
```

판정 원칙은 하나다 — **이 MR이 만든 것만 막는다.**

```python
if errs:                       return 1   # 컴파일 오류: develop 이 컴파일되면 이 MR 이 깬 것
if base is None:               return 0 if unity_rc == 0 else 2
if failed_names(data) - base:  return 1   # 새 실패
if data["failed"] == 0 and unity_rc != 0:  return 2   # 실패 없는데 죽음 = 환경 문제
return 0
```

판단하지 못한 것을 회귀로 단정하면 아무 잘못 없는 사람의 머지가 막히고, 그러면 사람들이 이
신호를 우회하는 법을 배운다. `mr-lint`의 스택 경고가 실측 **26 대 0**으로 무시된 것이 그
경로였다.

판정은 `unity-verdict.txt`로 넘긴다. 보고 스크립트가 죽어 파일이 없으면 잡은 **2(경고)**로
끝난다 — Unity 원시 코드로 되돌아가면 기존 실패에 다시 걸린다.

> **`allow_failure: true` 를 "안정화 후 차단으로" 바꾸려고 기다릴 필요가 없었다.** 기준선
> 비교가 이미 새 실패와 기존 실패를 가르고 있었으므로, 25건이 남아 있어도 차단으로 갈 수 있다.

---

## 3. `ai-review` 토큰 구멍 — `-304` 가 절반만 고쳤다

`-304`의 타이머는 잘 돌고 있었다. journal 상 매 틱 `토큰 갱신 OK`. 그런데

| 시각(UTC) | 일 |
| --- | --- |
| 05:47 | 타이머 → `토큰 갱신 OK` |
| 08:34 | `!229`의 `ai-review` → **`category=authentication http=401`** |
| 08:48 | 타이머 → `토큰 갱신 OK` · `.credentials.json` mtime 갱신 |
| 이후 | `!230`의 `ai-review` 성공 |

### 원인 — EC2 에서 직접 재 봤다

만료까지 **2.8시간 남은** 상태에서 `claude -p PONG`을 돌렸다.

```
실행 전 mtime: 08:48:32
  claude 종료 0
실행 후 mtime: 08:48:32
```

**CLI는 만료된 뒤에만 토큰을 갈아 준다.** 그러므로 8시간 TTL을 3시간마다 두드려도 갱신은
"만료 후 첫 틱" 에만 일어나고, **타이머 주기가 그대로 최대 무응답 구간**이 된다.

### 고친 것

`OnUnitActiveSec` 3h → **20min**. 구멍이 최대 20분으로 줄어든다. 비용은 20분마다 짧은 호출
하나(실측 1.5~1.8초 CPU).

이 구멍을 **없앨** 수는 없다 — 갱신 시점을 CLI가 정하기 때문이다. 근본적으로는
`ANTHROPIC_API_KEY` 백엔드로 바꾸면 마운트·갱신·소유권 문제가 모두 사라진다(비용은 든다).
리프레시 토큰은 **2026-09-29**에 만료되고, 그때는 사람이 러너 호스트에서 재로그인해야 한다.

---

## 확인한 것 — 내 잘못이 아닌 것

`!214`(팀장 MR)의 `mr-lint`·`repo-hygiene` 실패는 **정당하다.**

- 작성자 체크리스트 5개 미완
- 씬·프리팹 3개(`Main.unity`, `PF_Stage1_WeldingPlasmaGun.prefab`, `SF_Stage1_PlasmaWeldingGun.prefab`)
  가 바뀌었는데 MR 본문은 "변경한 씬·프리팹: 없음"

검사가 제 일을 한 것이라 변경하지 않았다.

---

<a id="남은-것"></a>

## 남은 작업

- **`deploy-backend`를 눌러야 한다** — 이 MR이 머지되면 `.gitlab-ci.yml` 변경으로 배포 잡이
  생긴다. `develop`이 보호 브랜치라 Developer(나)는 403 이므로 **팀장이 눌러야 한다.**
  누른 뒤 백엔드 기동 로그에 `relay 제어 API 도달 OK`가 찍히는지 확인한다(`-303`)
- 릴레이를 실제로 지나는 플레이 검증은 **서로 다른 망의 두 사람**이 필요하다(`-299` §검증)
