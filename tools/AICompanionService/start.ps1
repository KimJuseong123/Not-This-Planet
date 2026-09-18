param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$taskPython = Join-Path $PSScriptRoot '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $taskPython)) { throw 'Run setup.ps1 first.' }

function Test-CurrentService($taskHealth) {
    if (-not $taskHealth -or $taskHealth.service -ne 'ai-companion-lab' -or $taskHealth.protocol -ne 'companion-v4') { return $false }
    if (-not $taskHealth.service_root -or [IO.Path]::GetFullPath($taskHealth.service_root) -ne [IO.Path]::GetFullPath($PSScriptRoot)) { return $false }
    foreach ($taskSource in @('app.py', 'companion_v3.py', 'companion_v4.py')) {
        $taskHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $taskSource) -Algorithm SHA256).Hash
        if ($taskHealth.source_hashes.$taskSource -ne $taskHash) { return $false }
    }
    foreach ($taskAction in @('follow','wait','cancel','pull_pin','move_to_pin','pull_nearby_pins','pickup_tool','pickup_bat','pickup_welder','drop_tool','hit_target','defend_meteors')) {
        if ($taskHealth.actions -notcontains $taskAction) { return $false }
    }
    return $true
}

$taskHealth = $null
try { $taskHealth = Invoke-RestMethod 'http://127.0.0.1:8765/api/health' -TimeoutSec 2 } catch { }
if (Test-CurrentService $taskHealth) {
    Write-Host 'Current v4 tool-skill AI service is already running.'
    if (-not $NoBrowser) { Start-Process 'http://127.0.0.1:8765' }
    return
}
foreach ($taskPort in @(8765, 11439)) {
    $taskListeners = @(Get-NetTCPConnection -State Listen -LocalPort $taskPort -ErrorAction SilentlyContinue)
    if ($taskListeners.Count -gt 0) {
        $taskPids = ($taskListeners.OwningProcess | Sort-Object -Unique) -join ', '
        throw "Port $taskPort is occupied (PID $taskPids). An older/different service or leftover model process is running. Stop the intended lab via its shutdown button before retrying. Nothing was stopped automatically."
    }
}
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'logs') | Out-Null
$taskStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$taskErrorLog = Join-Path $PSScriptRoot "logs/app-$taskStamp-error.log"
$taskProcess = Start-Process -FilePath $taskPython -ArgumentList @('app.py') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $PSScriptRoot "logs/app-$taskStamp.log") -RedirectStandardError $taskErrorLog -PassThru
for ($taskAttempt = 0; $taskAttempt -lt 60; $taskAttempt++) {
    $taskProcess.Refresh()
    if ($taskProcess.HasExited) {
        Get-Content -LiteralPath $taskErrorLog -Encoding UTF8 -Tail 16 | Write-Host
        throw "AI server failed to start. Error log: $taskErrorLog"
    }
    try { $taskHealth = Invoke-RestMethod 'http://127.0.0.1:8765/api/health' -TimeoutSec 1 } catch { $taskHealth = $null }
    if (Test-CurrentService $taskHealth) {
        Write-Host "AI service ready: v4 tool skills, PID $($taskHealth.pid), http://127.0.0.1:8765"
        if (-not $NoBrowser) { Start-Process 'http://127.0.0.1:8765' }
        return
    }
    Start-Sleep -Milliseconds 250
}
throw "Server readiness not confirmed. Check $taskErrorLog. No other processes were stopped."
