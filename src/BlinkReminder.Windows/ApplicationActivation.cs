using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BlinkReminder.Windows;

public static class ApplicationActivation
{
    public const string NotificationActivationArgument = "----AppNotificationActivated:";

    /// <summary>Call after registering notifications. EXE --background is handled by the host.</summary>
    public static bool IsStartupLaunch()
    {
        try
        {
            return ApplicationPaths.HasPackageIdentity()
                && AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.StartupTask;
        }
        catch (Exception ex) when (IsIntegrationFailure(ex)) { return false; }
    }

    // The switch selects the COM handshake path; it is never treated as a notification action.
    public static bool IsNotificationLaunch(IEnumerable<string> arguments) =>
        arguments.Contains(NotificationActivationArgument, StringComparer.Ordinal);

    internal static InstanceCommand? ReadNotificationCommand()
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        return activation.Kind == ExtendedActivationKind.AppNotification
            && activation.Data is AppNotificationActivatedEventArgs notification
            && NativeNotifications.TryParseActivation(notification.Argument, out var command)
                ? command : null;
    }

    internal static bool IsIntegrationFailure(Exception ex) => ex is COMException or Win32Exception
        or InvalidOperationException or UnauthorizedAccessException or DllNotFoundException or TypeLoadException
        or FileNotFoundException or BadImageFormatException or TypeInitializationException or ArgumentException;
}
