using System.Windows.Threading;
using BlinkReminder.App.Views;
using BlinkReminder.Core;
using BlinkReminder.Windows;
using CoreSession = BlinkReminder.Core.SessionSnapshot;
using WindowsSession = BlinkReminder.Windows.SessionSnapshot;

namespace BlinkReminder.App.Services;

/// <summary>Owns one scheduler poller and one presentation at a time on the WPF dispatcher.</summary>
public sealed class ReminderHost : IAsyncDisposable
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly ReminderScheduler scheduler;
    private readonly WindowsSessionStateProvider sessionProvider = new();
    private readonly NativeNotifications notifications;
    private readonly LocalStatistics statistics;
    private readonly LocalDiagnostics diagnostics;
    private readonly CancellationTokenSource shutdown = new();
    private AppSettings settings;
    private PeriodicTimer? poller;
    private Task? pollTask;
    private Task? presentationTask;
    private CancellationTokenSource? presentationCancellation;
    private long presentationGeneration;
    private bool previewPending;
    private bool isPreview;
    private bool presentationShown;
    private bool manualPresentation;
    private bool resumed;
    private bool disposed;

    public ReminderHost(AppSettings settings, NativeNotifications notifications, LocalStatistics stats,
        LocalDiagnostics diagnostics)
    {
        this.settings = SettingsValidator.Clone(settings);
        scheduler = new(this.settings);
        this.notifications = notifications;
        statistics = stats;
        this.diagnostics = diagnostics;
        sessionProvider.Changed += OnSessionChanged;
        sessionProvider.Resumed += OnSessionResumed;
    }

    public event EventHandler? Changed;
    public SchedulerSnapshot Snapshot => scheduler.GetSnapshot();
    public string LastMessage { get; private set; } = "";
    public string? FeedbackKey { get; private set; }

    public bool ManualPresentation
    {
        get => manualPresentation;
        set
        {
            VerifyAccess();
            if (manualPresentation == value) return;
            manualPresentation = value;
            CancelPresentation();
            Poll();
        }
    }

    public void Start()
    {
        VerifyAccess();
        if (pollTask is not null) return;
        Poll();
        poller = new(TimeSpan.FromSeconds(15));
        pollTask = RunPollerAsync(poller, shutdown.Token);
    }

    public void ApplySettings(AppSettings newSettings)
    {
        VerifyAccess();
        var validated = SettingsValidator.Clone(newSettings);
        CancelPresentation();
        settings = validated;
        scheduler.UpdateSettings(settings);
        statistics.SetEnabled(settings.StatisticsEnabled);
        FeedbackKey = null;
        Poll();
    }

    public void Pause(TimeSpan? duration)
    {
        VerifyAccess();
        CancelPresentation();
        if (duration is { } finite) scheduler.PauseFor(finite);
        else scheduler.PauseIndefinitely();
        NotifyChanged();
    }

    public void PauseUntilTomorrow()
    {
        VerifyAccess();
        CancelPresentation();
        scheduler.PauseUntilTomorrow();
        NotifyChanged();
    }

    public void Resume()
    {
        VerifyAccess();
        CancelPresentation();
        scheduler.Resume();
        Poll();
    }

    public void Snooze()
    {
        VerifyAccess();
        CancelPresentation();
        scheduler.Snooze(TimeSpan.FromMinutes(5));
        NotifyChanged();
    }

    public void Dismiss(bool confirm = false)
    {
        VerifyAccess();
        if (confirm && presentationShown && presentationTask is { IsCompleted: false })
            statistics.Record(StatisticEvent.ExplicitConfirmation);
        CancelPresentation();
        NotifyChanged();
    }

    public Task PreviewAsync() => PreviewAsync(settings);

    public async Task PreviewAsync(AppSettings previewSettings)
    {
        VerifyAccess();
        if (previewPending) return;
        var previewSettingsSnapshot = SettingsValidator.Clone(previewSettings);
        previewPending = true;
        try
        {
            CancelPresentation();
            var pendingGeneration = presentationGeneration;
            if (presentationTask is { } previous) await previous;
            if (disposed || pendingGeneration != presentationGeneration) return;
            var session = ReadSession();
            if (!CanPreview(session))
            {
                FeedbackKey = "PreviewSuppressed";
                NotifyChanged();
                return;
            }
            var reminder = previewSettingsSnapshot.Blink;
            var request = new ReminderRequest(Guid.NewGuid(), ReminderKind.Blink, reminder.Message,
                TimeSpan.FromSeconds(reminder.DurationSeconds), reminder.ShowCountdown, previewSettingsSnapshot.Appearance.PresentationMode);
            isPreview = true;
            var generation = ++presentationGeneration;
            presentationTask = PresentAsync(request, previewSettingsSnapshot, preview: true, generation);
            await presentationTask;
        }
        finally
        {
            previewPending = false;
            if (!disposed) NotifyChanged();
        }
    }

    private async Task RunPollerAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (dispatcher.HasShutdownStarted) return;
                await dispatcher.InvokeAsync(Poll, DispatcherPriority.Background, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void Poll()
    {
        if (disposed) return;
        var session = ReadSession();
        // Explicit previews bypass interval/work/quiet/manual-pause rules, but never protected sessions.
        if (isPreview)
        {
            if (!CanPreview(session)) CancelPresentation();
            NotifyChanged();
            return;
        }

        var request = scheduler.Evaluate(session);
        if (scheduler.GetSnapshot().CurrentReminder is null && presentationTask is { IsCompleted: false })
            CancelPresentation();
        if (request is not null)
        {
            if (presentationTask is { IsCompleted: false } || previewPending)
                scheduler.ResetIntervals();
            else
            {
                var generation = ++presentationGeneration;
                presentationTask = PresentAsync(request, SettingsValidator.Clone(settings), preview: false, generation);
            }
        }
        NotifyChanged();
    }

    private CoreSession ReadSession()
    {
        try
        {
            WindowsSession state = sessionProvider.GetSnapshot(settings.PauseWhenIdle);
            var snapshot = new CoreSession(
                IsLocked: state.IsLocked,
                IsSuspended: state.IsSuspended,
                IsPresentationMode: manualPresentation || state.NotificationPermission == NotificationPermission.Presentation,
                IsUserNotificationSuppressed: state.NotificationPermission == NotificationPermission.Busy,
                IsNotificationStateUnknown: state.IsSecureDesktop || state.NotificationPermission == NotificationPermission.Unknown,
                IdleTime: state.Idle ?? TimeSpan.Zero,
                ResumedSinceLastCheck: resumed);
            resumed = false;
            return snapshot;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            FeedbackKey = "SessionStateUnavailable";
            // One event transition is enough; polling must not write a log line every 15 seconds.
            if (scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.UnknownSessionState) is false)
                diagnostics.Write("session.state_unavailable");
            return new(IsPresentationMode: manualPresentation, IsNotificationStateUnknown: true);
        }
    }

    private static bool CanPreview(CoreSession session) => !session.IsLocked && !session.IsSuspended
        && !session.IsPresentationMode && !session.IsUserNotificationSuppressed && !session.IsNotificationStateUnknown;

    private async Task PresentAsync(ReminderRequest request, AppSettings presentationSettings, bool preview, long generation)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        presentationCancellation = cancellation;
        presentationShown = false;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (!CanPreview(ReadSession())) return;
            if (request.PresentationMode == PresentationMode.NativeNotification)
            {
                var registration = notifications.Register();
                var result = registration.Status == NotificationSendStatus.Submitted
                    ? notifications.Show(Localizer.Current["AppName"], request.Message, presentationSettings.Appearance.SoundEnabled)
                    : registration;
                switch (result.Status)
                {
                    case NotificationSendStatus.Submitted:
                        LastMessage = request.Message;
                        FeedbackKey = null;
                        statistics.Record(StatisticEvent.NativeNotificationRequested);
                        break;
                    case NotificationSendStatus.Blocked:
                        FeedbackKey = "NativeNotificationBlocked";
                        break;
                    case NotificationSendStatus.Unavailable:
                        FeedbackKey = "NativeNotificationUnavailable";
                        diagnostics.Write("notification.unavailable");
                        break;
                    default:
                        FeedbackKey = "NativeNotificationFailed";
                        diagnostics.Write("notification.submit_failed");
                        break;
                }
                return;
            }

            var banner = new BannerWindow(request, presentationSettings.Appearance);
            void OnPresented(object? sender, EventArgs args)
            {
                if (generation != presentationGeneration || cancellation.IsCancellationRequested || disposed || presentationShown) return;
                presentationShown = true;
                LastMessage = request.Message;
                FeedbackKey = null;
                statistics.Record(StatisticEvent.BannerShown);
                NotifyChanged();
            }
            banner.Presented += OnPresented;
            try { await banner.ShowAsync(cancellation.Token); }
            finally { banner.Presented -= OnPresented; }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception
            or System.Runtime.InteropServices.COMException or System.Windows.Markup.XamlParseException)
        {
            FeedbackKey = "BannerFailed";
            diagnostics.Write("presentation.failed");
        }
        finally
        {
            if (ReferenceEquals(presentationCancellation, cancellation)) presentationCancellation = null;
            if (generation == presentationGeneration && !disposed)
            {
                presentationShown = false;
                isPreview = false;
                if (preview) scheduler.ResetIntervals();
                else scheduler.CompletePresentation();
                NotifyChanged();
            }
        }
    }

    private void CancelPresentation()
    {
        ++presentationGeneration;
        presentationCancellation?.Cancel();
        presentationShown = false;
        isPreview = false;
        scheduler.ResetIntervals();
    }

    private void OnSessionChanged(object? sender, EventArgs args) => DispatchSessionChange(false);
    private void OnSessionResumed(object? sender, EventArgs args) => DispatchSessionChange(true);

    private void DispatchSessionChange(bool wasResumed)
    {
        if (dispatcher.HasShutdownStarted) return;
        _ = dispatcher.InvokeAsync(() =>
        {
            if (disposed) return;
            resumed |= wasResumed;
            // Display changes are also routed here: dismiss a stale monitor placement before the next interval.
            CancelPresentation();
            Poll();
        });
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
    private void VerifyAccess()
    {
        dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        sessionProvider.Changed -= OnSessionChanged;
        sessionProvider.Resumed -= OnSessionResumed;
        sessionProvider.Dispose();
        ++presentationGeneration;
        presentationCancellation?.Cancel();
        await shutdown.CancelAsync();
        poller?.Dispose();
        if (pollTask is not null) await pollTask;
        if (presentationTask is not null) await presentationTask;
        shutdown.Dispose();
    }
}
