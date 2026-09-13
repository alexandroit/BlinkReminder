namespace BlinkReminder.Core;

[Flags]
public enum PauseReason
{
    None = 0,
    SetupIncomplete = 1,
    Manual = 2,
    Snoozed = 4,
    OutsideSchedule = 8,
    QuietPeriod = 16,
    Locked = 32,
    Suspended = 64,
    Presentation = 128,
    WindowsSuppression = 256,
    UnknownSessionState = 512,
    Idle = 1024,
    Presenting = 2048,
    RemindersDisabled = 4096
}

public sealed record SessionSnapshot(
    bool IsLocked = false,
    bool IsSuspended = false,
    bool IsPresentationMode = false,
    bool IsUserNotificationSuppressed = false,
    bool IsNotificationStateUnknown = false,
    TimeSpan IdleTime = default,
    bool ResumedSinceLastCheck = false);

public sealed record ReminderRequest(Guid Id, ReminderKind Kind, string Message, TimeSpan Duration,
    bool ShowCountdown, PresentationMode PresentationMode);

public sealed record SchedulerSnapshot(PauseReason Reasons, TimeSpan? NextReminderIn,
    ReminderRequest? CurrentReminder, DateTimeOffset? ManualPauseUntil, DateTimeOffset? SnoozedUntil);

/// <summary>One host poller calls Evaluate; the scheduler never creates timers or wakes the computer.</summary>
public sealed class ReminderScheduler
{
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private AppSettings settings;
    private long blinkStarted;
    private long breakStarted;
    private long? manualStarted;
    private TimeSpan? manualDuration;
    private DateOnly? manualThroughDate;
    private bool manualIndefinite;
    private long? snoozeStarted;
    private TimeSpan snoozeDuration;
    private bool wasSuppressed;
    private ReminderRequest? current;
    private SessionSnapshot session = new();

    public ReminderScheduler(AppSettings settings, TimeProvider? timeProvider = null)
    {
        this.settings = SettingsValidator.Clone(settings);
        clock = timeProvider ?? TimeProvider.System;
        ResetIntervalsCore();
    }

    public ReminderRequest? Evaluate(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (gate)
        {
            session = snapshot;
            if (snapshot.ResumedSinceLastCheck)
            {
                current = null;
                ResetIntervalsCore();
            }
            var reasons = GetReasons();
            var suppressed = (reasons & ~PauseReason.Presenting) != PauseReason.None;
            if (suppressed)
            {
                // Keep all independent pause reasons; session recovery must never clear a manual pause.
                current = null;
                wasSuppressed = true;
                return null;
            }
            if (wasSuppressed)
            {
                ResetIntervalsCore();
                wasSuppressed = false;
            }
            if (current is not null) return null;

            if (settings.VisualBreak.Enabled && Due(breakStarted, settings.VisualBreak.IntervalMinutes))
            {
                // A visual break also satisfies a coincident blink reminder. Nothing is queued behind it.
                ResetIntervalsCore();
                return current = Create(ReminderKind.VisualBreak, settings.VisualBreak);
            }
            if (settings.Blink.Enabled && Due(blinkStarted, settings.Blink.IntervalMinutes))
            {
                blinkStarted = clock.GetTimestamp();
                return current = Create(ReminderKind.Blink, settings.Blink);
            }
            return null;
        }
    }

    public void PauseFor(TimeSpan duration)
    {
        ValidateDuration(duration);
        lock (gate)
        {
            ClearManual();
            manualStarted = clock.GetTimestamp();
            manualDuration = duration;
            BeginSuppression();
        }
    }

    public void PauseIndefinitely()
    {
        lock (gate)
        {
            ClearManual();
            manualIndefinite = true;
            BeginSuppression();
        }
    }

    public void PauseUntilTomorrow()
    {
        lock (gate)
        {
            ClearManual();
            manualThroughDate = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            BeginSuppression();
        }
    }

    public void Resume()
    {
        lock (gate)
        {
            ClearManual();
            snoozeStarted = null;
            current = null;
            ResetIntervalsCore();
        }
    }

    public void Snooze(TimeSpan duration)
    {
        ValidateDuration(duration);
        lock (gate)
        {
            snoozeStarted = clock.GetTimestamp();
            snoozeDuration = duration;
            BeginSuppression();
        }
    }

    public void ResetIntervals()
    {
        lock (gate)
        {
            current = null;
            ResetIntervalsCore();
        }
    }

    public void CompletePresentation()
    {
        lock (gate)
        {
            if (current is null) return;
            // Preserve the independent break cadence when a short blink banner finishes.
            // If the other reminder became overdue during presentation, discard that backlog.
            if (current.Kind == ReminderKind.VisualBreak) ResetIntervalsCore();
            else
            {
                blinkStarted = clock.GetTimestamp();
                if (Due(breakStarted, settings.VisualBreak.IntervalMinutes)) breakStarted = blinkStarted;
            }
            current = null;
        }
    }

    public void UpdateSettings(AppSettings newSettings)
    {
        var validated = SettingsValidator.Clone(newSettings);
        lock (gate)
        {
            settings = validated;
            current = null;
            ResetIntervalsCore();
        }
    }

    public SchedulerSnapshot GetSnapshot()
    {
        lock (gate)
        {
            var reasons = GetReasons();
            TimeSpan? next = null;
            if (reasons == PauseReason.None)
            {
                if (settings.Blink.Enabled) next = Remaining(blinkStarted, TimeSpan.FromMinutes(settings.Blink.IntervalMinutes));
                if (settings.VisualBreak.Enabled)
                {
                    var remaining = Remaining(breakStarted, TimeSpan.FromMinutes(settings.VisualBreak.IntervalMinutes));
                    if (next is null || remaining < next) next = remaining;
                }
            }
            var localNow = clock.GetLocalNow();
            DateTimeOffset? until = manualStarted is { } start && manualDuration is { } duration
                ? localNow + Remaining(start, duration) : null;
            if (manualThroughDate is { } date)
            {
                var midnight = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
                // The date rule itself does not depend on this estimated display value, including on DST changes.
                until = new DateTimeOffset(midnight, clock.LocalTimeZone.GetUtcOffset(midnight));
            }
            var snoozedUntil = snoozeStarted is { } snooze ? localNow + Remaining(snooze, snoozeDuration) : (DateTimeOffset?)null;
            return new(reasons, next, current, until, snoozedUntil);
        }
    }

    private PauseReason GetReasons()
    {
        var localNow = clock.GetLocalNow();
        if (manualStarted is { } start && manualDuration is { } duration && Remaining(start, duration) == TimeSpan.Zero)
            ClearManual();
        if (manualThroughDate is { } date && DateOnly.FromDateTime(localNow.DateTime) > date) ClearManual();
        if (snoozeStarted is { } snooze && Remaining(snooze, snoozeDuration) == TimeSpan.Zero) snoozeStarted = null;

        var reason = PauseReason.None;
        if (!settings.OnboardingCompleted) reason |= PauseReason.SetupIncomplete;
        if (!settings.Blink.Enabled && !settings.VisualBreak.Enabled) reason |= PauseReason.RemindersDisabled;
        if (manualIndefinite || manualStarted is not null || manualThroughDate is not null) reason |= PauseReason.Manual;
        if (snoozeStarted is not null) reason |= PauseReason.Snoozed;
        if (settings.Schedule.Enabled && !InPeriod(localNow, settings.Schedule.Days, settings.Schedule.Start, settings.Schedule.End))
            reason |= PauseReason.OutsideSchedule;
        if (settings.Schedule.QuietPeriods.Any(q => q.Enabled && InPeriod(localNow, q.Days, q.Start, q.End)))
            reason |= PauseReason.QuietPeriod;
        // Protected desktops are always suppressed. The preference controls interval resetting in the host,
        // never permission to draw over a lock screen or suspended session.
        if (session.IsLocked) reason |= PauseReason.Locked;
        if (session.IsSuspended) reason |= PauseReason.Suspended;
        if (session.IsPresentationMode) reason |= PauseReason.Presentation;
        if (session.IsUserNotificationSuppressed) reason |= PauseReason.WindowsSuppression;
        if (session.IsNotificationStateUnknown) reason |= PauseReason.UnknownSessionState;
        if (settings.PauseWhenIdle && session.IdleTime >= TimeSpan.FromMinutes(settings.IdleMinutes)) reason |= PauseReason.Idle;
        if (current is not null) reason |= PauseReason.Presenting;
        return reason;
    }

    private static bool InPeriod(DateTimeOffset localNow, DayOfWeek[] days, TimeOnly start, TimeOnly end)
    {
        var time = TimeOnly.FromDateTime(localNow.DateTime);
        if (start == end) return days.Contains(localNow.DayOfWeek);
        if (start < end) return days.Contains(localNow.DayOfWeek) && time >= start && time < end;
        if (time >= start) return days.Contains(localNow.DayOfWeek);
        return time < end && days.Contains(localNow.AddDays(-1).DayOfWeek);
    }

    private ReminderRequest Create(ReminderKind kind, ReminderSettings reminder) => new(Guid.NewGuid(), kind,
        reminder.Message, TimeSpan.FromSeconds(reminder.DurationSeconds), reminder.ShowCountdown,
        settings.Appearance.PresentationMode);

    private bool Due(long started, int minutes) => clock.GetElapsedTime(started) >= TimeSpan.FromMinutes(minutes);
    private TimeSpan Remaining(long started, TimeSpan duration) => TimeSpan.FromTicks(
        Math.Max(0, (duration - clock.GetElapsedTime(started)).Ticks));
    private void ResetIntervalsCore() => blinkStarted = breakStarted = clock.GetTimestamp();
    private void BeginSuppression() { current = null; wasSuppressed = true; }
    private void ClearManual() { manualStarted = null; manualDuration = null; manualThroughDate = null; manualIndefinite = false; }
    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be positive and no longer than seven days.");
    }
}
