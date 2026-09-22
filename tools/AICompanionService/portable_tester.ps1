param(
    [string]$PackRoot = (Join-Path $PSScriptRoot '../../artifacts/ai-pack/p3'),
    [switch]$SmokeTest
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$PackRoot = [IO.Path]::GetFullPath($PackRoot)
$taskExe = Join-Path $PackRoot 'runtime/ntp-ai-worker/ntp-ai-worker.exe'
$taskModels = Join-Path $PackRoot 'models'
if (-not (Test-Path -LiteralPath $taskExe)) { throw "AI pack executable missing: $taskExe" }
$script:aiTest = @{ Process=$null; Client=$null; Health=$null; Request=$null; Ready=$false; Started=$null }

function Start-AiTest {
    foreach ($taskPort in @(18785,11459)) {
        $taskProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$taskPort)
        try { $taskProbe.Start() } finally { $taskProbe.Stop() }
    }
    $taskBytes = New-Object byte[] 32
    $taskRng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $taskRng.GetBytes($taskBytes) } finally { $taskRng.Dispose() }
    $taskToken = ([BitConverter]::ToString($taskBytes)).Replace('-','').ToLowerInvariant()
    $taskParent = [Diagnostics.Process]::GetCurrentProcess()
    $taskBirth = ($taskParent.StartTime.ToUniversalTime() - [datetime]'1970-01-01').TotalSeconds.ToString('R',[Globalization.CultureInfo]::InvariantCulture)
    $taskInfo = New-Object Diagnostics.ProcessStartInfo
    $taskInfo.FileName = $taskExe
    $taskInfo.Arguments = '--model-root "' + $taskModels + '" --parent-pid ' + $PID + ' --parent-started ' + $taskBirth + ' --port 18785 --ollama-port 11459'
    $taskInfo.UseShellExecute = $false
    $taskInfo.CreateNoWindow = $true
    $taskInfo.RedirectStandardInput = $true
    $taskInfo.WorkingDirectory = Split-Path $taskExe
    $aiTest.Process = [Diagnostics.Process]::Start($taskInfo)
    $aiTest.Process.StandardInput.WriteLine($taskToken)
    $aiTest.Process.StandardInput.Close()
    $taskHandler = New-Object Net.Http.HttpClientHandler
    $taskHandler.UseProxy = $false
    $aiTest.Client = [Net.Http.HttpClient]::new($taskHandler)
    $aiTest.Client.Timeout = [timespan]::FromSeconds(40)
    $aiTest.Client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$taskToken)
    $aiTest.Started = Get-Date
}

function Stop-AiTest {
    if ($aiTest.Process -and -not $aiTest.Process.HasExited) {
        try {
            if ($aiTest.Client) {
                $taskStop = $aiTest.Client.PostAsync('http://127.0.0.1:18785/api/shutdown',[Net.Http.StringContent]::new(''))
                $null = $taskStop.Wait(1500)
            }
        } catch { }
        if (-not $aiTest.Process.WaitForExit(3000)) { $aiTest.Process.Kill(); $null = $aiTest.Process.WaitForExit(3000) }
    }
    if ($aiTest.Client) { $aiTest.Client.Dispose() }
    if ($aiTest.Process) { $aiTest.Process.Dispose() }
    $aiTest.Process=$null; $aiTest.Client=$null; $aiTest.Ready=$false
}

function Send-AiText([string]$Text) {
    $taskJson = @{text=$Text} | ConvertTo-Json -Compress
    $taskContent = [Net.Http.StringContent]::new($taskJson,[Text.Encoding]::UTF8,'application/json')
    $aiTest.Request = $aiTest.Client.PostAsync('http://127.0.0.1:18785/api/companion-v4/text',$taskContent)
}

if ($SmokeTest) {
    try {
        Start-AiTest
        $taskDeadline = (Get-Date).AddSeconds(120)
        $taskHealth = $null
        while ((Get-Date) -lt $taskDeadline) {
            if ($aiTest.Process.HasExited) { throw 'Worker exited before ready' }
            try { $taskHealth = $aiTest.Client.GetStringAsync('http://127.0.0.1:18785/api/health').GetAwaiter().GetResult() | ConvertFrom-Json } catch { }
            if ($taskHealth.state -eq 'failed') { throw $taskHealth.error }
            if ($taskHealth.state -eq 'ready') { break }
            Start-Sleep -Milliseconds 300
        }
        if ($taskHealth.state -ne 'ready') { throw 'Readiness timed out' }
        Send-AiText '나 따라와'
        $taskResponse = $aiTest.Request.GetAwaiter().GetResult()
        $null = $taskResponse.EnsureSuccessStatusCode()
        $taskResult = $taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        if ($taskResult.action -ne 'follow') { throw 'Unexpected classification' }
        Write-Output 'Tester smoke PASS: ready -> follow -> owned shutdown'
    } finally { Stop-AiTest }
    exit
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$taskForm = New-Object Windows.Forms.Form
$taskForm.Text = 'Not This Planet | AI 추가팩 테스트'
$taskForm.ClientSize = [Drawing.Size]::new(780,540)
$taskForm.StartPosition = 'CenterScreen'
$taskForm.Font = [Drawing.Font]::new('Malgun Gothic',10)
$taskForm.FormBorderStyle = 'FixedDialog'
$taskForm.MaximizeBox = $false
$taskIntro = New-Object Windows.Forms.Label
$taskIntro.SetBounds(20,15,740,45)
$taskIntro.Text = '추론 실행기만 테스트합니다. 게임 캐릭터는 움직이지 않습니다.' + "`n" + '모델 다운로드·마이크 녹음·문장 파일 저장은 하지 않습니다.'
$taskStart = New-Object Windows.Forms.Button
$taskStart.SetBounds(20,70,150,36); $taskStart.Text = 'AI 실행'
$taskStatus = New-Object Windows.Forms.Label
$taskStatus.SetBounds(185,78,565,30); $taskStatus.Text = '실행 버튼을 누르세요. 첫 준비는 약 20초입니다.'
$taskExamples = New-Object Windows.Forms.ComboBox
$taskExamples.SetBounds(20,125,600,32); $taskExamples.DropDownStyle = 'DropDownList'
@('나 따라와','일로 와봐','이거 못 뽑아줘','주변 못 전부 뽑아줘','몽둥이 주워줘','도구 내려놔','운석 막아줘','못 뽑지 마','오늘 날씨 좋네') | ForEach-Object { $null = $taskExamples.Items.Add($_) }
$taskExamples.SelectedIndex = 0
$taskText = New-Object Windows.Forms.TextBox
$taskText.SetBounds(20,170,600,32); $taskText.Text = '나 따라와'
$taskSend = New-Object Windows.Forms.Button
$taskSend.SetBounds(635,167,125,36); $taskSend.Text = '판단 요청'; $taskSend.Enabled=$false
$taskLog = New-Object Windows.Forms.RichTextBox
$taskLog.SetBounds(20,220,740,250); $taskLog.ReadOnly=$true; $taskLog.BackColor=[Drawing.Color]::White
$taskClose = New-Object Windows.Forms.Button
$taskClose.SetBounds(580,490,180,32); $taskClose.Text = '종료 및 실행기 정리'
$taskForm.Controls.AddRange(@($taskIntro,$taskStart,$taskStatus,$taskExamples,$taskText,$taskSend,$taskLog,$taskClose))
$taskLabels = @{follow='따라오기';wait='대기';cancel='작업 중단';pull_pin='못 하나 뽑기';move_to_pin='못 앞으로 이동';pull_nearby_pins='주변 못 모두 뽑기';pickup_tool='도구 줍기';pickup_bat='몽둥이 줍기';pickup_welder='용접건 줍기';drop_tool='내려놓기';hit_target='대상 때리기';defend_meteors='운석 방어';none='명령 아님 / 미지원';clarify='의도 불명확'}
$taskExamples.Add_SelectedIndexChanged({ $taskText.Text=[string]$taskExamples.SelectedItem })
$taskStart.Add_Click({
    try { Start-AiTest; $taskStart.Enabled=$false; $taskStatus.Text='모델 준비 중… 잠시 기다려주세요.' }
    catch { Stop-AiTest; $taskStatus.Text='실행 실패: 포트 사용 여부와 추가팩 위치를 확인해주세요.'; $taskLog.AppendText($_.Exception.Message + "`n") }
})
$taskSend.Add_Click({
    if (-not $aiTest.Ready -or [string]::IsNullOrWhiteSpace($taskText.Text)) { return }
    $taskLog.AppendText('입력: ' + $taskText.Text + "`n")
    Send-AiText $taskText.Text
    $taskSend.Enabled=$false
})
$taskText.Add_KeyDown({ if ($_.KeyCode -eq 'Enter' -and $taskSend.Enabled) { $taskSend.PerformClick(); $_.SuppressKeyPress=$true } })
$taskClose.Add_Click({ $taskForm.Close() })
$taskTimer = New-Object Windows.Forms.Timer
$taskTimer.Interval=500
$taskTimer.Add_Tick({
    if (-not $aiTest.Process) { return }
    if ($aiTest.Process.HasExited) { $taskStatus.Text='실행기가 종료됐습니다. 창을 닫고 다시 실행해주세요.'; $taskSend.Enabled=$false; $taskTimer.Stop(); return }
    try {
        if ($aiTest.Health -and $aiTest.Health.IsCompleted) {
            try {
                $taskHealth = $aiTest.Health.GetAwaiter().GetResult() | ConvertFrom-Json
                $aiTest.Ready = $taskHealth.state -eq 'ready'
                if ($aiTest.Ready) { $taskStatus.Text='준비 완료 · 문장을 입력하고 판단 요청을 누르세요.' }
                elseif ($taskHealth.state -eq 'failed') { $taskStatus.Text='준비/추론 실패: ' + $taskHealth.error }
            } catch { $aiTest.Ready=$false }
            $aiTest.Health=$null
        }
        if (-not $aiTest.Health) { $aiTest.Health=$aiTest.Client.GetStringAsync('http://127.0.0.1:18785/api/health') }
        if ($aiTest.Request -and $aiTest.Request.IsCompleted) {
            try {
                $taskResponse=$aiTest.Request.GetAwaiter().GetResult()
                $null=$taskResponse.EnsureSuccessStatusCode()
                $taskResult=$taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                $taskLog.AppendText('결과: ' + $taskLabels[$taskResult.action] + ' [' + $taskResult.action + '] · ' + $taskResult.elapsed_ms + " ms`n`n")
                $taskResponse.Dispose()
            } catch { $taskLog.AppendText('요청 실패: ' + $_.Exception.Message + "`n`n") }
            $aiTest.Request=$null
        }
        $taskSend.Enabled=$aiTest.Ready -and -not $aiTest.Request
        if (-not $aiTest.Ready -and ((Get-Date)-$aiTest.Started).TotalSeconds -gt 120) { $taskStatus.Text='준비 지연/실패. 종료 후 추가팩 상태를 확인해주세요.' }
    } catch { $taskStatus.Text='통신 확인 중 문제가 발생했습니다.' }
})
$taskForm.Add_FormClosing({ $taskTimer.Stop(); Stop-AiTest })
$taskTimer.Start()
try { $null=$taskForm.ShowDialog() } finally { $taskTimer.Dispose(); Stop-AiTest; $taskForm.Dispose() }
