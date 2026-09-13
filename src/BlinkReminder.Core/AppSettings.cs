using System.Text.Json.Serialization;

namespace BlinkReminder.Core;

public enum ReminderKind { Blink, VisualBreak }
public enum PresentationMode { Banner, NativeNotification }
public enum BannerPosition { TopCenter, TopLeft, TopRight }
public enum MonitorSelection { ActiveWindow, Primary, Selected }
public enum AppTheme { System, Light, Dark }

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public bool OnboardingCompleted { get; set; }
    public string Language { get; set; } = "pt-BR";
    public ReminderSettings Blink { get; set; } = new();
    public ReminderSettings VisualBreak { get; set; } = new()
    {
        Enabled = false,
        IntervalMinutes = 20,
        DurationSeconds = 20,
        Message = "Faça uma pequena pausa visual.",
        ShowCountdown = true
    };
    public AppearanceSettings Appearance { get; set; } = new();
    public ScheduleSettings Schedule { get; set; } = new();
    public bool StartWithWindows { get; set; }
    public bool PauseOnLockOrSuspend { get; set; } = true;
    public bool PauseWhenIdle { get; set; }
    public int IdleMinutes { get; set; } = 5;
    public bool StatisticsEnabled { get; set; }
}

public sealed class ReminderSettings
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 10;
    public int DurationSeconds { get; set; } = 5;
    public string Message { get; set; } = "Lembrete: pisque os olhos suavemente.";
    public bool ShowCountdown { get; set; }
}

public sealed class AppearanceSettings
{
    public PresentationMode PresentationMode { get; set; }
    public BannerPosition Position { get; set; }
    public MonitorSelection Monitor { get; set; }
    public string MonitorDeviceName { get; set; } = "";
    public int Margin { get; set; } = 20;
    public int FontSize { get; set; } = 18;
    public AppTheme Theme { get; set; }
    public bool AnimationsEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; }
}

public sealed class ScheduleSettings
{
    public bool Enabled { get; set; }
    public DayOfWeek[] Days { get; set; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
    public TimeOnly Start { get; set; } = new(9, 0);
    public TimeOnly End { get; set; } = new(17, 0);
    public List<QuietPeriod> QuietPeriods { get; set; } = [];
}

public sealed class QuietPeriod
{
    public bool Enabled { get; set; } = true;
    public DayOfWeek[] Days { get; set; } = Enum.GetValues<DayOfWeek>();
    public TimeOnly Start { get; set; } = new(12, 0);
    public TimeOnly End { get; set; } = new(13, 0);
}
