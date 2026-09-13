[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
$publish = Get-PublishDirectory
$installedBytes = (Get-ChildItem $publish -File -Recurse | Measure-Object Length -Sum).Sum
$artifacts = @(Get-ChildItem "$RepositoryRoot/artifacts/installers" -File | Where-Object Extension -in '.exe', '.msix' | ForEach-Object {
    [ordered]@{ file = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
[ordered]@{ version = Get-ProductVersion; architecture = 'win-x64'; installedBytes = $installedBytes; artifacts = $artifacts } |
    ConvertTo-Json -Depth 4 | Set-Content "$RepositoryRoot/artifacts/installers/artifact-manifest.json" -Encoding utf8NoBOM
