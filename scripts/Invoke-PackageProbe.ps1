function Invoke-BuddyPackageProbe {
    param([Parameter(Mandatory = $true)][string]$Directory, [string]$LogDirectory)
    $buddyProbeInfo = New-Object System.Diagnostics.ProcessStartInfo
    $buddyProbeInfo.FileName = Join-Path $Directory 'Buddy.exe'
    $buddyProbeInfo.Arguments = '--check-package'
    $buddyProbeInfo.UseShellExecute = $false
    $buddyProbeInfo.CreateNoWindow = $true
    $buddyProbeInfo.RedirectStandardOutput = $true
    $buddyProbeInfo.RedirectStandardError = $true
    # Own the process handle from creation. Start-Process -PassThru followed by
    # WaitForExit can lose the exit code in Windows PowerShell after a fast exit.
    $buddyProbeProcess = [System.Diagnostics.Process]::Start($buddyProbeInfo)
    try {
        $buddyProbeOutput = $buddyProbeProcess.StandardOutput.ReadToEndAsync()
        $buddyProbeErrors = $buddyProbeProcess.StandardError.ReadToEndAsync()
        if (-not $buddyProbeProcess.WaitForExit(30000)) {
            $buddyProbeProcess.Kill()
            throw 'Native apphost dependency probe timed out.'
        }
        $buddyProbeText = $buddyProbeOutput.GetAwaiter().GetResult()
        $buddyProbeErrorText = $buddyProbeErrors.GetAwaiter().GetResult()
        if ($LogDirectory) {
            $buddyProbeText | Set-Content -LiteralPath (Join-Path $LogDirectory 'Native-dependencies.txt') -Encoding utf8
            $buddyProbeErrorText | Set-Content -LiteralPath (Join-Path $LogDirectory 'Native-dependencies-errors.txt') -Encoding utf8
        }
        if ($buddyProbeProcess.ExitCode -ne 0) {
            throw "Native dependency probe failed (exit $($buddyProbeProcess.ExitCode)): $buddyProbeErrorText"
        }
        Write-Output $buddyProbeText.Trim()
    } finally { $buddyProbeProcess.Dispose() }
}
