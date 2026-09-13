Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepositoryRoot = Split-Path $PSScriptRoot -Parent
$Tools = Get-Content (Join-Path $PSScriptRoot 'tool-versions.json') -Raw | ConvertFrom-Json
$Brand = Get-Content (Join-Path $RepositoryRoot 'assets/branding/brand.json') -Raw | ConvertFrom-Json
$Identity = Get-Content (Join-Path $RepositoryRoot 'packaging/identity.json') -Raw | ConvertFrom-Json

function Assert-Windows {
    if (-not $IsWindows) { throw 'This command requires Windows and PowerShell 7.' }
}
function Invoke-Checked {
    param([Parameter(Mandatory)][string]$File, [string[]]$Arguments = @())
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE." }
}
function Get-SdkTool {
    param([Parameter(Mandatory)][string]$Name)
    $path = Join-Path ${env:ProgramFiles(x86)} "Windows Kits/10/bin/$($Tools.windowsSdk)/x64/$Name"
    if (-not (Test-Path $path)) { throw "Missing $path. Install Windows SDK $($Tools.windowsSdk)." }
    return $path
}
function Get-ProductVersion {
    [xml]$props = Get-Content (Join-Path $RepositoryRoot 'Directory.Build.props') -Raw
    $version = @($props.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must have three numeric components.' }
    return [string]$version
}
function Get-PublishDirectory { return Join-Path $RepositoryRoot 'artifacts/publish/win-x64' }
