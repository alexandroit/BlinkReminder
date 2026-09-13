[CmdletBinding()]
param([string]$CompilerPath = "$env:ProgramFiles\Inno Setup 7\ISCC.exe", [string]$SignToolCommand = '')
. "$PSScriptRoot/Common.ps1"
Assert-Windows
if (-not (Test-Path $CompilerPath)) { throw 'Install Inno Setup 7.1.0 or specify -CompilerPath.' }
$compilerVersion = (Get-Item $CompilerPath).VersionInfo.FileVersion
if ($compilerVersion -notlike "$($Tools.innoSetup)*") { throw "Expected Inno Setup $($Tools.innoSetup), found $compilerVersion." }
$version = Get-ProductVersion
$publish = Get-PublishDirectory
if (-not (Test-Path "$publish/BlinkReminder.exe")) { throw 'Run build/Publish.ps1 first.' }
$output = Join-Path $RepositoryRoot 'artifacts/installers'
New-Item $output -ItemType Directory -Force | Out-Null
$definitions = @("/DAppVersion=$version", "/DAppName=$($Brand.displayName)", "/DAppPublisher=$($Brand.publisherDisplayName)", "/DAppGuid=$($Identity.innoAppId)", "/DInstallName=$($Identity.installDirectoryName)", "/DSourceDirectory=$publish", "/DOutputDirectory=$output", "/DRepositoryRoot=$RepositoryRoot")
if ($SignToolCommand) { $definitions += '/DSignedBuild=1'; $definitions += "/SBlinkSign=$SignToolCommand" }
Invoke-Checked $CompilerPath ($definitions + (Join-Path $RepositoryRoot 'packaging/inno/BlinkReminder.iss'))
