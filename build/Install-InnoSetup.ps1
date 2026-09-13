[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
Assert-Windows
$installer = Join-Path ([IO.Path]::GetTempPath()) "blink-inno-$([Guid]::NewGuid()).exe"
try {
    Invoke-WebRequest $Tools.innoDownloadUrl -OutFile $installer
    if ((Get-FileHash $installer -Algorithm SHA256).Hash -ne $Tools.innoSha256) { throw 'Inno Setup installer checksum mismatch.' }
    $signature = Get-AuthenticodeSignature $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') { throw 'Inno Setup publisher signature did not verify.' }
    $process = Start-Process $installer -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($process.ExitCode)." }
} finally { Remove-Item $installer -Force -ErrorAction SilentlyContinue }
