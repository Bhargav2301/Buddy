$ErrorActionPreference = 'Stop'
$buddyRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$buddyOutput = Join-Path $buddyRoot 'dist\Buddy-Windows'
dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Service tests failed.' }
dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Desktop.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Desktop logic tests failed.' }
dotnet publish (Join-Path $buddyRoot 'apps\windows\Buddy.Windows\Buddy.Windows.csproj') -c Release -r win-x64 --self-contained true -o $buddyOutput
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
dotnet run --project (Join-Path $buddyRoot 'tests\Buddy.Windows.PackageChecks') -c Release -- $buddyOutput
if ($LASTEXITCODE -ne 0) { throw 'Windows package validation failed.' }
Copy-Item (Join-Path $buddyRoot 'scripts\Install-Buddy.cmd'), (Join-Path $buddyRoot 'scripts\Run-Buddy.cmd'), (Join-Path $buddyRoot 'scripts\Install-Buddy.ps1'), (Join-Path $buddyRoot 'scripts\Enable-Phone-Access.ps1'), (Join-Path $buddyRoot 'scripts\Setup-Local-AI.ps1') $buddyOutput
Set-Content -Path (Join-Path $buddyOutput 'Windows-Repair.txt') -Value 'Buddy 0.1.0 - Windows repair 1 - 2026-09-26'
Set-Content -Path (Join-Path $buddyOutput 'Cursor-Companion.txt') -Value 'Buddy 0.1.0 - Cursor companion 1 - Ctrl+Space for chat; Ctrl+Shift+Space for voice'
Copy-Item (Join-Path $buddyRoot 'docs\Buddy-Setup-Guide.md') $buddyOutput
Copy-Item (Join-Path $buddyRoot 'docs\Windows-Quick-Start.txt') (Join-Path $buddyOutput 'START-HERE.txt')
Copy-Item (Join-Path $buddyRoot 'docs\licenses') $buddyOutput -Recurse -Force
Compress-Archive -Path (Join-Path $buddyOutput '*') -DestinationPath (Join-Path $buddyRoot 'dist\Buddy-Windows-v0.1.0.zip') -Force
