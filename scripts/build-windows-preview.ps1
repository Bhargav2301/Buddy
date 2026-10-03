$ErrorActionPreference = 'Stop'
$buddyRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$buddyCommit = git -C $buddyRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'A source commit is required for a reviewable preview.' }
$buddyChanges = git -C $buddyRoot status --porcelain
if ($buddyChanges) { throw 'Commit the reviewed changes before creating a traceable preview.' }
$buddyVersion = (Get-Content (Join-Path $buddyRoot 'VERSION') -Raw).Trim()
$buddyName = 'Buddy-Windows-MVP-Preview-' + $buddyCommit.Substring(0, 8) + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$buddyOutput = [IO.Path]::GetFullPath((Join-Path $buddyRoot ('dist\' + $buddyName)))
$buddyDist = [IO.Path]::GetFullPath((Join-Path $buddyRoot 'dist'))
if ([IO.Path]::GetDirectoryName($buddyOutput) -ne $buddyDist -or (Test-Path -LiteralPath $buddyOutput)) { throw 'Preview must use a fresh folder inside dist.' }
New-Item -ItemType Directory -Path $buddyOutput | Out-Null
$buddyValidation = Join-Path $buddyOutput 'Validation'
New-Item -ItemType Directory -Path $buddyValidation | Out-Null
foreach ($buddyTest in @('Buddy.Tests', 'Buddy.Desktop.Tests', 'Buddy.Assistant.Tests', 'Buddy.Mvp.Tests')) {
    dotnet run --project (Join-Path $buddyRoot ('tests\' + $buddyTest)) -c Release -r win-x64 --self-contained true |
        Tee-Object -FilePath (Join-Path $buddyValidation ($buddyTest + '.txt'))
    if ($LASTEXITCODE -ne 0) { throw "$buddyTest failed." }
}
foreach ($buddyMode in @('settings-navigation', 'ocr')) {
    dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Windows.IntegrationTests') -c Release -- "--$buddyMode" |
        Tee-Object -FilePath (Join-Path $buddyValidation ($buddyMode + '.txt'))
    if ($LASTEXITCODE -ne 0) { throw "Native $buddyMode fixture failed." }
}
dotnet publish (Join-Path $buddyRoot 'apps\windows\Buddy.Windows\Buddy.Windows.csproj') -c Release -r win-x64 --self-contained true -o $buddyOutput
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Windows.PackageChecks') -c Release -r win-x64 --self-contained true -- $buddyOutput |
    Tee-Object -FilePath (Join-Path $buddyValidation 'Package.txt')
if ($LASTEXITCODE -ne 0) { throw 'Package validation failed.' }
. (Join-Path $buddyRoot 'scripts\Invoke-PackageProbe.ps1')
Invoke-BuddyPackageProbe -Directory $buddyOutput -LogDirectory $buddyValidation
foreach ($buddyScript in @('Install-Buddy.cmd', 'Rollback-Buddy.cmd', 'Run-Buddy.cmd', 'Open-Buddy-Settings.cmd', 'Install-Buddy.ps1', 'Enable-Phone-Access.ps1', 'Setup-Local-AI.ps1', 'Install-Prerequisites.cmd', 'Install-Prerequisites.ps1')) {
    Copy-Item -LiteralPath (Join-Path $buddyRoot ('scripts\' + $buddyScript)) -Destination $buddyOutput
}
foreach ($buddyDoc in @('MVP-Implementation-Status.md', 'Windows-MVP-Preview.md')) {
    Copy-Item -LiteralPath (Join-Path $buddyRoot ('docs\' + $buddyDoc)) -Destination $buddyOutput
}
Copy-Item -LiteralPath (Join-Path $buddyRoot 'docs\licenses') -Destination $buddyOutput -Recurse
Copy-Item -LiteralPath (Join-Path $buddyRoot 'docs\acceptance') -Destination $buddyValidation -Recurse
@{ version = $buddyVersion; commit = $buddyCommit; channel = 'MVP preview'; completedMvp = $false; target = 'win-x64'; builtAtUtc = [DateTime]::UtcNow.ToString('O'); nativeAcceptance = 'Pending: see MVP-Implementation-Status.md' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $buddyOutput 'Build-Info.json') -Encoding utf8
'Testing preview, not completed version 0.5.0. Read Windows-MVP-Preview.md before installing. The previous released ZIP is unchanged.' |
    Set-Content -LiteralPath (Join-Path $buddyOutput 'START-HERE.txt') -Encoding utf8
$buddyHashRows = Get-ChildItem -LiteralPath $buddyOutput -File -Recurse | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $_.FullName.Substring($buddyOutput.Length + 1)
}
$buddyHashRows | Set-Content -LiteralPath (Join-Path $buddyOutput 'SHA256SUMS.txt') -Encoding ascii
$buddyZip = Join-Path $buddyDist ($buddyName + '.zip')
Compress-Archive -Path (Join-Path $buddyOutput '*') -DestinationPath $buddyZip
(Get-FileHash -LiteralPath $buddyZip -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($buddyZip) |
    Set-Content -LiteralPath ($buddyZip + '.sha256') -Encoding ascii
Write-Output "Preview package: $buddyZip"
