# 01. 클론부터 빌드·배포까지

## 1. 버전과 준비물

| 구분 | 저장소 기준 | 근거 |
| --- | --- | --- |
| Unity Editor | **6000.5.4f1**, revision `d550df8bd089` | `METAGAME/ProjectSettings/ProjectVersion.txt` |
| 렌더·입력 | URP 17.5.0, Input System 1.19.0 | `METAGAME/Packages/manifest.json` |
| 대상 | Windows x64, D3D11 | `SpaceBuild.cs` |
| JVM/JDK | Java toolchain 17, 컨테이너 `eclipse-temurin:17-jre` | `backend/build.gradle`, `backend/Dockerfile` |
| Gradle | wrapper 9.7.1 | `backend/gradle/wrapper/gradle-wrapper.properties` |
| WAS | Spring Boot 4.1.1, 내장 Tomcat 계열 | `backend/build.gradle`; 외부 WAS 설치 없음 |
| DB | `mysql:8.0`, utf8mb4 / utf8mb4_unicode_ci | compose. 패치 버전·이미지 digest는 고정 안 됨 |
| 런처 | .NET 10, `net10.0`, `win-x64`, self-contained | `launcher/NotThisPlanetLauncher.csproj` |
| CI Python | `python:3.12` | `.gitlab-ci.yml` |
| AI 제작 | Python 3.10 x64, 고정 의존성 | `tools/AICompanionService/requirements.lock`, `requirements-pack.txt` |
| 서버 | Ubuntu 계열, Docker Compose v2, nginx, systemd, certbot | 서버 스크립트의 `/home/ubuntu`와 유닛. 실제 OS/제품 패치 버전 미확인 |
| IDE | 제품 버전 미고정 | Visual Studio/Rider/VS Code 선택 가능. 인수 PC에서 실제 버전 기록 |

내장 Tomcat의 정확한 해석 버전은 `./gradlew dependencyInsight --dependency tomcat-embed-core --configuration runtimeClasspath`로 기록합니다. 문서에 없는 버전을 임의로 확정하지 않습니다. Unity 빌드에는 해당 버전의 Windows Build Support 모듈과 유효한 Unity 라이선스가 필요합니다.

## 2. 클론과 LFS

아래 명령은 Git Bash/Linux 셸이며, `REPO`는 실제 클론 경로로 지정합니다.

```bash
git lfs install --skip-repo
git clone --branch S15P21B202-753-infra https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202.git
cd S15P21B202
git config core.hooksPath .githooks
git lfs pull
git rev-parse HEAD
git lfs fsck
```

이번 문서가 아직 원격에 반영되지 않았다면 인계된 753 워크트리의 `exec/`도 함께 받습니다. GitLab 웹의 소스 ZIP만으로 LFS 실파일을 확보했다고 판단하지 않습니다. `git lfs update --force`는 팀 훅을 덮어쓸 수 있어 사용하지 않습니다.

빈 캐시에서 Unity를 처음 열면 패키지 복원과 에셋 임포트가 필요합니다. 저장소 루트가 아니라 `METAGAME/`을 프로젝트 경로로 선택합니다. 빌드 직전 LFS 포인터만 남은 파일, 패키지 복원 실패, `.meta` 누락을 확인합니다. 이번 체크아웃에서 공급사 폰트 3개에 LFS 형식 경고가 관찰되었습니다([검토 결과](06-review.md)).

## 3. 백엔드 로컬 실행

1. JDK 17과 Docker 엔진을 준비합니다. Docker CLI 설치만으로는 DB가 실행되지 않습니다.
2. `backend/.env.example`을 `backend/.env`로 복사합니다. 예시의 비밀은 로컬 전용이며 운영에 재사용하지 않습니다.
3. `backend/`에서 실행합니다.

```bash
docker compose up -d mysql
chmod +x gradlew
./gradlew --no-daemon test bootJar
./gradlew bootRun
```

Windows PowerShell에서는 `./gradlew.bat --no-daemon test bootJar`, `./gradlew.bat bootRun`을 사용합니다. 테스트는 H2를 사용하므로 MySQL 없이도 실행하도록 구성되어 있습니다. 로컬 3306이 이미 점유되어 있으면 포트 매핑과 `DB_URL`을 함께 변경합니다.

산출물은 `backend/build/libs/backend-0.0.1-SNAPSHOT.jar`입니다. `backend/Dockerfile`은 `*-SNAPSHOT.jar`를 복사하므로 오래된 같은 패턴의 JAR를 여러 개 두지 않습니다.

```bash
curl -sS -X POST http://127.0.0.1:8080/auth/guest \
  -H 'Content-Type: application/json' \
  -d '{"deviceId":"porting-local","nickname":"porting"}'
```

이 요청은 게스트 계정을 만들 수 있습니다. 응답의 토큰은 보고서에 붙이지 않습니다. 운영 최소 버전 게이트를 켠 환경에서는 현재 허용된 `clientVersion`도 요청에 포함해야 합니다.

## 4. 게임 빌드(Windows PowerShell)

전용 빌드 체크아웃에서 수행합니다. 다음은 **개발 서버**를 가리키는 빌드입니다.

```powershell
$repo = (Get-Location).Path
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.5.4f1/Editor/Unity.exe'
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
$env:SPACE_DEV = '1'
$env:SPACE_BUILD_DIR = Join-Path $repo 'Build/Windows-dev'
python scripts/ci/write_ai_pack_source.py
if ($LASTEXITCODE -ne 0) { throw 'AI 배포 정보 생성 실패' }
$unityArgs = @('-batchmode', '-quit', '-projectPath', "`"$repo/METAGAME`"",
  '-buildTarget', 'Win64', '-executeMethod',
  'Metagame.EditorTools.SpaceBuild.BuildWindowsDev',
  '-logFile', "`"$repo/unity-build.log`"")
$buildProcess = Start-Process -FilePath $unity -ArgumentList $unityArgs `
  -WorkingDirectory $repo -Wait -PassThru -WindowStyle Hidden
if ($buildProcess.ExitCode -ne 0) { throw 'Unity 빌드 실패: unity-build.log 확인' }
```

최종 성공은 로그의 `BuildResult.Succeeded`에 해당하는 성공 출력과 `NotThisPlanet.exe`·`NotThisPlanet_Data` 등 산출물을 함께 확인합니다. 빌드 전처리의 조기 반환 때문에 종료 코드 하나만으로 성공을 판정하지 않습니다. 생성된 **폴더 전체**를 배포합니다.

운영 빌드는 같은 순서에서 `SPACE_DEV=0`, `SPACE_RELEASE_VERSION=vX.Y.Z`(실제 결정한 버전), 출력 폴더를 지정하고 메서드를 `Metagame.EditorTools.SpaceBuild.BuildWindows`로 바꿉니다. AI 배포 정보 생성에는 `--required`를 붙입니다. 새 도메인은 `AI_PACK_LATEST_URL` 및 `BackendEndpoints`/런처 주소도 함께 반영합니다. 테스트용으로는 게임 실행 인자 `-backend=...`를 쓸 수 있습니다.

`write_ai_pack_source.py`는 추적 중인 JSON을 생성하며 `SpaceBuild`는 설정·에셋 전처리를 합니다. 빌드 후 변경 목록을 검토하고 결과물을 확인합니다. 이전 README의 두 씬만 수동 등록하지 말고 [현재 9개 씬](00-project-map.md)을 유지합니다.

## 5. 새 서버 배포

### 5-1. 네트워크·도구

서버 관리자가 Docker Engine/Compose v2, nginx, Python 3, certbot nginx 플러그인을 설치하고 DNS A 레코드를 새 서버로 연결합니다. 기존 서버의 정확한 패키지 버전과 이미지 digest를 인계받아 재현합니다.

| 포트 | 공개 범위 | 용도 |
| --- | --- | --- |
| TCP 22 | 관리자 IP | SSH |
| TCP 80/443 | 인터넷 | 인증서 발급/HTTPS/WSS/다운로드 |
| UDP 17778 | 플레이어 네트워크 | 게임 릴레이; 클라우드 보안그룹과 호스트 방화벽 모두 허용 |
| TCP 17779 | Docker 백엔드 서브넷과 로컬 | 릴레이 제어. 인터넷 공개 금지 |
| TCP 8080/8081 | `127.0.0.1` | 운영/개발 백엔드 nginx upstream |
| TCP 3306 | Docker 내부 | 운영 DB. 외부 포트 매핑 없음 |

릴레이 유닛은 제어 API를 `0.0.0.0`에 바인딩합니다. 주석의 “로컬 전용”만 믿지 말고 실제 Docker 서브넷을 조회해 17779 접근 규칙을 제한합니다. UFW 활성화 전 현재 SSH 접속이 유지되는 규칙을 확인합니다.

### 5-2. DB와 백엔드

```bash
REPO=/path/to/S15P21B202
APP=/home/ubuntu/metagame
mkdir -p "$APP/build/libs"
cp "$REPO/scripts/deploy/server/compose.prod.yml" "$APP/docker-compose.yml"
cp "$REPO/exec/config/compose.handover.yml" "$APP/docker-compose.override.yml"
cp "$REPO/backend/Dockerfile" "$APP/Dockerfile"
cp "$REPO/backend/build/libs/backend-0.0.1-SNAPSHOT.jar" "$APP/build/libs/"
# 신규 서버에서만 예시 파일을 복사한다. 기존 .env를 덮어쓰지 않는다.
cp -n "$REPO/exec/config/server.env.example" "$APP/.env"
chmod 600 "$APP/.env"
```

관리자가 `.env`의 `CHANGE_ME`를 모두 실제 값으로 교체합니다. `JWT_SECRET`은 `openssl rand -base64 48`로 생성하고 `RELAY_PUBLIC_ADDRESS`는 새 서버 DNS로 지정합니다. 최초 검증에서는 `CLIENT_MIN_VERSION`을 비웁니다.

```bash
cd "$APP"
docker compose config --quiet
docker compose up -d mysql
```

운영 인수라면 이 시점에 [04의 덤프 복원](04-database.md)을 먼저 마칩니다. 새 DB로 기동하는 경우에만 백엔드가 JPA 엔티티 기준으로 테이블을 만듭니다. **빈 DB 기동은 기존 데이터 복원이 아닙니다.**

```bash
docker compose up -d --build backend
docker compose ps
docker compose logs --tail 100 backend
```

개발 환경은 `/home/ubuntu/metagame-dev`에 `compose.dev.yml`을 `docker-compose.yml`로 복사하여 동일하게 준비합니다. 디렉터리/프로젝트 이름을 `metagame-dev`로 유지하고 DB/JWT 비밀을 따로 생성합니다. 운영 8080, 개발 8081이고 각각 로그·볼륨도 분리됩니다. 기본 운영 유닛은 `/home/ubuntu/metagame`을 전제로 합니다.

### 5-3. 릴레이

`scripts/relay/metagame-relay.service`의 사용자 `ubuntu`를 실제 서비스 계정과 맞춥니다.

```bash
sudo install -d /opt/metagame-relay
sudo install -m 644 "$REPO/scripts/relay/metagame_relay.py" /opt/metagame-relay/
sudo install -m 644 "$REPO/scripts/relay/metagame-relay.service" /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now metagame-relay
curl -fsS http://127.0.0.1:17779/health
```

백엔드 컨테이너에서도 `host.docker.internal:17779`에 닿는지 검사합니다. 단순 로컬 health 성공만으로 외부 UDP 연결 성공을 판단하지 않습니다. [05](05-demo.md)의 서로 다른 PC 2인 접속이 필요합니다.

### 5-4. nginx와 TLS: 새 서버 순서

저장소 nginx 사본은 **이미 존재하는 인증서 파일**을 참조합니다. 인증서가 없는 서버에 그대로 먼저 활성화하면 `nginx -t`부터 실패합니다.

1. 새 도메인을 사용하는 HTTP 전용 임시 server 블록으로 nginx를 기동하고 DNS/80 접근을 확인합니다.
2. `sudo certbot --nginx -d <새 도메인>`으로 인증서를 발급합니다.
3. `scripts/deploy/server/nginx/upgrade-map.conf`, `limits.conf`를 `/etc/nginx/conf.d/`로 복사합니다.
4. `metagame.conf`의 `server_name`, 인증서 경로를 새 도메인으로 바꾸어 `/etc/nginx/sites-available/metagame`에 설치하고 sites-enabled에서 활성화합니다. 기존 사이트가 있으면 중복 server_name을 정리합니다.
5. AI 서비스를 포함한다면 [AI location 예시](config/nginx-ai-packs.conf.example)를 HTTPS server 블록 안에 추가합니다. 저장소 nginx 사본에는 해당 경로가 없습니다.
6. `sudo nginx -t` 성공 후 `sudo systemctl reload nginx`, `sudo certbot renew --dry-run`으로 확인합니다.

`/dev/`의 `proxy_pass http://127.0.0.1:8081/;` 끝 `/`는 접두사 제거에 필요합니다. WSS는 HTTP/1.1, Upgrade, Connection 헤더와 3600초 timeout을 유지합니다. `/logs`는 9m 요청 한도, 120초 프록시 timeout입니다.

## 6. 런처·게임 파일·사이트

런처 빌드(저장소 루트, Windows):

```powershell
$launcherVersion = (Get-Content launcher/VERSION).Trim()
dotnet publish launcher/NotThisPlanetLauncher.csproj -c Release "-p:Version=$launcherVersion"
if ($LASTEXITCODE -ne 0) { throw '런처 publish 실패' }
& ./launcher/bin/Release/net10.0/win-x64/publish/NotThisPlanetLauncher.exe --selfcheck
```

산출물과 게임 폴더를 서버의 **공개 웹 폴더 밖 임시 인계 디렉터리**로 전송합니다. 아래 명령은 서버에서 저장소 루트 기준으로 실행하며 버전과 경로는 실제 산출물로 바꿉니다.

```bash
python3 scripts/launcher/publish_launcher.py /staging/NotThisPlanetLauncher.exe v1.1.0 --out /var/www/metagame-builds --dry-run
python3 scripts/launcher/publish_launcher.py /staging/NotThisPlanetLauncher.exe v1.1.0 --out /var/www/metagame-builds
python3 scripts/launcher/make_manifest.py /staging/NotThisPlanet v1.2.3 --out /var/www/metagame-builds --no-latest
# 파일 검증·시연 후 사용자가 받는 버전을 전환한다.
BUILDS_DIR=/var/www/metagame-builds PROMOTE_VERSION=v1.2.3 python3 scripts/ci/publish_builds.py promote
```

`v1.2.3`은 명령 형식 예시이며 현재 운영 버전이라고 확정한 값이 아닙니다. 게임의 `SPACE_RELEASE_VERSION`과 같아야 합니다. `--keep` 기본 보관 정책과 과거 파일 회수 동작을 확인하고 롤백 대상 버전은 별도로 보관합니다. 런처 `--min`은 옛 런처 차단이 필요할 때만 지정합니다.

사이트는 npm 없이 `website/`를 배포합니다. `release-config.js`의 게임 버전·런처 주소·다운로드 용량·설문 URL을 실제 값으로 바꿉니다. 사이트 전체 백업 후 전용 디렉터리에 실행합니다. `publish_site.py`는 대상에 남은 불필요 파일을 삭제하므로 공유 폴더를 주지 않습니다.

```bash
git lfs pull --include="website/assets/**" --exclude=""
SITE_DIR=/var/www/metagame-site BUILDS_DIR=/var/www/metagame-builds python3 scripts/ci/publish_site.py
```

웹 루트는 저장소 전체가 아닙니다. nginx가 `/play/`를 `/var/www/metagame-site/`에, 허용된 `/builds/` 파일만 `/var/www/metagame-builds/`에 연결합니다.

## 7. AI 추가팩

일반 플레이의 필수 서버는 아닙니다. AI 동료를 시연할 때는 실행기/모델이 별도로 필요합니다.

1. [외부 서비스 문서](03-external-services.md)의 모델·Ollama와 기존 검증된 팩을 인계받습니다.
2. 재제작할 경우 별도 Python 3.10 가상환경에 `requirements-pack.txt`를 설치하고 `tools/AICompanionService/build_portable.py --help` 및 [제작 계약](../tools/AICompanionService/PORTABLE.md)을 확인합니다. `--output`, `--ollama-dir`, `--model-root`, `--tokenizer`가 필요합니다.
3. `runtime.zip`, `models.zip`, `pack-manifest.json`과 고지를 인계합니다. 생성 직후 preview 팩의 `release_approved=false`를 검증 없이 true로 바꾸지 않습니다. 실제 배포 승인된 팩을 사용합니다.
4. `schema=1`, `platform=windows-x64-cpu`, `protocol=companion-v4`, 승인 상태, ZIP 2개의 크기·해시·배포 경로를 `AiPackValidation.Manifest`와 대조합니다.
5. 공개 서버의 전용 버전 폴더에 저장하고 `latest-companion-v4`가 승인된 폴더를 가리키도록 구성합니다. nginx 예시의 alias 경로와 실제 폴더를 맞춥니다.
6. HTTPS 최신 manifest·두 ZIP 다운로드와 해시를 검사합니다. 게임 빌드 전에 `write_ai_pack_source.py`를 실행합니다. 저장소의 빈 JSON만으로 수동 빌드하면 AI 설치 주소가 없습니다.
7. 호스트 PC에서 설치·취소·재시도·음성 명령을 검증합니다. 18765/11439 포트 충돌과 마이크 권한도 확인합니다.

## 8. CI/CD 복원과 롤백

`.gitlab-ci.yml`의 `linux` 러너는 일반 테스트/빌드, protected `deploy` 러너는 배포를 담당합니다. Unity 이미지 버전은 `ubuntu-6000.5.4f1-base-3.2.2` 및 `windows-mono-3.2.2`입니다. 라이선스와 GitLab 변수는 기존 운영자에게 별도로 인계받습니다.

`scripts/deploy/host/install.sh`, `install-dev.sh`는 systemd path/service와 스풀을 설치합니다. 설정과 비밀을 준비한 뒤 관리자 계정으로 적용합니다. `mount-release-dirs.sh`는 배포 러너에 `/publish/builds`, `/publish/site`를 연결합니다. **러너 작업 경로 `/builds`에 공개 폴더를 마운트하지 않습니다.** `deploy.sh`는 스풀 밖 `/opt/metagame-deploy`에 둡니다.

develop은 dev 배포, 운영은 해당 protected 릴리스 잡의 조건과 수동 승인을 확인합니다. 실제 서버에서 `systemctl status metagame-deploy.path metagame-deploy-dev.path`, 스풀 `STATUS`, `deploy.log`를 확인합니다. 백엔드 재시작은 방/WebSocket 세션에 영향을 줍니다.

롤백은 세 부분을 별도로 판단합니다.

- JAR: 스풀 `previous/`의 직전 JAR 복귀. 자동 배포는 준비 확인 실패 시 이를 시도합니다.
- 게임: 보존된 manifest/files가 있는 버전을 `PROMOTE_VERSION`으로 전환. 최소 버전도 롤백 게임을 허용하도록 맞춥니다.
- DB: JAR 롤백으로 스키마가 돌아오지 않습니다. `ddl-auto: update`이므로 변경 전 백업과 별도 복원 검증이 필요합니다.

최소 버전 게이트를 켜면 버전 없는 현재 배포 health 요청이 거절될 수 있습니다. [검토 R3](06-review.md)를 해결한 후 게이트를 활성화합니다.
