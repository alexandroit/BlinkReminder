using System.Windows;
using BlinkReminder.App.Services;
using BlinkReminder.App.ViewModels;
using BlinkReminder.App.Views;
using BlinkReminder.Core;
using BlinkReminder.Windows;
using Microsoft.Win32;

namespace BlinkReminder.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceCoordinator? instance;
    private ReminderHost? host;
    private NativeNotifications? notifications;
    private TrayService? tray;
    private MainViewModel? viewModel;
    private Views.MainWindow? settingsWindow;
    private LocalDiagnostics? diagnostics;
    private bool exiting;
    private bool pendingOpen;
    private AppTheme currentTheme;

    protected override async void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        try
        {
            if (ProcessSecurity.IsElevated())
            {
                if (!args.Args.Contains("--uninstall-integration", StringComparer.Ordinal))
                    MessageBox.Show(Localizer.Current["Elevated"], "BlinkReminder", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(1);
                return;
            }

            if (args.Args.Contains("--uninstall-integration", StringComparer.Ordinal))
            {
                if (!ApplicationPaths.HasPackageIdentity())
                {
                    await new WindowsStartupAdapter().RemoveCurrentInstallationAsync();
                    using var cleanup = new NativeNotifications();
                    cleanup.Register();
                    await cleanup.RemoveRegistrationAsync();
                }
                Shutdown();
                return;
            }

            bool notificationLaunch = ApplicationActivation.IsNotificationLaunch(args.Args);
            instance = SingleInstanceCoordinator.TryAcquire();
            if (!instance.IsPrimary)
            {
                bool delivered;
                if (notificationLaunch)
                {
                    using var activation = new NativeNotifications();
                    activation.Register();
                    var command = await activation.GetLaunchCommandAsync();
                    delivered = command is null || await instance.SendAsync(command.Value);
                }
                else delivered = await instance.SendAsync(InstanceCommand.OpenSettings);
                if (!delivered) MessageBox.Show(Localizer.Current["AlreadyRunning"], "BlinkReminder");
                await instance.DisposeAsync();
                instance = null;
                Shutdown();
                return;
            }
            instance.CommandReceived += InstanceCommandReceived;

            var paths = ApplicationPaths.Resolve();
            var store = new JsonSettingsStore(paths.SettingsDirectory);
            var loaded = store.Load();
            var settings = loaded.Settings;
            Localizer.Current.SetLanguage(settings.Language);
            currentTheme = settings.Appearance.Theme;
            ThemeService.Apply(currentTheme);
            diagnostics = new(paths.DiagnosticDirectory);
            var statistics = new LocalStatistics(paths.SettingsDirectory, settings.StatisticsEnabled);
            notifications = new NativeNotifications();
            notifications.ActivationRequested += InstanceCommandReceived;
            notifications.Failed += (_, _) => diagnostics.Write("notification.integration_failed");
            notifications.Register();
            var startup = new WindowsStartupAdapter();
            host = new ReminderHost(settings, notifications, statistics, diagnostics);
            viewModel = new MainViewModel(settings, store, host, statistics, diagnostics, startup, ExitApplication);
            settingsWindow = new Views.MainWindow(viewModel, () => tray?.Available == true);
            MainWindow = settingsWindow;
            tray = new TrayService(OpenSettings, Preview, host.Pause, host.PauseUntilTomorrow, host.Resume,
                host.Snooze, () => host.Dismiss(), ExitApplication);
            tray.Unavailable += (_, _) => { viewModel.Feedback = Localizer.Current["TrayUnavailable"]; OpenSettings(); };
            host.Changed += (_, _) => UpdateTray();
            Localizer.Current.PropertyChanged += LanguageChanged;
            SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
            SessionEnding += OnSessionEnding;
            await viewModel.RefreshStartupAsync();
            if (loaded.Warning is not null) viewModel.Feedback = Localizer.Current["SettingsRecovered"];
            if (!tray.Available) viewModel.Feedback = Localizer.Current["TrayUnavailable"];
            host.Start();
            UpdateTray();
            diagnostics.Write("app.started");
            if (notificationLaunch)
            {
                var launchCommand = await notifications.GetLaunchCommandAsync();
                if (launchCommand is null)
                {
                    await StopAsync();
                    Shutdown();
                    return;
                }
                InstanceCommandReceived(this, launchCommand.Value);
            }
            bool background = args.Args.Contains("--background", StringComparer.Ordinal)
                || (!notificationLaunch && ApplicationActivation.IsStartupLaunch());
            if (pendingOpen || !background || !settings.OnboardingCompleted || !tray.Available) OpenSettings();
        }
        catch (Exception)
        {
            diagnostics?.Write("app.start_failed");
            MessageBox.Show(Localizer.Current["OperationFailed"], "BlinkReminder", MessageBoxButton.OK, MessageBoxImage.Error);
            await StopAsync();
            Shutdown(1);
        }
    }

    private void OpenSettings()
    {
        if (exiting) return;
        if (settingsWindow is null) { pendingOpen = true; return; }
        pendingOpen = false;
        settingsWindow.BringToFront();
    }

    private async void Preview()
    {
        if (host is null || exiting) return;
        try { await host.PreviewAsync(); }
        catch (Exception error) { viewModel?.ReportError(error); }
    }

    private void InstanceCommandReceived(object? sender, InstanceCommand command) => Dispatcher.BeginInvoke(() =>
    {
        if (command == InstanceCommand.Preview) Preview();
        else if (command == InstanceCommand.OpenSettings) OpenSettings();
    });

    private void UpdateTray()
    {
        if (viewModel is null || host is null) return;
        bool paused = (host.Snapshot.Reasons & ~PauseReason.Presenting) != PauseReason.None;
        tray?.Update($"{viewModel.Status} · {viewModel.NextReminder}", paused);
        currentTheme = viewModel.Settings.Appearance.Theme;
    }

    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        tray?.RefreshLanguage();
        UpdateTray();
    }

    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs args) =>
        Dispatcher.BeginInvoke(() => ThemeService.Apply(currentTheme));

    private async void ExitApplication()
    {
        if (exiting) return;
        await StopAsync();
        Shutdown();
    }

    private async Task StopAsync()
    {
        if (exiting) return;
        exiting = true;
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        Localizer.Current.PropertyChanged -= LanguageChanged;
        SessionEnding -= OnSessionEnding;
        if (settingsWindow is not null) settingsWindow.IsExiting = true;
        if (host is not null) await host.DisposeAsync();
        tray?.Dispose();
        if (notifications is not null)
        {
            try { await notifications.ClearAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { diagnostics?.Write("notification.cleanup_timeout"); }
            notifications.Dispose();
        }
        if (instance is not null) await instance.DisposeAsync();
        diagnostics?.Write("app.stopped");
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs args)
    {
        // Do not hold up sign-out. Windows tears down the process and its per-session handles.
        exiting = true;
        if (settingsWindow is not null) settingsWindow.IsExiting = true;
        tray?.Dispose();
        notifications?.Dispose();
    }
}
