[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
$publish = Get-PublishDirectory
$publishedPayloadBytes = (Get-ChildItem $publish -File -Recurse | Measure-Object Length -Sum).Sum
$sourceCommit = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not determine the source commit.' }
$artifacts = @(Get-ChildItem "$RepositoryRoot/artifacts/installers" -File | Where-Object Extension -in '.exe', '.msix' | ForEach-Object {
    [ordered]@{ file = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
[ordered]@{ version = Get-ProductVersion; architecture = 'win-x64'; sourceCommit = $sourceCommit; publishedPayloadBytes = $publishedPayloadBytes; artifacts = $artifacts } |
    ConvertTo-Json -Depth 4 | Set-Content "$RepositoryRoot/artifacts/installers/artifact-manifest.json" -Encoding utf8NoBOM
