using BlinkReminder.Core;

namespace BlinkReminder.Core.Tests;

public sealed class ReminderSchedulerTests
{
    private static AppSettings Ready() => new() { OnboardingCompleted = true };

    [Fact]
    public void DefaultsRequireSetupAndExplicitConsent()
    {
        var defaults = new AppSettings();
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(defaults, clock);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Null(scheduler.Evaluate(new()));
        Assert.True(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.SetupIncomplete));
        Assert.False(defaults.StartWithWindows);
        Assert.False(defaults.StatisticsEnabled);
        Assert.False(defaults.PauseWhenIdle);
        Assert.False(defaults.Appearance.SoundEnabled);
        Assert.False(defaults.VisualBreak.Enabled);
    }

    [Fact]
    public void BlinkUsesMonotonicTimeRatherThanWallClockChanges()
    {
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(Ready(), clock);
        clock.ChangeWallClock(TimeSpan.FromDays(30));
        Assert.Null(scheduler.Evaluate(new()));
        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Null(scheduler.Evaluate(new()));
        clock.ChangeWallClock(TimeSpan.FromDays(-60));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(ReminderKind.Blink, scheduler.Evaluate(new())?.Kind);
    }

    [Fact]
    public void PresentationCannotOverlapAndCompletionStartsTheNextInterval()
    {
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(Ready(), clock);
        clock.Advance(TimeSpan.FromMinutes(10));
        var first = scheduler.Evaluate(new());
        Assert.NotNull(first);
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Null(scheduler.Evaluate(new()));
        Assert.Equal(first.Id, scheduler.GetSnapshot().CurrentReminder?.Id);
        scheduler.CompletePresentation();
        Assert.Null(scheduler.Evaluate(new()));
        Assert.Equal(TimeSpan.FromMinutes(10), scheduler.GetSnapshot().NextReminderIn);
    }

    [Fact]
    public void CoincidentRemindersPrioritizeVisualBreakAndDropTheBlinkBacklog()
    {
        var settings = Ready();
        settings.VisualBreak.Enabled = true;
        settings.VisualBreak.IntervalMinutes = 10;
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(settings, clock);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(ReminderKind.VisualBreak, scheduler.Evaluate(new())?.Kind);
        scheduler.CompletePresentation();
        Assert.Null(scheduler.Evaluate(new()));
        Assert.Equal(TimeSpan.FromMinutes(10), scheduler.GetSnapshot().NextReminderIn);
    }

    [Fact]
    public void ShortBlinkDoesNotRestartIndependentVisualBreakInterval()
    {
        var settings = Ready();
        settings.VisualBreak.Enabled = true;
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(settings, clock);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(ReminderKind.Blink, scheduler.Evaluate(new())?.Kind);
        clock.Advance(TimeSpan.FromSeconds(5));
        scheduler.CompletePresentation();
        clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(5));
        Assert.Equal(ReminderKind.VisualBreak, scheduler.Evaluate(new())?.Kind);
    }

    [Fact]
    public void UnlockCannotClearAnExistingManualPause()
    {
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(Ready(), clock);
        scheduler.PauseFor(TimeSpan.FromMinutes(30));
        scheduler.Evaluate(new(IsLocked: true));
        Assert.True(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.Manual | PauseReason.Locked));
        clock.Advance(TimeSpan.FromMinutes(15));
        scheduler.Evaluate(new());
        Assert.True(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.Manual));
        Assert.False(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.Locked));
        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Null(scheduler.Evaluate(new()));
        Assert.Equal(TimeSpan.FromMinutes(10), scheduler.GetSnapshot().NextReminderIn);
    }

    [Fact]
    public void ManualPauseDurationAlsoUsesMonotonicTime()
    {
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(Ready(), clock);
        scheduler.PauseFor(TimeSpan.FromMinutes(15));
        clock.ChangeWallClock(TimeSpan.FromDays(1));
        scheduler.Evaluate(new());
        Assert.True(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.Manual));
        clock.Advance(TimeSpan.FromMinutes(15));
        scheduler.Evaluate(new());
        Assert.False(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.Manual));
    }

    [Fact]
    public void ManualPauseUntilTomorrowUsesLocalDate()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test offset", TimeSpan.FromHours(-4), "Test", "Test");
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 15, 3, 50, 0, TimeSpan.Zero), zone);
        var scheduler = new ReminderScheduler(Ready(), clock);
        scheduler.PauseUntilTomorrow();
        clock.Advance(TimeSpan.FromMinutes(9));
        scheduler.Evaluate(new());
        Assert.True(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.Manual));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(scheduler.Evaluate(new()));
        Assert.False(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.Manual));
        Assert.Equal(TimeSpan.FromMinutes(10), scheduler.GetSnapshot().NextReminderIn);
    }

    [Fact]
    public void ManualAndSnoozeReasonsCoexist()
    {
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(Ready(), clock);
        scheduler.PauseIndefinitely();
        scheduler.Snooze(TimeSpan.FromMinutes(5));
        Assert.True(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.Manual | PauseReason.Snoozed));
        clock.Advance(TimeSpan.FromMinutes(5));
        scheduler.Evaluate(new());
        Assert.True(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.Manual));
        Assert.False(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.Snoozed));
        scheduler.Resume();
        Assert.Null(scheduler.Evaluate(new()));
    }

    [Fact]
    public void ResumeEventDiscardsAnOverdueReminderEvenWithoutPriorSuspendPoll()
    {
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(Ready(), clock);
        clock.Advance(TimeSpan.FromHours(3));
        Assert.Null(scheduler.Evaluate(new(ResumedSinceLastCheck: true)));
        Assert.Equal(TimeSpan.FromMinutes(10), scheduler.GetSnapshot().NextReminderIn);
    }

    [Fact]
    public void ResumeEventAlsoCancelsAnInterruptedPresentation()
    {
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(Ready(), clock);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.NotNull(scheduler.Evaluate(new()));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(scheduler.Evaluate(new(ResumedSinceLastCheck: true)));
        Assert.Null(scheduler.GetSnapshot().CurrentReminder);
        Assert.Equal(TimeSpan.FromMinutes(10), scheduler.GetSnapshot().NextReminderIn);
    }

    [Fact]
    public void ChangedSettingsAreClonedAndReplaceAnExistingInterval()
    {
        var settings = Ready();
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(settings, clock);
        clock.Advance(TimeSpan.FromMinutes(9));
        settings.Blink.IntervalMinutes = 3;
        scheduler.UpdateSettings(settings);
        settings.Blink.IntervalMinutes = 1;
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(scheduler.Evaluate(new()));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(ReminderKind.Blink, scheduler.Evaluate(new())?.Kind);
    }

    [Fact]
    public void WorkScheduleOverMidnightBelongsToTheStartingWeekday()
    {
        var settings = Ready();
        settings.Schedule.Enabled = true;
        settings.Schedule.Days = [DayOfWeek.Monday];
        settings.Schedule.Start = new(22, 0);
        settings.Schedule.End = new(2, 0);
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 14, 23, 50, 0, TimeSpan.Zero));
        var scheduler = new ReminderScheduler(settings, clock);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(ReminderKind.Blink, scheduler.Evaluate(new())?.Kind);
        scheduler.CompletePresentation();
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Null(scheduler.Evaluate(new()));
        Assert.True(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.OutsideSchedule));
    }

    [Fact]
    public void QuietPeriodOverMidnightRestartsFullIntervalWhenItEnds()
    {
        var settings = Ready();
        settings.Schedule.QuietPeriods.Add(new() { Days = [DayOfWeek.Monday], Start = new(23, 0), End = new(1, 0) });
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 14, 23, 50, 0, TimeSpan.Zero));
        var scheduler = new ReminderScheduler(settings, clock);
        scheduler.Evaluate(new());
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Null(scheduler.Evaluate(new()));
        Assert.True(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.QuietPeriod));
        clock.Advance(TimeSpan.FromMinutes(40));
        Assert.Null(scheduler.Evaluate(new()));
        Assert.Equal(TimeSpan.FromMinutes(10), scheduler.GetSnapshot().NextReminderIn);
    }

    [Fact]
    public void EnteringWorkScheduleDoesNotEmitAccumulatedReminders()
    {
        var settings = Ready();
        settings.Schedule.Enabled = true;
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero));
        var scheduler = new ReminderScheduler(settings, clock);
        scheduler.Evaluate(new());
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(scheduler.Evaluate(new()));
        Assert.Equal(TimeSpan.FromMinutes(10), scheduler.GetSnapshot().NextReminderIn);
    }

    [Fact]
    public void IdlePauseIsOptInAndCollectsNoInputHistory()
    {
        var settings = Ready();
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(settings, clock);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.NotNull(scheduler.Evaluate(new(IdleTime: TimeSpan.FromHours(1))));
        settings.PauseWhenIdle = true;
        scheduler.UpdateSettings(settings);
        scheduler.Evaluate(new(IdleTime: TimeSpan.FromMinutes(5)));
        Assert.True(scheduler.GetSnapshot().Reasons.HasFlag(PauseReason.Idle));
        Assert.Null(scheduler.GetSnapshot().CurrentReminder);
    }

    [Theory]
    [InlineData(PauseReason.Locked)]
    [InlineData(PauseReason.Suspended)]
    [InlineData(PauseReason.Presentation)]
    [InlineData(PauseReason.WindowsSuppression)]
    [InlineData(PauseReason.UnknownSessionState)]
    public void UnsafeSessionStateCancelsCurrentPresentation(PauseReason reason)
    {
        var settings = Ready();
        settings.PauseOnLockOrSuspend = false;
        var clock = new FakeClock();
        var scheduler = new ReminderScheduler(settings, clock);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.NotNull(scheduler.Evaluate(new()));
        scheduler.Evaluate(new(IsLocked: reason == PauseReason.Locked,
            IsSuspended: reason == PauseReason.Suspended, IsPresentationMode: reason == PauseReason.Presentation,
            IsUserNotificationSuppressed: reason == PauseReason.WindowsSuppression,
            IsNotificationStateUnknown: reason == PauseReason.UnknownSessionState));
        Assert.Null(scheduler.GetSnapshot().CurrentReminder);
        Assert.True(scheduler.GetSnapshot().Reasons.HasFlag(reason));
    }
}
