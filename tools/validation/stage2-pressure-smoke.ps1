$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$exe=Join-Path $repo 'artifacts/stage2-smoke/Stage2Smoke.exe'
$out=Join-Path $repo 'artifacts/stage2-pressure'
$started=[DateTime]::UtcNow
$owned=@()
try {
    function Start-SmokePeer([string]$role) {
        $log=Join-Path $out ($role+'.log')
        $p=Start-Process -FilePath $exe -ArgumentList @('-batchmode','-nographics','-stage2-pressure-smoke',('-stage2-smoke-'+$role),'-logFile',$log) -WindowStyle Hidden -PassThru
        $script:owned+=$p
        return $p
    }
    $smokeHost=Start-SmokePeer 'host'
    Start-Sleep -Seconds 2
    $smokeClient=Start-SmokePeer 'client'
    $observerStarted=$false
    while(([DateTime]::UtcNow-$started).TotalSeconds -lt 100) {
        $hostLog=Join-Path $out 'host.log'
        if(-not $observerStarted -and (Test-Path $hostLog) -and (Select-String -Path $hostLog -SimpleMatch 'authenticated remote relief PASS' -Quiet)) {
            $smokeObserver=Start-SmokePeer 'observer'
            $observerStarted=$true
        }
        if($observerStarted -and -not ($owned | Where-Object {-not $_.HasExited})) {break}
        if($owned | Where-Object {$_.HasExited -and $_.ExitCode -ne 0}) {throw 'A pressure smoke peer failed'}
        Start-Sleep -Milliseconds 500
    }
    if(-not $observerStarted -or ($owned | Where-Object {-not $_.HasExited})) {throw 'Pressure smoke timed out'}
    $messages=@(Get-Content (Join-Path $out 'host.log'),(Join-Path $out 'client.log'),(Join-Path $out 'observer.log') | Where-Object {$_ -match '\[PressureSmoke\]'})
    if(($messages | Where-Object {$_ -match 'PASS'}).Count -ne 6) {throw ('Missing smoke assertions: '+($messages -join '; '))}
    $report=@{passed=$true;transport='Mirror Telepathy loopback';peers=3;assertions=$messages;utc=[DateTime]::UtcNow.ToString('o')}
    $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'network-smoke.json') -Encoding UTF8
    $messages
} finally {
    foreach($p in $owned) {if(-not $p.HasExited){Stop-Process -Id $p.Id}}
}
