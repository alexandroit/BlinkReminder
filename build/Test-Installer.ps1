[CmdletBinding()]
param([switch]$DisposableMachine)
. "$PSScriptRoot/Common.ps1"
Assert-Windows
if (-not $DisposableMachine) { throw 'This smoke test installs and uninstalls the app. Run only on a disposable Windows machine with -DisposableMachine.' }
$installer = Join-Path $RepositoryRoot "artifacts/installers/BlinkReminder-$(Get-ProductVersion)-win-x64-setup.exe"
if (-not (Test-Path $installer)) { throw 'Build the EXE installer first.' }
$uninstallKey = "Software\Microsoft\Windows\CurrentVersion\Uninstall\{$($Identity.innoAppId)}_is1"
$userKey = "HKCU:\$uninstallKey"
$machineKey = "HKLM:\$uninstallKey"
if ((Test-Path $userKey) -or (Test-Path $machineKey)) { throw 'An existing BlinkReminder installation was found. Use a clean disposable machine.' }
$userDirectory = Join-Path $env:LOCALAPPDATA "Programs/$($Identity.installDirectoryName)"
$machineDirectory = Join-Path $env:ProgramFiles $Identity.installDirectoryName
if ((Test-Path $userDirectory) -or (Test-Path $machineDirectory)) { throw 'An existing BlinkReminder directory was found. Use a clean disposable machine.' }
$resultsDirectory = Join-Path $RepositoryRoot 'artifacts/test-results'
New-Item $resultsDirectory -ItemType Directory -Force | Out-Null
$checks = [Collections.Generic.List[object]]::new()
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$administrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

function Run-Setup {
    param([string]$Scope, [string]$LogName, [bool]$ExpectSuccess = $true)
    $logPath = Join-Path $resultsDirectory "$LogName.log"
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NORESTARTAPPLICATIONS', "/LOG=`"$logPath`"")
    if ($Scope) { $arguments = @("/$Scope") + $arguments }
    $process = Start-Process $installer -ArgumentList $arguments -Wait -PassThru
    if ($ExpectSuccess -and $process.ExitCode -ne 0) { throw "$LogName failed with exit code $($process.ExitCode)." }
    if (-not $ExpectSuccess -and $process.ExitCode -eq 0) { throw "$LogName accepted a conflicting install scope." }
    $checks.Add([ordered]@{ check = $LogName; passed = $true; exitCode = $process.ExitCode })
}
function Remove-TestInstallation {
    param([string]$Directory)
    $uninstaller = Join-Path $Directory 'unins000.exe'
    if (Test-Path $uninstaller) {
        $process = Start-Process $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Uninstall failed with exit code $($process.ExitCode)." }
    }
}

try {
    Run-Setup CURRENTUSER 'current-user-install'
    if (-not (Test-Path "$userDirectory/BlinkReminder.exe") -or -not (Test-Path $userKey) -or (Test-Path $machineKey)) { throw 'Current-user install paths or registry scope are incorrect.' }
    Run-Setup '' 'current-user-upgrade'
    if (-not (Test-Path "$userDirectory/BlinkReminder.exe") -or (Test-Path $machineKey)) { throw 'Current-user upgrade changed scope.' }
    if ($administrator) {
        Run-Setup ALLUSERS 'reject-global-over-user' $false
        if (Test-Path $machineKey) { throw 'Cross-scope rejection left a machine registration.' }
    }
    Remove-TestInstallation $userDirectory
    if ((Test-Path "$userDirectory/BlinkReminder.exe") -or (Test-Path $userKey)) { throw 'Current-user uninstall left installation resources.' }
    $checks.Add([ordered]@{ check = 'current-user-uninstall'; passed = $true })

    if ($administrator) {
        Run-Setup ALLUSERS 'all-users-install'
        if (-not (Test-Path "$machineDirectory/BlinkReminder.exe") -or -not (Test-Path $machineKey) -or (Test-Path $userKey)) { throw 'All-users paths or registry scope are incorrect.' }
        Run-Setup '' 'all-users-upgrade'
        Run-Setup CURRENTUSER 'reject-user-over-global' $false
        if (Test-Path $userKey) { throw 'Cross-scope rejection left a user registration.' }
        Remove-TestInstallation $machineDirectory
        if ((Test-Path "$machineDirectory/BlinkReminder.exe") -or (Test-Path $machineKey)) { throw 'Global uninstall left installation resources.' }
        $checks.Add([ordered]@{ check = 'all-users-uninstall'; passed = $true })
    }
    [ordered]@{
        hostAlreadyAdministrator = $administrator
        limitations = @('No interactive UAC test', 'No second-user credential test', 'No ordinary-user privilege proof', 'No app visual/runtime acceptance')
        checks = $checks.ToArray()
    } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $resultsDirectory 'installer-smoke.json') -Encoding utf8NoBOM
} finally {
    foreach ($directory in @($userDirectory, $machineDirectory)) {
        try { Remove-TestInstallation $directory } catch { Write-Warning "Test installation cleanup failed: $($_.Exception.Message)" }
    }
}
