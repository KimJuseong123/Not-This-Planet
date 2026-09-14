$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$taskPython = Join-Path $PSScriptRoot '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $taskPython)) { throw 'Run setup.ps1 first.' }
$taskStatus = $null
try { $taskStatus = Invoke-RestMethod 'http://127.0.0.1:8765/api/status' -TimeoutSec 2 } catch { }
if ($taskStatus) {
    if ($taskStatus.service_root -and [IO.Path]::GetFullPath($taskStatus.service_root) -eq [IO.Path]::GetFullPath($PSScriptRoot)) {
        Write-Host 'This service is already running.'
        return
    }
    throw 'Port 8765 belongs to another or older AI service. Stop it intentionally before starting this copy. Nothing was stopped.'
}
foreach ($taskPort in @(8765, 11439)) {
    if (Get-NetTCPConnection -State Listen -LocalPort $taskPort -ErrorAction SilentlyContinue) {
        throw "Port $taskPort is already occupied. Nothing was stopped."
    }
}
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'logs') | Out-Null
$taskStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$taskProcess = Start-Process -FilePath $taskPython -ArgumentList @('app.py') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $PSScriptRoot "logs/app-$taskStamp.log") -RedirectStandardError (Join-Path $PSScriptRoot "logs/app-$taskStamp-error.log") -PassThru
for ($taskAttempt = 0; $taskAttempt -lt 60; $taskAttempt++) {
    $taskProcess.Refresh()
    if ($taskProcess.HasExited) { throw 'AI server exited. Inspect logs.' }
    try { $taskStatus = Invoke-RestMethod 'http://127.0.0.1:8765/api/status' -TimeoutSec 1 } catch { $taskStatus = $null }
    if ($taskStatus -and $taskStatus.service_root -and [IO.Path]::GetFullPath($taskStatus.service_root) -eq [IO.Path]::GetFullPath($PSScriptRoot)) {
        Write-Host 'AI service ready: http://127.0.0.1:8765 (Unity v2 endpoint available).'
        return
    }
    Start-Sleep -Milliseconds 250
}
throw 'Server readiness not confirmed. Inspect logs; this script did not stop any other process.'
