[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
$assetsPath = Join-Path $RepositoryRoot 'src/BlinkReminder.App/obj/project.assets.json'
if (-not (Test-Path $assetsPath)) { throw 'Restore and publish the application before collecting licenses.' }
$assets = Get-Content $assetsPath -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
$destination = Join-Path (Get-PublishDirectory) 'Licenses'
New-Item $destination -ItemType Directory -Force | Out-Null
$packages = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -eq 'package') { [void]$packages.Add($library.Value.path) }
}
foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    [void]$packages.Add("$runtime/$($Tools.dotnetRuntime)")
}
$index = @()
foreach ($package in ($packages | Sort-Object)) {
    $packagePath = $null
    foreach ($root in $packageRoots) {
        $candidate = Join-Path $root $package
        if (Test-Path $candidate) { $packagePath = $candidate; break }
    }
    if (-not $packagePath) { throw "Restored dependency directory is missing: $package" }
    $name = $package.Replace('/', '-')
    $licenseDirectory = Join-Path $destination $name
    $notices = @(Get-ChildItem $packagePath -File | Where-Object { $_.Name -match '(?i)license|licence|third.?party|notices?' })
    if ($notices.Count -gt 0) {
        New-Item $licenseDirectory -ItemType Directory -Force | Out-Null
        $notices | Copy-Item -Destination $licenseDirectory
    }
    $license = ''
    $nuspec = Get-ChildItem $packagePath -Filter '*.nuspec' -File | Select-Object -First 1
    if ($nuspec) {
        [xml]$metadata = Get-Content $nuspec.FullName -Raw
        $node = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
        if ($node) { $license = $node.InnerText }
    }
    $index += [ordered]@{ package = $package; declaredLicense = $license; copiedFiles = @($notices | ForEach-Object Name) }
}
$index | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $destination 'dependency-index.json') -Encoding utf8NoBOM
foreach ($required in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64', 'microsoft.windowsappsdk.foundation')) {
    if (-not ($index | Where-Object { $_.package.StartsWith("$required/") -and $_.copiedFiles.Count -gt 0 })) {
        throw "No original license/notice file was collected for $required. Inspect the restored package before distribution."
    }
}
