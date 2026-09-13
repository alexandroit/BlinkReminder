namespace BlinkReminder.Core.Tests;

internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset utc;
    private long timestamp;
    private readonly TimeZoneInfo zone;

    public FakeClock(DateTimeOffset? start = null, TimeZoneInfo? zone = null)
    {
        utc = start ?? new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
        this.zone = zone ?? TimeZoneInfo.Utc;
    }

    public override DateTimeOffset GetUtcNow() => utc;
    public override long GetTimestamp() => timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override TimeZoneInfo LocalTimeZone => zone;
    public void Advance(TimeSpan elapsed) { utc += elapsed; timestamp += elapsed.Ticks; }
    public void ChangeWallClock(TimeSpan delta) => utc += delta;
}
