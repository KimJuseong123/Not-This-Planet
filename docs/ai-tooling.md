# AI 도구 연동 가이드 (Jira · GitLab)

> 관련 문서: [Jira 컨벤션](./jira-convention.md) · [워크플로우 구현 계획](./workflow-plan.md)
> Jira 작성을 AI에게 맡기는 것은 개인 선택이다([Jira 컨벤션 §0](./jira-convention.md)). 맡기는 사람만 이 문서를 따른다.

## 0. 원칙 — 계정이 아니라 내 PC에 연결한다

| 방식 | 어디에 저장되나 | 공용 Claude 계정에서 |
| --- | --- | --- |
| claude.ai **커넥터**(Atlassian Rovo, GitLab 등) | claude.ai **계정**에 연결됨 | ❌ 사용 금지 — 같은 계정을 쓰는 다른 사람에게 내 Jira/GitLab 권한이 노출된다 |
| Claude Code **MCP 서버** (`.mcp.json` / `~/.claude.json`) | **이 PC의 파일**. 계정과 무관 | ✅ 사용 |
| **CLI** (`glab`, `git`) | 이 PC의 설정 파일 | ✅ 사용 |

- 토큰은 항상 **개인 계정**의 것. 팀 공용 토큰을 만들지 않는다.
- 이 Windows 로그인(SSAFY)도 공용이면 토큰이 이 PC의 파일에 남는다는 점을 기억한다 → 만료는 프로젝트 종료일로, 자리를 떠날 때 `glab auth logout`, 필요하면 Atlassian 토큰 폐기.
- **토큰을 리포·채팅·로그에 붙이지 않는다.** `.mcp.json`은 커밋되는 파일이므로 값 대신 환경 변수 참조(`${...}`)만 적는다.

## 1. Jira — 로컬 MCP 서버 (`mcp-atlassian`)

API 토큰 방식. 브라우저 OAuth가 없어 어디서든 동작하고, 토큰은 내 환경 변수에만 있다. 이 PC에는 `uv`가 설치되어 있어 `uvx`로 바로 띄울 수 있다.

### 1.1 Atlassian API 토큰 만들기
1. https://id.atlassian.com/manage-profile/security/api-tokens → **API 토큰 만들기**
2. 이름 `claude-code-metagame`, 만료 = 프로젝트 종료일
3. 토큰을 복사해 아래 환경 변수에만 넣는다 (다시 볼 수 없으니 바로)

### 1.2 환경 변수 (PowerShell, 현재 Windows 사용자에 영구 저장)
```powershell
setx JIRA_URL "https://<우리사이트>.atlassian.net"
setx JIRA_USERNAME "<내 Atlassian 이메일>"
setx JIRA_API_TOKEN "<복사한 토큰>"
```
새 터미널(또는 VS Code 재시작)부터 적용된다. 확인: `echo $env:JIRA_API_TOKEN`.

### 1.3 `.mcp.json` (리포 루트, 이미 있음 — 값 없이 참조만)
```json
{
  "mcpServers": {
    "jira": {
      "command": "uvx",
      "args": ["mcp-atlassian"],
      "env": {
        "JIRA_URL": "${JIRA_URL}",
        "JIRA_USERNAME": "${JIRA_USERNAME}",
        "JIRA_API_TOKEN": "${JIRA_API_TOKEN}",
        "JIRA_PROJECTS_FILTER": "S15P21B202"
      }
    }
  }
}
```
- Claude Code가 이 프로젝트를 열면 "프로젝트 MCP 서버를 사용할까요?"라고 묻는다 — AI를 쓰지 않는 팀원은 **거절**하면 된다(다시 묻지 않음).
- 개인 설정으로 두고 싶으면 같은 내용을 `%USERPROFILE%\.claude.json`의 `mcpServers`에 넣어도 된다(사용자 범위).
- Docker를 쓰고 싶으면 `command: "docker", args: ["run","-i","--rm","-e","JIRA_URL","-e","JIRA_USERNAME","-e","JIRA_API_TOKEN","ghcr.io/sooperset/mcp-atlassian:latest"]`.
- 공식 Atlassian 원격 MCP(`https://mcp.atlassian.com/v1/sse`, OAuth)도 이 PC에만 저장되므로 계정 노출은 없다. 단 브라우저 로그인 1회가 필요하고 헤드리스 세션에서는 인증이 안 되므로 여기서는 토큰 방식을 기본으로 한다.

### 1.4 AI에게 Jira 이슈를 시킬 때
- 컨텍스트로 `docs/jira-convention.md`를 준다 (필수 필드, `[파트]` 제목, 설명 템플릿, 스토리 포인트 척도).
- 예: "S15P21B202 프로젝트에 `[UI] 로비 방 목록 스크롤 구현` Task 만들어. Epic S15P21B202-5, 담당 나, 스토리 포인트 5, 설명은 템플릿대로."
- 자동화(R1·R2)가 레이블·기한을 보정하고 제목 형식을 검사하므로, AI가 실수해도 사람과 같은 기준으로 걸러진다.

## 2. GitLab — `glab` CLI

MCP 서버 없이 CLI로 충분하다. Claude Code는 터미널에서 `glab`·`git`을 그대로 쓴다.

### 2.1 설치 · 로그인
```powershell
winget install glab.glab
# 새 터미널에서
glab auth login --hostname lab.ssafy.com
```
- 프롬프트에서 **Token** 방식 선택 → 개인 액세스 토큰 붙이기.
- 토큰 발급: lab.ssafy.com → 프로필 → **Access Tokens** → 이름 `glab-metagame`, 만료 = 프로젝트 종료일, 스코프 **`api`** (+ `write_repository`).
- 확인: `glab auth status`, `glab repo view s15-metaverse-game-sub1/S15P21B202`
- 토큰 저장 위치: `%APPDATA%\glab-cli\config.yml` (평문). 떠날 때 `glab auth logout --hostname lab.ssafy.com`.

### 2.2 자주 쓰는 것
```powershell
glab mr create --target-branch develop --fill        # 템플릿은 웹에서만 자동 적용 → --description-file로 Default.md 내용 사용
glab mr list --state opened                          # 승인 대기열
glab api projects/:id/protected_branches             # [Infra] 설정 확인
```
- 브랜치 생성은 컨벤션대로 **Jira 이슈 화면 "브랜치 만들기"** 를 쓴다. `glab`/`git`으로 만들어도 이름 규칙만 맞으면 동작은 같다.

## 3. [Infra]가 AI로 처리할 수 있는 것 / 없는 것

| 작업 | AI(MCP·CLI)로 | 비고 |
| --- | --- | --- |
| GitLab 보호 브랜치 5규칙 · 머지 설정 · 멤버 역할 | ✅ `glab api` (Maintainer 토큰) | 계획 부록 B를 스크립트로 일괄 적용 가능 |
| Jira 테스트 이슈 생성·전환·검증 | ✅ mcp-atlassian | 계획 §5 테스트 절차 |
| Jira Automation 규칙 R1~R9 | ⚠ 프로젝트 관리자 + 브라우저 세션이면 가능(템플릿 생성 API + 내부 API PUT) | 규약: `jira-gitlab-automation.md` 부록 A |
| Jira 필수 필드 · 워크플로우 전환 제한 | ❌ Jira 사이트 관리자 영역 | 〃 §1, §3 |
| GitLab Runner 등록 | ⚠ 러너 PC에서 직접 | 1-a |

## 4. 자주 걸리는 것

| 증상 | 원인 | 해결 |
| --- | --- | --- |
| `glab : 용어가 인식되지 않습니다` — 설치 직후 | winget이 사용자 PATH(`%LOCALAPPDATA%\Programs\glab`)에 추가했지만, **이미 열려 있던** 터미널·VS Code는 옛 PATH를 들고 있음 | 새 터미널을 열거나 **VS Code를 완전히 재시작**(통합 터미널만 새로 열면 안 됨 — VS Code 프로세스의 PATH를 상속). 급하면 현재 창에서 `$env:Path += ";$env:LOCALAPPDATA\Programs\glab"` |
| `이 시스템에서 스크립트를 실행할 수 없으므로 … profile.ps1 파일을 로드할 수 없습니다` | Windows PowerShell 5.1의 실행 정책이 기본값(Restricted)이라 프로필(conda init 훅)을 못 읽음. 기능엔 영향 없음 | Windows PowerShell에서 `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` (관리자 불필요). 또는 무시 |
| `.mcp.json`의 Jira 서버가 연결 실패 | `setx` 후 VS Code를 재시작하지 않아 환경 변수가 비어 있음 | VS Code 재시작 → `echo $env:JIRA_API_TOKEN`으로 확인 |
| `uvx mcp-atlassian` 첫 실행이 느림 | 패키지 다운로드·캐시 | 1회만. 이후엔 캐시 사용 |
