$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
Get-Command uv -ErrorAction Stop | Out-Null
Get-Command ollama -ErrorAction Stop | Out-Null
if (-not (Test-Path '.venv/Scripts/python.exe')) {
    uv venv --python 3.10 .venv
    if ($LASTEXITCODE -ne 0) { throw 'Python environment creation failed.' }
}
uv pip install --python .venv/Scripts/python.exe -r requirements.lock
if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
Write-Host 'Dependencies ready. Start start.ps1, then run .venv/Scripts/python.exe prepare_models.py to download models.'
Write-Host 'This script does not start inference or download models automatically.'
