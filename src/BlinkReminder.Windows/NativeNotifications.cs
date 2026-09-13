using Microsoft.Windows.AppNotifications;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace BlinkReminder.Windows;

public enum NotificationSendStatus { Submitted, Blocked, Unavailable, Failed }
public sealed record NotificationSendResult(NotificationSendStatus Status, int? ErrorCode = null);

/// <summary>Submitting a notification never claims that Windows displayed it or that the user saw it.</summary>
public sealed class NativeNotifications : IDisposable
{
    private AppNotificationManager? manager;
    private readonly TaskCompletionSource<InstanceCommand> firstActivation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event EventHandler<InstanceCommand>? ActivationRequested;
    public event EventHandler<int>? Failed;

    public NotificationSendResult Register()
    {
        if (manager is not null) return new(NotificationSendStatus.Submitted);
        try
        {
            if (ProcessSecurity.IsElevated() || !AppNotificationManager.IsSupported()) return new(NotificationSendStatus.Unavailable);
            var candidate = AppNotificationManager.Default;
            candidate.NotificationInvoked += OnInvoked;
            try { candidate.Register(); }
            catch { candidate.NotificationInvoked -= OnInvoked; throw; }
            manager = candidate;
            return new(NotificationSendStatus.Submitted);
        }
        catch (Exception ex) when (IsIntegrationFailure(ex)) { return Failure(ex); }
    }

    public NotificationSendResult Show(string title, string message, bool sound = false)
    {
        if (manager is null) return new(NotificationSendStatus.Unavailable);
        try
        {
            if (manager.Setting != AppNotificationSetting.Enabled) return new(NotificationSendStatus.Blocked);
            var notification = new AppNotification(BuildPayload(title, message, sound))
            {
                Tag = "reminder",
                Group = "BlinkReminder",
                Expiration = DateTimeOffset.Now.AddMinutes(5)
            };
            manager.Show(notification);
            return new(notification.Id != 0 ? NotificationSendStatus.Submitted : NotificationSendStatus.Failed);
        }
        catch (Exception ex) when (IsIntegrationFailure(ex)) { return Failure(ex); }
    }

    public static string BuildPayload(string title, string message, bool sound = false)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        if (title.Length > 200 || message.Length > 2000) throw new ArgumentOutOfRangeException(nameof(message), "notification_text_too_long");
        var toast = new XElement("toast", new XAttribute("launch", "action=openSettings"),
            new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
                new XElement("text", title), new XElement("text", message))));
        if (!sound) toast.Add(new XElement("audio", new XAttribute("silent", "true")));
        return toast.ToString(SaveOptions.DisableFormatting);
    }

    public static bool TryParseActivation(string? arguments, out InstanceCommand command)
    {
        command = InstanceCommand.OpenSettings;
        // Do not pass arbitrary arguments, URIs, filenames or shell commands to the application.
        return string.Equals(arguments, "action=openSettings", StringComparison.Ordinal);
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (!TryParseActivation(args.Argument, out var command)) return;
        firstActivation.TrySetResult(command);
        ActivationRequested?.Invoke(this, command);
    }

    /// <summary>After Register, complete a COM activation handshake without accepting arbitrary command-line input.</summary>
    public async Task<InstanceCommand?> GetLaunchCommandAsync(CancellationToken cancellationToken = default)
    {
        if (manager is null) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            // The SDK may wait for the COM payload. Keep that wait away from WPF's dispatcher.
            var coldActivation = Task.Run(() =>
            {
                try { return ApplicationActivation.ReadNotificationCommand(); }
                catch (Exception ex) when (ApplicationActivation.IsIntegrationFailure(ex)) { return null; }
            }, timeout.Token);
            var completed = await Task.WhenAny(coldActivation, firstActivation.Task).WaitAsync(timeout.Token).ConfigureAwait(false);
            if (completed == firstActivation.Task) return await firstActivation.Task.ConfigureAwait(false);
            var command = await coldActivation.ConfigureAwait(false);
            return command ?? await firstActivation.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
    }

    public async Task ClearAsync()
    {
        if (manager is null) return;
        try { await manager.RemoveAllAsync(); }
        catch (Exception ex) when (IsIntegrationFailure(ex)) { Failed?.Invoke(this, ex.HResult); }
    }

    public async Task RemoveRegistrationAsync()
    {
        if (manager is null) return;
        await ClearAsync();
        var previous = manager;
        manager = null;
        previous.NotificationInvoked -= OnInvoked;
        try { previous.UnregisterAll(); }
        catch (Exception ex) when (IsIntegrationFailure(ex)) { Failed?.Invoke(this, ex.HResult); }
    }

    public void Dispose()
    {
        if (manager is null) return;
        var previous = manager;
        manager = null;
        previous.NotificationInvoked -= OnInvoked;
        try { previous.Unregister(); }
        catch (Exception ex) when (IsIntegrationFailure(ex)) { Failed?.Invoke(this, ex.HResult); }
    }

    private NotificationSendResult Failure(Exception ex)
    {
        Failed?.Invoke(this, ex.HResult);
        return new(NotificationSendStatus.Failed, ex.HResult);
    }

    private static bool IsIntegrationFailure(Exception ex) => ex is COMException or InvalidOperationException
        or UnauthorizedAccessException or DllNotFoundException or TypeLoadException or FileNotFoundException
        or BadImageFormatException or TypeInitializationException or ArgumentException or System.Xml.XmlException;
}
