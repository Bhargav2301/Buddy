$ErrorActionPreference = 'Stop'
$buddyRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$buddyVersion = (Get-Content (Join-Path $buddyRoot 'VERSION') -Raw).Trim()
if ($buddyVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid VERSION file.' }
$buddyOutput = [IO.Path]::GetFullPath((Join-Path $buddyRoot 'dist\Buddy-Windows'))
$buddyExpectedOutput = [IO.Path]::GetFullPath((Join-Path $buddyRoot 'dist')) + [IO.Path]::DirectorySeparatorChar + 'Buddy-Windows'
if ($buddyOutput -ne $buddyExpectedOutput -or -not $buddyOutput.StartsWith([IO.Path]::GetFullPath($buddyRoot) + [IO.Path]::DirectorySeparatorChar)) { throw 'Unsafe publish directory.' }
if (Test-Path -LiteralPath $buddyOutput) { Remove-Item -LiteralPath $buddyOutput -Recurse -Force }
dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Tests') -c Release -r win-x64 --self-contained true
if ($LASTEXITCODE -ne 0) { throw 'Service tests failed.' }
dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Desktop.Tests') -c Release -r win-x64 --self-contained true
if ($LASTEXITCODE -ne 0) { throw 'Desktop logic tests failed.' }
dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Windows.IntegrationTests') -c Release -- --settings-navigation
if ($LASTEXITCODE -ne 0) { throw 'Settings window navigation tests failed.' }
dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Windows.IntegrationTests') -c Release -- --ocr
if ($LASTEXITCODE -ne 0) { throw 'Native OCR dependency or fixture tests failed.' }
dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Assistant.Tests') -c Release -r win-x64 --self-contained true
if ($LASTEXITCODE -ne 0) { throw 'Assistant safety and tool tests failed.' }
foreach($buddySuite in @('Preview','Teaching','Optional','Qa','Execution')) {
    dotnet run --project (Join-Path $buddyRoot ('tests\Buddy.'+$buddySuite+'.Tests')) -c Release -r win-x64 --self-contained true
    if($LASTEXITCODE -ne 0){throw ('Additional policy/service tests failed: '+$buddySuite)}
}
dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Mvp.Tests') -c Release -r win-x64 --self-contained true
if ($LASTEXITCODE -ne 0) { throw 'MVP workflow tests failed.' }
dotnet publish (Join-Path $buddyRoot 'apps\windows\Buddy.Windows\Buddy.Windows.csproj') -c Release -r win-x64 --self-contained true -o $buddyOutput
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Windows.PackageChecks') -c Release -r win-x64 --self-contained true -- $buddyOutput
if ($LASTEXITCODE -ne 0) { throw 'Windows package validation failed.' }
. (Join-Path $buddyRoot 'scripts\Invoke-PackageProbe.ps1')
Invoke-BuddyPackageProbe -Directory $buddyOutput
Copy-Item (Join-Path $buddyRoot 'scripts\Install-Buddy.cmd'), (Join-Path $buddyRoot 'scripts\Rollback-Buddy.cmd'), (Join-Path $buddyRoot 'scripts\Run-Buddy.cmd'), (Join-Path $buddyRoot 'scripts\Open-Buddy-Settings.cmd'), (Join-Path $buddyRoot 'scripts\Install-Buddy.ps1'), (Join-Path $buddyRoot 'scripts\Enable-Phone-Access.ps1'), (Join-Path $buddyRoot 'scripts\Setup-Local-AI.ps1') $buddyOutput
Copy-Item (Join-Path $buddyRoot 'scripts\Install-Prerequisites.cmd'), (Join-Path $buddyRoot 'scripts\Install-Prerequisites.ps1') $buddyOutput
Set-Content -Path (Join-Path $buddyOutput 'Windows-Repair.txt') -Value "Buddy $buddyVersion - Windows Desktop runtime packaging repair included"
Set-Content -Path (Join-Path $buddyOutput 'Cursor-Companion.txt') -Value "Buddy $buddyVersion - Configure separate chat/voice shortcuts in Settings; existing bindings are preserved"
$buddyCommit = 'source-archive'
if (Test-Path (Join-Path $buddyRoot '.git')) {
    $buddyCommit = git -C $buddyRoot rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify the source commit.' }
}
@{ version = $buddyVersion; commit = $buddyCommit; target = 'win-x64'; builtAtUtc = [DateTime]::UtcNow.ToString('O') } |
    ConvertTo-Json | Set-Content (Join-Path $buddyOutput 'Build-Info.json') -Encoding utf8
Copy-Item (Join-Path $buddyRoot 'CHANGELOG.md'), (Join-Path $buddyRoot 'VERSION') $buddyOutput
Copy-Item (Join-Path $buddyRoot 'docs\Buddy-Setup-Guide.md') $buddyOutput
Copy-Item (Join-Path $buddyRoot 'docs\Windows-Assistant-Preview.md'), (Join-Path $buddyRoot 'docs\Windows-0.3.9.md') $buddyOutput
Copy-Item (Join-Path $buddyRoot 'docs\Windows-MVP-Preview.md'), (Join-Path $buddyRoot 'docs\MVP-Implementation-Status.md') $buddyOutput
Copy-Item (Join-Path $buddyRoot 'docs\Windows-Quick-Start.txt') (Join-Path $buddyOutput 'START-HERE.txt')
Copy-Item (Join-Path $buddyRoot 'docs\licenses') $buddyOutput -Recurse -Force
Compress-Archive -Path (Join-Path $buddyOutput '*') -DestinationPath (Join-Path $buddyRoot "dist\Buddy-Windows-v$buddyVersion.zip") -Force
