[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
. "$PSScriptRoot/Common.ps1"
Assert-Windows
Push-Location $RepositoryRoot
try {
    Invoke-Checked dotnet @('test', 'BlinkReminder.slnx', '-c', $Configuration, '--no-build', '--no-restore', '--logger', 'trx', '--results-directory', 'artifacts/test-results')
} finally { Pop-Location }
