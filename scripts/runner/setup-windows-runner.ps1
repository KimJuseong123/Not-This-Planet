<#
.SYNOPSIS
  METAGAME GitLab 프로젝트 러너를 Windows PC에 설치·등록한다 (shell executor, 기본 powershell 5.1).
.DESCRIPTION
  1) C:\GitLab-Runner 에 gitlab-runner.exe 다운로드  2) Windows 서비스로 설치·시작
  3) 러너 등록 (토큰은 환경변수 RUNNER_TOKEN 으로만 받는다 — 채팅·문서에 적지 말 것)
  4) Python 확인(없으면 winget 으로 3.12 설치) — scripts/ci/*.py 는 표준 라이브러리만 사용
  실행: 관리자 PowerShell 에서
      $env:RUNNER_TOKEN = "<팀장이 Settings → CI/CD → Runners → New project runner 에서 발급한 glrt-… 토큰>"
      .\scripts\runner\setup-windows-runner.ps1 [-Unity]      # -Unity: unity-win 태그 추가(Unity 6000.5.4f1 설치된 PC)
  토큰 발급 시 팀장이 체크할 것: "Run untagged jobs" ✔ (mr-lint 등 태그 없는 잡을 받기 위해), 태그 windows[,unity-win]
  ※ glrt- 토큰 등록에서는 태그·untagged·잠금 등을 명령줄로 줄 수 없고(GitLab 서버 설정), 생성 후에도 러너 편집 화면에서 바꿀 수 있다
#>
param([switch]$Unity, [string]$InstallDir = "C:\GitLab-Runner", [string]$GitLabUrl = "https://lab.ssafy.com", [string]$Shell = "pwsh")
# $Shell: 기본 pwsh(PowerShell 7, MSI 시스템 설치 — 아래에서 없으면 설치). 5.1(powershell) 은 한글 CI 변수로 잡 스크립트가 깨져 실패함(2026-08-27 실측).
#         Microsoft Store 판 pwsh 는 SYSTEM 서비스가 실행 못 하므로 반드시 machine 범위 설치.

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # 진행률 표시를 끄면 Invoke-WebRequest 가 수 배 빨라짐(5.1 에서 특히)
function Step($n, $msg) { Write-Host ("[{0}] {1}  {2}" -f $n, (Get-Date -Format "HH:mm:ss"), $msg) -ForegroundColor Cyan }

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "관리자 PowerShell 에서 실행하세요 (서비스 설치 필요)."
}
if (-not $env:RUNNER_TOKEN) { throw "환경변수 RUNNER_TOKEN 이 비어 있습니다. 팀장에게 러너 등록 토큰(glrt-…)을 받아 `$env:RUNNER_TOKEN 에 넣고 다시 실행." }
if ($env:RUNNER_TOKEN -notlike "glrt-*") { Write-Warning "토큰이 glrt- 로 시작하지 않습니다. 'New project runner' 에서 발급한 인증 토큰이어야 합니다(옛 registration token 은 불가)." }

# 1) 바이너리 (약 100MB)
New-Item -ItemType Directory -Force $InstallDir | Out-Null
$exe = Join-Path $InstallDir "gitlab-runner.exe"
if (-not (Test-Path $exe) -or (Get-Item $exe).Length -lt 50MB) {
    Step 1 "gitlab-runner.exe 다운로드 (약 100MB, 네트워크에 따라 1~5분)..."
    $tmp = "$exe.part"
    Invoke-WebRequest -Uri "https://gitlab-runner-downloads.s3.amazonaws.com/latest/binaries/gitlab-runner-windows-amd64.exe" -OutFile $tmp -TimeoutSec 900
    Move-Item -Force $tmp $exe
    Step 1 ("다운로드 완료 {0:N1} MB" -f ((Get-Item $exe).Length / 1MB))
} else { Step 1 "gitlab-runner.exe 있음 (건너뜀)" }
& $exe --version

# 2) Python (서비스는 SYSTEM 계정이므로 "모든 사용자(machine)" 설치가 필요 — 사용자 전용 설치는 서비스가 못 봄)
Step 2 "Python 확인..."
$pyOk = $false
foreach ($cand in @("python", "py")) {
    $cmd = Get-Command $cand -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source -notlike "*WindowsApps*") {   # Microsoft Store 별칭(stub) 제외
        $v = & $cmd.Source --version 2>&1
        if ("$v" -match "Python 3\.(1[0-9])") { $pyOk = $true; Write-Host "  Python OK: $v ($($cmd.Source))"; break }
    }
}
if (-not $pyOk) {
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        Step 2 "Python 3.12 설치 (winget, 모든 사용자, 1~3분, 출력 없음)..."
        winget install --id Python.Python.3.12 --scope machine --accept-package-agreements --accept-source-agreements --silent --disable-interactivity
        $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")
        Write-Host "  설치 후: $(& python --version 2>&1)"
    } else {
        Write-Warning "winget 이 없습니다. https://www.python.org/downloads/ 에서 3.12 를 'Install for all users' + 'Add to PATH' 로 설치한 뒤 다시 실행하세요."
    }
}

# 2b) pwsh (PowerShell 7) 시스템 전체 설치 — 서비스(SYSTEM)가 실행할 수 있어야 함
if ($Shell -eq "pwsh") {
    $pwshMsi = "C:\Program Files\PowerShell\7\pwsh.exe"
    if (-not (Test-Path $pwshMsi)) {
        Step 2 "PowerShell 7 시스템 전체 설치 — GitHub 릴리스 MSI (winget 은 Store 판이 있으면 건너뜀)"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/PowerShell/PowerShell/releases/latest" -Headers @{ "User-Agent" = "metagame-runner-setup" }
        $asset = $rel.assets | Where-Object { $_.name -like "PowerShell-*-win-x64.msi" } | Select-Object -First 1
        if (-not $asset) { throw "pwsh win-x64 MSI 를 GitHub 릴리스에서 찾지 못했습니다 — https://github.com/PowerShell/PowerShell/releases 에서 수동 설치하세요(S15P21B202-135)" }
        $msi = Join-Path $env:TEMP $asset.name
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $msi -TimeoutSec 900
        $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart ADD_PATH=1 REGISTER_MANIFEST=1 ENABLE_PSREMOTING=0" -Wait -PassThru
        if ($p.ExitCode -notin 0, 3010 -or -not (Test-Path $pwshMsi)) { throw "pwsh MSI 설치 실패(코드 $($p.ExitCode)) — $msi 수동 설치 후 재실행" }
        Remove-Item $msi -ErrorAction SilentlyContinue
    }
    $mp = [Environment]::GetEnvironmentVariable("Path", "Machine")
    if ($mp -notlike "*C:\Program Files\PowerShell\7*") { [Environment]::SetEnvironmentVariable("Path", "$mp;C:\Program Files\PowerShell\7", "Machine") }
    Step 2 "pwsh OK: $(& $pwshMsi -NoProfile -Command '$PSVersionTable.PSVersion.ToString()')"
}

# 3) 등록
$tags = if ($Unity) { "windows,unity-win" } else { "windows" }   # 참고용 — glrt- 토큰 등록에서는 태그를 여기서 못 정함(서버 설정)
$desc = "METAGAME-$env:COMPUTERNAME"
$cfg = Join-Path $InstallDir "config.toml"
if ((Test-Path $cfg) -and (Select-String -Path $cfg -Pattern ([regex]::Escape($desc)) -Quiet)) {
    Step 3 "이미 등록됨 ($desc) — 건너뜀"
} else {
    Step 3 "러너 등록: $desc  (태그·Run untagged 는 GitLab 러너 설정값을 따름 — 필요 태그: $tags)"
    & $exe register --non-interactive --url $GitLabUrl --token $env:RUNNER_TOKEN --executor shell --shell $Shell `
        --description $desc --builds-dir (Join-Path $InstallDir "builds") --config $cfg
    if ($LASTEXITCODE -ne 0) { throw "등록 실패 — 위 gitlab-runner 오류 줄 참고. 'is not valid' = 이 프로젝트의 유효한 glrt- 토큰이 아님(토큰은 생성 직후 한 번만 표시됨 → Settings → CI/CD → Runners → New project runner 로 새로 발급)" }
}

# 4) 서비스
Step 4 "서비스 설치·시작"
$svc = Get-Service gitlab-runner -ErrorAction SilentlyContinue
if (-not $svc) { & $exe install --working-directory $InstallDir --config $cfg }
& $exe start 2>&1 | Out-Host
Start-Sleep -Seconds 3
& $exe status
& $exe verify --config $cfg
Write-Host @"

완료. 다음을 확인하세요:
 - GitLab Settings → CI/CD → Runners 에 '$desc' 가 초록불로 보임. 러너 편집(연필)에서 **Run untagged jobs ✔** 와 태그($tags)를 확인 — 태그 없는 잡(mr-lint 등)을 받으려면 untagged 허용이 필수
 - 팀장: 변수 CI_ENABLE=true 설정 → 이후 MR 파이프라인이 이 러너에서 실행됨
 - Unity 잡을 켤 때: 변수 UNITY_RUNNER=true, UNITY_PATH=C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe
 - 이 PC 는 절전 금지(전원 옵션), 재부팅 시 서비스가 자동 시작됨
 - 러너 PC 는 CI 변수(토큰)를 볼 수 있으므로 팀원 외 접근이 없는 PC 를 쓸 것
"@ -ForegroundColor Green
