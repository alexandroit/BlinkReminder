using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using BlinkReminder.App.Services;
using BlinkReminder.Core;
using BlinkReminder.Windows;
using Microsoft.Win32;
using BannerPosition = BlinkReminder.Core.BannerPosition;

namespace BlinkReminder.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
    private readonly JsonSettingsStore store;
    private readonly ReminderHost host;
    private readonly LocalStatistics statistics;
    private readonly LocalDiagnostics diagnostics;
    private readonly WindowsStartupAdapter startup;
    private AppSettings settings;
    private string feedback = "";
    private string startupStatus = "";
    public AppSettings Settings => settings;
    public string Feedback { get => feedback; set => Set(ref feedback, value); }
    public string StartupStatus { get => startupStatus; private set => Set(ref startupStatus, value); }
    public string BlinkInterval { get; set; } = "";
    public string BlinkDuration { get; set; } = "";
    public string BreakInterval { get; set; } = "";
    public string BreakDuration { get; set; } = "";
    public string BannerMargin { get; set; } = "";
    public string BannerFontSize { get; set; } = "";
    public string IdleThreshold { get; set; } = "";
    public string StartTime { get; set; } = "09:00";
    public string EndTime { get; set; } = "17:00";
    public ObservableCollection<DayChoice> Days { get; private set; } = [];
    public ObservableCollection<QuietPeriodViewModel> QuietPeriods { get; private set; } = [];
    public IReadOnlyList<string> Monitors { get; } = WindowPlacement.GetMonitors().Select(m => m.DeviceName).ToArray();
    public Choice<string>[] Languages { get; } = [new("pt-BR", "Português (Brasil)"), new("en", "English"), new("fr-CA", "Français (Canada)")];
    public Choice<PresentationMode>[] PresentationModes => [new(PresentationMode.Banner, T["ModeBanner"]), new(PresentationMode.NativeNotification, T["ModeNative"])];
    public Choice<BannerPosition>[] Positions => [new(BannerPosition.TopCenter, T["TopCenter"]), new(BannerPosition.TopLeft, T["TopLeft"]), new(BannerPosition.TopRight, T["TopRight"])];
    public Choice<MonitorSelection>[] MonitorModes => [new(MonitorSelection.ActiveWindow, T["MonitorActive"]), new(MonitorSelection.Primary, T["MonitorPrimary"]), new(MonitorSelection.Selected, T["MonitorSelected"])];
    public Choice<AppTheme>[] Themes => [new(AppTheme.System, T["ThemeSystem"]), new(AppTheme.Light, T["ThemeLight"]), new(AppTheme.Dark, T["ThemeDark"])];
    private static Localizer T => Localizer.Current;
    public bool ManualPresentation { get => host.ManualPresentation; set { host.ManualPresentation = value; Notify(); Refresh(); } }
    public string Status
    {
        get
        {
            var reasons = host.Snapshot.Reasons;
            var labels = Enum.GetValues<PauseReason>().Where(reason => reason != PauseReason.None && reason != PauseReason.Presenting && reasons.HasFlag(reason))
                .Select(reason => T["Pause_" + reason]).ToArray();
            return labels.Length > 0 ? string.Join(" · ", labels) : reasons.HasFlag(PauseReason.Presenting) ? T["Presenting"] : T["Ready"];
        }
    }
    public string NextReminder => host.Snapshot.NextReminderIn is { } remaining ? $"{Math.Ceiling(remaining.TotalMinutes):0} min" : T["NotScheduled"];
    public string LastMessage => string.IsNullOrEmpty(host.LastMessage) ? T["None"] : host.LastMessage;
    public string Counts { get { var count = statistics.GetSnapshot(); return T.Format("CountFormat", count.BannersShown, count.NativeNotificationRequests, count.ExplicitConfirmations); } }
    public string Language
    {
        get => settings.Language;
        set
        {
            if (settings.Language == value || value is null) return;
            bool defaultBlink = settings.Blink.Message == T["DefaultBlink"];
            bool defaultBreak = settings.VisualBreak.Message == T["DefaultBreak"];
            settings.Language = value;
            T.SetLanguage(value);
            if (defaultBlink) settings.Blink.Message = T["DefaultBlink"];
            if (defaultBreak) settings.VisualBreak.Message = T["DefaultBreak"];
            Notify(nameof(Settings));
            RefreshChoices();
            Notify();
        }
    }

    public AsyncCommand PreviewCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand SnoozeCommand { get; }
    public RelayCommand DismissCommand { get; }
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand AddQuietCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ExportDiagnosticsCommand { get; }
    public RelayCommand ClearDataCommand { get; }
    public AsyncCommand ResetCommand { get; }
    public RelayCommand SourceCommand { get; }
    public RelayCommand ReleasesCommand { get; }

    public MainViewModel(AppSettings initial, JsonSettingsStore store, ReminderHost host,
        LocalStatistics statistics, LocalDiagnostics diagnostics, WindowsStartupAdapter startup, Action exit)
    {
        this.store = store;
        this.host = host;
        this.statistics = statistics;
        this.diagnostics = diagnostics;
        this.startup = startup;
        settings = SettingsValidator.Clone(initial);
        LoadEditor(settings);
        PreviewCommand = new(() => host.PreviewAsync(ReadEditor()), ReportError);
        PauseCommand = new(() => host.Pause(TimeSpan.FromMinutes(15)));
        ResumeCommand = new(host.Resume);
        SnoozeCommand = new(host.Snooze);
        DismissCommand = new(() => host.Dismiss());
        ConfirmCommand = new(() => host.Dismiss(confirm: true));
        ExitCommand = new(exit);
        AddQuietCommand = new(() => { if (QuietPeriods.Count < 16) QuietPeriods.Add(new(new QuietPeriod(), RemoveQuiet)); });
        ImportCommand = new(() => RunFileAction(Import));
        ExportCommand = new(() => RunFileAction(Export));
        ExportDiagnosticsCommand = new(() => RunFileAction(ExportDiagnostics));
        ClearDataCommand = new(() => RunFileAction(() => { statistics.Clear(); diagnostics.Clear(); Feedback = T["Done"]; Refresh(); }));
        ResetCommand = new(ResetAsync, ReportError);
        SourceCommand = new(() => OpenLink("https://github.com/alexandroit/BlinkReminder"));
        ReleasesCommand = new(() => OpenLink("https://github.com/alexandroit/BlinkReminder/releases"));
        host.Changed += (_, _) => { if (host.FeedbackKey is { } key) Feedback = T[key]; Refresh(); };
    }

    public void Refresh()
    {
        Notify(nameof(Status)); Notify(nameof(NextReminder)); Notify(nameof(LastMessage)); Notify(nameof(Counts));
    }

    private void RefreshChoices()
    {
        Notify(nameof(PresentationModes)); Notify(nameof(Positions)); Notify(nameof(MonitorModes)); Notify(nameof(Themes));
        foreach (var day in Days.Concat(QuietPeriods.SelectMany(period => period.Days))) day.RefreshLabel();
        Refresh();
    }

    public async Task RefreshStartupAsync()
    {
        var state = await startup.GetStateAsync();
        StartupStatus = T[state == StartupRegistrationState.Enabled ? "StartupEnabled" : state == StartupRegistrationState.Disabled ? "StartupDisabled" : "StartupRestricted"];
    }

    public async Task SaveAsync()
    {
        try
        {
            AppSettings candidate = ReadEditor();
            candidate.OnboardingCompleted = true;
            store.Save(candidate);
            var state = await startup.SetEnabledAsync(candidate.StartWithWindows, explicitConsent: candidate.StartWithWindows);
            bool startupDenied = candidate.StartWithWindows && state != StartupRegistrationState.Enabled;
            candidate.StartWithWindows = state == StartupRegistrationState.Enabled;
            store.Save(candidate);
            statistics.SetEnabled(candidate.StatisticsEnabled);
            host.ApplySettings(candidate);
            ThemeService.Apply(candidate.Appearance.Theme);
            LoadEditor(candidate);
            diagnostics.Write("settings.saved");
            Feedback = T[startupDenied ? "StartupBlocked" : "Saved"];
            await RefreshStartupAsync();
        }
        catch (Exception error) { ReportError(error); }
    }

    public void ReportInvalidInput() => Feedback = T["InvalidSettings"];
    public void ReportError(Exception error)
    {
        diagnostics.Write(error is SettingsValidationException or FormatException ? "settings.invalid" : "operation.failed");
        Feedback = T[error is SettingsValidationException or FormatException ? "InvalidSettings" : "OperationFailed"];
    }

    private AppSettings ReadEditor()
    {
        settings.Blink.IntervalMinutes = ParseNumber(BlinkInterval);
        settings.Blink.DurationSeconds = ParseNumber(BlinkDuration);
        settings.VisualBreak.IntervalMinutes = ParseNumber(BreakInterval);
        settings.VisualBreak.DurationSeconds = ParseNumber(BreakDuration);
        settings.Appearance.Margin = ParseNumber(BannerMargin);
        settings.Appearance.FontSize = ParseNumber(BannerFontSize);
        settings.IdleMinutes = ParseNumber(IdleThreshold);
        settings.Schedule.Start = QuietPeriodViewModel.ParseTime(StartTime);
        settings.Schedule.End = QuietPeriodViewModel.ParseTime(EndTime);
        settings.Schedule.Days = Days.Where(day => day.Selected).Select(day => day.Value).ToArray();
        settings.Schedule.QuietPeriods = QuietPeriods.Select(period => period.ToSettings()).ToList();
        return SettingsValidator.Clone(settings);
    }

    private void LoadEditor(AppSettings value)
    {
        settings = SettingsValidator.Clone(value);
        T.SetLanguage(settings.Language);
        BlinkInterval = settings.Blink.IntervalMinutes.ToString(CultureInfo.InvariantCulture); Notify(nameof(BlinkInterval));
        BlinkDuration = settings.Blink.DurationSeconds.ToString(CultureInfo.InvariantCulture); Notify(nameof(BlinkDuration));
        BreakInterval = settings.VisualBreak.IntervalMinutes.ToString(CultureInfo.InvariantCulture); Notify(nameof(BreakInterval));
        BreakDuration = settings.VisualBreak.DurationSeconds.ToString(CultureInfo.InvariantCulture); Notify(nameof(BreakDuration));
        BannerMargin = settings.Appearance.Margin.ToString(CultureInfo.InvariantCulture); Notify(nameof(BannerMargin));
        BannerFontSize = settings.Appearance.FontSize.ToString(CultureInfo.InvariantCulture); Notify(nameof(BannerFontSize));
        IdleThreshold = settings.IdleMinutes.ToString(CultureInfo.InvariantCulture); Notify(nameof(IdleThreshold));

        Days = new(Weekdays.Select(day => new DayChoice(day, settings.Schedule.Days.Contains(day))));
        QuietPeriods = new(settings.Schedule.QuietPeriods.Select(period => new QuietPeriodViewModel(period, RemoveQuiet)));
        StartTime = settings.Schedule.Start.ToString("HH:mm", CultureInfo.InvariantCulture);
        EndTime = settings.Schedule.End.ToString("HH:mm", CultureInfo.InvariantCulture);
        Notify(nameof(Settings)); Notify(nameof(Language)); Notify(nameof(Days)); Notify(nameof(QuietPeriods));
        Notify(nameof(StartTime)); Notify(nameof(EndTime)); RefreshChoices();
    }

    private static int ParseNumber(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
        ? value : throw new SettingsValidationException("Invalid number.");

    private void RemoveQuiet(QuietPeriodViewModel period) => QuietPeriods.Remove(period);
    private void RunFileAction(Action action) { try { action(); } catch (Exception error) { ReportError(error); } }
    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        var imported = store.Import(dialog.FileName);
        imported.StartWithWindows = false;
        LoadEditor(imported);
        Feedback = T["Imported"];
    }

    private void Export()
    {
        var candidate = ReadEditor();
        var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "BlinkReminder-settings.json" };
        if (dialog.ShowDialog() == true) { store.Export(dialog.FileName, candidate); Feedback = T["Done"]; }
    }

    private void ExportDiagnostics()
    {
        var dialog = new SaveFileDialog { Filter = "TXT (*.txt)|*.txt", FileName = "BlinkReminder-diagnostics.txt" };
        if (dialog.ShowDialog() == true) { diagnostics.Export(dialog.FileName); Feedback = T["Done"]; }
    }

    private async Task ResetAsync()
    {
        if (MessageBox.Show(T["ResetConfirm"], T["AppName"], MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await startup.RemoveCurrentInstallationAsync();
        store.Reset();
        var defaults = new AppSettings();
        statistics.SetEnabled(false);
        host.ApplySettings(defaults);
        LoadEditor(defaults);
        ThemeService.Apply(defaults.Appearance.Theme);
        Feedback = T["Done"];
        await RefreshStartupAsync();
    }

    private void OpenLink(string url) => RunFileAction(() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
}
