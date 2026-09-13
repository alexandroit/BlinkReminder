using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlinkReminder.Core;

public sealed class SettingsValidationException(string message) : Exception(message);

public static class SettingsValidator
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static void Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Require(settings.SchemaVersion == AppSettings.CurrentSchemaVersion, "Unsupported settings version.");
        Require(settings.Language is "pt-BR" or "en" or "fr-CA", "Unsupported language.");
        Reminder(settings.Blink, "blink");
        Reminder(settings.VisualBreak, "visualBreak");
        Require(settings.Appearance is not null, "Appearance settings are missing.");
        var appearance = settings.Appearance!;
        Require(Enum.IsDefined(appearance.PresentationMode) && Enum.IsDefined(appearance.Position)
            && Enum.IsDefined(appearance.Monitor) && Enum.IsDefined(appearance.Theme), "Invalid appearance choice.");
        Require(appearance.MonitorDeviceName is not null && appearance.MonitorDeviceName.Length <= 256,
            "Monitor name is too long.");
        Require(appearance.Margin is >= 0 and <= 200, "Banner margin must be between 0 and 200.");
        Require(appearance.FontSize is >= 12 and <= 36, "Font size must be between 12 and 36.");
        Require(settings.IdleMinutes is >= 1 and <= 240, "Idle threshold must be between 1 and 240 minutes.");
        Require(settings.Schedule is not null, "Schedule settings are missing.");
        var schedule = settings.Schedule!;
        Days(schedule.Days);
        Require(schedule.QuietPeriods is not null && schedule.QuietPeriods.Count <= 16,
            "At most 16 quiet periods are supported.");
        foreach (var quiet in schedule.QuietPeriods!)
        {
            Require(quiet is not null, "A quiet period is missing.");
            Days(quiet!.Days);
        }
    }

    public static AppSettings Clone(AppSettings settings)
    {
        Validate(settings);
        return JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions)!;
    }

    private static void Reminder(ReminderSettings? reminder, string name)
    {
        Require(reminder is not null, $"Missing {name} settings.");
        Require(reminder!.IntervalMinutes is >= 1 and <= 1440, $"{name} interval must be between 1 and 1440 minutes.");
        Require(reminder.DurationSeconds is >= 2 and <= 600, $"{name} duration must be between 2 and 600 seconds.");
        Require(!string.IsNullOrWhiteSpace(reminder.Message) && reminder.Message.Length <= 240,
            $"{name} message must contain between 1 and 240 characters.");
        Require(!reminder.Message.Any(c => char.IsControl(c) && c is not '\n' and not '\r'),
            $"{name} message contains unsupported control characters.");
    }

    private static void Days(DayOfWeek[]? days) => Require(
        days is { Length: > 0 and <= 7 } && days.All(Enum.IsDefined) && days.Distinct().Count() == days.Length,
        "Choose between one and seven different weekdays.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new SettingsValidationException(message);
    }
}
