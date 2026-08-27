<#
.SYNOPSIS
  러너 셸을 Windows PowerShell 5.1(powershell) → PowerShell 7(pwsh) 로 바꾼다. 필요하면 pwsh 를 시스템 전체(MSI)로 설치한다.
.DESCRIPTION
  왜: 5.1 은 러너가 만든 잡 스크립트를 cp949 로 읽어, CI 변수에 한글(MR 제목·본문·커밋 메시지)이 있으면 바이트가 깨져
      "문자열에 " 종결자가 없습니다" 파서 오류로 모든 잡이 2초 만에 실패한다(2026-08-27 실측). pwsh 는 UTF-8 이 기본.
  Microsoft Store 판 pwsh 는 SYSTEM 서비스가 실행할 수 없으므로 winget 으로 machine 범위(MSI) 설치를 쓴다.
  실행(관리자 PowerShell): .\scripts\runner\runner-use-pwsh.ps1
#>
param([string]$InstallDir = "C:\GitLab-Runner")
$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "관리자 PowerShell 에서 실행하세요." }
$cfg = Join-Path $InstallDir "config.toml"; $exe = Join-Path $InstallDir "gitlab-runner.exe"
if (-not (Test-Path $cfg)) { throw "$cfg 없음" }

$pwshMsi = "C:\Program Files\PowerShell\7\pwsh.exe"
if (-not (Test-Path $pwshMsi)) {
    Write-Host "[1] PowerShell 7 시스템 전체 설치 — GitHub 릴리스 MSI 직접 설치 (winget 은 Store 판이 있으면 건너뛰는 경우가 있음)" -ForegroundColor Cyan
    $ProgressPreference = "SilentlyContinue"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/PowerShell/PowerShell/releases/latest" -Headers @{ "User-Agent" = "metagame-runner-setup" }
    $asset = $rel.assets | Where-Object { $_.name -like "PowerShell-*-win-x64.msi" } | Select-Object -First 1
    if (-not $asset) { throw "릴리스에서 win-x64 MSI 를 못 찾음 — https://aka.ms/powershell 에서 수동 설치" }
    $msi = Join-Path $env:TEMP $asset.name
    Write-Host "    다운로드 $($asset.name) ($([int]($asset.size/1MB)) MB)..."
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $msi -TimeoutSec 900
    Write-Host "    msiexec 설치(조용히, 1~2분)..."
    $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart ADD_PATH=1 REGISTER_MANIFEST=1 ENABLE_PSREMOTING=0" -Wait -PassThru
    if ($p.ExitCode -notin 0, 3010) { throw "msiexec 종료 코드 $($p.ExitCode) — $msi 를 더블클릭해 수동 설치 후 재실행" }
    if (-not (Test-Path $pwshMsi)) { throw "설치 후에도 $pwshMsi 가 없음 — $msi 를 수동 설치 후 재실행" }
    Remove-Item $msi -ErrorAction SilentlyContinue
} else { Write-Host "[1] pwsh 있음: $pwshMsi" }
& $pwshMsi -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'

# 시스템 PATH 에 pwsh 폴더 보장 (서비스가 pwsh 를 찾도록)
$mp = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($mp -notlike "*C:\Program Files\PowerShell\7*") {
    [Environment]::SetEnvironmentVariable("Path", "$mp;C:\Program Files\PowerShell\7", "Machine"); Write-Host "[2] 시스템 PATH 에 pwsh 추가"
} else { Write-Host "[2] 시스템 PATH OK" }

Write-Host "[3] config.toml shell → pwsh" -ForegroundColor Cyan
Copy-Item $cfg "$cfg.bak-shell" -Force
$txt = [IO.File]::ReadAllText($cfg)
$txt = [regex]::Replace($txt, '(?m)^(\s*shell\s*=\s*)"powershell"', '${1}"pwsh"')
if ($txt -notmatch '(?m)^\s*shell\s*=\s*"pwsh"') { throw "config.toml 에서 shell 항목을 못 찾음 — 직접 shell = `"pwsh`" 로 수정" }
[IO.File]::WriteAllText($cfg, $txt)
& $exe restart
Start-Sleep -Seconds 3
& $exe verify --config $cfg
Write-Host "완료 — 이제 MR 파이프라인을 재실행하면 잡이 pwsh 로 돕니다." -ForegroundColor Green
