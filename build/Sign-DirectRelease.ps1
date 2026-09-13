[CmdletBinding()]
param([Parameter(Mandatory)][string]$CertificateThumbprint, [Parameter(Mandatory)][uri]$TimestampUrl)
. "$PSScriptRoot/Common.ps1"
Assert-Windows
if ($Brand.publisherDisplayName -like 'REPLACE_*') { throw 'Set the real publisher display name before preparing a signed release.' }
if ($CertificateThumbprint -notmatch '^[0-9A-Fa-f]{40}$') { throw 'Expected a certificate thumbprint, not a path or secret.' }
if ($TimestampUrl.Scheme -notin @('http', 'https')) { throw 'An RFC 3161 timestamp service URL is required.' }
$certificate = Get-Item "Cert:/CurrentUser/My/$CertificateThumbprint"
if (-not $certificate.HasPrivateKey) { throw 'The selected certificate has no accessible private key.' }
if ($certificate.NotAfter -lt (Get-Date)) { throw 'The selected certificate has expired.' }
$signTool = Get-SdkTool 'signtool.exe'
$signArguments = @('sign', '/sha1', $CertificateThumbprint, '/fd', 'SHA256', '/tr', $TimestampUrl.AbsoluteUri, '/td', 'SHA256')
$files = Get-ChildItem (Get-PublishDirectory) -File | Where-Object { $_.Name -like 'BlinkReminder*.dll' -or $_.Name -eq 'BlinkReminder.exe' }
foreach ($file in $files) {
    Invoke-Checked $signTool ($signArguments + $file.FullName)
    Invoke-Checked $signTool @('verify', '/pa', '/all', '/v', $file.FullName)
}
# Inno signs the uninstaller before embedding it, and then signs the setup EXE.
$command = '"' + $signTool + '" sign /sha1 ' + $CertificateThumbprint + ' /fd SHA256 /tr "' + $TimestampUrl.AbsoluteUri + '" /td SHA256 $f'
& "$PSScriptRoot/Package-Exe.ps1" -SignToolCommand $command
foreach ($file in Get-ChildItem "$RepositoryRoot/artifacts/installers/*-setup.exe") {
    Invoke-Checked $signTool @('verify', '/pa', '/all', '/v', $file.FullName)
}
