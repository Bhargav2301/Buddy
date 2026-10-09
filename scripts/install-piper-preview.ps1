param(
    [Parameter(Mandatory=$true)][string]$PreviewDirectory,
    [Parameter(Mandatory=$true)][string]$PythonExecutable
)
$ErrorActionPreference = 'Stop'
$buddyRepo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$buddyPreview = [IO.Path]::GetFullPath($PreviewDirectory)
if (!(Test-Path -LiteralPath (Join-Path $buddyPreview '.buddy-preview'))) { throw 'This installer is only for a separately marked Buddy preview.' }
$voiceRoot = Join-Path $buddyPreview 'local-voice'
if (Test-Path -LiteralPath $voiceRoot) { throw 'Existing voice files are preserved. Choose a new preview.' }
$manifest = Get-Content -LiteralPath (Join-Path $buddyRepo 'docs\voice\download-manifest.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $voiceRoot | Out-Null
& $PythonExecutable -m venv (Join-Path $voiceRoot 'runtime')
if ($LASTEXITCODE -ne 0) { throw 'Could not create isolated Python environment.' }
$wheels = Join-Path $voiceRoot 'wheels'
New-Item -ItemType Directory -Path $wheels | Out-Null
foreach ($package in $manifest.packages) {
    $uri = [Uri]$package.url
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'files.pythonhosted.org' -or [IO.Path]::GetFileName($package.filename) -ne $package.filename) { throw 'Unexpected package source.' }
    $file = Join-Path $wheels $package.filename
    Invoke-WebRequest -Uri $uri -OutFile $file
    if ((Get-FileHash -LiteralPath $file).Hash -ne $package.sha256) { throw ('Checksum failed: ' + $package.name) }
}
$lock = Join-Path $voiceRoot 'requirements.lock'
Copy-Item -LiteralPath (Join-Path $buddyRepo 'docs\voice\requirements.lock') -Destination $lock
& (Join-Path $voiceRoot 'runtime\Scripts\python.exe') -m pip install --no-index --no-deps --require-hashes --disable-pip-version-check --find-links $wheels -r $lock
if ($LASTEXITCODE -ne 0) { throw 'Hash-locked installation failed.' }
$models = Join-Path $voiceRoot 'models'
New-Item -ItemType Directory -Path $models | Out-Null
foreach ($model in $manifest.modelFiles) {
    $file = Join-Path $models ([IO.Path]::GetFileName($model.path))
    Invoke-WebRequest -Uri ('https://huggingface.co/rhasspy/piper-voices/resolve/' + $manifest.modelRevision + '/' + $model.path) -OutFile $file
    if ((Get-Item -LiteralPath $file).Length -ne $model.size) { throw 'Model file size mismatch.' }
    if ($model.lfs -and (Get-FileHash -LiteralPath $file).Hash -ne $model.lfs.oid) { throw 'Model checksum failed.' }
}
if ((Get-FileHash -LiteralPath (Join-Path $models 'en_GB-vctk-medium.onnx.json')).Hash -ne '7f85e6391ed0f7f46e4abd19345929a16be931a0c9945086f96692dce2087fa8') { throw 'Model configuration checksum failed.' }
Copy-Item -LiteralPath (Join-Path $buddyRepo 'docs\voice\download-manifest.json') -Destination $voiceRoot
Copy-Item -LiteralPath (Join-Path $buddyRepo 'docs\voice\README.md') -Destination (Join-Path $voiceRoot 'VOICE-NOTICES.md')
$desktopFile = Join-Path $buddyPreview 'preview-data\desktop.json'
if (!(Test-Path -LiteralPath $desktopFile)) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $desktopFile) -Force | Out-Null
    @{VoiceEngine='piper';NeuralSpeakerId=60;VoiceRate=-1} | ConvertTo-Json | Set-Content -LiteralPath $desktopFile
}
Write-Output 'Approved local voice files installed. No global Python packages or Windows settings changed.'
