# Synthetic offline fixture, not a recording of the user. Never accesses a microphone.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$taskSynth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$taskSynth.SelectVoice('Microsoft Heami Desktop')
$taskFixtureDir = Join-Path $PSScriptRoot 'fixtures'
New-Item -ItemType Directory -Force -Path $taskFixtureDir | Out-Null
$taskSamples = @(
    @{Name='synthetic-pull-pin.wav'; Text='야 이거 고정못 좀 뽑아줘'},
    @{Name='synthetic-follow.wav'; Text='나 좀 따라와'},
    @{Name='synthetic-negative.wav'; Text='그 못은 뽑지 마'}
)
foreach ($taskSample in $taskSamples) {
    $taskSynth.SetOutputToWaveFile((Join-Path $taskFixtureDir $taskSample.Name))
    $taskSynth.Speak($taskSample.Text)
    $taskSynth.SetOutputToNull()
}
$taskSynth.Dispose()
