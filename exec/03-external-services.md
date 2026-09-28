# 03. 외부 서비스와 계정 인계

현재 저장소의 사용처를 기준으로 정리했습니다. 운영 계정의 소유자·요금제·만료일은 저장소로 확인할 수 없으므로 인수 시 실제 관리자와 확인합니다. 비밀번호·토큰 원문은 이 폴더에 저장하지 않습니다.

## 실행·배포에 쓰는 서비스

| 서비스 | 가입/권한 준비 | 활용·설정 | 인수 확인 |
| --- | --- | --- | --- |
| GitLab (`lab.ssafy.com`) | 기관 계정과 프로젝트 초대 필요. 개인 계정 생성만으로 이 프로젝트 접근 권한이 생기지 않음 | 소스, LFS, CI, Package Registry. 원격 주소는 `01` 참조 | clone, LFS pull, registry 다운로드, protected branch/runner 권한 |
| Unity Hub/Editor | Unity 계정으로 로그인하고 조직의 적합한 라이선스를 활성화 | 6000.5.4f1 및 Windows Build Support 설치. CI는 별도 라이선스 구성 | 개인 PC와 CI의 활성화 방식·만료·담당자 |
| 서버/EC2·DNS | 기존 지급 서버의 관리자 권한 인계 또는 새 서버와 DNS 관리 계정 준비 | 기존 코드의 도메인은 `j15b202.p.ssafy.io`. Ubuntu, Docker, nginx, TLS, 릴레이 운영 | SSH 키, 보안그룹, IP, DNS 변경권, 디스크, 백업 저장소 |
| 인증서/Certbot | 새 도메인 소유·DNS/80 접근 확보 | nginx TLS 인증서 발급·갱신. 경로는 `/etc/letsencrypt/live/<도메인>/` | 만료일·자동 갱신 timer·renew dry-run |
| 컨테이너/패키지 레지스트리 | 해당 빌드 환경에서 공개 registry에 접근 가능해야 함 | Docker 이미지, Maven Central, Gradle 배포, Unity 패키지, NuGet 복원 | 프록시·방화벽·라이선스 제한 및 캐시 |
| Google Forms | 기존 폼의 편집/응답 관리 권한 이전, 또는 조직 계정으로 대체 폼 생성 | `website/release-config.js`의 공개 응답 URL, `index.html`의 fallback 2곳 변경 | 비로그인 응답 가능 여부, 응답 데이터 소유자 |

MySQL은 서버 내부 자체 운영이며 별도 클라우드 DB 가입이 필수는 아닙니다. 게임의 게스트 로그인은 `deviceId`와 닉네임 기반입니다. 소셜 OAuth 설정은 사용하지 않습니다. `users.steam_id`는 향후 연동용 자리이며 현재 Steam 로그인을 구성하는 절차가 아닙니다. Mirror를 사용하고 있으므로 첨부 예시의 Photon Cloud 가입 절차를 그대로 적용하지 않습니다.

## 선택 기능: AI 동료

| 구성 | 확보 방법 | 저장소 사용처 |
| --- | --- | --- |
| Ollama CPU 실행기 | 기존 검증 버전의 실행 파일/라이브러리와 고지 인계. 제작 시 공식 배포물 사용 | `tools/AICompanionService/build_portable.py` |
| Qwen3 1.7B | 기존 로컬 모델 저장소 또는 준비 도구로 확보. 모델 manifest/blob 함께 필요 | `engine.py`, `prepare_models.py` |
| 한국어 Whisper small | `ghost613/faster-whisper-small_korean-zeroth` 모델 파일 확보 | `prepare_models.py` |
| 비교용 Whisper base | `Systran/faster-whisper-base`; 실험용 | 기본 배포팩에는 포함하지 않음 |
| 실행기/모델 ZIP 배포 | 프로젝트 HTTPS 서버의 `/ai-packs/`에 승인된 팩 게시 | `write_ai_pack_source.py`, `AiPackLatest.cs` |

기존 준비 스크립트는 공개 모델을 사용하며 유료 LLM API 키를 요구하지 않습니다. 모델을 새로 받을 때는 해당 모델의 접근 조건과 라이선스를 확인하고, 검증한 모델 파일의 해시/리비전과 Ollama 버전을 기록합니다. 저장소만으로 기존 모델 바이너리의 정확한 리비전까지 복원할 수는 없습니다.

개발 실험 서비스의 8765와 배포 워커의 18765는 서로 다른 실행 방식입니다. 일반 사용자에게 Python·Ollama 설치를 요구하는 대신 배포팩에 필요한 실행기를 포함합니다. 배포 워커는 같은 PC의 127.0.0.1에 바인딩하며 실행 때 생성한 인증 토큰을 표준입력으로 받습니다. 고정 토큰을 문서·명령행에 넣지 않습니다. 음성/LLM 추론은 로컬 처리이며 외부 모델 API 호출과 구분합니다.

## 협업 자동화: 게임 실행과 분리

| 서비스 | 필요한 인계 | 설정 위치 |
| --- | --- | --- |
| Jira | 프로젝트 열람·이슈/자동화 관리 계정, 필요한 API 토큰 | `docs/jira-gitlab-automation.md`, `scripts/ci` |
| Mattermost | 알림 채널·webhook 관리 권한 | `MM_WEBHOOK_URL`, CI 알림 도구 |
| AI 코드 리뷰 | 기존 제공자 계정/사용 권한·CI 비밀변수 | `scripts/ci/ai_review.py`, CI 구성 |
| Edgegap | 과거 릴레이 대체 경로를 재사용할 때만 계정/API 권한 필요 | `relay.provider=edgegap`, `EDGEGAP_RELAY_TOKEN` |

자체 릴레이가 현재 기본이므로 Edgegap 토큰을 만들었다고 정상 멀티플레이 복원이 끝난 것은 아닙니다. 외부 자동화를 새 계정으로 이전할 때 토큰 발급 → CI 변수 등록 → 비운영 환경 검증 → 이전 토큰 폐기 순으로 담당자가 진행합니다.

## 계정 인계 기록 양식

| 항목 | 기입 |
| --- | --- |
| 서비스/프로젝트 이름 | |
| 기존 소유자 → 인수자 | |
| 인수자에게 부여한 역할 | |
| 만료일/결제·갱신 담당자 | |
| 비밀값 전달 경로(값은 기록 금지) | |
| 접근·기능 검증 시각과 결과 | |
