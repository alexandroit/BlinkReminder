[CmdletBinding()]
param([string]$CompilerPath = "$env:ProgramFiles\Inno Setup 7\ISCC.exe", [string]$SignToolCommand = '')
. "$PSScriptRoot/Common.ps1"
Assert-Windows
if (-not (Test-Path $CompilerPath)) { throw 'Install Inno Setup 7.1.0 or specify -CompilerPath.' }
# ISCC's executable version resource is 0.0.0.0; query the actual compiler engine.
$compilerVersion = (& $CompilerPath '--version' 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $compilerVersion -ne $Tools.innoSetup) {
    throw "Expected Inno Setup compiler engine $($Tools.innoSetup), found $compilerVersion."
}
$version = Get-ProductVersion
$publish = Get-PublishDirectory
if (-not (Test-Path "$publish/BlinkReminder.exe")) { throw 'Run build/Publish.ps1 first.' }
$output = Join-Path $RepositoryRoot 'artifacts/installers'
New-Item $output -ItemType Directory -Force | Out-Null
$definitions = @("/DAppVersion=$version", "/DAppName=$($Brand.displayName)", "/DAppPublisher=$($Brand.publisherDisplayName)", "/DAppGuid=$($Identity.innoAppId)", "/DInstallName=$($Identity.installDirectoryName)", "/DSourceDirectory=$publish", "/DOutputDirectory=$output", "/DRepositoryRoot=$RepositoryRoot")
if ($SignToolCommand) { $definitions += '/DSignedBuild=1'; $definitions += "/SBlinkSign=$SignToolCommand" }
Invoke-Checked $CompilerPath ($definitions + (Join-Path $RepositoryRoot 'packaging/inno/BlinkReminder.iss'))
