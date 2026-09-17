param([ValidateSet('pressure','yard')][string]$Mode='pressure',[string]$OutputFolder='artifacts/stage2-pressure')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$exe=Join-Path $repo 'artifacts/stage2-smoke/Stage2Smoke.exe'
$out=Join-Path $repo $OutputFolder
New-Item -ItemType Directory -Force -Path $out | Out-Null
$lateTrigger=if($Mode -eq 'pressure'){'authenticated remote relief PASS'}else{'remote pilot motion PASS'}
$marker=if($Mode -eq 'pressure'){'\[PressureSmoke\]'}else{'\[Stage2Smoke\]'}
$expected=if($Mode -eq 'pressure'){8}else{5}
$started=[DateTime]::UtcNow
$owned=@()
try {
    function Start-SmokePeer([string]$role) {
        $log=Join-Path $out ($role+'.log')
        $launchArgs=@('-batchmode','-nographics',('-stage2-smoke-'+$role),'-logFile',$log)
        if($Mode -eq 'pressure'){$launchArgs+='-stage2-pressure-smoke'}
        $p=Start-Process -FilePath $exe -ArgumentList $launchArgs -WindowStyle Hidden -PassThru
        $script:owned+=$p
        return $p
    }
    $smokeHost=Start-SmokePeer 'host'
    Start-Sleep -Seconds 2
    $smokeClient=Start-SmokePeer 'client'
    $observerStarted=$false
    while(([DateTime]::UtcNow-$started).TotalSeconds -lt 100) {
        $hostLog=Join-Path $out 'host.log'
        if(-not $observerStarted -and (Test-Path $hostLog) -and (Select-String -Path $hostLog -SimpleMatch $lateTrigger -Quiet)) {
            $smokeObserver=Start-SmokePeer 'observer'
            $observerStarted=$true
        }
        if($observerStarted -and -not ($owned | Where-Object {-not $_.HasExited})) {break}
        if($owned | Where-Object {$_.HasExited -and $_.ExitCode -ne 0}) {throw 'A pressure smoke peer failed'}
        Start-Sleep -Milliseconds 500
    }
    if(-not $observerStarted -or ($owned | Where-Object {-not $_.HasExited})) {throw 'Pressure smoke timed out'}
    $messages=@(Get-Content (Join-Path $out 'host.log'),(Join-Path $out 'client.log'),(Join-Path $out 'observer.log') | Where-Object {$_ -match $marker})
    if(($messages | Where-Object {$_ -match 'PASS'}).Count -ne $expected) {throw ('Missing smoke assertions: '+($messages -join '; '))}
    $report=@{passed=$true;mode=$Mode;transport='Mirror Telepathy loopback';peers=3;assertions=$messages;utc=[DateTime]::UtcNow.ToString('o')}
    $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'network-smoke.json') -Encoding UTF8
    $messages
} finally {
    foreach($p in $owned) {if(-not $p.HasExited){Stop-Process -Id $p.Id}}
}
