using BlinkReminder.Core;

namespace BlinkReminder.Core.Tests;

public sealed class PrivacyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "BlinkReminder-privacy-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DisabledStatisticsCreateNoFilesAndRecordNothing()
    {
        var stats = new LocalStatistics(directory);
        stats.Record(StatisticEvent.BannerShown);
        Assert.Equal(new StatisticsSnapshot(0, 0, 0), stats.GetSnapshot());
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void StatisticsDistinguishRequestsFromShownBannersAndExplicitConfirmations()
    {
        var stats = new LocalStatistics(directory, enabled: true);
        stats.Record(StatisticEvent.NativeNotificationRequested);
        Assert.Equal(new StatisticsSnapshot(0, 1, 0), stats.GetSnapshot());
        stats.Record(StatisticEvent.BannerShown);
        stats.Record(StatisticEvent.ExplicitConfirmation);
        stats.SetEnabled(false);
        stats.Record(StatisticEvent.BannerShown);
        Assert.Equal(new StatisticsSnapshot(1, 1, 1), new LocalStatistics(directory, enabled: true).GetSnapshot());
        stats.Clear();
        Assert.Equal(new StatisticsSnapshot(0, 0, 0), stats.GetSnapshot());
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void ReenablingStatisticsPreservesPreviouslyConsentedCounters()
    {
        var firstSession = new LocalStatistics(directory, enabled: true);
        firstSession.Record(StatisticEvent.BannerShown);
        var secondSession = new LocalStatistics(directory);
        Assert.Equal(new StatisticsSnapshot(0, 0, 0), secondSession.GetSnapshot());
        secondSession.SetEnabled(true);
        secondSession.Record(StatisticEvent.NativeNotificationRequested);
        Assert.Equal(new StatisticsSnapshot(1, 1, 0), secondSession.GetSnapshot());
    }

    [Fact]
    public void DiagnosticsAreBoundedRotatedAndRejectFreeformContent()
    {
        var log = new LocalDiagnostics(directory, new FakeClock());
        Assert.Throws<ArgumentException>(() => log.Write("A window title or other personal text"));
        for (var i = 0; i < 3000; i++) log.Write("notification.registration_failed");
        var logs = Directory.GetFiles(directory);
        Assert.Equal(2, logs.Length);
        Assert.All(logs, file => Assert.InRange(new FileInfo(file).Length, 1, LocalDiagnostics.MaximumLogBytes));
        log.Clear();
        Assert.Empty(Directory.GetFiles(directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
