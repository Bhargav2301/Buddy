[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$buddySource = Split-Path -Parent $MyInvocation.MyCommand.Path
try {
    $buddyExe = Join-Path $buddySource 'Buddy.exe'
    if (!(Test-Path $buddyExe)) { throw 'Choose Extract All on the complete Windows ZIP first.' }
    $buddyProcess = Start-Process -FilePath $buddyExe -ArgumentList '--install' -Wait -PassThru
    if ($buddyProcess.ExitCode -ne 0) { throw 'Installation failed. Check %LOCALAPPDATA%\Buddy\Logs\startup.log.' }
} catch {
    Write-Host $_.Exception.Message
    Read-Host 'Press Enter to close'
    exit 1
}
