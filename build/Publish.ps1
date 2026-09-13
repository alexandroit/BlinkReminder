[CmdletBinding()]
param()
. "$PSScriptRoot/Common.ps1"
Assert-Windows
$destination = Get-PublishDirectory
if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
Push-Location $RepositoryRoot
try {
    Invoke-Checked dotnet @('publish', 'src/BlinkReminder.App/BlinkReminder.App.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:RestoreLockedMode=true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-o', $destination)
    $runtimeConfig = Get-Content (Join-Path $destination 'BlinkReminder.runtimeconfig.json') -Raw | ConvertFrom-Json
    foreach ($frameworkName in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
        $framework = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq $frameworkName)
        if ($framework.Count -ne 1 -or $framework[0].version -ne $Tools.dotnetRuntime) {
            throw "Expected bundled $frameworkName $($Tools.dotnetRuntime). Check the pinned SDK and published runtime configuration."
        }
    }
    Copy-Item LICENSE, THIRD-PARTY-NOTICES.md $destination
    & "$PSScriptRoot/Collect-Licenses.ps1"
    if (-not (Test-Path (Join-Path $destination 'BlinkReminder.exe'))) { throw 'App executable missing.' }
    if (-not (Test-Path (Join-Path $destination 'coreclr.dll'))) { throw '.NET self-contained runtime missing.' }
    if (-not (Test-Path (Join-Path $destination 'Microsoft.WindowsAppRuntime.dll'))) { throw 'Windows App SDK self-contained runtime missing.' }
} finally { Pop-Location }
