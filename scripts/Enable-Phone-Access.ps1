[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$buddyExecutable = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'Buddy.exe'
if (!(Test-Path $buddyExecutable)) { throw 'Run this script from the installed Buddy folder.' }
$buddyIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$buddyPrincipal = New-Object Security.Principal.WindowsPrincipal($buddyIdentity)
if (!$buddyPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList @('-NoProfile', '-File', ('"' + $MyInvocation.MyCommand.Path + '"'))
    exit
}
# One application, one TCP port, Private networks, local subnet only. No router changes.
$buddyExisting = Get-NetFirewallRule -DisplayName 'Buddy paired phone access' -ErrorAction SilentlyContinue
if ($buddyExisting) { $buddyExisting | Remove-NetFirewallRule }
New-NetFirewallRule -DisplayName 'Buddy paired phone access' -Direction Inbound -Action Allow -Program $buddyExecutable -Protocol TCP -LocalPort 47831 -Profile Private -RemoteAddress LocalSubnet | Out-Null
Write-Host 'Phone access enabled on Private local networks. Keep your PC and phone on the same Wi-Fi.'
Read-Host 'Press Enter to close'
