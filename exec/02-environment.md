# 02. 환경변수·계정·설정 파일

## 설정 원본 목록

| 파일/위치 | 역할 |
| --- | --- |
| `backend/src/main/resources/application.yml` | 백엔드 프로퍼티와 기본값 |
| `backend/.env.example` → `backend/.env` | 로컬 실행용 예시와 실제 값 |
| `backend/docker-compose.yml` | 로컬 MySQL, 고정 개발 비밀번호 포함 |
| `scripts/deploy/server/compose.prod.yml`, `compose.dev.yml` | 서버 컨테이너와 환경변수 전달 |
| 서버 `~/metagame/.env`, `~/metagame-dev/.env` | 운영·개발 실제 비밀값; Git 미포함 |
| `exec/config/server.env.example` | 이번 인계용 비밀값 없는 템플릿 |
| `exec/config/compose.handover.yml` | 기존 compose에 빠진 선택 환경변수 전달 보완 |
| `METAGAME/Assets/_Project/Scripts/Runtime/Core/BackendEndpoints.cs` | 게임 백엔드 주소·실행 인자 |
| `METAGAME/Assets/StreamingAssets/ai-pack-source.json` | 게임에 포함될 AI 추가팩 주소 |
| `scripts/ci/write_ai_pack_source.py` | AI 배포 정보를 빌드 전 생성 |
| `launcher/Program.cs`, `DevMode.cs`, `SelfUpdate.cs` | 런처 배포 주소·채널·갱신 |
| `website/release-config.js` | 공개 다운로드/설문/버전 표기 |
| 서버 `/etc/gitlab-runner/config.toml`, GitLab CI/CD Variables | 러너·마운트·자동화 토큰; 별도 인계 |

## 백엔드 변수

`.env`를 읽는 방식이 다릅니다. `backend/`에서 직접 Java를 실행하면 Spring이 `.env`를 읽습니다. Docker Compose의 `.env`는 **compose 치환용**이며 `environment`에 적힌 키만 컨테이너로 전달됩니다.

| 변수 | 기본/예시 | 용도·주의 |
| --- | --- | --- |
| `DB_PASSWORD` | 기본 없음 | DB 사용자 비밀번호, 필수 |
| `DB_ROOT_PASSWORD` | 기본 없음(운영 compose) | MySQL 최초 초기화 root 비밀번호 |
| `DB_URL` | `jdbc:mysql://localhost:3306/metagame?serverTimezone=Asia/Seoul&characterEncoding=UTF-8` | 직접 실행 기본. compose는 호스트를 `mysql`로 지정 |
| `DB_USERNAME` | `metagame` | 앱 DB 계정. compose 고정 |
| `SERVER_PORT` | `8080` | 직접 실행 포트. compose는 내부 8080 유지 |
| `JWT_SECRET` | 기본 없음 | 표준 Base64, 디코드 후 32바이트 이상. 운영/개발 각자 생성 |
| `JWT_ACCESS_TOKEN_VALIDITY_MS` | `86400000` | 토큰 유효기간 24시간 |
| `GAME_RECONNECT_GRACE_MS` | `30000` | 재접속 유예 |
| `RELAY_PROVIDER` | `local` | 자체 릴레이. `edgegap`은 과거 대체 경로 |
| `RELAY_CONTROL_URL` | 직접 실행 `http://127.0.0.1:17779`; compose `http://host.docker.internal:17779` | 컨테이너 localhost는 호스트가 아님 |
| `RELAY_PUBLIC_ADDRESS` | 빈 값 | 외부에서 접근할 DNS/IP. 비면 릴레이를 쓰지 않고 직접 접속 경로로 감 |
| `RELAY_ALWAYS` | `false` | AP 격리 환경 등에서 릴레이 강제. 기본 compose는 전달하지 않음 |
| `EDGEGAP_RELAY_TOKEN` | 빈 값 | 기존 Edgegap 경로 전용. 일반 배포 필수 아님 |
| `RUNLOG_DIR` | `/var/log/metagame-runlogs` | compose에서 같은 내부 경로로 고정, 호스트 prod/dev 폴더 구분 |
| `RUNLOG_KEEP_DAYS` | `14` | 업로드 로그 보관 기간 |
| `CLIENT_MIN_VERSION` | 빈 값 | 빈 값이면 구버전 제한 꺼짐. `vX.Y.Z` 사용. **dev는 비워 둠** |
| `CLIENT_DOWNLOAD_URL` | `https://j15b202.p.ssafy.io/play/` | 구버전 거절 시 안내 주소 |

기본 compose에 없는 `RELAY_ALWAYS`, `CLIENT_MIN_VERSION`, `CLIENT_DOWNLOAD_URL`, `RUNLOG_KEEP_DAYS`, `JWT_ACCESS_TOKEN_VALIDITY_MS`, `GAME_RECONNECT_GRACE_MS`는 [보완 파일](config/compose.handover.yml)을 병합해야 `.env` 변경이 적용됩니다. 이는 저장소 사본 기준이며 현재 운영 서버가 동일하다고 확인한 것은 아닙니다.

비밀값 생성 예(Linux): `openssl rand -base64 48`. JWT에 URL-safe Base64(`-`, `_`)를 사용하지 않습니다. `.env` 권한은 600으로 제한합니다. 기존 MySQL 볼륨의 암호는 `.env`만 바꿔도 바뀌지 않습니다. 데이터 보존이 필요하면 기존 암호를 인계받거나 DBA가 DB 계정 암호와 앱 설정을 함께 변경합니다. 볼륨 삭제로 해결하지 않습니다.

## 빌드·실행 변수

| 키 | 쓰는 곳 | 의미 |
| --- | --- | --- |
| `JAVA_HOME` | Gradle | JDK 경로. 프로젝트 toolchain은 17 |
| `PYTHONUTF8=1`, `PYTHONIOENCODING=utf-8` | Python 도구 | Windows 한글·이모지 출력 인코딩 오류 방지 |
| `SPACE_BUILD_DIR` | Unity | 출력 폴더, 기본 `Build/Windows` |
| `SPACE_DEV` | Unity | `1` 개발, `0` 운영. `BuildWindowsDev`는 개발을 강제 |
| `SPACE_RELEASE_VERSION` | Unity | 릴리스 버전 `vX.Y.Z`; 최소 버전 게이트와 맞춤 |
| `SPACE_BUILD_ID` | CI 개발 빌드 | 개발 빌드 식별값 |
| `SPACE_EXPERIMENT` | Unity | 실험 라벨, 선택 |
| `AI_PACK_LATEST_URL` | AI 배포 정보 생성 | 기본 `/ai-packs/latest-companion-v4/pack-manifest.json`의 프로젝트 HTTPS 주소. `-`이면 비활성 |
| `AI_PACK_MANIFEST_URL`, `AI_PACK_MANIFEST_SHA256` | AI 배포 정보 생성 | 최신 조회 실패 시 대체할 고정 manifest 주소와 SHA-256. 둘 다 함께 설정 |
| 게임 인자 `-backend=https://서버/dev` | 게임 | 빌드 기본값보다 우선하는 접속 주소 |
| 런처 인자 `--dev`, `--version <버전>` | 런처 | 개발 목록·지정 버전. 게임과 런처 버전은 서로 다름 |

에디터는 기본적으로 개발 서버를 가리킵니다. 로컬 서버를 검증할 때 빌드 게임에 `-backend=http://127.0.0.1:8080`을 줍니다. 런처 기본 주소를 다른 도메인으로 옮길 때는 `launcher/`의 원래 도메인 참조도 모두 수정하고 재빌드해야 합니다.

## 서버·CI 인계

| 대상 | 주요 값/권한 | 인수 확인 |
| --- | --- | --- |
| EC2 | SSH 계정, 키, sudo, 보안그룹/DNS 권한 | 실제 관리자로 접속 확인. 키 본문은 문서에 넣지 않음 |
| MySQL | `metagame` DB/계정, root 관리 계정 | prod/dev 볼륨·비밀 분리 |
| GitLab | 프로젝트 접근, Package Registry, protected ref/runner 관리 | 클론·LFS·잡 실행·패키지 읽기 권한 |
| CI 비밀 | `GITLAB_TOKEN`, `JIRA_API_TOKEN`, `ANTHROPIC_API_KEY`, `MM_WEBHOOK_URL` 등 사용 중인 변수 | UI의 변수 목록과 `.gitlab-ci.yml`/`scripts/ci` 사용처 대조. 사용하지 않는 통합은 인수 범위에서 명시 |
| 빌드/배포 변수 | `DEPLOY_SPOOL`, `DEPLOY_TIMEOUT`, `BUILDS_DIR`, `SITE_DIR`, `PROMOTE_VERSION` | 기본 `/spool`, 420초, `/publish/builds`, `/publish/site`; dev는 `/spool-dev` |
| systemd 배포 | `APP_DIR`, `SPOOL_DIR`, `HEALTH_PORT`, `ENV_NAME` | prod `/home/ubuntu/metagame`, 8080; dev 별도 경로, 8081 |
| 릴레이 | `RELAY_IDLE_TIMEOUT=8`, `RELAY_SESSION_TTL=1800`(초) | UDP 17778 / 제어 TCP 17779; 유닛 인자가 포트 환경변수보다 우선 |

IDE의 제품·정확한 버전은 저장소에서 강제하지 않습니다. Unity IDE 연동 패키지 버전이 Visual Studio/Rider 제품 버전은 아닙니다. 인수자 PC에서 `code --version`, IDE의 About, `java -version`, `dotnet --info`, 서버에서 `nginx -v`, `docker version`, `docker compose version`, MySQL `SELECT VERSION()`으로 기록합니다.
