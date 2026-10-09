param(
    [Parameter(Mandatory=$true)][string]$PreviewPath,
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-p]{32}$')][string]$ExtensionId,
    [Parameter(Mandatory=$true)][ValidateSet('Chrome','Edge')][string]$Browser,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
# PREPARATION ONLY. This script does not change the registry, load extensions,
# launch a browser, read a conversation or grant native messaging access.
$preview = (Resolve-Path -LiteralPath $PreviewPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $preview '.buddy-preview') -PathType Leaf)) { throw 'Choose an isolated Buddy preview.' }
$hostExe = Join-Path $preview 'Buddy.BrowserHost.exe'
if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) { throw 'The preview does not contain the reviewed browser host.' }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'Use a new output directory; existing setup will not be overwritten.' }
$sha = [Security.Cryptography.SHA256]::Create()
try { $seed = [Text.Encoding]::UTF8.GetBytes([Environment]::UserName + '|' + $preview.TrimEnd('\','/').ToUpperInvariant()); $suffix = ([BitConverter]::ToString($sha.ComputeHash($seed))).Replace('-','').ToLowerInvariant().Substring(0,24) }
finally { $sha.Dispose() }
$pipeName = 'Buddy.BrowserContext.v1.' + $suffix
$registry = if ($Browser -eq 'Chrome') { 'Software\Google\Chrome\NativeMessagingHosts\com.buddy.browser_context' } else { 'Software\Microsoft\Edge\NativeMessagingHosts\com.buddy.browser_context' }
New-Item -ItemType Directory -Path $destination | Out-Null
$policyPath = Join-Path $destination 'browser-host-policy.json'
[IO.File]::WriteAllText($policyPath, (@{ version=1; extensionId=$ExtensionId; pipeName=$pipeName } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$manifestPath = Join-Path $destination 'com.buddy.browser_context.json'
[IO.File]::WriteAllText($manifestPath, (@{ name='com.buddy.browser_context'; description='Buddy reviewed browser context'; path=$hostExe; type='stdio'; allowed_origins=@('chrome-extension://' + $ExtensionId + '/') } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$policyTarget = Join-Path $preview 'browser-host-policy.json'
$hostStream = [IO.File]::OpenRead($hostExe)
$hostHasher = [Security.Cryptography.SHA256]::Create()
try { $hostHash = ([BitConverter]::ToString($hostHasher.ComputeHash($hostStream))).Replace('-','').ToLowerInvariant() }
finally { $hostHasher.Dispose(); $hostStream.Dispose() }
$review = [ordered]@{
    preparationOnly=$true; browser=$Browser; extensionId=$ExtensionId; preview=$preview; pipeName=$pipeName
    manifest=$manifestPath; hostExecutable=$hostExe; hostSha256=$hostHash
    pendingPolicyCopy=@{ source=$policyPath; destination=$policyTarget; overwriteAllowed=$false }
    pendingRegistryValue=@{ hive='HKEY_CURRENT_USER'; key=$registry; name='(Default)'; type='REG_SZ'; value=$manifestPath }
    consentRequired='Review and separately authorize copying the policy and adding this per-user registry value after loading only the exact reviewed unpacked extension. No real chat/history/upload permission is implied.'
    firstLiveScope='User-clicked ChatGPT readiness characterization only. Production account/workspace identity admission is unavailable; history, draft insertion and attachments stay blocked.'
    revoke='Close the Buddy browser session, remove this exact registry value and the unpacked extension. Preserve any prior registry value before installation.'
    unsupported='Comet native-host registry discovery is not established by this setup; an attachment button or Chrome-extension compatibility is insufficient.'
}
[IO.File]::WriteAllText((Join-Path $destination 'REVIEW-SETUP.json'), ($review | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output ('Prepared setup review only: ' + (Join-Path $destination 'REVIEW-SETUP.json'))
