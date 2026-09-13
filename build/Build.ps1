[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
. "$PSScriptRoot/Common.ps1"
Assert-Windows
Push-Location $RepositoryRoot
try {
    Invoke-Checked dotnet @('build', 'BlinkReminder.slnx', '-c', $Configuration, '-r', 'win-x64', '--no-restore')
} finally { Pop-Location }
