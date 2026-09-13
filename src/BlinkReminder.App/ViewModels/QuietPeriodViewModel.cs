using System.Collections.ObjectModel;
using System.Globalization;
using BlinkReminder.Core;

namespace BlinkReminder.App.ViewModels;

public sealed class QuietPeriodViewModel
{
    public bool Enabled { get; set; }
    public string Start { get; set; }
    public string End { get; set; }
    public ObservableCollection<DayChoice> Days { get; }
    public RelayCommand RemoveCommand { get; }

    public QuietPeriodViewModel(QuietPeriod period, Action<QuietPeriodViewModel> remove)
    {
        Enabled = period.Enabled;
        Start = period.Start.ToString("HH:mm", CultureInfo.InvariantCulture);
        End = period.End.ToString("HH:mm", CultureInfo.InvariantCulture);
        Days = new(MainViewModel.Weekdays.Select(day => new DayChoice(day, period.Days.Contains(day))));
        RemoveCommand = new(() => remove(this));
    }

    public QuietPeriod ToSettings() => new()
    {
        Enabled = Enabled,
        Start = ParseTime(Start),
        End = ParseTime(End),
        Days = Days.Where(day => day.Selected).Select(day => day.Value).ToArray()
    };

    internal static TimeOnly ParseTime(string text) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time : throw new SettingsValidationException("Invalid local time.");
}
