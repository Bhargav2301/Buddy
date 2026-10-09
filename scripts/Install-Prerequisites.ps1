$ErrorActionPreference = 'Stop'
# Official Microsoft v14 x64 permalink. This script never accepts installer terms.
$buddyRuntimeUrl = 'https://aka.ms/vc14/vc_redist.x64.exe'
$buddyRuntimeFolder = Join-Path ([IO.Path]::GetTempPath()) ('Buddy.Prerequisite.' + [Guid]::NewGuid().ToString('N'))
$buddyRuntimeFile = Join-Path $buddyRuntimeFolder 'vc_redist.x64.exe'
New-Item -ItemType Directory -Path $buddyRuntimeFolder | Out-Null
Invoke-WebRequest -Uri $buddyRuntimeUrl -OutFile $buddyRuntimeFile
$buddySignature = Get-AuthenticodeSignature -LiteralPath $buddyRuntimeFile
if ($buddySignature.Status -ne 'Valid' -or $buddySignature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
    throw 'The downloaded Microsoft runtime signature could not be verified. The installer was not opened.'
}
# Visible installer is intentional: the user must review the Microsoft UI.
$buddyRuntimeProcess = Start-Process -FilePath $buddyRuntimeFile -WindowStyle Normal -PassThru -Wait
if ($buddyRuntimeProcess.ExitCode -notin @(0, 3010)) { throw "Microsoft runtime installation returned $($buddyRuntimeProcess.ExitCode). Retry after resolving the installer message." }
Write-Output 'Runtime setup finished. If Windows requests a restart, restart before installing Buddy.'
