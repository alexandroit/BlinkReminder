using Microsoft.Win32;
using System.Security;
using global::Windows.ApplicationModel;

namespace BlinkReminder.Windows;

public enum StartupRegistrationState { Disabled, Enabled, DisabledByUser, DisabledByPolicy, OwnedByOtherInstallation, Unavailable }

public sealed class WindowsStartupAdapter
{
    public const string TaskId = "BlinkReminder.Startup";
    public const string RunValueName = "BlinkReminder";
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private readonly bool packaged;
    private readonly string executable;

    public WindowsStartupAdapter() : this(ApplicationPaths.HasPackageIdentity(), Environment.ProcessPath ?? throw new InvalidOperationException("executable_path_missing")) { }

    public WindowsStartupAdapter(bool packaged, string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"')) throw new ArgumentException("executable_path_invalid", nameof(executable));
        this.packaged = packaged;
        this.executable = Path.GetFullPath(executable);
    }

    public async Task<StartupRegistrationState> GetStateAsync()
    {
        try
        {
            if (packaged) return ConvertState((await StartupTask.GetAsync(TaskId)).State);
            return ReadUnpackagedState();
        }
        catch (Exception ex) when (IsIntegrationFailure(ex)) { return StartupRegistrationState.Unavailable; }
    }

    public async Task<StartupRegistrationState> SetEnabledAsync(bool enabled, bool explicitConsent)
    {
        if (enabled && !explicitConsent) throw new InvalidOperationException("startup_consent_required");
        try
        {
            if (packaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                if (!enabled) { task.Disable(); return ConvertState(task.State); }
                if (task.State is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy)
                    return ConvertState(task.State);
                return ConvertState(await task.RequestEnableAsync());
            }
            StartupRegistrationState state = ReadUnpackagedState();
            if (enabled && state is StartupRegistrationState.DisabledByUser or StartupRegistrationState.DisabledByPolicy
                or StartupRegistrationState.OwnedByOtherInstallation or StartupRegistrationState.Unavailable) return state;
            using var run = Registry.CurrentUser.CreateSubKey(RunPath);
            if (enabled) run.SetValue(RunValueName, Command, RegistryValueKind.String);
            else if (OwnsCommand(run.GetValue(RunValueName))) run.DeleteValue(RunValueName, false);
            return ReadUnpackagedState();
        }
        catch (Exception ex) when (IsIntegrationFailure(ex)) { return StartupRegistrationState.Unavailable; }
    }

    public Task<StartupRegistrationState> RemoveCurrentInstallationAsync() => SetEnabledAsync(false, false);

    private string Command => $"\"{executable}\" --background";
    private bool OwnsCommand(object? value) => value is string command && string.Equals(command, Command, StringComparison.OrdinalIgnoreCase);

    private StartupRegistrationState ReadUnpackagedState()
    {
        const string policyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        using var userPolicy = Registry.CurrentUser.OpenSubKey(policyPath);
        using var machinePolicy = Registry.LocalMachine.OpenSubKey(policyPath);
        if (IsDisabledPolicy(userPolicy) || IsDisabledPolicy(machinePolicy)) return StartupRegistrationState.DisabledByPolicy;
        using var approval = Registry.CurrentUser.OpenSubKey(ApprovedPath);
        var status = InterpretStartupApproval(approval?.GetValue(RunValueName));
        if (status != StartupRegistrationState.Enabled) return status;
        using var run = Registry.CurrentUser.OpenSubKey(RunPath);
        object? value = run?.GetValue(RunValueName);
        if (value is null) return StartupRegistrationState.Disabled;
        return OwnsCommand(value) ? StartupRegistrationState.Enabled : StartupRegistrationState.OwnedByOtherInstallation;
    }

    private static bool IsDisabledPolicy(RegistryKey? key) => key?.GetValue("DisableCurrentUserRun") is int value && value != 0;

    /// <summary>StartupApproved is not a public API. Read only; unfamiliar records fail closed.</summary>
    public static StartupRegistrationState InterpretStartupApproval(object? value) => value switch
    {
        null => StartupRegistrationState.Enabled,
        byte[] { Length: >= 12 } bytes when bytes[0] is 2 or 6 => StartupRegistrationState.Enabled,
        byte[] { Length: >= 12 } bytes when bytes[0] is 3 or 7 => StartupRegistrationState.DisabledByUser,
        _ => StartupRegistrationState.Unavailable
    };

    private static StartupRegistrationState ConvertState(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => StartupRegistrationState.Enabled,
        StartupTaskState.Disabled => StartupRegistrationState.Disabled,
        StartupTaskState.DisabledByUser => StartupRegistrationState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupRegistrationState.DisabledByPolicy,
        _ => StartupRegistrationState.Unavailable
    };

    private static bool IsIntegrationFailure(Exception exception) => exception is
        UnauthorizedAccessException or SecurityException or IOException or System.Runtime.InteropServices.COMException;
}
