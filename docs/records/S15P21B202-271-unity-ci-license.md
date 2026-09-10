# Unity CI 라이선스 — 오프라인 차단을 우회하지 않고 통과한 경로 (S15P21B202-271)

[문서 안내](../README.md) → [작업 기록](README.md) → 서버·배포

Unity 컴파일 검사를 CI에서 실행하기 위해 라이선스를 구성한 기록입니다. 실패한 활성화 경로와 채택한 방식, 러너 마운트·캐시·첫 실행 결과를 설명합니다.

본문의 “현재”, 검증 수치, 미완료 항목은 **기록 당시 기준**입니다. 후속 변경은 관련 이슈와 [주제별 색인](README.md)을 함께 확인합니다.

작성 2026-09-02 · [Infra] 금준영

이 문서는 **왜 이 방법인가**를 남긴다. 시도했다 막힌 길과 그 근거를 함께 적는 이유는, 나중에
누가 "그냥 `.alf` 올리면 되지 않나" 로 되돌아오는 것을 막기 위해서다.

## 문제

이 프로젝트에는 게임 C# 컴파일 검사가 없었다. CI 전체 이력 **잡 2,583건 중 `unity-compile` 0건**
(2026-08-27 ~ 09-02). 잡 정의는 있었지만 `$UNITY_RUNNER == "true"` 조건이라 한 번도 생성되지
않았고, `tags: [unity-win]` + PowerShell 이라 EC2 도커 러너에서 돌 수도 없었다.

그 공백에서 사고가 났다. 09-02 13:33:31 `!189` 머지(`59afb2d`)가 넣은 `SoundRuntime.cs`가
저장소에 없는 static event 12개와 `PlayerRig.HandReleaseReason`을 참조해 `Metagame.Gameplay`
어셈블리가 깨졌다. 그것을 참조하는 테스트 어셈블리까지 함께 컴파일 불가가 되어 **develop이
2시간 37분** 컴파일되지 않았고, 그 사이 develop 머지는 0건이었다. `!189`의 파이프라인은
`mr-lint`·`mr-title`·`mr-overlap`·`repo-hygiene`·`stack-order`·`ai-review`가 **전부 초록**이었다.

작성자 체크리스트는 증거가 못 된다 — `mr_lint.py`가 미체크를 하드 실패로 막아 **체크는 초록불의
필수 조건**이고, develop을 깨뜨린 `!189`도 그 박스가 체크돼 있었다.

<a id="막힌-길"></a>

## 검토했으나 사용할 수 없었던 방식

| 시도 | 결과 | 근거 |
| --- | --- | --- |
| 오프라인(수동) 활성화 `.alf` → `.ulf` | **불가** | Unity가 Personal 시트의 수동 활성화를 지원하지 않는다. 공식 문서가 2026-09-01 갱신됐다(support.unity.com 4401914348436). 실제로 업로드하면 "Offline activation is available only for Enterprise and Industry seats" 로 거부된다 |
| 커맨드라인 `-serial` | **불가** | Personal은 시리얼이 없다. 공식 매뉴얼이 "For Unity Personal, the Unity Hub is the only method for activating" 이라고 적는다 |
| 자체 호스팅 Licensing Server | **불가** | Enterprise 구독 전용 |
| Unity Build Server | **불가** | Software Additional Terms가 Personal 티어와 함께 쓰는 것을 명문으로 금지한다 |
| 브라우저 자동화로 활성화 페이지 조작 | **하지 않는다** | 시트 자격 판정 우회에 해당할 소지가 있고, 서버가 Personal을 거부하므로 기술적으로도 무효다. 비밀번호·TOTP 시드까지 CI에 쌓인다 |
| 개발자 PC 러너에서 로컬 라이선스 사용 | **버렸다** | 목표와 어긋난다. 러너를 EC2로 옮긴 이유가 "PC가 꺼지면 팀 전체가 멈춘다" 였다(08-28 1시간, 08-31 파이프라인 26건 전멸). 게다가 PC의 에디터는 6000.0.81f1 로 프로젝트 버전(6000.5.4f1)과 다르다 — 버전이 다르면 batchmode가 프로젝트를 자동으로 업그레이드한다 |

<a id="통한-길"></a>

## 채택한 라이선스 활성화 방식

GameCI 이미지(`unityci/editor:ubuntu-6000.5.4f1-base-3.2.2`)에 **Unity 자체 라이선스 클라이언트
1.18.1** 이 들어 있고, 그 `--help`에 이렇게 적혀 있다.

```
--activate-ulf   Acquire Unity ULF license file.
                 Add --serial parameter to activate a PRO license
--serial         The serial number to activate a PRO ULF license file
```

즉 `--serial`을 **주지 않으면** Personal 경로다. EC2 컨테이너에서 `--username`·`--password`로
실행했더니 성공했다.

```
Successfully activated ULF license for user <email>    (종료코드 0)
Product Name: Unity Personal · License Type: ULF
com.unity.editor.headless             ← CI 에 필요한 권한
com.unity.editor.watermarks.disable
Expiration Date: 2026-10-09
```

그리고 **나온 `.ulf`(2,507B)만 있으면 자격증명 없이 재사용된다.** 그래서 CI에 비밀번호를 두지 않는다.

### 머신 바인딩이 이미지에 고정돼 있다

`.ulf`의 바인딩 값 `576562626572264761624c65526f7578`을 디코드하면 **`Webber&GabLeRoux`**
(GameCI 관리자 이름)다. 컨테이너 hostname·MAC은 매 실행 무작위인데 MachineID는
`D7nTUnjNAmtsUMcnoyrqkgIbYdM=`로 동일했다.

- 좋은 점: **같은 GameCI 이미지를 쓰는 어느 기계에서든 통한다.** 서버를 옮기거나 새로 만들어도
  재활성화가 필요 없다
- 나쁜 점: 유출되면 그 이미지를 가진 누구나 이 라이선스를 쓸 수 있다

처음에는 "EC2 호스트에서 파생된 값이라 안정적" 이라고 잘못 읽었다. 실제로는 이미지가 고정한 값이다.

### 전용 Unity 계정을 쓴다

Unity 약관은 **시트당 동시 1 인스턴스**다. 개인 계정을 쓰면 개발자가 에디터를 열어 둔 시간에
CI 잡이 돌아 위반이 된다. 그래서 이 프로젝트 CI 외에 아무 용도 없는 Unity ID를 새로 만들었다.

- **2FA 를 걸지 않는다** — 걸면 커맨드라인 인증이 막힌다
- **구글 등 소셜 로그인으로 만들면 안 된다** — 자체 비밀번호가 없어
  `401 Invalid Credential (143.002)`로 실패한다. 실제로 그렇게 한 번 막혔고, `id.unity.com`에서
  비밀번호를 설정해 해결했다
- 그 계정에 에셋 스토어 구매·조직 소속 등 다른 자산을 붙이지 않는다. 그것이 이 방식의 위험 관리다

## 라이선스를 잡에 전달하는 방법 — CI 변수가 아니라 마운트

EC2 러너 `config.toml`의 `volumes`에 읽기 전용으로 넣는다.

```
/home/ubuntu/unity-license/Unity_lic.ulf:/root/.local/share/unity3d/Unity/Unity_lic.ulf:ro
```

CI 변수(`UNITY_LICENSE`)를 쓰지 않는 이유:

- **MR은 보호 브랜치가 아니라 `Protected` 변수를 읽지 못한다.** MR 마다 검사하는 것이 목적이므로
  `Protected`를 켤 수 없고, 끄면 값이 잡 환경변수로 들어가 로그로 새어나갈 길이 생긴다
- 마운트는 환경변수가 아니고 GitLab UI 에도 값이 없다. Claude 자격증명을 이미 같은 방식으로 넣는다
- **노출 자체는 같다** — 어느 쪽이든 MR 잡이 파일을 읽을 수 있다. `-252`(배포 스풀 격리)와 같은
  종류의 문제이고, 전용 계정으로 감당한다. `-252`의 러너 분리가 끝나면 같은 격리 대상에 넣는다

되돌리기: EC2의 `mount-unity-license.py --remove` · 백업 `config.toml.bak-before-unity`.
활성화 반납: `Unity.Licensing.Client --return-ulf`.

## 잡 설계 — 이미지를 뜯어보고 맞췄다

- `unity-editor` 래퍼가 `xvfb-run -ae /dev/stdout … -batchmode`를 이미 붙인다. 그래서 `-batchmode`를
  또 주지 않고, **`-nographics`도 주지 않는다** — xvfb가 있어 URP 관련 임포트가 정상적으로 돈다
- 이미지 `ENTRYPOINT`가 비어 있어(`Cmd=/bin/bash`) GitLab CI와 그대로 맞는다. `entrypoint: [""]`가 필요 없다
- 이미지 태그는 **프로젝트 버전과 같아야 한다** — 다르면 batchmode가 프로젝트를 자동으로 업그레이드한다
- 라이선스가 마운트되지 않았으면 무엇을 어디에 넣어야 하는지 적고 즉시 실패한다
- 만료일을 매 실행 로그에 찍는다. **2026-10-09 전에 `--update-ulf` 갱신이 필요하다**
- 컴파일 오류는 로그 수천 줄 대신 `error CS` 줄만 중복 제거해 40건까지 보여 준다

## 실행 시간 — 임포트를 남기는 것이 전부였다

| 회차 | 슬롯 | 소요 | 상태 |
| --- | --- | --- | --- |
| 1 (콜드) | concurrent-1 | **8분 16초** | 임포트·컴파일 7분 30초 + 테스트 35.6초 |
| 2 | concurrent-0 | 7분 58초 | 다른 슬롯이라 또 콜드 |
| 3 | concurrent-1 | 7분 49초 | `Library`가 지워져 또 콜드 |
| 4 (웜) | concurrent-0 | **70초** | 임포트 64초 + 테스트 20초 |

`Library`는 **2.2GB** 다. 두 가지를 고치고서야 웜이 됐다.

1. **`GIT_CLEAN_FLAGS: -ffdx -e METAGAME/Library`를 전역으로 올렸다.** 도커 실행기의 빌드
   디렉터리는 그 슬롯의 **모든 잡이 공유**한다. `unity-compile` 에만 예외를 주면, 다음에 그 슬롯에서
   도는 `mr-lint`가 예외 없는 `git clean -ffdx`로 `Library`를 지운다. 실측으로 슬롯 2개 중
   1개만 남아 있었다
2. **`resource_group: unity`** — EC2는 vCPU 4 · RAM 15GiB 이고 러너 `concurrent = 2` 인데 Unity
   임포트는 CPU·IO를 다 쓴다. 두 개가 겹치면 서로 느려지고 가벼운 검사들이 굶는다

GitLab `cache:`(paths: `METAGAME/Library`)를 쓰지 않은 이유: 2.2GB 를 매 잡 tar/untar 하는 비용이
임포트 절감분을 깎는다. 러너가 1대뿐이라 빌드 디렉터리에 남기는 것이 더 싸다. 슬롯이 비워지면
다시 8분이 드는 것은 감수한다.

## 첫 실행이 알려 준 것

**컴파일은 통과했다** — `error CS` 0건. 즉 이 검사를 붙인 시점의 develop은 컴파일된다.

**EditMode 647개 중 630 통과 · 17 실패.** 네 번 모두 같은 결과다.

| 건수 | 테스트 클래스 | 오늘 커밋 |
| --- | --- | --- |
| 7 | `ClampPullTests` | 없음 (09-01 이 마지막) |
| 3 | `TractorBeamControllerTests` | 1회 |
| 2 | `DownedRescueTests` | 없음 |
| 1 | `LowGravityFieldTests` | 2회 |
| 1 | `MeteorTests` | 2회 |
| 1 | `PartClampTests` | 없음 |
| 1 | `PartialLiftTests` | 1회 |
| 1 | `TransportAltitudeTests` | 1회 |

`!165`(09-01)에 "develop에 이미 실패하는 EditMode 테스트 13개" 기록이 있고 지금 17개다. 오늘
건드리지 않은 파일이 10건을 갖고 있어 그쪽은 기존 부채가 확실하다. 나머지 7건은 오늘 바뀐 파일에
있어 증가분이 거기 있을 가능성이 높다 — 다만 before/after를 직접 재지는 않았으므로 단정하지 않는다.

실패 메시지는 두 종류가 섞여 있다.

- **기획과 구현 불일치**: `Pull_TakesThePlannedTime` — "2명은 기획 §12.2 의 약 4.5초여야 한다.
  Expected: 4.5 +/- 0.1 But was: 1.12"
- **전제 미성립**: `MovingAwayFromTheClamp_PullsIt` — "잡고 있어야 한다. Expected: True But was: False".
  본체 검증에 도달하지 못한 형태로, 테스트가 구현보다 앞서 작성됐거나 씬 전제가 바뀐 경우일 수 있다

그래서 이 17개를 **버그로 단정하지 않는다.** 담당자가 볼 목록으로 넘기고, 정리되기 전까지
`allow_failure: true`를 유지한다. 지금 차단으로 바꾸면 모든 MR이 실패 표시이 된다.

## 남은 일

- 실패 17개 분류·정리 → 그 뒤 `allow_failure: false`로 전환
- **2026-10-09 만료 전 `--update-ulf` 갱신**
- `-252` 러너 격리가 끝나면 `.ulf` 마운트도 배포 전용 러너 쪽으로 옮겨 MR 잡에서 떼어낸다
- `.gitlab/merge_request_templates/Default.md`와 `scripts/ci/ai_review.py`가 리뷰어·AI 에게
  "컴파일은 기계가 봤다" 고 말하는 부분 정리 — 이제 **Unity C# 도** 실제로 보므로 문장을 사실에
  맞게 고치면 된다(백엔드는 `build-backend`가 본다)
