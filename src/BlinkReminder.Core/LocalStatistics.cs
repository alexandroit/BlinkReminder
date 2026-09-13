using System.Text.Json;

namespace BlinkReminder.Core;

public enum StatisticEvent { BannerShown, NativeNotificationRequested, ExplicitConfirmation }
public sealed record StatisticsSnapshot(long BannersShown, long NativeNotificationRequests, long ExplicitConfirmations);

/// <summary>Opt-in aggregate counters only. A notification request is never counted as seen or as an actual blink.</summary>
public sealed class LocalStatistics
{
    private readonly object gate = new();
    private readonly string filePath;
    private StatisticsSnapshot counts = new(0, 0, 0);
    private bool enabled;

    public LocalStatistics(string directory, bool enabled = false)
    {
        filePath = Path.Combine(Path.GetFullPath(directory), "statistics.json");
        this.enabled = enabled;
        if (enabled) ReadSavedCounts();
    }

    private void ReadSavedCounts()
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<StatisticsSnapshot>(AtomicFile.ReadBounded(filePath, 4096));
            if (saved is not null && saved.BannersShown >= 0 && saved.NativeNotificationRequests >= 0 && saved.ExplicitConfirmations >= 0)
                counts = saved;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or SettingsValidationException)
        {
            // Optional statistics must never prevent reminders from starting.
        }
    }

    public void SetEnabled(bool value)
    {
        lock (gate)
        {
            if (value && !enabled) ReadSavedCounts();
            enabled = value;
        }
    }
    public StatisticsSnapshot GetSnapshot() { lock (gate) return counts; }

    public void Record(StatisticEvent statistic)
    {
        lock (gate)
        {
            if (!enabled) return;
            counts = statistic switch
            {
                StatisticEvent.BannerShown => counts with { BannersShown = Increment(counts.BannersShown) },
                StatisticEvent.NativeNotificationRequested => counts with { NativeNotificationRequests = Increment(counts.NativeNotificationRequests) },
                StatisticEvent.ExplicitConfirmation => counts with { ExplicitConfirmations = Increment(counts.ExplicitConfirmations) },
                _ => throw new ArgumentOutOfRangeException(nameof(statistic))
            };
            try { AtomicFile.Write(filePath, JsonSerializer.SerializeToUtf8Bytes(counts)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            counts = new(0, 0, 0);
        }
    }

    private static long Increment(long value) => value == long.MaxValue ? value : value + 1;
}
