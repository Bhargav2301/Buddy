param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$buddyRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$buddyOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $buddyOutput) { throw 'Choose a new preview directory; existing files are never overwritten.' }
New-Item -ItemType Directory -Path $buddyOutput | Out-Null
dotnet publish (Join-Path $buddyRoot 'apps\windows\Buddy.Windows\Buddy.Windows.csproj') -c Release -r win-x64 --self-contained true -o $buddyOutput
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
'Isolated local review; installation is disabled.' | Set-Content -LiteralPath (Join-Path $buddyOutput '.buddy-preview') -Encoding utf8
@'
@echo off
start "Buddy local preview" "%~dp0Buddy.exe" --preview --home
'@ | Set-Content -LiteralPath (Join-Path $buddyOutput 'Run-Preview.cmd') -Encoding ascii
Copy-Item -LiteralPath (Join-Path $buddyRoot 'docs\JARVIS-Local-Preview.md') -Destination $buddyOutput
Copy-Item -LiteralPath (Join-Path $buddyRoot 'docs\licenses') -Destination $buddyOutput -Recurse
$buddyCommit = git -c "safe.directory=$($buddyRoot.Replace('\','/'))" -C $buddyRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not record source baseline.' }
$buddyFiles = git -c "safe.directory=$($buddyRoot.Replace('\','/'))" -C $buddyRoot ls-files --cached --others --exclude-standard
$buddySourceHashes = foreach ($file in $buddyFiles) {
    $full = Join-Path $buddyRoot $file
    if (Test-Path -LiteralPath $full -PathType Leaf) { [ordered]@{ path=$file; sha256=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash } }
}
$buddySourceHashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $buddyOutput 'Source-SHA256.json') -Encoding utf8
@{ version=(Get-Content (Join-Path $buddyRoot 'VERSION') -Raw).Trim(); baselineCommit=$buddyCommit; localUncommittedChanges=$true; channel='isolated local JARVIS review'; installedReplacement=$false; cloudProvidersEnabled=$false; builtAtUtc=[DateTime]::UtcNow.ToString('O'); target='win-x64'; requirements='PRD v1.0, TRD v1.1 addendum, UIUX v1.0'; physicalAudioAcceptance='pending' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $buddyOutput 'Build-Info.json') -Encoding utf8
$buddyValidation = Join-Path $buddyOutput 'Validation'; New-Item -ItemType Directory $buddyValidation | Out-Null
. (Join-Path $buddyRoot 'scripts\Invoke-PackageProbe.ps1')
Invoke-BuddyPackageProbe -Directory $buddyOutput -LogDirectory $buddyValidation
$buddyHashRows = Get-ChildItem -LiteralPath $buddyOutput -Recurse -File | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $_.FullName.Substring($buddyOutput.Length + 1)
}
$buddyHashRows | Set-Content -LiteralPath (Join-Path $buddyOutput 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Isolated preview: $buddyOutput"
