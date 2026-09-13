using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using BlinkReminder.App;
using BlinkReminder.App.Services;
using BlinkReminder.App.ViewModels;
using BlinkReminder.App.Views;
using BlinkReminder.Core;
using BlinkReminder.Windows;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BlinkReminder.App.Tests;

public sealed class WindowConstructionTests
{
    [Fact]
    public async Task RealWindowResourcesTabsAndDraftValidationWorkTogether()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await ExerciseWindowsAsync(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            try { Dispatcher.Run(); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "BlinkReminder WPF construction test" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static async Task ExerciseWindowsAsync()
    {
        // Exactly one WPF Application is constructed in this test process. Application.Run
        // and OnStartup are never invoked, so this does not start the product or bypass its guard.
        var application = new global::BlinkReminder.App.App();
        application.InitializeComponent();
        var bindingErrors = new BindingErrorListener();
        var source = PresentationTraceSources.DataBindingSource;
        var previousLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Warning;
        source.Listeners.Add(bindingErrors);
        string directory = Path.Combine(Path.GetTempPath(), "BlinkReminder.App.Tests", Guid.NewGuid().ToString("N"));
        var settings = new AppSettings();
        var store = new JsonSettingsStore(directory);
        var statistics = new LocalStatistics(directory, enabled: false);
        var diagnostics = new LocalDiagnostics(Path.Combine(directory, "Diagnostics"));
        using var notifications = new NativeNotifications(); // No Register or Show calls.
        var host = new ReminderHost(settings, notifications, statistics, diagnostics);
        MainWindow? window = null;
        BannerWindow? banner = null;
        try
        {
            ThemeService.Apply(AppTheme.Light);
            var viewModel = new MainViewModel(settings, store, host, statistics, diagnostics,
                new WindowsStartupAdapter(false, Path.Combine(directory, "BlinkReminder.exe")), () => { });
            window = new MainWindow(viewModel, () => false)
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                Opacity = 0
            };
            // A transparent test window enters WPF's real loaded/layout lifecycle. This is
            // a markup/binding test, not a screenshot, focus, keyboard or accessibility test.
            window.Show();
            await SettleLayoutAsync(window);
            Assert.True(window.IsLoaded);
            Assert.Equal("BlinkReminder", Localizer.Current["AppName"]);
            Assert.Equal(Localizer.Current["AppName"], window.Title);

            var tabs = VisualDescendants(window).OfType<TabControl>().Single();
            Assert.Equal(6, tabs.Items.Count);
            foreach (string language in new[] { "pt-BR", "en", "fr-CA" })
            {
                viewModel.Language = language;
                foreach (AppTheme theme in new[] { AppTheme.Light, AppTheme.Dark, AppTheme.System })
                {
                    ThemeService.Apply(theme);
                    foreach (string key in new[] { "WindowBrush", "SurfaceBrush", "TextBrush", "MutedBrush", "BorderBrush", "AccentBrush" })
                        Assert.IsAssignableFrom<Brush>(application.FindResource(key));
                    for (int index = 0; index < tabs.Items.Count; index++)
                    {
                        tabs.SelectedIndex = index;
                        await SettleLayoutAsync(window);
                        var tab = Assert.IsType<TabItem>(tabs.Items[index]);
                        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(tab.Header)));
                        var content = Assert.IsAssignableFrom<FrameworkElement>(tab.Content);
                        Assert.True(content.IsLoaded);
                        Assert.Same(viewModel, content.DataContext);
                    }
                }
            }

            var request = new ReminderRequest(Guid.NewGuid(), ReminderKind.Blink, "Construction test",
                TimeSpan.FromSeconds(5), false, PresentationMode.Banner);
            banner = new BannerWindow(request, settings.Appearance);
            Assert.False(banner.IsVisible);
            Assert.False(banner.ShowActivated);
            Assert.False(banner.ShowInTaskbar);
            Assert.IsAssignableFrom<Brush>(banner.FindResource("SurfaceBrush"));

            tabs.SelectedIndex = 1;
            await SettleLayoutAsync(window);
            var interval = Assert.IsType<TextBox>(window.FindName("Input1"));
            var binding = interval.GetBindingExpression(TextBox.TextProperty);
            Assert.NotNull(binding);
            Assert.Equal(nameof(MainViewModel.BlinkInterval), binding.ParentBinding.Path.Path);
            interval.Text = "not-a-number";
            binding.UpdateSource();
            Assert.Equal("not-a-number", viewModel.BlinkInterval);
            await viewModel.SaveAsync();
            Assert.Equal(Localizer.Current["InvalidSettings"], viewModel.Feedback);
            Assert.False(File.Exists(store.FilePath));
            Assert.False(viewModel.Settings.OnboardingCompleted);
            Assert.Equal(settings.Blink.IntervalMinutes, viewModel.Settings.Blink.IntervalMinutes);
            Assert.Empty(bindingErrors.Messages);
        }
        finally
        {
            if (banner is not null) banner.Close();
            if (window is not null) { window.IsExiting = true; window.Close(); }
            source.Listeners.Remove(bindingErrors);
            source.Switch.Level = previousLevel;
            bindingErrors.Dispose();
            await host.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            application.Shutdown();
        }
    }

    private static async Task SettleLayoutAsync(Window window)
    {
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
