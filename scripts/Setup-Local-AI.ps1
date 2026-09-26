[CmdletBinding()]
param([ValidateSet('qwen3:4b-instruct-2507-q4_K_M','qwen3:1.7b','qwen3:0.6b','qwen3:8b')] [string]$Model = 'qwen3:4b-instruct-2507-q4_K_M')
$ErrorActionPreference = 'Stop'
Write-Host 'This setup installs Ollama if needed and downloads a local AI model. Internet and several GB of disk space are required.'
$buddyOllama = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
if (!(Test-Path $buddyOllama)) {
    $buddyCommand = Get-Command ollama -ErrorAction SilentlyContinue
    if ($buddyCommand) { $buddyOllama = $buddyCommand.Source }
    else {
        if (!(Get-Command winget -ErrorAction SilentlyContinue)) { Start-Process 'https://ollama.com/download/windows'; throw 'Install Ollama from its official page, then run this script again.' }
        winget install --id Ollama.Ollama --exact --source winget --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) { throw 'Ollama installation did not complete. Use the official download page.' }
        if (!(Test-Path $buddyOllama)) { throw 'Ollama is installed in a custom location. Start it from the Start menu and use Buddy PC setup.' }
    }
}
try { Invoke-RestMethod 'http://127.0.0.1:11434/api/tags' -TimeoutSec 3 | Out-Null }
catch {
    Start-Process -FilePath $buddyOllama -ArgumentList 'serve' -WindowStyle Hidden
    $buddyReady = $false
    for ($buddyAttempt = 0; $buddyAttempt -lt 20; $buddyAttempt++) {
        Start-Sleep -Seconds 1
        try { Invoke-RestMethod 'http://127.0.0.1:11434/api/tags' -TimeoutSec 2 | Out-Null; $buddyReady = $true; break } catch { }
    }
    if (!$buddyReady) { throw 'Ollama did not start. Open it from the Windows Start menu and retry.' }
}
& $buddyOllama pull $Model
if ($LASTEXITCODE -ne 0) { throw 'Model download failed. Check your internet connection and free disk space, then retry.' }
Write-Host "Downloaded $Model. Open Buddy > PC setup & models, select the same model, and click Download & use chat model. Existing models are reused."
Read-Host 'Press Enter to close'
