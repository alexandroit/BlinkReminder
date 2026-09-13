using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;

namespace BlinkReminder.Windows;

public enum NotificationPermission { Available, Busy, Presentation, Unknown }
public sealed record SessionSnapshot(bool IsLocked, bool IsSuspended, bool IsSecureDesktop,
    NotificationPermission NotificationPermission, TimeSpan? Idle);

public sealed class WindowsSessionStateProvider : IDisposable
{
    private volatile bool locked, suspended;
    private bool disposed;
    public event EventHandler? Changed;
    public event EventHandler? Resumed;

    public WindowsSessionStateProvider()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public SessionSnapshot GetSnapshot(bool includeIdle = false)
    {
        int result = NativeMethods.SHQueryUserNotificationState(out int state);
        NotificationPermission permission = result < 0 ? NotificationPermission.Unknown : state switch
        {
            5 => NotificationPermission.Available, // QUNS_ACCEPTS_NOTIFICATIONS
            4 => NotificationPermission.Presentation,
            1 or 2 or 3 or 6 or 7 => NotificationPermission.Busy,
            _ => NotificationPermission.Unknown
        };
        return new(locked, suspended, !IsDefaultInputDesktop(), permission, includeIdle ? ReadIdle() : null);
    }

    private static bool IsDefaultInputDesktop()
    {
        nint desktop = NativeMethods.OpenInputDesktop(0, false, 1);
        if (desktop == 0) return false;
        try
        {
            var name = new StringBuilder(256);
            return NativeMethods.GetUserObjectInformation(desktop, 2, name, 512, out _)
                && string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
        }
        finally { NativeMethods.CloseDesktop(desktop); }
    }

    private static TimeSpan? ReadIdle()
    {
        var info = new NativeMethods.LastInputInfo { Size = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>() };
        return NativeMethods.GetLastInputInfo(ref info)
            ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : null;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs args)
    {
        if (args.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect) locked = true;
        if (args.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect)
        { locked = false; Resumed?.Invoke(this, EventArgs.Empty); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend) suspended = true;
        if (args.Mode == PowerModes.Resume) { suspended = false; Resumed?.Invoke(this, EventArgs.Empty); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
