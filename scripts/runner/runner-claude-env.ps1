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
param([string]$InstallDir = "C:\GitLab-Runner", [string]$ClaudeUserProfile = $env:USERPROFILE,
      [switch]$SelfTest)
$ErrorActionPreference = "Stop"

function Set-RunnerEnvironment {
    <#
      config.toml 의 모든 [[runners]] 섹션에 environment 줄을 넣거나 교체한다.
      섹션마다 상태를 초기화한다 — 예전에는 $done 이 전역 1회여서 [[runners]] 가 둘 이상일 때
      두 번째 이후 러너의 기존 environment 줄이 삭제되고 대체 줄도 들어가지 않았다(설정 손실, S15P21B202-135).
    #>
    param([string[]]$Lines, [string]$EnvLine)
    $out = New-Object System.Collections.Generic.List[string]
    $inRunner = $false      # [[runners]] 최상위 테이블 안인가
    $inSubTable = $false    # 그 안의 [runners.xxx] 하위 테이블 안인가
    $done = $false          # 이 러너에 environment 를 이미 넣었나
    foreach ($l in $Lines) {
        if ($l -match '^\s*\[\[runners\]\]') {
            if ($inRunner -and -not $done) { $out.Add("  $EnvLine") }   # 앞 섹션에 못 넣었으면 닫히기 전에 넣는다
            $inRunner = $true; $inSubTable = $false; $done = $false
            $out.Add($l); continue
        }
        if ($inRunner -and $l -match '^\s*\[runners\.') {            # [runners.docker] 등 하위 테이블
            if (-not $done) { $out.Add("  $EnvLine"); $done = $true }   # 하위 섹션 전에 삽입
            $inSubTable = $true
            $out.Add($l); continue
        }
        if ($inRunner -and $l -match '^\s*\[') {                      # 러너와 무관한 최상위 섹션([session_server] 등)
            if (-not $done) { $out.Add("  $EnvLine"); $done = $true }
            $inRunner = $false; $inSubTable = $false                    # $done 여부와 무관하게 러너 구역을 벗어난다
            $out.Add($l); continue                                      # (안 그러면 그 섹션의 environment 줄이 삭제된다)
        }
        if ($inRunner -and -not $inSubTable -and $l -match '^\s*environment\s*=') {
            if (-not $done) { $out.Add("  $EnvLine"); $done = $true }
            continue                                                     # 러너의 기존 값 교체(중복 줄은 버린다)
        }
        $out.Add($l)                                                     # 그 밖의 줄(하위 테이블·다른 섹션의 키)은 그대로
    }
    if ($inRunner -and -not $done) { $out.Add("  $EnvLine") }
    return $out
}

if ($SelfTest) {
    # 관리자 권한·러너 설치 없이 변환 규칙만 확인한다: pwsh -File scripts\runner\runner-claude-env.ps1 -SelfTest
    $sample = @(
        'concurrent = 2',
        '[[runners]]', '  name = "a"', '  environment = ["OLD=1"]', '  [runners.custom_build_dir]',
        '[[runners]]', '  name = "b"', '  environment = ["OLD=2"]', '  [runners.docker]',
        '    environment = ["KEEP_SUBTABLE=1"]',
        '[[runners]]', '  name = "c"',
        '[session_server]', '  environment = ["KEEP_OTHER=1"]'
    )
    $res = Set-RunnerEnvironment -Lines $sample -EnvLine 'environment = ["NEW=1"]'
    $count = ($res | Where-Object { $_ -match 'NEW=1' }).Count
    $old = ($res | Where-Object { $_ -match 'OLD=' }).Count
    if ($count -ne 3) { throw "러너 3개 모두에 들어가야 하는데 $count 개" }
    if ($old -ne 0) { throw "러너의 기존 environment 줄이 남았다: $old 개" }
    # 우리 것이 아닌 environment 줄은 손대지 않는다(하위 테이블·다른 최상위 섹션) — MR !79 리뷰 지적
    foreach ($keep in @('KEEP_SUBTABLE=1', 'KEEP_OTHER=1', 'concurrent = 2')) {
        if (-not ($res | Where-Object { $_ -match [regex]::Escape($keep) })) { throw "보존해야 할 줄이 사라졌다: $keep" }
    }
    if ($res[$res.Count - 1] -notmatch 'KEEP_OTHER') { throw "[session_server] 뒤 줄 순서가 어긋났다" }
    # 개수만 세면 순서 회귀가 통과한다 — 각 [[runners]] 블록 안에 새 줄이 정확히 1개인지 위치로 확인
    # (MR !79 리뷰 지적)
    $blockStarts = @(); for ($i = 0; $i -lt $res.Count; $i++) { if ($res[$i] -match '^\s*\[\[runners\]\]') { $blockStarts += $i } }
    if ($blockStarts.Count -ne 3) { throw "[[runners]] 블록이 3개가 아니다: $($blockStarts.Count)" }
    for ($b = 0; $b -lt $blockStarts.Count; $b++) {
        $from = $blockStarts[$b] + 1
        $to = if ($b + 1 -lt $blockStarts.Count) { $blockStarts[$b + 1] - 1 } else { $res.Count - 1 }
        $inBlock = @($res[$from..$to] | Where-Object { $_ -match 'NEW=1' })
        if ($inBlock.Count -ne 1) { throw "$($b + 1)번째 [[runners]] 블록의 새 environment 줄이 $($inBlock.Count) 개" }
    }
    Write-Host "SelfTest OK — [[runners]] 3개 반영 · 하위 테이블/다른 섹션의 environment 와 최상위 키 보존" -ForegroundColor Green
    exit 0
}
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "관리자 PowerShell 에서 실행하세요." }
$cfg = Join-Path $InstallDir "config.toml"; $exe = Join-Path $InstallDir "gitlab-runner.exe"
if (-not (Test-Path $cfg)) { throw "$cfg 없음 — setup-windows-runner.ps1 먼저" }
$claudeDir = Join-Path $ClaudeUserProfile ".claude"; $claudeBin = Join-Path $ClaudeUserProfile ".local\bin\claude.exe"
if (-not (Test-Path (Join-Path $claudeDir ".credentials.json"))) { throw "$claudeDir\.credentials.json 없음 — 이 사용자로 `claude` 실행 후 /login 먼저" }
if (-not (Test-Path $claudeBin)) { throw "$claudeBin 없음 — irm https://claude.ai/install.ps1 | iex 로 설치" }

$envLine = 'environment = ["CLAUDE_CONFIG_DIR=' + ($claudeDir -replace '\\', '\\') + '", "CLAUDE_BIN=' + ($claudeBin -replace '\\', '\\') + '", "PYTHONUTF8=1"]'
$lines = Get-Content $cfg
$out = Set-RunnerEnvironment -Lines $lines -EnvLine $envLine
Copy-Item $cfg "$cfg.bak" -Force
[IO.File]::WriteAllLines($cfg, $out)
Write-Host "config.toml 갱신:`n  $envLine" -ForegroundColor Cyan
& $exe restart
Start-Sleep -Seconds 3
& $exe verify --config $cfg
Write-Host "완료. 팀장이 변수 AI_REVIEW_CLI=true(+GITLAB_TOKEN) 를 넣으면 ai-review 잡이 이 PC 의 Claude 로그인으로 실행됩니다." -ForegroundColor Green
