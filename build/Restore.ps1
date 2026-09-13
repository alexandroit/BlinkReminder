[CmdletBinding()]
param([switch]$UpdateLockFiles)
. "$PSScriptRoot/Common.ps1"
Assert-Windows
Push-Location $RepositoryRoot
try {
    $arguments = @('restore', 'BlinkReminder.slnx', '-r', 'win-x64')
    if ($UpdateLockFiles) { $arguments += '--force-evaluate' } else { $arguments += '--locked-mode' }
    Invoke-Checked dotnet $arguments
} finally { Pop-Location }
