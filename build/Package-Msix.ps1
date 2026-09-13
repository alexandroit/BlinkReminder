[CmdletBinding()]
param([switch]$ForStore)
. "$PSScriptRoot/Common.ps1"
Assert-Windows
if ($ForStore -and (-not $Identity.storeIdentityConfigured -or $Brand.publisherDisplayName -like 'REPLACE_*' -or -not $Brand.supportUrl -or -not $Brand.privacyUrl)) {
    throw 'Configure the reserved Partner Center identity, publisher, support and privacy URLs before preparing a Store submission.'
}
$version = "$(Get-ProductVersion).0"
$stage = Join-Path $RepositoryRoot 'artifacts/msix-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item $stage -ItemType Directory -Force | Out-Null
Copy-Item "$(Get-PublishDirectory)/*" $stage -Recurse
New-Item "$stage/Assets" -ItemType Directory -Force | Out-Null
Copy-Item "$RepositoryRoot/assets/generated/*.png" "$stage/Assets"
$values = @{
    PackageName = $Identity.packageName; PackagePublisher = $Identity.packagePublisher
    Version = $version; DisplayName = $Brand.displayName; PublisherDisplayName = $Brand.publisherDisplayName
    Description = $Brand.description; StartupTaskId = $Identity.startupTaskId
    NotificationActivatorId = $Identity.notificationActivatorId
}
$template = Get-Content "$RepositoryRoot/packaging/msix/AppxManifest.xml.template" -Raw
foreach ($key in $values.Keys) { $template = $template.Replace("{{$key}}", [Security.SecurityElement]::Escape([string]$values[$key])) }
if ($template -match '\{\{') { throw 'Unresolved manifest placeholder.' }
$template | Set-Content "$stage/AppxManifest.xml" -Encoding utf8NoBOM
New-Item "$RepositoryRoot/artifacts/installers" -ItemType Directory -Force | Out-Null
$channel = if ($ForStore) { 'store-unsigned' } else { 'development-unsigned' }
Invoke-Checked (Get-SdkTool 'makeappx.exe') @('pack', '/d', $stage, '/p', "$RepositoryRoot/artifacts/installers/BlinkReminder-$version-win-x64-$channel.msix", '/o')
