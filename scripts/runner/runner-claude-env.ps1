<#
.SYNOPSIS
  SYSTEM 계정으로 도는 gitlab-runner 잡이 이 PC 사용자의 Claude Code 로그인(~\.claude)과 CLI 를 쓰도록
  config.toml 의 [[runners]] 섹션에 environment 를 넣고 서비스를 재시작한다. 비밀번호·토큰 불필요.
.DESCRIPTION
  실행(관리자 PowerShell, Claude 에 로그인한 사용자 세션에서):  .\scripts\runner\runner-claude-env.ps1
  넣는 값:
    CLAUDE_CONFIG_DIR = <이 사용자>\.claude            (로그인 자격 .credentials.json 이 있는 폴더 — SYSTEM 이 읽고, 토큰 갱신 시 씀)
    CLAUDE_BIN        = <이 사용자>\.local\bin\claude.exe
    PYTHONUTF8        = 1
  주의: 같은 자격 파일을 사용자의 VS Code Claude Code 와 러너가 함께 쓴다. 로그아웃(/logout)하면 CI 의 AI 리뷰도 멈추고,
        토큰 갱신이 겹치면 한쪽이 "Not logged in" 이 될 수 있다(그땐 사용자 세션에서 `claude` 로 재로그인).
#>
param([string]$InstallDir = "C:\GitLab-Runner", [string]$ClaudeUserProfile = $env:USERPROFILE)
$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "관리자 PowerShell 에서 실행하세요." }
$cfg = Join-Path $InstallDir "config.toml"; $exe = Join-Path $InstallDir "gitlab-runner.exe"
if (-not (Test-Path $cfg)) { throw "$cfg 없음 — setup-windows-runner.ps1 먼저" }
$claudeDir = Join-Path $ClaudeUserProfile ".claude"; $claudeBin = Join-Path $ClaudeUserProfile ".local\bin\claude.exe"
if (-not (Test-Path (Join-Path $claudeDir ".credentials.json"))) { throw "$claudeDir\.credentials.json 없음 — 이 사용자로 `claude` 실행 후 /login 먼저" }
if (-not (Test-Path $claudeBin)) { throw "$claudeBin 없음 — irm https://claude.ai/install.ps1 | iex 로 설치" }

$envLine = 'environment = ["CLAUDE_CONFIG_DIR=' + ($claudeDir -replace '\\', '\\') + '", "CLAUDE_BIN=' + ($claudeBin -replace '\\', '\\') + '", "PYTHONUTF8=1"]'
$lines = Get-Content $cfg
$out = New-Object System.Collections.Generic.List[string]
$inRunner = $false; $done = $false
foreach ($l in $lines) {
    if ($l -match '^\[\[runners\]\]') { $inRunner = $true; $out.Add($l); continue }
    if ($inRunner -and $l -match '^\s*environment\s*=') { if (-not $done) { $out.Add("  $envLine"); $done = $true }; continue }   # 기존 값 교체
    if ($inRunner -and -not $done -and $l -match '^\s*\[runners\.') { $out.Add("  $envLine"); $done = $true }          # 하위 섹션 전에 삽입
    $out.Add($l)
}
if ($inRunner -and -not $done) { $out.Add("  $envLine") }
Copy-Item $cfg "$cfg.bak" -Force
[IO.File]::WriteAllLines($cfg, $out)
Write-Host "config.toml 갱신:`n  $envLine" -ForegroundColor Cyan
& $exe restart
Start-Sleep -Seconds 3
& $exe verify --config $cfg
Write-Host "완료. 팀장이 변수 AI_REVIEW_CLI=true(+GITLAB_TOKEN) 를 넣으면 ai-review 잡이 이 PC 의 Claude 로그인으로 실행됩니다." -ForegroundColor Green
