# 06. 오류 가능성과 기존 기능 영향 검토

기준: 2026-09-28, `bc55ddf2`, `S15P21B202-753-infra`. 이번 변경은 `exec/` 문서·설정 예시·백업 도구 추가와 CI의 AI 리뷰 경로 보완입니다. 게임 코드·씬·패키지·기존 배포 설정은 수정하지 않았습니다. 아래 문제는 기존 소스/설정에 대한 검토이며 이번 문서 추가로 발생한 회귀와 구분합니다.

## 우선 확인할 항목

### R1. 최신 DB 덤프와 복원 검증 자료 없음

- **중요도 높음 / 확인됨(저장소 범위)**: 추적 소스에 운영 SQL 덤프가 없습니다.
- 영향: 새 서버에서 실행되더라도 기존 사용자·랭킹 데이터 인수를 완료할 수 없습니다.
- 대응: [04 DB 인계](04-database.md)로 최신 덤프·해시·메타데이터를 확보하고 별도 DB에서 복원 검증합니다. 엔티티로 만든 빈 스키마를 운영 덤프라고 제출하지 않습니다.

### R2. `.env`에만 추가한 일부 설정은 컨테이너에 전달되지 않음

- **중요도 높음 / 확인됨(설정)**: `backend/src/main/resources/application.yml:41,67,82,84` 등은 `RELAY_ALWAYS`, `RUNLOG_KEEP_DAYS`, `CLIENT_MIN_VERSION`, `CLIENT_DOWNLOAD_URL`을 읽지만 `scripts/deploy/server/compose.prod.yml`/`compose.dev.yml`의 environment에는 이 키가 없습니다.
- 영향: 릴레이 강제/최소 버전/다운로드 안내를 `.env`에 설정해도 기본값으로 실행될 수 있습니다. 새 서버에서 AP 격리 환경의 입장 실패, 의도한 구버전 제한 미적용 가능성이 있습니다.
- 대응: [보완 compose](config/compose.handover.yml)를 서버 `docker-compose.override.yml`로 복사하여 병합합니다. 기본 compose는 수정하지 않았습니다. 개발 최소 버전은 빈 값으로 유지합니다.
- 검증: `docker compose config --quiet` 후 비밀이 아닌 대상 변수만 컨테이너에서 확인하고 실제 릴레이/버전 요청을 시험합니다. 전체 환경변수를 출력하면 비밀이 노출됩니다.

### R3. 최소 버전 게이트를 켜면 기존 배포 health 요청과 충돌

- **중요도 높음 / 조건부 경로 확인됨, 운영 미재현**: `scripts/deploy/host/deploy.sh:88-93`의 게스트 로그인 health는 `clientVersion`을 보내지 않습니다. `AuthController.java:37-41`은 게이트에서 거절되면 HTTP 426을 반환합니다. `ClientVersionGate.check`는 게이트 활성 상태에서 버전 없는 요청을 UNKNOWN으로 판정합니다.
- 영향: R2를 보완하고 `CLIENT_MIN_VERSION`을 설정하면 정상 앱도 health에서 200을 반환하지 않아 배포가 롤백/실패할 수 있습니다. 과거 JAR 복귀도 같은 정책을 적용받으면 회복하지 못할 수 있습니다.
- 대응: 별도 readiness endpoint 또는 허용 버전을 제공하는 health 계약을 먼저 구현·검증한 뒤 게이트를 켭니다. 이번 템플릿의 최소 버전 기본값은 비워 두었습니다.
- 검증: dev의 별도 검증 환경에서 게이트 꺼짐/켜짐 × 버전 없음/구버전/최신 버전 요청과 배포 성공·실패·롤백을 확인합니다.

### R4. nginx 사본만으로 AI 팩 다운로드 복원 불가

- **중요도 높음(AI 기능) / 확인됨(설정)**: `write_ai_pack_source.py`는 `/ai-packs/latest-companion-v4/pack-manifest.json`을 사용하지만 저장소 `scripts/deploy/server/nginx/metagame.conf`에는 `/ai-packs/` location이 없습니다. 일반 `/`는 백엔드로 전달합니다.
- 영향: 해당 사본만 설치한 새 서버에서는 AI 다운로드 파일이 제공되지 않습니다. 사람만 플레이와는 별도입니다. 현재 서버에 별도 설정이 존재하는지는 확인하지 않았습니다.
- 대응: 실제 운영 팩과 서버 설정을 함께 인계합니다. [location 예시](config/nginx-ai-packs.conf.example)를 새 서버 파일 경로와 맞추고 HTTPS manifest·ZIP 해시·설치 성공을 검증합니다.
- 추가: 저장소 `ai-pack-source.json`은 빈 값입니다. 수동 빌드에도 `write_ai_pack_source.py`가 필요합니다. 빌드 때 URL을 썼다는 사실만으로 원격 파일 접근이 검증되지는 않습니다.

### R5. 새 서버에서 인증서 생성 전 nginx 사본 활성화 시 실패 가능

- **중요도 중간 / 조건부 확인됨**: `scripts/deploy/server/nginx/metagame.conf:204-207`은 기존 도메인의 인증서/SSL 옵션 파일을 참조합니다.
- 영향: 파일 없는 신규 서버에서 nginx 검사/기동이 실패할 수 있습니다.
- 대응: [01](01-build-deploy.md)의 HTTP 준비 → 인증서 발급 → 최종 TLS 설정 적용 순서를 사용합니다. 기존 서버 인증서 존재 여부와 신규 도메인 경로를 구분합니다.

### R6. DB 자동 변경과 JAR 롤백의 범위가 다름

- **중요도 높음(스키마 변경 시) / 확인됨(설정), 손실 재현 아님**: `application.yml:12`의 `ddl-auto: update`와 `deploy.sh`의 JAR 백업/복원 경로.
- 영향: 백엔드 롤백만으로 스키마·데이터를 과거 상태로 복원할 수 없습니다. DB 암호 문제를 볼륨 삭제로 해결하면 기존 데이터가 사라집니다.
- 대응: 변경 전 DB 덤프와 복원 검증, 버전 간 스키마 호환 확인. 장기적으로 명시적 마이그레이션을 별도 작업으로 관리합니다.

### R7. 빌드 필수 씬 누락 시 종료 코드만으로 성공 판단 위험

- **중요도 중간 / 확인됨(코드)**: `SpaceBuild.cs:68-75,150`은 필수 씬이 없으면 오류를 기록하고 빌드 본문에서 반환하며 이 경로에는 명시적 비정상 종료가 없습니다.
- 영향: 파일 누락 상태에서 명령 종료 코드만 검사하는 외부 수동 빌드 절차가 빌드 누락을 놓칠 수 있습니다. 현재 설정의 9개 씬이 빠져 있다는 뜻은 아닙니다.
- 대응: 로그의 성공 결과와 exe/Data 산출물 확인을 매뉴얼에 포함했습니다. 자동화 코드를 개선한다면 누락 상황에서 명시적으로 실패하도록 별도 검증합니다.

### R8. LFS 체크아웃 경고

- **중요도 낮음 / 확인됨**: 워크트리 생성 시 Mirror/Edgegap 공급사 폰트 3개가 LFS 포인터여야 하나 일반 파일이라는 경고를 냈습니다.
- 위치: `METAGAME/Assets/_Project/ThirdParty/Mirror/Hosting/Edgegap/Editor/Fonts/Src/Spartan/Spartan-Regular.ttf`, `Spartan-SemiBold.ttf`, `Src/UbuntuMono-R.ttf`.
- 영향: 새 체크아웃에서 불필요한 수정 표시·LFS 정합성 경고가 생길 수 있습니다. 이번 문서 변경과 무관한 기준 상태입니다.
- 대응: 공급사 에셋과 `.gitattributes` 일치를 별도 작업으로 확인합니다. 이 작업에서는 파일을 다시 저장하거나 LFS 이력을 바꾸지 않았습니다.

## 실행·정적 검증 결과

최초 로컬 검사에서 `exec/`는 아직 Git에 추적되지 않아 `git ls-files`를 사용하는 경로 검사의 대상에 포함되지 않았습니다. 커밋 후 CI에서 `test_ai_review_paths.py`가 `exec/`의 누락을 검출했습니다. `.gitlab-ci.yml`의 `ai-review`와 `ai-review-gate` 양쪽에 `exec/**/*`를 추가하고, 추적 파일이 포함된 상태로 아래 검사를 다시 실행했습니다. 실행 도구가 포함되어 있으므로 문서 면제 목록에는 넣지 않았습니다.

| 검사 | 결과 | 범위/제한 |
| --- | --- | --- |
| CI 오프라인 전체 테스트 | **88 통과 / 0 실패** | Git Bash 경로를 하위 프로세스 PATH에 명시한 최종 실행. [로그](validation/ci-tests.log) |
| 최초 셸 검사 실패 | 환경 설정 후 해소 | 최초 82 통과/6 실패. WSL/Bash `E_ACCESSDENIED` 때문에 실패한 6개도 최종 실행에서 모두 통과 |
| Python 기본 인코딩 실행 | 최초 cp949 출력 오류 | `PYTHONUTF8=1`로 다시 실행하여 위 결과 확보 |
| 백엔드 `test bootJar` | **차단** | Gradle 다운로드 `Permission denied: getsockopt`; 컴파일/테스트 도달 전. [로그](validation/backend-build.log) |
| Docker/MySQL 실기 | **미실시** | Docker 엔진 named pipe 없음. 실제 compose 기동·덤프·복원 미검증 |
| 운영·개발 compose + 보완 파일 | **양쪽 config 검사 통과** | `config --quiet` 종료 0. Docker 사용자 설정 접근 경고는 있었으며 엔진 기동 없이 구문·병합만 확인 |
| Unity 게임 빌드/테스트 | **미실시** | 설치된 6000.5.4f1 존재 확인, 화면 조작·프로젝트 실행 없음 |
| 런처 publish/자체검사 | **미실시** | .NET SDK 10.0.400 존재 확인. 기존 런처 도구의 Python 검사는 위 CI에 포함 |
| 문서 링크·씬 경로·환경변수 대응 | [정적 검증 기록](validation/static-checks.txt) | 운영 서버 상태를 증명하지 않음 |
| 신규 백업 도구 | Bash 문법 및 모의 덤프 성공/실패 검사 통과 | [검사 기록](validation/backup-script-check.txt). 압축·해시 및 실패 시 잔여 파일 없음 확인. 실DB는 미검증 |
| 실제 2인 게임·AI·웹 | **미실시** | [시연 점검표](05-demo.md)에 남김 |

이 PC의 Java는 21.0.12이며 프로젝트 toolchain 요구는 17입니다. Gradle 다운로드 제한을 해소한 후 JDK 17 확보도 확인해야 합니다. 저장소의 Java 설정 자체가 이번 실행에서 컴파일 검증된 것은 아닙니다.

## 기존 기능 영향과 인수 순서

`exec/`의 파일을 추가하는 것만으로 게임·런처·서버 실행 흐름이 바뀌지 않습니다. 실제 적용 영향은 별도로 판단해야 합니다.

1. DB 덤프와 비밀·서버 상태를 인계받고 별도 환경에 복원합니다(R1/R6).
2. 기본 웹·백엔드·WSS·릴레이를 2인으로 검증합니다(R2/R5).
3. AI 선택 기능의 배포 파일·주소·로컬 실행을 검증합니다(R4).
4. 최소 버전 게이트와 배포 health의 계약을 맞춘 후 정책을 적용합니다(R3).
5. [05](05-demo.md)의 기존 기능 회귀 검증을 완료합니다.

운영/개발 DB·포트·로그가 분리되어 있고, Unity 패키지 잠금 파일, H2 백엔드 테스트, CI 오프라인 테스트, 런처 해시 검사 경로가 존재합니다. 다만 이번 확인 범위로 **발생 가능한 오류가 전혀 없다**고 결론 내릴 수는 없습니다.
